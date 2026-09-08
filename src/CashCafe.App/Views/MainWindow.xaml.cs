using System;
using System.Linq;

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CashCafe.App.Core.ViewModels;

namespace CashCafe.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _idleCheck;

    public MainWindow()
    {
        InitializeComponent();

        // The admin panel locks itself when it has been left alone. Checked here rather than
        // in the view model because it is a property of the window being open, not of the data.
        _idleCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _idleCheck.Tick += (_, _) =>
        {
            if (App.Admin.HasGoneIdle()) CloseAdmin();
        };
        _idleCheck.Start();
    }

    private CafeViewModel Till => (CafeViewModel)DataContext;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F4:
                OpenDeposit();
                e.Handled = true;
                break;

            case Key.F5:
                Till.ReloadFromDatabase();
                e.Handled = true;
                break;

            case Key.F9:
                OpenAdmin();
                e.Handled = true;
                break;

            case Key.F11:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                WindowStyle = WindowStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : WindowStyle.None;
                e.Handled = true;
                break;

            case Key.Escape when Cafe.Visibility == Visibility.Visible:
                Till.Clear();
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    private void OpenDeposit()
    {
        var dialog = new DepositDialog { Owner = this };
        if (dialog.ShowDialog() == true) Till.ReloadFromDatabase();
    }

    /// <summary>
    /// Opens the admin panel behind the PIN. The till keeps working whatever happens here —
    /// a lockout must never stop the café serving.
    /// </summary>
    private void OpenAdmin()
    {
        if (!PinDialog.Ask(App.Admin, this)) return;

        var admin = new AdminWindow(new AdminViewModel(App.Cafe, App.Imports, App.Backups, App.Admin))
        {
            Owner = this,
        };

        admin.ShowDialog();
        CloseAdmin();
    }

    private void CloseAdmin()
    {
        App.Admin.Close();
        Till.ReloadFromDatabase();
    }
}
