using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class WorldZoneService
{
    public MhWorldZone CreateZone(string upkPath)
    {
        return new MhWorldZone
        {
            Name = Path.GetFileNameWithoutExtension(upkPath),
            SourceUpkPath = upkPath,
            WorldPath = upkPath
        };
    }
}

