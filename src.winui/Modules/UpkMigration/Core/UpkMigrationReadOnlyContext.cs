using System;
using System.IO;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

public sealed class UpkMigrationReadOnlyContext
{
    public UpkMigrationReadOnlyContext(string sourceRootPath)
    {
        SourceRootPath = NormalizePath(sourceRootPath);
    }

    public string SourceRootPath { get; }

    public void EnsureReadableSource(string sourcePath)
    {
        string fullPath = NormalizePath(sourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Source file not found.", fullPath);

        if (!string.IsNullOrWhiteSpace(SourceRootPath) && !fullPath.StartsWith(SourceRootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source path is outside the approved read-only root.");
    }

    public void EnsureWritableOutput(string outputPath)
    {
        string fullPath = NormalizePath(outputPath);
        if (string.IsNullOrWhiteSpace(fullPath))
            throw new InvalidOperationException("Output path is required.");

        if (!string.IsNullOrWhiteSpace(SourceRootPath) && fullPath.StartsWith(SourceRootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Output path must not be inside the read-only source root.");
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

