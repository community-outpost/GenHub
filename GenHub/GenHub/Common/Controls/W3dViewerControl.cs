using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Services.Tools.ModelViewer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GenHub.Common.Controls;

/// <summary>
/// OpenGL viewer rendering textured W3D models with skeleton overlay,
/// orbit camera, and click-to-select sub-object picking.
/// </summary>
#pragma warning disable S1939 // OpenGlControlBase implements ICustomHitTest via explicit/non-virtual method; re-listing interface is required for proper Avalonia hit-test routing.
[SuppressMessage("Minor Code Smell", "S1939:Redundant interface implementations should be removed", Justification = "OpenGlControlBase implements ICustomHitTest via explicit/non-virtual method; re-listing interface is required for proper Avalonia hit-test routing.")]
[SuppressMessage("Major Code Smell", "S1939:Redundant interface implementations should be removed", Justification = "OpenGlControlBase implements ICustomHitTest via explicit/non-virtual method; re-listing interface is required for proper Avalonia hit-test routing.")]
public sealed class W3dViewerControl : OpenGlControlBase, ICustomHitTest
{
#pragma warning restore S1939
    /// <summary>
    /// The rendered scene.
    /// </summary>
    public static readonly StyledProperty<W3dRenderScene?> SceneProperty =
        AvaloniaProperty.Register<W3dViewerControl, W3dRenderScene?>(nameof(Scene));

