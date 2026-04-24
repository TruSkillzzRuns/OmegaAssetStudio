namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public sealed class ToggleNavmeshCommand : WorldAsyncRelayCommand
{
    public ToggleNavmeshCommand(Func<Task> execute)
        : base(execute)
    {
    }

    public ToggleNavmeshCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        : base(execute, canExecute)
    {
    }
}

