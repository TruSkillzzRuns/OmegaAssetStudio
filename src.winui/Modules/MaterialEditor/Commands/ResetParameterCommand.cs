using System;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Commands;

public sealed class ResetParameterCommand : ICommand
{
    private readonly Action<MaterialParameter?> execute;

    public ResetParameterCommand(Action<MaterialParameter?> execute)
    {
        this.execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter as MaterialParameter);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

