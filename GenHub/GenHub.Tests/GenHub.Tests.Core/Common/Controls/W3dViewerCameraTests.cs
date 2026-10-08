using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using GenHub.Common.Controls;
using GenHub.Common.Editors;
using GenHub.Core.Models.Tools.ModelViewer;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Headless interaction tests for the 3D viewer camera inside the shared document canvas.
/// </summary>
public sealed class W3dViewerCameraTests
{
    /// <summary>
    /// Verifies that left-drag on the viewer orbits the camera instead of scrolling the document.
    /// </summary>
    [AvaloniaFact]
    public void Viewer_LeftDrag_OrbitsCameraWithoutScrolling()
    {
        var viewer = new W3dViewerControl
        {
            Width = 400,
            Height = 300,
            Scene = EmptyScene(),
        };
        var canvas = new EditorCanvasControl
        {
            Content = viewer,
        };
        var window = new Window { Width = 800, Height = 600, Content = canvas };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var scroller = canvas.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            scroller.Should().NotBeNull();
            double yawBefore = viewer.CameraYaw;
            double pitchBefore = viewer.CameraPitch;
            var start = CenterOnWindow(viewer, window);

            window.MouseDown(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);
            window.MouseMove(start + new Point(60, 30));
            Dispatcher.UIThread.RunJobs(null);
            window.MouseUp(start + new Point(60, 30), MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);

            viewer.CameraYaw.Should().NotBeApproximately(yawBefore, 0.0001);
            viewer.CameraPitch.Should().NotBeApproximately(pitchBefore, 0.0001);
            scroller!.Offset.Should().Be(new Vector(0, 0));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that left-drag pans the document while pan mode is active.
    /// </summary>
    [AvaloniaFact]
    public void Viewer_LeftDragInPanMode_PansDocumentWithoutOrbiting()
    {
        var viewer = new W3dViewerControl
        {
            Width = 400,
            Height = 300,
            Scene = EmptyScene(),
            IsCanvasPanMode = true,
        };
        var canvas = new EditorCanvasControl
        {
            IsPanMode = true,
            Content = new StackPanel
            {
                Width = 2000,
                Height = 2000,
                Children = { viewer },
            },
        };
        var window = new Window { Width = 800, Height = 600, Content = canvas };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var scroller = canvas.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            scroller.Should().NotBeNull();
            scroller!.Offset = new Vector(600, 0);
            Dispatcher.UIThread.RunJobs(null);
            var offsetBefore = scroller.Offset;
            double yawBefore = viewer.CameraYaw;
            double pitchBefore = viewer.CameraPitch;
            var start = CenterOnWindow(viewer, window);

            window.MouseDown(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);
            window.MouseMove(start + new Point(-120, -80));
            Dispatcher.UIThread.RunJobs(null);
            window.MouseUp(start + new Point(-120, -80), MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);

            viewer.CameraYaw.Should().BeApproximately(yawBefore, 0.0001);
            viewer.CameraPitch.Should().BeApproximately(pitchBefore, 0.0001);
            scroller.Offset.X.Should().BeApproximately(offsetBefore.X + 120, 0.5);
            scroller.Offset.Y.Should().BeApproximately(offsetBefore.Y + 80, 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that left-drag orbits when the viewer sits offset inside the document layout.
    /// </summary>
    [AvaloniaFact]
    public void Viewer_LeftDragNestedWithOffset_OrbitsCamera()
    {
        var viewer = new W3dViewerControl
        {
            Width = 300,
            Height = 200,
            Margin = new Thickness(600, 500, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Scene = EmptyScene(),
        };
        var canvas = new EditorCanvasControl
        {
            Content = viewer,
        };
        var window = new Window { Width = 1000, Height = 800, Content = canvas };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var scroller = canvas.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            scroller.Should().NotBeNull();
            double yawBefore = viewer.CameraYaw;
            double pitchBefore = viewer.CameraPitch;
            var start = CenterOnWindow(viewer, window);

            window.MouseDown(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);
            window.MouseMove(start + new Point(60, 30));
            Dispatcher.UIThread.RunJobs(null);
            window.MouseUp(start + new Point(60, 30), MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);

            viewer.CameraYaw.Should().NotBeApproximately(yawBefore, 0.0001);
            viewer.CameraPitch.Should().NotBeApproximately(pitchBefore, 0.0001);
            scroller!.Offset.Should().Be(new Vector(0, 0));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that focusing a mesh moves the camera target to the mesh center.
    /// </summary>
    [AvaloniaFact]
    public void FocusMesh_SingleMesh_MovesTargetToMeshCenter()
    {
        var vertices = new float[]
        {
            10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            14, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var mesh = new W3dRenderMesh("M", vertices, [], -1, 1, false, false, 0);
        var origin = new W3dVector3(0, 0, 0);
        var bounds = new W3dBoundingBox(origin, new W3dVector3(1, 1, 1), origin, 1);
        var viewer = new W3dViewerControl
        {
            Width = 400,
            Height = 300,
            Scene = new W3dRenderScene([mesh], [], [], bounds),
        };
        var window = new Window { Width = 800, Height = 600, Content = viewer };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            viewer.FocusMesh(0);

            Assert.Equal(12, viewer.CameraTarget.X, 3);
            Assert.Equal(0, viewer.CameraTarget.Y, 3);
            Assert.Equal(0, viewer.CameraTarget.Z, 3);
        }
        finally
        {
            window.Close();
        }
    }

    private static W3dRenderScene EmptyScene()
    {
        var origin = new W3dVector3(0, 0, 0);
        var bounds = new W3dBoundingBox(origin, new W3dVector3(1, 1, 1), origin, 1);
        return new W3dRenderScene([], [], [], bounds);
    }

    private static Point CenterOnWindow(Control control, Window window)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);
        center.Should().NotBeNull();
        return center!.Value;
    }
}
