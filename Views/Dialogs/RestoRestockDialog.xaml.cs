using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class RestoRestockDialog : Window
{
    private readonly AppDbContext _db;
    private readonly List<RestoInventoryItem> _existingItems;
    private RestoInventoryItem? _selectedItem;

    private static readonly string[] Categories = { "Meat", "Poultry", "Seafood", "Beverage", "Liquor", "Produce", "Seasoning", "Supplies", "Dairy", "Frozen", "Dry Goods" };
    private static readonly string[] Units = { "kg", "g", "pc", "bottle", "case", "pack", "liter", "can", "box", "sack" };

    public RestoRestockDialog(AppDbContext db, List<RestoInventoryItem>? existingItems = null, RestoInventoryItem? preselected = null)
    {
        InitializeComponent();
        _db = db;

        CategoryCombo.ItemsSource = Categories;
        UnitCombo.ItemsSource = Units;
        CategoryCombo.Text = "Meat";
        UnitCombo.Text = "kg";

        _existingItems = existingItems ?? new List<RestoInventoryItem>();

        // Add a "-- New Item --" placeholder to the combo
        var comboItems = new List<object> { "-- Add New Item --" };
        comboItems.AddRange(_existingItems);
        ExistingItemCombo.ItemsSource = comboItems;

        if (preselected != null)
        {
            // Find and select the preselected item
            var idx = _existingItems.FindIndex(i => i.Id == preselected.Id);
            if (idx >= 0) ExistingItemCombo.SelectedIndex = idx + 1; // +1 for the "New Item" entry
        }
    }

    private void OnExistingItemChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExistingItemCombo.SelectedIndex <= 0 || ExistingItemCombo.SelectedItem is string)
        {
            // New item mode
            _selectedItem = null;
            CodeBox.IsEnabled = true;
            NameBox.IsEnabled = true;
            CategoryCombo.IsEnabled = true;
            UnitCombo.IsEnabled = true;
            MinStockBox.IsEnabled = true;
            CodeBox.Text = "";
            NameBox.Text = "";
            QtyLabel.Text = "Initial Quantity *";
            CurrentStockPanel.Visibility = Visibility.Collapsed;
            SaveBtn.Content = "Add Item";
            TitleLbl.Text = "Add New Inventory Item";
            return;
        }

        if (ExistingItemCombo.SelectedItem is RestoInventoryItem item)
        {
            _selectedItem = item;
            CodeBox.Text = item.ItemCode;
            NameBox.Text = item.Name;
            CategoryCombo.Text = item.Category;
            UnitCombo.Text = item.Unit;
            CostBox.Text = item.CostPrice?.ToString("N2") ?? "";
            MinStockBox.Text = item.MinStockLevel.ToString("N0");

            // Lock fields in restock mode
            CodeBox.IsEnabled = false;
            NameBox.IsEnabled = false;
            CategoryCombo.IsEnabled = false;
            UnitCombo.IsEnabled = false;
            MinStockBox.IsEnabled = false;

            QtyLabel.Text = "Quantity to ADD *";
            QtyBox.Text = "";
            CurrentStockPanel.Visibility = Visibility.Visible;
            CurrentStockLbl.Text = $"Current stock: {item.Quantity:N1} {item.Unit}";
            SaveBtn.Content = "Restock";
            TitleLbl.Text = $"Restock: {item.Name}";
        }
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";

        if (!decimal.TryParse(QtyBox.Text.Trim(), out var qty) || qty <= 0)
        {
            ErrLbl.Text = "Please enter a valid quantity (> 0).";
            QtyBox.Focus();
            return;
        }

        decimal? cost = null;
        var costText = CostBox.Text.Replace("₱", "").Trim();
        if (!string.IsNullOrWhiteSpace(costText))
        {
            if (decimal.TryParse(costText, out var c) && c >= 0)
                cost = c;
        }

        if (_selectedItem != null)
        {
            // Restock existing item
            _selectedItem.Quantity += qty;
            if (cost.HasValue && cost.Value > 0) _selectedItem.CostPrice = cost.Value;
            _selectedItem.LastRestockedAt = DateTime.Now;
            _db.SaveChanges();
        }
        else
        {
            // Add new inventory item
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrLbl.Text = "Item name is required for new items.";
                NameBox.Focus();
                return;
            }

            var code = string.IsNullOrWhiteSpace(CodeBox.Text) ? GenerateCode() : CodeBox.Text.Trim();
            var category = string.IsNullOrWhiteSpace(CategoryCombo.Text) ? "Supplies" : CategoryCombo.Text.Trim();
            var unit = string.IsNullOrWhiteSpace(UnitCombo.Text) ? "pc" : UnitCombo.Text.Trim();
            decimal.TryParse(MinStockBox.Text.Trim(), out var minStock);
            if (minStock <= 0) minStock = 5;

            var newItem = new RestoInventoryItem
            {
                ItemCode = code,
                Name = NameBox.Text.Trim(),
                Category = category,
                Quantity = qty,
                Unit = unit,
                MinStockLevel = minStock,
                CostPrice = cost,
                LastRestockedAt = DateTime.Now
            };
            _db.RestoInventoryItems.Add(newItem);
            _db.SaveChanges();
        }

        DialogResult = true;
    }

    private string GenerateCode()
    {
        var count = _db.RestoInventoryItems.Count() + 1;
        return $"INV-{count:D3}";
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}
