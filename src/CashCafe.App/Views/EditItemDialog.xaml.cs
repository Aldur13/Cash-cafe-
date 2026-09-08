using System.Windows;
using CashCafe.App.Core.ViewModels;
using CashCafe.Domain;

namespace CashCafe.App.Views;

public partial class EditItemDialog : Window
{
    private readonly AdminViewModel _admin;
    private readonly Item _item;

    public EditItemDialog(AdminViewModel admin, Item item)
    {
        InitializeComponent();
        _admin = admin;
        _item = item;

        Heading.Text = $"{item.Name} — currently {item.Price}";
        PriceBox.Text = item.Price.ToString();
        AvailableBox.IsChecked = item.IsAvailable;

        Loaded += (_, _) => PriceBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
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
            if (parsed.Value != _item.Price)
            {
                if (string.IsNullOrWhiteSpace(ReasonBox.Text))
                {
                    ShowProblem("A reason is required when the price changes.");
                    return;
                }

                _admin.ChangePrice(_item.Id, parsed.Value, ReasonBox.Text.Trim());
            }

            if (AvailableBox.IsChecked != _item.IsAvailable)
                _admin.SetItemAvailable(_item.Id, AvailableBox.IsChecked == true);
        }
        catch (System.Exception ex)
        {
            ShowProblem($"Could not save that. {ex.Message}");
            return;
        }

        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            $"Remove {_item.Name}?\n\nIf it has never been sold it disappears completely. " +
            "If it has been sold, it is archived instead — hidden from the till, kept in history.",
            "Cash Café", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes) return;

        _admin.RemoveItem(_item.Id);
        DialogResult = true;
    }

    private void ShowProblem(string text)
    {
        Problem.Text = text;
        Problem.Visibility = Visibility.Visible;
    }
}
