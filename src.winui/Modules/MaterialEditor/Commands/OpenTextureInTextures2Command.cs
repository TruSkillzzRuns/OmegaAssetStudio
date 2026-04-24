using System;
using System.Threading.Tasks;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Commands;

public sealed class OpenTextureInTextures2Command : ICommand
{
    private readonly Func<MaterialTextureSlot?, Task> execute;
    private readonly Func<MaterialTextureSlot?, bool>? canExecute;

    public OpenTextureInTextures2Command(Func<MaterialTextureSlot?, Task> execute, Func<MaterialTextureSlot?, bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter as MaterialTextureSlot) ?? true;

    public async void Execute(object? parameter) => await execute(parameter as MaterialTextureSlot).ConfigureAwait(true);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

