using System.Windows.Media;
using FluxForge.Application.Settings;
using Wpf.Ui.Appearance;

namespace FluxForge.Desktop.Services;

public sealed class ThemeService(ISettingsStore settings)
{
    private static readonly Color Accent = Color.FromRgb(0x0A, 0x84, 0xFF);

    public bool IsDark { get; private set; } = true;

    public void ApplySaved() => Apply(settings.Load().Theme != "Light");

    public void Apply(bool dark)
    {
        IsDark = dark;
        var theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.Mica, true);
        ApplicationAccentColorManager.Apply(Accent, theme, false, false);
        settings.Save(settings.Load() with { Theme = dark ? "Dark" : "Light" });
    }
}
