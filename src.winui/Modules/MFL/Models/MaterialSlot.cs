namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class MaterialSlot
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public string MaterialPath { get; set; } = string.Empty;

    public MaterialSlot Clone() => new()
    {
        Index = Index,
        Name = Name,
        MaterialPath = MaterialPath
    };
}

