using System.Collections.Generic;
using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;

public sealed class NeutralMaterial
{
    public string Name { get; set; } = string.Empty;
    public List<string> ReferencedTextures { get; } = [];
    public Dictionary<string, string> TextParameters { get; } = [];
    public Dictionary<string, float> ScalarParameters { get; } = [];
    public Dictionary<string, Vector3> VectorParameters { get; } = [];
    public string? DiffuseDescription { get; set; }
    public string? SpecularDescription { get; set; }
    public string? NormalDescription { get; set; }
    public string? Metadata { get; set; }
}

