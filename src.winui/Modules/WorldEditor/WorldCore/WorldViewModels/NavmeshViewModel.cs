using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class NavmeshViewModel : WorldToolViewModelBase
{
    private readonly NavmeshService navmeshService;
    private string sourceUpkPath = string.Empty;
    private string statusText = "Ready.";
    private MhNavmeshData? selectedNavmesh;

    public NavmeshViewModel()
        : this(new NavmeshService())
    {
    }

    public NavmeshViewModel(NavmeshService navmeshService)
    {
        this.navmeshService = navmeshService;
    }

    public ObservableCollection<MhNavmeshData> NavmeshLayers { get; } = [];

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public MhNavmeshData? SelectedNavmesh
    {
        get => selectedNavmesh;
        set
        {
            if (!SetProperty(ref selectedNavmesh, value))
                return;

            OnPropertyChanged(nameof(SelectedNavmeshName));
            OnPropertyChanged(nameof(SelectedNavmeshPolygonCount));
            OnPropertyChanged(nameof(SelectedNavmeshBoundsText));
            OnPropertyChanged(nameof(SelectedNavmeshNotes));
        }
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string SelectedNavmeshName => SelectedNavmesh?.Name ?? "No navmesh selected.";

    public string SelectedNavmeshPolygonCount => SelectedNavmesh is null
        ? "Polygon count unavailable."
        : $"Polygons: {SelectedNavmesh.PolygonCount}";

    public string SelectedNavmeshBoundsText => SelectedNavmesh?.BoundsText ?? "Bounds unavailable.";

    public string SelectedNavmeshNotes => SelectedNavmesh?.Notes ?? "Notes unavailable.";

    public void LoadNavmesh()
    {
        NavmeshLayers.Clear();
        foreach (MhNavmeshData item in navmeshService.LoadNavmesh(SourceUpkPath))
            NavmeshLayers.Add(item);
        SelectedNavmesh = NavmeshLayers.Count > 0 ? NavmeshLayers[0] : null;
        StatusText = $"Loaded {NavmeshLayers.Count} navmesh layer(s).";
    }
}

