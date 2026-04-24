using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialInterop;

public static class MaterialInterop_Graph
{
    public static (List<MhMaterialGraphNode> Nodes, List<MhMaterialGraphConnection> Connections) FromDefinition(MaterialDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<MhMaterialGraphNode> nodes =
        [
            new MhMaterialGraphNode
            {
                NodeId = "root",
                NodeType = "Material",
                Label = definition.Name
            }
        ];

        List<MhMaterialGraphConnection> connections = [];
        return (nodes, connections);
    }
}

