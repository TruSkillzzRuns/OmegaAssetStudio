using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;

public sealed class ColorVariantService
{
    public IReadOnlyList<MhColorVariantProfile> BuildVariants(IEnumerable<MhColorVariantProfile> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        return variants.ToArray();
    }
}

