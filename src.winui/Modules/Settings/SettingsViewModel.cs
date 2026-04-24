using System.ComponentModel;
using System.Runtime.CompilerServices;
using OmegaAssetStudio.WinUI.Services;

namespace OmegaAssetStudio.WinUI.Modules.Settings;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public SettingsViewModel()
    {
        ThemeService.ThemeChanged += ThemeService_ThemeChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsDarkMode
    {
        get => ThemeService.CurrentTheme == AppThemeMode.Dark;
        set
        {
            AppThemeMode nextTheme = value ? AppThemeMode.Dark : AppThemeMode.Light;
            if (ThemeService.CurrentTheme != nextTheme)
            {
                ThemeService.SetTheme(nextTheme);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeLabel));
        }
    }

    public string ThemeLabel => IsDarkMode ? "Dark" : "Light";

    private void ThemeService_ThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsDarkMode));
        OnPropertyChanged(nameof(ThemeLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
