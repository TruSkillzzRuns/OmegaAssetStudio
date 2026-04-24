using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;

public sealed class MaterialGraphService
{
    public IReadOnlyList<MhMaterialGraphNode> BuildNodes(IEnumerable<MhMaterialGraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes.ToArray();
    }

    public IReadOnlyList<MhMaterialGraphConnection> BuildConnections(IEnumerable<MhMaterialGraphConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        return connections.ToArray();
    }
}

