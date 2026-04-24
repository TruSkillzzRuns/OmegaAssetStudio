using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class MinimapViewModel : WorldToolViewModelBase
{
    private readonly MinimapService minimapService;
    private string sourceUpkPath = string.Empty;
    private string statusText = "Ready.";
    private MhMinimapData? selectedMinimap;

    public MinimapViewModel()
        : this(new MinimapService())
    {
    }

    public MinimapViewModel(MinimapService minimapService)
    {
        this.minimapService = minimapService;
    }

    public ObservableCollection<MhMinimapData> MinimapLayers { get; } = [];

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public MhMinimapData? SelectedMinimap
    {
        get => selectedMinimap;
        set => SetProperty(ref selectedMinimap, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public void LoadMinimap()
    {
        MinimapLayers.Clear();
        foreach (MhMinimapData item in minimapService.LoadMinimap(SourceUpkPath))
            MinimapLayers.Add(item);
        StatusText = $"Loaded {MinimapLayers.Count} minimap layer(s).";
    }
}

