using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Commands;

public sealed class SaveMaterialCommand : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool>? canExecute;

    public SaveMaterialCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter) => await execute().ConfigureAwait(true);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

