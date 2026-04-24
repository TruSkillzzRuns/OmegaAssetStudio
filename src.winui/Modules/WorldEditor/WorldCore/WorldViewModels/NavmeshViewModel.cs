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
        set => SetProperty(ref selectedNavmesh, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public void LoadNavmesh()
    {
        NavmeshLayers.Clear();
        foreach (MhNavmeshData item in navmeshService.LoadNavmesh(SourceUpkPath))
            NavmeshLayers.Add(item);
        StatusText = $"Loaded {NavmeshLayers.Count} navmesh layer(s).";
    }
}

