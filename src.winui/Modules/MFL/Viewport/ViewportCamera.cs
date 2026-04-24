using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Viewport;

public sealed class ViewportCamera : INotifyPropertyChanged
{
    private Vector3 target = Vector3.Zero;
    private float yaw = 0.9f;
    private float pitch = 0.35f;
    private float distance = 6.0f;
    private float fieldOfViewDegrees = 45.0f;
    private float aspectRatio = 1.0f;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Vector3 Target
    {
        get => target;
        set => SetField(ref target, value);
    }

    public float Yaw
    {
        get => yaw;
        set => SetField(ref yaw, value);
    }

    public float Pitch
    {
        get => pitch;
        set
        {
            float clamped = Math.Clamp(value, -1.45f, 1.45f);
            SetField(ref pitch, clamped);
        }
    }

    public float Distance
    {
        get => distance;
        set => SetField(ref distance, Math.Max(0.1f, value));
    }

    public float FieldOfViewDegrees
    {
        get => fieldOfViewDegrees;
        set => SetField(ref fieldOfViewDegrees, Math.Clamp(value, 20.0f, 100.0f));
    }

    public float AspectRatio
    {
        get => aspectRatio;
        set => SetField(ref aspectRatio, Math.Max(0.1f, value));
    }

    public Vector3 Position
    {
        get
        {
            Vector3 offset = new(
                MathF.Cos(Pitch) * MathF.Sin(Yaw),
                MathF.Sin(Pitch),
                MathF.Cos(Pitch) * MathF.Cos(Yaw));
            return Target + (offset * Distance);
        }
    }

    public Vector3 Forward => Vector3.Normalize(Target - Position);

    public Vector3 Right
    {
        get
        {
            Vector3 forward = Forward;
            Vector3 right = Vector3.Cross(forward, Vector3.UnitY);
            if (right == Vector3.Zero)
                right = Vector3.UnitX;
            return Vector3.Normalize(right);
        }
    }

    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

    public void Orbit(float deltaX, float deltaY)
    {
        Yaw -= deltaX * 0.01f;
        Pitch += deltaY * 0.01f;
    }

    public void Pan(float deltaX, float deltaY)
    {
        float scale = Math.Max(0.001f, Distance * 0.0025f);
        Target += (-Right * deltaX * scale) + (Up * deltaY * scale);
    }

    public void Zoom(float delta)
    {
        Distance = Math.Clamp(Distance - delta * 0.01f, 0.2f, 200.0f);
    }

    public void FocusOnBounds(BoundingBox bounds)
    {
        if (bounds.IsEmpty)
            return;

        Target = bounds.Center;
        Distance = Math.Max(1.5f, bounds.Size.Length() * 1.8f);
        Yaw = 0.9f;
        Pitch = 0.35f;
    }

    public bool TryProject(Vector3 world, double viewportWidth, double viewportHeight, out Windows.Foundation.Point screenPoint, out float depth)
    {
        Vector3 relative = world - Position;
        float cameraX = Vector3.Dot(relative, Right);
        float cameraY = Vector3.Dot(relative, Up);
        float cameraZ = Vector3.Dot(relative, Forward);

        if (cameraZ <= 0.0001f)
        {
            screenPoint = default;
            depth = float.MaxValue;
            return false;
        }

        float fovRadians = FieldOfViewDegrees * (MathF.PI / 180.0f);
        float tanHalf = MathF.Tan(fovRadians * 0.5f);
        float ndcX = cameraX / (cameraZ * tanHalf * AspectRatio);
        float ndcY = cameraY / (cameraZ * tanHalf);

        double screenX = ((ndcX + 1.0f) * 0.5f) * viewportWidth;
        double screenY = ((1.0f - ndcY) * 0.5f) * viewportHeight;
        screenPoint = new Windows.Foundation.Point(screenX, screenY);
        depth = cameraZ;
        return true;
    }

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    public Matrix4x4 GetProjectionMatrix(float aspect)
    {
        float fovRadians = FieldOfViewDegrees * (MathF.PI / 180.0f);
        return Matrix4x4.CreatePerspectiveFieldOfView(fovRadians, Math.Max(0.1f, aspect), 0.01f, 1000.0f);
    }

    public ViewportRay CreateRay(Windows.Foundation.Point screenPoint, double viewportWidth, double viewportHeight)
    {
        float ndcX = (float)((screenPoint.X / Math.Max(1.0, viewportWidth)) * 2.0 - 1.0);
        float ndcY = (float)(1.0 - (screenPoint.Y / Math.Max(1.0, viewportHeight)) * 2.0);
        float fovRadians = FieldOfViewDegrees * (MathF.PI / 180.0f);
        float tanHalf = MathF.Tan(fovRadians * 0.5f);

        Vector3 direction = Vector3.Normalize(Forward
            + (Right * ndcX * tanHalf * AspectRatio)
            + (Up * ndcY * tanHalf));
        return new ViewportRay(Position, direction);
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

