using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OmegaAssetStudio.ThanosMigration.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosPrototypeMergerService
{
    private readonly UpkFileRepository repository = new();

    public async Task<IReadOnlyList<ThanosMigrationStep>> MergePrototypes(IReadOnlyList<ThanosPrototypeMergePlan> plans, string client152Root)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentException.ThrowIfNullOrWhiteSpace(client152Root);

        List<ThanosMigrationStep> steps = [];
        string fullRoot = Path.GetFullPath(client152Root);
        Directory.CreateDirectory(fullRoot);

        foreach (ThanosPrototypeMergePlan plan in plans)
        {
            string targetPath = Path.GetFullPath(plan.TargetUpkPath);
            List<ThanosMigrationStep> planSteps =
            [
                new() { Name = "LoadTargetUpk", Description = $"Load target package: {targetPath}" },
                new() { Name = "LoadSourcePrototypes", Description = $"Load {plan.SourcePrototypes.Count:N0} source prototype(s)." },
                new() { Name = "InjectPrototypes", Description = "Clone source exports into the target package." },
                new() { Name = "PatchReferences", Description = "Patch import/export references and table indices." },
                new() { Name = "WriteUpdatedUpk", Description = "Write the updated 1.52 package." }
            ];

            steps.AddRange(planSteps);

            try
            {
                UnrealHeader targetHeader = await LoadTargetHeaderAsync(targetPath, plan.SourcePrototypes).ConfigureAwait(false);
                planSteps[0].Status = ThanosMigrationStepStatus.Done;

                Dictionary<int, int> exportMap = new();
                Dictionary<string, int> importMap = new(StringComparer.OrdinalIgnoreCase);
                List<UnrealHeader> sourceHeaders = [];
                Dictionary<string, UnrealHeader> sourceHeaderCache = new(StringComparer.OrdinalIgnoreCase);

                foreach (ThanosPrototypeSource source in plan.SourcePrototypes)
                {
                    UnrealHeader sourceHeader = await LoadHeaderAsync(source.SourceUpkPath, sourceHeaderCache).ConfigureAwait(false);
                    if (!sourceHeaders.Contains(sourceHeader))
                        sourceHeaders.Add(sourceHeader);
                }

                planSteps[1].Status = ThanosMigrationStepStatus.Done;
                planSteps[1].Reason = $"Loaded {sourceHeaders.Count:N0} source package(s).";

                int injectedCount = 0;
                foreach (ThanosPrototypeSource source in plan.SourcePrototypes)
                {
                    UnrealHeader sourceHeader = sourceHeaderCache[Path.GetFullPath(source.SourceUpkPath)];
                    UnrealExportTableEntry sourceExport = sourceHeader.ExportTable.First(entry => entry.TableIndex == source.ExportIndex);

                    int targetExportIndex = await EnsureExportAsync(targetHeader, sourceHeader, sourceExport, exportMap, importMap).ConfigureAwait(false);
                    exportMap[sourceExport.TableIndex] = targetExportIndex;
                    injectedCount++;
                }

                planSteps[2].Status = ThanosMigrationStepStatus.Done;
                planSteps[2].Reason = $"Injected {injectedCount:N0} prototype export(s).";

                PatchExportReferences(targetHeader, exportMap, importMap);
                planSteps[3].Status = ThanosMigrationStepStatus.Done;
                planSteps[3].Reason = "References patched where matching target entries were found.";

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? fullRoot);
                await repository.SaveUpkFile(targetHeader, targetPath).ConfigureAwait(false);
                planSteps[4].Status = ThanosMigrationStepStatus.Done;
                planSteps[4].Reason = $"Wrote {targetPath}.";
            }
            catch (System.Exception ex)
            {
                foreach (ThanosMigrationStep step in planSteps.Where(static item => item.Status == ThanosMigrationStepStatus.Pending))
                {
                    step.Status = ThanosMigrationStepStatus.Failed;
                    step.Reason = ex.Message;
                    step.Exception = ex;
                }
            }
        }

        return steps;
    }

    private async Task<UnrealHeader> LoadTargetHeaderAsync(string targetPath, IReadOnlyList<ThanosPrototypeSource> sources)
    {
        if (File.Exists(targetPath))
        {
            UnrealHeader targetHeader = await LoadHeaderAsync(targetPath, new Dictionary<string, UnrealHeader>(StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
            return targetHeader;
        }

        if (sources.Count == 0)
            throw new InvalidOperationException("No source prototypes were provided for target package creation.");

        UnrealHeader baseHeader = await LoadHeaderAsync(sources[0].SourceUpkPath, new Dictionary<string, UnrealHeader>(StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
        baseHeader.FullFilename = targetPath;
        SetProperty(baseHeader, "Version", (ushort)152);
        SetProperty(baseHeader, "Licensee", (ushort)0);
        return baseHeader;
    }

    private async Task<UnrealHeader> LoadHeaderAsync(string path, Dictionary<string, UnrealHeader> cache)
    {
        string fullPath = Path.GetFullPath(path);
        if (cache.TryGetValue(fullPath, out UnrealHeader? cached))
            return cached;

        UnrealHeader header = await repository.LoadUpkFile(fullPath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);
        cache[fullPath] = header;
        return header;
    }

    private Task<int> EnsureExportAsync(
        UnrealHeader targetHeader,
        UnrealHeader sourceHeader,
        UnrealExportTableEntry sourceExport,
        Dictionary<int, int> exportMap,
        Dictionary<string, int> importMap)
    {
        int existingIndex = FindMatchingExportIndex(targetHeader, sourceExport);
        if (existingIndex > 0)
            return Task.FromResult(existingIndex);

        int clonedClassReference = ResolveReference(targetHeader, sourceHeader, sourceExport.ClassReference, exportMap, importMap);
        int clonedSuperReference = ResolveReference(targetHeader, sourceHeader, sourceExport.SuperReference, exportMap, importMap);
        int clonedOuterReference = ResolveReference(targetHeader, sourceHeader, sourceExport.OuterReference, exportMap, importMap);
        int clonedArchetypeReference = ResolveReference(targetHeader, sourceHeader, sourceExport.ArchetypeReference, exportMap, importMap);

        UnrealExportTableEntry cloned = (UnrealExportTableEntry)Activator.CreateInstance(typeof(UnrealExportTableEntry), nonPublic: true)!;
        CopyExportTableEntry(cloned, sourceExport);
        SetProperty(cloned, "UnrealHeader", targetHeader);
        SetProperty(cloned, "ClassReference", clonedClassReference);
        SetProperty(cloned, "SuperReference", clonedSuperReference);
        SetProperty(cloned, "OuterReference", clonedOuterReference);
        SetProperty(cloned, "ArchetypeReference", clonedArchetypeReference);

        if (sourceExport.UnrealObject is not null)
            SetProperty(cloned, "UnrealObject", sourceExport.UnrealObject);

        if (sourceExport.UnrealObjectReader is not null)
            SetProperty(cloned, "UnrealObjectReader", sourceExport.UnrealObjectReader);

        targetHeader.ExportTable.Add(cloned);
        int newIndex = targetHeader.ExportTable.Count;
        cloned.TableIndex = newIndex;
        cloned.ObjectNameIndex.TableEntry = cloned;
        return Task.FromResult(newIndex);
    }

    private static void PatchExportReferences(UnrealHeader targetHeader, Dictionary<int, int> exportMap, Dictionary<string, int> importMap)
    {
        for (int i = 0; i < targetHeader.ExportTable.Count; i++)
        {
            UnrealExportTableEntry export = targetHeader.ExportTable[i];
            export.TableIndex = i + 1;
            int classReference = ResolveReference(targetHeader, targetHeader, export.ClassReference, exportMap, importMap);
            int superReference = ResolveReference(targetHeader, targetHeader, export.SuperReference, exportMap, importMap);
            int outerReference = ResolveReference(targetHeader, targetHeader, export.OuterReference, exportMap, importMap);
            int archetypeReference = ResolveReference(targetHeader, targetHeader, export.ArchetypeReference, exportMap, importMap);

            SetProperty(export, "ClassReference", classReference);
            SetProperty(export, "SuperReference", superReference);
            SetProperty(export, "OuterReference", outerReference);
            SetProperty(export, "ArchetypeReference", archetypeReference);
            export.ObjectNameIndex.TableEntry = export;
        }
    }

    private static int ResolveReference(
        UnrealHeader targetHeader,
        UnrealHeader sourceHeader,
        int reference,
        Dictionary<int, int> exportMap,
        Dictionary<string, int> importMap)
    {
        if (reference == 0)
            return 0;

        if (reference > 0)
        {
            if (exportMap.TryGetValue(reference, out int mappedExport))
                return mappedExport;

            UnrealExportTableEntry? sourceExport = sourceHeader.ExportTable.FirstOrDefault(entry => entry.TableIndex == reference);
            if (sourceExport is null)
                return reference;

            int match = FindMatchingExportIndex(targetHeader, sourceExport);
            return match > 0 ? match : reference;
        }

        string key = $"{sourceHeader.FullFilename}|{reference}";
        if (importMap.TryGetValue(key, out int mappedImport))
            return mappedImport;

        UnrealImportTableEntry? sourceImport = sourceHeader.ImportTable.FirstOrDefault(entry => entry.TableIndex == reference);
        if (sourceImport is null)
            return reference;

        int matchImport = FindMatchingImportIndex(targetHeader, sourceImport);
        if (matchImport > 0)
            return matchImport;

        UnrealImportTableEntry clonedImport = new();
        CopyImportTableEntry(clonedImport, sourceImport);
        SetProperty(clonedImport, "UnrealHeader", targetHeader);
        targetHeader.ImportTable.Add(clonedImport);
        clonedImport.TableIndex = -(targetHeader.ImportTable.Count);
        clonedImport.ObjectNameIndex.TableEntry = clonedImport;
        importMap[key] = clonedImport.TableIndex;
        return clonedImport.TableIndex;
    }

    private static int FindMatchingExportIndex(UnrealHeader targetHeader, UnrealExportTableEntry sourceExport)
    {
        string sourcePath = sourceExport.GetPathName();
        string objectName = sourceExport.ObjectNameIndex?.Name ?? string.Empty;
        string className = sourceExport.ClassReferenceNameIndex?.Name ?? string.Empty;

        foreach (UnrealExportTableEntry targetExport in targetHeader.ExportTable)
        {
            if (string.Equals(targetExport.GetPathName(), sourcePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetExport.ObjectNameIndex?.Name, objectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetExport.ClassReferenceNameIndex?.Name, className, StringComparison.OrdinalIgnoreCase))
            {
                return targetExport.TableIndex;
            }
        }

        return 0;
    }

    private static int FindMatchingImportIndex(UnrealHeader targetHeader, UnrealImportTableEntry sourceImport)
    {
        string sourcePath = sourceImport.GetPathName();
        string objectName = sourceImport.ObjectNameIndex?.Name ?? string.Empty;
        string className = sourceImport.ClassNameIndex?.Name ?? string.Empty;

        foreach (UnrealImportTableEntry targetImport in targetHeader.ImportTable)
        {
            if (string.Equals(targetImport.GetPathName(), sourcePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetImport.ObjectNameIndex?.Name, objectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetImport.ClassNameIndex?.Name, className, StringComparison.OrdinalIgnoreCase))
            {
                return targetImport.TableIndex;
            }
        }

        return 0;
    }

    private static void CopyExportTableEntry(UnrealExportTableEntry target, UnrealExportTableEntry source)
    {
        SetProperty(target, "ClassReference", source.ClassReference);
        SetProperty(target, "SuperReference", source.SuperReference);
        SetProperty(target, "OuterReference", source.OuterReference);
        SetProperty(target, "ArchetypeReference", source.ArchetypeReference);
        SetProperty(target, "ObjectFlags", source.ObjectFlags);
        SetProperty(target, "SerialDataSize", source.SerialDataSize);
        SetProperty(target, "SerialDataOffset", source.SerialDataOffset);
        SetProperty(target, "ExportFlags", source.ExportFlags);
        SetProperty(target, "PackageGuid", source.PackageGuid?.ToArray());
        SetProperty(target, "PackageFlags", source.PackageFlags);

        SetProperty(target.ObjectNameIndex, "Index", source.ObjectNameIndex.Index);
        SetProperty(target.ObjectNameIndex, "Numeric", source.ObjectNameIndex.Numeric);
        SetProperty(target.ObjectNameIndex, "Name", source.ObjectNameIndex.Name);

        target.NetObjects.Clear();
        foreach (int netObject in source.NetObjects)
            target.NetObjects.Add(netObject);

        SetProperty(target, "UnrealObjectReader", source.UnrealObjectReader);
        SetProperty(target, "UnrealObject", source.UnrealObject);
    }

    private static void CopyImportTableEntry(UnrealImportTableEntry target, UnrealImportTableEntry source)
    {
        SetProperty(target.PackageNameIndex, "Index", source.PackageNameIndex.Index);
        SetProperty(target.PackageNameIndex, "Numeric", source.PackageNameIndex.Numeric);
        SetProperty(target.PackageNameIndex, "Name", source.PackageNameIndex.Name);

        SetProperty(target.ClassNameIndex, "Index", source.ClassNameIndex.Index);
        SetProperty(target.ClassNameIndex, "Numeric", source.ClassNameIndex.Numeric);
        SetProperty(target.ClassNameIndex, "Name", source.ClassNameIndex.Name);

        SetProperty(target.ObjectNameIndex, "Index", source.ObjectNameIndex.Index);
        SetProperty(target.ObjectNameIndex, "Numeric", source.ObjectNameIndex.Numeric);
        SetProperty(target.ObjectNameIndex, "Name", source.ObjectNameIndex.Name);

        SetProperty(target, "OuterReference", source.OuterReference);
        target.OuterReferenceNameIndex = source.OuterReferenceNameIndex;
    }

    private static void SetProperty<T>(object instance, string propertyName, T value)
    {
        PropertyInfo? property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property?.SetValue(instance, value);
    }
}

