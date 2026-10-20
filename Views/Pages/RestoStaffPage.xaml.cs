using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public partial class RestoStaffPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private readonly Action<string> _navigate;
    private List<RestoStaff> _staffMembers = new();

    public RestoStaffPage(AppDbContext db, User user, Action<string> navigate)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        _navigate = navigate;

        Loaded += async (_, _) => await LoadStaffAsync();
    }

    private async Task LoadStaffAsync()
    {
        try
        {
            _staffMembers = await RestaurantService.GetStaffMembersAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading staff: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (StaffGrid == null || _staffMembers == null) return;
        var search = TxtSearchStaff?.Text?.Trim().ToLower() ?? "";
        StaffGrid.ItemsSource = _staffMembers
            .Where(s => string.IsNullOrEmpty(search) ||
                        (s.FullName != null && s.FullName.ToLower().Contains(search)) ||
                        (s.StaffCode != null && s.StaffCode.ToLower().Contains(search)) ||
                        (s.Role != null && s.Role.ToLower().Contains(search)))
            .ToList();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnAddStaffClick(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this);
        var dlg = new RestoStaffDialog(new AppDbContext())
        {
            Owner = win
        };

        if (dlg.ShowDialog() == true)
        {
            _ = LoadStaffAsync();
        }
    }

    private void OnEditStaffRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoStaff staff)
        {
            EditStaff(staff);
        }
    }

    private void OnGridRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StaffGrid.SelectedItem is RestoStaff staff)
        {
            EditStaff(staff);
        }
    }

    private async void EditStaff(RestoStaff staff)
    {
        var win = Window.GetWindow(this);
        var dlgCtx = new AppDbContext();
        var tracked = await dlgCtx.RestoStaffMembers.FindAsync(staff.Id);
        if (tracked == null)
        {
            dlgCtx.Dispose();
            MessageBox.Show("Staff member not found. They may have been deleted.",
                "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new RestoStaffDialog(dlgCtx, tracked) { Owner = win };
        if (dlg.ShowDialog() == true)
            await LoadStaffAsync();

        dlgCtx.Dispose();
    }

    private async void OnDeleteStaffRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RestoStaff staff)
        {
            var res = MessageBox.Show($"Are you sure you want to remove staff member '{staff.FullName}'?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await RestaurantService.DeleteStaffAsync(staff.Id);
                await LoadStaffAsync();
            }
        }
    }
}
