using System;
using System.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CashCafe.App.Core.ViewModels;
using CashCafe.Domain;

namespace CashCafe.App.Views;

public partial class CafeView : UserControl
{
    public CafeView()
    {
        InitializeComponent();
        Loaded += (_, _) => StudentBox.Focus();
    }

    private CafeViewModel Till => (CafeViewModel)DataContext;

    /// <summary>
    /// The keyboard flow the counter actually uses: arrows move through the list, Enter
    /// chooses and moves on. Someone serving a queue should never need the mouse.
    /// </summary>
    private void StudentBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when StudentList.Items.Count > 0:
                StudentList.SelectedIndex = Math.Min(StudentList.SelectedIndex + 1, StudentList.Items.Count - 1);
                e.Handled = true;
                break;

            case Key.Up when StudentList.Items.Count > 0:
                StudentList.SelectedIndex = Math.Max(StudentList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;

            case Key.Enter when StudentList.Items.Count > 0:
                var chosen = (StudentList.SelectedItem ?? StudentList.Items[0]) as Student;
                Till.SelectStudent(chosen);
                MoveFocusToFirstItemRow();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// A single letter on an empty row is still a shortcut key — "t" adds the toast without
    /// opening the dropdown at all — because that is faster than clicking through a menu for
    /// the item that sells fifty times a day. Anything else falls through to the combo box's
    /// own behaviour: opening the list, or jumping to the item whose name starts with what
    /// was typed.
    /// </summary>
    private void ItemCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not int lineNo) return;

        if (e.Key is >= Key.A and <= Key.Z && combo.SelectedItem is null && !combo.IsDropDownOpen)
        {
            var item = Till.ItemForShortcut(e.Key.ToString());
            if (item is not null)
            {
                Till.SetItem(lineNo, item);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Back && combo.SelectedItem is null && lineNo == 1)
        {
            StudentBox.Focus();
            StudentBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { Tag: int lineNo } combo) return;
        if (combo.SelectedItem is Item item) Till.SetItem(lineNo, item);
    }

    private void StudentList_Chosen(object sender, MouseButtonEventArgs e)
    {
        if (StudentList.SelectedItem is Student student)
        {
            Till.SelectStudent(student);
            MoveFocusToFirstItemRow();
        }
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int lineNo }) Till.RemoveRow(lineNo);
    }

    /// <summary>
    /// Deposit and Admin live on the till screen, so the buttons hand the work back up to the
    /// window that owns those dialogs rather than opening them from here.
    /// </summary>
    private void Deposit_Click(object sender, RoutedEventArgs e) =>
        RaiseWindowKey(Key.F4);

    private void Admin_Click(object sender, RoutedEventArgs e) =>
        RaiseWindowKey(Key.F9);

    private void RaiseWindowKey(Key key)
    {
        if (Window.GetWindow(this) is not { } window) return;

        var source = PresentationSource.FromVisual(window);
        if (source is null) return;

        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });
    }

    private void MoveFocusToFirstItemRow() =>
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
}
