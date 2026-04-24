namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class Triangle
{
    public int A { get; set; }

    public int B { get; set; }

    public int C { get; set; }

    public int MaterialSlotIndex { get; set; }

    public int SectionIndex { get; set; }

    public int LodIndex { get; set; }

    public Triangle Clone() => new()
    {
        A = A,
        B = B,
        C = C,
        MaterialSlotIndex = MaterialSlotIndex,
        SectionIndex = SectionIndex,
        LodIndex = LodIndex
    };
}

