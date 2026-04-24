using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;

public static class WorldInterop_Lighting
{
    public static IReadOnlyList<MhLightProbe> ExtractLighting(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return [];

        return [];
    }
}

