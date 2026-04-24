using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldGeometryViewer;

public sealed class WorldGeometryViewerViewModel : WorldToolViewModelBase
{
    private readonly GeometryViewModel geometryViewModel;
    private string statusText = "Ready.";
    private MhWorldGeometry? selectedGeometry;
    private string meshCategoryFilter = "All";

    public WorldGeometryViewerViewModel()
        : this(new GeometryViewModel())
    {
    }

    public WorldGeometryViewerViewModel(GeometryViewModel geometryViewModel)
    {
        this.geometryViewModel = geometryViewModel;
        ShowAllCategories();
    }

    public ObservableCollection<MhWorldGeometry> Geometry => geometryViewModel.Geometry;

    public ObservableCollection<string> CategoryFilters { get; } = new(["All", "Buildings", "Props", "Terrain"]);

    public string SourceUpkPath
    {
        get => geometryViewModel.SourceUpkPath;
        set
        {
            geometryViewModel.SourceUpkPath = value;
            OnPropertyChanged();
        }
    }

    public MhWorldGeometry? SelectedGeometry
    {
        get => selectedGeometry;
        set
        {
            if (!SetProperty(ref selectedGeometry, value))
                return;

            StatusText = selectedGeometry is null
                ? "No geometry selected."
                : $"Selected {selectedGeometry.Name}.";
        }
    }

    public string MeshCategoryFilter
    {
        get => meshCategoryFilter;
        set
        {
            if (!SetProperty(ref meshCategoryFilter, value))
                return;

            ApplyCategoryFilter();
        }
    }

    public string ZoneBoundsText => Geometry.Count == 0
        ? "No zone bounds loaded."
        : Geometry[0].BoundsText ?? "Zone bounds unavailable.";

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public void LoadGeometry()
    {
        geometryViewModel.LoadGeometry();
        SelectedGeometry = Geometry.FirstOrDefault();
        StatusText = $"Loaded {Geometry.Count} geometry item(s).";
        OnPropertyChanged(nameof(ZoneBoundsText));
    }

    public void ShowAllCategories()
    {
        MeshCategoryFilter = "All";
        ApplyCategoryFilter();
    }

    public void ShowBuildingsOnly()
    {
        MeshCategoryFilter = "Buildings";
        ApplyCategoryFilter();
    }

    public void ShowPropsOnly()
    {
        MeshCategoryFilter = "Props";
        ApplyCategoryFilter();
    }

    public void ShowTerrainOnly()
    {
        MeshCategoryFilter = "Terrain";
        ApplyCategoryFilter();
    }

    private void ApplyCategoryFilter()
    {
        foreach (MhWorldGeometry geometry in Geometry)
        {
            geometry.IsBuilding = string.Equals(MeshCategoryFilter, "Buildings", StringComparison.OrdinalIgnoreCase) && string.Equals(geometry.Category, "Buildings", StringComparison.OrdinalIgnoreCase);
            geometry.IsTerrain = string.Equals(MeshCategoryFilter, "Terrain", StringComparison.OrdinalIgnoreCase) && string.Equals(geometry.Category, "Terrain", StringComparison.OrdinalIgnoreCase);
        }

        StatusText = $"Category filter: {MeshCategoryFilter}.";
        OnPropertyChanged(nameof(ZoneBoundsText));
    }
}

