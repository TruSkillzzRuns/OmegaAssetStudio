using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ColorVariantGenerator;

public sealed class ColorVariantGeneratorViewModel : MaterialToolViewModelBase
{
    private MhMaterialInstance? selectedMaterial;
    private MhColorVariantProfile? selectedVariant;

    public ColorVariantGeneratorViewModel()
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

            SelectedVariant = Variants.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedMaterialSummaryText));
        }
    }

    public MhColorVariantProfile? SelectedVariant
    {
        get => selectedVariant;
        set
        {
            if (!SetProperty(ref selectedVariant, value))
                return;

            RaisePropertyChanged(nameof(SelectedVariantSummaryText));
        }
    }

    public string VariantCountText => $"{Variants.Count:N0} variant(s)";
    public string SelectedMaterialSummaryText => SelectedMaterial is null
        ? "No material selected."
        : $"Material: {SelectedMaterial.Name}";
    public string SelectedVariantSummaryText => SelectedVariant is null
        ? "No color variant selected."
        : $"Variant: {SelectedVariant.Name} | Primary: {SelectedVariant.PrimaryColor}";
}

