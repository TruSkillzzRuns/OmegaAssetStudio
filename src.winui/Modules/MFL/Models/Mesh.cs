namespace OmegaAssetStudio.WinUI.Modules.MFL.Models;

public sealed class Mesh
{
    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public List<Vertex> Vertices { get; set; } = [];

    public List<Triangle> Triangles { get; set; } = [];

    public List<Bone> Bones { get; set; } = [];

    public List<MaterialSlot> MaterialSlots { get; set; } = [];

    public List<UVSet> UVSets { get; set; } = [];

    public List<LODGroup> LODGroups { get; set; } = [];

    public List<Socket> Sockets { get; set; } = [];

    public BoundingBox Bounds { get; set; } = BoundingBox.Empty;

    public Mesh Clone()
    {
        return new Mesh
        {
            Name = Name,
            SourcePath = SourcePath,
            Vertices = Vertices.Select(vertex => vertex.Clone()).ToList(),
            Triangles = Triangles.Select(triangle => triangle.Clone()).ToList(),
            Bones = Bones.Select(bone => bone.Clone()).ToList(),
            MaterialSlots = MaterialSlots.Select(slot => slot.Clone()).ToList(),
            UVSets = UVSets.Select(set => set.Clone()).ToList(),
            LODGroups = LODGroups.Select(group => group.Clone()).ToList(),
            Sockets = Sockets.Select(socket => socket.Clone()).ToList(),
            Bounds = Bounds.Clone()
        };
    }

    public void RecalculateBounds()
    {
        Bounds = BoundingBox.Empty;
        foreach (Vertex vertex in Vertices)
        {
            Bounds.Include(vertex.Position);
        }
    }
}

