using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class PropService
{
    public IReadOnlyList<MhPropInstance> LoadProps(string upkPath) => WorldInterop_Props.ExtractProps(upkPath);
}

