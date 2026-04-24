using System.Windows.Input;

namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public class WorldAsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> execute;
    private readonly Predicate<object?>? canExecute;
    private bool isExecuting;

    public WorldAsyncRelayCommand(Func<Task> execute)
        : this(_ => execute(), null)
    {
    }

    public WorldAsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isExecuting && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute(parameter).ConfigureAwait(true);
        }
        finally
        {
            isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

