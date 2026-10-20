using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class RestoMenuDialog : Window
{
    private readonly AppDbContext _db;
    private readonly RestoMenuItem? _existing;
    private string? _pendingSourceImagePath;
    private bool _imageWasRemoved = false;

    private static readonly string[] DefaultCategories = 
    { 
        "Appetizer", "Sizzling", "Grill", "Pulutan", "Soup", 
        "Rice", "Noodles", "Seafood", "Drinks", "Alcoholic", 
        "Cocktails", "Dessert", "Combo Meals", "Ala Carte" 
    };

    public RestoMenuDialog(AppDbContext db, RestoMenuItem? existing = null)
    {
        InitializeComponent();
        _db = db;
        _existing = existing;

        CategoryCombo.ItemsSource = DefaultCategories;

        if (existing is not null)
        {
            TitleLbl.Text = "Edit Dish";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            CategoryCombo.Text = existing.Category;
            PriceBox.Text = existing.Price.ToString("N2");
            CostPriceBox.Text = existing.CostPrice?.ToString("N2") ?? "";
            DescBox.Text = existing.Description ?? "";
            AvailableCheck.IsChecked = existing.IsAvailable;

            // Load existing image if present
            if (!string.IsNullOrWhiteSpace(existing.ImagePath))
            {
                var bitmap = ProductImageService.LoadBitmap(existing.ImagePath, 140);
                if (bitmap != null)
                {
                    DishImgPreview.Source = bitmap;
                    DishImgPreview.Visibility = Visibility.Visible;
                    PlaceholderIcon.Visibility = Visibility.Collapsed;
                    RemoveImgBtn.Visibility = Visibility.Visible;
                    ImageStatusLabel.Text = Path.GetFileName(existing.ImagePath);
                }
            }
        }
        else
        {
            CategoryCombo.Text = "Grill";
        }
    }

    private void OnChooseImage(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Dish Photo",
                Filter = "Image Files (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                _pendingSourceImagePath = dlg.FileName;
                _imageWasRemoved = false;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
                bi.DecodePixelWidth = 140;
                bi.EndInit();
                bi.Freeze();

                DishImgPreview.Source = bi;
                DishImgPreview.Visibility = Visibility.Visible;
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

        DishImgPreview.Source = null;
        DishImgPreview.Visibility = Visibility.Collapsed;
        PlaceholderIcon.Visibility = Visibility.Visible;
        RemoveImgBtn.Visibility = Visibility.Collapsed;
        ImageStatusLabel.Text = "No image selected";
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrLbl.Text = "Dish name is required.";
            NameBox.Focus();
            return;
        }

        if (!decimal.TryParse(PriceBox.Text.Replace("₱", "").Trim(), out var price) || price < 0)
        {
            ErrLbl.Text = "Please enter a valid selling price (₱).";
            PriceBox.Focus();
            return;
        }

        decimal? costPrice = null;
        var cpText = CostPriceBox.Text.Replace("₱", "").Trim();
        if (!string.IsNullOrWhiteSpace(cpText))
        {
            if (decimal.TryParse(cpText, out var cp) && cp >= 0)
                costPrice = cp;
        }

        var code = string.IsNullOrWhiteSpace(CodeBox.Text) ? GenerateCode() : CodeBox.Text.Trim();
        var category = string.IsNullOrWhiteSpace(CategoryCombo.Text) ? "Grill" : CategoryCombo.Text.Trim();

        if (_existing is null)
        {
            var newItem = new RestoMenuItem
            {
                Code = code,
                Name = NameBox.Text.Trim(),
                Category = category,
                Price = price,
                CostPrice = costPrice,
                Description = string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim(),
                IsAvailable = AvailableCheck.IsChecked == true,
                CreatedAt = DateTime.Now
            };
            _db.RestoMenuItems.Add(newItem);
            _db.SaveChanges(); // Generates Id

            if (!string.IsNullOrWhiteSpace(_pendingSourceImagePath))
            {
                var savedRel = ProductImageService.SaveProductImage(_pendingSourceImagePath, newItem.Id);
                if (savedRel != null)
                {
                    newItem.ImagePath = savedRel;
                    _db.SaveChanges();
                }
            }
        }
        else
        {
            // Ensure the entity is tracked before modifying it
            if (_db.Entry(_existing).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
                _db.RestoMenuItems.Attach(_existing);

            _existing.Code = code;
            _existing.Name = NameBox.Text.Trim();
            _existing.Category = category;
            _existing.Price = price;
            _existing.CostPrice = costPrice;
            _existing.Description = string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim();
            _existing.IsAvailable = AvailableCheck.IsChecked == true;

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

    private string GenerateCode()
    {
        var cat = (CategoryCombo.Text ?? "DISH").Trim().ToUpper();
        var prefix = cat.Length >= 3 ? cat[..3] : cat;
        var count = _db.RestoMenuItems.Count(m => m.Category == (CategoryCombo.Text ?? "Grill")) + 1;
        return $"{prefix}-{count:D3}";
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}
