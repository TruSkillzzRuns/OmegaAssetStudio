using OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldRenderer;

public sealed class WorldRendererState
{
    public MhWorldZone? ActiveZone { get; set; }

    public bool ShowCollisionOverlay { get; set; }

    public bool ShowNavmeshOverlay { get; set; }

    public bool ShowLightingOverlay { get; set; }

    public bool ShowTriggersOverlay { get; set; }

    public bool ShowMinimapOverlay { get; set; }
}

