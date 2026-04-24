using System.Collections.Generic;
using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;

public sealed class NeutralAnimation
{
    public string Name { get; set; } = string.Empty;
    public float LengthSeconds { get; set; }
    public int FrameCount { get; set; }
    public List<NeutralAnimationTrack> Tracks { get; } = [];
}

public sealed class NeutralAnimationTrack
{
    public string BoneName { get; set; } = string.Empty;
    public List<NeutralAnimationFrame> Frames { get; } = [];
}

public sealed record NeutralAnimationFrame(float TimeSeconds, Vector3 Position, Quaternion Rotation);

