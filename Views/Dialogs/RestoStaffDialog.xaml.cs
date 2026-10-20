using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class RestoStaffDialog : Window
{
    private readonly AppDbContext _db;
    private readonly RestoStaff? _existing;

    private static readonly string[] Roles = { "Manager", "Chef", "Cook", "Server", "Bartender", "Cashier", "Dishwasher", "Host" };
    private static readonly string[] Shifts = { "Morning", "Afternoon", "Evening", "Night", "Full Day", "Split Shift" };

    public RestoStaffDialog(AppDbContext db, RestoStaff? existing = null)
    {
        InitializeComponent();
        _db = db;
        _existing = existing;

        RoleCombo.ItemsSource = Roles;
        ShiftCombo.ItemsSource = Shifts;
        HireDatePicker.SelectedDate = DateTime.Now;

        if (existing is not null)
        {
            TitleLbl.Text = "Edit Staff";
            CodeBox.Text = existing.StaffCode;
            NameBox.Text = existing.FullName;
            RoleCombo.Text = existing.Role;
            ShiftCombo.Text = existing.Shift;
            ContactBox.Text = existing.ContactNumber;
            HireDatePicker.SelectedDate = existing.HireDate;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        else
        {
            RoleCombo.Text = "Server";
            ShiftCombo.Text = "Evening";
        }
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrLbl.Text = "Full name is required.";
            NameBox.Focus();
            return;
        }

        var role = string.IsNullOrWhiteSpace(RoleCombo.Text) ? "Server" : RoleCombo.Text.Trim();
        var shift = string.IsNullOrWhiteSpace(ShiftCombo.Text) ? "Evening" : ShiftCombo.Text.Trim();
        var code = string.IsNullOrWhiteSpace(CodeBox.Text) ? GenerateCode() : CodeBox.Text.Trim();

        if (_existing is null)
        {
            var newStaff = new RestoStaff
            {
                StaffCode = code,
                FullName = NameBox.Text.Trim(),
                Role = role,
                Shift = shift,
                ContactNumber = ContactBox.Text?.Trim() ?? "",
                HireDate = HireDatePicker.SelectedDate ?? DateTime.Now,
                IsActive = ActiveCheck.IsChecked == true
            };
            _db.RestoStaffMembers.Add(newStaff);
        }
        else
        {
            // Ensure the entity is tracked before modifying it
            if (_db.Entry(_existing).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
                _db.RestoStaffMembers.Attach(_existing);

            _existing.StaffCode = code;
            _existing.FullName = NameBox.Text.Trim();
            _existing.Role = role;
            _existing.Shift = shift;
            _existing.ContactNumber = ContactBox.Text?.Trim() ?? "";
            _existing.HireDate = HireDatePicker.SelectedDate ?? _existing.HireDate;
            _existing.IsActive = ActiveCheck.IsChecked == true;
        }

        _db.SaveChanges();
        DialogResult = true;
    }

    private string GenerateCode()
    {
        var count = _db.RestoStaffMembers.Count() + 1;
        return $"STF-{count:D3}";
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}
