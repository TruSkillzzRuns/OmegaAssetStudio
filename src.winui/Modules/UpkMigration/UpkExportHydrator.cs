using UpkManager.Models.UpkFile.Objects;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration;

internal static class UpkExportHydrator
{
    public static bool TryHydrate<T>(Upk148ExportTableEntry entry, out T? resolved, Action<string>? log = null)
        where T : class
        => TryHydrate(entry, out resolved, log, true);

    public static bool TryHydrate<T>(Upk148ExportTableEntry entry, out T? resolved, Action<string>? log, bool reportWarnings)
        where T : class
        => TryHydrateCore(entry, out resolved, log, reportWarnings);

    private static bool TryHydrateCore<T>(Upk148ExportTableEntry entry, out T? resolved, Action<string>? log, bool reportWarnings)
        where T : class
    {
        resolved = entry.ResolvedObject as T;
        if (resolved is not null)
            return true;

        try
        {
            if (entry.RawExport.UnrealHeader is not null)
                entry.RawExport.UnrealHeader.ReadExportObjectAsync(entry.RawExport, null).GetAwaiter().GetResult();

            if (entry.RawExport.UnrealObject is null)
                entry.RawExport.ParseUnrealObject(false, false).GetAwaiter().GetResult();

            if (entry.RawExport.UnrealObject is IUnrealObject parsed && parsed.UObject is T typed)
            {
                resolved = typed;
                return true;
            }

            if (reportWarnings && entry.RawExport.UnrealObject is IUnrealObject parsedObject)
            {
                string actualType = parsedObject.UObject?.GetType().Name ?? parsedObject.GetType().Name;
                log?.Invoke($"Warning: hydrated export type mismatch for {entry.PathName}: expected {typeof(T).Name}, got {actualType}");
            }
        }
        catch (Exception ex)
        {
            if (reportWarnings)
                log?.Invoke($"Warning: hydrate failed for {entry.PathName}: {ex.Message}");
        }

        return false;
    }
}

