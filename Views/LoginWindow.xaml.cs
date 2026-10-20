using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.ViewModels;

namespace AntiquePasabuyPOS.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(AppDbContext db)
    {
        InitializeComponent();
        _vm = new LoginViewModel(db);
        _vm.OnLoginSuccess = user => { OpenMain(db, user); };
        DataContext = _vm;

        Loaded += (_, _) =>
        {
            ApplyBusinessProfile();
            InitializeLottieBackground();
            UsernameBox.Focus();
        };
    }

    // ── Apply the active business profile to the login screen ──────────────

    private void ApplyBusinessProfile()
    {
        var p = BusinessProfileService.ActiveProfile;

        // ── Left panel background colors ──────────────────────────────────
        var sidebarColor = ParseColor(p.LoginSidebarColor, Color.FromRgb(0x24, 0x15, 0x48));
        var glowColor    = ParseColor(p.LoginSidebarGlowColor, Color.FromRgb(0x3D, 0x23, 0x75));

        LeftPanelGrid.Background = new SolidColorBrush(sidebarColor);

        // Radial glow gradient
        var radialBrush = new RadialGradientBrush
        {
            Center         = new System.Windows.Point(0.5, 0.4),
            GradientOrigin = new System.Windows.Point(0.5, 0.4),
            RadiusX        = 0.75,
            RadiusY        = 0.75,
            MappingMode    = BrushMappingMode.RelativeToBoundingBox
        };
        radialBrush.GradientStops.Add(new GradientStop(glowColor, 0));
        radialBrush.GradientStops.Add(new GradientStop(sidebarColor, 1));
        LeftPanelGlowBorder.Background = radialBrush;

        // Bottom gradient overlay uses the sidebar color with transparency
        var gradBrush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint   = new System.Windows.Point(0, 1)
        };
        gradBrush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x00, sidebarColor.R, sidebarColor.G, sidebarColor.B), 0));
        gradBrush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0xD9, sidebarColor.R, sidebarColor.G, sidebarColor.B), 0.6));
        gradBrush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0xFF, sidebarColor.R, sidebarColor.G, sidebarColor.B), 1));
        BottomGradientBorder.Background = gradBrush;

        // ── Branding texts ─────────────────────────────────────────────────
        BrandingTitle.Text    = p.LoginBrandingTitle;
        BrandingSubtitle.Text = p.LoginBrandingSubtitle;
        BrandingTagline.Text  = p.LoginTagline;

        // ── Login button color ─────────────────────────────────────────────
        var btnBg      = ParseColor(p.LoginButtonColor, Color.FromRgb(0x45, 0x2B, 0x86));
        var btnHover   = ParseColor(p.LoginButtonHoverColor, Color.FromRgb(0x5A, 0x3B, 0xA8));
        var btnPressed = ParseColor(p.LoginButtonPressedColor, Color.FromRgb(0x35, 0x1F, 0x68));

        LoginBtn.Tag = new LoginBtnColors(btnBg, btnHover, btnPressed);
        LoginBtn.Background = new SolidColorBrush(btnBg);
        LoginBtn.BorderBrush = new SolidColorBrush(btnBg);
    }

    // ── Load the correct Lottie file for the active profile ────────────────

    private void InitializeLottieBackground()
    {
        try
        {
            var fileName = BusinessProfileService.ActiveProfile.LottieFileName;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "Business team.json";

            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", fileName),
            };

            var jsonFile = candidates.FirstOrDefault(File.Exists);
            if (jsonFile != null && LottiePlayer != null)
            {
                LottiePlayer.FileName = Path.GetFullPath(jsonFile);
                try { LottiePlayer.PlayAnimation(); } catch { }
            }
        }
        catch
        {
            // Graceful fallback — animation simply won't play
        }
    }

    // ── Login actions ──────────────────────────────────────────────────────

    private void OnLoginClick(object s, RoutedEventArgs e) => TryLogin();

    private void OnKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (s == UsernameBox && string.IsNullOrWhiteSpace(PasswordBox.Password))
                PasswordBox.Focus();
            else
                TryLogin();
        }
    }

    private void TryLogin()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        ErrorText.Text = "";

        _vm.Username = UsernameBox.Text?.Trim() ?? "";
        _vm.Password = PasswordBox.Password ?? "";
        _vm.LoginCommand.Execute(null);

        if (!string.IsNullOrEmpty(_vm.LoginError))
        {
            ErrorText.Text = _vm.LoginError;
            ErrorBanner.Visibility = Visibility.Visible;
        }
    }

    private void OpenMain(AppDbContext db, User user)
    {
        var main = new MainWindow(db, user);
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { LottiePlayer?.StopAnimation(); } catch { }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var converted = ColorConverter.ConvertFromString(hex);
                if (converted is Color c) return c;
            }
        }
        catch { }
        return fallback;
    }

    // Simple data bag so the button hover trigger can access the colors
    public record LoginBtnColors(Color Normal, Color Hover, Color Pressed);
}
