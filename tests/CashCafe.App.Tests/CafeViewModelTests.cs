using CashCafe.Domain;
using CashCafe.Domain.Rules;

namespace CashCafe.App.Tests;

public class CafeViewModelTests
{
    [Fact]
    public void The_till_opens_with_one_empty_row_and_nothing_selected()
    {
        using var fixture = new TillFixture();
        var till = fixture.NewTill();

        till.Rows.Should().ContainSingle();
        till.Student.Should().BeNull();
        till.CanExecute.Should().BeFalse();
        till.ExecuteBlockedReason.Should().Be("Choose a student");
    }

    [Fact]
    public void Typing_filters_the_student_list_from_the_first_keystroke()
    {
        using var fixture = new TillFixture();
        fixture.AddStudent("Carl", "Jacobs", 50);
        fixture.AddStudent("Astrid", "Lindqvist", 20);
        var till = fixture.NewTill();

        till.StudentQuery = "car";

        till.StudentMatches.Should().ContainSingle();
        till.StudentMatches[0].DisplayName.Should().Be("Carl Jacobs");
    }

    [Fact]
    public void Choosing_a_student_shows_what_they_have_and_what_they_can_spend()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50, "9B");
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));

        till.BalanceNow.Should().Be(Money.FromKronor(50));
        till.CanSpend.Should().Be(Money.FromKronor(60), "the floor is -10 kr");
        till.StudentIsInDebt.Should().BeFalse();
    }

    [Fact]
    public void A_new_empty_row_opens_as_each_one_is_filled()
    {
        using var fixture = new TillFixture();
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        till.Rows.Should().HaveCount(1);

        till.SetItem(1, toast);

        till.Rows.Should().HaveCount(2);
        till.Rows[1].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Rows_stop_at_ten()
    {
        using var fixture = new TillFixture();
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        for (var line = 1; line <= 10; line++) till.SetItem(line, toast);

        till.Rows.Should().HaveCount(10);
        till.ItemCount.Should().Be(10);
    }

    [Fact]
    public void The_price_appears_as_soon_as_an_item_is_chosen()
    {
        using var fixture = new TillFixture();
        var juice = fixture.AddItem("Juice", 15);
        var till = fixture.NewTill();

        till.SetItem(1, juice, quantity: 2);

        till.Rows[0].UnitPrice.Should().Be(Money.FromKronor(15));
        till.Rows[0].LineTotal.Should().Be(Money.FromKronor(30));
        till.Total.Should().Be(Money.FromKronor(30));
    }

    [Fact]
    public void The_new_balance_is_shown_before_anything_is_committed()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);
        var juice = fixture.AddItem("Juice", 15);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);
        till.SetItem(2, juice, quantity: 2);

        till.Total.Should().Be(Money.FromKronor(40));
        till.BalanceAfter.Should().Be(Money.FromKronor(10));
        till.CanExecute.Should().BeTrue();
        fixture.Balance(carl).Should().Be(Money.FromKronor(50), "nothing is written until Execute");
    }

    [Fact]
    public void Execute_commits_the_purchase_and_clears_the_screen()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);

        till.Execute().Should().BeTrue();

        fixture.Balance(carl).Should().Be(Money.FromKronor(40));
        till.Student.Should().BeNull("the till is ready for the next student");
        till.Rows.Should().ContainSingle();
        till.Message.Should().Contain("Carl Jacobs").And.Contain("40,00 kr");
        till.MessageIsError.Should().BeFalse();
    }

    [Fact]
    public void The_floor_blocks_execute_and_says_exactly_why()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var cake = fixture.AddItem("Tårta", 72.50m);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, cake);

        till.CanExecute.Should().BeFalse();
        till.ExecuteBlockedReason.Should().Contain("Carl Jacobs")
            .And.Contain("60,00 kr")
            .And.Contain("12,50 kr too much");

        till.Execute().Should().BeFalse();
        fixture.Balance(carl).Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void A_purchase_landing_exactly_on_the_floor_goes_through()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var big = fixture.AddItem("Stor beställning", 60);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, big);

        till.CanExecute.Should().BeTrue();
        till.Execute().Should().BeTrue();
        fixture.Balance(carl).Should().Be(Money.FromKronor(-10));
    }

    [Fact]
    public void A_price_changed_in_the_admin_panel_stops_the_purchase_and_refreshes_the_till()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);

        // Another window changes the price after the row is on screen.
        fixture.Items.ChangePrice(toast.Id, Money.FromKronor(12), "new supplier", "admin");

        till.Execute().Should().BeFalse();
        till.MessageIsError.Should().BeTrue();
        till.Message.Should().Contain("12,00 kr");
        fixture.Balance(carl).Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void Undo_reverses_the_last_purchase_and_leaves_both_rows_in_the_history()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);
        till.Execute();

        till.RecentPurchases.Should().ContainSingle();
        till.UndoLast().Should().BeTrue();

        fixture.Balance(carl).Should().Be(Money.FromKronor(50));
        till.RecentPurchases.Should().BeEmpty();
        fixture.Ledger.History(carl).Should().HaveCount(3, "deposit, purchase and reversal");
    }

    [Fact]
    public void Undo_with_nothing_to_undo_says_so_rather_than_doing_anything()
    {
        using var fixture = new TillFixture();
        var till = fixture.NewTill();

        till.UndoLast().Should().BeFalse();
        till.Message.Should().Contain("nothing to undo");
    }

    [Fact]
    public void A_deactivated_student_cannot_be_charged_from_the_till()
    {
        using var fixture = new TillFixture();
        var gone = fixture.AddStudent("Gone", "Student", 50);
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(gone));
        till.SetItem(1, toast);
        fixture.Students.SetActive(gone, false, "admin");

        till.Execute().Should().BeFalse();
        fixture.Balance(gone).Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void A_shortcut_key_finds_its_item()
    {
        using var fixture = new TillFixture();
        fixture.AddItem("Toast", 10, shortcut: "t");
        var till = fixture.NewTill();

        till.ItemForShortcut("T")!.Name.Should().Be("Toast");
        till.ItemForShortcut("z").Should().BeNull();
    }

    [Fact]
    public void An_item_marked_sold_out_disappears_from_the_till()
    {
        using var fixture = new TillFixture();
        var toast = fixture.AddItem("Toast", 10);
        fixture.Items.SetAvailable(toast.Id, false, "admin");

        var till = fixture.NewTill();
        till.SearchItems("toast");

        till.ItemMatches.Should().BeEmpty();
    }

    [Fact]
    public void A_custom_amount_can_be_charged_for_something_with_no_fixed_price()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetCustomAmount(1, "Beställd tårta", Money.FromKronor(35));

        till.Total.Should().Be(Money.FromKronor(35));
        till.Execute().Should().BeTrue();

        var line = fixture.Ledger.History(carl).First(t => t.Type == TransactionType.Purchase).Lines[0];
        line.IsCustom.Should().BeTrue("custom amounts are flagged so they can be reviewed");
        line.ItemName.Should().Be("Beställd tårta");
    }

    [Fact]
    public void Clearing_throws_the_whole_purchase_away()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);
        till.Clear();

        till.Student.Should().BeNull();
        till.Total.Should().Be(Money.Zero);
        till.Rows.Should().ContainSingle();
    }

    [Fact]
    public void A_large_purchase_is_flagged_for_a_second_look()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 1000);
        var till = fixture.NewTill();

        till.SelectStudent(fixture.Students.Find(carl));
        till.SetCustomAmount(1, "Klassfika", Money.FromKronor(600));

        till.NeedsLargePurchaseConfirmation.Should().BeTrue();
    }

    [Fact]
    public void A_student_in_debt_is_marked_and_can_still_spend_up_to_the_floor()
    {
        using var fixture = new TillFixture();
        var omar = fixture.AddStudent("Omar", "Haddad");
        var toast = fixture.AddItem("Toast", 10);

        var first = fixture.NewTill();
        first.SelectStudent(fixture.Students.Find(omar));
        first.SetItem(1, toast);
        first.Execute().Should().BeTrue("0 kr minus a 10 kr toast lands exactly on the floor");

        var second = fixture.NewTill();
        second.SelectStudent(fixture.Students.Find(omar));

        second.StudentIsInDebt.Should().BeTrue();
        second.CanSpend.Should().Be(Money.Zero);

        second.SetItem(1, toast);
        second.CanExecute.Should().BeFalse();
    }

    [Fact]
    public void The_item_dropdown_lists_everything_on_sale()
    {
        using var fixture = new TillFixture();
        fixture.AddItem("Toast", 10);
        fixture.AddItem("Juice", 15);
        var till = fixture.NewTill();

        till.AvailableItems.Should().HaveCount(2);
        till.AvailableItems.Should().Contain(i => i.Name == "Toast");
        till.AvailableItems.Should().Contain(i => i.Name == "Juice");
    }

    [Fact]
    public void An_item_taken_off_sale_disappears_from_the_dropdown_after_a_reload()
    {
        using var fixture = new TillFixture();
        var toast = fixture.AddItem("Toast", 10);
        var till = fixture.NewTill();

        till.AvailableItems.Should().ContainSingle();

        fixture.Items.SetAvailable(toast.Id, available: false, "test");
        till.ReloadFromDatabase();

        till.AvailableItems.Should().BeEmpty();
    }
}
