using System.Collections.Generic;
using System.Linq;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

public sealed class MaterialDefinition : NotifyPropertyChangedBase
{
    private string name = string.Empty;
    private string path = string.Empty;
    private string sourceUpkPath = string.Empty;
    private string sourceMeshExportPath = string.Empty;
    private string type = string.Empty;
    private List<MaterialTextureSlot> textureSlots = new();
    private List<MaterialParameter> scalarParameters = new();
    private List<MaterialParameter> vectorParameters = new();

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string Path
    {
        get => path;
        set => SetProperty(ref path, value);
    }

    public string SourceUpkPath
    {
        get => sourceUpkPath;
        set => SetProperty(ref sourceUpkPath, value);
    }

    public string SourceMeshExportPath
    {
        get => sourceMeshExportPath;
        set => SetProperty(ref sourceMeshExportPath, value);
    }

    public string Type
    {
        get => type;
        set => SetProperty(ref type, value);
    }

    public List<MaterialTextureSlot> TextureSlots
    {
        get => textureSlots;
        set => SetProperty(ref textureSlots, value);
    }

    public List<MaterialParameter> ScalarParameters
    {
        get => scalarParameters;
        set => SetProperty(ref scalarParameters, value);
    }

    public List<MaterialParameter> VectorParameters
    {
        get => vectorParameters;
        set => SetProperty(ref vectorParameters, value);
    }

    public MaterialDefinition Clone()
    {
        return new MaterialDefinition
        {
            Name = Name,
            Path = Path,
            SourceUpkPath = SourceUpkPath,
            SourceMeshExportPath = SourceMeshExportPath,
            Type = Type,
            TextureSlots = TextureSlots.Select(slot => slot.Clone()).ToList(),
            ScalarParameters = ScalarParameters.Select(parameter => parameter.Clone()).ToList(),
            VectorParameters = VectorParameters.Select(parameter => parameter.Clone()).ToList()
        };
    }

    public void CopyFrom(MaterialDefinition source)
    {
        Name = source.Name;
        Path = source.Path;
        SourceUpkPath = source.SourceUpkPath;
        SourceMeshExportPath = source.SourceMeshExportPath;
        Type = source.Type;
        TextureSlots = source.TextureSlots.Select(slot => slot.Clone()).ToList();
        ScalarParameters = source.ScalarParameters.Select(parameter => parameter.Clone()).ToList();
        VectorParameters = source.VectorParameters.Select(parameter => parameter.Clone()).ToList();
    }
}

