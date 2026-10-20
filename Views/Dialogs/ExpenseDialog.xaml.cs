using System.Windows;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class ExpenseDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Expense? _expense;

    public ExpenseDialog(AppDbContext db, Expense? expense = null)
    {
        InitializeComponent();
        _db = db;
        _expense = expense;

        if (_expense != null)
        {
            TitleLabel.Text = "Edit Expense";
            Title = "Edit Expense";
            DescBox.Text = _expense.Description;
            AmtBox.Text = _expense.Amount.ToString("F2");
            CatBox.Text = _expense.Category;
            NotesBox.Text = _expense.Notes ?? "";
        }
    }

    private void OnSave(object s, RoutedEventArgs e)
    {
        ErrLbl.Text = "";
        var desc = DescBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(desc))
        {
            ErrLbl.Text = "Description is required.";
            return;
        }

        if (!decimal.TryParse(AmtBox.Text, out var amt) || amt <= 0)
        {
            ErrLbl.Text = "Please enter a valid positive amount.";
            return;
        }

        var cat = string.IsNullOrWhiteSpace(CatBox.Text) ? "General" : CatBox.Text.Trim();
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        if (_expense == null)
        {
            _db.Expenses.Add(new Expense
            {
                Description = desc,
                Amount = amt,
                Category = cat,
                Notes = notes,
                ExpenseDate = DateOnly.FromDateTime(DateTime.Today)
            });
        }
        else
        {
            _expense.Description = desc;
            _expense.Amount = amt;
            _expense.Category = cat;
            _expense.Notes = notes;
        }

        _db.SaveChanges();
        DialogResult = true;
        Close();
    }

    private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}
