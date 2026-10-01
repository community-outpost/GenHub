// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Silk.NET renderer for the WorldBuilder 3D viewport. Owns no context:
/// Avalonia creates and owns the shared context, and this class issues draw
/// calls against it. Draws the terrain mesh, placed W3D models, and the
/// ground grid; water and overlays attach in later work packages.
/// Shaders stay in the GLES3 subset so the same sources compile on desktop
/// GL 3.3+ and OpenGL ES 3.0+.
/// </summary>
public sealed class WbRenderer : IDisposable
{
    private sealed record ModelUpload(
        uint Vao,
        uint Vbo,
        uint Ibo,
        uint IndexCount,
        uint Texture,
        W3dGlState State,
        bool TwoSided);

    /// <summary>
    /// Minimum desktop GL major version.
    /// </summary>
    public const int MinDesktopMajor = 3;

    /// <summary>
    /// Minimum desktop GL minor version at major 3.
    /// </summary>
    public const int MinDesktopMinor = 3;

    /// <summary>
    /// Minimum OpenGL ES major version.
    /// </summary>
    public const int MinEsMajor = 3;

    private const string VertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uViewProj;
        void main()
        {
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string FragmentShaderBody = """
        out vec4 FragColor;
        uniform vec4 uColor;
        void main()
        {
            FragColor = uColor;
        }
        """;

    private const string TerrainVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aColor;
        layout(location = 2) in vec2 aUv1;
        layout(location = 3) in vec2 aUv2;
        layout(location = 4) in vec2 aAlpha;
        uniform mat4 uViewProj;
        out vec3 vColor;
        out vec2 vUv1;
        out vec2 vUv2;
        out vec2 vAlpha;
        void main()
        {
            gl_Position = uViewProj * vec4(aPos, 1.0);
            vColor = aColor;
            vUv1 = aUv1;
            vUv2 = aUv2;
            vAlpha = aAlpha;
        }
        """;

    private const string TerrainFragmentShaderBody = """
        in vec3 vColor;
        in vec2 vUv1;
        in vec2 vUv2;
        in vec2 vAlpha;
        uniform sampler2D uTiles;
        out vec4 FragColor;
        void main()
        {
            vec3 baseTile = texture(uTiles, vUv1).rgb;
            vec3 overTile = texture(uTiles, vUv2).rgb;
            vec3 color = mix(baseTile, overTile, vAlpha.x) * vColor;
            FragColor = vec4(color, 1.0);
        }
        """;

    private const string ModelVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;
        layout(location = 3) in vec4 aColor;
        uniform mat4 uViewProj;
        uniform vec3 uSunDir;
        out vec2 vUv;
        out vec4 vColor;
        out float vShade;
        void main()
        {
            gl_Position = uViewProj * vec4(aPos, 1.0);
            vUv = aUv;
            vColor = aColor;
            float ndl = max(dot(normalize(aNormal), normalize(uSunDir)), 0.0);
            vShade = 0.45 + 0.55 * ndl;
        }
        """;

    private const string OverlayVertexShaderBody = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aColor;
        uniform mat4 uViewProj;
        out vec4 vColor;
        void main()
        {
            gl_Position = uViewProj * vec4(aPos, 1.0);
            vColor = aColor;
        }
        """;

    private const string OverlayFragmentShaderBody = """
        in vec4 vColor;
        out vec4 FragColor;
        void main()
        {
            FragColor = vColor;
        }
        """;

    private const string ModelFragmentShaderBody = """
        in vec2 vUv;
        in vec4 vColor;
        in float vShade;
        uniform sampler2D uTex;
        uniform int uTextured;
        uniform int uAlphaTest;
        uniform float uAlphaRef;
        uniform int uCombine;
        out vec4 FragColor;
        void main()
        {
            vec4 texel = uTextured != 0 ? texture(uTex, vUv) : vec4(1.0);
            vec3 lit = texel.rgb * vShade;
            vec3 rgb = lit;
            if (uCombine == 1)
            {
                rgb = lit * vColor.rgb;
            }
            else if (uCombine == 2)
            {
                rgb = lit + vColor.rgb;
            }
            else if (uCombine == 3)
            {
                rgb = clamp(2.0 * lit * vColor.rgb, 0.0, 1.0);
            }

            float alpha = texel.a * vColor.a;
            if (uAlphaTest != 0 && alpha < uAlphaRef)
            {
                discard;
            }

            FragColor = vec4(rgb, alpha);
        }
        """;

    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _gridVao;
    private readonly uint _gridVbo;
    private readonly uint _terrainProgram;
    private readonly uint _terrainVao;
    private readonly uint _terrainVbo;
    private readonly uint _terrainIbo;
    private readonly uint _terrainTexture;
    private readonly int _viewProjLocation;
    private readonly int _colorLocation;
    private readonly int _terrainViewProjLocation;
    private readonly int _terrainTilesLocation;
    private readonly uint _modelProgram;
    private readonly int _modelViewProjLocation;
    private readonly int _modelSunLocation;
    private readonly int _modelTexLocation;
    private readonly int _modelTexturedLocation;
    private readonly int _modelAlphaTestLocation;
    private readonly int _modelAlphaRefLocation;
    private readonly int _modelCombineLocation;
    private readonly uint _overlayProgram;
    private readonly uint _waterVao;
    private readonly uint _waterVbo;
    private readonly uint _waterIbo;
    private readonly uint _linesVao;
    private readonly uint _linesVbo;
    private readonly int _overlayViewProjLocation;
    private readonly Dictionary<string, uint> _modelTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModelUpload> _modelUploads = [];
    private int _gridVertexCount;
    private string _gridKey = string.Empty;
    private uint _terrainIndexCount;
    private uint _terrainExtraIndexCount;
    private long _terrainVersion = -1;
    private long _modelsVersion = -1;
    private uint _waterIndexCount;
    private long _waterVersion = -1;
    private int _linesVertexCount;
    private long _linesVersion = -1;
    private bool _disposed;

    private WbRenderer(
        GL gl,
        uint program,
        uint gridVao,
        uint gridVbo,
        uint terrainProgram,
        uint terrainVao,
        uint terrainVbo,
        uint terrainIbo,
        uint terrainTexture,
        int viewProjLocation,
        int colorLocation,
        int terrainViewProjLocation,
        int terrainTilesLocation,
        uint modelProgram,
        int modelViewProjLocation,
        int modelSunLocation,
        int modelTexLocation,
        int modelTexturedLocation,
        int modelAlphaTestLocation,
        int modelAlphaRefLocation,
        int modelCombineLocation,
        uint overlayProgram,
        uint waterVao,
        uint waterVbo,
        uint waterIbo,
        uint linesVao,
        uint linesVbo,
        int overlayViewProjLocation)
    {
        _gl = gl;
        _program = program;
        _gridVao = gridVao;
        _gridVbo = gridVbo;
        _terrainProgram = terrainProgram;
        _terrainVao = terrainVao;
        _terrainVbo = terrainVbo;
        _terrainIbo = terrainIbo;
        _terrainTexture = terrainTexture;
        _viewProjLocation = viewProjLocation;
        _colorLocation = colorLocation;
        _terrainViewProjLocation = terrainViewProjLocation;
        _terrainTilesLocation = terrainTilesLocation;
        _modelProgram = modelProgram;
        _modelViewProjLocation = modelViewProjLocation;
        _modelSunLocation = modelSunLocation;
        _modelTexLocation = modelTexLocation;
        _modelTexturedLocation = modelTexturedLocation;
        _modelAlphaTestLocation = modelAlphaTestLocation;
        _modelAlphaRefLocation = modelAlphaRefLocation;
        _modelCombineLocation = modelCombineLocation;
        _overlayProgram = overlayProgram;
        _waterVao = waterVao;
        _waterVbo = waterVbo;
        _waterIbo = waterIbo;
        _linesVao = linesVao;
        _linesVbo = linesVbo;
        _overlayViewProjLocation = overlayViewProjLocation;
    }

    /// <summary>
    /// Creates a renderer against an already-current GL context.
    /// </summary>
    /// <param name="gl">The Silk.NET bindings for the current context.</param>
    /// <returns>The renderer, or a failure describing the missing requirement.</returns>
    public static OperationResult<WbRenderer> Create(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        var version = GetVersionString(gl);
        if (!CheckVersion(version, out var error))
        {
            return OperationResult<WbRenderer>.CreateFailure(error);
        }

        var program = CompileProgram(gl, VertexShaderBody, FragmentShaderBody, out error);
        if (program == 0)
        {
            return OperationResult<WbRenderer>.CreateFailure(error);
        }

        var terrainProgram = CompileProgram(gl, TerrainVertexShaderBody, TerrainFragmentShaderBody, out error);
        if (terrainProgram == 0)
        {
            gl.DeleteProgram(program);
            return OperationResult<WbRenderer>.CreateFailure(error);
        }

        var modelProgram = CompileProgram(gl, ModelVertexShaderBody, ModelFragmentShaderBody, out error);
        if (modelProgram == 0)
        {
            gl.DeleteProgram(program);
            gl.DeleteProgram(terrainProgram);
            return OperationResult<WbRenderer>.CreateFailure(error);
        }

        var overlayProgram = CompileProgram(gl, OverlayVertexShaderBody, OverlayFragmentShaderBody, out error);
        if (overlayProgram == 0)
        {
            gl.DeleteProgram(program);
            gl.DeleteProgram(terrainProgram);
            gl.DeleteProgram(modelProgram);
            return OperationResult<WbRenderer>.CreateFailure(error);
        }

        var waterVao = gl.GenVertexArray();
        var waterVbo = gl.GenBuffer();
        var waterIbo = gl.GenBuffer();
        gl.BindVertexArray(waterVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, waterVbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, waterIbo);
        SetupOverlayAttribs(gl);
        gl.BindVertexArray(0);
        var linesVao = gl.GenVertexArray();
        var linesVbo = gl.GenBuffer();
        gl.BindVertexArray(linesVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, linesVbo);
        SetupOverlayAttribs(gl);
        gl.BindVertexArray(0);

        var vao = gl.GenVertexArray();
        var vbo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * (uint)sizeof(float), IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.BindVertexArray(0);
        var terrainVao = gl.GenVertexArray();
        var terrainVbo = gl.GenBuffer();
        var terrainIbo = gl.GenBuffer();
        gl.BindVertexArray(terrainVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, terrainVbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, terrainIbo);
        SetupTerrainAttribs(gl);
        gl.BindVertexArray(0);
        var terrainTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, terrainTexture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.Enable(EnableCap.DepthTest);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        var viewProjLocation = gl.GetUniformLocation(program, "uViewProj");
        var colorLocation = gl.GetUniformLocation(program, "uColor");
        var terrainViewProjLocation = gl.GetUniformLocation(terrainProgram, "uViewProj");
        var terrainTilesLocation = gl.GetUniformLocation(terrainProgram, "uTiles");
        var modelViewProjLocation = gl.GetUniformLocation(modelProgram, "uViewProj");
        var modelSunLocation = gl.GetUniformLocation(modelProgram, "uSunDir");
        var modelTexLocation = gl.GetUniformLocation(modelProgram, "uTex");
        var modelTexturedLocation = gl.GetUniformLocation(modelProgram, "uTextured");
        var modelAlphaTestLocation = gl.GetUniformLocation(modelProgram, "uAlphaTest");
        var modelAlphaRefLocation = gl.GetUniformLocation(modelProgram, "uAlphaRef");
        var modelCombineLocation = gl.GetUniformLocation(modelProgram, "uCombine");
        var overlayViewProjLocation = gl.GetUniformLocation(overlayProgram, "uViewProj");
        return OperationResult<WbRenderer>.CreateSuccess(new WbRenderer(
            gl,
            program,
            vao,
            vbo,
            terrainProgram,
            terrainVao,
            terrainVbo,
            terrainIbo,
            terrainTexture,
            viewProjLocation,
            colorLocation,
            terrainViewProjLocation,
            terrainTilesLocation,
            modelProgram,
            modelViewProjLocation,
            modelSunLocation,
            modelTexLocation,
            modelTexturedLocation,
            modelAlphaTestLocation,
            modelAlphaRefLocation,
            modelCombineLocation,
            overlayProgram,
            waterVao,
            waterVbo,
            waterIbo,
            linesVao,
            linesVbo,
            overlayViewProjLocation));
    }

    /// <summary>
    /// Checks a GL version string against the minimum requirements.
    /// </summary>
    /// <param name="version">The version string, or null when unavailable.</param>
    /// <param name="error">The failure detail when the check fails.</param>
    /// <returns>True when the context is usable.</returns>
    public static bool CheckVersion(string? version, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(version))
        {
            error = "OpenGL version is unavailable.";
            return false;
        }

        if (version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase))
        {
            var es = ParseVersion(version);
            if (es.Major < MinEsMajor)
            {
                error = $"OpenGL ES {MinEsMajor}.0 or newer is required (found '{version}').";
                return false;
            }

            return true;
        }

        var desktop = ParseVersion(version);
        if (desktop.Major < MinDesktopMajor || (desktop.Major == MinDesktopMajor && desktop.Minor < MinDesktopMinor))
        {
            error = $"OpenGL {MinDesktopMajor}.{MinDesktopMinor} or newer is required (found '{version}').";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Renders one frame: terrain mesh, placed models, then the ground grid when enabled.
    /// </summary>
    /// <param name="camera">The orbit camera.</param>
    /// <param name="viewportPixels">The viewport size in pixels.</param>
    /// <param name="terrain">The terrain data, or null for an empty scene.</param>
    /// <param name="options">The render options.</param>
    public void Render(WbCamera camera, Vector2 viewportPixels, MapTerrainData? terrain, MapCanvasRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        _gl.ClearColor(0.5f, 0.5f, 0.5f, 1.0f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        if (terrain == null || terrain.Width <= 0 || terrain.Height <= 0)
        {
            return;
        }

        var viewProj = camera.ViewMatrix() * camera.ProjectionMatrix(viewportPixels.X / Math.Max(1.0f, viewportPixels.Y));
        if (_terrainIndexCount > 0)
        {
            DrawTerrain(viewProj);
        }

        if (options.Layers.HasFlag(MapCanvasLayers.Objects) && _modelUploads.Count > 0)
        {
            DrawModels(viewProj, SunDirection(options));
        }

        if (options.Layers.HasFlag(MapCanvasLayers.Water) && _waterIndexCount > 0)
        {
            DrawWater(viewProj);
        }

        if (_linesVertexCount > 0)
        {
            DrawLines(viewProj);
        }

        if (options.Layers.HasFlag(MapCanvasLayers.Grid))
        {
            DrawGrid(viewProj, terrain, options.GridStep);
        }
    }

    /// <summary>
    /// Uploads terrain mesh and atlas pixels, replacing the previous upload
    /// when the version differs. Must run on the render thread.
    /// </summary>
    /// <param name="data">The render data, or null to clear the terrain.</param>
    /// <param name="version">The data version token.</param>
    public void SetTerrainData(WbTerrainRenderData? data, long version)
    {
        ThrowIfDisposed();
        if (version == _terrainVersion)
        {
            return;
        }

        _terrainVersion = version;
        _terrainIndexCount = 0;
        _terrainExtraIndexCount = 0;
        if (data == null || data.Indices.Length == 0)
        {
            return;
        }

        _gl.BindVertexArray(_terrainVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _terrainVbo);
        var upload = data.Vertices;
        if (data.ExtraVertices.Length > 0)
        {
            upload = new float[data.Vertices.Length + data.ExtraVertices.Length];
            Array.Copy(data.Vertices, upload, data.Vertices.Length);
            Array.Copy(data.ExtraVertices, 0, upload, data.Vertices.Length, data.ExtraVertices.Length);
        }

        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, upload, BufferUsageARB.StaticDraw);

        var combined = new uint[data.Indices.Length + data.ExtraIndices.Length];
        Array.Copy(data.Indices, combined, data.Indices.Length);
        var extraBase = (uint)(data.Vertices.Length / WbTerrainMesh.StrideFloats);
        for (var i = 0; i < data.ExtraIndices.Length; i++)
        {
            combined[data.Indices.Length + i] = extraBase + data.ExtraIndices[i];
        }

        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, combined, BufferUsageARB.StaticDraw);

        _gl.BindTexture(TextureTarget.Texture2D, _terrainTexture);

        // GetPinnableReference yields a null pointer for empty input, matching the previous fixed behavior.
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            (int)InternalFormat.Rgba8,
            (uint)data.AtlasWidth,
            (uint)data.AtlasHeight,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            in data.AtlasPixels.AsSpan().GetPinnableReference());

        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.BindVertexArray(0);
        _terrainIndexCount = (uint)data.Indices.Length;
        _terrainExtraIndexCount = (uint)data.ExtraIndices.Length;
    }

    /// <summary>
    /// Uploads model draws, replacing the previous upload when the version
    /// differs. Must run on the render thread.
    /// </summary>
    /// <param name="data">The render data, or null to clear the models.</param>
    /// <param name="version">The data version token.</param>
    public void SetModels(WbModelRenderData? data, long version)
    {
        ThrowIfDisposed();
        if (version == _modelsVersion)
        {
            return;
        }

        _modelsVersion = version;
        ClearModelUploads();
        if (data == null)
        {
            return;
        }

        var usedTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var draw in data.Draws)
        {
            var upload = UploadDraw(draw, usedTextures);
            if (upload != null)
            {
                _modelUploads.Add(upload);
            }
        }

        EvictUnusedModelTextures(usedTextures);
    }

    /// <summary>
    /// Uploads water quads, replacing the previous upload when the version
    /// differs. Must run on the render thread.
    /// </summary>
    /// <param name="data">The render data, or null to clear the water.</param>
    /// <param name="version">The data version token.</param>
    public void SetWater(WbWaterData? data, long version)
    {
        ThrowIfDisposed();
        if (version == _waterVersion)
        {
            return;
        }

        _waterVersion = version;
        _waterIndexCount = 0;
        if (data == null || data.Indices.Length == 0)
        {
            return;
        }

        _gl.BindVertexArray(_waterVao);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data.Vertices, BufferUsageARB.StaticDraw);

        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, data.Indices, BufferUsageARB.StaticDraw);

        _gl.BindVertexArray(0);
        _waterIndexCount = (uint)data.Indices.Length;
    }

    /// <summary>
    /// Uploads overlay lines, replacing the previous upload when the version
    /// differs. Must run on the render thread.
    /// </summary>
    /// <param name="data">The render data, or null to clear the lines.</param>
    /// <param name="version">The data version token.</param>
    public void SetOverlayLines(WbOverlayLines? data, long version)
    {
        ThrowIfDisposed();
        if (version == _linesVersion)
        {
            return;
        }

        _linesVersion = version;
        _linesVertexCount = 0;
        if (data == null || data.Vertices.Length == 0)
        {
            return;
        }

        _gl.BindVertexArray(_linesVao);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, data.Vertices, BufferUsageARB.StaticDraw);

        _gl.BindVertexArray(0);
        _linesVertexCount = data.Vertices.Length / WbOverlayLines.StrideFloats;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gl.DeleteBuffer(_gridVbo);
        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteProgram(_program);
        _gl.DeleteBuffer(_terrainVbo);
        _gl.DeleteBuffer(_terrainIbo);
        _gl.DeleteVertexArray(_terrainVao);
        _gl.DeleteTexture(_terrainTexture);
        _gl.DeleteProgram(_terrainProgram);
        ClearModelUploads();
        foreach (var texture in _modelTextures.Values)
        {
            _gl.DeleteTexture(texture);
        }

        _modelTextures.Clear();
        _gl.DeleteProgram(_modelProgram);
        _gl.DeleteBuffer(_waterVbo);
        _gl.DeleteBuffer(_waterIbo);
        _gl.DeleteVertexArray(_waterVao);
        _gl.DeleteBuffer(_linesVbo);
        _gl.DeleteVertexArray(_linesVao);
        _gl.DeleteProgram(_overlayProgram);
    }

    private static void SetupTerrainAttribs(GL gl)
    {
        const uint stride = 12 * sizeof(float);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (IntPtr)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(10 * sizeof(float)));
        gl.EnableVertexAttribArray(4);
    }

    private static string GetVersionString(GL gl)
    {
        return gl.GetStringS(StringName.Version) ?? string.Empty;
    }

    private static uint CompileProgram(GL gl, string vertexBody, string fragmentBody, out string error)
    {
        error = string.Empty;
        var version = GetVersionString(gl);
        var directive = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase) ? "#version 300 es" : "#version 330 core";
        var vertex = CompileShader(gl, ShaderType.VertexShader, directive + "\n" + vertexBody, out error);
        if (vertex == 0)
        {
            return 0;
        }

        var fragment = CompileShader(gl, ShaderType.FragmentShader, directive + "\n" + fragmentBody, out error);
        if (fragment == 0)
        {
            gl.DeleteShader(vertex);
            return 0;
        }

        var program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        gl.LinkProgram(program);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            error = $"Program link failed: {gl.GetProgramInfoLog(program)}.";
            gl.DeleteProgram(program);
            return 0;
        }

        return program;
    }

    private static uint CompileShader(GL gl, ShaderType type, string source, out string error)
    {
        error = string.Empty;
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            error = $"{type} compile failed: {gl.GetShaderInfoLog(shader)}.";
            gl.DeleteShader(shader);
            return 0;
        }

        return shader;
    }

    private static (int Major, int Minor) ParseVersion(string version)
    {
        var major = 0;
        var minor = 0;
        var index = 0;
        while (index < version.Length && !char.IsDigit(version[index]))
        {
            index++;
        }

        while (index < version.Length && char.IsDigit(version[index]))
        {
            major = (major * 10) + (version[index] - '0');
            index++;
        }

        if (index < version.Length && version[index] == '.')
        {
            index++;
            while (index < version.Length && char.IsDigit(version[index]))
            {
                minor = (minor * 10) + (version[index] - '0');
                index++;
            }
        }

        return (major, minor);
    }

    private static float[] ToColumnMajor(Matrix4x4 matrix)
    {
        return
        [
            matrix.M11, matrix.M21, matrix.M31, matrix.M41,
            matrix.M12, matrix.M22, matrix.M32, matrix.M42,
            matrix.M13, matrix.M23, matrix.M33, matrix.M43,
            matrix.M14, matrix.M24, matrix.M34, matrix.M44,
        ];
    }

    private static DepthFunction MapDepthFunction(W3dDepthFunction function)
    {
        return function switch
        {
            W3dDepthFunction.Never => DepthFunction.Never,
            W3dDepthFunction.Less => DepthFunction.Less,
            W3dDepthFunction.Equal => DepthFunction.Equal,
            W3dDepthFunction.Lequal => DepthFunction.Lequal,
            W3dDepthFunction.Greater => DepthFunction.Greater,
            W3dDepthFunction.NotEqual => DepthFunction.Notequal,
            W3dDepthFunction.Gequal => DepthFunction.Gequal,
            _ => DepthFunction.Always,
        };
    }

    private static BlendingFactor MapBlendFactor(W3dBlendFactor factor)
    {
        return factor switch
        {
            W3dBlendFactor.Zero => BlendingFactor.Zero,
            W3dBlendFactor.One => BlendingFactor.One,
            W3dBlendFactor.SrcColor => BlendingFactor.SrcColor,
            W3dBlendFactor.OneMinusSrcColor => BlendingFactor.OneMinusSrcColor,
            W3dBlendFactor.SrcAlpha => BlendingFactor.SrcAlpha,
            _ => BlendingFactor.OneMinusSrcAlpha,
        };
    }

    private static Vector3 SunDirection(MapCanvasRenderOptions options)
    {
        var pitch = options.SunPitch * MathF.PI / 180.0f;
        var yaw = options.SunYaw * MathF.PI / 180.0f;
        return new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
    }

    private static void SetupOverlayAttribs(GL gl)
    {
        const uint stride = (uint)WbWaterData.StrideFloats * sizeof(float);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
    }

    private static void SetupModelAttribs(GL gl)
    {
        const uint stride = (uint)WbModelDraw.StrideFloats * sizeof(float);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (IntPtr)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (IntPtr)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (IntPtr)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
    }

    /// <summary>
    /// Draws indexed triangles from a byte offset into the bound element buffer.
    /// Silk.NET 2.22 exposes no safe offset overload for DrawElements, so the
    /// offset cast is isolated here. No managed memory is pinned or dereferenced.
    /// </summary>
    /// <param name="gl">The GL bindings.</param>
    /// <param name="mode">The primitive mode.</param>
    /// <param name="count">The index count.</param>
    /// <param name="byteOffset">The byte offset into the bound element buffer.</param>
    [SuppressMessage("Major Vulnerability", "S6640", Justification = "Byte offset into GL-owned index buffer; Silk.NET 2.22 offers no safe DrawElements offset overload, and no managed memory is pinned or dereferenced.")]
    private static void DrawElementsAtOffset(GL gl, PrimitiveType mode, uint count, nint byteOffset)
    {
        unsafe
        {
            gl.DrawElements(mode, count, DrawElementsType.UnsignedInt, (void*)byteOffset);
        }
    }

    private void DrawTerrain(Matrix4x4 viewProj)
    {
        _gl.UseProgram(_terrainProgram);
        var columnMajor = ToColumnMajor(viewProj);
        _gl.UniformMatrix4(_terrainViewProjLocation, false, columnMajor.AsSpan());

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _terrainTexture);
        _gl.Uniform1(_terrainTilesLocation, 0);
        _gl.BindVertexArray(_terrainVao);
        DrawElementsAtOffset(_gl, PrimitiveType.Triangles, _terrainIndexCount, 0);
        if (_terrainExtraIndexCount > 0)
        {
            DrawElementsAtOffset(_gl, PrimitiveType.Triangles, _terrainExtraIndexCount, (nint)(_terrainIndexCount * sizeof(uint)));
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    private void DrawGrid(Matrix4x4 viewProj, MapTerrainData terrain, int gridStep)
    {
        var step = Math.Max(1, gridStep);
        var minX = -terrain.BorderSize * 10.0f;
        var minY = -terrain.BorderSize * 10.0f;
        var maxX = (terrain.Width - terrain.BorderSize) * 10.0f;
        var maxY = (terrain.Height - terrain.BorderSize) * 10.0f;
        var key = $"{terrain.Width}x{terrain.Height}:{terrain.BorderSize}:{step}";
        if (!string.Equals(key, _gridKey, StringComparison.Ordinal))
        {
            _gridKey = key;
            var vertices = WbGridMesh.Build(minX, minY, maxX, maxY, step * 10.0f, 0.5f);
            _gridVertexCount = vertices.Length / 3;
            UploadGridBuffer(vertices);
        }

        if (_gridVertexCount == 0)
        {
            return;
        }

        _gl.UseProgram(_program);
        var columnMajor = ToColumnMajor(viewProj);
        _gl.UniformMatrix4(_viewProjLocation, false, columnMajor.AsSpan());

        _gl.Uniform4(_colorLocation, 1.0f, 1.0f, 1.0f, 0.35f);
        _gl.BindVertexArray(_gridVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    private void UploadGridBuffer(float[] vertices)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);
    }

    private ModelUpload? UploadDraw(WbModelDraw draw, HashSet<string> usedTextures)
    {
        if (draw.Vertices.Length == 0 || draw.Indices.Length == 0)
        {
            return null;
        }

        var texture = UploadModelTexture(draw, usedTextures);
        var vao = _gl.GenVertexArray();
        var vbo = _gl.GenBuffer();
        var ibo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);
        SetupModelAttribs(_gl);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, draw.Vertices, BufferUsageARB.StaticDraw);

        _gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, draw.Indices, BufferUsageARB.StaticDraw);

        _gl.BindVertexArray(0);
        return new ModelUpload(vao, vbo, ibo, (uint)draw.Indices.Length, texture, draw.State, draw.TwoSided);
    }

    private uint UploadModelTexture(WbModelDraw draw, HashSet<string> usedTextures)
    {
        if (draw.TextureName == null || draw.Texture == null)
        {
            return 0;
        }

        usedTextures.Add(draw.TextureName);
        if (_modelTextures.TryGetValue(draw.TextureName, out var existing))
        {
            return existing;
        }

        var decoded = draw.Texture;
        if (decoded.Width <= 0 || decoded.Height <= 0 || decoded.PixelData.Length != decoded.Width * decoded.Height * 4)
        {
            return 0;
        }

        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // GetPinnableReference yields a null pointer for empty input, matching the previous fixed behavior.
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            (int)InternalFormat.Rgba8,
            (uint)decoded.Width,
            (uint)decoded.Height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            in decoded.PixelData.AsSpan().GetPinnableReference());

        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _modelTextures[draw.TextureName] = texture;
        return texture;
    }

    private void EvictUnusedModelTextures(HashSet<string> usedTextures)
    {
        var evicted = new List<string>();
        foreach (var name in _modelTextures.Keys)
        {
            if (!usedTextures.Contains(name))
            {
                evicted.Add(name);
            }
        }

        foreach (var name in evicted)
        {
            _gl.DeleteTexture(_modelTextures[name]);
            _modelTextures.Remove(name);
        }
    }

    private void ClearModelUploads()
    {
        foreach (var upload in _modelUploads)
        {
            _gl.DeleteBuffer(upload.Vbo);
            _gl.DeleteBuffer(upload.Ibo);
            _gl.DeleteVertexArray(upload.Vao);
        }

        _modelUploads.Clear();
    }

    private void DrawModels(Matrix4x4 viewProj, Vector3 sunDirection)
    {
        _gl.UseProgram(_modelProgram);
        var columnMajor = ToColumnMajor(viewProj);
        _gl.UniformMatrix4(_modelViewProjLocation, false, columnMajor.AsSpan());

        _gl.Uniform3(_modelSunLocation, sunDirection.X, sunDirection.Y, sunDirection.Z);
        _gl.Uniform1(_modelAlphaRefLocation, W3dShaderMap.AlphaReference);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.Uniform1(_modelTexLocation, 0);
        foreach (var upload in _modelUploads)
        {
            DrawModelUpload(upload);
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Disable(EnableCap.CullFace);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    private void DrawModelUpload(ModelUpload upload)
    {
        var state = upload.State;
        _gl.DepthFunc(MapDepthFunction(state.DepthFunction));
        _gl.DepthMask(state.DepthWrite);
        if (state.BlendEnabled)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(MapBlendFactor(state.SrcFactor), MapBlendFactor(state.DstFactor));
        }
        else
        {
            _gl.Disable(EnableCap.Blend);
        }

        if (upload.TwoSided)
        {
            _gl.Disable(EnableCap.CullFace);
        }
        else
        {
            _gl.Enable(EnableCap.CullFace);
            _gl.FrontFace(FrontFaceDirection.CW);
        }

        _gl.BindTexture(TextureTarget.Texture2D, upload.Texture);
        _gl.Uniform1(_modelTexturedLocation, upload.Texture != 0 && state.Textured ? 1 : 0);
        _gl.Uniform1(_modelAlphaTestLocation, state.AlphaTest ? 1 : 0);
        _gl.Uniform1(_modelCombineLocation, (int)state.Combine);
        _gl.BindVertexArray(upload.Vao);
        DrawElementsAtOffset(_gl, PrimitiveType.Triangles, upload.IndexCount, 0);
    }

    private void DrawWater(Matrix4x4 viewProj)
    {
        _gl.UseProgram(_overlayProgram);
        var columnMajor = ToColumnMajor(viewProj);
        _gl.UniformMatrix4(_overlayViewProjLocation, false, columnMajor.AsSpan());

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.BindVertexArray(_waterVao);
        DrawElementsAtOffset(_gl, PrimitiveType.Triangles, _waterIndexCount, 0);
        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        _gl.UseProgram(0);
    }

    private void DrawLines(Matrix4x4 viewProj)
    {
        _gl.UseProgram(_overlayProgram);
        var columnMajor = ToColumnMajor(viewProj);
        _gl.UniformMatrix4(_overlayViewProjLocation, false, columnMajor.AsSpan());

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.BindVertexArray(_linesVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_linesVertexCount);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
