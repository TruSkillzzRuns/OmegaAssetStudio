using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OmegaAssetStudio.WinUI.OmegaIntel.Commands;

public sealed class ScanUpkCommand : ICommand
{
    private readonly Func<Task> executeAsync;
    private bool isExecuting;

    public ScanUpkCommand(Func<Task> executeAsync)
    {
        this.executeAsync = executeAsync;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isExecuting;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await executeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

