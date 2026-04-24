using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class LightingViewModel : WorldToolViewModelBase
{
    private readonly LightingService lightingService;
    private string sourceUpkPath = string.Empty;
    private string statusText = "Ready.";
    private MhLightProbe? selectedLightProbe;

    public LightingViewModel()
        : this(new LightingService())
    {
    }

    public LightingViewModel(LightingService lightingService)
    {
        this.lightingService = lightingService;
    }

    public ObservableCollection<MhLightProbe> LightProbes { get; } = [];

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public MhLightProbe? SelectedLightProbe
    {
        get => selectedLightProbe;
        set => SetProperty(ref selectedLightProbe, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public void LoadLighting()
    {
        LightProbes.Clear();
        foreach (MhLightProbe item in lightingService.LoadLighting(SourceUpkPath))
            LightProbes.Add(item);
        StatusText = $"Loaded {LightProbes.Count} light probe(s).";
    }
}

