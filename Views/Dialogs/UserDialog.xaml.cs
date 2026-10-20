using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class UserDialog : Window
{
    private readonly AppDbContext _db;
    private readonly User? _existing;
    public UserDialog(AppDbContext db, User? existing = null)
    {
        InitializeComponent();
        _db = db; _existing = existing;
        if (existing is not null)
        {
            TitleLbl.Text = "Edit User";
            FullNameBox.Text = existing.FullName;
            UsernameBox.Text = existing.Username;
            PwdLabel.Text = "New Password (leave blank to keep)";
            RoleCombo.SelectedIndex = (int)existing.Role;
        }
    }
    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";
        if (string.IsNullOrWhiteSpace(FullNameBox.Text)) { ErrLbl.Text = "Full name required."; return; }
        if (string.IsNullOrWhiteSpace(UsernameBox.Text)) { ErrLbl.Text = "Username required."; return; }
        if (_existing is null && string.IsNullOrWhiteSpace(PwdBox.Password)) { ErrLbl.Text = "Password required."; return; }
        var role = Enum.Parse<UserRole>((RoleCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Staff");
        if (_existing is null)
        {
            _db.Users.Add(new User
            {
                FullName = FullNameBox.Text.Trim(), Username = UsernameBox.Text.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(PwdBox.Password), Role = role
            });
        }
        else
        {
            _existing.FullName = FullNameBox.Text.Trim();
            _existing.Username = UsernameBox.Text.Trim();
            _existing.Role = role;
            _existing.UpdatedAt = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(PwdBox.Password))
                _existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(PwdBox.Password);
        }
        _db.SaveChanges();
        DialogResult = true;
    }
    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}
