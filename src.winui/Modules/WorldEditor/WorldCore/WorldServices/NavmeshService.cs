using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class NavmeshService
{
    public IReadOnlyList<MhNavmeshData> LoadNavmesh(string upkPath) => WorldInterop_Navmesh.ExtractNavmesh(upkPath);
}

