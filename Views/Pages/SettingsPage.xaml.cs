using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class SettingsPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;

    private List<RateTierVm> _rateTierVms = [];

    private string _activeSection = "business";

    public SettingsPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null, string? initialSection = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        Loaded += (_, _) =>
        {
            LoadSettings();
            var startSection = string.Equals(initialSection, "stores", StringComparison.OrdinalIgnoreCase) ? "stores" : "business";
            SwitchSection(startSection);
            if (string.Equals(initialSection, "stores", StringComparison.OrdinalIgnoreCase))
            {
                NewStoreNameBox?.BringIntoView();
                NewStoreNameBox?.Focus();
            }
        };
    }

    private void SwitchSection(string section)
    {
        _activeSection = section.ToLower();
        var tabs = new[] { TabBusiness, TabPasabuy, TabPrinting, TabData, TabSystem, TabDisplay };
        var target = _activeSection switch
        {
            "pasabuy" or "stores" => TabPasabuy,
            "printing"            => TabPrinting,
            "data"                => TabData,
            "system"              => TabSystem,
            "display"             => TabDisplay,
            _                     => TabBusiness
        };

        foreach (var t in tabs)
        {
            NavProps.SetIsActive(t, t == target);
            t.Foreground = t == target ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));
        }

        if (SectionBusiness != null) SectionBusiness.Visibility = target == TabBusiness ? Visibility.Visible : Visibility.Collapsed;
        if (SectionPasabuy != null)  SectionPasabuy.Visibility  = target == TabPasabuy  ? Visibility.Visible : Visibility.Collapsed;
        if (SectionDelivery != null) SectionDelivery.Visibility = Visibility.Collapsed; // removed from UI
        if (SectionPrinting != null) SectionPrinting.Visibility = target == TabPrinting ? Visibility.Visible : Visibility.Collapsed;
        if (SectionData != null)     SectionData.Visibility     = target == TabData     ? Visibility.Visible : Visibility.Collapsed;
        if (SectionSystem != null)   SectionSystem.Visibility   = target == TabSystem   ? Visibility.Visible : Visibility.Collapsed;
        if (SectionDisplay != null)  SectionDisplay.Visibility  = target == TabDisplay  ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSectionTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            SwitchSection(tag);
        }
    }

    private bool _suppressProfileEvents = false;

    private void LoadSettings()
    {
        _suppressProfileEvents = true;
        try
        {
            var bp = BusinessProfileService.ActiveProfile;
            var pId = bp.Id;

            // Load profile combo
            CmbBusinessProfile.Items.Clear();
            foreach (var profile in BusinessProfileService.AllProfiles)
            {
                var item = new ComboBoxItem
                {
                    Content = BusinessProfileService.GetDisplayName(profile),
                    Tag = profile.Id
                };
                CmbBusinessProfile.Items.Add(item);
                if (profile.Id == bp.Id)
                    CmbBusinessProfile.SelectedItem = item;
            }

            TxtActiveProfileDesc.Text = $"Operating as {bp.Name} ({bp.BusinessType})";

            BizNameBox.Text     = SeedData.GetSetting(_db, $"business_name_{pId}", bp.Name);
            BizSubtitleBox.Text = SeedData.GetSetting(_db, $"business_subtitle_{pId}", bp.Subtitle);
            BizAddressBox.Text  = SeedData.GetSetting(_db, $"business_address_{pId}", bp.Address);
            BizContactBox.Text  = SeedData.GetSetting(_db, $"business_contact_{pId}", bp.ContactNumber);
            HeaderBox.Text      = SeedData.GetSetting(_db, $"receipt_header_{pId}", bp.ReceiptHeader);
            FooterBox.Text      = SeedData.GetSetting(_db, $"receipt_footer_{pId}", bp.ReceiptFooter);

            DelivFeeBox.Text    = SeedData.GetSetting(_db, "default_delivery_fee", "50");
            BuyingDaysBox.Text  = SeedData.GetSetting(_db, "buying_schedule_days", "Wednesday, Thursday");
            BuyingNotesBox.Text = SeedData.GetSetting(_db, "buying_schedule_notes", "Wed: SM & Grocery; Thu: Robinsons & Bakeries");

            bool isFeeEn = bool.TryParse(SeedData.GetSetting(_db, $"pasabuy_fee_enabled_{pId}", bp.PasabuyFeeEnabled.ToString()), out var fe) ? fe : bp.PasabuyFeeEnabled;
            ChkEnablePasabuyFee.IsChecked = isFeeEn;
            TxtDefaultPasabuyFee.Text = SeedData.GetSetting(_db, $"default_pasabuy_fee_{pId}", bp.DefaultPasabuyFee.ToString("0.00"));

            if (BusinessProfileService.IsAngelitos)
            {
                TxtPasabuyFeeNotice.Text = "Note: Pasabuy Fee is disabled for Angelito's Bar & Resto Grill by default.";
            }
            else
            {
                TxtPasabuyFeeNotice.Text = "When enabled, orders automatically calculate Pasabuy service fee based on tier rates.";
            }

            _rateTierVms = PasabuyFeeService.GetRateTiers(_db).Select(t => new RateTierVm(t)).ToList();
            RateTiersGrid.ItemsSource = null;
            RateTiersGrid.ItemsSource = _rateTierVms;

            LoadStores();
            LoadPrinters();
            LoadSystemInfo();
            LoadDisplaySettings();
        }
        finally
        {
            _suppressProfileEvents = false;
        }
    }

    private void OnBusinessProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileEvents) return;
        if (CmbBusinessProfile.SelectedItem is ComboBoxItem item && item.Tag is string pid)
        {
            var p = BusinessProfileService.GetProfileById(pid);
            if (p != null)
                TxtActiveProfileDesc.Text = $"Click 'APPLY PROFILE' to switch to {p.Name}";
        }
    }

    private void OnApplyProfileClick(object sender, RoutedEventArgs e)
    {
        if (CmbBusinessProfile.SelectedItem is ComboBoxItem item && item.Tag is string pid)
        {
            BusinessProfileService.SetActiveProfile(pid, _db);
            ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);
            LoadSettings();

            SaveStatus.Text = $"Switched to {BusinessProfileService.ActiveProfile.Name}!";
            SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
        }
    }

    private void LoadSystemInfo()
    {
        try
        {
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "antique_pasabuy.db");
            DbPathLabel.Text = dbPath;
            if (File.Exists(dbPath))
            {
                var fi = new FileInfo(dbPath);
                var kb = fi.Length / 1024.0;
                DbSizeLabel.Text = $"Database Size: {kb:N1} KB ({fi.Length:N0} bytes) — Active & Protected";
            }
            else
            {
                DbSizeLabel.Text = "Database file initializing...";
            }
        }
        catch
        {
            DbSizeLabel.Text = "Database Size: Ready";
        }

        SystemUserLabel.Text = $"{Environment.UserName} ({Environment.MachineName})";
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open data folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnBackupDatabase(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "antique_pasabuy.db");
            if (!File.Exists(source))
            {
                MessageBox.Show("Source database file not found.", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "backups");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(backupDir, $"antique_pasabuy_backup_{timestamp}.db");

            File.Copy(source, dest, true);

            MessageBox.Show($"Database backup successfully created at:\n{dest}", "Backup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            SaveStatus.Text = $"Backup created at {DateTime.Now:HH:mm:ss}";
            SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Backup failed: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadStores()
    {
        var stores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).ToList();
        StoresListControl.ItemsSource = stores;
    }

    private void OnAddStore(object sender, RoutedEventArgs e)
    {
        var name = NewStoreNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a store name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            NewStoreNameBox.Focus();
            return;
        }

        var branch = string.IsNullOrWhiteSpace(NewStoreBranchBox.Text) ? "Main Branch" : NewStoreBranchBox.Text.Trim();
        var exists = _db.Stores.Any(s => s.Name.ToLower() == name.ToLower() && s.IsActive);
        if (exists)
        {
            MessageBox.Show($"Store '{name}' already exists.", "Duplicate Store", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _db.Stores.Add(new Store
        {
            Name = name,
            Branch = branch,
            IsActive = true,
            CreatedAt = DateTime.Now
        });
        _db.SaveChanges();

        NewStoreNameBox.Text = "";
        NewStoreBranchBox.Text = "";
        LoadStores();

        SaveStatus.Text = $"Store '{name}' added successfully!";
        SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
    }

    private void OnDeleteStore(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Store store)
        {
            if (MessageBox.Show($"Are you sure you want to remove store '{store.Name}'?", "Confirm Remove",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                store.IsActive = false;
                _db.SaveChanges();
                LoadStores();
            }
        }
    }

    private void OnResetDefaultRates(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reset all pasabuy rates back to the official Antique Pasabuy table?",
            "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        PasabuyFeeService.ResetToDefaults(_db);
        _rateTierVms = PasabuyFeeService.GetRateTiers(_db).Select(t => new RateTierVm(t)).ToList();
        RateTiersGrid.ItemsSource = null;
        RateTiersGrid.ItemsSource = _rateTierVms;
        SaveStatus.Text = "Rate tiers reset to official Antique Pasabuy table!";
        SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
    }

    private void OnAddRateTier(object s, RoutedEventArgs e)
    {
        // Add a blank tier with default fixed fee — owner can edit it inline
        var lastMax = _rateTierVms.LastOrDefault(t => t.MaxAmount.HasValue)?.MaxAmount;
        var newMin = (lastMax.HasValue ? lastMax.Value + 1m : 0m);
        var vm = new RateTierVm(new PasabuyRateTier { MinAmount = newMin, MaxAmount = newMin + 499m, FixedFee = 50m });
        _rateTierVms.Add(vm);
        RateTiersGrid.ItemsSource = null;
        RateTiersGrid.ItemsSource = _rateTierVms;
        RateTiersGrid.SelectedItem = vm;
        RateTiersGrid.ScrollIntoView(vm);
        SaveStatus.Text = "New tier added — click cells to edit, then click \"Save Rates\".";
        SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
    }

    private void OnDeleteRateTier(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RateTierVm vm)
        {
            _rateTierVms.Remove(vm);
            RateTiersGrid.ItemsSource = null;
            RateTiersGrid.ItemsSource = _rateTierVms;
            SaveStatus.Text = "Tier removed. Click \"Save Rates\" to apply.";
            SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
        }
    }

    private void OnSaveRateTiers(object s, RoutedEventArgs e)
    {
        // Commit any in-progress cell edit first
        RateTiersGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var tiers = _rateTierVms
            .Select(vm => vm.ToModel())
            .Where(t => t != null)
            .Cast<PasabuyRateTier>()
            .OrderBy(t => t.MinAmount)
            .ToList();

        if (tiers.Count == 0)
        {
            MessageBox.Show("No valid tiers to save. Please add at least one tier.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PasabuyFeeService.SaveRateTiers(_db, tiers);
        _rateTierVms = tiers.Select(t => new RateTierVm(t)).ToList();
        RateTiersGrid.ItemsSource = null;
        RateTiersGrid.ItemsSource = _rateTierVms;
        SaveStatus.Text = $"{tiers.Count} rate tiers saved successfully!";
        SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
    }

    private void OnRateTierCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // Refresh the Preview column after any cell edit completes
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is RateTierVm vm)
        {
            vm.CommitEdits();
        }
    }

    private void LoadPrinters()
    {
        var (printers, defaultP) = PrinterService.GetInstalledPrinters();
        PrinterCombo.ItemsSource = printers;
        var saved = SeedData.GetSetting(_db, "default_printer", defaultP);
        PrinterCombo.SelectedItem = printers.Contains(saved) ? saved : printers.FirstOrDefault();
        UpdatePrinterStatus();
    }

    private void OnPrinterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePrinterStatus();
    }

    private void UpdatePrinterStatus()
    {
        if (PrinterStatusBadge == null || PrinterStatusDot == null || PrinterStatusText == null) return;

        var selected = PrinterCombo.SelectedItem?.ToString();
        var hasPrinters = PrinterCombo.Items.Count > 0 && !string.IsNullOrWhiteSpace(selected);

        if (hasPrinters)
        {
            var (jobs, summary) = PrinterService.GetPrinterQueueStatus(selected);
            if (jobs > 0 || summary.Contains("Error", StringComparison.OrdinalIgnoreCase) || summary.Contains("Paper", StringComparison.OrdinalIgnoreCase))
            {
                PrinterStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 243, 199)); // amber-100
                PrinterStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6));           // amber-600
                PrinterStatusText.Text = $"{jobs} Job(s) Pending";
                PrinterStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9));    // amber-700
                PrinterStatusBadge.ToolTip = summary;
            }
            else
            {
                PrinterStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 252, 231)); // green-100
                PrinterStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74));           // green-600
                PrinterStatusText.Text = "Connected";
                PrinterStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));    // green-700
                PrinterStatusBadge.ToolTip = $"Printer '{selected}' is ready.";
            }

            if (SpoolerQueueStatusText != null)
            {
                SpoolerQueueStatusText.Text = summary;
            }
        }
        else
        {
            PrinterStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226)); // red-100
            PrinterStatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));           // red-600
            PrinterStatusText.Text = "Not Found / Offline";
            PrinterStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));    // red-700
            PrinterStatusBadge.ToolTip = "No printer detected or selected. Please connect a printer or install printer drivers.";

            if (SpoolerQueueStatusText != null)
            {
                SpoolerQueueStatusText.Text = "No printer selected";
            }
        }
    }

    private void OnClearSpoolerQueue(object sender, RoutedEventArgs e)
    {
        var selected = PrinterCombo.SelectedItem?.ToString();
        var (ok, msg) = PrinterService.ClearPrinterQueue(selected);
        UpdatePrinterStatus();
        MessageBox.Show(msg, ok ? "Queue Cleared" : "Notice", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnRefreshPrinters(object s, RoutedEventArgs e)
    {
        LoadPrinters();
        SaveStatus.Text = $"Found {PrinterCombo.Items.Count} printer(s).";
    }

    private void OnTestPrint(object s, RoutedEventArgs e)
    {
        var printer = PrinterCombo.SelectedItem?.ToString() ?? "";
        var bizName = BizNameBox.Text.Trim();
        var (ok, msg) = PrinterService.PrintTestPage(printer, showDialog: false, businessName: bizName);
        SaveStatus.Text = msg;
        SaveStatus.Foreground = ok ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136))
                                   : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        var bp = BusinessProfileService.ActiveProfile;
        var pId = bp.Id;

        // Save profile specific information
        SeedData.SetSetting(_db, $"business_name_{pId}",         BizNameBox.Text.Trim());
        SeedData.SetSetting(_db, $"business_subtitle_{pId}",     BizSubtitleBox.Text.Trim());
        SeedData.SetSetting(_db, $"business_address_{pId}",      BizAddressBox.Text.Trim());
        SeedData.SetSetting(_db, $"business_contact_{pId}",      BizContactBox.Text.Trim());
        SeedData.SetSetting(_db, $"receipt_header_{pId}",        HeaderBox.Text.Trim());
        SeedData.SetSetting(_db, $"receipt_footer_{pId}",        FooterBox.Text.Trim());

        // Keep legacy keys updated for backwards compatibility
        SeedData.SetSetting(_db, "business_name",  BizNameBox.Text.Trim());
        SeedData.SetSetting(_db, "receipt_footer", FooterBox.Text.Trim());

        // Pasabuy fee settings
        SeedData.SetSetting(_db, $"pasabuy_fee_enabled_{pId}", (ChkEnablePasabuyFee.IsChecked == true).ToString());
        SeedData.SetSetting(_db, $"default_pasabuy_fee_{pId}", TxtDefaultPasabuyFee.Text.Trim());

        SeedData.SetSetting(_db, "default_delivery_fee",  DelivFeeBox.Text.Trim());
        SeedData.SetSetting(_db, "buying_schedule_days",  BuyingDaysBox.Text.Trim());
        SeedData.SetSetting(_db, "buying_schedule_notes", BuyingNotesBox.Text.Trim());
        SeedData.SetSetting(_db, "default_printer",       PrinterCombo.SelectedItem?.ToString() ?? "");

        // Also save current rate tiers when global save is triggered
        var tiers = _rateTierVms.Select(vm => vm.ToModel()).Where(t => t != null).Cast<PasabuyRateTier>().OrderBy(t => t.MinAmount).ToList();
        if (tiers.Count > 0) PasabuyFeeService.SaveRateTiers(_db, tiers);

        // Persist display settings
        if (ScaleModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && Enum.TryParse<UiScaleMode>(tagStr, out var mode))
        {
            var win = Window.GetWindow(this);
            DisplayScaleManager.ApplyScale(mode, (int)CustomScaleSlider.Value, win?.ActualWidth ?? 0, win?.ActualHeight ?? 0, save: true);
        }

        SaveStatus.Text = "All settings saved successfully!";
        SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
    }

    private bool _suppressScaleEvents = false;

    private void LoadDisplaySettings()
    {
        _suppressScaleEvents = true;
        try
        {
            UpdateThemeButtonStyles();

            var modeName = DisplayScaleManager.CurrentMode.ToString();
            foreach (ComboBoxItem item in ScaleModeCombo.Items)
            {
                if (item.Tag?.ToString() == modeName)
                {
                    ScaleModeCombo.SelectedItem = item;
                    break;
                }
            }

            if (ScaleModeCombo.SelectedItem == null && ScaleModeCombo.Items.Count > 0)
            {
                ScaleModeCombo.SelectedIndex = 0;
            }

            CustomScaleSlider.Value = DisplayScaleManager.CustomScalePercent;
            CustomScaleValueLabel.Text = $"{DisplayScaleManager.CustomScalePercent}%";
            CustomScalePanel.Visibility = DisplayScaleManager.CurrentMode == UiScaleMode.Custom ? Visibility.Visible : Visibility.Collapsed;

            UpdateEffectiveScaleLabel();
            UpdateScreenDiagnostics();
        }
        finally
        {
            _suppressScaleEvents = false;
        }
    }

    private void UpdateThemeButtonStyles()
    {
        bool isDark = ThemeManager.CurrentTheme == AppTheme.Dark;
        if (BtnThemeLight != null)
        {
            BtnThemeLight.FontWeight = !isDark ? FontWeights.Bold : FontWeights.Normal;
            BtnThemeLight.Foreground = !isDark ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 53, 145))
                                               : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
        }
        if (BtnThemeDark != null)
        {
            BtnThemeDark.FontWeight = isDark ? FontWeights.Bold : FontWeights.Normal;
            BtnThemeDark.Foreground = isDark ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 94, 215))
                                             : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
        }
    }

    private void UpdateEffectiveScaleLabel()
    {
        if (EffectiveScaleLabel != null)
        {
            EffectiveScaleLabel.Text = $"{(DisplayScaleManager.CurrentScaleFactor * 100):N0}% ({DisplayScaleManager.CurrentScaleFactor:N2}x)";
        }
    }

    private void UpdateScreenDiagnostics()
    {
        if (ScreenDiagnosticsLabel != null)
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            var wa = SystemParameters.WorkArea;
            ScreenDiagnosticsLabel.Text = $"Primary Display: {sw:N0} × {sh:N0} px | Usable Work Area: {wa.Width:N0} × {wa.Height:N0} px";
        }
    }

    private void OnSetLightMode(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyTheme(AppTheme.Light);
        UpdateThemeButtonStyles();
        SaveStatus.Text = "Light mode applied!";
        SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
    }

    private void OnSetDarkMode(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyTheme(AppTheme.Dark);
        UpdateThemeButtonStyles();
        SaveStatus.Text = "Dark mode applied!";
        SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
    }

    private void OnScaleModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScaleEvents) return;
        if (ScaleModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
        {
            if (Enum.TryParse<UiScaleMode>(tagStr, out var mode))
            {
                CustomScalePanel.Visibility = mode == UiScaleMode.Custom ? Visibility.Visible : Visibility.Collapsed;
                var win = Window.GetWindow(this);
                double w = win?.ActualWidth ?? 0;
                double h = win?.ActualHeight ?? 0;
                DisplayScaleManager.ApplyScale(mode, (int)CustomScaleSlider.Value, w, h, save: true);
                UpdateEffectiveScaleLabel();
                SaveStatus.Text = $"UI scale set to {mode} ({(DisplayScaleManager.CurrentScaleFactor * 100):N0}%)";
                SaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136));
            }
        }
    }

    private void OnCustomScaleSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)Math.Round(CustomScaleSlider?.Value ?? 100);
        if (CustomScaleValueLabel != null) CustomScaleValueLabel.Text = $"{val}%";

        if (_suppressScaleEvents) return;

        if (ScaleModeCombo?.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "Custom")
        {
            var win = Window.GetWindow(this);
            double w = win?.ActualWidth ?? 0;
            double h = win?.ActualHeight ?? 0;
            DisplayScaleManager.ApplyScale(UiScaleMode.Custom, val, w, h, save: true);
            UpdateEffectiveScaleLabel();
        }
    }
}

