using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;

public static class WorldInterop_Collision
{
    public static IReadOnlyList<MhCollisionVolume> ExtractCollisionVolumes(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return [];

        return [];
    }
}

