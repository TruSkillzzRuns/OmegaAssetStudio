namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public sealed class ToggleCollisionCommand : WorldAsyncRelayCommand
{
    public ToggleCollisionCommand(Func<Task> execute)
        : base(execute)
    {
    }

    public ToggleCollisionCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        : base(execute, canExecute)
    {
    }
}

