namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public sealed class ToggleTriggersCommand : WorldAsyncRelayCommand
{
    public ToggleTriggersCommand(Func<Task> execute)
        : base(execute)
    {
    }

    public ToggleTriggersCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        : base(execute, canExecute)
    {
    }
}

