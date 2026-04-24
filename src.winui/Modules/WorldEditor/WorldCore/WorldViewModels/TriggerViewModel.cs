using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class TriggerViewModel : WorldToolViewModelBase
{
    private readonly TriggerService triggerService;
    private string sourceUpkPath = string.Empty;
    private string statusText = "Ready.";
    private MhTriggerVolume? selectedTrigger;

    public TriggerViewModel()
        : this(new TriggerService())
    {
    }

    public TriggerViewModel(TriggerService triggerService)
    {
        this.triggerService = triggerService;
    }

    public ObservableCollection<MhTriggerVolume> TriggerVolumes { get; } = [];

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public MhTriggerVolume? SelectedTrigger
    {
        get => selectedTrigger;
        set => SetProperty(ref selectedTrigger, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public void LoadTriggers()
    {
        TriggerVolumes.Clear();
        foreach (MhTriggerVolume item in triggerService.LoadTriggers(SourceUpkPath))
            TriggerVolumes.Add(item);
        StatusText = $"Loaded {TriggerVolumes.Count} trigger volume(s).";
    }
}

