using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CashCafe.App.Core.ViewModels;
using CashCafe.Domain;
using CashCafe.Excel;
using Microsoft.Win32;

namespace CashCafe.App.Views;

public partial class AdminWindow : Window
{
    private AdminViewModel Admin => (AdminViewModel)DataContext;

    public AdminWindow(AdminViewModel admin)
    {
        InitializeComponent();
        DataContext = admin;
    }

    private void AddStudent_Click(object sender, RoutedEventArgs e)
    {
        new AddStudentDialog(Admin) { Owner = this }.ShowDialog();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        new AddItemDialog(Admin) { Owner = this }.ShowDialog();
    }

    private void ItemsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsGrid.SelectedItem is Item item)
            new EditItemDialog(Admin, item) { Owner = this }.ShowDialog();
    }

    private void StudentsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (StudentsGrid.SelectedItem is Student student)
            new EditStudentDialog(Admin, student) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// Reads the file, plans the import, and shows the preview. Nothing is written to the
    /// café database until the preview window's own Import button is pressed.
    /// </summary>
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the Excel sheet to import",
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            var preview = Admin.PrepareImport(stream, Path.GetFileName(dialog.FileName));

            var window = new ImportPreviewWindow(Admin, dialog.FileName, preview) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"That file could not be read.\n\n{ex.Message}\n\n" +
                "Check it is the .xlsx file itself and not open in Excel at the same time.",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Reads a menu spreadsheet (Name and Price columns, at least) and shows what it would
    /// change. Nothing is written to the menu until the preview window's own Import button
    /// is pressed.
    /// </summary>
    private void ImportMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the Excel sheet with the menu",
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            var preview = Admin.PrepareItemImport(stream, Path.GetFileName(dialog.FileName));

            if (preview is null)
            {
                MessageBox.Show(Admin.Message, "Cash Café", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            new ItemImportPreviewWindow(Admin, preview) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"That file could not be read.\n\n{ex.Message}\n\n" +
                "Check it is the .xlsx file itself and not open in Excel at the same time.",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Writes the current menu (name, category, price, on sale) to a workbook.</summary>
    private void ExportMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export menu to Excel",
            Filter = "Excel files (*.xlsx)|*.xlsx",
            FileName = $"cashcafe-meny-{DateTime.Now:yyyy-MM-dd}.xlsx",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            using var stream = File.Create(dialog.FileName);
            new WorkbookExporter().WriteMenu(Admin.Items, stream);

            MessageBox.Show($"Exported to {dialog.FileName}.",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The export could not be written.\n\n{ex.Message}",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Writes every student, item, transaction and audit entry to one workbook.</summary>
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export to Excel",
            Filter = "Excel files (*.xlsx)|*.xlsx",
            FileName = $"cashcafe-export-{DateTime.Now:yyyy-MM-dd}.xlsx",
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var data = Admin.BuildExport();
            using var stream = File.Create(dialog.FileName);
            new WorkbookExporter().WriteEverything(data, stream);

            MessageBox.Show($"Exported to {dialog.FileName}.",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The export could not be written.\n\n{ex.Message}",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
