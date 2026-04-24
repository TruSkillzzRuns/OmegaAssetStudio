using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using OmegaAssetStudio.BackupManager;
using OmegaAssetStudio.MaterialInspector;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Types;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor;

public sealed class MaterialEditorService
{
    private readonly MaterialRepository repository;
    private readonly MaterialInspectorService inspectorService;
    private readonly UpkFileRepository upkRepository = new();

    public event Action<string>? LogMessage;

    public MaterialEditorService()
        : this(new MaterialRepository(), new MaterialInspectorService())
    {
    }

    public MaterialEditorService(MaterialRepository repository, MaterialInspectorService inspectorService)
    {
        this.repository = repository;
        this.inspectorService = inspectorService;
    }

    public void Clear()
    {
        repository.Clear();
    }

    public async Task<IReadOnlyList<MaterialDefinition>> LoadMaterialsFromUpkAsync(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new ArgumentException("UPK path is required.", nameof(upkPath));

        if (!File.Exists(upkPath))
            throw new FileNotFoundException("UPK file not found.", upkPath);

        LogMessage?.Invoke($"Loading materials from {Path.GetFileName(upkPath)}");

        IReadOnlyList<string> skeletalMeshExports = await inspectorService.GetSkeletalMeshExportsAsync(upkPath).ConfigureAwait(true);
        List<MaterialDefinition> materials = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string skeletalMeshExport in skeletalMeshExports)
        {
            LogMessage?.Invoke($"Inspecting {skeletalMeshExport}");
            MaterialInspectorResult result = await inspectorService.InspectAsync(upkPath, skeletalMeshExport).ConfigureAwait(true);

            foreach (MaterialInspectorSectionInfo section in result.Sections)
            {
                foreach (MaterialInspectorMaterialNode node in section.MaterialChain)
                {
                    if (string.IsNullOrWhiteSpace(node.Path) || node.Path is "<missing>" or "<unresolved>")
                        continue;

                    if (!seenPaths.Add(node.Path))
                        continue;

                    MaterialDefinition material = CreateMaterialDefinition(upkPath, skeletalMeshExport, node);
                    repository.AddOrUpdate(material);
                    if (!ShouldHideFromBrowser(material))
                        materials.Add(material);
                }
            }
        }

        await LoadDirectMaterialExportsAsync(upkPath, materials, seenPaths).ConfigureAwait(true);

        if (materials.Count == 0)
            LogMessage?.Invoke($"No material definitions were resolved from {Path.GetFileName(upkPath)}.");

