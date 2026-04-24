using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class CollisionService
{
    public IReadOnlyList<MhCollisionVolume> LoadCollisionVolumes(string upkPath) => WorldInterop_Collision.ExtractCollisionVolumes(upkPath);
}

