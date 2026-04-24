using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class LightingService
{
    public IReadOnlyList<MhLightProbe> LoadLighting(string upkPath) => WorldInterop_Lighting.ExtractLighting(upkPath);
}

