using System.ComponentModel;
using System.Runtime.CompilerServices;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;
using OmegaAssetStudio.WinUI.Modules.MFL.Scene;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Viewport;

public sealed class Scene : INotifyPropertyChanged
{
    private string activeMeshKey = "MeshA";
    private bool ghostInactiveMesh = true;

    public Scene()
    {
        UpdateNodeStates();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MeshNode MeshNodeA { get; } = new("Mesh A");

    public MeshNode MeshNodeB { get; } = new("Mesh B");

    public string ActiveMeshKey
    {
        get => activeMeshKey;
        set
        {
            string normalized = NormalizeKey(value);
            if (SetField(ref activeMeshKey, normalized))
            {
                UpdateNodeStates();
                OnPropertyChanged(nameof(ActiveNode));
                OnPropertyChanged(nameof(PassiveNode));
            }
        }
    }

    public bool GhostInactiveMesh
    {
        get => ghostInactiveMesh;
        set
        {
            if (SetField(ref ghostInactiveMesh, value))
                UpdateNodeStates();
        }
    }

    public MeshNode ActiveNode => GetNode(ActiveMeshKey) ?? MeshNodeA;

    public MeshNode PassiveNode => ActiveNode == MeshNodeA ? MeshNodeB : MeshNodeA;

    public IEnumerable<MeshNode> Nodes => [MeshNodeA, MeshNodeB];

    public void AttachMeshes(Mesh? meshA, Mesh? meshB)
    {
        MeshNodeA.Mesh = meshA;
        MeshNodeB.Mesh = meshB;
        MeshNodeA.Name = meshA?.Name is { Length: > 0 } meshAName ? meshAName : "Mesh A";
        MeshNodeB.Name = meshB?.Name is { Length: > 0 } meshBName ? meshBName : "Mesh B";
        UpdateNodeStates();
    }

    public MeshNode? GetNode(string meshKey)
    {
        return NormalizeKey(meshKey) switch
        {
            "MeshB" => MeshNodeB,
            _ => MeshNodeA
        };
    }

    public void SetActiveMesh(string meshKey)
    {
        ActiveMeshKey = meshKey;
    }

    public void SetHighlightedTriangles(string meshKey, IReadOnlyCollection<int>? triangleIndices, int selectedTriangleIndex)
    {
        MeshNode active = GetNode(meshKey) ?? MeshNodeA;
        MeshNode passive = active == MeshNodeA ? MeshNodeB : MeshNodeA;

        active.HighlightedTriangleIndices = triangleIndices?.ToList() ?? [];
        active.SelectedTriangleIndex = selectedTriangleIndex;
        passive.HighlightedTriangleIndices = [];
        passive.SelectedTriangleIndex = -1;
    }

    private void UpdateNodeStates()
    {
        bool activeIsA = string.Equals(ActiveMeshKey, "MeshA", StringComparison.OrdinalIgnoreCase);
        MeshNodeA.IsHighlighted = activeIsA;
        MeshNodeB.IsHighlighted = !activeIsA;
        MeshNodeA.IsGhosted = GhostInactiveMesh && !activeIsA && MeshNodeA.IsVisible;
        MeshNodeB.IsGhosted = GhostInactiveMesh && activeIsA && MeshNodeB.IsVisible;
    }

    private static string NormalizeKey(string? meshKey)
    {
        return string.Equals(meshKey, "MeshB", StringComparison.OrdinalIgnoreCase) ? "MeshB" : "MeshA";
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

