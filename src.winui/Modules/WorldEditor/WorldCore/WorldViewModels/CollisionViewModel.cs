using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

public sealed class CollisionViewModel : WorldToolViewModelBase
{
    private readonly CollisionService collisionService;
    private string sourceUpkPath = string.Empty;
    private string statusText = "Ready.";
    private MhCollisionVolume? selectedCollision;

    public CollisionViewModel()
        : this(new CollisionService())
    {
    }

    public CollisionViewModel(CollisionService collisionService)
    {
        this.collisionService = collisionService;
    }

    public ObservableCollection<MhCollisionVolume> CollisionVolumes { get; } = [];

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public MhCollisionVolume? SelectedCollision
    {
        get => selectedCollision;
        set
        {
            if (!SetProperty(ref selectedCollision, value))
                return;

            OnPropertyChanged(nameof(SelectedCollisionName));
            OnPropertyChanged(nameof(SelectedCollisionShapeType));
            OnPropertyChanged(nameof(SelectedCollisionTransformText));
            OnPropertyChanged(nameof(SelectedCollisionBoundsText));
        }
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string SelectedCollisionName => SelectedCollision?.Name ?? "No collision selected.";

    public string SelectedCollisionShapeType => SelectedCollision?.ShapeType ?? "Shape unavailable.";

    public string SelectedCollisionTransformText => SelectedCollision?.TransformText ?? "Transform unavailable.";

    public string SelectedCollisionBoundsText => SelectedCollision?.BoundsText ?? "Bounds unavailable.";

    public void LoadCollisionVolumes()
    {
        CollisionVolumes.Clear();
        foreach (MhCollisionVolume item in collisionService.LoadCollisionVolumes(SourceUpkPath))
            CollisionVolumes.Add(item);
        SelectedCollision = CollisionVolumes.Count > 0 ? CollisionVolumes[0] : null;
        StatusText = $"Loaded {CollisionVolumes.Count} collision volume(s).";
    }
}

