using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Rendering;

public sealed class MeshNode : INotifyPropertyChanged
{
    private string name = string.Empty;
    private Mesh? mesh;
    private MeshPreviewMesh? previewMesh;
    private MeshPreviewMesh? basePreviewMesh;
    private Vector3 position = Vector3.Zero;
    private Vector3 rotationDegrees = Vector3.Zero;
    private Vector3 scale = Vector3.One;
    private bool isVisible = true;
    private bool isWireframe;
    private bool isHighlighted;
    private bool isGhosted;
    private int selectedTriangleIndex = -1;
    private int highlightedSectionIndex = -1;
    private List<int> highlightedTriangleIndices = [];

    public MeshNode(string name)
    {
        this.name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public Mesh? Mesh
    {
        get => mesh;
        set => SetField(ref mesh, value);
    }

    internal MeshPreviewMesh? PreviewMesh
    {
        get => previewMesh;
        set => SetField(ref previewMesh, value);
    }

    internal MeshPreviewMesh? BasePreviewMesh
    {
        get => basePreviewMesh;
        set => SetField(ref basePreviewMesh, value);
    }

    public Vector3 Position
    {
        get => position;
        set
        {
            if (SetField(ref position, value))
                OnPropertyChanged(nameof(WorldTransform));
        }
    }

    public Vector3 RotationDegrees
    {
        get => rotationDegrees;
        set
        {
            if (SetField(ref rotationDegrees, value))
                OnPropertyChanged(nameof(WorldTransform));
        }
    }

    public Vector3 Scale
    {
        get => scale;
        set
        {
            if (SetField(ref scale, value))
                OnPropertyChanged(nameof(WorldTransform));
        }
    }

    public bool IsVisible
    {
        get => isVisible;
        set => SetField(ref isVisible, value);
    }

    public bool IsWireframe
    {
        get => isWireframe;
        set => SetField(ref isWireframe, value);
    }

    public bool IsHighlighted
    {
        get => isHighlighted;
        set => SetField(ref isHighlighted, value);
    }

    public bool IsGhosted
    {
        get => isGhosted;
        set => SetField(ref isGhosted, value);
    }

    public int SelectedTriangleIndex
    {
        get => selectedTriangleIndex;
        set => SetField(ref selectedTriangleIndex, value);
    }

    public int HighlightedSectionIndex
    {
        get => highlightedSectionIndex;
        set => SetField(ref highlightedSectionIndex, value);
    }

    public List<int> HighlightedTriangleIndices
    {
        get => highlightedTriangleIndices;
        set => SetField(ref highlightedTriangleIndices, value);
    }

    public Matrix4x4 WorldTransform
    {
        get
        {
            Vector3 radians = RotationDegrees * (MathF.PI / 180.0f);
            Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(Scale);
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
            Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(Position);
            return scaleMatrix * rotationMatrix * translationMatrix;
        }
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

