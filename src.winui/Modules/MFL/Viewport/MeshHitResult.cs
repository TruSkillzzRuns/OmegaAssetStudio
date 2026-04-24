using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Viewport;

public sealed class MeshHitResult : EventArgs
{
    public string MeshKey { get; set; } = string.Empty;

    public int TriangleIndex { get; set; } = -1;

    public int VertexIndex { get; set; } = -1;

    public Vector3 HitPoint { get; set; } = Vector3.Zero;

    public float Distance { get; set; }
}

public readonly record struct ViewportRay(Vector3 Origin, Vector3 Direction);

