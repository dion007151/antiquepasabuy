using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class RestoTableDialog : Window
{
    private readonly AppDbContext _db;
    private readonly RestoTable? _existing;

    private static readonly string[] Sections = { "Main Dining", "Bar Area", "VIP Room", "Patio", "Rooftop", "Private", "Counter" };

    public RestoTableDialog(AppDbContext db, RestoTable? existing = null)
    {
        InitializeComponent();
        _db = db;
        _existing = existing;

        SectionCombo.ItemsSource = Sections;
        StatusCombo.ItemsSource = Enum.GetValues(typeof(RestoTableStatus));
        StatusCombo.SelectedItem = RestoTableStatus.Available;

        if (existing is not null)
        {
            TitleLbl.Text = "Edit Table";
            NumberBox.Text = existing.TableNumber;
            NameBox.Text = existing.Name;
            // Use SelectedItem for ItemsSource-bound combos; fall back to Text if not in list
            SectionCombo.SelectedItem = existing.Section;
            if (SectionCombo.SelectedItem == null)
                SectionCombo.Text = existing.Section;
            CapacityBox.Text = existing.Capacity.ToString();
            StatusCombo.SelectedItem = existing.Status;
        }
        else
        {
            SectionCombo.SelectedIndex = 0; // "Main Dining"
            // Auto-generate next table number safely
            try
            {
                var count = _db.RestoTables.Count() + 1;
                NumberBox.Text = $"T-{count:D2}";
            }
            catch
            {
                NumberBox.Text = "T-01";
            }
        }
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";
        if (string.IsNullOrWhiteSpace(NumberBox.Text))
        {
            ErrLbl.Text = "Table number is required.";
            NumberBox.Focus();
            return;
        }

        if (!int.TryParse(CapacityBox.Text.Trim(), out var capacity) || capacity <= 0)
        {
            capacity = 4;
        }

        var section = string.IsNullOrWhiteSpace(SectionCombo.Text) ? "Main Dining" : SectionCombo.Text.Trim();
        var status = StatusCombo.SelectedItem is RestoTableStatus st ? st : RestoTableStatus.Available;

        if (_existing is null)
        {
            var newTable = new RestoTable
            {
                TableNumber = NumberBox.Text.Trim(),
                Name = NameBox.Text?.Trim() ?? "",
                Capacity = capacity,
                Section = section,
                Status = status,
                UpdatedAt = DateTime.Now
            };
            _db.RestoTables.Add(newTable);
        }
        else
        {
            // Ensure the entity is tracked before modifying it
            if (_db.Entry(_existing).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
                _db.RestoTables.Attach(_existing);

            _existing.TableNumber = NumberBox.Text.Trim();
            _existing.Name = NameBox.Text?.Trim() ?? "";
            _existing.Capacity = capacity;
            _existing.Section = section;
            _existing.Status = status;
            _existing.UpdatedAt = DateTime.Now;
        }

        _db.SaveChanges();
        DialogResult = true;
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}
