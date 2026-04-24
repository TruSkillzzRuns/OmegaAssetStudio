using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmegaAssetStudio.ThanosMigration.Models;
using UpkManager.Models.UpkFile.Classes;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace OmegaAssetStudio.ThanosMigration.Services;

public sealed class ThanosDependencyScannerService
{
    private readonly UpkFileRepository repository;

    public ThanosDependencyScannerService(UpkFileRepository repository)
    {
        this.repository = repository;
    }

    public async Task<ThanosDependencyReport> ScanDependenciesAsync(string sourceUpkPath, string client152Root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUpkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(client152Root);

        string fullSourcePath = Path.GetFullPath(sourceUpkPath);
        string fullClientRoot = Path.GetFullPath(client152Root);

        if (!File.Exists(fullSourcePath))
            throw new FileNotFoundException("Source UPK not found.", fullSourcePath);

        if (!Directory.Exists(fullClientRoot))
            throw new DirectoryNotFoundException($"Client root not found: {fullClientRoot}");

        EnsurePackageIndex(fullClientRoot);

        UnrealHeader header = await repository.LoadUpkFile(fullSourcePath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);

        List<DependencyCandidate> candidates = [];
        CollectTableCandidates(header, fullSourcePath, candidates);
        await CollectObjectGraphCandidatesAsync(header, fullSourcePath, candidates).ConfigureAwait(false);

        List<ThanosDependencyItem> missing = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (DependencyCandidate candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.ObjectPath))
                continue;

            if (!seenPaths.Add(candidate.ObjectPath))
                continue;

            if (IsPresentInClient(candidate.ObjectPath, fullClientRoot))
                continue;

