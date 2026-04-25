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
        set
        {
            if (!SetProperty(ref selectedTrigger, value))
                return;

            OnPropertyChanged(nameof(SelectedTriggerName));
            OnPropertyChanged(nameof(SelectedTriggerType));
            OnPropertyChanged(nameof(SelectedTriggerBoundsText));
            OnPropertyChanged(nameof(SelectedTriggerScriptText));
        }
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string SelectedTriggerName => SelectedTrigger?.Name ?? "No trigger selected.";

    public string SelectedTriggerType => SelectedTrigger?.TriggerType ?? "Type unavailable.";

    public string SelectedTriggerBoundsText => SelectedTrigger?.BoundsText ?? "Bounds unavailable.";

    public string SelectedTriggerScriptText => SelectedTrigger?.ScriptText ?? "Script unavailable.";

    public void LoadTriggers()
    {
        TriggerVolumes.Clear();
        foreach (MhTriggerVolume item in triggerService.LoadTriggers(SourceUpkPath))
            TriggerVolumes.Add(item);
        SelectedTrigger = TriggerVolumes.Count > 0 ? TriggerVolumes[0] : null;
        StatusText = $"Loaded {TriggerVolumes.Count} trigger volume(s).";
    }
}

