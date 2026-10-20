using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class StoresPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private List<Store> _allStores = [];

    public StoresPage(AppDbContext db, User user, Action<string>? navigate = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        Loaded += (_, _) =>
        {
            if (_allStores.Count > 0)
            {
                ApplyFilter();
                Dispatcher.InvokeAsync(LoadStores, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                LoadStores();
            }
            NewStoreNameBox.Focus();
        };
    }

    // ── Load / filter ──────────────────────────────────────────────────────

    private bool _isLoadingStores = false;

    private async void LoadStores()
    {
        if (_isLoadingStores) return;
        _isLoadingStores = true;
        try
        {
            List<Store> stores = [];
            await Task.Run(() =>
            {
                using var bgDb = new AppDbContext();
                stores = bgDb.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).ToList();
            });
            _allStores = stores;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StoresPage] LoadStores error: {ex.Message}");
        }
        finally
        {
            _isLoadingStores = false;
        }
    }

    private void ApplyFilter()
    {
        var term = SearchBox?.Text?.Trim().ToLower() ?? "";
        var filtered = string.IsNullOrEmpty(term)
            ? _allStores
            : _allStores.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.Branch?.ToLower().Contains(term) ?? false)).ToList();

        StoresListControl.ItemsSource = filtered;
        EmptyPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    // ── Add store ──────────────────────────────────────────────────────────

    private void OnAddStore(object sender, RoutedEventArgs e)
    {
        ErrorLabel.Visibility = Visibility.Collapsed;

        var name = NewStoreNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorLabel.Text = "Please enter a store name.";
            ErrorLabel.Visibility = Visibility.Visible;
            NewStoreNameBox.Focus();
            return;
        }

        var exists = _db.Stores.Any(s => s.Name.ToLower() == name.ToLower() && s.IsActive);
        if (exists)
        {
            ErrorLabel.Text = $"Store '{name}' already exists.";
            ErrorLabel.Visibility = Visibility.Visible;
            return;
        }

        var branch = string.IsNullOrWhiteSpace(NewStoreBranchBox.Text)
            ? "Main Branch"
            : NewStoreBranchBox.Text.Trim();

        _db.Stores.Add(new Store
        {
            Name      = name,
            Branch    = branch,
            IsActive  = true,
            CreatedAt = DateTime.Now
        });
        _db.SaveChanges();

        NewStoreNameBox.Text   = "";
        NewStoreBranchBox.Text = "";
        LoadStores();

        StatusMsg.Text       = $"Store '{name}' added!";
        StatusMsg.Foreground = new SolidColorBrush(Color.FromRgb(13, 148, 136));
    }

    // ── Delete store ───────────────────────────────────────────────────────

    private void OnDeleteStore(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Store store) return;
        if (MessageBox.Show(
            $"Remove store '{store.Name}'?",
            "Confirm Remove", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        store.IsActive = false;
        _db.SaveChanges();
        LoadStores();

        StatusMsg.Text       = $"Store '{store.Name}' removed.";
        StatusMsg.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
    }
}
