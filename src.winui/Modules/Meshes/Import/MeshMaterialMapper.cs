using OmegaAssetStudio.Unreal.SkeletalMesh;

namespace OmegaAssetStudio.WinUI.Modules.Meshes.Import;

public class MeshMaterialMapper
{
    public int Resolve(SkeletalMesh skeletalMesh, FBXSectionData sectionData)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(sectionData);
        return Resolve(skeletalMesh, sectionData.MaterialName);
    }

    public int Resolve(SkeletalMesh skeletalMesh, string materialName)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        materialName = NormalizeMaterialName(materialName);

        int index = FindMaterialIndex(skeletalMesh, materialName);
        if (index >= 0)
            return index;

        skeletalMesh.Materials.Add(materialName);
        return skeletalMesh.Materials.Count - 1;
    }

    public IReadOnlyList<int> ResolveAll(SkeletalMesh skeletalMesh, IEnumerable<FBXSectionData> sections)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(sections);

        List<int> resolved = [];
        foreach (FBXSectionData section in sections)
            resolved.Add(Resolve(skeletalMesh, section));

        return resolved;
    }

    public int FindMaterialIndex(SkeletalMesh skeletalMesh, string materialName)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        materialName = NormalizeMaterialName(materialName);

        for (int i = 0; i < skeletalMesh.Materials.Count; i++)
        {
            if (string.Equals(NormalizeMaterialName(skeletalMesh.Materials[i]), materialName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string NormalizeMaterialName(string? materialName)
    {
        return string.IsNullOrWhiteSpace(materialName) ? "Material" : materialName.Trim();
    }
}

