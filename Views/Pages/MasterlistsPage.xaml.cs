using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class MasterlistsPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;

    private List<MasterlistRowVm> _allRows  = [];
    private List<MasterlistRowVm> _filtered = [];
    private bool _isLoading = false;
    private bool _isReady = false;
    private bool _isPopulatingFilter = false;

    public MasterlistsPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db       = db;
        _user     = user;
        _navigate = navigate;

        _isReady  = true;
        Loaded += (_, _) => LoadData();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async void LoadData()
    {
        if (_isLoading) return;
        _isLoading = true;

        ShowLoading(true);

        try
        {
            List<MasterlistRowVm> rows = [];
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                rows = MasterlistService.GetAllRows(bgDb);
            });

            _allRows = rows;
            PopulateYearFilter();
            ApplyFilter();
            UpdateKpis();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MasterlistsPage] LoadData error: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            ShowLoading(false);
        }
    }

    private void PopulateYearFilter()
    {
        if (FilterYearBox == null) return;
        _isPopulatingFilter = true;
        try
        {
            var years = _allRows.Select(r => r.Year).Distinct().OrderByDescending(y => y).ToList();
            // Keep "All Years" item at index 0, rebuild the rest
            while (FilterYearBox.Items.Count > 1)
                FilterYearBox.Items.RemoveAt(1);

            foreach (var year in years)
            {
                var item = new ComboBoxItem { Content = year.ToString(), Tag = year };
                FilterYearBox.Items.Add(item);
            }

            // Also add current year if not already present
            var currentYear = DateTime.Now.Year;
            if (!years.Contains(currentYear))
            {
                var item = new ComboBoxItem { Content = currentYear.ToString(), Tag = currentYear };
                FilterYearBox.Items.Add(item);
            }

            FilterYearBox.SelectedIndex = 0;
        }
        finally
        {
            _isPopulatingFilter = false;
        }
    }

    // ── Filtering & sorting ───────────────────────────────────────────────────

    private void ApplyFilter()
    {
        if (!_isReady || MasterlistGrid == null || EmptyState == null) return;

        var search = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";

        // Month filter
        int? filterMonth = null;
        if (FilterMonthBox?.SelectedItem is ComboBoxItem monthItem && monthItem.Tag != null
            && int.TryParse(monthItem.Tag.ToString(), out int fm))
            filterMonth = fm;

        // Year filter
        int? filterYear = null;
        if (FilterYearBox?.SelectedItem is ComboBoxItem yearItem && yearItem.Tag != null
            && int.TryParse(yearItem.Tag.ToString(), out int fy))
            filterYear = fy;

        // Status filter
        string? filterStatus = null;
        if (FilterStatusBox?.SelectedItem is ComboBoxItem statusItem
            && statusItem.Content?.ToString() is string sc
            && sc != "All Statuses")
            filterStatus = sc;

        _filtered = _allRows.Where(r =>
        {
            if (!string.IsNullOrEmpty(search))
            {
                var ctrl = r.ControlNumber?.ToLowerInvariant() ?? "";
                var name = r.Name?.ToLowerInvariant() ?? "";
                if (!ctrl.Contains(search) && !name.Contains(search))
                    return false;
            }
            if (filterMonth.HasValue && r.Month != filterMonth.Value) return false;
            if (filterYear.HasValue  && r.Year  != filterYear.Value)  return false;
            if (filterStatus != null && !string.Equals(r.Status, filterStatus, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }).ToList();

        // Sorting
        var sortTag = (SortBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "newest";
        _filtered = sortTag switch
        {
            "oldest" => [.. _filtered.OrderBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.Id)],
            "orders" => [.. _filtered.OrderByDescending(r => r.OrderCount)],
            "items"  => [.. _filtered.OrderByDescending(r => r.TotalOrderedItems)],
            _        => [.. _filtered.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.Id)],
        };

        MasterlistGrid.ItemsSource = _filtered;
        EmptyState.Visibility      = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MasterlistGrid.Visibility  = _filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateKpis()
    {
        if (KpiTotalBatches == null || KpiActive == null || KpiTotalOrders == null || KpiTotalItems == null || KpiUnavailable == null) return;
        KpiTotalBatches.Text  = _allRows.Count.ToString("N0");
        KpiActive.Text        = _allRows.Count(r => r.IsActive).ToString("N0");
        KpiTotalOrders.Text   = _allRows.Sum(r => r.OrderCount).ToString("N0");
        KpiTotalItems.Text    = _allRows.Sum(r => r.TotalOrderedItems).ToString("N0");
        KpiUnavailable.Text   = _allRows.Sum(r => r.TotalUnavailable).ToString("N0");
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void ShowLoading(bool show)
    {
        if (LoadingOverlay != null) LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            if (EmptyState != null) EmptyState.Visibility = Visibility.Collapsed;
            if (MasterlistGrid != null) MasterlistGrid.Visibility = Visibility.Collapsed;
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isReady) return;
        ApplyFilter();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || _isPopulatingFilter) return;
        ApplyFilter();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => LoadData();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MasterlistGrid.SelectedItem is MasterlistRowVm row)
            OpenMasterlistDetail(row.Id, row.ControlNumber, row.Name);
    }

    private void OnOpenMasterlist(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MasterlistRowVm row)
            OpenMasterlistDetail(row.Id, row.ControlNumber, row.Name);
    }

    private void OpenMasterlistDetail(int id, string controlNo, string name)
    {
        var detailPage = new MasterlistDetailPage(_db, _user, _navigate, id, controlNo, name);
        var window     = Window.GetWindow(this);
        if (window is Views.MainWindow mw)
        {
            // Navigate inline inside the content frame — re-use the existing frame
            if (mw.ContentFrame.Content is Page)
                mw.ContentFrame.Navigate(detailPage);
        }
    }

    private void OnCreateMasterlist(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateMasterlistDialog(_db, _user.Id)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true && dialog.CreatedMasterlist != null)
        {
            LoadData();
            // Immediately open the newly created masterlist
            OpenMasterlistDetail(
                dialog.CreatedMasterlist.Id,
                dialog.CreatedMasterlist.ControlNumber,
                dialog.CreatedMasterlist.Name);
        }
    }

    private void OnPrintMasterlist(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MasterlistRowVm row)
        {
            try
            {
                using var bgDb = new AppDbContext();
                var detail = MasterlistService.GetDetail(bgDb, row.Id);
                PrintMasterlist(detail);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Print error: {ex.Message}", "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnMoreActions(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MasterlistRowVm row) return;

        var ctx = new ContextMenu();

        if (row.IsActive)
        {
            var closeItem = new MenuItem { Header = "Close Batch" };
            closeItem.Click += (_, _) =>
            {
                if (MessageBox.Show($"Close masterlist '{row.ControlNumber} — {row.Name}'?\n\nThis marks it as Closed. It remains searchable and printable.",
                    "Close Masterlist", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    MasterlistService.Close(_db, row.Id);
                    LoadData();
                }
            };
            ctx.Items.Add(closeItem);
        }

        if (!row.IsArchived)
        {
            var archiveItem = new MenuItem { Header = "Archive" };
            archiveItem.Click += (_, _) =>
            {
                if (MessageBox.Show($"Archive '{row.ControlNumber} — {row.Name}'?\n\nThe record is preserved permanently. No data will be lost.",
                    "Archive Masterlist", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    MasterlistService.Archive(_db, row.Id);
                    LoadData();
                }
            };
            ctx.Items.Add(archiveItem);
        }

        if (ctx.Items.Count == 0)
        {
            var noActs = new MenuItem { Header = "No actions available", IsEnabled = false };
            ctx.Items.Add(noActs);
        }

        ctx.PlacementTarget = btn;
        ctx.Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ctx.IsOpen          = true;
    }

    // ── Printing ──────────────────────────────────────────────────────────────

    private void PrintMasterlist(MasterlistDetailVm detail)
    {
        var ml = detail.Masterlist;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("================================");
        sb.AppendLine("   DIONIX POS / ANTIQUE PASABUY");
        sb.AppendLine("================================");
        sb.AppendLine($"MASTERLIST: {ml.ControlNumber}");
        sb.AppendLine($"{ml.Name}");
        sb.AppendLine($"Period: {ml.StartDate:MMM dd} - {ml.EndDate:MMM dd, yyyy}");
        sb.AppendLine($"Status: {ml.Status}");
        sb.AppendLine($"Printed: {DateTime.Now:MMM dd, yyyy hh:mm tt}");
        sb.AppendLine("================================");
        sb.AppendLine();

        foreach (var order in detail.Orders.OrderBy(o => o.CustomerName))
        {
            sb.AppendLine($"** {order.CustomerName.ToUpperInvariant()} **");
            sb.AppendLine($"   Order: {order.OrderNo}");
            sb.AppendLine();

            foreach (var item in order.Items)
            {
                var statusLabel = item.StatusLabel;
                sb.AppendLine($"   [{statusLabel,-15}] {item.ProductName}");
                sb.AppendLine($"               Qty: {item.Qty} {item.Unit}  Purchased: {item.PurchasedQty?.ToString("N0") ?? "—"}");
                if (!string.IsNullOrWhiteSpace(item.UnavailableReason))
                    sb.AppendLine($"               Reason: {item.UnavailableReason}");
            }

            sb.AppendLine("--------------------------------");
            sb.AppendLine();
        }

        sb.AppendLine("================================");
        sb.AppendLine($"TOTAL ORDERS    : {detail.TotalOrders}");
        sb.AppendLine($"TOTAL ITEMS     : {detail.TotalItems}");
        sb.AppendLine($"PURCHASED       : {detail.TotalPurchased:N0}");
        sb.AppendLine($"BOUGHT ON HAND  : {detail.TotalBoughtOnHand:N0}");
        sb.AppendLine($"NOT AVAILABLE   : {detail.TotalUnavailable:N0}");
        sb.AppendLine("================================");
        sb.AppendLine();

        // Use the existing PrinterService to send to the thermal printer
        var printerName = SeedData.GetSetting(_db, "selected_printer", "");
        if (string.IsNullOrWhiteSpace(printerName))
        {
            // Fall back to print dialog
            var pd = new System.Windows.Controls.PrintDialog();
            if (pd.ShowDialog() == true)
            {
                var doc = new System.Windows.Documents.FlowDocument(
                    new System.Windows.Documents.Paragraph(
                        new System.Windows.Documents.Run(sb.ToString()))
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Courier New"),
                        FontSize = 11
                    });
                var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)doc)
                    .DocumentPaginator;
                pd.PrintDocument(paginator, $"Masterlist {ml.ControlNumber}");
            }
        }
        else
        {
            var bytes = System.Text.Encoding.GetEncoding("IBM437").GetBytes(sb.ToString());
            PrinterService.SendRawBytesToPrinter(printerName, bytes);
        }
    }
}