            missing.Add(new ThanosDependencyItem
            {
                Name = candidate.Name,
                ObjectPath = candidate.ObjectPath,
                PackageName = candidate.PackageName,
                ClassName = candidate.ClassName,
                OuterName = candidate.OuterName,
                SourceUpkPath = fullSourcePath,
                ExportIndex = candidate.ExportIndex,
                MissingInClient = true,
                ReferenceKind = candidate.ReferenceKind,
                Details = candidate.Details
            });
        }

        return new ThanosDependencyReport
        {
            FilePath = fullSourcePath,
            SourceUpkPath = fullSourcePath,
            Client152Root = fullClientRoot,
            Summary = $"Real dependency scan from {Path.GetFileName(fullSourcePath)} against {Path.GetFileName(fullClientRoot)} found {missing.Count:N0} missing item(s).",
            MissingDependencies = missing
                .OrderBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ClassName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ObjectPath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private void EnsurePackageIndex(string client152Root)
    {
        if (repository.PackageIndex is not null)
            return;

        string[] candidates =
        [
            Path.Combine(client152Root, "Data", "mh152.mpk"),
            Path.Combine(AppContext.BaseDirectory, "Data", "mh152.mpk")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                repository.LoadPackageIndex(candidate);
                return;
            }
        }
    }

    private void CollectTableCandidates(UnrealHeader header, string sourceUpkPath, List<DependencyCandidate> candidates)
    {
        foreach (UnrealImportTableEntry import in header.ImportTable)
        {
            AddCandidate(candidates, BuildCandidate(import, "ImportTable", sourceUpkPath, import.TableIndex, import.GetPathName()));
        }

        foreach (UnrealExportTableEntry export in header.ExportTable)
        {
            AddTableReferenceCandidate(candidates, header, export.ClassReference, "ClassReference", export, sourceUpkPath);
            AddTableReferenceCandidate(candidates, header, export.SuperReference, "SuperReference", export, sourceUpkPath);
            AddTableReferenceCandidate(candidates, header, export.OuterReference, "OuterReference", export, sourceUpkPath);
            AddTableReferenceCandidate(candidates, header, export.ArchetypeReference, "ArchetypeReference", export, sourceUpkPath);
        }
    }

    private async Task CollectObjectGraphCandidatesAsync(UnrealHeader header, string sourceUpkPath, List<DependencyCandidate> candidates)
    {
        foreach (UnrealExportTableEntry export in header.ExportTable)
        {
            try
            {
                if (export.UnrealObject is null)
                    await export.ParseUnrealObject(false, false).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort scan. Table references are still useful even when bodies fail to parse.
            }

            if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UObject unrealType)
                continue;

            ScanUObject(unrealType, export, sourceUpkPath, candidates, export.GetPathName());
        }
    }

    private void ScanUObject(UObject unrealObject, UnrealExportTableEntry ownerExport, string sourceUpkPath, List<DependencyCandidate> candidates, string scopePath)
    {
        foreach (UnrealProperty property in unrealObject.Properties)
            ScanProperty(property.Value, ownerExport, sourceUpkPath, candidates, $"{scopePath}.{property.NameIndex?.Name}");
    }

    private void ScanProperty(UProperty propertyValue, UnrealExportTableEntry ownerExport, string sourceUpkPath, List<DependencyCandidate> candidates, string originPath)
    {
        if (propertyValue is null)
            return;

        switch (propertyValue)
        {
            case UClassProperty classProperty:
                AddObjectReference(candidates, classProperty.Object, "ClassProperty", ownerExport, sourceUpkPath, originPath);
                AddObjectReference(candidates, classProperty.MetaClass, "ClassProperty.MetaClass", ownerExport, sourceUpkPath, originPath);
                break;
            case UInterfaceProperty interfaceProperty:
                AddObjectReference(candidates, interfaceProperty.InterfaceClass, "InterfaceProperty", ownerExport, sourceUpkPath, originPath);
                break;
            case UComponentProperty componentProperty:
                AddObjectReference(candidates, componentProperty.Object, "ComponentProperty", ownerExport, sourceUpkPath, originPath);
                break;
            case UObjectProperty objectProperty:
                AddObjectReference(candidates, objectProperty.Object, "ObjectProperty", ownerExport, sourceUpkPath, originPath);
                break;
            case UStructProperty structProperty:
                if (structProperty.StructValue is not null)
                    ScanProperty(structProperty.StructValue, ownerExport, sourceUpkPath, candidates, $"{originPath}.Struct");
                break;
            case UArrayProperty arrayProperty:
                if (arrayProperty.Array is not null)
                {
                    for (int i = 0; i < arrayProperty.Array.Length; i++)
                        ScanProperty(arrayProperty.Array[i], ownerExport, sourceUpkPath, candidates, $"{originPath}[{i}]");
                }
                break;
        }
    }

    private static void AddTableReferenceCandidate(List<DependencyCandidate> candidates, UnrealHeader header, int reference, string referenceKind, UnrealExportTableEntry ownerExport, string sourceUpkPath)
    {
        if (reference == 0)
            return;

        UnrealObjectTableEntryBase? tableEntry = null;
        try
        {
            tableEntry = header.GetObjectTableEntry(reference);
        }
        catch
        {
            return;
        }

        if (tableEntry is null)
            return;

        AddCandidate(candidates, BuildCandidate(tableEntry, referenceKind, sourceUpkPath, ownerExport.TableIndex, ownerExport.GetPathName()));
    }

    private static void AddObjectReference(List<DependencyCandidate> candidates, FName? reference, string referenceKind, UnrealExportTableEntry ownerExport, string sourceUpkPath, string originPath)
    {
        if (reference is not FObject objectReference || objectReference.TableEntry is null)
            return;

        AddCandidate(candidates, BuildCandidate(objectReference.TableEntry, referenceKind, sourceUpkPath, ownerExport.TableIndex, originPath));
    }

    private static DependencyCandidate BuildCandidate(UnrealObjectTableEntryBase tableEntry, string referenceKind, string sourceUpkPath, int exportIndex, string originPath)
    {
        string objectPath = tableEntry.GetPathName();
        string name = tableEntry.ObjectNameIndex?.Name ?? Path.GetFileNameWithoutExtension(objectPath);
        string packageName;
        string className;
        string outerName;

        switch (tableEntry)
        {
            case UnrealExportTableEntry export:
                packageName = Path.GetFileNameWithoutExtension(export.UnrealHeader?.FullFilename ?? string.Empty);
                className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
                outerName = export.OuterReferenceNameIndex?.Name ?? string.Empty;
                break;
            case UnrealImportTableEntry import:
                packageName = import.PackageNameIndex?.Name ?? string.Empty;
                className = import.ClassNameIndex?.Name ?? string.Empty;
                outerName = import.OuterReferenceNameIndex?.Name ?? string.Empty;
                break;
            default:
                packageName = string.Empty;
                className = string.Empty;
                outerName = string.Empty;
                break;
        }

        return new DependencyCandidate
        {
            Name = name,
            ObjectPath = objectPath,
            PackageName = packageName,
            ClassName = className,
            OuterName = outerName,
            ReferenceKind = referenceKind,
            SourceUpkPath = sourceUpkPath,
            ExportIndex = exportIndex,
            Details = $"{referenceKind} -> {originPath}"
        };
    }

    private static void AddCandidate(List<DependencyCandidate> candidates, DependencyCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ObjectPath))
            return;

        candidates.Add(candidate);
    }

    private bool IsPresentInClient(string objectPath, string client152Root)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
            return false;

        try
        {
            if (repository.PackageIndex?.ContainsObjectPath(objectPath) == true)
                return true;

            UnrealObjectTableEntryBase? entry = repository.GetExportEntry(objectPath, client152Root);
            return entry is not null;
        }
        catch
        {
            return false;
        }
    }

    private sealed class DependencyCandidate
    {
        public string Name { get; set; } = string.Empty;

        public string ObjectPath { get; set; } = string.Empty;

        public string PackageName { get; set; } = string.Empty;

        public string ClassName { get; set; } = string.Empty;

        public string OuterName { get; set; } = string.Empty;

        public string ReferenceKind { get; set; } = string.Empty;

        public string SourceUpkPath { get; set; } = string.Empty;

        public int ExportIndex { get; set; }

        public string Details { get; set; } = string.Empty;
    }
}

