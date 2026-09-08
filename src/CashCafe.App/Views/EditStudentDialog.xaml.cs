using System.Windows;
using CashCafe.App.Core.ViewModels;
using CashCafe.Domain;

namespace CashCafe.App.Views;

public partial class EditStudentDialog : Window
{
    private readonly AdminViewModel _admin;
    private readonly Student _student;

    public EditStudentDialog(AdminViewModel admin, Student student)
    {
        InitializeComponent();
        _admin = admin;
        _student = student;

        Heading.Text = $"{student.DisplayName} — balance {student.Balance}";
        ActiveBox.IsChecked = student.IsActive;
        LimitBox.Text = student.CreditLimit?.ToString() ?? string.Empty;

        Loaded += (_, _) => LimitBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Money? newLimit = null;

        if (!string.IsNullOrWhiteSpace(LimitBox.Text))
        {
            var parsed = MoneyParser.Parse(LimitBox.Text);
            if (!parsed.Success)
            {
                ShowProblem($"That limit could not be read. {parsed.Error}");
                return;
            }

            newLimit = parsed.Value;
        }

        try
        {
            if (ActiveBox.IsChecked != _student.IsActive)
                _admin.SetStudentActive(_student.Id, ActiveBox.IsChecked == true);

            if (newLimit != _student.CreditLimit)
            {
                if (string.IsNullOrWhiteSpace(ReasonBox.Text))
                {
                    ShowProblem("A reason is required when the limit changes.");
                    return;
                }

                _admin.SetCreditLimit(_student.Id, newLimit, ReasonBox.Text.Trim());
            }
        }
        catch (System.Exception ex)
        {
            ShowProblem($"Could not save that. {ex.Message}");
            return;
        }

        DialogResult = true;
    }

    private void ShowProblem(string text)
    {
        Problem.Text = text;
        Problem.Visibility = Visibility.Visible;
    }
}
