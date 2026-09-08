using System.Windows;
using CashCafe.App.Core.ViewModels;

namespace CashCafe.App.Views;

public partial class AdminWindow : Window
{
    public AdminWindow(AdminViewModel admin)
    {
        InitializeComponent();
        DataContext = admin;
    }
}
