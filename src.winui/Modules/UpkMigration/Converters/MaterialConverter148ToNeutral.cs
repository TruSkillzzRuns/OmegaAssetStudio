using OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;
using UpkManager.Models.UpkFile.Engine.Material;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Converters;

public sealed class MaterialConverter148ToNeutral
{
    public IReadOnlyList<NeutralMaterial> Convert(Upk148ExportTableEntry entry, Action<string>? log = null)
    {
        if (UpkExportHydrator.TryHydrate(entry, out UMaterial? material, log, false) && material is not null)
            return [ConvertMaterial(entry, material, log)];

        if (UpkExportHydrator.TryHydrate(entry, out UMaterialInstanceConstant? constant, log, false) && constant is not null)
            return [ConvertConstant(entry, constant, log)];

        if (UpkExportHydrator.TryHydrate(entry, out UMaterialInstance? instance, log, false) && instance is not null)
            return [ConvertInstance(entry, instance, log)];

        return [];
    }

    private static NeutralMaterial ConvertMaterial(Upk148ExportTableEntry entry, UMaterial material, Action<string>? log)
    {
        NeutralMaterial neutral = new()
        {
            Name = string.IsNullOrWhiteSpace(entry.ObjectName) ? entry.PathName : entry.ObjectName,
            DiffuseDescription = material.DiffuseColor?.ToString(),
            NormalDescription = material.Normal?.ToString(),
            SpecularDescription = material.EmissiveColor?.ToString(),
            Metadata = $"OpacityMask={material.OpacityMaskClipValue}"
        };

        if (material.Expressions is not null)
        {
            foreach (object expression in material.Expressions)
            {
                if (expression is null)
                    continue;

                neutral.TextParameters[expression.GetType().Name] = expression.ToString() ?? string.Empty;
            }
        }

        log?.Invoke($"Converted material {entry.PathName}.");
        return neutral;
    }

    private static NeutralMaterial ConvertConstant(Upk148ExportTableEntry entry, UMaterialInstanceConstant constant, Action<string>? log)
    {
        NeutralMaterial neutral = new()
        {
            Name = string.IsNullOrWhiteSpace(entry.ObjectName) ? entry.PathName : entry.ObjectName,
            Metadata = "MaterialInstanceConstant"
        };

        if (constant.TextureParameterValues is not null)
        {
            foreach (FTextureParameterValue value in constant.TextureParameterValues)
            {
                if (value?.ParameterName is null || string.IsNullOrWhiteSpace(value.ParameterName.Name))
                    continue;

                neutral.ReferencedTextures.Add(value.ParameterName.Name);
            }
        }

        if (constant.ScalarParameterValues is not null)
        {
            foreach (FScalarParameterValue value in constant.ScalarParameterValues)
            {
                if (value?.ParameterName is null || string.IsNullOrWhiteSpace(value.ParameterName.Name))
                    continue;

                neutral.ScalarParameters[value.ParameterName.Name] = value.ParameterValue;
            }
        }

        if (constant.VectorParameterValues is not null)
        {
            foreach (FVectorParameterValue value in constant.VectorParameterValues)
            {
                if (value?.ParameterName is null || string.IsNullOrWhiteSpace(value.ParameterName.Name))
                    continue;

                neutral.VectorParameters[value.ParameterName.Name] = value.ParameterValue.ToVector3();
            }
        }

        log?.Invoke($"Converted material instance constant {entry.PathName}.");
        return neutral;
    }

    private static NeutralMaterial ConvertInstance(Upk148ExportTableEntry entry, UMaterialInstance instance, Action<string>? log)
    {
        NeutralMaterial neutral = new()
        {
            Name = string.IsNullOrWhiteSpace(entry.ObjectName) ? entry.PathName : entry.ObjectName,
            Metadata = "MaterialInstance"
        };

        if (instance.Parent is not null)
            neutral.TextParameters["Parent"] = instance.Parent.ToString() ?? string.Empty;

        if (instance.StaticParameters is not null)
            neutral.Metadata = $"StaticPermutationResources={instance.StaticPermutationResources?.Length ?? 0}";

        log?.Invoke($"Converted material instance {entry.PathName}.");
        return neutral;
    }
}

