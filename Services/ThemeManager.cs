using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using AntiquePasabuyPOS.Models;
using MaterialDesignThemes.Wpf;

namespace AntiquePasabuyPOS.Services;

public enum AppTheme
{
    Light,
    Dark
}

/// <summary>
/// Centralized Theme Manager providing zero-restart Light/Dark mode switching,
/// persistence in theme_config.json, MaterialDesign sync, and DynamicResource updates.
/// Now business-profile-aware: applies the correct color palette for each business.
/// </summary>
public static class ThemeManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "theme_config.json");

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public static event Action<AppTheme>? ThemeChanged;

    public static void Initialize()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Theme", out var prop))
                {
                    var val = prop.GetString();
                    if (string.Equals(val, "Dark", StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentTheme = AppTheme.Dark;
                    }
                }
            }
        }
        catch
        {
            CurrentTheme = AppTheme.Light;
        }

        ApplyTheme(CurrentTheme);
    }

    public static void ToggleTheme()
    {
        ApplyTheme(CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        bool isDark = theme == AppTheme.Dark;
        var bp = BusinessProfileService.ActiveProfile;

        // 1. MaterialDesign Palette Helper Sync
        try
        {
            var paletteHelper = new PaletteHelper();
            var mdTheme = paletteHelper.GetTheme();
            mdTheme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(mdTheme);
        }
        catch
        {
            // Ignored if called before Application is initialized
        }

        // 2. Update Application.Current.Resources DynamicResource Brushes
        //    Uses the active BusinessProfile's color palette
        if (Application.Current?.Resources != null)
        {
            SetBrush("PosBackground",     isDark ? bp.DarkBackgroundColor     : bp.BackgroundColor);
            SetBrush("PosSurface",        isDark ? bp.DarkSurfaceColor        : bp.CardColor);
            SetBrush("PosPanel",          isDark ? bp.DarkPanelColor          : bp.LightAccentColor);
            SetBrush("PosPrimary",        isDark ? bp.DarkPrimaryColor        : bp.PrimaryColor);
            SetBrush("PosSecondary",      isDark ? bp.DarkSecondaryColor      : bp.SecondaryColor);
            SetBrush("PosLightBlue",      isDark ? bp.DarkPanelColor          : bp.LightAccentColor);
            SetBrush("PosLightPurple",    isDark ? bp.DarkPanelColor          : bp.LightAccentColor);
            SetBrush("PosPrimaryText",    isDark ? bp.DarkPrimaryTextColor    : bp.TextColor);
            SetBrush("PosSecondaryText",  isDark ? bp.DarkSecondaryTextColor  : bp.SecondaryTextColor);
            SetBrush("PosMutedText",      isDark ? bp.DarkMutedTextColor      : bp.SecondaryTextColor);
            SetBrush("PosBorder",         isDark ? bp.DarkBorderColor         : bp.BorderColor);
            SetBrush("PosInput",          isDark ? bp.DarkSurfaceColor        : bp.CardColor);
            SetBrush("PosInputText",      isDark ? bp.DarkPrimaryTextColor    : bp.TextColor);
            SetBrush("PosTableHeader",    isDark ? bp.DarkPanelColor          : bp.LightAccentColor);
            SetBrush("PosTableRow",       isDark ? bp.DarkSurfaceColor        : bp.CardColor);
            SetBrush("PosAlternateRow",   isDark ? bp.DarkPanelColor          : "#FAF9FD");

            // Semantic Status Colors (unchanged across profiles)
            SetBrush("PosSuccess",        isDark ? "#10B981" : "#10B981");
            SetBrush("PosWarning",        isDark ? "#F59E0B" : "#F59E0B");
            SetBrush("PosDanger",         isDark ? "#EF4444" : "#EF4444");
            SetBrush("PosInfo",           isDark ? bp.DarkSecondaryColor : bp.PrimaryColor);
        }

        // 3. Persist Selected Theme Locally
        try
        {
            var payload = new
            {
                Theme = theme.ToString(),
                ScaleMode = DisplayScaleManager.CurrentMode.ToString(),
                CustomScale = DisplayScaleManager.CustomScalePercent
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(payload));
        }
        catch
        {
            // Ignore persistence errors in read-only environments
        }

        // 4. Notify Listeners
        ThemeChanged?.Invoke(CurrentTheme);
    }

    /// <summary>
    /// Reapplies the current theme using the active business profile's palette.
    /// Call this after switching business profiles.
    /// </summary>
    public static void ReapplyForActiveProfile()
    {
        ApplyTheme(CurrentTheme);
    }

    private static void SetBrush(string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }
}
