using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace AntiquePasabuyPOS.Services;

public enum UiScaleMode
{
    Auto,
    Small,    // 85%
    Compact,  // 92%
    Normal,   // 100%
    Large,    // 115%
    Custom    // user-defined 80%–150%
}

/// <summary>
/// Manages application-wide UI scaling (Auto screen-aware, Small, Compact, Normal, Large, Custom)
/// with live vector LayoutTransform updates, local persistence, and zero-restart responsiveness.
/// </summary>
public static class DisplayScaleManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "theme_config.json");

    public static UiScaleMode CurrentMode { get; private set; } = UiScaleMode.Auto;
    public static int CustomScalePercent { get; private set; } = 100;
    public static double CurrentScaleFactor { get; private set; } = 1.0;

    public static event Action<double>? ScaleChanged;

    public static void Initialize()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ScaleMode", out var modeProp))
                {
                    if (Enum.TryParse<UiScaleMode>(modeProp.GetString(), true, out var parsed))
                    {
                        CurrentMode = parsed;
                    }
                }
                if (doc.RootElement.TryGetProperty("CustomScale", out var customProp) && customProp.TryGetInt32(out var customVal))
                {
                    CustomScalePercent = Math.Clamp(customVal, 80, 150);
                }
            }
        }
        catch
        {
            CurrentMode = UiScaleMode.Auto;
            CustomScalePercent = 100;
        }

        ApplyScale(CurrentMode, CustomScalePercent, save: false);
    }

    public static double CalculateEffectiveScale(UiScaleMode mode, int customPercent, double winWidth = 0, double winHeight = 0)
    {
        switch (mode)
        {
            case UiScaleMode.Small:
                return 0.85;
            case UiScaleMode.Compact:
                return 0.92;
            case UiScaleMode.Normal:
                return 1.00;
            case UiScaleMode.Large:
                return 1.15;
            case UiScaleMode.Custom:
                return Math.Clamp(customPercent / 100.0, 0.80, 1.50);
            case UiScaleMode.Auto:
            default:
                // Screen-aware automatic scaling detection
                double screenW = winWidth > 0 ? winWidth : SystemParameters.PrimaryScreenWidth;
                double screenH = winHeight > 0 ? winHeight : SystemParameters.PrimaryScreenHeight;

                if (screenH <= 768 || screenW <= 1366)
                {
                    // Small laptop screen (e.g. 1366x768): use compact density
                    return 0.90;
                }
                else if (screenW >= 2560 || screenH >= 1440)
                {
                    // High-resolution / large monitor
                    return 1.15;
                }
                else if (screenW <= 1600 && screenH <= 900)
                {
                    return 0.95;
                }
                else
                {
                    // Standard 1080p desktop POS
                    return 1.00;
                }
        }
    }

    public static void ApplyScale(UiScaleMode mode, int customPercent = 100, double winWidth = 0, double winHeight = 0, bool save = true)
    {
        CurrentMode = mode;
        CustomScalePercent = Math.Clamp(customPercent, 80, 150);
        CurrentScaleFactor = CalculateEffectiveScale(mode, CustomScalePercent, winWidth, winHeight);

        if (save)
        {
            SaveConfig();
        }

        ScaleChanged?.Invoke(CurrentScaleFactor);
    }

    public static void UpdateAutoScale(double winWidth, double winHeight)
    {
        if (CurrentMode != UiScaleMode.Auto) return;

        double newScale = CalculateEffectiveScale(UiScaleMode.Auto, 100, winWidth, winHeight);
        if (Math.Abs(newScale - CurrentScaleFactor) > 0.01)
        {
            CurrentScaleFactor = newScale;
            ScaleChanged?.Invoke(CurrentScaleFactor);
        }
    }

    private static void SaveConfig()
    {
        try
        {
            var existingTheme = ThemeManager.CurrentTheme.ToString();
            var payload = new
            {
                Theme = existingTheme,
                ScaleMode = CurrentMode.ToString(),
                CustomScale = CustomScalePercent
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(payload));
        }
        catch
        {
            // Ignore persistence errors in read-only environments
        }
    }
}
