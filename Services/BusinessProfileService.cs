using System.IO;
using System.Text.Json;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Centralized service for managing business profiles.
/// Holds the two pre-configured profiles and controls which one is active.
/// Persists selection to the Settings table and to a local JSON file.
/// </summary>
public static class BusinessProfileService
{
    // ── Profile IDs (constants used throughout the app) ────────────────────
    public const string ANTIQUE_PASABUY = "ANTIQUE_PASABUY";
    public const string ANGELITOS_RESTO = "ANGELITOS_RESTO";

    private const string SettingsKey = "active_business_profile";
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "business_profile.json");

    // ── Pre-configured Profiles ───────────────────────────────────────────

    public static readonly BusinessProfile AntiquePasabuyProfile = new()
    {
        Id = ANTIQUE_PASABUY,
        Name = "Antique Pasabuy",
        SystemName = "DIONIX POS",
        Subtitle = "ANTIQUE PASABUY & RETAIL",
        BusinessType = "Retail",
        LogoResource = "pack://application:,,,/pasabuy logo 2.png",

        // Login screen
        LottieFileName = "Business team.json",
        LoginSidebarColor = "#0F172A",
        LoginSidebarGlowColor = "#1E293B",
        LoginBrandingTitle = "DIONIX POS",
        LoginBrandingSubtitle = "Pasabuy · Delivery · Retail",
        LoginTagline = "Your Orders. Our Hands. Delivered.",
        LoginButtonColor = "#452B86",
        LoginButtonHoverColor = "#5B43E8",
        LoginButtonPressedColor = "#351E6E",

        // Official DIONIX POS Business Color Palette
        PrimaryColor = "#452B86",
        SecondaryColor = "#5B43E8",
        SidebarDarkColor = "#10182B",
        SidebarBorderColor = "#1B243B",
        SidebarBottomColor = "#0D1322",
        HoverColor = "#5B43E8",
        ActiveNavBgColor = "#452B86",
        ActiveNavBorderColor = "#5B43E8",
        HoverNavBgColor = "#1A253E",
        HoverNavBorderColor = "#2B3A60",
        ChildActiveNavBgColor = "#1A253E",
        ChildActiveNavBorderColor = "#5B43E8",
        NavTextColor = "#94A3B8",
        NavGroupColor = "#647089",
        AvatarGradientStart = "#452B86",
        AvatarGradientEnd = "#5B43E8",
        AvatarRoleBgColor = "#1A253E",
        AvatarRoleTextColor = "#C4B5FD",
        BrandAccentColor = "#C4B5FD",
        LightAccentColor = "#EDE9FE",
        BackgroundColor = "#F5F7FB",
        CardColor = "#FFFFFF",
        TextColor = "#20263A",
        SecondaryTextColor = "#647089",
        BorderColor = "#E1E6EF",
        PressedColor = "#351E6E",

        DarkBackgroundColor = "#0B0F19",
        DarkSurfaceColor = "#111827",
        DarkPanelColor = "#1F2937",
        DarkPrimaryColor = "#818CF8",
        DarkSecondaryColor = "#A5B4FC",
        DarkPrimaryTextColor = "#F8FAFC",
        DarkSecondaryTextColor = "#94A3B8",
        DarkMutedTextColor = "#64748B",
        DarkBorderColor = "#1F2937",

        PasabuyFeeEnabled = true,
        DefaultPasabuyFee = 50m,
        Address = "San Jose de Buenavista, Antique",
        ContactNumber = "",
        ReceiptHeader = "DIONIX POS — ANTIQUE PASABUY & RETAIL",
        ReceiptFooter = "Thank you for trusting Antique Pasabuy!",

        QuickActionLabel = "NEW PURCHASE ORDER",
        QuickActionTooltip = "Create a new Purchase Order",
        QuickActionNavId = "NewOrder",
    };

    public static readonly BusinessProfile AngelitosProfile = new()
    {
        Id = ANGELITOS_RESTO,
        Name = "Angelito's Bar & Resto Grill",
        SystemName = "DIONIX POS",
        Subtitle = "BAR & RESTO GRILL",
        BusinessType = "Restaurant",
        LogoResource = "", // Configurable — placeholder until actual logo provided

        // Login screen
        LottieFileName = "Restaurant.json",
        LoginSidebarColor = "#1E1010",
        LoginSidebarGlowColor = "#3D2020",
        LoginBrandingTitle = "DIONIX POS",
        LoginBrandingSubtitle = "Angelito's Bar & Resto Grill",
        LoginTagline = "Serving Excellence. Every Day.",
        LoginButtonColor = "#4A2C2C",
        LoginButtonHoverColor = "#6B3E3E",
        LoginButtonPressedColor = "#291818",

        // Warm brown/restaurant theme based on #291818
        PrimaryColor = "#291818",
        SecondaryColor = "#4A2C2C",
        SidebarDarkColor = "#1E1010",
        SidebarBorderColor = "#3D2222",
        SidebarBottomColor = "#170C0C",
        HoverColor = "#4A2C2C",
        ActiveNavBgColor = "#3D2222",
        ActiveNavBorderColor = "#5C3A3A",
        HoverNavBgColor = "#332020",
        HoverNavBorderColor = "#4A2C2C",
        ChildActiveNavBgColor = "#332020",
        ChildActiveNavBorderColor = "#3D2222",
        NavTextColor = "#C9ADA7",
        NavGroupColor = "#B8998F",
        AvatarGradientStart = "#4A2C2C",
        AvatarGradientEnd = "#8B5E5E",
        AvatarRoleBgColor = "#332020",
        AvatarRoleTextColor = "#D4B5AE",
        BrandAccentColor = "#D4B5AE",
        LightAccentColor = "#F5EEEB",
        BackgroundColor = "#FAF7F5",
        CardColor = "#FFFFFF",
        TextColor = "#2D1F1F",
        SecondaryTextColor = "#7A6565",
        BorderColor = "#E8DDD8",
        PressedColor = "#1E1010",

        DarkBackgroundColor = "#1A0F0F",
        DarkSurfaceColor = "#241616",
        DarkPanelColor = "#1E1010",
        DarkPrimaryColor = "#C9877A",
        DarkSecondaryColor = "#D4A599",
        DarkPrimaryTextColor = "#FAF7F5",
        DarkSecondaryTextColor = "#C9ADA7",
        DarkMutedTextColor = "#9A7E78",
        DarkBorderColor = "#3D2222",

        PasabuyFeeEnabled = false,
        DefaultPasabuyFee = 0m,
        Address = "",
        ContactNumber = "",
        ReceiptHeader = "ANGELITO'S BAR & RESTO GRILL",
        ReceiptFooter = "Thank you for dining with us!",

        QuickActionLabel = "NEW SALE",
        QuickActionTooltip = "Create a new Sale",
        QuickActionNavId = "RestoSales",
    };

    // ── All Profiles ──────────────────────────────────────────────────────

    public static readonly BusinessProfile[] AllProfiles = [AntiquePasabuyProfile, AngelitosProfile];

    // ── Active Profile ────────────────────────────────────────────────────

    private static BusinessProfile _activeProfile = AntiquePasabuyProfile;

    public static BusinessProfile ActiveProfile => _activeProfile;
    public static bool IsPasabuy => _activeProfile.Id == ANTIQUE_PASABUY;
    public static bool IsAngelitos => _activeProfile.Id == ANGELITOS_RESTO;

    /// <summary>Raised whenever the active business profile changes.</summary>
    public static event Action<BusinessProfile>? ProfileChanged;

    /// <summary>
    /// Returns whether the Pasabuy Fee is enabled for the active profile.
    /// Reads the DB-persisted setting (saved by the Settings page) first,
    /// falling back to the profile's hardcoded default.
    /// Always pass a valid db context so the user's saved setting is respected.
    /// </summary>
    public static bool IsPasabuyFeeEnabled(AppDbContext? db = null)
    {
        var bp = _activeProfile;
        if (db == null) return bp.PasabuyFeeEnabled;
        try
        {
            var raw = SeedData.GetSetting(db, $"pasabuy_fee_enabled_{bp.Id}", bp.PasabuyFeeEnabled.ToString());
            return bool.TryParse(raw, out var result) ? result : bp.PasabuyFeeEnabled;
        }
        catch
        {
            return bp.PasabuyFeeEnabled;
        }
    }

    // ── Initialize (called once at app startup) ───────────────────────────

    public static void Initialize(AppDbContext? db = null)
    {
        var savedId = "";

        // Try DB first
        if (db != null)
        {
            try
            {
                savedId = SeedData.GetSetting(db, SettingsKey, "");
            }
            catch { }
        }

        // Fallback to local JSON
        if (string.IsNullOrWhiteSpace(savedId))
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ActiveProfile", out var prop))
                        savedId = prop.GetString() ?? "";
                }
            }
            catch { }
        }

        _activeProfile = GetProfileById(savedId) ?? AntiquePasabuyProfile;
        ThemeManager.ReapplyForActiveProfile();
    }

    // ── Switch Profile ────────────────────────────────────────────────────

    public static void SetActiveProfile(string profileId, AppDbContext? db = null)
    {
        var profile = GetProfileById(profileId);
        if (profile == null) return;

        _activeProfile = profile;
        ThemeManager.ReapplyForActiveProfile();

        // Persist to DB
        if (db != null)
        {
            try { SeedData.SetSetting(db, SettingsKey, profileId); }
            catch { }
        }

        // Persist to local JSON (survives even if DB setting lost)
        try
        {
            var payload = new { ActiveProfile = profileId };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(payload));
        }
        catch { }

        // Notify listeners
        ProfileChanged?.Invoke(_activeProfile);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    public static BusinessProfile? GetProfileById(string? id) => id switch
    {
        ANTIQUE_PASABUY => AntiquePasabuyProfile,
        ANGELITOS_RESTO => AngelitosProfile,
        _ => null
    };

    /// <summary>
    /// Gets the display name for profile selection dropdowns.
    /// </summary>
    public static string GetDisplayName(BusinessProfile profile) => profile.Id switch
    {
        ANTIQUE_PASABUY => "Antique Pasabuy POS",
        ANGELITOS_RESTO => "Angelito's Bar & Resto Grill",
        _ => profile.Name
    };
}
