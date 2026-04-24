using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldInterop;
using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldServices;

public sealed class TriggerService
{
    public IReadOnlyList<MhTriggerVolume> LoadTriggers(string upkPath) => WorldInterop_Triggers.ExtractTriggers(upkPath);
}

