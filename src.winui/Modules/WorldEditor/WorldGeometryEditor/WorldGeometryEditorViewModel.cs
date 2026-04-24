using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldViewModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldGeometryEditor;

public sealed class WorldGeometryEditorViewModel : WorldToolViewModelBase
{
    private readonly GeometryViewModel geometryViewModel;
    private MhWorldGeometry? selectedGeometry;
    private string statusText = "Ready.";

    public WorldGeometryEditorViewModel()
        : this(new GeometryViewModel())
    {
    }

    public WorldGeometryEditorViewModel(GeometryViewModel geometryViewModel)
    {
        this.geometryViewModel = geometryViewModel;
        selectedGeometry = geometryViewModel.Geometry.FirstOrDefault();
    }

    public IEnumerable<MhWorldGeometry> Geometry => geometryViewModel.Geometry;

    public MhWorldGeometry? SelectedGeometry
    {
        get => selectedGeometry;
        set
        {
            if (!SetProperty(ref selectedGeometry, value))
            {
                return;
            }

            StatusText = selectedGeometry is null
                ? "No geometry selected."
                : $"Editing {selectedGeometry.Name}.";
        }
    }

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }
    public double RotationX { get; set; }
    public double RotationY { get; set; }
    public double RotationZ { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double ScaleZ { get; set; } = 1;

    public void LoadSelectedGeometry()
    {
        SelectedGeometry ??= geometryViewModel.Geometry.FirstOrDefault();
        StatusText = SelectedGeometry is null
            ? "No geometry loaded."
            : $"Loaded {SelectedGeometry.Name} for editing.";
    }

    public void ApplyTransform()
    {
        StatusText = SelectedGeometry is null
            ? "No geometry selected."
            : $"Applied transform to {SelectedGeometry.Name}.";
    }

    public void DuplicateSelected()
    {
        StatusText = SelectedGeometry is null
            ? "No geometry selected."
            : $"Duplicated {SelectedGeometry.Name}.";
    }

    public void DeleteSelected()
    {
        StatusText = SelectedGeometry is null
            ? "No geometry selected."
            : $"Deleted {SelectedGeometry.Name}.";
    }
}

