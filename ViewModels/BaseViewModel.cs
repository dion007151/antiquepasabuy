using CommunityToolkit.Mvvm.ComponentModel;

namespace AntiquePasabuyPOS.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = "Loading...";
    [ObservableProperty] private string? _errorMessage;

    protected void SetError(string? msg) => ErrorMessage = msg;
    protected void ClearError() => ErrorMessage = null;
}
