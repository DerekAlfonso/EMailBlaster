using System.IO;
using System.Text.Json;
using System.Windows;

namespace EmailBlaster.Desktop;

public enum AppTheme
{
    Dark = 0,
    Light = 1
}

/// <summary>
/// Applies and persists the UI theme. The colour brushes live in two resource dictionaries
/// (<c>Themes/Palette.Light.xaml</c> and <c>Themes/Palette.Dark.xaml</c>); this manager merges the
/// active one into the application resources and swaps it on toggle. Every style and view references
/// the brushes via <c>DynamicResource</c>, so swapping the dictionary repaints the whole UI live with
/// no restart.
///
/// The selected theme is stored in <c>%APPDATA%\EmailBlaster\preferences.json</c> and restored on the
/// next launch. Dark is the default when no preference has been saved yet.
/// </summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    private static ResourceDictionary? _paletteDictionary;

    private static readonly string PrefsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EmailBlaster", "preferences.json");

    /// <summary>Loads the persisted theme (default dark) and applies it. Call once at startup.</summary>
    public static void Initialize()
    {
        Current = LoadPreference();
        ApplyPalette(Current);
    }

    /// <summary>Switches to the given theme, applies it live and persists the choice.</summary>
    public static void Set(AppTheme theme)
    {
        Current = theme;
        ApplyPalette(theme);
        SavePreference(theme);
    }

    /// <summary>Flips between dark and light. Returns the new theme.</summary>
    public static AppTheme Toggle()
    {
        Set(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        return Current;
    }

    private static void ApplyPalette(AppTheme theme)
    {
        var fileName = theme == AppTheme.Dark ? "Palette.Dark.xaml" : "Palette.Light.xaml";
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Themes/{fileName}", UriKind.Relative)
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_paletteDictionary is not null)
            merged.Remove(_paletteDictionary);
        // Insert first so it is in place before Theme.xaml's styles resolve their DynamicResource brushes.
        merged.Insert(0, dictionary);
        _paletteDictionary = dictionary;
    }

    private static AppTheme LoadPreference()
    {
        try
        {
            if (File.Exists(PrefsPath))
            {
                var json = File.ReadAllText(PrefsPath);
                var prefs = JsonSerializer.Deserialize<Preferences>(json);
                if (prefs is not null && Enum.TryParse<AppTheme>(prefs.Theme, ignoreCase: true, out var t))
                    return t;
            }
        }
        catch
        {
            // Corrupt or unreadable preferences fall back to the default.
        }
        return AppTheme.Dark; // default
    }

    private static void SavePreference(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            var json = JsonSerializer.Serialize(new Preferences { Theme = theme.ToString() },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PrefsPath, json);
        }
        catch
        {
            // Non-fatal: the theme still applies for this session even if it can't be saved.
        }
    }

    private sealed class Preferences
    {
        public string Theme { get; set; } = "Dark";
    }
}
