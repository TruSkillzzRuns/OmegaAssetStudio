using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialInterop;

public static class MaterialInterop_ColorVariants
{
    public static IReadOnlyList<MhColorVariantProfile> FromDefinition(MaterialDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return [new MhColorVariantProfile { Name = $"{definition.Name} Color Variant" }];
    }
}

