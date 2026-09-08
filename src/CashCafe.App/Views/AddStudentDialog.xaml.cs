using System.Windows;
using CashCafe.App.Core.ViewModels;

namespace CashCafe.App.Views;

/// <summary>
/// Adds one student by hand. The bulk way in is Excel import — this is for the student who
/// joins mid-term, after the sheet was last imported.
/// </summary>
public partial class AddStudentDialog : Window
{
    private readonly AdminViewModel _admin;

    public AddStudentDialog(AdminViewModel admin)
    {
        InitializeComponent();
        _admin = admin;
        Loaded += (_, _) => FirstNameBox.Focus();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var first = FirstNameBox.Text.Trim();
        var last = LastNameBox.Text.Trim();

        if (first.Length == 0)
        {
            ShowProblem("First name is required.");
            return;
        }

        try
        {
            _admin.AddStudent(first, last, string.IsNullOrWhiteSpace(ClassBox.Text) ? null : ClassBox.Text.Trim());
        }
        catch (System.Exception ex)
        {
            ShowProblem($"Could not add the student. {ex.Message}");
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
