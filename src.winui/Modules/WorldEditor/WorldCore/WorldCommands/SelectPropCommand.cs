namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public sealed class SelectPropCommand : WorldAsyncRelayCommand
{
    public SelectPropCommand(Func<Task> execute)
        : base(execute)
    {
    }

    public SelectPropCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        : base(execute, canExecute)
    {
    }
}

