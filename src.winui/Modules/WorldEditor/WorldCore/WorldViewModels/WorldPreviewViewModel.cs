using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class WorldPreviewViewModel : WorldToolViewModelBase
{
    private readonly WorldZoneService worldZoneService;
    private MhWorldZone? selectedZone;
    private string selectedZoneSummary = "Select a zone to inspect details.";
    private string statusText = "Ready.";

    public WorldPreviewViewModel()
        : this(new WorldZoneService())
    {
    }

    public WorldPreviewViewModel(WorldZoneService worldZoneService)
    {
        this.worldZoneService = worldZoneService;
    }

    public ObservableCollection<MhWorldZone> Zones { get; } = [];

    public MhWorldZone? SelectedZone
    {
        get => selectedZone;
        set
        {
            if (SetProperty(ref selectedZone, value))
            {
                SelectedZoneSummary = value is null
                    ? "Select a zone to inspect details."
                    : $"Name: {value.Name}\nSource: {value.SourceUpkPath}\nWorld Path: {value.WorldPath}";
            }
        }
    }

    public string SelectedZoneSummary
    {
        get => selectedZoneSummary;
        set => SetProperty(ref selectedZoneSummary, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public void LoadZone(string upkPath)
    {
        MhWorldZone zone = worldZoneService.CreateZone(upkPath);
        Zones.Clear();
        Zones.Add(zone);
        SelectedZone = zone;
        StatusText = $"Loaded {zone.Name}.";
    }
}

