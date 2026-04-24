namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public sealed class LoadZoneCommand : WorldAsyncRelayCommand
{
    public LoadZoneCommand(Func<Task> execute)
        : base(execute)
    {
    }

    public LoadZoneCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        : base(execute, canExecute)
    {
    }
}

