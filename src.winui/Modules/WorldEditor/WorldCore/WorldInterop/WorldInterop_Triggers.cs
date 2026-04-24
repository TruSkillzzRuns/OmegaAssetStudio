using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;

public static class WorldInterop_Triggers
{
    public static IReadOnlyList<MhTriggerVolume> ExtractTriggers(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return [];

        return [];
    }
}

