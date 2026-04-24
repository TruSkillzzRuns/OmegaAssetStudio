using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;

public static class WorldInterop_Props
{
    public static IReadOnlyList<MhPropInstance> ExtractProps(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return [];

        return [];
    }
}