/// <summary>
/// ViewModel wrapping a PasabuyRateTier with clean, simple editable fields for the DataGrid.
/// Eliminates confusing decimals (e.g. shows 99 instead of 99.99, 100 instead of 100.00).
/// </summary>
public class RateTierVm : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

    public decimal MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public decimal? FixedFee { get; set; }
    public decimal? PercentageFee { get; set; }

    // Editable string properties for the DataGrid cells
    private string _minAmountEdit;
    private string _maxAmountEdit;
    private string _fixedFeeEdit;
    private string _percentFeeEdit;

    public string MinAmountEdit
    {
        get => _minAmountEdit;
        set { _minAmountEdit = value; OnPropChanged(); OnPropChanged(nameof(MinAmountDisplay)); }
    }

    public string MaxAmountEdit
    {
        get => _maxAmountEdit;
        set { _maxAmountEdit = value; OnPropChanged(); OnPropChanged(nameof(MaxAmountDisplay)); }
    }

    public string FixedFeeEdit
    {
        get => _fixedFeeEdit;
        set { _fixedFeeEdit = value; OnPropChanged(); OnPropChanged(nameof(FixedFeeDisplay)); OnPropChanged(nameof(DisplayFee)); }
    }

    public string PercentFeeEdit
    {
        get => _percentFeeEdit;
        set { _percentFeeEdit = value; OnPropChanged(); OnPropChanged(nameof(PercentFeeDisplay)); OnPropChanged(nameof(DisplayFee)); }
    }

    public string MinAmountDisplay => string.IsNullOrWhiteSpace(_minAmountEdit) || _minAmountEdit == "0" ? "₱0 (or starting)" : $"₱{_minAmountEdit}";
    public string MaxAmountDisplay => string.IsNullOrWhiteSpace(_maxAmountEdit) ? "UP (open-ended)" : $"{_maxAmountEdit} pesos";
    public string FixedFeeDisplay  => string.IsNullOrWhiteSpace(_fixedFeeEdit)  ? "—" : $"₱{_fixedFeeEdit}";
    public string PercentFeeDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_percentFeeEdit)) return "—";
            var clean = _percentFeeEdit.Trim().TrimEnd('%');
            if (decimal.TryParse(clean, out var p))
            {
                var pctVal = p > 1 ? p : p * 100;
                return $"{pctVal:0.#}%";
            }
            return _percentFeeEdit;
        }
    }

    public string DisplayFee
    {
        get
        {
            if (decimal.TryParse(_fixedFeeEdit, out var f) && f > 0)
                return $"₱{(f % 1 == 0 ? f.ToString("N0") : f.ToString("N2"))}";

            if (!string.IsNullOrWhiteSpace(_percentFeeEdit))
            {
                var clean = _percentFeeEdit.Trim().TrimEnd('%');
                if (decimal.TryParse(clean, out var pct) && pct > 0)
                {
                    var pctVal = pct > 1 ? pct : pct * 100;
                    return $"{pctVal:0.#}%";
                }
            }
            return "—";
        }
    }

    private static string FormatVal(decimal? val)
    {
        if (!val.HasValue) return "";
        return val.Value % 1 == 0 ? val.Value.ToString("0") : val.Value.ToString("0.##");
    }

    public RateTierVm(PasabuyRateTier tier)
    {
        MinAmount = tier.MinAmount;
        MaxAmount = tier.MaxAmount;
        FixedFee = tier.FixedFee;
        PercentageFee = tier.PercentageFee;

        _minAmountEdit = FormatVal(tier.MinAmount);
        _maxAmountEdit = FormatVal(tier.MaxAmount);
        _fixedFeeEdit  = FormatVal(tier.FixedFee);
        _percentFeeEdit = tier.PercentageFee.HasValue
            ? $"{tier.PercentageFee.Value * 100:0.#}%"
            : "";
    }

    /// <summary>Parses the editable string fields back into the model.</summary>
    public void CommitEdits()
    {
        if (decimal.TryParse(_minAmountEdit, out var mn))
            MinAmount = mn;

        if (string.IsNullOrWhiteSpace(_maxAmountEdit) || _maxAmountEdit.Equals("UP", StringComparison.OrdinalIgnoreCase))
            MaxAmount = null;
        else if (decimal.TryParse(_maxAmountEdit, out var mx))
            MaxAmount = mx;

        FixedFee = decimal.TryParse(_fixedFeeEdit, out var fx) ? fx : (decimal?)null;

        if (string.IsNullOrWhiteSpace(_percentFeeEdit))
        {
            PercentageFee = null;
        }
        else
        {
            var clean = _percentFeeEdit.Trim().TrimEnd('%');
            if (decimal.TryParse(clean, out var pc))
            {
                PercentageFee = pc > 1 ? pc / 100m : pc;
            }
            else
            {
                PercentageFee = null;
            }
        }

        OnPropChanged(nameof(DisplayFee));
        OnPropChanged(nameof(MinAmountDisplay));
        OnPropChanged(nameof(MaxAmountDisplay));
        OnPropChanged(nameof(FixedFeeDisplay));
        OnPropChanged(nameof(PercentFeeDisplay));
    }

    /// <summary>Converts VM back to a model tier, or null if invalid.</summary>
    public PasabuyRateTier? ToModel()
    {
        CommitEdits();
        // Must have at least one fee type
        if (!FixedFee.HasValue && !PercentageFee.HasValue) return null;
        return new PasabuyRateTier
        {
            MinAmount     = MinAmount,
            MaxAmount     = MaxAmount,
            FixedFee      = FixedFee,
            PercentageFee = PercentageFee
        };
    }
}
