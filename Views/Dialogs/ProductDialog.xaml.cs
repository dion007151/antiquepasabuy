using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class ProductDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Product? _existing;
    private string? _pendingSourceImagePath;
    private bool _imageWasRemoved = false;

    public ProductDialog(AppDbContext db, Product? existing = null)
    {
        InitializeComponent();
        _db = db;
        _existing = existing;

        var stores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => s.Name).ToList();
        if (stores.Count == 0) stores = ["SM", "Robinsons", "Mercury Drug", "Grocery Store", "Bakery"];
        StoreCombo.ItemsSource = stores;

        if (existing is not null)
        {
            TitleLbl.Text = "Edit Product";
            NameBox.Text = existing.Name;
            BarcodeBox.Text = existing.Barcode ?? "";
            CatBox.Text = existing.Category;
            StoreCombo.Text = existing.Store ?? "SM";
            CostPriceBox.Text = existing.CostPrice.HasValue ? existing.CostPrice.Value.ToString("N2") : "";
            PriceBox.Text = existing.Price.ToString("N2");
            UnitBox.Text = existing.Unit;

            // Load Pasabuy Fee setting
            if (existing.FeeType == ProductFeeType.MANUAL)
            {
                ManualFeeRadio.IsChecked = true;
                ManualFeeAmountBox.Text = existing.ManualFeeAmount?.ToString("N2") ?? "";
                if (ManualFeePanel != null) ManualFeePanel.Visibility = Visibility.Visible;
            }
            else if (existing.FeeType == ProductFeeType.NONE || existing.IsFeeExempt)
            {
                NoFeeRadio.IsChecked = true;
                if (ManualFeePanel != null) ManualFeePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                StandardFeeRadio.IsChecked = true;
                if (ManualFeePanel != null) ManualFeePanel.Visibility = Visibility.Collapsed;
            }

            // Load existing image if present
            if (!string.IsNullOrWhiteSpace(existing.ImagePath))
            {
                var bitmap = ProductImageService.LoadBitmap(existing.ImagePath, 140);
                if (bitmap != null)
                {
                    ProductImgPreview.Source = bitmap;
                    ProductImgPreview.Visibility = Visibility.Visible;
                    PlaceholderIcon.Visibility = Visibility.Collapsed;
                    RemoveImgBtn.Visibility = Visibility.Visible;
                    ImageStatusLabel.Text = Path.GetFileName(existing.ImagePath);
                }
            }
        }
        else
        {
            StoreCombo.Text = "SM";
        }
    }

    private void OnChooseImage(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Product Image",
                Filter = "Image Files (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                _pendingSourceImagePath = dlg.FileName;
                _imageWasRemoved = false;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad; // Never lock the source file
                bi.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
                bi.DecodePixelWidth = 140;
                bi.EndInit();
                bi.Freeze();

                ProductImgPreview.Source = bi;
                ProductImgPreview.Visibility = Visibility.Visible;
                PlaceholderIcon.Visibility = Visibility.Collapsed;
                RemoveImgBtn.Visibility = Visibility.Visible;
                ImageStatusLabel.Text = Path.GetFileName(dlg.FileName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open image file: {ex.Message}", "Image Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRemoveImage(object sender, RoutedEventArgs e)
    {
        _pendingSourceImagePath = null;
        _imageWasRemoved = true;

        ProductImgPreview.Source = null;
        ProductImgPreview.Visibility = Visibility.Collapsed;
        PlaceholderIcon.Visibility = Visibility.Visible;
        RemoveImgBtn.Visibility = Visibility.Collapsed;
        ImageStatusLabel.Text = "No image selected (default placeholder will show)";
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrLbl.Text = "Product name is required.";
            NameBox.Focus();
            return;
        }

        if (!decimal.TryParse(PriceBox.Text.Replace("₱", "").Trim(), out var price) || price < 0)
        {
            ErrLbl.Text = "Please enter a valid price (₱).";
            PriceBox.Focus();
            return;
        }

        var storeName = string.IsNullOrWhiteSpace(StoreCombo.Text) ? "SM" : StoreCombo.Text.Trim();
        var categoryName = string.IsNullOrWhiteSpace(CatBox.Text) ? "General" : CatBox.Text.Trim();
        var unitName = string.IsNullOrWhiteSpace(UnitBox.Text) ? "pc" : UnitBox.Text.Trim();
        var barcodeVal = string.IsNullOrWhiteSpace(BarcodeBox.Text) ? null : BarcodeBox.Text.Trim();

        decimal? costPrice = null;
        if (!string.IsNullOrWhiteSpace(CostPriceBox.Text) &&
            decimal.TryParse(CostPriceBox.Text.Replace("₱", "").Trim(), out var parsedCost) && parsedCost >= 0)
        {
            costPrice = parsedCost;
        }

        // Fee calculation mode
        var feeType = ProductFeeType.STANDARD;
        decimal? manualFee = null;
        if (ManualFeeRadio.IsChecked == true)
        {
            feeType = ProductFeeType.MANUAL;
            if (string.IsNullOrWhiteSpace(ManualFeeAmountBox.Text) ||
                !decimal.TryParse(ManualFeeAmountBox.Text.Replace("₱", "").Trim(), out var parsedFee) ||
                parsedFee < 0)
            {
                ErrLbl.Text = "Please enter a valid flat fee per unit (₱0.00 or higher).";
                ManualFeeAmountBox.Focus();
                return;
            }
            manualFee = parsedFee;
        }
        else if (NoFeeRadio.IsChecked == true)
        {
            feeType = ProductFeeType.NONE;
        }

        if (_existing is null)
        {
            var newProd = new Product
            {
                Name = NameBox.Text.Trim(),
                Barcode = barcodeVal,
                Category = categoryName,
                Store = storeName,
                CostPrice = costPrice,
                Price = price,
                Unit = unitName,
                IsActive = true,
                FeeType = feeType,
                ManualFeeAmount = manualFee,
                IsFeeExempt = feeType == ProductFeeType.NONE,
                CreatedAt = DateTime.Now
            };
            _db.Products.Add(newProd);
            _db.SaveChanges(); // Generates Id

            if (!string.IsNullOrWhiteSpace(_pendingSourceImagePath))
            {
                var savedRel = ProductImageService.SaveProductImage(_pendingSourceImagePath, newProd.Id);
                if (savedRel != null)
                {
                    newProd.ImagePath = savedRel;
                    _db.SaveChanges();
                }
            }
        }
        else
        {
            _existing.Name = NameBox.Text.Trim();
            _existing.Barcode = barcodeVal;
            _existing.Category = categoryName;
            _existing.Store = storeName;
            _existing.CostPrice = costPrice;
            _existing.Price = price;
            _existing.Unit = unitName;
            _existing.FeeType = feeType;
            _existing.ManualFeeAmount = manualFee;
            _existing.IsFeeExempt = feeType == ProductFeeType.NONE;

            if (_imageWasRemoved)
            {
                var oldImg = _existing.ImagePath;
                _existing.ImagePath = null;
                _db.SaveChanges();
                ProductImageService.DeleteProductImage(oldImg);
            }
            else if (!string.IsNullOrWhiteSpace(_pendingSourceImagePath))
            {
                var oldImg = _existing.ImagePath;
                var savedRel = ProductImageService.SaveProductImage(_pendingSourceImagePath, _existing.Id);
                if (savedRel != null)
                {
                    _existing.ImagePath = savedRel;
                    _db.SaveChanges();
                    if (!string.Equals(oldImg, savedRel, StringComparison.OrdinalIgnoreCase))
                    {
                        ProductImageService.DeleteProductImage(oldImg);
                    }
                }
            }
            else
            {
                _db.SaveChanges();
            }
        }

        DialogResult = true;
    }

    private void OnScanBarcodeClick(object sender, RoutedEventArgs e)
    {
        var dlg = new BarcodeScannerDialog(_db, (Product scannedProduct) =>
        {
            if (scannedProduct == null) return;
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(scannedProduct.Barcode))
                    BarcodeBox.Text = scannedProduct.Barcode;

                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = scannedProduct.Name;

                if (scannedProduct.Price > 0 && (string.IsNullOrWhiteSpace(PriceBox.Text) || PriceBox.Text == "0.00"))
                    PriceBox.Text = scannedProduct.Price.ToString("N2");

                if (!string.IsNullOrWhiteSpace(scannedProduct.Category) && (string.IsNullOrWhiteSpace(CatBox.Text) || CatBox.Text == "General"))
                    CatBox.Text = scannedProduct.Category;

                if (!string.IsNullOrWhiteSpace(scannedProduct.Store))
                    StoreCombo.Text = scannedProduct.Store;
            });
        })
        {
            Owner = this,
            Title = "Scan Product Barcode"
        };
        dlg.ShowDialog();
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;

    private void OnFeeRadioChanged(object sender, RoutedEventArgs e)
    {
        if (ManualFeePanel == null) return;
        ManualFeePanel.Visibility = (ManualFeeRadio.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
    }
}

