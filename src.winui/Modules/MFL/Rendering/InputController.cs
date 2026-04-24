using System.Numerics;
using Windows.Foundation;
using Windows.System;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Rendering;

public sealed class InputController
{
    private enum DragMode
    {
        None,
        Orbit,
        Pan
    }

    private bool isDragging;
    private bool wasClick;
    private bool selectionEligible;
    private Point dragStart;
    private Point lastPoint;
    private DragMode dragMode = DragMode.None;

    public bool BeginPointer(Point point, bool isLeftButtonPressed, bool isRightButtonPressed, bool isMiddleButtonPressed)
    {
        wasClick = true;
        selectionEligible = isLeftButtonPressed && !isRightButtonPressed && !isMiddleButtonPressed;
        dragStart = point;
        lastPoint = point;
        isDragging = true;
        dragMode = isRightButtonPressed || isMiddleButtonPressed ? DragMode.Pan : DragMode.Orbit;
        return selectionEligible;
    }

    public void MovePointer(Point point, Camera? camera)
    {
        if (!isDragging || camera is null)
            return;

        if (Math.Abs(point.X - dragStart.X) > 2.0 || Math.Abs(point.Y - dragStart.Y) > 2.0)
            wasClick = false;

        Vector2 delta = new((float)(point.X - lastPoint.X), (float)(point.Y - lastPoint.Y));
        if (dragMode == DragMode.Pan)
            camera.Pan(delta.X, delta.Y);
        else
            camera.Orbit(delta.X, delta.Y);

        lastPoint = point;
    }

    public bool EndPointer()
    {
        if (!isDragging)
            return false;

        isDragging = false;
        bool click = wasClick && selectionEligible;
        dragMode = DragMode.None;
        selectionEligible = false;
        return click;
    }

    public void Zoom(int wheelDelta, Camera? camera)
    {
        camera?.Zoom(wheelDelta);
    }

    public bool HandleKeyDown(VirtualKey key, Camera? camera)
    {
        if (camera is null)
            return false;

        if (key == VirtualKey.R)
        {
            camera.Yaw = 0.9f;
            camera.Pitch = 0.35f;
            camera.Distance = Math.Max(1.5f, camera.Distance);
            return true;
        }

        if (key == VirtualKey.F)
        {
            camera.Distance = Math.Max(1.5f, camera.Distance);
            return true;
        }

        return false;
    }
}

