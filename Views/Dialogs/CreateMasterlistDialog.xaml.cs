using System.Windows;
using System.Windows.Controls;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class CreateMasterlistDialog : Window
{
    private readonly AppDbContext _db;
    private readonly int _userId;

    public Masterlist? CreatedMasterlist { get; private set; }

    public CreateMasterlistDialog(AppDbContext db, int userId)
    {
        InitializeComponent();
        _db     = db;
        _userId = userId;

        // Default to current month/year
        var now = DateTime.Now;
        MonthBox.SelectedIndex = now.Month - 1;
        YearBox.Text           = now.Year.ToString();

        // Default date range to current week (Mon–Sun)
        var today    = now.Date;
        var monday   = today.AddDays(-(int)today.DayOfWeek == 0 ? 6 : (int)today.DayOfWeek - 1);
        var sunday   = monday.AddDays(6);
        StartDatePicker.SelectedDate = monday;
        EndDatePicker.SelectedDate   = sunday;

        RefreshControlNumber();
    }

    // ── Auto-refresh control number ────────────────────────────────────────

    private void RefreshControlNumber()
    {
        if (!TryGetMonthYear(out int month, out int year)) return;
        try
        {
            var ctrl = MasterlistService.GenerateControlNumber(_db, month, year);
            ControlNumberLabel.Text = ctrl;
        }
        catch { ControlNumberLabel.Text = "—"; }
    }

    private void OnMonthYearChanged(object sender, EventArgs e)
        => RefreshControlNumber();

    private void OnDateChanged(object sender, SelectionChangedEventArgs e)
    {
        // Auto-fill name when both dates are set and name is empty
        if (StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue
            && string.IsNullOrWhiteSpace(NameBox.Text))
        {
            if (TryGetMonthYear(out int month, out int year))
            {
                var monthName = new DateTime(year, month, 1).ToString("MMMM");
                // Count existing for this month to get week number
                var existingCount = _db.Masterlists
                    .Count(m => m.Month == month && m.Year == year);
                NameBox.Text = $"Week {existingCount + 1} – {monthName} {year}";
            }
        }
    }

    // ── Validation & create ────────────────────────────────────────────────

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        HideError();

        // 1. Name
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Please enter a masterlist name or week label.");
            NameBox.Focus();
            return;
        }

        // 2. Month / Year
        if (!TryGetMonthYear(out int month, out int year))
        {
            ShowError("Please select a valid month and enter a 4-digit year.");
            return;
        }

        // 3. Dates
        if (!StartDatePicker.SelectedDate.HasValue)
        {
            ShowError("Please select a start date.");
            return;
        }
        if (!EndDatePicker.SelectedDate.HasValue)
        {
            ShowError("Please select an end date.");
            return;
        }
        var start = StartDatePicker.SelectedDate.Value.Date;
        var end   = EndDatePicker.SelectedDate.Value.Date;
        if (end < start)
        {
            ShowError("End date cannot be before the start date.");
            return;
        }

        // 4. Generate (and re-validate) control number
        var controlNumber = MasterlistService.GenerateControlNumber(_db, month, year);
        if (MasterlistService.ControlNumberExists(_db, controlNumber))
        {
            ShowError($"Control number '{controlNumber}' already exists. Please try again.");
            return;
        }

        // 5. Overlap check
        if (MasterlistService.HasOverlap(_db, start, end, month, year))
        {
            ShowError("The selected date range overlaps with an existing masterlist for this month. Please adjust the dates.");
            return;
        }

        // 6. Create
        var masterlist = new Masterlist
        {
            ControlNumber = controlNumber,
            Name          = name,
            Month         = month,
            Year          = year,
            StartDate     = start,
            EndDate       = end,
            Notes         = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            Status        = "Active",
            CreatedBy     = _userId,
            CreatedAt     = DateTime.Now,
            UpdatedAt     = DateTime.Now,
        };

        try
        {
            MasterlistService.Create(_db, masterlist);
            CreatedMasterlist = masterlist;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to create masterlist: {ex.Message}");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool TryGetMonthYear(out int month, out int year)
    {
        month = 0; year = 0;
        if (MonthBox == null || YearBox == null) return false;
        if (MonthBox.SelectedItem is not ComboBoxItem item) return false;
        if (!int.TryParse(item.Tag?.ToString(), out month))  return false;
        if (!int.TryParse(YearBox.Text?.Trim(), out year))   return false;
        if (month < 1 || month > 12 || year < 2020 || year > 2100) return false;
        return true;
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text      = msg;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
        => ErrorBorder.Visibility = Visibility.Collapsed;
}
