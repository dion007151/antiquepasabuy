using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Globalization;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Views.Pages;

public class ActiveColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true ? new SolidColorBrush(Color.FromRgb(16,185,129)) : new SolidColorBrush(Color.FromRgb(239,68,68));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
public class ActiveTextConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is true ? "Active" : "Inactive";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class UserRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public User User { get; }
    public Action? OnSelectionChanged { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); OnSelectionChanged?.Invoke(); }
        }
    }

    public UserRowVm(User u, Action? onSelectionChanged = null)
    {
        User = u;
        OnSelectionChanged = onSelectionChanged;
    }

    public int      Id        => User.Id;
    public string   Username  => User.Username;
    public string   FullName  => User.FullName;
    public UserRole Role      => User.Role;
    public bool     IsActive  => User.IsActive;
    public DateTime CreatedAt => User.CreatedAt;
}

public partial class UsersPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private List<UserRowVm> _all = [];
    private List<UserRowVm> _visible = [];

    public UsersPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null)
    {
        InitializeComponent();
        _db = db; _user = user;
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        _all = _db.Users.OrderBy(u => u.FullName).ToList()
                   .Select(u => new UserRowVm(u, UpdateSelectionState)).ToList();
        ApplySearch();
    }

    private void ApplySearch()
    {
        if (SearchBox is null || UsersGrid is null || _all is null) return;
        var t = SearchBox.Text?.Trim().ToLower() ?? "";
        _visible = string.IsNullOrEmpty(t) ? _all
            : _all.Where(u => (u.FullName?.ToLower().Contains(t) ?? false) ||
                              (u.Username?.ToLower().Contains(t) ?? false) ||
                              u.Role.ToString().ToLower().Contains(t)).ToList();
        UsersGrid.ItemsSource = _visible;
        UpdateSelectionState();
    }

    // ══ SELECTION CONTROLS ════════════════════════════════════════════════════
    private void OnHeaderSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            var target = cb.IsChecked == true;
            foreach (var r in _visible) r.IsSelected = target;
            UpdateSelectionState();
        }
    }

    private void OnRowCheckBoxClick(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _visible) r.IsSelected = true;
        UpdateSelectionState();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _all) r.IsSelected = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var count = _visible.Count(r => r.IsSelected);
        if (SelectedCountLabel != null)
            SelectedCountLabel.Text = $"{count} selected";
        if (BulkSelectionPanel != null)
            BulkSelectionPanel.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (BulkDeleteBtn != null)
            BulkDeleteBtn.IsEnabled = count > 0;
    }

    private void OnBulkDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = _visible.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        // Prevent deleting the currently logged-in user
        if (selected.Any(r => r.Id == _user.Id))
        {
            MessageBox.Show("You cannot delete your own logged-in account. Deselect it and try again.",
                "Action Prohibited", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var res = MessageBox.Show(
            $"Are you sure you want to delete {selected.Count} user(s)?\n\nThis cannot be undone.",
            "Confirm Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        int deleted = 0;
        foreach (var vm in selected)
        {
            var u = _db.Users.Find(vm.Id);
            if (u != null) { _db.Users.Remove(u); deleted++; }
        }
        _db.SaveChanges();
        MessageBox.Show($"Deleted {deleted} user(s).", "Bulk Delete", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadData();
    }

    private void OnClearSearch(object s, RoutedEventArgs e)
    {
        if (SearchBox is not null)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }
    }

    private void OnSearch(object s, TextChangedEventArgs e) => ApplySearch();

    private void OnAddUser(object s, RoutedEventArgs e)
    {
        var dlg = new Dialogs.UserDialog(_db);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnEditUser(object s, RoutedEventArgs e)
    {
        if (s is not Button btn) return;
        int id = btn.Tag is int i ? i : (btn.DataContext is UserRowVm vm ? vm.Id : -1);
        if (id < 0) return;
        var u = _db.Users.Find(id);
        if (u is null) return;
        var dlg = new Dialogs.UserDialog(_db, u);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnResetPassword(object s, RoutedEventArgs e)
    {
        if (s is not Button btn) return;
        int id = btn.Tag is int i ? i : (btn.DataContext is UserRowVm vm ? vm.Id : -1);
        if (id < 0) return;
        var u = _db.Users.Find(id);
        if (u is null) return;
        var dlg = new Dialogs.ResetPasswordDialog(u.FullName);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true && dlg.NewPassword is not null)
        {
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dlg.NewPassword);
            u.UpdatedAt = DateTime.Now;
            _db.SaveChanges();
            MessageBox.Show("Password reset successfully.", "Done");
            LoadData();
        }
    }

    private void OnDeleteUser(object s, RoutedEventArgs e)
    {
        if (s is not Button btn) return;
        int id = btn.Tag is int i ? i : (btn.DataContext is UserRowVm vm ? vm.Id : -1);
        if (id < 0) return;
        var u = _db.Users.Find(id);
        if (u is null) return;

        if (u.Id == _user.Id)
        {
            MessageBox.Show("You cannot delete your own logged-in account.", "Action Prohibited",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var res = MessageBox.Show(
            $"Are you sure you want to delete user '{u.FullName}' (@{u.Username})?",
            "Confirm Delete User",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            _db.Users.Remove(u);
            _db.SaveChanges();
            MessageBox.Show($"User '{u.FullName}' has been deleted.", "User Deleted",
                MessageBoxButton.OK, MessageBoxImage.Information);
            LoadData();
        }
    }
}
