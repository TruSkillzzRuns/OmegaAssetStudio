using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class GeometryViewModel : WorldToolViewModelBase
{
    private readonly GeometryService geometryService;
    private string sourceUpkPath = string.Empty;
    private string statusText = "Ready.";
    private MhWorldGeometry? selectedGeometry;

    public GeometryViewModel()
        : this(new GeometryService())
    {
    }

    public GeometryViewModel(GeometryService geometryService)
    {
        this.geometryService = geometryService;
    }

    public ObservableCollection<MhWorldGeometry> Geometry { get; } = [];

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public MhWorldGeometry? SelectedGeometry
    {
        get => selectedGeometry;
        set => SetProperty(ref selectedGeometry, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public void LoadGeometry()
    {
        Geometry.Clear();
        foreach (MhWorldGeometry item in geometryService.LoadGeometry(SourceUpkPath))
            Geometry.Add(item);
        StatusText = $"Loaded {Geometry.Count} geometry item(s).";
    }
}

