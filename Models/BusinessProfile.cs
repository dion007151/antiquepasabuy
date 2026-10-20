namespace AntiquePasabuyPOS.Models;

/// <summary>
/// Represents a business profile configuration.
/// This is NOT a database entity — it is a static configuration object
/// that defines branding, theme colors, features, and identity for each business.
/// </summary>
public class BusinessProfile
{
    // ── Identity ──────────────────────────────────────────────────────────
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string SystemName { get; init; } = "DIONIX POS";
    public string Subtitle { get; init; } = "";
    public string BusinessType { get; init; } = "Retail"; // Retail, Restaurant

    // ── Branding ──────────────────────────────────────────────────────────
    /// <summary>WPF pack:// URI for the logo resource, or empty if no logo.</summary>
    public string LogoResource { get; init; } = "";

    // ── Login Screen Customization ────────────────────────────────────────
    /// <summary>Filename of the Lottie JSON animation shown on the login screen left panel.</summary>
    public string LottieFileName { get; init; } = "Business team.json";
    /// <summary>Background color of the login left panel (hex).</summary>
    public string LoginSidebarColor { get; init; } = "#241548";
    /// <summary>Gradient mid-color for the login left panel glow (hex).</summary>
    public string LoginSidebarGlowColor { get; init; } = "#3D2375";
    /// <summary>First branding line shown at the bottom of the login panel.</summary>
    public string LoginBrandingTitle { get; init; } = "DIONIX POS";
    /// <summary>Second branding line (business name/type) shown on login panel.</summary>
    public string LoginBrandingSubtitle { get; init; } = "Antique Pasabuy & Retail";
    /// <summary>Tagline shown beneath the separator on login panel.</summary>
    public string LoginTagline { get; init; } = "Your Orders. Our Hands. Delivered.";
    /// <summary>Accent color for the login button background (hex).</summary>
    public string LoginButtonColor { get; init; } = "#452B86";
    /// <summary>Hover color for the login button (hex).</summary>
    public string LoginButtonHoverColor { get; init; } = "#5A3BA8";
    /// <summary>Pressed color for the login button (hex).</summary>
    public string LoginButtonPressedColor { get; init; } = "#351F68";


    // ── Theme Colors ──────────────────────────────────────────────────────
    public string PrimaryColor { get; init; } = "#452B86";
    public string SecondaryColor { get; init; } = "#5A3BA8";
    public string SidebarDarkColor { get; init; } = "#241548";
    public string SidebarBorderColor { get; init; } = "#382463";
    public string SidebarBottomColor { get; init; } = "#1B0E38";
    public string HoverColor { get; init; } = "#5A3BA8";
    public string ActiveNavBgColor { get; init; } = "#452B86";
    public string ActiveNavBorderColor { get; init; } = "#5A3BA8";
    public string HoverNavBgColor { get; init; } = "#351F68";
    public string HoverNavBorderColor { get; init; } = "#4A2E8A";
    public string ChildActiveNavBgColor { get; init; } = "#351F68";
    public string ChildActiveNavBorderColor { get; init; } = "#452B86";
    public string NavTextColor { get; init; } = "#B8AFCE";
    public string NavGroupColor { get; init; } = "#A99FC5";
    public string AvatarGradientStart { get; init; } = "#452B86";
    public string AvatarGradientEnd { get; init; } = "#7C3AED";
    public string AvatarRoleBgColor { get; init; } = "#351F68";
    public string AvatarRoleTextColor { get; init; } = "#C4B5FD";
    public string BrandAccentColor { get; init; } = "#C4B5FD";
    public string LightAccentColor { get; init; } = "#F1EDFA";
    public string BackgroundColor { get; init; } = "#F7F6FA";
    public string CardColor { get; init; } = "#FFFFFF";
    public string TextColor { get; init; } = "#1F1B2D";
    public string SecondaryTextColor { get; init; } = "#716B80";
    public string BorderColor { get; init; } = "#E5E1F0";
    public string PressedColor { get; init; } = "#2E185B";

    // ── Dark Mode Overrides ───────────────────────────────────────────────
    public string DarkBackgroundColor { get; init; } = "#160D2E";
    public string DarkSurfaceColor { get; init; } = "#1E123D";
    public string DarkPanelColor { get; init; } = "#241548";
    public string DarkPrimaryColor { get; init; } = "#7C5CD6";
    public string DarkSecondaryColor { get; init; } = "#987CE8";
    public string DarkPrimaryTextColor { get; init; } = "#F7F6FA";
    public string DarkSecondaryTextColor { get; init; } = "#C4BCDE";
    public string DarkMutedTextColor { get; init; } = "#8E82AA";
    public string DarkBorderColor { get; init; } = "#382463";

    // ── Feature Flags ─────────────────────────────────────────────────────
    public bool PasabuyFeeEnabled { get; init; } = true;
    public bool EnablePasabuyFee { get => PasabuyFeeEnabled; init => PasabuyFeeEnabled = value; }
    public string PasabuyFeeLabel { get; init; } = "Pasabuy Fee";
    public decimal DefaultPasabuyFee { get; init; } = 50m;

    // ── Business Details ──────────────────────────────────────────────────
    public string Address { get; init; } = "";
    public string ContactNumber { get; init; } = "";
    public string ReceiptHeader { get; init; } = "";
    public string ReceiptFooter { get; init; } = "Thank you for your patronage!";

    // ── Quick-Action Button ───────────────────────────────────────────────
    public string QuickActionLabel { get; init; } = "NEW PURCHASE ORDER";
    public string QuickActionTooltip { get; init; } = "Create a new Purchase Order";
    public string QuickActionNavId { get; init; } = "NewOrder";
}
