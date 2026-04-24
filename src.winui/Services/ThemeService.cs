using System;
using System.IO;
using System.Text.Json;

namespace OmegaAssetStudio.WinUI.Services;

public enum AppThemeMode
{
    Light,
    Dark,
}

public sealed class ThemeChangedEventArgs : EventArgs
{
    public ThemeChangedEventArgs(AppThemeMode theme)
    {
        Theme = theme;
    }

    public AppThemeMode Theme { get; }
}

public static class ThemeService
{
    private sealed class ThemeSettings
    {
        public AppThemeMode Theme { get; set; } = AppThemeMode.Dark;
    }

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OmegaAssetStudio");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    private static AppThemeMode currentTheme = AppThemeMode.Dark;

    public static event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public static AppThemeMode CurrentTheme => currentTheme;

    public static void Initialize()
    {
        currentTheme = LoadTheme();
    }

    public static void SetTheme(AppThemeMode theme)
    {
        if (currentTheme == theme)
        {
            return;
        }

        currentTheme = theme;
        SaveTheme(theme);
        ThemeChanged?.Invoke(null, new ThemeChangedEventArgs(theme));
    }

    public static bool IsDarkMode => currentTheme == AppThemeMode.Dark;

    private static AppThemeMode LoadTheme()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return AppThemeMode.Dark;
            }

            string json = File.ReadAllText(SettingsPath);
            ThemeSettings? settings = JsonSerializer.Deserialize<ThemeSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            return settings?.Theme ?? AppThemeMode.Dark;
        }
        catch
        {
            return AppThemeMode.Dark;
        }
    }

    private static void SaveTheme(AppThemeMode theme)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            ThemeSettings settings = new()
            {
                Theme = theme,
            };

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }
}
