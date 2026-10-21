using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public class StockMovementVm
{
    private readonly StockMovement _m;

    public DateTime Timestamp => _m.Timestamp;
    public double ChangeQty => _m.ChangeQty;
    public double NewQuantity => _m.NewQuantity;
    public string Note => _m.Note;

    public string FormattedChange => _m.ChangeQty > 0
        ? $"+{_m.ChangeQty:G}"
        : $"{_m.ChangeQty:G}";

    public Brush ChangeColor => _m.ChangeQty > 0
        ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) // Emerald Green
        : new SolidColorBrush(Color.FromRgb(239, 68, 68));  // Red

    public StockMovementVm(StockMovement m) => _m = m;
}

public partial class StockHistoryDialog : Window
{
    public StockHistoryDialog(AppDbContext db, Product product)
    {
        InitializeComponent();

        ProductSubtitle.Text = product.Name;
        CurrentStockLabel.Text = $"{product.StockQuantity:G} {product.Unit ?? "pc"}";

        var stockSvc = new StockService(db);
        var movements = stockSvc.GetMovements(product.Id);

        if (movements.Count == 0)
        {
            EmptyMsg.Visibility = Visibility.Visible;
            MovementsGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyMsg.Visibility = Visibility.Collapsed;
            MovementsGrid.Visibility = Visibility.Visible;
            MovementsGrid.ItemsSource = movements.Select(m => new StockMovementVm(m)).ToList();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