    /// <summary>
    /// The selected scene mesh index, or -1 for none.
    /// </summary>
    public static readonly StyledProperty<int> SelectedMeshIndexProperty =
        AvaloniaProperty.Register<W3dViewerControl, int>(
            nameof(SelectedMeshIndex),
            defaultValue: -1,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Whether the skeleton overlay renders.
    /// </summary>
    public static readonly StyledProperty<bool> ShowSkeletonProperty =
        AvaloniaProperty.Register<W3dViewerControl, bool>(nameof(ShowSkeleton), defaultValue: true);

    /// <summary>
    /// Whether meshes render as wireframe edges.
    /// </summary>
    public static readonly StyledProperty<bool> ShowWireframeProperty =
        AvaloniaProperty.Register<W3dViewerControl, bool>(nameof(ShowWireframe), defaultValue: false);

    /// <summary>
    /// Per-pivot world transforms for animation poses, or null for the bind pose.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Matrix4x4>?> PoseProperty =
        AvaloniaProperty.Register<W3dViewerControl, IReadOnlyList<Matrix4x4>?>(nameof(Pose));

    /// <summary>
    /// Per-pivot bind-pose world transforms used to relativize animation poses,
    /// or null when the bind pose is unavailable.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Matrix4x4>?> BindPoseProperty =
        AvaloniaProperty.Register<W3dViewerControl, IReadOnlyList<Matrix4x4>?>(nameof(BindPose));

    /// <summary>
    /// Whether the camera target follows the animated root pivot.
    /// </summary>
    public static readonly StyledProperty<bool> TrackTargetProperty =
        AvaloniaProperty.Register<W3dViewerControl, bool>(nameof(TrackTarget), defaultValue: false);

    /// <summary>
    /// Mesh names hidden by the active draw state, or null when all meshes show.
    /// </summary>
    public static readonly StyledProperty<IReadOnlySet<string>?> HiddenMeshNamesProperty =
        AvaloniaProperty.Register<W3dViewerControl, IReadOnlySet<string>?>(nameof(HiddenMeshNames));

    /// <summary>
    /// Whether the surrounding canvas is in pan mode, in which case left-drag pans the canvas.
    /// </summary>
    public static readonly StyledProperty<bool> IsCanvasPanModeProperty =
        AvaloniaProperty.Register<W3dViewerControl, bool>(nameof(IsCanvasPanMode), defaultValue: false);

    /// <summary>
    /// The last GL failure message, or null when healthy.
    /// </summary>
    public static readonly DirectProperty<W3dViewerControl, string?> GlErrorProperty =
        AvaloniaProperty.RegisterDirect<W3dViewerControl, string?>(
            nameof(GlError),
            control => control.GlError,
            (control, value) => control.GlError = value);

    private const int GlLines = 0x0001;
    private const int GlPoints = 0x0000;
    private const int GlUnsignedInt = 0x1405;
    private const int GlLessEqual = 0x0203;
    private const double HoverThrottleMs = 30;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlClampToEdge = 0x812F;
    private const int PositionAttribute = 0;
    private const int NormalAttribute = 1;
    private const int UvAttribute = 2;
    private const int ColorAttribute = 3;
    private const int VertexFloats = 12;
    private const double DragThresholdPixels = 4.0;
    private const double OrbitSpeed = 0.008;
    private const double ZoomStep = 1.12;
    private const double KeyboardPanFraction = 10.0;
    private const double KeyboardZoomStep = 1.2;
    private const float FieldOfViewRadians = 0.7853982f;

    private const string MeshVertexShader = """
        attribute vec3 aPos;
        attribute vec3 aNormal;
        attribute vec2 aUv;
        attribute vec4 aColor;
        uniform mat4 uMvp;
        uniform mat4 uModel;
        varying vec2 vUv;
        varying vec4 vColor;
        varying float vLight;
        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            vUv = aUv;
            vColor = aColor;
            vec3 normal = normalize(mat3(uModel) * aNormal);
            vec3 lightDir = normalize(vec3(0.5, -0.6, 0.8));
            vLight = 0.38 + 0.62 * max(dot(normal, lightDir), 0.0);
        }
        """;

    private const string MeshFragmentShader = """
        #ifdef GL_ES
        precision mediump float;
        #endif
        uniform sampler2D uTexture;
        uniform float uHasTexture;
        uniform float uAlphaTest;
        uniform float uOpacity;
        uniform float uSelected;
        uniform float uHovered;
        varying vec2 vUv;
        varying vec4 vColor;
        varying float vLight;
        void main()
        {
            vec4 texel = vec4(1.0);
            if (uHasTexture > 0.5)
            {
                texel = texture2D(uTexture, vUv);
            }

            vec4 base = vColor * texel;
            if (uAlphaTest > 0.5 && base.a < 0.5)
            {
                discard;
            }

            if (uOpacity < 0.999)
            {
                float hash = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
                if (hash > uOpacity)
                {
                    discard;
                }
            }

            vec3 rgb = base.rgb * vLight;
            if (uSelected > 0.5)
            {
                rgb = mix(rgb, vec3(1.0, 0.75, 0.25), 0.45);
            }
            else if (uHovered > 0.5)
            {
                rgb = mix(rgb, vec3(1.0, 1.0, 1.0), 0.25);
            }

            gl_FragColor = vec4(rgb, base.a * uOpacity);
        }
        """;

    private const string LineVertexShader = """
        attribute vec3 aPos;
        attribute vec4 aColor;
        uniform mat4 uMvp;
        varying vec4 vColor;
        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            vColor = aColor;
        }
        """;

    private const string LineFragmentShader = """
        #ifdef GL_ES
        precision mediump float;
        #endif
        varying vec4 vColor;
        void main()
        {
            gl_FragColor = vColor;
        }
        """;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly object _cameraLock = new();
    private readonly List<W3dMeshBuffers> _meshBuffers = [];
    private readonly List<int> _textures = [];
    private Matrix4x4[] _inverseBind = [];

    private string? _glError;
    private bool _glReady;
    private bool _glFailed;
    private bool _sceneDirty = true;
    private bool _linesDirty = true;

    private int _meshProgram;
    private int _lineProgram;
    private int _lineBuffer;
    private int _lineVertexCount;

    private int _meshMvpLocation = -1;
    private int _meshModelLocation = -1;
    private int _meshHasTextureLocation = -1;
    private int _meshAlphaTestLocation = -1;
    private int _meshOpacityLocation = -1;
    private int _meshSelectedLocation = -1;
    private int _meshHoveredLocation = -1;
    private int _lineMvpLocation = -1;

    private double _yaw = 0.7;
    private double _pitch = 0.45;
    private double _distance = 10;
    private Vector3 _target;
    private double _minDistance = 0.5;
    private double _maxDistance = 200;

    private int _hoveredMeshIndex = -1;
    private long _lastHoverTimestamp;
    private Point _lastPointerPosition;
    private Point _pressPosition;
    private bool _pressing;
    private bool _dragging;
    private bool _panning;
    private int _rootPivotIndex;
    private Vector3 _lastRootTranslation;
    private bool _hasLastRootTranslation;

    /// <summary>
    /// Initializes a new instance of the <see cref="W3dViewerControl"/> class.
    /// </summary>
    public W3dViewerControl()
    {
        Focusable = true;
        DoubleTapped += (_, _) => ResetView();
    }

    /// <summary>
    /// Gets or sets the rendered scene.
    /// </summary>
    public W3dRenderScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected scene mesh index, or -1 for none.
    /// </summary>
    public int SelectedMeshIndex
    {
        get => GetValue(SelectedMeshIndexProperty);
        set => SetValue(SelectedMeshIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the skeleton overlay renders.
    /// </summary>
    public bool ShowSkeleton
    {
        get => GetValue(ShowSkeletonProperty);
        set => SetValue(ShowSkeletonProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether meshes render as wireframe edges.
    /// </summary>
    public bool ShowWireframe
    {
        get => GetValue(ShowWireframeProperty);
        set => SetValue(ShowWireframeProperty, value);
    }

    /// <summary>
    /// Gets or sets the per-pivot world transforms for animation poses, or null for the bind pose.
    /// </summary>
    public IReadOnlyList<Matrix4x4>? Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    /// <summary>
    /// Gets or sets the per-pivot bind-pose world transforms, or null when unavailable.
    /// </summary>
    public IReadOnlyList<Matrix4x4>? BindPose
    {
        get => GetValue(BindPoseProperty);
        set => SetValue(BindPoseProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the camera target follows the animated root pivot.
    /// </summary>
    public bool TrackTarget
    {
        get => GetValue(TrackTargetProperty);
        set => SetValue(TrackTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the mesh names hidden by the active draw state, or null when all meshes show.
    /// </summary>
    public IReadOnlySet<string>? HiddenMeshNames
    {
        get => GetValue(HiddenMeshNamesProperty);
        set => SetValue(HiddenMeshNamesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the surrounding canvas is in pan mode.
    /// </summary>
    public bool IsCanvasPanMode
    {
        get => GetValue(IsCanvasPanModeProperty);
        set => SetValue(IsCanvasPanModeProperty, value);
    }

    /// <summary>
    /// Gets the last GL failure message, or null when healthy.
    /// </summary>
    public string? GlError
    {
        get => _glError;
        private set => SetAndRaise(GlErrorProperty, ref _glError, value);
    }

    /// <summary>
    /// Gets the current orbit yaw in radians.
    /// </summary>
    public double CameraYaw
    {
        get
        {
            lock (_cameraLock)
            {
                return _yaw;
            }
        }
    }

    /// <summary>
    /// Gets the current orbit pitch in radians.
    /// </summary>
    public double CameraPitch
    {
        get
        {
            lock (_cameraLock)
            {
                return _pitch;
            }
        }
    }

    /// <summary>
    /// Gets the current camera distance from the target.
    /// </summary>
    public double CameraDistance
    {
        get
        {
            lock (_cameraLock)
            {
                return _distance;
            }
        }
    }

    /// <summary>
    /// Gets the current camera orbit target.
    /// </summary>
    public Vector3 CameraTarget
    {
        get
        {
            lock (_cameraLock)
            {
                return _target;
            }
        }
    }

    /// <summary>
    /// Hit-tests the viewer bounds so camera gestures reach the control.
    /// OpenGL surfaces never paint render geometry, so without this every
    /// press falls through to the document canvas behind the viewer.
    /// </summary>
    /// <param name="point">The point in the viewer local coordinate space.</param>
    /// <returns>True when the point falls inside the viewer bounds.</returns>
    bool ICustomHitTest.HitTest(Point point)
    {
        return new Rect(Bounds.Size).Contains(point);
    }

    /// <summary>
    /// Frames the current scene with a default orbit angle.
    /// </summary>
    public void ResetView()
    {
        var scene = Scene;
        if (scene == null)
        {
            return;
        }

        lock (_cameraLock)
        {
            FrameScene(scene);
        }

        RequestNextFrameRendering();
    }

    /// <summary>
    /// Moves the camera target to frame the given mesh, keeping the current orbit angles.
    /// </summary>
    /// <param name="meshIndex">The scene mesh index to frame.</param>
    public void FocusMesh(int meshIndex)
    {
        var scene = Scene;
        if (scene == null || meshIndex < 0 || meshIndex >= scene.Meshes.Count)
        {
            return;
        }

        var models = CurrentMeshModels(scene);
        if (meshIndex >= models.Length)
        {
            return;
        }

        var mesh = scene.Meshes[meshIndex];
        var model = models[meshIndex];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        int count = 0;
        for (int i = 0; i + 2 < mesh.Vertices.Count; i += W3dRenderMesh.Stride)
        {
            var world = Vector3.Transform(new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]), model);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
            count++;
        }

        if (count == 0)
        {
            return;
        }

        var center = (min + max) / 2;
        float radius = Math.Max((max - min).Length() / 2, 0.5f);
        lock (_cameraLock)
        {
            _target = center;
            _distance = Math.Clamp(radius * 3.2, _minDistance, _maxDistance);
        }

        RequestNextFrameRendering();
    }

    /// <summary>
    /// Computes the model matrix for a mesh given an optional pose and bind pose.
    /// </summary>
    /// <param name="mesh">The mesh whose transform is being computed.</param>
    /// <param name="pose">The active animation pose, if any.</param>
    /// <param name="bindPose">The rest bind pose of the skeleton, if any.</param>
    /// <param name="inverseBind">The inverse bind pose matrices for skin meshes, if any.</param>
    /// <returns>The computed 4x4 model matrix.</returns>
    internal static Matrix4x4 ComputeMeshModelTransform(
        W3dRenderMesh mesh,
        IReadOnlyList<Matrix4x4>? pose,
        IReadOnlyList<Matrix4x4>? bindPose,
        IReadOnlyList<Matrix4x4>? inverseBind)
    {
        Matrix4x4 inner;
        if (pose == null || mesh.BoneIndex < 0 || mesh.BoneIndex >= pose.Count)
        {
            inner = !mesh.IsSkin && bindPose != null && mesh.BoneIndex >= 0 && mesh.BoneIndex < bindPose.Count
                ? bindPose[mesh.BoneIndex]
                : Matrix4x4.Identity;
        }
        else if (mesh.IsSkin)
        {
            // Deformable skin mesh vertices sit in bind-pose world space, so animation applies
            // the pivot motion relative to the bind pose instead of the absolute pose.
            // Without a matching inverse-bind matrix the absolute pose would
            // double-transform bind-space vertices, so render the bind shape.
            inner = inverseBind != null && mesh.BoneIndex < inverseBind.Count
                ? inverseBind[mesh.BoneIndex] * pose[mesh.BoneIndex]
                : Matrix4x4.Identity;
        }
        else
        {
            // Rigid meshes (turrets, wheels, chassis) sit in object space relative to their pivot
            // and must use the plain animated pivot transform.
            inner = pose[mesh.BoneIndex];
        }

        // Composed multi-model scenes shift each part along X in world space.
        // The bone transform runs first (pivot space into model space) and the
        // slot shift applies after it; reversing the order would rotate the
        // offset by animated pivots and fling parts across the scene.
        return mesh.LayoutOffset == Vector3.Zero
            ? inner
            : inner * Matrix4x4.CreateTranslation(mesh.LayoutOffset);
    }

    /// <summary>
    /// Determines whether a pointer press belongs to the surrounding canvas.
    /// Middle-drag always pans the canvas, and left-drag pans the canvas while
    /// pan mode is set, so the viewer must not capture those gestures.
    /// </summary>
    /// <param name="isCanvasPanMode">Whether the surrounding canvas is in pan mode.</param>
    /// <param name="isMiddlePressed">Whether the middle button is pressed.</param>
    /// <param name="isLeftPressed">Whether the left button is pressed.</param>
    /// <returns>True when the press must bubble to the canvas; otherwise, false.</returns>
    internal static bool ShouldYieldPressToCanvas(bool isCanvasPanMode, bool isMiddlePressed, bool isLeftPressed) =>
        isMiddlePressed || (isCanvasPanMode && isLeftPressed);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SceneProperty)
        {
            _sceneDirty = true;
            _linesDirty = true;
            var scene = Scene;
            if (scene != null)
            {
                _rootPivotIndex = FindRootPivot(scene);
                lock (_cameraLock)
                {
                    FrameScene(scene);
                }
            }

            SelectedMeshIndex = -1;
            SetHover(-1, null);
            _hasLastRootTranslation = false;
            RequestNextFrameRendering();
        }
        else if (change.Property == PoseProperty)
        {
            _linesDirty = true;
            TrackAnimatedRoot();
            RequestNextFrameRendering();
        }
        else if (change.Property == BindPoseProperty)
        {
            RebuildInverseBind();
            _linesDirty = true;
            RequestNextFrameRendering();
        }
        else if (change.Property == TrackTargetProperty)
        {
            _hasLastRootTranslation = false;
        }
        else if (change.Property == SelectedMeshIndexProperty ||
                 change.Property == ShowSkeletonProperty ||
                 change.Property == ShowWireframeProperty ||
                 change.Property == HiddenMeshNamesProperty)
        {
            RequestNextFrameRendering();
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlInit(GlInterface gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        try
        {
            _meshProgram = CreateProgram(
                gl,
                MeshVertexShader,
                MeshFragmentShader,
                [("aPos", PositionAttribute), ("aNormal", NormalAttribute), ("aUv", UvAttribute), ("aColor", ColorAttribute)]);
            _lineProgram = CreateProgram(
                gl,
                LineVertexShader,
                LineFragmentShader,
                [("aPos", PositionAttribute), ("aColor", 1)]);
            CacheUniformLocations(gl);
            _lineBuffer = gl.GenBuffer();
            _glReady = true;
            _sceneDirty = true;
            _linesDirty = true;
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _glReady = false;
        _glFailed = false;
        try
        {
            foreach (var buffers in _meshBuffers)
            {
                gl.DeleteBuffer(buffers.VertexBuffer);
                gl.DeleteBuffer(buffers.IndexBuffer);
                gl.DeleteBuffer(buffers.EdgeBuffer);
            }

            foreach (var texture in _textures)
            {
                gl.DeleteTexture(texture);
            }

            if (_lineBuffer != 0)
            {
                gl.DeleteBuffer(_lineBuffer);
            }

            if (_meshProgram != 0)
            {
                gl.DeleteProgram(_meshProgram);
            }

            if (_lineProgram != 0)
            {
                gl.DeleteProgram(_lineProgram);
            }
        }
        catch (InvalidOperationException)
        {
            // Context teardown must never throw.
        }
        finally
        {
            _meshBuffers.Clear();
            _textures.Clear();
            _meshProgram = 0;
            _lineProgram = 0;
            _lineBuffer = 0;
            _lineVertexCount = 0;
            _sceneDirty = true;
            _linesDirty = true;
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (!_glReady || _glFailed)
        {
            return;
        }

        try
        {
            var scene = Scene;
            RenderScene(gl, fb, scene);
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (ShouldYieldPressToCanvas(IsCanvasPanMode, point.Properties.IsMiddleButtonPressed, point.Properties.IsLeftButtonPressed))
        {
            return;
        }

        Focus();
        _pressing = point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed;
        _panning = point.Properties.IsRightButtonPressed ||
            (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _dragging = false;
        _pressPosition = e.GetPosition(this);
        _lastPointerPosition = _pressPosition;
        if (_pressing)
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var delta = position - _lastPointerPosition;
        _lastPointerPosition = position;

        if (_pressing && !_dragging &&
            (Math.Abs(position.X - _pressPosition.X) > DragThresholdPixels ||
             Math.Abs(position.Y - _pressPosition.Y) > DragThresholdPixels))
        {
            _dragging = true;
        }

        if (_pressing && _dragging)
        {
            lock (_cameraLock)
            {
                if (_panning)
                {
                    PanByPixels(delta.X, delta.Y);
                }
                else
                {
                    _yaw -= delta.X * OrbitSpeed;
                    _pitch = Math.Clamp(_pitch + (delta.Y * OrbitSpeed), -1.45, 1.45);
                }
            }

            RequestNextFrameRendering();
            return;
        }

        if (!_pressing)
        {
            UpdateHover(position);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool wasClick = _pressing && !_dragging && !_panning;
        _pressing = false;
        _panning = false;
        _dragging = false;
        e.Pointer.Capture(null);

        if (wasClick)
        {
            PickAt(e.GetPosition(this));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+wheel belongs to the surrounding document canvas zoom.
            return;
        }

        e.Handled = true;
        if (Math.Abs(e.Delta.Y) > double.Epsilon)
        {
            ZoomBySteps(e.Delta.Y);
        }

        RequestNextFrameRendering();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (HandleNavigationKey(e.Key))
        {
            e.Handled = true;
            RequestNextFrameRendering();
        }
    }

    private static int CreateProgram(GlInterface gl, string vertexSource, string fragmentSource, (string Name, int Index)[] attributes)
    {
        int vertexShader = gl.CreateShader(GlConsts.GL_VERTEX_SHADER);
        string? vertexError = gl.CompileShaderAndGetError(vertexShader, vertexSource);
        if (!string.IsNullOrEmpty(vertexError))
        {
            throw new InvalidOperationException($"Vertex shader failed: {vertexError}");
        }

        int fragmentShader = gl.CreateShader(GlConsts.GL_FRAGMENT_SHADER);
        string? fragmentError = gl.CompileShaderAndGetError(fragmentShader, fragmentSource);
        if (!string.IsNullOrEmpty(fragmentError))
        {
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
            throw new InvalidOperationException($"Fragment shader failed: {fragmentError}");
        }

        int program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        foreach (var attribute in attributes)
        {
            gl.BindAttribLocationString(program, attribute.Index, attribute.Name);
        }

        gl.LinkProgram(program);
        string? linkError = gl.LinkProgramAndGetError(program);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        if (!string.IsNullOrEmpty(linkError))
        {
            gl.DeleteProgram(program);
            throw new InvalidOperationException($"Program link failed: {linkError}");
        }

        return program;
    }

    private static void UploadFloats(GlInterface gl, int target, float[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            gl.BufferData(target, (IntPtr)(data.Length * sizeof(float)), handle.AddrOfPinnedObject(), GlConsts.GL_STATIC_DRAW);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void UploadUInts(GlInterface gl, int target, uint[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            gl.BufferData(target, (IntPtr)(data.Length * sizeof(uint)), handle.AddrOfPinnedObject(), GlConsts.GL_STATIC_DRAW);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void UploadTexture(GlInterface gl, int texture, W3dRenderTexture image)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.PixelData.Length < image.Width * image.Height * 4)
        {
            return;
        }

        gl.BindTexture(GlConsts.GL_TEXTURE_2D, texture);
        var handle = GCHandle.Alloc(image.PixelData, GCHandleType.Pinned);
        try
        {
            gl.TexImage2D(
                GlConsts.GL_TEXTURE_2D,
                0,
                GlConsts.GL_RGBA,
                image.Width,
                image.Height,
                0,
                GlConsts.GL_RGBA,
                GlConsts.GL_UNSIGNED_BYTE,
                handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }

        gl.TexParameteri(GlConsts.GL_TEXTURE_2D, GlConsts.GL_TEXTURE_MIN_FILTER, GlConsts.GL_LINEAR);
        gl.TexParameteri(GlConsts.GL_TEXTURE_2D, GlConsts.GL_TEXTURE_MAG_FILTER, GlConsts.GL_LINEAR);
        gl.TexParameteri(GlConsts.GL_TEXTURE_2D, GlTextureWrapS, GlClampToEdge);
        gl.TexParameteri(GlConsts.GL_TEXTURE_2D, GlTextureWrapT, GlClampToEdge);
    }

    [SuppressMessage("Security", "S6640:Avoid using unsafe code blocks", Justification = "UniformMatrix4fv requires native float pointer for OpenGL interop.")]
    private static unsafe void SetMatrix(GlInterface gl, int location, Matrix4x4 matrix)
    {
        if (location < 0)
        {
            return;
        }

        float* values = stackalloc float[16];
        values[0] = matrix.M11;
        values[1] = matrix.M12;
        values[2] = matrix.M13;
        values[3] = matrix.M14;
        values[4] = matrix.M21;
        values[5] = matrix.M22;
        values[6] = matrix.M23;
        values[7] = matrix.M24;
        values[8] = matrix.M31;
        values[9] = matrix.M32;
        values[10] = matrix.M33;
        values[11] = matrix.M34;
        values[12] = matrix.M41;
        values[13] = matrix.M42;
        values[14] = matrix.M43;
        values[15] = matrix.M44;
        gl.UniformMatrix4fv(location, 1, false, values);
    }

    private static uint[] BuildEdges(IReadOnlyList<uint> indices)
    {
        var edges = new uint[(indices.Count / 3) * 6];
        int edge = 0;
        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            uint a = indices[t];
            uint b = indices[t + 1];
            uint c = indices[t + 2];
            edges[edge] = a;
            edges[edge + 1] = b;
            edges[edge + 2] = b;
            edges[edge + 3] = c;
            edges[edge + 4] = c;
            edges[edge + 5] = a;
            edge += 6;
        }

        return edges;
    }

    private static int FindRootPivot(W3dRenderScene scene)
    {
        foreach (var segment in scene.Skeleton)
        {
            if (segment.ParentIndex < 0)
            {
                return segment.PivotIndex;
            }
        }

        return 0;
    }

    private static Vector3 SkeletonEndPoint(W3dSkeletonSegment segment, bool parentEnd, IReadOnlyList<Matrix4x4>? pose)
    {
        int pivotIndex = parentEnd ? segment.ParentIndex : segment.PivotIndex;
        if (pose != null && pivotIndex >= 0 && pivotIndex < pose.Count)
        {
            return pose[pivotIndex].Translation;
        }

        if (parentEnd && segment.ParentIndex < 0 && pose != null && segment.PivotIndex >= 0 && segment.PivotIndex < pose.Count)
        {
            return pose[segment.PivotIndex].Translation;
        }

        var stored = parentEnd ? segment.Start : segment.End;
        return new Vector3(stored.X, stored.Y, stored.Z);
    }

    private static void AddLineVertex(List<float> data, Vector3 point, float r, float g, float b, float a)
    {
        data.Add(point.X);
        data.Add(point.Y);
        data.Add(point.Z);
        data.Add(r);
        data.Add(g);
        data.Add(b);
        data.Add(a);
    }

    private static float GridStep(float extent)
    {
        float raw = extent / 10;
        float magnitude = (float)Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 0.001f))));
        float normalized = raw / magnitude;
        if (normalized >= 5)
        {
            return 5 * magnitude;
        }

        if (normalized >= 2)
        {
            return 2 * magnitude;
        }

        return magnitude;
    }

    private static void AddGridLines(List<float> data, W3dRenderScene scene)
    {
        float extent = Math.Max(scene.Bounds.SphereRadius * 2, 4);
        float step = GridStep(extent);
        float z = scene.Bounds.Min.Z - (extent * 0.02f);
        float half = (float)Math.Floor(extent / step) * step;
        const float Line = 0.22f;

        for (float line = -half; line <= half + (step / 2); line += step)
        {
            bool axis = Math.Abs(line) < step / 2;
            float shade = axis ? Line + 0.12f : Line;
            AddLineVertex(data, new Vector3(line, -half, z), shade, shade, shade + 0.03f, 1f);
            AddLineVertex(data, new Vector3(line, half, z), shade, shade, shade + 0.03f, 1f);
            AddLineVertex(data, new Vector3(-half, line, z), shade, shade, shade + 0.03f, 1f);
            AddLineVertex(data, new Vector3(half, line, z), shade, shade, shade + 0.03f, 1f);
        }
    }

    private void Fail(string message)
    {
        _glFailed = true;
        if (Dispatcher.UIThread.CheckAccess())
        {
            GlError = message;
        }
        else
        {
            Dispatcher.UIThread.Post(() => GlError = message);
        }
    }

    private Matrix4x4 MeshModel(W3dRenderMesh mesh, IReadOnlyList<Matrix4x4>? pose)
    {
        return ComputeMeshModelTransform(mesh, pose, BindPose, _inverseBind);
    }

    private void CacheUniformLocations(GlInterface gl)
    {
        _meshMvpLocation = gl.GetUniformLocationString(_meshProgram, "uMvp");
        _meshModelLocation = gl.GetUniformLocationString(_meshProgram, "uModel");
        _meshHasTextureLocation = gl.GetUniformLocationString(_meshProgram, "uHasTexture");
        _meshAlphaTestLocation = gl.GetUniformLocationString(_meshProgram, "uAlphaTest");
        _meshOpacityLocation = gl.GetUniformLocationString(_meshProgram, "uOpacity");
        _meshSelectedLocation = gl.GetUniformLocationString(_meshProgram, "uSelected");
        _meshHoveredLocation = gl.GetUniformLocationString(_meshProgram, "uHovered");
        _lineMvpLocation = gl.GetUniformLocationString(_lineProgram, "uMvp");
    }

    private void FrameScene(W3dRenderScene scene)
    {
        var bounds = scene.Bounds;
        _target = new Vector3(bounds.SphereCenter.X, bounds.SphereCenter.Y, bounds.SphereCenter.Z);
        float radius = Math.Max(bounds.SphereRadius, 0.5f);
        _distance = radius * 3.2;
        _minDistance = Math.Max(radius / 200, 0.01);
        _maxDistance = radius * 60;
        _yaw = 0.7;
        _pitch = 0.45;
    }

    private void ZoomBySteps(double steps)
    {
        lock (_cameraLock)
        {
            _distance = Math.Clamp(_distance / Math.Pow(ZoomStep, steps), _minDistance, _maxDistance);
        }
    }

    private void RebuildInverseBind()
    {
        var bind = BindPose;
        if (bind == null || bind.Count == 0)
        {
            _inverseBind = [];
            return;
        }

        var inverses = new Matrix4x4[bind.Count];
        for (int i = 0; i < bind.Count; i++)
        {
            inverses[i] = Matrix4x4.Invert(bind[i], out var inverted) ? inverted : Matrix4x4.Identity;
        }

        _inverseBind = inverses;
    }

    private void TrackAnimatedRoot()
    {
        var pose = Pose;
        if (!TrackTarget || pose == null || _rootPivotIndex < 0 || _rootPivotIndex >= pose.Count)
        {
            _hasLastRootTranslation = false;
            return;
        }

        var root = pose[_rootPivotIndex].Translation;
        if (_hasLastRootTranslation)
        {
            lock (_cameraLock)
            {
                _target += root - _lastRootTranslation;
            }
        }

        _lastRootTranslation = root;
        _hasLastRootTranslation = true;
    }

    private bool HandleNavigationKey(Key key)
    {
        double panPixels = Math.Max(Bounds.Height, 1) / KeyboardPanFraction;
        switch (key)
        {
            case Key.Left:
            case Key.A:
                PanByPixels(-panPixels, 0);
                return true;
            case Key.Right:
            case Key.D:
                PanByPixels(panPixels, 0);
                return true;
            case Key.Up:
            case Key.W:
                PanByPixels(0, -panPixels);
                return true;
            case Key.Down:
            case Key.S:
                PanByPixels(0, panPixels);
                return true;
            case Key.Add:
            case Key.OemPlus:
                ZoomBySteps(1);
                return true;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomBySteps(-1);
                return true;
            case Key.Home:
                ResetView();
                return true;
            default:
                return false;
        }
    }

    private bool IsMeshHidden(string meshName)
    {
        if (HiddenMeshNames == null)
        {
            return false;
        }

        if (HiddenMeshNames.Contains(meshName))
        {
            return true;
        }

        // Draw states name sub-objects without the model prefix, e.g. TREADSL01 for AVLEOPARD.TREADSL01.
        int dot = meshName.LastIndexOf('.');
        return dot >= 0 && HiddenMeshNames.Contains(meshName[(dot + 1)..]);
    }

    private void PanByPixels(double dx, double dy)
    {
        lock (_cameraLock)
        {
            var (right, up) = CameraAxes();
            float scale = (float)(_distance / Math.Max(Bounds.Height, 1));
            _target -= right * (float)(dx * scale);
            _target += up * (float)(dy * scale);
        }
    }

    private (Vector3 Right, Vector3 Up) CameraAxes()
    {
        var eye = CameraEye();
        var forward = Vector3.Normalize(_target - eye);
        var worldUp = Math.Abs(forward.Z) > 0.95f ? new Vector3(0, 1, 0) : new Vector3(0, 0, 1);
        var right = Vector3.Normalize(Vector3.Cross(forward, worldUp));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        return (right, up);
    }

    private Vector3 CameraEye()
    {
        float cosPitch = (float)Math.Cos(_pitch);
        var offset = new Vector3(
            (float)(Math.Cos(_yaw) * cosPitch),
            (float)(Math.Sin(_yaw) * cosPitch),
            (float)Math.Sin(_pitch)) * (float)_distance;
        return _target + offset;
    }

    private void RenderScene(GlInterface gl, int fb, W3dRenderScene? scene)
    {
        int width = Math.Max((int)Bounds.Width, 1);
        int height = Math.Max((int)Bounds.Height, 1);
        gl.BindFramebuffer(GlConsts.GL_FRAMEBUFFER, fb);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0.07f, 0.08f, 0.1f, 1f);
        gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT | GlConsts.GL_DEPTH_BUFFER_BIT);
        gl.Enable(GlConsts.GL_DEPTH_TEST);
        gl.DepthFunc(GlLessEqual);
        gl.DepthMask(1);

        if (scene == null)
        {
            return;
        }

        if (_sceneDirty)
        {
            UploadScene(gl, scene);
            _sceneDirty = false;
        }

        if (_linesDirty)
        {
            UploadLines(gl, scene);
            _linesDirty = false;
        }

        Matrix4x4 view = Matrix4x4.Identity;
        Matrix4x4 projection = Matrix4x4.Identity;
        lock (_cameraLock)
        {
            var eye = CameraEye();
            view = Matrix4x4.CreateLookAt(eye, _target, new Vector3(0, 0, 1));
            float aspect = width / (float)height;
            float near = (float)Math.Max(_distance / 100, 0.01);
            float far = (float)((_distance * 20) + 100);
            projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewRadians, aspect, near, far);
        }

        var pose = Pose;
        bool wireframe = ShowWireframe;
        int selected = SelectedMeshIndex;
        int hovered = _hoveredMeshIndex;
        DrawMeshes(gl, scene, new MeshRenderContext(view, projection, pose, wireframe, selected, hovered));

        if (ShowSkeleton)
        {
            DrawLines(gl, view, projection);
        }
    }

    private void UploadScene(GlInterface gl, W3dRenderScene scene)
    {
        foreach (var buffers in _meshBuffers)
        {
            gl.DeleteBuffer(buffers.VertexBuffer);
            gl.DeleteBuffer(buffers.IndexBuffer);
            gl.DeleteBuffer(buffers.EdgeBuffer);
        }

        foreach (var texture in _textures)
        {
            gl.DeleteTexture(texture);
        }

        _meshBuffers.Clear();
        _textures.Clear();

        foreach (var image in scene.Textures)
        {
            int texture = gl.GenTexture();
            UploadTexture(gl, texture, image);
            _textures.Add(texture);
        }

        foreach (var mesh in scene.Meshes)
        {
            int vertexBuffer = gl.GenBuffer();
            gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, vertexBuffer);
            UploadFloats(gl, GlConsts.GL_ARRAY_BUFFER, [.. mesh.Vertices]);

            int indexBuffer = gl.GenBuffer();
            gl.BindBuffer(GlConsts.GL_ELEMENT_ARRAY_BUFFER, indexBuffer);
            UploadUInts(gl, GlConsts.GL_ELEMENT_ARRAY_BUFFER, [.. mesh.Indices]);

            uint[] edges = BuildEdges(mesh.Indices);
            int edgeBuffer = gl.GenBuffer();
            gl.BindBuffer(GlConsts.GL_ELEMENT_ARRAY_BUFFER, edgeBuffer);
            UploadUInts(gl, GlConsts.GL_ELEMENT_ARRAY_BUFFER, edges);

            _meshBuffers.Add(new W3dMeshBuffers(vertexBuffer, indexBuffer, edgeBuffer, mesh.Indices.Count, edges.Length));
        }
    }

    private void UploadLines(GlInterface gl, W3dRenderScene scene)
    {
        var data = new List<float>();
        var pose = Pose;
        foreach (var segment in scene.Skeleton)
        {
            var start = SkeletonEndPoint(segment, true, pose);
            var end = SkeletonEndPoint(segment, false, pose);
            AddLineVertex(data, start, 1f, 0.75f, 0.25f, 1f);
            AddLineVertex(data, end, 1f, 0.75f, 0.25f, 1f);
        }

        AddGridLines(data, scene);
        _lineVertexCount = data.Count / 7;

        gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _lineBuffer);
        UploadFloats(gl, GlConsts.GL_ARRAY_BUFFER, [.. data]);
    }

    private readonly record struct MeshRenderContext(
        Matrix4x4 View,
        Matrix4x4 Projection,
        IReadOnlyList<Matrix4x4>? Pose,
        bool Wireframe,
        int Selected,
        int Hovered);

    private void DrawMeshes(GlInterface gl, W3dRenderScene scene, MeshRenderContext context)
    {
        gl.UseProgram(_meshProgram);
        gl.EnableVertexAttribArray(PositionAttribute);
        gl.EnableVertexAttribArray(NormalAttribute);
        gl.EnableVertexAttribArray(UvAttribute);
        gl.EnableVertexAttribArray(ColorAttribute);

        for (int i = 0; i < scene.Meshes.Count && i < _meshBuffers.Count; i++)
        {
            if (scene.Meshes[i].IsHidden || IsMeshHidden(scene.Meshes[i].Name))
            {
                continue;
            }

            RenderMeshElement(gl, scene.Meshes[i], _meshBuffers[i], i, context);
        }
    }

    private void RenderMeshElement(
        GlInterface gl,
        W3dRenderMesh mesh,
        W3dMeshBuffers buffers,
        int meshIndex,
        MeshRenderContext context)
    {
        var model = MeshModel(mesh, context.Pose);
        var mvp = model * context.View * context.Projection;
        SetMatrix(gl, _meshMvpLocation, mvp);
        SetMatrix(gl, _meshModelLocation, model);
        gl.Uniform1f(_meshHasTextureLocation, mesh.TextureIndex >= 0 && mesh.TextureIndex < _textures.Count ? 1 : 0);
        gl.Uniform1f(_meshAlphaTestLocation, mesh.AlphaTest ? 1 : 0);
        gl.Uniform1f(_meshOpacityLocation, mesh.Opacity);
        gl.Uniform1f(_meshSelectedLocation, meshIndex == context.Selected ? 1 : 0);
        gl.Uniform1f(_meshHoveredLocation, meshIndex == context.Hovered ? 1 : 0);

        if (mesh.TextureIndex >= 0 && mesh.TextureIndex < _textures.Count)
        {
            gl.BindTexture(GlConsts.GL_TEXTURE_2D, _textures[mesh.TextureIndex]);
        }

        int stride = VertexFloats * sizeof(float);
        gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, buffers.VertexBuffer);
        gl.VertexAttribPointer(PositionAttribute, 3, GlConsts.GL_FLOAT, 0, stride, IntPtr.Zero);
        gl.VertexAttribPointer(NormalAttribute, 3, GlConsts.GL_FLOAT, 0, stride, (IntPtr)(3 * sizeof(float)));
        gl.VertexAttribPointer(UvAttribute, 2, GlConsts.GL_FLOAT, 0, stride, (IntPtr)(6 * sizeof(float)));
        gl.VertexAttribPointer(ColorAttribute, 4, GlConsts.GL_FLOAT, 0, stride, (IntPtr)(8 * sizeof(float)));

        if (context.Wireframe)
        {
            gl.BindBuffer(GlConsts.GL_ELEMENT_ARRAY_BUFFER, buffers.EdgeBuffer);
            gl.DrawElements(GlLines, buffers.EdgeCount, GlUnsignedInt, IntPtr.Zero);
        }
        else
        {
            gl.BindBuffer(GlConsts.GL_ELEMENT_ARRAY_BUFFER, buffers.IndexBuffer);
            gl.DrawElements(GlConsts.GL_TRIANGLES, buffers.IndexCount, GlUnsignedInt, IntPtr.Zero);
        }
    }

    private void DrawLines(GlInterface gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_lineVertexCount == 0)
        {
            return;
        }

        gl.UseProgram(_lineProgram);
        SetMatrix(gl, _lineMvpLocation, view * projection);
        gl.BindBuffer(GlConsts.GL_ARRAY_BUFFER, _lineBuffer);
        gl.EnableVertexAttribArray(PositionAttribute);
        gl.EnableVertexAttribArray(1);
        int stride = 7 * sizeof(float);
        gl.VertexAttribPointer(PositionAttribute, 3, GlConsts.GL_FLOAT, 0, stride, IntPtr.Zero);
        gl.VertexAttribPointer(1, 4, GlConsts.GL_FLOAT, 0, stride, (IntPtr)(3 * sizeof(float)));

        // GlInterface exposes no attribute disable, so re-point the mesh-only
        // attributes into the line buffer to keep stale strides from overreading.
        gl.VertexAttribPointer(UvAttribute, 2, GlConsts.GL_FLOAT, 0, stride, IntPtr.Zero);
        gl.VertexAttribPointer(ColorAttribute, 4, GlConsts.GL_FLOAT, 0, stride, IntPtr.Zero);
        gl.DrawArrays(GlLines, 0, (IntPtr)_lineVertexCount);
        gl.DrawArrays(GlPoints, 0, (IntPtr)_lineVertexCount);
    }

    private void UpdateHover(Point position)
    {
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastHoverTimestamp, now).TotalMilliseconds < HoverThrottleMs)
        {
            return;
        }

        _lastHoverTimestamp = now;
        var scene = Scene;
        if (scene == null || scene.Meshes.Count == 0)
        {
            SetHover(-1, null);
            return;
        }

        var ray = ScreenRay(position);
        if (ray == null)
        {
            SetHover(-1, null);
            return;
        }

        var hit = W3dRayPicker.Pick(scene, ray.Value.Origin, ray.Value.Direction, CurrentMeshModels(scene), IsMeshHidden);
        if (hit == null || scene.Meshes[hit.MeshIndex].IsHidden || IsMeshHidden(scene.Meshes[hit.MeshIndex].Name))
        {
            SetHover(-1, null);
            return;
        }

        SetHover(hit.MeshIndex, scene.Meshes[hit.MeshIndex].Name);
    }

    private void SetHover(int meshIndex, string? meshName)
    {
        if (_hoveredMeshIndex == meshIndex)
        {
            return;
        }

        _hoveredMeshIndex = meshIndex;
        ToolTip.SetTip(this, meshName);
        Cursor = meshIndex >= 0 ? HandCursor : Cursor.Default;
        RequestNextFrameRendering();
    }

    private void PickAt(Point position)
    {
        var scene = Scene;
        if (scene == null || scene.Meshes.Count == 0)
        {
            return;
        }

        var ray = ScreenRay(position);
        if (ray == null)
        {
            return;
        }

        var hit = W3dRayPicker.Pick(scene, ray.Value.Origin, ray.Value.Direction, CurrentMeshModels(scene), IsMeshHidden);
        SelectedMeshIndex = hit?.MeshIndex ?? -1;
    }

    private Matrix4x4[] CurrentMeshModels(W3dRenderScene scene)
    {
        var pose = Pose;
        var models = new Matrix4x4[scene.Meshes.Count];
        for (int i = 0; i < models.Length; i++)
        {
            models[i] = MeshModel(scene.Meshes[i], pose);
        }

        return models;
    }

    private (Vector3 Origin, Vector3 Direction)? ScreenRay(Point position)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 1 || height < 1)
        {
            return null;
        }

        Matrix4x4 view = Matrix4x4.Identity;
        Matrix4x4 projection = Matrix4x4.Identity;
        lock (_cameraLock)
        {
            var eye = CameraEye();
            view = Matrix4x4.CreateLookAt(eye, _target, new Vector3(0, 0, 1));
            float aspect = (float)(width / height);
            float near = (float)Math.Max(_distance / 100, 0.01);
            float far = (float)((_distance * 20) + 100);
            projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewRadians, aspect, near, far);
        }

        if (!Matrix4x4.Invert(view * projection, out var inverse))
        {
            return null;
        }

        float x = (float)((position.X / width * 2) - 1);
        float y = (float)(1 - (position.Y / height * 2));
        var nearPoint = Vector3.Transform(new Vector3(x, y, 0), inverse);
        var farPoint = Vector3.Transform(new Vector3(x, y, 1), inverse);
        var direction = farPoint - nearPoint;
        if (direction.LengthSquared() < 1e-12f)
        {
            return null;
        }

        return (nearPoint, Vector3.Normalize(direction));
    }

    private sealed record W3dMeshBuffers(int VertexBuffer, int IndexBuffer, int EdgeBuffer, int IndexCount, int EdgeCount);
}
