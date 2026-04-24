using System;
using System.Threading.Tasks;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.OmegaIntel.Commands;

namespace OmegaAssetStudio.WinUI.OmegaIntel;

public sealed class OmegaIntelViewModel
{
    public OmegaIntelViewModel()
    {
        ScanUpkCommand = new ScanUpkCommand(async () =>
        {
            if (ScanUpkRequestedAsync is not null)
                await ScanUpkRequestedAsync().ConfigureAwait(true);
        });
    }

    public ICommand ScanUpkCommand { get; }

    public Func<Task>? ScanUpkRequestedAsync { get; set; }
}

