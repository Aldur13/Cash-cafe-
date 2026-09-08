using CashCafe.Domain;
using CashCafe.Domain.Import;

namespace CashCafe.Excel.Tests;

public class ItemImportPlannerTests
{
    private static Item Existing(long id, string name, decimal price, string? category = null,
        bool available = true, bool archived = false) => new()
    {
        Id = id,
        Name = name,
        SearchName = SearchNormalizer.Normalize(name),
        Category = category,
        Price = Money.FromKronor(price),
        IsAvailable = available,
        IsArchived = archived,
    };

    private static ParsedItemRow Row(int number, string name, decimal price, string? category = null,
        bool? available = null) => new()
    {
        RowNumber = number,
        RawText = $"{name} {price}",
        Name = name,
        Price = Money.FromKronor(price),
        Category = category,
        Available = available,
    };

    [Fact]
    public void A_name_not_on_the_menu_becomes_a_new_item()
    {
        var preview = ItemImportPlanner.Plan(new[] { Row(2, "Toast", 10) }, Array.Empty<Item>(), "meny.xlsx", "Varor");

        preview.Rows.Should().ContainSingle();
        preview.Rows[0].Status.Should().Be(ItemImportStatus.NewItem);
        preview.Rows[0].Action.Should().Be(ItemImportAction.CreateItem);
        preview.WillCreate.Should().Be(1);
        preview.CanImport.Should().BeTrue();
    }

    [Fact]
    public void An_identical_row_is_kept_as_is()
    {
        var preview = ItemImportPlanner.Plan(
            new[] { Row(2, "Toast", 10) },
            new[] { Existing(1, "Toast", 10) },
            "meny.xlsx", "Varor");

        preview.Rows[0].Status.Should().Be(ItemImportStatus.ExistsUnchanged);
        preview.Rows[0].Action.Should().Be(ItemImportAction.KeepExisting);
        preview.WillUpdate.Should().Be(0);
    }

    [Fact]
    public void A_different_price_is_an_update()
    {
        var preview = ItemImportPlanner.Plan(
            new[] { Row(2, "Toast", 12) },
            new[] { Existing(1, "Toast", 10) },
            "meny.xlsx", "Varor");

        var row = preview.Rows[0];
        row.Status.Should().Be(ItemImportStatus.ExistsDifferent);
        row.Action.Should().Be(ItemImportAction.Update);
        row.MatchedItemId.Should().Be(1);
        row.ExistingPrice.Should().Be(Money.FromKronor(10));
    }

    [Fact]
    public void Matching_is_case_and_accent_insensitive()
    {
        var preview = ItemImportPlanner.Plan(
            new[] { Row(2, "smörgås", 20) },
            new[] { Existing(1, "Smörgås", 20) },
            "meny.xlsx", "Varor");

        preview.Rows[0].Action.Should().Be(ItemImportAction.KeepExisting);
    }

    [Fact]
    public void The_same_name_twice_in_the_file_flags_the_second_as_a_duplicate()
    {
        var preview = ItemImportPlanner.Plan(
            new[] { Row(2, "Toast", 10), Row(3, "Toast", 11) },
            Array.Empty<Item>(), "meny.xlsx", "Varor");

        preview.Rows[1].Status.Should().Be(ItemImportStatus.DuplicateInFile);
        preview.Rows[1].Action.Should().Be(ItemImportAction.Ignore);
    }

    [Fact]
    public void A_row_with_no_price_is_an_error_and_blocks_the_whole_import()
    {
        var badRow = new ParsedItemRow { RowNumber = 2, Name = "Toast", Price = null, Error = "No price." };

        var preview = ItemImportPlanner.Plan(new[] { badRow }, Array.Empty<Item>(), "meny.xlsx", "Varor");

        preview.Rows[0].Status.Should().Be(ItemImportStatus.Error);
        preview.Errors.Should().Be(1);
        preview.CanImport.Should().BeFalse();
    }

    [Fact]
    public void A_blank_row_is_ignored_rather_than_treated_as_an_error()
    {
        var blank = new ParsedItemRow { RowNumber = 2, Name = null, Price = null };

        var preview = ItemImportPlanner.Plan(new[] { blank }, Array.Empty<Item>(), "meny.xlsx", "Varor");

        preview.Rows[0].Status.Should().Be(ItemImportStatus.SkippedNoName);
        preview.Rows[0].Action.Should().Be(ItemImportAction.Ignore);
        preview.Errors.Should().Be(0);
    }

    [Fact]
    public void An_archived_item_with_the_same_name_is_treated_as_new_rather_than_matched()
    {
        var preview = ItemImportPlanner.Plan(
            new[] { Row(2, "Gammal fika", 10) },
            new[] { Existing(1, "Gammal fika", 10, archived: true) },
            "meny.xlsx", "Varor");

        preview.Rows[0].Action.Should().Be(ItemImportAction.CreateItem);
    }
}
