using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class MinimapService
{
    public IReadOnlyList<MhMinimapData> LoadMinimap(string upkPath) => WorldInterop_Minimap.ExtractMinimap(upkPath);
}

