using System;
using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class NewMasterlistDialog : Window
{
    private readonly AppDbContext _db;
    private readonly User? _user;
    private readonly Masterlist? _currentMasterlist;
    private readonly MasterlistSummary _currentSummary;

    public Masterlist? CreatedMasterlist { get; private set; }

    public NewMasterlistDialog(AppDbContext? db = null, User? user = null)
    {
        InitializeComponent();
        _db = db ?? new AppDbContext();
        _user = user;

        StartDatePicker.SelectedDate = DateTime.Today;

        _currentMasterlist = MasterlistService.GetActive(_db);
        if (_currentMasterlist != null)
        {
            _currentSummary = MasterlistService.GetActiveSummary(_db);

            if (_currentSummary.OrdersWithPendingItems > 0 || _currentSummary.TotalUnpaidBalance > 0)
            {
                WarningPanel.Visibility = Visibility.Visible;
                WarningText.Text = $"{_currentSummary.OrdersWithPendingItems} order(s) still have items to buy. " +
                                    $"Total unpaid balance is ₱{_currentSummary.TotalUnpaidBalance:N2}.";
            }
            else
            {
                WarningPanel.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            _currentSummary = new MasterlistSummary();
            WarningPanel.Visibility = Visibility.Collapsed;
        }

        UpdateConfirmText();
        NameBox.TextChanged += (s, e) => UpdateConfirmText();
        NameBox.Focus();
    }

    private void UpdateConfirmText()
    {
        var previewName = string.IsNullOrWhiteSpace(NameBox.Text) ? "<new name>" : NameBox.Text.Trim();

        if (_currentMasterlist != null)
        {
            ConfirmText.Text = $"This closes {_currentMasterlist.Name}. Its {_currentSummary.OrderCount} orders stay where they are. New orders will be numbered {previewName}-001. Continue?";
        }
        else
        {
            ConfirmText.Text = $"New orders will be numbered {previewName}-001. Continue?";
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        ErrorLabel.Visibility = Visibility.Collapsed;
        var name = NameBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorLabel.Text = "Please enter a masterlist name.";
            ErrorLabel.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        if (!StartDatePicker.SelectedDate.HasValue)
        {
            ErrorLabel.Text = "Please select a start date.";
            ErrorLabel.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            CreatedMasterlist = MasterlistService.CloseAndCreate(_db, name, StartDatePicker.SelectedDate.Value, _user?.Id);
            OrderEventBus.NotifyDataRefreshed();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"Error: {ex.Message}";
            ErrorLabel.Visibility = Visibility.Visible;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
