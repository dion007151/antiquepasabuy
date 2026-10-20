using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly AppDbContext _db;
    public Action<User>? OnLoginSuccess { get; set; }

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _loginError = "";

    public LoginViewModel(AppDbContext db) => _db = db;

    [RelayCommand]
    private void Login()
    {
        LoginError = "";
        if (string.IsNullOrWhiteSpace(Username))
        {
            LoginError = "Please enter your username.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            LoginError = "Please enter your password.";
            return;
        }
        var user = _db.Users.FirstOrDefault(u => u.Username == Username && u.IsActive);
        if (user is null || !BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash))
        {
            LoginError = "Incorrect username or password.";
            return;
        }
        OnLoginSuccess?.Invoke(user);
    }
}
