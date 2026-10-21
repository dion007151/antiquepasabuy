using System;
using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class AddStockDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Product _product;

    public AddStockDialog(AppDbContext db, Product product)
    {
        InitializeComponent();
        _db = db;
        _product = product;

        ProductSubtitle.Text = product.Name;
        CurrentStockLabel.Text = $"{product.StockQuantity:G} {product.Unit ?? "pc"}";
        Loaded += (_, _) => QuantityBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        ErrLabel.Visibility = Visibility.Collapsed;
        if (!double.TryParse(QuantityBox.Text.Trim(), out var qty) || qty <= 0)
        {
            ErrLabel.Text = "Please enter a valid quantity greater than zero.";
            ErrLabel.Visibility = Visibility.Visible;
            QuantityBox.Focus();
            return;
        }

        try
        {
            var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? "Added" : NoteBox.Text.Trim();
            var stockSvc = new StockService(_db);
            stockSvc.AddStock(_product.Id, qty, note);
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
