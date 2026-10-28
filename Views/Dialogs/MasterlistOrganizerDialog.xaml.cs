using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class MasterlistOrganizerDialog : Window
{
    private readonly AppDbContext _db;
    private readonly User? _user;
    private List<MasterlistRowVm> _allMasterlists = [];
    private string _activeStatusFilter = "All";

    public MasterlistRowVm? SelectedMasterlist { get; private set; }

    public MasterlistOrganizerDialog(AppDbContext? db = null, User? user = null)
    {
        InitializeComponent();
        _db = db ?? new AppDbContext();
        _user = user;

        LoadMasterlists();
        HighlightFilterTab("All");
    }

    private void LoadMasterlists()
    {
        try
        {
            _allMasterlists = MasterlistService.GetAllRows(_db);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load masterlists: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (SearchBox == null || MasterlistsList == null) return;

        var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
        var filtered = _allMasterlists.AsEnumerable();

        if (_activeStatusFilter != "All")
        {
            filtered = filtered.Where(m => string.Equals(m.Status, _activeStatusFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(m =>
                (m.Name?.ToLowerInvariant().Contains(query) ?? false) ||
                (m.ControlNumber?.ToLowerInvariant().Contains(query) ?? false) ||
                m.MonthYearDisplay.ToLowerInvariant().Contains(query) ||
                m.DateRangeDisplay.ToLowerInvariant().Contains(query) ||
                m.Status.ToLowerInvariant().Contains(query));
        }

        var list = filtered.ToList();
        MasterlistsList.ItemsSource = list;

        EmptyStatePanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountSummaryText.Text = $"{list.Count} masterlist{(list.Count == 1 ? "" : "s")} found";
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeStatusFilter = tag;
            HighlightFilterTab(tag);
            ApplyFilter();
        }
    }

    private void HighlightFilterTab(string activeTag)
    {
        var buttons = new[] { FilterBtnAll, FilterBtnActive, FilterBtnClosed, FilterBtnArchived };
        foreach (var btn in buttons)
        {
            if (btn == null) continue;
            var isTarget = string.Equals(btn.Tag as string, activeTag, StringComparison.OrdinalIgnoreCase);
            NavProps.SetIsActive(btn, isTarget);
            btn.Foreground = isTarget ? Brushes.White : new SolidColorBrush(Color.FromRgb(71, 85, 105));
        }
    }

    private void OnOpenMasterlistClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MasterlistRowVm row)
        {
            SelectedMasterlist = row;
            DialogResult = true;
            Close();
        }
    }

    private void OnDeleteItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MasterlistRowVm row)
        {
            var msg = $"Are you sure you want to delete masterlist \"{row.Name}\" ({row.ControlNumber})?\n\n" +
                      $"• Its {row.OrderCount} order(s) will stay safe and intact (unassigned from this masterlist).\n" +
                      $"• This folder will be permanently removed.\n\n" +
                      "Do you want to proceed with deleting this masterlist?";

            var result = MessageBox.Show(msg, "Delete Masterlist Folder", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    MasterlistService.Delete(_db, row.Id);
                    OrderEventBus.NotifyDataRefreshed();
                    LoadMasterlists();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not delete masterlist: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void OnNewMasterlistClick(object sender, RoutedEventArgs e)
    {
        var dlg = new NewMasterlistDialog(_db, _user)
        {
            Owner = this
        };
        if (dlg.ShowDialog() == true)
        {
            OrderEventBus.NotifyDataRefreshed();
            LoadMasterlists();
        }
    }

    private void OnResetFiltersClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        _activeStatusFilter = "All";
        HighlightFilterTab("All");
        ApplyFilter();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
