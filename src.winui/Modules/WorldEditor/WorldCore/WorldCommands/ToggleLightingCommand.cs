namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public sealed class ToggleLightingCommand : WorldAsyncRelayCommand
{
    public ToggleLightingCommand(Func<Task> execute)
        : base(execute)
    {
    }

    public ToggleLightingCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        : base(execute, canExecute)
    {
    }
}

