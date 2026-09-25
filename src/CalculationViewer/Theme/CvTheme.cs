using MudBlazor;

namespace CalculationViewer.Theme;

/// <summary>Палітра «синька на холодному папері». Шрифти й дрібні деталі задаються в wwwroot/css/app.css.</summary>
public static class CvTheme
{
    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2557d6",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d99a0b",
            SecondaryContrastText = "#1a1200",
            Info = "#2557d6",
            Success = "#17805a",
            Warning = "#a76400",
            Error = "#c23a3a",
            Background = "#f2f5f6",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#14212b",
            DrawerBackground = "#ffffff",
            DrawerText = "#14212b",
            DrawerIcon = "#4a5b69",
            TextPrimary = "#14212b",
            TextSecondary = "#4a5b69",
            ActionDefault = "#4a5b69",
            Divider = "#dbe3e8",
            DividerLight = "#e6ecf0",
            LinesDefault = "#dbe3e8",
            LinesInputs = "#b7c4ce",
            TableLines = "#dbe3e8",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#8aa9ff",
            PrimaryContrastText = "#0b1220",
            Secondary = "#f0b840",
            SecondaryContrastText = "#1a1200",
            Info = "#8aa9ff",
            Success = "#4cc39a",
            Warning = "#f0b840",
            Error = "#ff8a8a",
            Background = "#0e151b",
            Surface = "#151e26",
            AppbarBackground = "#151e26",
            AppbarText = "#e6edf2",
            DrawerBackground = "#151e26",
            DrawerText = "#e6edf2",
            DrawerIcon = "#9fb0be",
            TextPrimary = "#e6edf2",
            TextSecondary = "#9fb0be",
            ActionDefault = "#9fb0be",
            Divider = "#263442",
            DividerLight = "#1e2a35",
            LinesDefault = "#263442",
            LinesInputs = "#3a4b5b",
            TableLines = "#263442",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            AppbarHeight = "56px",
            DrawerWidthLeft = "248px",
        },
    };
}
