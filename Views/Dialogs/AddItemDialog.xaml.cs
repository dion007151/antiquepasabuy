using System.Windows;
using System.Windows.Input;
using AntiquePasabuyPOS.Views.Pages;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class AddItemDialog : Window
{
    public OrderItemEntry? Result { get; private set; }

    public AddItemDialog(IEnumerable<string>? stores = null, string? defaultStore = null)
    {
        InitializeComponent();
        var storeList = stores?.ToList() ?? ["SM", "Robinsons", "Mercury Drug", "Grocery Store", "Bakery"];
        StoreCombo.ItemsSource = storeList;
        // Phase 2: pre-fill with last-used store; fall back to first in list
        var preferred = !string.IsNullOrWhiteSpace(defaultStore) && storeList.Contains(defaultStore)
            ? defaultStore
            : storeList.FirstOrDefault() ?? "SM";
        StoreCombo.Text = preferred;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAdd(sender, e);
        }
    }

    private void OnAdd(object s, RoutedEventArgs e)
    {
        ErrLabel.Text = "";
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrLabel.Text = "Item name is required.";
            NameBox.Focus();
            return;
        }

        if (!double.TryParse(QtyBox.Text, out var qty) || qty <= 0)
        {
            ErrLabel.Text = "Please enter a valid quantity (e.g. 1).";
            QtyBox.Focus();
            return;
        }

        // Price is optional/estimated in pasabuy — default to 0 if left blank
        var priceText = PriceBox.Text.Trim();
        decimal price = 0m;
        if (!string.IsNullOrEmpty(priceText))
        {
            if (!decimal.TryParse(priceText, out price) || price < 0)
            {
                ErrLabel.Text = "Please enter a valid price (or leave 0.00).";
                PriceBox.Focus();
                return;
            }
        }

        var storeName = string.IsNullOrWhiteSpace(StoreCombo.Text) ? "SM" : StoreCombo.Text.Trim();
        var itemNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        Result = new OrderItemEntry
        {
            Name = name,
            Qty = qty,
            Unit = UnitBox.Text.Trim().DefaultIfEmpty("pc"),
            Price = price,
            Store = storeName,
            Notes = itemNotes
        };

        DialogResult = true;
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}

public static class StringHelper
{
    public static string DefaultIfEmpty(this string s, string def) 
        => string.IsNullOrWhiteSpace(s) ? def : s;
}
