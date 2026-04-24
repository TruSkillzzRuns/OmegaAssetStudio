using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class BoundingBox
{
    public Vector3 Min { get; set; } = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    public Vector3 Max { get; set; } = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

    public static BoundingBox Empty => new();

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public Vector3 Center => IsEmpty ? Vector3.Zero : (Min + Max) * 0.5f;

    public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;

    public void Include(Vector3 point)
    {
        if (IsEmpty)
        {
            Min = point;
            Max = point;
            return;
        }

        Min = Vector3.Min(Min, point);
        Max = Vector3.Max(Max, point);
    }

    public BoundingBox Clone() => new()
    {
        Min = Min,
        Max = Max
    };
}

