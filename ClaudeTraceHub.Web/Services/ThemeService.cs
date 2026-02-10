using MudBlazor;

namespace ClaudeTraceHub.Web.Services;

public class ThemeService
{
    public event Action? OnThemeChanged;

    private string _currentThemeName = "Purple";
    private bool _isDarkMode;

    public string CurrentThemeName => _currentThemeName;
    public bool IsDarkMode => _isDarkMode;

    public MudTheme CurrentTheme => GetThemeByName(_currentThemeName);

    public static readonly Dictionary<string, ThemeDefinition> AvailableThemes = new()
    {
        ["Purple"] = new ThemeDefinition
        {
            Name = "Purple",
            Icon = Icons.Material.Filled.ColorLens,
            PreviewColor = "#5B4FC4",
            Light = new PaletteLight
            {
                Primary = "#5B4FC4",
                Secondary = "#00BFA5",
                AppbarBackground = "#5B4FC4",
                Background = "#f5f5f7",
                Surface = "#ffffff",
                DrawerBackground = "#ffffff",
                DrawerText = "#424242",
                DrawerIcon = "#757575",
                AppbarText = "#ffffff"
            },
            Dark = new PaletteDark
            {
                Primary = "#7C6FE0",
                Secondary = "#00E5CC",
                AppbarBackground = "#1e1e2e",
                Background = "#121218",
                Surface = "#1e1e2e",
                DrawerBackground = "#1a1a28",
                DrawerText = "#e0e0e0",
                DrawerIcon = "#b0b0b0",
                AppbarText = "#ffffff",
                TextPrimary = "#e0e0e0",
                TextSecondary = "#a0a0a0",
                ActionDefault = "#b0b0b0",
                Divider = "#2d2d3f"
            }
        },
        ["Ocean Blue"] = new ThemeDefinition
        {
            Name = "Ocean Blue",
            Icon = Icons.Material.Filled.Water,
            PreviewColor = "#1565C0",
            Light = new PaletteLight
            {
                Primary = "#1565C0",
                Secondary = "#FF6F00",
                AppbarBackground = "#1565C0",
                Background = "#f0f4f8",
                Surface = "#ffffff",
                DrawerBackground = "#ffffff",
                DrawerText = "#37474F",
                DrawerIcon = "#607D8B",
                AppbarText = "#ffffff"
            },
            Dark = new PaletteDark
            {
                Primary = "#42A5F5",
                Secondary = "#FFB74D",
                AppbarBackground = "#0d1b2a",
                Background = "#0a1628",
                Surface = "#0d1b2a",
                DrawerBackground = "#0b1724",
                DrawerText = "#e0e0e0",
                DrawerIcon = "#90CAF9",
                AppbarText = "#ffffff",
                TextPrimary = "#e0e0e0",
                TextSecondary = "#90a4ae",
                ActionDefault = "#90a4ae",
                Divider = "#1b3a5c"
            }
        },
        ["Forest Green"] = new ThemeDefinition
        {
            Name = "Forest Green",
            Icon = Icons.Material.Filled.Forest,
            PreviewColor = "#2E7D32",
            Light = new PaletteLight
            {
                Primary = "#2E7D32",
                Secondary = "#F57C00",
                AppbarBackground = "#2E7D32",
                Background = "#f1f8e9",
                Surface = "#ffffff",
                DrawerBackground = "#ffffff",
                DrawerText = "#33691E",
                DrawerIcon = "#689F38",
                AppbarText = "#ffffff"
            },
            Dark = new PaletteDark
            {
                Primary = "#66BB6A",
                Secondary = "#FFB74D",
                AppbarBackground = "#1a2e1a",
                Background = "#0f1a0f",
                Surface = "#1a2e1a",
                DrawerBackground = "#162416",
                DrawerText = "#e0e0e0",
                DrawerIcon = "#a5d6a7",
                AppbarText = "#ffffff",
                TextPrimary = "#e0e0e0",
                TextSecondary = "#a0b0a0",
                ActionDefault = "#a0b0a0",
                Divider = "#2d4a2d"
            }
        },
        ["Sunset"] = new ThemeDefinition
        {
            Name = "Sunset",
            Icon = Icons.Material.Filled.WbSunny,
            PreviewColor = "#E65100",
            Light = new PaletteLight
            {
                Primary = "#E65100",
                Secondary = "#6A1B9A",
                AppbarBackground = "#E65100",
                Background = "#FFF3E0",
                Surface = "#ffffff",
                DrawerBackground = "#ffffff",
                DrawerText = "#4E342E",
                DrawerIcon = "#8D6E63",
                AppbarText = "#ffffff"
            },
            Dark = new PaletteDark
            {
                Primary = "#FF8A65",
                Secondary = "#CE93D8",
                AppbarBackground = "#2e1a0d",
                Background = "#1a0f08",
                Surface = "#2e1a0d",
                DrawerBackground = "#241408",
                DrawerText = "#e0e0e0",
                DrawerIcon = "#FFAB91",
                AppbarText = "#ffffff",
                TextPrimary = "#e0e0e0",
                TextSecondary = "#b0a090",
                ActionDefault = "#b0a090",
                Divider = "#4a2d1a"
            }
        }
    };

    public void SetTheme(string themeName, bool isDark)
    {
        if (!AvailableThemes.ContainsKey(themeName)) return;
        _currentThemeName = themeName;
        _isDarkMode = isDark;
        OnThemeChanged?.Invoke();
    }

    public void ToggleDarkMode()
    {
        _isDarkMode = !_isDarkMode;
        OnThemeChanged?.Invoke();
    }

    public void SetFromStorage(string? themeName, bool isDark)
    {
        if (!string.IsNullOrEmpty(themeName) && AvailableThemes.ContainsKey(themeName))
            _currentThemeName = themeName;
        _isDarkMode = isDark;
    }

    private static MudTheme GetThemeByName(string name)
    {
        var def = AvailableThemes.GetValueOrDefault(name) ?? AvailableThemes["Purple"];
        return new MudTheme
        {
            PaletteLight = def.Light,
            PaletteDark = def.Dark,
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = new[] { "Roboto", "Helvetica Neue", "Helvetica", "Arial", "sans-serif" }
                }
            },
            LayoutProperties = new LayoutProperties
            {
                DrawerWidthLeft = "260px"
            }
        };
    }
}

public class ThemeDefinition
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string PreviewColor { get; set; } = "";
    public PaletteLight Light { get; set; } = new();
    public PaletteDark Dark { get; set; } = new();
}
