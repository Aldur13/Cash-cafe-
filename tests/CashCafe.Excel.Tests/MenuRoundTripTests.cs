using CashCafe.Domain;

namespace CashCafe.Excel.Tests;

/// <summary>What WriteMenu writes has to be exactly what ItemWorkbookReader can read back —
/// an export nobody can re-import is not a backup, it is a dead end.</summary>
public class MenuRoundTripTests
{
    private static Item Sample(long id, string name, decimal price, string? category) => new()
    {
        Id = id,
        Name = name,
        SearchName = SearchNormalizer.Normalize(name),
        Category = category,
        Price = Money.FromKronor(price),
    };

    [Fact]
    public void An_exported_menu_reads_back_the_same_prices_and_names()
    {
        var items = new[]
        {
            Sample(1, "Toast", 10, "Mat"),
            Sample(2, "Smörgås", 20.50m, "Mat"),
            Sample(3, "Juice", 15, null),
        };

        using var stream = new MemoryStream();
        new WorkbookExporter().WriteMenu(items, stream);
        stream.Position = 0;

        var result = new ItemWorkbookReader().Read(stream);

        result.Should().NotBeNull();
        var rows = result!.Value.Rows;
        rows.Should().HaveCount(3);

        // WriteMenu sorts alphabetically, so match by name rather than position.
        var toast = rows.Single(r => r.Name == "Toast");
        toast.Price.Should().Be(Money.FromKronor(10));
        toast.Category.Should().Be("Mat");

        rows.Single(r => r.Name == "Smörgås").Price.Should().Be(Money.FromKronor(20.50m));
        rows.Should().Contain(r => r.Name == "Juice");
    }

    [Fact]
    public void An_archived_item_is_left_out_of_the_export()
    {
        var archived = Sample(1, "Gammal fika", 5, null) with { IsArchived = true };
        var active = Sample(2, "Toast", 10, null);

        using var stream = new MemoryStream();
        new WorkbookExporter().WriteMenu(new[] { archived, active }, stream);
        stream.Position = 0;

        var result = new ItemWorkbookReader().Read(stream);

        result!.Value.Rows.Should().ContainSingle(r => r.Name == "Toast");
    }
}
