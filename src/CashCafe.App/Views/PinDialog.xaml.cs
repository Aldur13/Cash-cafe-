using System;
using System.Windows;
using CashCafe.App.Core.Services;

namespace CashCafe.App.Views;

public partial class PinDialog : Window
{
    private readonly AdminSession _session;
    private readonly bool _isFirstRun;

    private PinDialog(AdminSession session, bool isFirstRun)
    {
        InitializeComponent();

        _session = session;
        _isFirstRun = isFirstRun;

        if (isFirstRun)
        {
            Heading.Text = "Choose an admin PIN";
            Explanation.Text =
                "4–12 digits. It is stored only as a hash, so it cannot be read back out of the " +
                "database or a backup. Write it down and keep it where the school keeps such things — " +
                "losing it means a reset that needs administrator rights on this computer.";
        }

        Loaded += (_, _) => PinBox.Focus();
    }

    /// <summary>Asks for the PIN to open the admin panel. Returns false if it was not given.</summary>
    public static bool Ask(AdminSession session, Window owner)
    {
        if (session.IsLockedOut)
        {
            MessageBox.Show(
                $"Too many wrong PINs. Admin is locked for another {session.LockoutRemaining.Minutes + 1} minute(s).\n\n" +
                "The till is unaffected and the café can keep selling.",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return new PinDialog(session, isFirstRun: false) { Owner = owner }.ShowDialog() == true;
    }

    public static bool SetUpFirstPin(AdminSession session) =>
        new PinDialog(session, isFirstRun: true).ShowDialog() == true;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var pin = PinBox.Password;

        if (_isFirstRun)
        {
            try
            {
                session_SetPin(pin);
                DialogResult = true;
            }
            catch (ArgumentException ex)
            {
                ShowProblem(ex.Message);
            }

            return;
        }

        switch (_session.Enter(pin))
        {
            case PinResult.Accepted:
                DialogResult = true;
                break;

            case PinResult.Wrong:
                ShowProblem("That PIN is not right.");
                PinBox.Clear();
                PinBox.Focus();
                break;

            case PinResult.LockedOut:
                ShowProblem("Too many wrong PINs. Admin is locked for five minutes — the till still works.");
                OkButton.IsEnabled = false;
                break;

            case PinResult.NotSetUp:
                ShowProblem("No admin PIN has been set on this computer yet.");
                break;
        }
    }

    private void session_SetPin(string pin) => _session.SetPin(pin, "setup");

    private void ShowProblem(string text)
    {
        Problem.Text = text;
        Problem.Visibility = Visibility.Visible;
    }
}
