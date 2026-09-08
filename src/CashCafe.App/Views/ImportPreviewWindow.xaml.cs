using System.Windows;
using CashCafe.App.Core.ViewModels;
using CashCafe.Domain.Import;

namespace CashCafe.App.Views;

/// <summary>
/// Nothing is written to the database while this window is open. Import only happens when
/// <see cref="Import_Click"/> runs, and only if there are zero errors in the preview.
/// </summary>
public partial class ImportPreviewWindow : Window
{
    private readonly AdminViewModel _admin;
    private readonly string _sourcePath;
    private readonly ImportPreview _preview;

    public bool Imported { get; private set; }

    public ImportPreviewWindow(AdminViewModel admin, string sourcePath, ImportPreview preview)
    {
        InitializeComponent();

        _admin = admin;
        _sourcePath = sourcePath;
        _preview = preview;

        RowsGrid.ItemsSource = preview.Rows;
        RowsReadText.Text = preview.RowsRead.ToString();
        WillCreateText.Text = preview.WillCreate.ToString();
        WillAdjustText.Text = preview.WillAdjust.ToString();
        ErrorsText.Text = preview.Errors.ToString();
        TotalChangeText.Text = preview.TotalChange.ToString();

        ImportButton.IsEnabled = preview.CanImport;
        if (!preview.CanImport)
            ImportButton.ToolTip = "Fix the rows marked Error before this can be imported.";
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            $"Import {_preview.RowsRead} row(s) from {_preview.FileName}?\n\n" +
            $"{_preview.WillCreate} new student(s), {_preview.WillAdjust} balance(s) adjusted.\n\n" +
            "A backup is taken automatically before this, and the whole import can be undone " +
            "afterwards from the history if something looks wrong.",
            "Cash Café", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes) return;

        try
        {
            App.Backups.Create(App.LocalBackups, reason: "pre-import");

            var archivedPath = CafePaths.ArchiveImport(_sourcePath);
            if (!_admin.ConfirmImport(archivedPath))
            {
                MessageBox.Show(_admin.Message, "Cash Café", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"The import failed and nothing was changed.\n\n{ex.Message}",
                "Cash Café", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Imported = true;
        MessageBox.Show(_admin.Message, "Cash Café", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }
}
