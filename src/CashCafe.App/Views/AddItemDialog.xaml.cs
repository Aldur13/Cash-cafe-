using System.Windows;
using CashCafe.App.Core.ViewModels;
using CashCafe.Domain;

namespace CashCafe.App.Views;

public partial class AddItemDialog : Window
{
    private readonly AdminViewModel _admin;

    public AddItemDialog(AdminViewModel admin)
    {
        InitializeComponent();
        _admin = admin;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowProblem("A name is required.");
            return;
        }

        var parsed = MoneyParser.Parse(PriceBox.Text);
        if (!parsed.Success)
        {
            ShowProblem($"That price could not be read. {parsed.Error}");
            return;
        }

        if (parsed.Value < Money.Zero)
        {
            ShowProblem("The price cannot be negative.");
            return;
        }

        try
        {
            _admin.AddItem(
                name,
                parsed.Value,
                string.IsNullOrWhiteSpace(CategoryBox.Text) ? null : CategoryBox.Text.Trim());
        }
        catch (System.Exception ex)
        {
            ShowProblem($"Could not add the item. {ex.Message}");
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
