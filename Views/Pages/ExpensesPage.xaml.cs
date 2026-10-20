using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using AntiquePasabuyPOS.Views.Dialogs;

namespace AntiquePasabuyPOS.Views.Pages;

public class ExpenseRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public Expense Expense { get; }
    public Action? OnSelectionChanged { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); OnSelectionChanged?.Invoke(); }
        }
    }

    public ExpenseRowVm(Expense e, Action? onSelectionChanged = null)
    {
        Expense = e;
        OnSelectionChanged = onSelectionChanged;
    }

    // Forwarded properties bound in XAML
    public int     Id          => Expense.Id;
    public string  Description => Expense.Description ?? "Expense";
    public string  Category    => Expense.Category ?? "General";
    public decimal Amount      => Expense.Amount;
    public DateOnly ExpenseDate => Expense.ExpenseDate;
    public string  Notes       => Expense.Notes ?? "-";
}

public partial class ExpensesPage : Page
{
    private readonly AppDbContext _db;
    private readonly User _user;
    private List<ExpenseRowVm> _all = [];
    private List<ExpenseRowVm> _visible = [];
    private string _activeFilter = "all";

    public ExpensesPage(AppDbContext db, User user, Action<string>? navigate = null, Action? onSuccess = null)
    {
        InitializeComponent();
        _db = db;
        _user = user;
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        var expenses = _db.Expenses.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id).ToList();
        _all = expenses.Select(e => new ExpenseRowVm(e, UpdateSelectionState)).ToList();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayExp = expenses.Where(e => e.ExpenseDate == today).Sum(e => e.Amount);
        var monthExp = expenses.Where(e => e.ExpenseDate.Year == today.Year && e.ExpenseDate.Month == today.Month).Sum(e => e.Amount);

        TodayLabel.Text = $"₱{todayExp:N2}";
        MonthLabel.Text = $"₱{monthExp:N2}";

        ApplyFilter();
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeFilter = tag.ToLower();

            SetButtonStyle(FilterAllBtn, _activeFilter == "all");
            SetButtonStyle(FilterTodayBtn, _activeFilter == "today");
            SetButtonStyle(FilterWeekBtn, _activeFilter == "week");
            SetButtonStyle(FilterMonthBtn, _activeFilter == "month");

            ApplyFilter();
        }
    }

    private void SetButtonStyle(Button btn, bool isActive)
    {
        NavProps.SetIsActive(btn, isActive);
        btn.Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
    }

    private void ApplyFilter()
    {
        if (SearchBox is null || MainGrid is null || _all is null) return;
        var t = SearchBox.Text?.Trim().ToLower() ?? "";
        var today = DateOnly.FromDateTime(DateTime.Today);

        var filtered = _all.AsEnumerable();

        if (_activeFilter == "today")
            filtered = filtered.Where(e => e.ExpenseDate == today);
        else if (_activeFilter == "week")
        {
            var dayOfWeek = (int)DateTime.Today.DayOfWeek;
            var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek;
            var startOfWeek = DateOnly.FromDateTime(DateTime.Today.AddDays(mondayOffset));
            filtered = filtered.Where(e => e.ExpenseDate >= startOfWeek && e.ExpenseDate <= today);
        }
        else if (_activeFilter == "month")
            filtered = filtered.Where(e => e.ExpenseDate.Year == today.Year && e.ExpenseDate.Month == today.Month);

        if (!string.IsNullOrEmpty(t))
            filtered = filtered.Where(e => (e.Description?.ToLower().Contains(t) ?? false) ||
                                           (e.Category?.ToLower().Contains(t) ?? false) ||
                                           (e.Notes?.ToLower().Contains(t) ?? false) ||
                                           e.Amount.ToString().Contains(t));

        _visible = filtered.ToList();
        MainGrid.ItemsSource = _visible;
        CountLabel.Text = $"{_visible.Count} (₱{_visible.Sum(x => x.Amount):N2})";
        if (EmptyStatePanel != null)
            EmptyStatePanel.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionState();
    }

    // ══ SELECTION CONTROLS ════════════════════════════════════════════════════
    private void OnHeaderSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            var target = cb.IsChecked == true;
            foreach (var r in _visible) r.IsSelected = target;
            UpdateSelectionState();
        }
    }

    private void OnRowCheckBoxClick(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _visible) r.IsSelected = true;
        UpdateSelectionState();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _all) r.IsSelected = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var count = _visible.Count(r => r.IsSelected);
        if (SelectedCountLabel != null)
            SelectedCountLabel.Text = $"{count} selected";
        if (BulkSelectionPanel != null)
            BulkSelectionPanel.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (BulkDeleteBtn != null)
            BulkDeleteBtn.IsEnabled = count > 0;
    }

    private void OnBulkDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = _visible.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var res = MessageBox.Show(
            $"Are you sure you want to delete {selected.Count} expense(s)?\n\nThis cannot be undone.",
            "Confirm Bulk Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        int deleted = 0;
        foreach (var vm in selected)
        {
            var exp = _db.Expenses.Find(vm.Id);
            if (exp != null) { _db.Expenses.Remove(exp); deleted++; }
        }
        _db.SaveChanges();
        MessageBox.Show($"Deleted {deleted} expense(s).", "Bulk Delete", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadData();
    }

    private void OnSearchChanged(object s, TextChangedEventArgs e)
    {
        ClearSearchBtn.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter();
    }

    private void OnClearSearch(object s, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void OnAdd(object s, RoutedEventArgs e)
    {
        var dlg = new ExpenseDialog(_db);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnViewExpense(object s, RoutedEventArgs e)
    {
        if (s is not Button btn) return;
        Expense? exp = null;
        if (btn.Tag is int id)
            exp = _db.Expenses.Find(id);
        else if (btn.DataContext is ExpenseRowVm vm)
            exp = _db.Expenses.Find(vm.Id);
        if (exp is null) return;

        var msg = $"Description: {exp.Description}\n" +
                  $"Category: {exp.Category}\n" +
                  $"Amount: ₱{exp.Amount:N2}\n" +
                  $"Date: {exp.ExpenseDate:MMMM dd, yyyy}\n" +
                  $"Notes: {(string.IsNullOrWhiteSpace(exp.Notes) ? "None" : exp.Notes)}\n\n" +
                  $"Would you like to edit this expense?";

        var res = MessageBox.Show(msg, "Expense Details", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (res == MessageBoxResult.Yes)
        {
            var dlg = new ExpenseDialog(_db, exp);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true) LoadData();
        }
    }

    private void OnMoreMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private Expense? GetExpenseFromSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Parent is ContextMenu cm && cm.PlacementTarget is Button btn && btn.Tag is int id)
                return _db.Expenses.Find(id);
            if (fe.Tag is int directId)
                return _db.Expenses.Find(directId);
            // Also support DataContext binding
            if (fe.DataContext is ExpenseRowVm vm)
                return _db.Expenses.Find(vm.Id);
        }
        return null;
    }

    private void OnEditFromMenu(object sender, RoutedEventArgs e)
    {
        var exp = GetExpenseFromSender(sender);
        if (exp is null) return;

        var dlg = new ExpenseDialog(_db, exp);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) LoadData();
    }

    private void OnDeleteFromMenu(object sender, RoutedEventArgs e)
    {
        var exp = GetExpenseFromSender(sender);
        if (exp is null) return;

        var res = MessageBox.Show($"Are you sure you want to delete expense '{exp.Description}' (₱{exp.Amount:N2})?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            _db.Expenses.Remove(exp);
            _db.SaveChanges();
            LoadData();
        }
    }
}
