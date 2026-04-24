using Microsoft.UI.Dispatching;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Rendering;

internal sealed class D3D11DeviceManager : IDisposable
{
    private DispatcherQueueController? controller;

    public DispatcherQueue? DispatcherQueue { get; private set; }

    public void EnsureStarted()
    {
        if (controller is not null)
            return;

        controller = DispatcherQueueController.CreateOnDedicatedThread();
        DispatcherQueue = controller.DispatcherQueue;
    }

    public void Dispose()
    {
        DispatcherQueue = null;
        controller = null;
    }
}

