using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Pages;

namespace AntiquePasabuyPOS.Views;

public partial class MainWindow : Window
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly DispatcherTimer _clockTimer;

    // ── Flat navigation state ─────────────────────────────────────────────
    private string _currentNavId = "Dashboard";

    /// <summary>
    /// Maps every navigation ID to the flat sidebar button that represents it.
    /// </summary>
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);

    // ── Constructor ───────────────────────────────────────────────────────
    public MainWindow(AppDbContext db, User user)
    {
        InitializeComponent();
        _db = db;
        _user = user;

        // User profile display
        var parts = user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        AvatarInitials.Text = parts.Length > 0
            ? string.Concat(parts.Take(2).Select(p => char.ToUpper(p[0]).ToString()))
            : "?";
        UserNameLabel.Text = user.FullName;
        UserRoleLabel.Text = user.Role.ToString();
        DateLabel.Text     = DateTime.Now.ToString("ddd, MMM dd yyyy");

        // ── Live date/clock: refresh the date label every minute so it stays
        //    accurate even if the app is left running overnight.
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _clockTimer.Tick += (_, _) =>
        {
            DateLabel.Text = DateTime.Now.ToString("ddd, MMM dd yyyy");
        };
        _clockTimer.Start();

        Closed += (_, _) => _clockTimer.Stop();

        // Theme
        UpdateThemeIcon(AntiquePasabuyPOS.Services.ThemeManager.CurrentTheme);
        AntiquePasabuyPOS.Services.ThemeManager.ThemeChanged += UpdateThemeIcon;

        // Multi-business profile change listener
        AntiquePasabuyPOS.Services.BusinessProfileService.ProfileChanged += OnProfileChanged;

        // Display scale
        AntiquePasabuyPOS.Services.DisplayScaleManager.ScaleChanged += OnScaleChanged;
        OnScaleChanged(AntiquePasabuyPOS.Services.DisplayScaleManager.CurrentScaleFactor);
        SizeChanged += OnWindowSizeChanged;

        RegisterNavButtons();
        UpdateSidebarBranding();
        UpdateActiveBatchDisplay();

        if (AntiquePasabuyPOS.Services.BusinessProfileService.IsAngelitos)
            Navigate("RestoDashboard");
        else
            Navigate("Dashboard");
    }

    private void UpdateActiveBatchDisplay()
    {
        if (ActiveBatchBadge != null)
            ActiveBatchBadge.Visibility = Visibility.Collapsed;
    }

    private void OnSwitchActiveBatch(object sender, RoutedEventArgs e)
    {
        var currentBatch = SeedData.GetSetting(_db, "active_batch_code", "AUG 2");
        var dialog = new Window
        {
            Title = "Switch Active Trip Batch",
            Width = 360,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = "Active Batch / Trip Code",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "e.g., AUG 2, AUG 3, JCO aug4. New orders will automatically use this batch.",
            FontSize = 11,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        var textBox = new TextBox
        {
            Text = currentBatch,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(textBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
        cancelBtn.Click += (_, _) => dialog.Close();

        var saveBtn = new Button
        {
            Content = "Set Active Batch",
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 43, 134)),
            Foreground = System.Windows.Media.Brushes.White,
            Padding = new Thickness(16, 6, 16, 6),
            FontWeight = FontWeights.Bold
        };
        saveBtn.Click += (_, _) =>
        {
            var val = textBox.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(val))
            {
                MessageBox.Show("Please enter a valid batch code.", "Invalid Batch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SeedData.SetSetting(_db, "active_batch_code", val);
            UpdateActiveBatchDisplay();
            OrderEventBus.NotifyDataRefreshed();
            dialog.Close();
        };

        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(saveBtn);
        stack.Children.Add(btnPanel);

        dialog.Content = stack;
        dialog.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
        dialog.ShowDialog();
    }

    private void OnProfileChanged(BusinessProfile profile)
    {
        Dispatcher.Invoke(() =>
        {
            _pageCache.Clear();
            RegisterNavButtons();
            UpdateSidebarBranding();

            if (AntiquePasabuyPOS.Services.BusinessProfileService.IsAngelitos)
                Navigate("RestoDashboard");
            else
                Navigate("Dashboard");
        });
    }

    private void UpdateSidebarBranding()
    {
        var bp = AntiquePasabuyPOS.Services.BusinessProfileService.ActiveProfile;
        bool isAngelitos = AntiquePasabuyPOS.Services.BusinessProfileService.IsAngelitos;

        // 1. Headers
        if (PasabuyBrandHeader != null)
            PasabuyBrandHeader.Visibility = isAngelitos ? Visibility.Collapsed : Visibility.Visible;
        if (AngelitosBrandHeader != null)
            AngelitosBrandHeader.Visibility = isAngelitos ? Visibility.Visible : Visibility.Collapsed;

        // 2. Quick Action
        if (HeroLauncherBorder != null)
            HeroLauncherBorder.Visibility = isAngelitos ? Visibility.Visible : Visibility.Collapsed;
        if (NavBtn_NewSale != null)
            NavBtn_NewSale.Visibility = isAngelitos ? Visibility.Visible : Visibility.Collapsed;

        // 3. Navigation Panels
        if (PasabuyNavPanel != null)
            PasabuyNavPanel.Visibility = isAngelitos ? Visibility.Collapsed : Visibility.Visible;
        if (AngelitosNavPanel != null)
            AngelitosNavPanel.Visibility = isAngelitos ? Visibility.Visible : Visibility.Collapsed;

        // 4. Sidebar Dynamic Colors
        try
        {
            var bc = new System.Windows.Media.BrushConverter();
            if (SidebarBorder != null)
            {
                SidebarBorder.Background = (System.Windows.Media.Brush)bc.ConvertFromString(bp.SidebarDarkColor)!;
                SidebarBorder.BorderBrush = (System.Windows.Media.Brush)bc.ConvertFromString(bp.SidebarBorderColor)!;
            }
            if (BrandHeaderBorder != null)
                BrandHeaderBorder.BorderBrush = (System.Windows.Media.Brush)bc.ConvertFromString(bp.SidebarBorderColor)!;
            if (SidebarBottomBorder != null)
            {
                SidebarBottomBorder.Background = (System.Windows.Media.Brush)bc.ConvertFromString(bp.SidebarBottomColor)!;
                SidebarBottomBorder.BorderBrush = (System.Windows.Media.Brush)bc.ConvertFromString(bp.SidebarBorderColor)!;
            }
            if (UserRoleBadgeBorder != null)
                UserRoleBadgeBorder.Background = (System.Windows.Media.Brush)bc.ConvertFromString(bp.AvatarRoleBgColor)!;
            if (UserRoleLabel != null)
                UserRoleLabel.Foreground = (System.Windows.Media.Brush)bc.ConvertFromString(bp.AvatarRoleTextColor)!;

            if (AvatarBorder != null)
            {
                var startCol = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bp.AvatarGradientStart)!;
                var endCol = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bp.AvatarGradientEnd)!;
                AvatarBorder.Background = new System.Windows.Media.LinearGradientBrush(startCol, endCol, new Point(0, 0), new Point(1, 1));
            }
        }
        catch { }
    }

    // ── Navigation registration ───────────────────────────────────────────

    /// <summary>
    /// Maps every route ID to its flat sidebar button.
    /// Multiple IDs can share the same button (e.g. "NewOrder" highlights NavBtn_NewOrder,
    /// but pressing F2 for "Orders" highlights NavBtn_Orders).
    /// </summary>
    private void RegisterNavButtons()
    {
        _navButtons.Clear();

        if (AntiquePasabuyPOS.Services.BusinessProfileService.IsAngelitos)
        {
            // Angelito's Restaurant Navigation
            if (NavBtn_NewSale != null) _navButtons["RestoSales"] = NavBtn_NewSale;
            if (NavBtn_RestoDashboard != null) _navButtons["RestoDashboard"] = NavBtn_RestoDashboard;
            if (NavBtn_RestoSales != null) _navButtons["RestoSales"] = NavBtn_RestoSales;
            if (NavBtn_RestoTables != null) _navButtons["RestoTables"] = NavBtn_RestoTables;
            if (NavBtn_RestoOrders != null) _navButtons["RestoOrders"] = NavBtn_RestoOrders;
            if (NavBtn_RestoMenu != null) _navButtons["RestoMenu"] = NavBtn_RestoMenu;
            if (NavBtn_RestoInventory != null) _navButtons["RestoInventory"] = NavBtn_RestoInventory;
            if (NavBtn_RestoCustomers != null) _navButtons["RestoCustomers"] = NavBtn_RestoCustomers;
            if (NavBtn_RestoStaff != null) _navButtons["RestoStaff"] = NavBtn_RestoStaff;
            if (NavBtn_RestoSalesReport != null) _navButtons["RestoSalesReport"] = NavBtn_RestoSalesReport;
            if (NavBtn_RestoInventoryReport != null) _navButtons["RestoInventoryReport"] = NavBtn_RestoInventoryReport;
            if (NavBtn_RestoSettings != null) _navButtons["Settings"] = NavBtn_RestoSettings;
        }
        else
        {
            // Antique Pasabuy Navigation
            if (NavBtn_Dashboard != null)    _navButtons["Dashboard"]   = NavBtn_Dashboard;
            if (NavBtn_NewOrder != null)     _navButtons["NewOrder"]    = NavBtn_NewOrder;
            if (NavBtn_Orders != null)       _navButtons["Orders"]      = NavBtn_Orders;
            if (NavBtn_Masterlists != null)  _navButtons["Masterlists"] = NavBtn_Masterlists;
            if (NavBtn_Buying != null)       _navButtons["Buying"]      = NavBtn_Buying;
            if (NavBtn_Products != null)     _navButtons["Products"]    = NavBtn_Products;
            if (NavBtn_Stores != null)       _navButtons["Stores"]      = NavBtn_Stores;
            if (NavBtn_Customers != null)    _navButtons["Customers"]   = NavBtn_Customers;
            if (NavBtn_Reports != null)      _navButtons["Reports"]     = NavBtn_Reports;
            if (NavBtn_Settings != null)     _navButtons["Settings"]    = NavBtn_Settings;

            // Aliases so deep-links highlight the right nav button
            if (NavBtn_Orders != null)
            {
                _navButtons["PendingOrders"]  = NavBtn_Orders;
                _navButtons["CompletedOrders"]= NavBtn_Orders;
                _navButtons["BoughtOrders"]   = NavBtn_Orders;
            }
            if (NavBtn_Reports != null)
            {
                _navButtons["SalesReport"]    = NavBtn_Reports;
                _navButtons["BuyingReport"]   = NavBtn_Reports;
                _navButtons["CustomerReport"] = NavBtn_Reports;
            }
        }
    }

    // ── Click handler (all flat nav buttons share this) ───────────────────

    private void OnNavButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string navId)
            Navigate(navId);
    }

    // ── Core navigation ───────────────────────────────────────────────────

    /// <summary>
    /// Single-click flat navigation: highlight the correct sidebar button,
    /// update the topbar labels, and load the target page.
    /// </summary>
    public void Navigate(string id)
    {
        _currentNavId = id;

        // Highlight active button — deactivate all others
        var activeBtn = _navButtons.TryGetValue(id, out var btn) ? btn : null;
        foreach (var (_, navBtn) in _navButtons)
        {
            NavProps.SetIsActive(navBtn, ReferenceEquals(navBtn, activeBtn));
        }

        UpdateTopbar(id);
        LoadPage(id);
    }

    // ── Topbar labels ─────────────────────────────────────────────────────

    private void UpdateTopbar(string id)
    {
        var labels = new Dictionary<string, (string title, string bc)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"]         = ("Dashboard",                                  "Home / Dashboard"),
            ["NewOrder"]          = ("New Purchase Order",                         "Orders / New"),
            ["Orders"]            = ("Purchase Orders",                            "Orders"),
            ["PendingOrders"]     = ("Purchase Orders",                            "Orders"),
            ["CompletedOrders"]   = ("Purchase Orders",                            "Orders"),
            ["BoughtOrders"]      = ("Purchase Orders",                            "Orders"),
            ["Masterlists"]       = ("Masterlists",                                "Masterlists"),
            ["Customers"]         = ("Customers",                                  "Customers"),
            ["SalesReport"]       = ("Reports",                                    "Reports"),
            ["BuyingReport"]      = ("Reports",                                    "Reports"),
            ["CustomerReport"]    = ("Reports",                                    "Reports"),
            ["Reports"]           = ("Reports",                                    "Reports"),
            ["Buying"]            = ("Shopping",                                   "Shopping"),
            ["Stores"]            = ("Store Partners",                             "Stores"),
            ["Products"]          = ("Product Catalog",                            "Products"),

            // Angelito's routes
            ["RestoDashboard"]       = ("Angelito's Resto Dashboard",                 "Overview / Live Status"),
            ["RestoSales"]           = ("Restaurant POS / New Sale",                  "Operations / POS"),
            ["RestoTables"]          = ("Table Management",                           "Floor Plan / Tables"),
            ["RestoOrders"]          = ("Restaurant Orders",                          "Operations / Orders"),
            ["RestoKitchen"]         = ("Kitchen Display System",                     "Operations / Kitchen"),
            ["RestoMenu"]            = ("Menu & Food Items",                          "Management / Menu"),
            ["RestoInventory"]       = ("Restaurant Inventory",                       "Management / Stock"),
            ["RestoCustomers"]       = ("Diners & Regulars",                          "Management / Customers"),
            ["RestoStaff"]           = ("Staff Directory",                            "Management / Staff"),
            ["RestoSalesReport"]     = ("Sales Reports",                              "Reports / Sales"),
            ["RestoInventoryReport"] = ("Inventory Reports",                          "Reports / Inventory"),

            // Shared
            ["Settings"]             = ("System Settings",                            "Settings / System"),
        };

        if (labels.TryGetValue(id, out var info))
        {
            PageTitleLabel.Text  = info.title;
            BreadcrumbLabel.Text = info.bc;
        }
    }

    // ── Page factory ──────────────────────────────────────────────────────

    private readonly Dictionary<string, Page> _pageCache = new(StringComparer.OrdinalIgnoreCase);

    private void LoadPage(string id)
    {
        // Parameterized filter routes (PendingOrders, BoughtOrders, CompletedOrders, SalesReport, etc.) or NewOrder are created fresh
        bool isCacheable = id is "Dashboard" or "Orders" or "Masterlists" or "Buying" or "Customers" or "Stores" or "Products" or "Reports" or "Settings"
                               or "RestoDashboard" or "RestoSales" or "RestoTables" or "RestoOrders" or "RestoKitchen" or "RestoMenu" or "RestoInventory" or "RestoCustomers" or "RestoStaff";

        Page? page = null;
        if (isCacheable && _pageCache.TryGetValue(id, out var cached) && cached != null)
        {
            page = cached;
        }
        else
        {
            page = id switch
            {
                // Pasabuy pages
                "Dashboard"              => new DashboardPage(_db, _user, Navigate),
                "NewOrder"               => new NewOrderPage(_db, _user, onSuccess: () => Navigate("Orders")),
                "Orders"                 => new OrdersPage(_db, _user, Navigate),
                "Masterlists"            => new MasterlistsPage(_db, _user, Navigate),
                "Buying"                 => new BuyingListPage(_db, _user, Navigate),
                "PendingOrders"          => new OrdersPage(_db, _user, Navigate, initialFilter: OrderStatus.WAITING_CONFIRMATION),
                "BoughtOrders"           => new OrdersPage(_db, _user, Navigate, initialFilter: OrderStatus.BOUGHT),
                "CompletedOrders"        => new OrdersPage(_db, _user, Navigate, initialFilter: OrderStatus.COMPLETED),
                "Customers"              => new CustomersPage(_db, _user, Navigate),
                "SalesReport"            => new ReportsPage(_db, _user, Navigate, initialReport: "sales"),
                "BuyingReport"           => new ReportsPage(_db, _user, Navigate, initialReport: "buying"),
                "CustomerReport"         => new ReportsPage(_db, _user, Navigate, initialReport: "customers"),
                "Reports"                => new ReportsPage(_db, _user, Navigate, initialReport: "sales"),
                "Stores"                 => new StoresPage(_db, _user, Navigate),
                "Products"               => new ProductsPage(_db, _user, Navigate),

                // Angelito's pages
                "RestoDashboard"         => new RestoDashboardPage(_db, _user, Navigate),
                "RestoSales"             => new RestoSalesPage(_db, _user, Navigate),
                "RestoTables"            => new RestoTablesPage(_db, _user, Navigate),
                "RestoOrders"            => new RestoOrdersPage(_db, _user, Navigate),
                "RestoKitchen"           => new RestoKitchenPage(_db, _user, Navigate),
                "RestoMenu"              => new RestoMenuPage(_db, _user, Navigate),
                "RestoInventory"         => new RestoInventoryPage(_db, _user, Navigate),
                "RestoCustomers"         => new RestoCustomersPage(_db, _user, Navigate),
                "RestoStaff"             => new RestoStaffPage(_db, _user, Navigate),
                "RestoSalesReport"       => new RestoReportsPage(_db, _user, Navigate, "sales"),
                "RestoInventoryReport"   => new RestoReportsPage(_db, _user, Navigate, "inventory"),

                // Shared settings
                "Settings"               => new SettingsPage(_db, _user, Navigate),
                _ => null
            };

            if (page != null && isCacheable)
            {
                _pageCache[id] = page;
            }
        }

        if (page is not null)
        {
            ContentFrame.Content = page;
            while (ContentFrame.CanGoBack)
            {
                try { ContentFrame.RemoveBackEntry(); }
                catch { break; }
            }
        }
    }

    // ── Theme ─────────────────────────────────────────────────────────────

    private void OnThemeToggle(object s, RoutedEventArgs e)
    {
        AntiquePasabuyPOS.Services.ThemeManager.ToggleTheme();
    }

    private void UpdateThemeIcon(AntiquePasabuyPOS.Services.AppTheme theme)
    {
        ThemeIcon.Kind = theme == AntiquePasabuyPOS.Services.AppTheme.Dark
            ? PackIconKind.WeatherNight
            : PackIconKind.WeatherSunny;
    }

    // ── Display scale & window resize ─────────────────────────────────────

    private void OnScaleChanged(double factor)
    {
        if (MainScaleTransform != null)
        {
            MainScaleTransform.ScaleX = factor;
            MainScaleTransform.ScaleY = factor;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarCol != null)
            SidebarCol.Width = new GridLength(e.NewSize.Width < 1200 ? 225 : 260);

        AntiquePasabuyPOS.Services.DisplayScaleManager.UpdateAutoScale(e.NewSize.Width, e.NewSize.Height);
    }

    // ── Logout ────────────────────────────────────────────────────────────

    private void OnLogout(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Are you sure you want to logout?", "Logout",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _pageCache.Clear();
            new LoginWindow(_db).Show();
            Close();
        }
    }
}
