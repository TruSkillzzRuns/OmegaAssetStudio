using System;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Commands;

public sealed class SelectTextureSlotCommand : ICommand
{
    private readonly Action<MaterialTextureSlot?> execute;

    public SelectTextureSlotCommand(Action<MaterialTextureSlot?> execute)
    {
        this.execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter as MaterialTextureSlot);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

