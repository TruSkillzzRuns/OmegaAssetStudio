using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class Bone
{
    public string Name { get; set; } = string.Empty;

    public int ParentIndex { get; set; } = -1;

    public Vector3 BindPosition { get; set; } = Vector3.Zero;

    public Quaternion BindRotation { get; set; } = Quaternion.Identity;

    public float Length { get; set; } = 1.0f;

    public Bone Clone() => new()
    {
        Name = Name,
        ParentIndex = ParentIndex,
        BindPosition = BindPosition,
        BindRotation = BindRotation,
        Length = Length
    };
}

