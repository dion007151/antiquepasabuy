using System.Windows;
namespace AntiquePasabuyPOS.Views.Dialogs;
public partial class ResetPasswordDialog : Window
{
    public string? NewPassword { get; private set; }
    public ResetPasswordDialog(string userName) { InitializeComponent(); TitleLbl.Text = $"Reset Password: {userName}"; }
    private void OnReset(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";
        if (string.IsNullOrWhiteSpace(PwdBox.Password)) { ErrLbl.Text = "Password is required."; return; }
        if (PwdBox.Password != ConfirmBox.Password) { ErrLbl.Text = "Passwords do not match."; return; }
        NewPassword = PwdBox.Password; DialogResult = true;
    }
    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}
