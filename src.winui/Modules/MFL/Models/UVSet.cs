using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class UVSet
{
    public string Name { get; set; } = string.Empty;

    public int ChannelIndex { get; set; }

    public List<Vector2> Coordinates { get; set; } = [];

    public UVSet Clone() => new()
    {
        Name = Name,
        ChannelIndex = ChannelIndex,
        Coordinates = Coordinates.Select(coord => coord).ToList()
    };
}

