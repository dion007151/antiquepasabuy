using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using MaterialDesignThemes.Wpf;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class CustomerDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Customer? _existing;
    private readonly string _businessId;

    public CustomerDialog(AppDbContext db, Customer? existing = null, string? businessId = null)
    {
        InitializeComponent();
        _db = db;
        _existing = existing;
        _businessId = businessId ?? (existing != null ? existing.BusinessId : (BusinessProfileService.IsAngelitos ? BusinessProfileService.ANGELITOS_RESTO : BusinessProfileService.ANTIQUE_PASABUY));

        if (existing is not null)
        {
            TitleLabel.Text = "Edit Customer";
            HeaderIcon.Kind = PackIconKind.AccountEdit;
            NameBox.Text = existing.Name;
            PhoneBox.Text = existing.Phone;
            AddressBox.Text = existing.Address;
            NotesBox.Text = existing.Notes;
        }
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrorLabel.Text = "";
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorLabel.Text = "Name is required.";
            NameBox.Focus();
            return;
        }

        var svc = new CustomerService(_db);
        if (_existing is null)
        {
            svc.Create(name, PhoneBox.Text.Trim(), AddressBox.Text.Trim(), NotesBox.Text.Trim(), _businessId);
        }
        else
        {
            svc.Update(_existing.Id, name, PhoneBox.Text.Trim(), AddressBox.Text.Trim(), NotesBox.Text.Trim());
        }

        DialogResult = true;
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;

    private void OnFormPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Smooth form scrolling
        if (sender is ScrollViewer scv)
        {
            scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta / 2.0));
            e.Handled = true;
        }
    }

    private void OnTextBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox tb)
        {
            // If the textbox is multi-line and has overflow text to scroll in this direction, let it scroll
            bool canScrollDown = e.Delta < 0 && tb.VerticalOffset < (tb.ExtentHeight - tb.ViewportHeight - 1);
            bool canScrollUp = e.Delta > 0 && tb.VerticalOffset > 1;

            if (canScrollDown || canScrollUp)
            {
                return; // Let the textbox handle its internal scroll
            }
        }

        // Otherwise bubble the wheel event to the parent FormScrollViewer so the whole dialog scrolls smoothly
        e.Handled = true;
        FormScrollViewer.ScrollToVerticalOffset(FormScrollViewer.VerticalOffset - (e.Delta / 2.0));
    }
}
