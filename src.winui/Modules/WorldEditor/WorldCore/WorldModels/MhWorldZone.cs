using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

public sealed class MhWorldZone : WorldToolViewModelBase
{
    private string name = string.Empty;
    private string? sourceUpkPath;
    private string? worldPath;
    private string? zoneBoundsText;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string? SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public string? WorldPath
    {
        get => worldPath;
        set => SetProperty(ref worldPath, value);
    }

    public string? ZoneBoundsText
    {
        get => zoneBoundsText;
        set => SetProperty(ref zoneBoundsText, value);
    }

    public ObservableCollection<MhWorldGeometry> Geometry { get; } = [];

    public ObservableCollection<MhPropInstance> Props { get; } = [];

    public ObservableCollection<MhCollisionVolume> CollisionVolumes { get; } = [];

    public ObservableCollection<MhNavmeshData> NavmeshLayers { get; } = [];

    public ObservableCollection<MhLightProbe> LightProbes { get; } = [];

    public ObservableCollection<MhTriggerVolume> TriggerVolumes { get; } = [];

    public ObservableCollection<MhMinimapData> MinimapLayers { get; } = [];
}

