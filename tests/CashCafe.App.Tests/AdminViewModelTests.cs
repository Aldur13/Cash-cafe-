using CashCafe.Domain;
using CashCafe.Excel;

namespace CashCafe.App.Tests;

public class AdminViewModelTests
{
    [Fact]
    public void Items_can_be_added_and_appear_at_the_till()
    {
        using var fixture = new TillFixture();

        fixture.Admin.AddItem("Toast", Money.FromKronor(10), "Mat");

        fixture.Admin.Items.Should().ContainSingle(i => i.Name == "Toast");

        var till = fixture.NewTill();
        till.SearchItems("toast");
        till.ItemMatches.Should().ContainSingle();
    }

    [Fact]
    public void Changing_a_price_never_rewrites_what_was_already_sold()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 100);
        var toast = fixture.AddItem("Toast", 10);

        var till = fixture.NewTill();
        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);
        till.Execute();

        fixture.Admin.ChangePrice(toast.Id, Money.FromKronor(20), "new supplier");

        var sold = fixture.Ledger.History(carl).First(t => t.Type == TransactionType.Purchase).Lines[0];
        sold.UnitPrice.Should().Be(Money.FromKronor(10), "history keeps the price that was charged");
        fixture.Items.Find(toast.Id)!.Price.Should().Be(Money.FromKronor(20));
        fixture.Admin.Message.Should().Contain("keep the price they were sold at");
    }

    [Fact]
    public void An_item_that_has_been_sold_is_archived_rather_than_deleted()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 100);
        var toast = fixture.AddItem("Toast", 10);

        var till = fixture.NewTill();
        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);
        till.Execute();

        fixture.Admin.RemoveItem(toast.Id);

        fixture.Admin.Message.Should().Contain("Archived");
        fixture.Items.Find(toast.Id)!.IsArchived.Should().BeTrue();
        fixture.Ledger.History(carl).First(t => t.Type == TransactionType.Purchase)
            .Lines[0].ItemName.Should().Be("Toast", "the sale still says what was bought");
    }

    [Fact]
    public void An_item_never_sold_is_deleted_outright()
    {
        using var fixture = new TillFixture();
        var unused = fixture.AddItem("Aldrig såld", 5);

        fixture.Admin.RemoveItem(unused.Id);

        fixture.Admin.Message.Should().Contain("never been sold");
        fixture.Items.Find(unused.Id).Should().BeNull();
    }

    [Fact]
    public void A_correction_needs_a_reason_and_shows_up_in_the_history()
    {
        using var fixture = new TillFixture();
        var omar = fixture.AddStudent("Omar", "Haddad", 10);

        fixture.Admin.Correct(omar, Money.FromKronor(7), "  ").Should().BeFalse();
        fixture.Balance(omar).Should().Be(Money.FromKronor(10));

        fixture.Admin.Correct(omar, Money.FromKronor(7), "Excel-avrundning, överenskommet med Omar").Should().BeTrue();

        fixture.Balance(omar).Should().Be(Money.FromKronor(17));
        fixture.Ledger.History(omar).Should().Contain(t =>
            t.Type == TransactionType.Adjustment && t.Reason!.Contains("Excel-avrundning"));
    }

    [Fact]
    public void An_admin_can_reverse_any_transaction_with_no_time_limit()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);

        var purchase = fixture.Ledger.ExecutePurchase(carl,
            new[] { new Domain.Rules.BasketLine { LineNo = 1, ItemId = toast.Id, ItemName = "Toast", UnitPrice = Money.FromKronor(10) } },
            fixture.Context.Settings, "Café", "s",
            occurredUtc: DateTimeOffset.UtcNow.AddDays(-3));

        fixture.Admin.Reverse(purchase.TransactionId, "Fel elev, upptäckt av föräldern").Should().BeTrue();

        fixture.Balance(carl).Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void A_per_student_limit_can_be_tightened_or_loosened()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 5);
        var toast = fixture.AddItem("Toast", 10);

        fixture.Admin.SetCreditLimit(carl, Money.Zero, "kontant endast, enligt vårdnadshavare");

        var till = fixture.NewTill();
        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);

        till.CanExecute.Should().BeFalse("this student may not go below zero at all");
    }

    [Fact]
    public void The_cafe_floor_can_be_changed_but_never_made_positive()
    {
        using var fixture = new TillFixture();

        fixture.Admin.SetMinimumBalance(Money.FromKronor(10)).Should().BeFalse();
        fixture.Admin.MessageIsError.Should().BeTrue();
        fixture.Context.Settings.MinimumBalance.Should().Be(Money.FromKronor(-10));

        fixture.Admin.SetMinimumBalance(Money.FromKronor(-20)).Should().BeTrue();
        fixture.Context.Settings.MinimumBalance.Should().Be(Money.FromKronor(-20));
    }

    [Fact]
    public void The_busyness_slider_is_the_same_one_the_site_shows()
    {
        using var fixture = new TillFixture();

        fixture.Admin.SetBusyness(BusynessLevel.Busy);

        fixture.SettingsRepository.Load().Busyness.Should().Be(BusynessLevel.Busy);
        fixture.Audit.Read(action: "BUSYNESS").Should().ContainSingle();
    }

    [Fact]
    public void Today_shows_what_sold_and_who_is_in_the_red()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 5);
        var toast = fixture.AddItem("Toast", 10);

        var till = fixture.NewTill();
        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);
        till.Execute();

        fixture.Admin.Reload();

        fixture.Admin.Today!.Sold.Should().Be(Money.FromKronor(10));
        fixture.Admin.Today.ByItem.Should().ContainSingle(i => i.ItemName == "Toast");
        fixture.Admin.InTheRed.Should().ContainSingle(s => s.DisplayName == "Carl Jacobs");
    }

    [Fact]
    public void An_import_shows_a_preview_and_writes_nothing_until_it_is_confirmed()
    {
        using var fixture = new TillFixture();
        using var sheet = OldCafeSheet();

        var preview = fixture.Admin.PrepareImport(sheet, "gamla-listan.xlsx");

        preview.WillCreate.Should().Be(3);
        preview.TotalInFile.Should().Be(Money.FromKronor(52.50m));
        fixture.Students.All().Should().BeEmpty("the preview writes nothing");

        fixture.Admin.ConfirmImport("/archive/gamla-listan.xlsx").Should().BeTrue();

        fixture.Students.All().Should().HaveCount(3);
        fixture.Students.All().Single(s => s.DisplayName == "Omar Haddad")
            .Balance.Should().Be(Money.FromKronor(-10));
    }

    [Fact]
    public void An_import_can_be_undone_from_the_admin_panel()
    {
        using var fixture = new TillFixture();
        using var sheet = OldCafeSheet();

        fixture.Admin.PrepareImport(sheet, "gamla-listan.xlsx");
        fixture.Admin.ConfirmImport("/archive/gamla-listan.xlsx");

        var import = fixture.Admin.ImportHistory().Single();
        fixture.Admin.UndoImport(import.Id);

        fixture.Students.All(includeInactive: true).Sum(s => s.Balance.Ore).Should().Be(0);
    }

    [Fact]
    public void An_export_gathers_everything_and_changes_nothing()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);

        var till = fixture.NewTill();
        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);
        till.Execute();

        var data = fixture.Admin.BuildExport();

        data.Students.Should().ContainSingle();
        data.Transactions.Should().HaveCount(2);
        data.Items.Should().ContainSingle();
        data.AuditLog.Should().NotBeEmpty();

        // And it really writes a workbook.
        using var file = new MemoryStream();
        new WorkbookExporter().WriteEverything(data, file);
        file.Length.Should().BeGreaterThan(0);

        fixture.Balance(carl).Should().Be(Money.FromKronor(40), "an export never changes anything");
    }

    [Fact]
    public void Backup_verify_and_restore_are_reachable_from_the_panel()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);

        fixture.Admin.BackupNow(fixture.BackupFolder, null).Succeeded.Should().BeTrue();
        fixture.Admin.VerifyBackup(fixture.BackupFolder, null).Succeeded.Should().BeTrue();

        fixture.Admin.Correct(carl, Money.FromKronor(-30), "test");
        fixture.Balance(carl).Should().Be(Money.FromKronor(20));

        var safety = new CashCafe.Backup.FolderDestination(
            Path.Combine(Path.GetTempPath(), $"safety-{Guid.NewGuid():N}"), "Safety");

        var restore = fixture.Admin.RestoreBackup(
            fixture.BackupFolder, fixture.BackupFolder.List().Single().Id, null, safety);

        restore.Succeeded.Should().BeTrue(restore.Message);
        fixture.Balance(carl).Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void Turning_the_student_site_off_is_one_setting()
    {
        using var fixture = new TillFixture();

        fixture.Admin.SetStudentSiteEnabled(true);
        fixture.SettingsRepository.Load().StudentSiteEnabled.Should().BeTrue();

        fixture.Admin.SetStudentSiteEnabled(false);
        fixture.SettingsRepository.Load().StudentSiteEnabled.Should().BeFalse();
        fixture.Admin.Message.Should().Contain("loses access at once");
    }

    private static MemoryStream OldCafeSheet()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.AddWorksheet("Blad1");
        sheet.Cell(1, 1).Value = "Carl Jacobs 50 kr";
        sheet.Cell(2, 1).Value = "Astrid Lindqvist 12,50 kr";
        sheet.Cell(3, 1).Value = "Omar Haddad -10 kr";

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
