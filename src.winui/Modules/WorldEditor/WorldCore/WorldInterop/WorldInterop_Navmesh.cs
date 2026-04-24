using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;

public static class WorldInterop_Navmesh
{
    public static IReadOnlyList<MhNavmeshData> ExtractNavmesh(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return [];

        return [];
    }
}

