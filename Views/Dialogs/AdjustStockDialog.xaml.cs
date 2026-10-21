using System;
using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class AdjustStockDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Product _product;

    public AdjustStockDialog(AppDbContext db, Product product)
    {
        InitializeComponent();
        _db = db;
        _product = product;

        ProductSubtitle.Text = product.Name;
        CurrentStockLabel.Text = $"{product.StockQuantity:G} {product.Unit ?? "pc"}";
        NewQuantityBox.Text = product.StockQuantity.ToString("G");
        Loaded += (_, _) =>
        {
            NewQuantityBox.Focus();
            NewQuantityBox.SelectAll();
        };
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        ErrLabel.Visibility = Visibility.Collapsed;
        if (!double.TryParse(NewQuantityBox.Text.Trim(), out var newQty) || newQty < 0)
        {
            ErrLabel.Text = "Please enter a valid stock quantity (0 or greater).";
            ErrLabel.Visibility = Visibility.Visible;
            NewQuantityBox.Focus();
            return;
        }

        var note = ReasonNoteBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            ErrLabel.Text = "A reason note is required for adjustments (e.g. damaged, spoilage).";
            ErrLabel.Visibility = Visibility.Visible;
            ReasonNoteBox.Focus();
            return;
        }

        try
        {
            var stockSvc = new StockService(_db);
            stockSvc.AdjustStock(_product.Id, newQty, note);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrLabel.Text = ex.Message;
            ErrLabel.Visibility = Visibility.Visible;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
