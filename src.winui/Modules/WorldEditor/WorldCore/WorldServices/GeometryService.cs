using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class GeometryService
{
    public IReadOnlyList<MhWorldGeometry> LoadGeometry(string upkPath) => WorldInterop_Geometry.ExtractGeometry(upkPath);
}

