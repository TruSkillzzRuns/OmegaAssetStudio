namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class BoneWeight
{
    public int BoneIndex { get; set; } = -1;

    public string BoneName { get; set; } = string.Empty;

    public float Weight { get; set; }

    public BoneWeight Clone() => new()
    {
        BoneIndex = BoneIndex,
        BoneName = BoneName,
        Weight = Weight
    };
}

