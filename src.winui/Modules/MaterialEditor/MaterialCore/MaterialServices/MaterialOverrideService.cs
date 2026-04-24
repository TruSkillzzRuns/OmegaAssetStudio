using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;

public sealed class MaterialOverrideService
{
    public IReadOnlyList<MhMaterialOverrideProfile> BuildProfiles(IEnumerable<MhMaterialOverrideProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return profiles.ToArray();
    }
}

