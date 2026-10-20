using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class MasterlistDetailPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;
    private readonly int _masterlistId;

    private MasterlistDetailVm? _detail;
    private List<MasterlistOrderVm> _displayOrders = [];

    public MasterlistDetailPage(AppDbContext db, User user, Action<string> navigate,
                                int masterlistId, string controlNo, string name)
    {
        InitializeComponent();
        _db           = db;
        _user         = user;
        _navigate     = navigate;
        _masterlistId = masterlistId;

        ControlNoLabel.Text = controlNo;
        TitleLabel.Text     = name;

        Loaded += (_, _) => LoadDetail();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async void LoadDetail()
    {
        try
        {
            MasterlistDetailVm? detail = null;
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                detail = MasterlistService.GetDetail(bgDb, _masterlistId);
            });

            if (detail == null) return;
            _detail = detail;

            // Header info
            var ml = detail.Masterlist;
            SubtitleLabel.Text   = $"{ml.StartDate:MMM dd} – {ml.EndDate:MMM dd, yyyy}  ·  {ml.Notes ?? ""}";
            DateRangeLabel.Text  = $"{ml.StartDate:MMM dd, yyyy}  →  {ml.EndDate:MMM dd, yyyy}";

            // Status badge
            DetailStatusLabel.Text = ml.Status;
            (DetailStatusBadge.Background, DetailStatusLabel.Foreground) = ml.Status switch
            {
                "Active"   => (new SolidColorBrush(Color.FromRgb(209, 250, 229)), new SolidColorBrush(Color.FromRgb(5, 150, 105))),
                "Closed"   => (new SolidColorBrush(Color.FromRgb(254, 243, 199)), new SolidColorBrush(Color.FromRgb(217, 119, 6))),
                "Archived" => (new SolidColorBrush(Color.FromRgb(241, 245, 249)), new SolidColorBrush(Color.FromRgb(100, 116, 139))),
                _          => (new SolidColorBrush(Color.FromRgb(237, 233, 254)), new SolidColorBrush(Color.FromRgb(79, 70, 229))),
            };

            DetailOrderCount.Text    = detail.TotalOrders.ToString("N0");
            DetailItemCount.Text     = detail.TotalItems.ToString("N0");
            DetailUnavailable.Text   = detail.TotalUnavailable.ToString("N0");
            DetailBoughtOnHand.Text  = detail.TotalBoughtOnHand.ToString("N0");

            _displayOrders = [.. detail.Orders];
            ApplyDetailFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MasterlistDetailPage] LoadDetail error: {ex.Message}");
            MessageBox.Show($"Failed to load masterlist detail: {ex.Message}",
                "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Filtering / sorting ───────────────────────────────────────────────────

    private void ApplyDetailFilter()
    {
        if (_detail == null) return;

        var search = DetailSearchBox?.Text?.Trim().ToLowerInvariant() ?? "";
        var sortTag = (DetailSortBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "customer";

        IEnumerable<MasterlistOrderVm> filtered = _detail.Orders;

        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(o =>
                o.CustomerName.ToLowerInvariant().Contains(search) ||
                o.OrderNo.ToLowerInvariant().Contains(search) ||
                o.Items.Any(i => i.ProductName.ToLowerInvariant().Contains(search)));
        }

        filtered = sortTag switch
        {
            "orders"  => filtered.OrderByDescending(o => o.TotalItems),
            "unavail" => filtered.OrderByDescending(o => o.Items.Count(i => i.ItemStatus == "UNAVAILABLE")),
            "newest"  => filtered.OrderByDescending(o => o.CreatedAt),
            _         => filtered.OrderBy(o => o.CustomerName),
        };

        _displayOrders = [.. filtered];
        RenderOrders();
    }

    private void OnDetailSearchChanged(object sender, TextChangedEventArgs e) => ApplyDetailFilter();
    private void OnDetailSortChanged(object sender, SelectionChangedEventArgs e) => ApplyDetailFilter();

    // ── Render order cards ────────────────────────────────────────────────────

    private void RenderOrders()
    {
        OrdersPanel.Children.Clear();

        if (_displayOrders.Count == 0)
        {
            var empty = new TextBlock
            {
                Text               = "No orders found for this masterlist.",
                FontSize           = 13,
                Foreground         = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontFamily         = new FontFamily("Segoe UI"),
                HorizontalAlignment= HorizontalAlignment.Center,
                Margin             = new Thickness(0, 40, 0, 0)
            };
            OrdersPanel.Children.Add(empty);
            return;
        }

        foreach (var order in _displayOrders)
            OrdersPanel.Children.Add(BuildOrderCard(order));
    }

    private Border BuildOrderCard(MasterlistOrderVm order)
    {
        var card = new Border
        {
            Background      = new SolidColorBrush(Colors.White),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(225, 230, 239)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(10),
            Margin          = new Thickness(0, 0, 0, 10),
            Padding         = new Thickness(16, 14, 16, 14),
        };

        var root = new StackPanel();

        // ── Customer header ───────────────────────────────────────────────────
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar
        var avatarBorder = new Border
        {
            Width        = 38,
            Height       = 38,
            CornerRadius = new CornerRadius(19),
            Margin       = new Thickness(0, 0, 12, 0),
        };
        avatarBorder.Background = new LinearGradientBrush(
            Color.FromRgb(69, 43, 134), Color.FromRgb(99, 102, 241),
            new Point(0, 0), new Point(1, 1));
        var initials = order.CustomerName.Length > 0
            ? string.Concat(order.CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Take(2).Select(p => char.ToUpper(p[0]).ToString()))
            : "?";
        avatarBorder.Child = new TextBlock
        {
            Text                = initials,
            Foreground          = Brushes.White,
            FontSize            = 14,
            FontWeight          = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        Grid.SetColumn(avatarBorder, 0);
        header.Children.Add(avatarBorder);

        // Name + order no
        var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock
        {
            Text       = order.CustomerName,
            FontSize   = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(32, 38, 58)),
            FontFamily = new FontFamily("Segoe UI"),
        });
        namePanel.Children.Add(new TextBlock
        {
            Text       = $"{order.OrderNo}  ·  {order.CreatedAt:MMM dd, yyyy}",
            FontSize   = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 112, 137)),
            FontFamily = new FontFamily("Segoe UI"),
            Margin     = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(namePanel, 1);
        header.Children.Add(namePanel);

        // Item count badge
        var countBadge = new Border
        {
            Background   = new SolidColorBrush(Color.FromRgb(237, 233, 254)),
            CornerRadius = new CornerRadius(6),
            Padding      = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        countBadge.Child = new TextBlock
        {
            Text       = $"{order.TotalItems} item{(order.TotalItems == 1 ? "" : "s")}",
            FontSize   = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(79, 70, 229)),
            FontFamily = new FontFamily("Segoe UI"),
        };
        Grid.SetColumn(countBadge, 2);
        header.Children.Add(countBadge);

        root.Children.Add(header);

        if (order.Items.Count > 0)
        {
            // Divider
            root.Children.Add(new Border
            {
                BorderBrush     = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin          = new Thickness(0, 10, 0, 10),
            });

            // Items grid
            var itemsGrid = new Grid();
            itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            // Header row
            int rowIdx = 0;
            itemsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddItemGridCell(itemsGrid, "PRODUCT",    rowIdx, 0, true);
            AddItemGridCell(itemsGrid, "QTY",        rowIdx, 1, true, TextAlignment.Center);
            AddItemGridCell(itemsGrid, "PURCHASED",  rowIdx, 2, true, TextAlignment.Center);
            AddItemGridCell(itemsGrid, "STATUS",     rowIdx, 3, true, TextAlignment.Center);
            rowIdx++;

            foreach (var item in order.Items)
            {
                itemsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Product name
                var productTb = new TextBlock
                {
                    Text              = item.ProductName,
                    FontSize          = 12.5,
                    FontFamily        = new FontFamily("Segoe UI"),
                    Foreground        = new SolidColorBrush(Color.FromRgb(32, 38, 58)),
                    Margin            = new Thickness(0, 3, 8, 3),
                    TextTrimming      = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(productTb, rowIdx);
                Grid.SetColumn(productTb, 0);
                itemsGrid.Children.Add(productTb);

                // Qty
                var qtyTb = new TextBlock
                {
                    Text              = $"{item.Qty:N0} {item.Unit}",
                    FontSize          = 12,
                    FontFamily        = new FontFamily("Segoe UI"),
                    Foreground        = new SolidColorBrush(Color.FromRgb(100, 112, 137)),
                    TextAlignment     = TextAlignment.Center,
                    Margin            = new Thickness(0, 3, 0, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(qtyTb, rowIdx);
                Grid.SetColumn(qtyTb, 1);
                itemsGrid.Children.Add(qtyTb);

                // Purchased qty
                var purchTb = new TextBlock
                {
                    Text              = item.PurchasedQty.HasValue ? $"{item.PurchasedQty:N0}" : "—",
                    FontSize          = 12,
                    FontFamily        = new FontFamily("Segoe UI"),
                    Foreground        = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
                    TextAlignment     = TextAlignment.Center,
                    Margin            = new Thickness(0, 3, 0, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(purchTb, rowIdx);
                Grid.SetColumn(purchTb, 2);
                itemsGrid.Children.Add(purchTb);

                // Status badge
                var statusColor = Color.FromRgb(79, 70, 229); // New — indigo
                var statusBg    = Color.FromRgb(237, 233, 254);
                switch (item.StatusLabel)
                {
                    case "Bought":
                        statusColor = Color.FromRgb(22, 163, 74);
                        statusBg    = Color.FromRgb(209, 250, 229);
                        break;
                    case "Bought On Hand":
                        statusColor = Color.FromRgb(8, 145, 178);
                        statusBg    = Color.FromRgb(207, 250, 254);
                        break;
                    case "Not Available":
                        statusColor = Color.FromRgb(220, 38, 38);
                        statusBg    = Color.FromRgb(254, 226, 226);
                        break;
                }

                var badge = new Border
                {
                    Background        = new SolidColorBrush(statusBg),
                    CornerRadius      = new CornerRadius(5),
                    Padding           = new Thickness(6, 2, 6, 2),
                    Margin            = new Thickness(0, 3, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child             = new TextBlock
                    {
                        Text       = item.StatusLabel,
                        FontSize   = 10.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(statusColor),
                        FontFamily = new FontFamily("Segoe UI"),
                    }
                };
                Grid.SetRow(badge, rowIdx);
                Grid.SetColumn(badge, 3);
                itemsGrid.Children.Add(badge);

                rowIdx++;
            }

            root.Children.Add(itemsGrid);

            // Unavailable note
            var unavailItems = order.Items.Where(i => i.StatusLabel == "Not Available").ToList();
            if (unavailItems.Count > 0)
            {
                root.Children.Add(new Border
                {
                    Background   = new SolidColorBrush(Color.FromRgb(254, 242, 242)),
                    BorderBrush  = new SolidColorBrush(Color.FromRgb(252, 165, 165)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding      = new Thickness(10, 6, 10, 6),
                    Margin       = new Thickness(0, 8, 0, 0),
                    Child        = new TextBlock
                    {
                        Text       = $"⚠ {unavailItems.Count} item(s) not available: " +
                                     string.Join(", ", unavailItems.Select(i => i.ProductName)),
                        FontSize   = 11.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                        FontFamily = new FontFamily("Segoe UI"),
                        TextWrapping = TextWrapping.Wrap,
                    }
                });
            }
        }
        else
        {
            root.Children.Add(new TextBlock
            {
                Text       = "No items in this order.",
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                FontFamily = new FontFamily("Segoe UI"),
                Margin     = new Thickness(50, 8, 0, 0),
            });
        }

        card.Child = root;
        return card;
    }

    private static void AddItemGridCell(Grid grid, string text, int row, int col,
                                        bool isHeader = false,
                                        TextAlignment align = TextAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text          = text,
            FontSize      = isHeader ? 9.5 : 12.5,
            FontWeight    = isHeader ? FontWeights.Bold : FontWeights.Normal,
            Foreground    = isHeader
                ? new SolidColorBrush(Color.FromRgb(100, 112, 137))
                : new SolidColorBrush(Color.FromRgb(32, 38, 58)),
            FontFamily    = new FontFamily("Segoe UI"),
            TextAlignment = align,
            Margin        = new Thickness(0, 0, 8, isHeader ? 6 : 0),
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void OnGoBack(object sender, RoutedEventArgs e)
        => _navigate("Masterlists");

    private void OnRefresh(object sender, RoutedEventArgs e)
        => LoadDetail();

    // ── Assign orders dialog ──────────────────────────────────────────────────

    private void OnAssignOrders(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this);

        // Load unassigned orders
        List<Order> unassigned = [];
        try
        {
            using var bgDb = new AppDbContext();
            unassigned = bgDb.Orders
                .Where(o => o.MasterlistId == null
                         && o.BusinessId == "ANTIQUE_PASABUY"
                         && o.Status != OrderStatus.CANCELLED)
                .OrderByDescending(o => o.CreatedAt)
                .Take(200)
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading orders: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (unassigned.Count == 0)
        {
            MessageBox.Show("There are no unassigned orders available to associate with this masterlist.",
                "No Orders", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Build a simple selection dialog
        var dlg = new Window
        {
            Title  = "Assign Orders to Masterlist",
            Width  = 580,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner  = win,
            ResizeMode = ResizeMode.CanResize,
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text       = "Select orders to assign to this masterlist.\nOnly orders not yet assigned to any masterlist are shown.",
            FontSize   = 12.5,
            FontFamily = new FontFamily("Segoe UI"),
            Margin     = new Thickness(16, 16, 16, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var listBox = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Margin        = new Thickness(16, 0, 16, 8),
            ItemTemplate  = BuildOrderListTemplate(),
        };
        listBox.ItemsSource = unassigned;
        Grid.SetRow(listBox, 1);
        grid.Children.Add(listBox);

        var btnPanel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(16, 0, 16, 16),
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(16, 8, 16, 8),
            Margin  = new Thickness(0, 0, 8, 0),
        };
        cancelBtn.Click += (_, _) => dlg.Close();

        var assignBtn = new Button
        {
            Content    = "Assign Selected",
            Padding    = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(79, 70, 229)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
        };
        assignBtn.Click += (_, _) =>
        {
            var selected = listBox.SelectedItems.Cast<Order>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one order.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var o in selected)
                MasterlistService.AssignOrder(_db, o.Id, _masterlistId);

            dlg.Close();
            LoadDetail();
        };
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(assignBtn);
        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        dlg.Content = grid;
        dlg.ShowDialog();
    }

    private static DataTemplate BuildOrderListTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.MarginProperty, new Thickness(4, 4, 4, 4));

        var line1 = new FrameworkElementFactory(typeof(TextBlock));
        line1.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("OrderNo"));
        line1.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        line1.SetValue(TextBlock.FontSizeProperty, 12.5);
        line1.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
        factory.AppendChild(line1);

        var line2 = new FrameworkElementFactory(typeof(TextBlock));
        line2.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Customer.Name"));
        line2.SetValue(TextBlock.FontSizeProperty, 11.5);
        line2.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(100, 112, 137)));
        line2.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
        factory.AppendChild(line2);

        return new DataTemplate { VisualTree = factory };
    }

    // ── Print ─────────────────────────────────────────────────────────────────

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        if (_detail == null) return;

        var ml = _detail.Masterlist;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("================================");
        sb.AppendLine("  DIONIX POS / ANTIQUE PASABUY");
        sb.AppendLine("================================");
        sb.AppendLine($"MASTERLIST: {ml.ControlNumber}");
        sb.AppendLine(ml.Name);
        sb.AppendLine($"Period: {ml.StartDate:MMM dd} – {ml.EndDate:MMM dd, yyyy}");
        sb.AppendLine($"Status: {ml.Status}");
        sb.AppendLine($"Printed: {DateTime.Now:MMM dd, yyyy hh:mm tt}");
        sb.AppendLine("================================");
        sb.AppendLine();

        foreach (var order in _displayOrders.OrderBy(o => o.CustomerName))
        {
            sb.AppendLine($"** {order.CustomerName.ToUpperInvariant()} **");
            sb.AppendLine($"   Order: {order.OrderNo}");
            sb.AppendLine();

            foreach (var item in order.Items)
            {
                var label = item.StatusLabel;
                sb.AppendLine($"   [{label,-15}] {item.ProductName}");
                sb.AppendLine($"               Ordered: {item.Qty} {item.Unit}  " +
                              $"Bought: {item.PurchasedQty?.ToString("N0") ?? "—"}");
                if (!string.IsNullOrWhiteSpace(item.UnavailableReason))
                    sb.AppendLine($"               Reason: {item.UnavailableReason}");
                if (!string.IsNullOrWhiteSpace(item.PurchaseNotes))
                    sb.AppendLine($"               Notes:  {item.PurchaseNotes}");
            }

            sb.AppendLine("--------------------------------");
            sb.AppendLine();
        }

        sb.AppendLine("================================");
        sb.AppendLine($"TOTAL ORDERS   : {_detail.TotalOrders}");
        sb.AppendLine($"TOTAL ITEMS    : {_detail.TotalItems}");
        sb.AppendLine($"PURCHASED      : {_detail.TotalPurchased:N0}");
        sb.AppendLine($"BOUGHT ON HAND : {_detail.TotalBoughtOnHand:N0}");
        sb.AppendLine($"NOT AVAILABLE  : {_detail.TotalUnavailable:N0}");
        sb.AppendLine("================================");
        sb.AppendLine();

        var printerName = SeedData.GetSetting(_db, "selected_printer", "");
        if (string.IsNullOrWhiteSpace(printerName))
        {
            var pd = new System.Windows.Controls.PrintDialog();
            if (pd.ShowDialog() == true)
            {
                var doc = new System.Windows.Documents.FlowDocument(
                    new System.Windows.Documents.Paragraph(
                        new System.Windows.Documents.Run(sb.ToString()))
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Courier New"),
                        FontSize   = 11
                    });
                pd.PrintDocument(
                    ((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator,
                    $"Masterlist {ml.ControlNumber}");
            }
        }
        else
        {
            var bytes = System.Text.Encoding.GetEncoding("IBM437").GetBytes(sb.ToString());
            PrinterService.SendRawBytesToPrinter(printerName, bytes);
        }
    }
}
