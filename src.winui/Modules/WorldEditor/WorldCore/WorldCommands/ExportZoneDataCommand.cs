namespace OmegaAssetStudio.WinUI.Modules.WorldEditor.WorldCore.WorldCommands;

public sealed class ExportZoneDataCommand : WorldAsyncRelayCommand
{
    public ExportZoneDataCommand(Func<Task> execute)
        : base(execute)
    {
    }

    public ExportZoneDataCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        : base(execute, canExecute)
    {
    }
}

