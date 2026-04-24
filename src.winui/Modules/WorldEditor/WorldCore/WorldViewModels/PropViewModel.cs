using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class PropViewModel : WorldToolViewModelBase
{
    private readonly PropService propService;
    private string sourceUpkPath = string.Empty;
    private string statusText = "Ready.";
    private MhPropInstance? selectedProp;

    public PropViewModel()
        : this(new PropService())
    {
    }

    public PropViewModel(PropService propService)
    {
        this.propService = propService;
    }

    public ObservableCollection<MhPropInstance> Props { get; } = [];

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public MhPropInstance? SelectedProp
    {
        get => selectedProp;
        set
        {
            if (!SetProperty(ref selectedProp, value))
                return;

            OnPropertyChanged(nameof(SelectedPropName));
            OnPropertyChanged(nameof(SelectedPropPath));
            OnPropertyChanged(nameof(SelectedPropTransform));
            OnPropertyChanged(nameof(SelectedPropCategory));
        }
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string SelectedPropName => SelectedProp?.Name ?? "No prop selected.";

    public string SelectedPropPath => SelectedProp?.PropPath ?? "Path unavailable.";

    public string SelectedPropTransform => SelectedProp?.TransformText ?? "Transform unavailable.";

    public string SelectedPropCategory => SelectedProp?.Category ?? "Category unavailable.";

    public void LoadProps()
    {
        Props.Clear();
        foreach (MhPropInstance item in propService.LoadProps(SourceUpkPath))
            Props.Add(item);
        StatusText = $"Loaded {Props.Count} prop(s).";
    }
}