        return materials;
    }

    public async Task<IReadOnlyList<string>> GetSkeletalMeshExportsAsync(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new ArgumentException("UPK path is required.", nameof(upkPath));

        if (!File.Exists(upkPath))
            throw new FileNotFoundException("UPK file not found.", upkPath);

        return await inspectorService.GetSkeletalMeshExportsAsync(upkPath).ConfigureAwait(true);
    }

    public async Task<int> GetSkeletalMeshLodCountAsync(string upkPath, string skeletalMeshExportPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new ArgumentException("UPK path is required.", nameof(upkPath));

        if (!File.Exists(upkPath))
            throw new FileNotFoundException("UPK file not found.", upkPath);

        if (string.IsNullOrWhiteSpace(skeletalMeshExportPath))
            return 1;

        UnrealHeader header = await upkRepository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable.FirstOrDefault(entry => string.Equals(entry.GetPathName(), skeletalMeshExportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"SkeletalMesh export '{skeletalMeshExportPath}' was not found.");

        if (export.UnrealObject is null)
        {
            await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);
        }

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException($"Export '{skeletalMeshExportPath}' is not a SkeletalMesh.");

        return Math.Max(1, skeletalMesh.LODModels?.Count ?? 1);
    }

    private async Task LoadDirectMaterialExportsAsync(string upkPath, List<MaterialDefinition> materials, HashSet<string> seenPaths)
    {
        UnrealHeader header = await upkRepository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        foreach (UnrealExportTableEntry export in header.ExportTable)
        {
            string path = export.GetPathName();
            string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path) || seenPaths.Contains(path))
                continue;

            if (!className.Contains("Material", StringComparison.OrdinalIgnoreCase) &&
                !className.Contains("MaterialInstance", StringComparison.OrdinalIgnoreCase))
                continue;

            if (export.UnrealObject is null)
            {
                await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
                await export.ParseUnrealObject(false, false).ConfigureAwait(true);
            }

            if (export.UnrealObject is not IUnrealObject unrealObject)
                continue;

            object? resolved = unrealObject.UObject;
            if (resolved is null)
                continue;

            MaterialDefinition material = new()
            {
                Name = path,
                Path = path,
                Type = resolved.GetType().Name,
                SourceUpkPath = upkPath,
                SourceMeshExportPath = string.Empty
            };

            if (resolved is UMaterialInstanceConstant instanceConstant)
            {
                material.TextureSlots = (instanceConstant.TextureParameterValues ?? [])
                    .Select(parameter => new MaterialTextureSlot
                    {
                        SlotName = parameter.ParameterName?.Name ?? "<unnamed>",
                        TextureName = parameter.ParameterValue?.GetPathName() ?? "<null>",
                        TexturePath = parameter.ParameterValue?.GetPathName() ?? "<null>",
                        IsOverride = true
                    })
                    .ToList();

                material.ScalarParameters = (instanceConstant.ScalarParameterValues ?? [])
                    .Select(parameter => new MaterialParameter
                    {
                        Name = parameter.ParameterName?.Name ?? "<unnamed>",
                        Category = "Scalar",
                        ScalarValue = parameter.ParameterValue,
                        DefaultScalarValue = parameter.ParameterValue
                    })
                    .ToList();

                material.VectorParameters = (instanceConstant.VectorParameterValues ?? [])
                    .Select(parameter => new MaterialParameter
                    {
                        Name = parameter.ParameterName?.Name ?? "<unnamed>",
                        Category = "Vector",
                        VectorValue = new Vector4(parameter.ParameterValue.R, parameter.ParameterValue.G, parameter.ParameterValue.B, parameter.ParameterValue.A),
                        DefaultVectorValue = new Vector4(parameter.ParameterValue.R, parameter.ParameterValue.G, parameter.ParameterValue.B, parameter.ParameterValue.A)
                    })
                    .ToList();
            }

            repository.AddOrUpdate(material);
            if (!ShouldHideFromBrowser(material))
                materials.Add(material);
            seenPaths.Add(path);
        }
    }

    public Task SaveMaterialAsync(MaterialDefinition material)
    {
        if (material is null)
            throw new ArgumentNullException(nameof(material));

        return SaveMaterialInternalAsync(material);
    }

    public async Task<MaterialValidationResult> ValidateMaterialRoundTripAsync(MaterialDefinition material)
    {
        if (material is null)
            throw new ArgumentNullException(nameof(material));

        if (string.IsNullOrWhiteSpace(material.SourceUpkPath))
            return new MaterialValidationResult
            {
                IsValid = false,
                MaterialName = material.Name,
                MaterialPath = material.Path,
                Message = "Material has no source UPK path."
            };

        if (!File.Exists(material.SourceUpkPath))
            return new MaterialValidationResult
            {
                IsValid = false,
                MaterialName = material.Name,
                MaterialPath = material.Path,
                Message = "Source UPK file not found."
            };

        try
        {
            UnrealHeader header = await upkRepository.LoadUpkFile(material.SourceUpkPath).ConfigureAwait(true);
            await header.ReadHeaderAsync(null).ConfigureAwait(true);

            UnrealExportTableEntry export = header.ExportTable.FirstOrDefault(entry => string.Equals(entry.GetPathName(), material.Path, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Material export '{material.Path}' was not found.");

            if (export.UnrealObject is null)
            {
                await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
                await export.ParseUnrealObject(false, false).ConfigureAwait(true);
            }

            if (export.UnrealObject is not IUnrealObject materialObject || materialObject.UObject is not UMaterialInstanceConstant)
                throw new InvalidOperationException($"Export '{material.Path}' did not reopen as a MaterialInstanceConstant.");

            return new MaterialValidationResult
            {
                IsValid = true,
                MaterialName = material.Name,
                MaterialPath = material.Path,
                TextureSlotCount = material.TextureSlots.Count,
                ScalarParameterCount = material.ScalarParameters.Count,
                VectorParameterCount = material.VectorParameters.Count,
                Message = "Round-trip validation succeeded."
            };
        }
        catch (Exception ex)
        {
            return new MaterialValidationResult
            {
                IsValid = false,
                MaterialName = material.Name,
                MaterialPath = material.Path,
                Message = ex.Message
            };
        }
    }

    public MaterialValidationResult ValidateMaterialDefinition(MaterialDefinition material)
    {
        if (material is null)
            throw new ArgumentNullException(nameof(material));

        List<string> issues = [];
        List<string> notes = [];

        if (string.IsNullOrWhiteSpace(material.Name))
            issues.Add("Material name is missing.");

        if (string.IsNullOrWhiteSpace(material.Path))
            issues.Add("Material path is missing.");

        if (string.IsNullOrWhiteSpace(material.SourceUpkPath))
            issues.Add("Source UPK path is missing.");
        else if (!File.Exists(material.SourceUpkPath))
            issues.Add("Source UPK file not found.");

        if (string.IsNullOrWhiteSpace(material.SourceMeshExportPath))
            notes.Add("Material is not linked to a preview skeletal mesh export.");

        if (material.TextureSlots.Count == 0)
            notes.Add("Material has no texture slots.");

        if (material.ScalarParameters.Count == 0)
            notes.Add("Material has no scalar parameters.");

        if (material.VectorParameters.Count == 0)
            notes.Add("Material has no vector parameters.");

        HashSet<string> textureNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (MaterialTextureSlot slot in material.TextureSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.SlotName))
                issues.Add("A texture slot is missing a slot name.");
            else if (!textureNames.Add(slot.SlotName.Trim()))
                notes.Add($"Duplicate texture slot name detected: {slot.SlotName}");

            if (string.IsNullOrWhiteSpace(slot.TextureName) || string.IsNullOrWhiteSpace(slot.TexturePath))
                notes.Add($"Texture slot '{slot.SlotName}' is not bound to a texture export.");
        }

        HashSet<string> scalarNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (MaterialParameter parameter in material.ScalarParameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                issues.Add("A scalar parameter is missing a name.");
            else if (!scalarNames.Add(parameter.Name.Trim()))
                notes.Add($"Duplicate scalar parameter detected: {parameter.Name}");
        }

        HashSet<string> vectorNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (MaterialParameter parameter in material.VectorParameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                issues.Add("A vector parameter is missing a name.");
            else if (!vectorNames.Add(parameter.Name.Trim()))
                notes.Add($"Duplicate vector parameter detected: {parameter.Name}");
        }

        return new MaterialValidationResult
        {
            IsValid = issues.Count == 0,
            MaterialName = material.Name,
            MaterialPath = material.Path,
            TextureSlotCount = material.TextureSlots.Count,
            ScalarParameterCount = material.ScalarParameters.Count,
            VectorParameterCount = material.VectorParameters.Count,
            Message = issues.Count == 0
                ? notes.Count == 0
                    ? "Material definition is valid."
                    : string.Join(" | ", notes)
                : string.Join(" | ", issues.Concat(notes))
        };
    }

    private async Task SaveMaterialInternalAsync(MaterialDefinition material)
    {
        if (string.IsNullOrWhiteSpace(material.SourceUpkPath))
            throw new InvalidOperationException($"Material '{material.Name}' does not have a source UPK path.");

        if (!File.Exists(material.SourceUpkPath))
            throw new FileNotFoundException("Source UPK file not found.", material.SourceUpkPath);

        string backupPath = BackupFileHelper.CreateBackup(material.SourceUpkPath);
        string tempPath = Path.Combine(
            Path.GetDirectoryName(material.SourceUpkPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(material.SourceUpkPath)}.materialeditor{Path.GetExtension(material.SourceUpkPath)}");

        if (File.Exists(tempPath))
            File.Delete(tempPath);

        LogMessage?.Invoke($"Saving material '{material.Name}' to {Path.GetFileName(material.SourceUpkPath)}");
        LogMessage?.Invoke($"Backup written: {backupPath}");

        MaterialValidationResult validation = ValidateMaterialDefinition(material);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Material validation failed: {validation.Message}");

        UnrealHeader header = await upkRepository.LoadUpkFile(material.SourceUpkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable.FirstOrDefault(entry => string.Equals(entry.GetPathName(), material.Path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Material export '{material.Path}' was not found in {Path.GetFileName(material.SourceUpkPath)}.");

        if (export.UnrealObject is null)
        {
            await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);
        }

        if (export.UnrealObject is not IUnrealObject materialObject || materialObject.UObject is not UMaterialInstanceConstant mic)
            throw new InvalidOperationException($"Export '{material.Path}' is not a MaterialInstanceConstant.");

        ApplyTextureParameters(mic, header, material);
        ApplyScalarParameters(mic, material);
        ApplyVectorParameters(mic, material);

        await upkRepository.SaveUpkFile(header, tempPath, message => LogMessage?.Invoke(message)).ConfigureAwait(true);
        File.Copy(tempPath, material.SourceUpkPath, true);
        File.Delete(tempPath);

        repository.AddOrUpdate(material);
        LogMessage?.Invoke($"Material '{material.Name}' saved successfully.");
    }

    private static void ApplyTextureParameters(UMaterialInstanceConstant mic, UnrealHeader header, MaterialDefinition material)
    {
        mic.TextureParameterValues ??= [];
        Dictionary<string, MaterialTextureSlot> slots = material.TextureSlots
            .Where(slot => !string.IsNullOrWhiteSpace(slot.SlotName))
            .GroupBy(slot => slot.SlotName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < mic.TextureParameterValues.Count; i++)
        {
            FTextureParameterValue parameter = mic.TextureParameterValues[i];
            string parameterName = parameter.ParameterName?.Name ?? string.Empty;
            if (!slots.TryGetValue(parameterName, out MaterialTextureSlot? slot))
                continue;

            FObject? textureReference = ResolveTextureReference(header, slot.TexturePath);
            if (textureReference is not null)
                parameter.ParameterValue = textureReference;
        }
    }

    private static void ApplyScalarParameters(UMaterialInstanceConstant mic, MaterialDefinition material)
    {
        mic.ScalarParameterValues ??= [];
        Dictionary<string, MaterialParameter> parameters = material.ScalarParameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(parameter => parameter.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < mic.ScalarParameterValues.Count; i++)
        {
            FScalarParameterValue parameter = mic.ScalarParameterValues[i];
            string parameterName = parameter.ParameterName?.Name ?? string.Empty;
            if (!parameters.TryGetValue(parameterName, out MaterialParameter? sourceParameter))
                continue;

            parameter.ParameterValue = sourceParameter.ScalarValue ?? sourceParameter.DefaultScalarValue ?? parameter.ParameterValue;
        }
    }

    private static void ApplyVectorParameters(UMaterialInstanceConstant mic, MaterialDefinition material)
    {
        mic.VectorParameterValues ??= [];
        Dictionary<string, MaterialParameter> parameters = material.VectorParameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(parameter => parameter.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < mic.VectorParameterValues.Count; i++)
        {
            FVectorParameterValue parameter = mic.VectorParameterValues[i];
            string parameterName = parameter.ParameterName?.Name ?? string.Empty;
            if (!parameters.TryGetValue(parameterName, out MaterialParameter? sourceParameter))
                continue;

            Vector4 value = sourceParameter.VectorValue ?? sourceParameter.DefaultVectorValue ?? Vector4.Zero;
            parameter.ParameterValue.R = value.X;
            parameter.ParameterValue.G = value.Y;
            parameter.ParameterValue.B = value.Z;
            parameter.ParameterValue.A = value.W;
        }
    }

    private static FObject? ResolveTextureReference(UnrealHeader header, string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        UnrealExportTableEntry? export = header.ExportTable.FirstOrDefault(entry => string.Equals(entry.GetPathName(), texturePath, StringComparison.OrdinalIgnoreCase));
        if (export is not null)
            return new FObject(export);

        string root = Path.GetDirectoryName(header.FullFilename) ?? string.Empty;
        UnrealObjectTableEntryBase? external = header.Repository?.GetExportEntry(texturePath, root);
        return external is null ? null : new FObject(external);
    }

    private static MaterialDefinition CreateMaterialDefinition(string upkPath, string skeletalMeshExportPath, MaterialInspectorMaterialNode node)
    {
        MaterialDefinition material = new()
        {
            Name = node.Path,
            Path = node.Path,
            Type = node.TypeName,
            SourceUpkPath = upkPath,
            SourceMeshExportPath = skeletalMeshExportPath
        };

        material.TextureSlots = node.TextureParameters.Select(parameter => new MaterialTextureSlot
        {
            SlotName = parameter.Name,
            TextureName = parameter.TexturePath,
            TexturePath = parameter.TexturePath,
            IsOverride = !string.IsNullOrWhiteSpace(parameter.TexturePath)
        }).ToList();

        material.ScalarParameters = node.ScalarParameters.Select(parameter => new MaterialParameter
        {
            Name = parameter.Name,
            Category = "Scalar",
            ScalarValue = parameter.Value,
            DefaultScalarValue = parameter.Value
        }).ToList();

        material.VectorParameters = node.VectorParameters.Select(parameter => new MaterialParameter
        {
            Name = parameter.Name,
            Category = "Vector",
            VectorValue = new System.Numerics.Vector4(parameter.Value.X, parameter.Value.Y, parameter.Value.Z, 1.0f),
            DefaultVectorValue = new System.Numerics.Vector4(parameter.Value.X, parameter.Value.Y, parameter.Value.Z, 1.0f)
        }).ToList();

        return material;
    }

    private static bool ShouldHideFromBrowser(MaterialDefinition material)
    {
        string value = $"{material.Name} {material.Path}".Trim().ToLowerInvariant();
        return value.Contains("chbasematerials")
            || value.Contains("chvfxmaterials")
            || value.Contains("vfx_shared_materials")
            || value.Contains("engine_materialfunctions02");
    }
}

