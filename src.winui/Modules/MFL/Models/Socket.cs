using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class Socket
{
    public string Name { get; set; } = string.Empty;

    public string BoneName { get; set; } = string.Empty;

    public int BoneIndex { get; set; } = -1;

    public Vector3 Position { get; set; } = Vector3.Zero;

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Socket Clone() => new()
    {
        Name = Name,
        BoneName = BoneName,
        BoneIndex = BoneIndex,
        Position = Position,
        Rotation = Rotation
    };
}

