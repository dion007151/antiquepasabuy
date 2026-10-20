using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    public AppDbContext Db { get; }
    public User CurrentUser { get; }

    [ObservableProperty] private string _activeView = "Dashboard";
    [ObservableProperty] private string _pageTitle = "Dashboard";

    public MainViewModel(AppDbContext db, User user)
    {
        Db = db;
        CurrentUser = user;
    }

    [RelayCommand]
    private void Navigate(string view)
    {
        ActiveView = view;
        PageTitle = view switch
        {
            "Dashboard"     => "Dashboard",
            "NewOrder"      => "New Order",
            "Orders"        => "Orders",
            "Shopping"      => "Shopping Queue",
            "Delivery"      => "Delivery",
            "Customers"     => "Customers",
            "Products"      => "Products",
            "Payments"      => "Payments",
            "Expenses"      => "Expenses",
            "Reports"       => "Reports",
            "Users"         => "User Management",
            "Settings"      => "Settings",
            _ => view
        };
    }
}
