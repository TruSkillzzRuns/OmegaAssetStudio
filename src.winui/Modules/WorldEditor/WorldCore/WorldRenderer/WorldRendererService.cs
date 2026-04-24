namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldRenderer;

public sealed class WorldRendererService
{
    public WorldRendererState State { get; } = new();

    public void SetActiveZone(object? zone)
    {
        State.ActiveZone = zone as OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldModels.MhWorldZone;
    }
}

