using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CashCafe.Domain;

namespace CashCafe.App.Views;

/// <summary>
/// Recording money that arrived. On the till screen rather than in the admin panel on
/// purpose: a parent Swishing the café is a counter event, not an administrative one.
///
/// It can only ever add money. Taking money off a balance is a correction, which needs the
/// admin PIN and a written reason.
/// </summary>
public partial class DepositDialog : Window
{
    private Student? _student;

    public DepositDialog()
    {
        InitializeComponent();

        StudentBox.TextChanged += (_, _) =>
        {
            StudentList.ItemsSource = StudentSearch.Find(App.Cafe.ActiveStudents, StudentBox.Text);
            StudentList.SelectedIndex = 0;
        };

        StudentList.SelectionChanged += (_, _) => _student = StudentList.SelectedItem as Student;

        Loaded += (_, _) => StudentBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_student is null)
        {
            ShowProblem("Choose a student first.");
            return;
        }

        var parsed = MoneyParser.Parse(AmountBox.Text);
        if (!parsed.Success)
        {
            ShowProblem($"That amount could not be read. {parsed.Error}");
            return;
        }

        if (parsed.Value <= Money.Zero)
        {
            ShowProblem("A deposit has to be more than 0 kr. To take money off a balance, " +
                        "an administrator makes a correction with a reason.");
            return;
        }

        var method = ((MethodBox.SelectedItem as ComboBoxItem)?.Content as string) switch
        {
            "Cash" => DepositMethod.Cash,
            "Bank transfer" => DepositMethod.BankTransfer,
            "Other" => DepositMethod.Other,
            _ => DepositMethod.Swish,
        };

        var result = App.Cafe.Ledger.Deposit(
            _student.Id, parsed.Value, method,
            string.IsNullOrWhiteSpace(ReferenceBox.Text) ? null : ReferenceBox.Text.Trim(),
            "Admin", App.Cafe.SessionId);

        if (!result.Succeeded)
        {
            ShowProblem(result.Message);
            return;
        }

        MessageBox.Show(result.Message, "Cash Café", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void ShowProblem(string text)
    {
        Problem.Text = text;
        Problem.Visibility = Visibility.Visible;
    }
}
