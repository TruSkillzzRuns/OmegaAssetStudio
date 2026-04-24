namespace OmegaAssetStudio.WinUI.Modules.Workflows;

public static class WorkflowsService
{
    private static WorkflowsView? workflowsWindow;

    public static void OpenWorkflowsWindow()
    {
        if (workflowsWindow is null)
        {
            workflowsWindow = new WorkflowsView();
            workflowsWindow.Closed += (_, _) => workflowsWindow = null;
        }

        workflowsWindow.Activate();
    }
}

