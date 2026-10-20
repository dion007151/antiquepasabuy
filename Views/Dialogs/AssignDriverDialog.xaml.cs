using System.Windows;
using System.Windows.Controls;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class AssignDriverDialog : Window
{
    public string DriverName { get; private set; } = string.Empty;
    public string? DriverPhone { get; private set; }
    public string? Notes { get; private set; }

    private static readonly string[] DefaultDrivers =
    [
        "John Doe (Rider)",
        "Pedro Santos (Express)",
        "Maria Cruz (Delivery)",
        "Rico Reyes (Pasabuy)"
    ];

    public AssignDriverDialog(
        string? orderNo = null,
        string? customerName = null,
        string? currentDriver = null,
        string? currentPhone = null,
        string? currentNotes = null,
        IEnumerable<string>? existingDrivers = null)
    {
        InitializeComponent();

        SubtitleLabel.Text = $"Order: {orderNo ?? "N/A"} • Customer: {customerName ?? "Walk-in"}";

        var allDrivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingDrivers != null)
        {
            foreach (var d in existingDrivers.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                allDrivers.Add(d.Trim());
            }
        }
        foreach (var def in DefaultDrivers)
        {
            allDrivers.Add(def);
        }

        var driverList = allDrivers.OrderBy(x => x).ToList();
        driverList.Insert(0, "-- Select a registered driver --");
        driverList.Add("Other / Custom Driver...");

        DriverPresetCombo.ItemsSource = driverList;

        if (!string.IsNullOrWhiteSpace(currentDriver))
        {
            DriverNameBox.Text = currentDriver.Trim();
            var match = driverList.FirstOrDefault(d => string.Equals(d, currentDriver.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                DriverPresetCombo.SelectedItem = match;
            }
            else
            {
                DriverPresetCombo.SelectedIndex = 0;
            }
        }
        else
        {
            DriverPresetCombo.SelectedIndex = 0;
        }

        if (!string.IsNullOrWhiteSpace(currentPhone))
        {
            DriverPhoneBox.Text = currentPhone.Trim();
        }

        if (!string.IsNullOrWhiteSpace(currentNotes))
        {
            NotesBox.Text = currentNotes.Trim();
        }

        DriverNameBox.Focus();
        if (!string.IsNullOrWhiteSpace(DriverNameBox.Text))
        {
            DriverNameBox.SelectAll();
        }
    }

    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriverPresetCombo.SelectedItem is string selected)
        {
            if (selected == "-- Select a registered driver --")
            {
                // do not overwrite if user already typed
                return;
            }
            if (selected == "Other / Custom Driver...")
            {
                DriverNameBox.Focus();
                return;
            }

            DriverNameBox.Text = selected;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = DriverNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorLabel.Text = "Please enter or select a driver / rider name.";
            ErrorLabel.Visibility = Visibility.Visible;
            DriverNameBox.Focus();
            return;
        }

        DriverName = name;
        DriverPhone = string.IsNullOrWhiteSpace(DriverPhoneBox.Text) ? null : DriverPhoneBox.Text.Trim();
        Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
