using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialGraphEditor;

public sealed class MaterialGraphEditorViewModel : MaterialToolViewModelBase
{
    private MhMaterialGraphNode? selectedNode;

    public MaterialGraphEditorViewModel()
    {
        Title = "Material Graph Editor";
        Nodes = [];
        Connections = [];
    }

    public ObservableCollection<MhMaterialGraphNode> Nodes { get; }

    public ObservableCollection<MhMaterialGraphConnection> Connections { get; }

    public MhMaterialGraphNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (!SetProperty(ref selectedNode, value))
                return;

            RaisePropertyChanged(nameof(SelectedNodeSummaryText));
        }
    }

    public string NodeCountText => $"{Nodes.Count:N0} node(s)";
    public string ConnectionCountText => $"{Connections.Count:N0} connection(s)";
    public string SelectedNodeSummaryText => SelectedNode is null
        ? "No material graph node selected."
        : $"Node: {SelectedNode.Label} | Type: {SelectedNode.NodeType}";

    public void LoadMaterial(MhMaterialInstance? material)
    {
        Nodes.Clear();
        Connections.Clear();
        if (material is null)
        {
            StatusText = "No material selected.";
            return;
        }

        foreach (MhMaterialGraphNode node in material.GraphNodes)
            Nodes.Add(node);

        foreach (MhMaterialGraphConnection connection in material.GraphConnections)
            Connections.Add(connection);
        SelectedNode = Nodes.FirstOrDefault();
        StatusText = $"{Nodes.Count:N0} node(s), {Connections.Count:N0} connection(s).";
        RaisePropertyChanged(nameof(NodeCountText));
        RaisePropertyChanged(nameof(ConnectionCountText));
        RaisePropertyChanged(nameof(SelectedNodeSummaryText));
    }
}

