using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoSalesPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;

    private List<RestoMenuItem> _allDishes = new();
    private string _activeCategory = "All";
    private readonly ObservableCollection<CartDishItem> _cart = new();

    // Timer used to debounce live table-bill updates (fires 1 s after last cart change)
    private System.Threading.Timer? _billSyncTimer;

    public class CartDishItem : INotifyPropertyChanged
    {
        public int MenuItemId { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal Price { get; set; }
        public decimal? CostPrice { get; set; }

        private decimal _quantity = 1m;
        public decimal Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged(nameof(Quantity));
                    OnPropertyChanged(nameof(Subtotal));
                }
            }
        }

        public decimal Subtotal => Price * Quantity;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public static string? PreSelectedTableNumber { get; set; }
    public static bool PreSelectForCheckout { get; set; }

    private bool _isLoadingTableOrder = false;

    public RestoSalesPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        CartGrid.ItemsSource = _cart;
        _cart.CollectionChanged += (_, _) => RecalculateTotals();
        CmbTable.SelectionChanged += OnTableSelectionChanged;
        TxtCustomerName.TextChanged += (_, _) => RecalculateTotals();

        Loaded += async (_, _) => await LoadDataAsync();
        Unloaded += (_, _) => _billSyncTimer?.Dispose();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            _allDishes = await RestaurantService.GetMenuItemsAsync();
            ApplyFilter();

            var tables = await RestaurantService.GetTablesAsync();
            var tableItems = tables.Select(t => $"{t.TableNumber} ({t.Section})").ToList();
            CmbTable.ItemsSource = tableItems;

            if (!string.IsNullOrEmpty(PreSelectedTableNumber))
            {
                var match = tableItems.FirstOrDefault(ti => ti.StartsWith(PreSelectedTableNumber + " ", StringComparison.OrdinalIgnoreCase) || ti.Equals(PreSelectedTableNumber, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    CmbTable.SelectedItem = match;
                }
                else if (tableItems.Count > 0)
                {
                    CmbTable.SelectedIndex = 0;
                }
                PreSelectedTableNumber = null;
            }
            else if (CmbTable.Items.Count > 0 && CmbTable.SelectedItem == null)
            {
                CmbTable.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading restaurant data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnTableSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingTableOrder) return;
        await LoadSelectedTableOrderAsync();
    }

    private string GetSelectedTableNumber()
    {
        if (CmbTable?.SelectedItem is not string s || string.IsNullOrWhiteSpace(s))
            return "Takeout";
        var idx = s.LastIndexOf(" (", StringComparison.Ordinal);
        return idx > 0 ? s.Substring(0, idx).Trim() : s.Trim();
    }

    private async Task LoadSelectedTableOrderAsync()
    {
        if (CmbTable.SelectedItem is not string) return;

        var tableNo = GetSelectedTableNumber();
        _isLoadingTableOrder = true;
        try
        {
            var activeOrder = await RestaurantService.GetActiveOrderByTableAsync(tableNo);
            if (activeOrder != null && activeOrder.Items.Count > 0)
            {
                _cart.Clear();
                foreach (var it in activeOrder.Items)
                {
                    // Re-look-up cost price from menu catalog (not stored in RestoOrderItem)
                    var menuItem = _allDishes.FirstOrDefault(d => d.Id == (it.MenuItemId ?? 0));
                    _cart.Add(new CartDishItem
                    {
                        MenuItemId = it.MenuItemId ?? 0,
                        Name = it.ItemName,
                        Category = menuItem?.Category ?? "",
                        Price = it.UnitPrice,
                        CostPrice = menuItem?.CostPrice,
                        Quantity = it.Quantity
                    });
                }

                if (!string.IsNullOrWhiteSpace(activeOrder.CustomerName))
                    TxtCustomerName.Text = activeOrder.CustomerName;

                if (TabStatusBadge != null && TxtTabStatus != null)
                {
                    TabStatusBadge.Visibility = Visibility.Visible;
                    TxtTabStatus.Text = $"Active Tab: {activeOrder.Items.Count} items (₱{activeOrder.TotalAmount:N2})";
                }
            }
            else
            {
                // New tab for this table
                _cart.Clear();
                TxtCustomerName.Text = "";
                if (TabStatusBadge != null)
                    TabStatusBadge.Visibility = Visibility.Collapsed;
            }
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading table order: {ex.Message}");
        }
        finally
        {
            _isLoadingTableOrder = false;
        }
    }

    private void ApplyFilter()
    {
        if (MenuItemsControl == null || _allDishes == null) return;

        var search = TxtSearchMenu?.Text?.Trim().ToLower() ?? "";
        var filtered = _allDishes.Where(d =>
        {
            var matchCat = _activeCategory == "All" || string.Equals(d.Category, _activeCategory, StringComparison.OrdinalIgnoreCase);
            var matchSearch = string.IsNullOrEmpty(search) || d.Name.ToLower().Contains(search) || d.Category.ToLower().Contains(search);
            return matchCat && matchSearch;
        }).ToList();

        MenuItemsControl.ItemsSource = filtered;
    }

    private void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cat)
        {
            _activeCategory = cat;

            // Update button styles
            if (CategoryPillsPanel != null)
            {
                foreach (var child in CategoryPillsPanel.Children)
                {
                    if (child is Button b)
                    {
                        b.Style = (b.Tag?.ToString() == cat)
                            ? (Style)FindResource("Pos3DPrimaryBtn")
                            : (Style)FindResource("Pos3DSecondaryBtn");
                    }
                }
            }

            ApplyFilter();
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnAddDishClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoMenuItem dish)
        {
            var existing = _cart.FirstOrDefault(c => c.MenuItemId == dish.Id);
            if (existing != null)
            {
                existing.Quantity += 1;
            }
            else
            {
                _cart.Add(new CartDishItem
                {
                    MenuItemId = dish.Id,
                    Name = dish.Name,
                    Category = dish.Category,
                    Price = dish.Price,
                    CostPrice = dish.CostPrice,
                    Quantity = 1
                });
            }
            RecalculateTotals();
        }
    }

    private void OnIncreaseQtyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CartDishItem item)
        {
            item.Quantity += 1;
            RecalculateTotals();
        }
    }

    private void OnDecreaseQtyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CartDishItem item)
        {
            if (item.Quantity > 1)
            {
                item.Quantity -= 1;
                RecalculateTotals();
            }
            else
            {
                _cart.Remove(item);
                RecalculateTotals();
            }
        }
    }

    private void OnCartCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(RecalculateTotals));
    }

    private void OnRemoveCartItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CartDishItem item)
        {
            _cart.Remove(item);
            RecalculateTotals();
        }
    }

    private void OnDiscountChanged(object sender, TextChangedEventArgs e) => RecalculateTotals();

    private void RecalculateTotals()
    {
        if (TxtSubtotal == null || TxtGrandTotal == null || TxtDiscount == null || _cart == null) return;

        var subtotal = _cart.Sum(c => c.Subtotal);
        decimal.TryParse(TxtDiscount.Text, out var discount);
        var grandTotal = Math.Max(0m, subtotal - discount);

        TxtSubtotal.Text = $"₱{subtotal:N2}";
        TxtGrandTotal.Text = $"₱{grandTotal:N2}";

        UpdateChangeCalculation(grandTotal);

        // Debounce: push running bill to table so floor plan stays synchronized
        ScheduleBillSync(grandTotal);
    }

    private void UpdateChangeCalculation(decimal? totalOverride = null)
    {
        if (TxtGrandTotal == null || TxtAmountPaid == null || TxtChange == null) return;

        decimal grandTotal;
        if (totalOverride.HasValue)
        {
            grandTotal = totalOverride.Value;
        }
        else
        {
            var subtotal = _cart?.Sum(c => c.Subtotal) ?? 0m;
            decimal.TryParse(TxtDiscount?.Text, out var discount);
            grandTotal = Math.Max(0m, subtotal - discount);
        }

        var amountText = TxtAmountPaid.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(amountText))
        {
            TxtChange.Text = "₱0.00";
            TxtChange.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#16A34A")!;
            return;
        }

        if (decimal.TryParse(amountText, out var paid))
        {
            if (paid >= grandTotal)
            {
                var change = paid - grandTotal;
                TxtChange.Text = $"₱{change:N2}";
                TxtChange.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#16A34A")!;
            }
            else
            {
                var shortfall = grandTotal - paid;
                TxtChange.Text = $"-₱{shortfall:N2} (short)";
                TxtChange.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#DC2626")!;
            }
        }
        else
        {
            TxtChange.Text = "₱0.00";
            TxtChange.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#16A34A")!;
        }
    }

    private void OnAmountPaidChanged(object sender, TextChangedEventArgs e)
    {
        UpdateChangeCalculation();
    }

    private void OnPaymentMethodChanged(object sender, RoutedEventArgs e)
    {
        if (TxtGrandTotal == null || TxtAmountPaid == null || _cart == null) return;

        var subtotal = _cart.Sum(c => c.Subtotal);
        decimal.TryParse(TxtDiscount?.Text, out var discount);
        var grandTotal = Math.Max(0m, subtotal - discount);

        if (RbGCash?.IsChecked == true || RbCard?.IsChecked == true)
        {
            TxtAmountPaid.Text = grandTotal > 0 ? grandTotal.ToString("F2") : "";
        }
        UpdateChangeCalculation(grandTotal);
    }

    private void OnQuickTenderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && TxtAmountPaid != null && _cart != null)
        {
            var subtotal = _cart.Sum(c => c.Subtotal);
            decimal.TryParse(TxtDiscount?.Text, out var discount);
            var grandTotal = Math.Max(0m, subtotal - discount);

            if (tag == "exact")
            {
                TxtAmountPaid.Text = grandTotal.ToString("F2");
            }
            else if (decimal.TryParse(tag, out var val))
            {
                TxtAmountPaid.Text = val.ToString("F2");
            }
        }
    }

    private void ScheduleBillSync(decimal runningTotal)
    {
        _billSyncTimer?.Dispose();

        if (_cart.Count == 0 || CmbTable.SelectedItem is null)
            return;

        var tableNo = GetSelectedTableNumber();
        var custName = string.IsNullOrWhiteSpace(TxtCustomerName?.Text)
            ? "Dine-in Guests"
            : TxtCustomerName.Text.Trim();

        _billSyncTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                var tables = await RestaurantService.GetTablesAsync();
                var t = tables.FirstOrDefault(x => x.TableNumber == tableNo);
                if (t != null)
                {
                    await RestaurantService.UpdateTableStatusAsync(
                        t.Id, RestoTableStatus.Occupied, custName, runningTotal);
                }
            }
            catch
            {
                // Swallow background sync exceptions
            }
        }, null, 600, System.Threading.Timeout.Infinite);
    }

    private async void OnSaveToTabClick(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0)
        {
            MessageBox.Show("Please add at least one dish or drink before saving to the table tab.", "Empty Order", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CmbTable.SelectedItem is null)
        {
            MessageBox.Show("Please select a table to save this tab to.", "Select Table", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tableNo = GetSelectedTableNumber();
        var custName = string.IsNullOrWhiteSpace(TxtCustomerName.Text) ? "Dine-in Guests" : TxtCustomerName.Text.Trim();

        var orderItems = _cart.Select(c => new RestoOrderItem
        {
            MenuItemId = c.MenuItemId > 0 ? c.MenuItemId : null,
            ItemName = c.Name,
            Quantity = c.Quantity,
            UnitPrice = c.Price,
            Status = RestoOrderStatus.Pending
        }).ToList();

        try
        {
            var savedOrder = await RestaurantService.SaveOrUpdateTableOrderAsync(
                tableNo, custName, _user.FullName, orderItems);

            if (TabStatusBadge != null && TxtTabStatus != null)
            {
                TabStatusBadge.Visibility = Visibility.Visible;
                TxtTabStatus.Text = $"Active Tab: {savedOrder.Items.Count} items (₱{savedOrder.TotalAmount:N2})";
            }

            MessageBox.Show(
                $"Tab for Table {tableNo} saved successfully!\nItems: {savedOrder.Items.Count}\nRunning Bill: ₱{savedOrder.TotalAmount:N2}\n\nGuest: {custName}\nStatus: Occupied (Floor plan updated)",
                "Table Tab Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save table tab: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnCompleteSaleClick(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0)
        {
            MessageBox.Show("Please add at least one dish or drink to the order.", "Empty Order", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Cancel any pending bill sync before completing
        _billSyncTimer?.Dispose();
        _billSyncTimer = null;

        var tableStr = GetSelectedTableNumber();
        var custName = string.IsNullOrWhiteSpace(TxtCustomerName.Text) ? "Walk-in Guest" : TxtCustomerName.Text.Trim();
        var subtotal = _cart.Sum(c => c.Subtotal);
        decimal.TryParse(TxtDiscount.Text, out var discount);
        var total = Math.Max(0m, subtotal - discount);

        var payMethod = RbGCash.IsChecked == true ? "GCash" : (RbCard.IsChecked == true ? "Card" : "Cash");

        // Parse and validate payment amount
        decimal amountPaid = total;
        if (!string.IsNullOrWhiteSpace(TxtAmountPaid?.Text))
        {
            if (decimal.TryParse(TxtAmountPaid.Text.Trim(), out var parsedPaid))
            {
                amountPaid = parsedPaid;
            }
        }

        if (amountPaid < total)
        {
            var shortfall = total - amountPaid;
            MessageBox.Show(
                $"Amount tendered (₱{amountPaid:N2}) is less than total due (₱{total:N2}).\nCustomer needs to pay ₱{shortfall:N2} more.",
                "Insufficient Payment", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAmountPaid?.Focus();
            return;
        }

        var change = Math.Max(0m, amountPaid - total);

        var sale = new RestoSale
        {
            TableNumber = tableStr,
            CustomerName = custName,
            Subtotal = subtotal,
            Discount = discount,
            TotalAmount = total,
            AmountPaid = amountPaid,
            Change = change,
            PaymentMethod = payMethod,
            PaymentStatus = RestoSaleStatus.Paid,
            CashierName = _user.FullName,
            BusinessId = BusinessProfileService.ANGELITOS_RESTO,
            CreatedAt = DateTime.Now,
            Items = _cart.Select(c => new RestoSaleItem
            {
                MenuItemId = c.MenuItemId,
                ItemName = c.Name,
                Category = c.Category,
                UnitPrice = c.Price,
                CostPrice = c.CostPrice,
                Quantity = c.Quantity,
                Subtotal = c.Subtotal
            }).ToList()
        };

        try
        {
            var saved = await RestaurantService.CreateSaleAsync(sale);
            MessageBox.Show(
                $"Restaurant sale {saved.SaleNo} recorded successfully!\n\n" +
                $"Total Due:       ₱{total:N2}\n" +
                $"Amount Tendered: ₱{amountPaid:N2}\n" +
                $"Change:          ₱{change:N2}\n" +
                $"Payment Method:  {payMethod}\n\n" +
                $"Table {tableStr} is now Available.",
                "Sale Completed", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset the order ticket
            _cart.Clear();
            TxtDiscount.Text = "0.00";
            TxtCustomerName.Text = "";
            if (TxtAmountPaid != null) TxtAmountPaid.Text = "";
            if (TxtChange != null) TxtChange.Text = "₱0.00";

            if (TabStatusBadge != null)
                TabStatusBadge.Visibility = Visibility.Collapsed;

            if (CmbTable.Items.Count > 0)
                CmbTable.SelectedIndex = 0;
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to record restaurant sale: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnVoidTabClick(object sender, RoutedEventArgs e)
    {
        if (CmbTable.SelectedItem is not string tableStr)
        {
            MessageBox.Show("Please select a table first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tableNo = tableStr.Split(' ')[0];

        if (_cart.Count == 0)
        {
            MessageBox.Show($"Table {tableNo} does not have any active order items to void.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var res = MessageBox.Show(
            $"Are you sure you want to delete and void the active order for Table {tableNo}?\n\n" +
            $"This will delete the active order ticket and set Table {tableNo} back to Available.",
            "Delete / Void Table Order",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            try
            {
                await RestaurantService.VoidTableTabAsync(tableNo);
                _cart.Clear();
                TxtDiscount.Text = "0.00";
                TxtCustomerName.Text = "";
                if (TxtAmountPaid != null) TxtAmountPaid.Text = "";
                if (TxtChange != null) TxtChange.Text = "₱0.00";
                if (TabStatusBadge != null) TabStatusBadge.Visibility = Visibility.Collapsed;
                RecalculateTotals();
                await LoadDataAsync();
                MessageBox.Show($"Active order for Table {tableNo} has been deleted and table is now Available.", "Order Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to void table tab: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
