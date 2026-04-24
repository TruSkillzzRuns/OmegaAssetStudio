using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

public sealed class ColorVariantViewModel : MaterialToolViewModelBase
{
    private MhMaterialInstance? selectedMaterial;
    private MhColorVariantProfile? selectedVariant;

    public ColorVariantViewModel()
    {
        Title = "Color Variant Generator";
        Variants = [];
    }

    public ObservableCollection<MhColorVariantProfile> Variants { get; }

    public MhMaterialInstance? SelectedMaterial
    {
        get => selectedMaterial;
        set
        {
            if (!SetProperty(ref selectedMaterial, value))
                return;

            Variants.Clear();
            if (selectedMaterial is null)
                return;

            foreach (MhColorVariantProfile variant in selectedMaterial.ColorVariants)
                Variants.Add(variant);
        }
    }

    public MhColorVariantProfile? SelectedVariant
    {
        get => selectedVariant;
        set => SetProperty(ref selectedVariant, value);
    }
}

