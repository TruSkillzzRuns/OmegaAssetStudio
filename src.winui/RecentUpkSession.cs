using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OmegaAssetStudio.WinUI.Models;

namespace OmegaAssetStudio.WinUI;

internal static class RecentUpkSession
{
    private const int MaxEntries = 12;
    private static readonly string StoragePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaAssetStudio", "RecentUpkEntries.json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static IReadOnlyList<RecentUpkEntry> GetRecentEntries()
    {
        string? json = ReadJson();
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            List<RecentUpkEntry>? entries = JsonSerializer.Deserialize<List<RecentUpkEntry>>(json, JsonOptions);
            return entries ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void RecordWorkspaceLaunch(WorkspaceLaunchContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.UpkPath))
            return;

        RecordUpk(context.UpkPath, context.WorkspaceTag, context.ExportPath, context.Title, context.Summary);
    }

    public static void RecordUpk(string upkPath, string? workspaceTag = null, string? exportPath = null, string? title = null, string? summary = null)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return;

        List<RecentUpkEntry> entries = GetRecentEntries().ToList();
        entries.RemoveAll(entry => string.Equals(entry.UpkPath, upkPath, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, new RecentUpkEntry
        {
            UpkPath = upkPath,
            WorkspaceTag = workspaceTag ?? string.Empty,
            ExportPath = exportPath ?? string.Empty,
            Title = title ?? string.Empty,
            Summary = summary ?? string.Empty,
            LastUsedText = DateTime.Now.ToString("g")
        });

        if (entries.Count > MaxEntries)
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

        WriteJson(JsonSerializer.Serialize(entries, JsonOptions));
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StoragePath))
                File.Delete(StoragePath);
        }
        catch
        {
        }
    }

    private static string? ReadJson()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return null;

            return File.ReadAllText(StoragePath);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteJson(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, json);
        }
        catch
        {
        }
    }
}

