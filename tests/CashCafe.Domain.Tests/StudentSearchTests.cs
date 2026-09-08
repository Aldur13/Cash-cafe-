using CashCafe.Domain;
using FluentAssertions;
using Xunit;

namespace CashCafe.Domain.Tests;

public class SearchNormalizerTests
{
    [Theory]
    [InlineData("Carl Jacobs", "carl jacobs")]
    [InlineData("  Carl   Jacobs  ", "carl jacobs")]
    [InlineData("Åsa Öberg", "asa oberg")]
    [InlineData("Émile Zola", "emile zola")]
    [InlineData("O'Brien-Smith", "o brien smith")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Names_are_folded_for_searching(string? input, string expected)
    {
        SearchNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Initials_come_from_the_words()
    {
        SearchNormalizer.Initials("Carl Jacobs").Should().Be("cj");
        SearchNormalizer.Initials("Åsa Maria Öberg").Should().Be("amo");
    }
}

public class StudentSearchTests
{
    private static Student Make(string name, string? className = null, DateTimeOffset? lastActivity = null, bool active = true)
    {
        var parts = name.Split(' ');
        return new Student
        {
            Id = name.GetHashCode(),
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[^1] : string.Empty,
            DisplayName = name,
            SearchName = SearchNormalizer.Normalize(name),
            ClassName = className,
            IsActive = active,
            LastActivityUtc = lastActivity,
        };
    }

    private static readonly List<Student> Students = new()
    {
        Make("Carl Jacobs", "9B"),
        Make("Carla Nyström", "7A"),
        Make("Oscar Lindh", "9B"),
        Make("Astrid Lindqvist", "8A"),
        Make("Åsa Öberg", "8A"),
        Make("Omar Haddad", "9B"),
    };

    [Fact]
    public void Typing_a_prefix_finds_the_student()
    {
        var hits = StudentSearch.Find(Students, "car");

        hits.Select(s => s.DisplayName).Should().Contain("Carl Jacobs");
    }

    [Fact]
    public void A_word_prefix_beats_a_mid_word_match()
    {
        var hits = StudentSearch.Find(Students, "car");

        // "Carl" and "Carla" start with it; "Oscar" only contains it.
        hits.Select(s => s.DisplayName).Should().StartWith(new[] { "Carl Jacobs", "Carla Nyström" });
        hits.Select(s => s.DisplayName).Should().Contain("Oscar Lindh");
    }

    [Fact]
    public void Words_match_in_any_order()
    {
        StudentSearch.Find(Students, "jac carl").Should().ContainSingle()
            .Which.DisplayName.Should().Be("Carl Jacobs");
    }

    [Fact]
    public void Initials_find_a_student()
    {
        StudentSearch.Find(Students, "cj").Select(s => s.DisplayName).Should().Contain("Carl Jacobs");
    }

    [Fact]
    public void Swedish_letters_are_folded_both_ways()
    {
        StudentSearch.Find(Students, "asa").Select(s => s.DisplayName).Should().Contain("Åsa Öberg");
        StudentSearch.Find(Students, "oberg").Select(s => s.DisplayName).Should().Contain("Åsa Öberg");
    }

    [Fact]
    public void One_typo_is_forgiven()
    {
        StudentSearch.Find(Students, "carl jacbos").Select(s => s.DisplayName).Should().Contain("Carl Jacobs");
        StudentSearch.Find(Students, "astrd").Select(s => s.DisplayName).Should().Contain("Astrid Lindqvist");
    }

    [Fact]
    public void Two_typos_are_not_forgiven()
    {
        StudentSearch.Find(Students, "jxcxbs").Should().BeEmpty();
    }

    [Fact]
    public void Recently_served_students_come_first_within_a_rank()
    {
        var recent = Make("Carla Andersson", "7A", DateTimeOffset.UtcNow);
        var list = new List<Student>(Students) { recent };

        StudentSearch.Find(list, "carla").First().DisplayName.Should().Be("Carla Andersson");
    }

    [Fact]
    public void Deactivated_students_are_not_searched()
    {
        var list = new List<Student> { Make("Gone Student", active: false) };

        StudentSearch.Find(list, "gone").Should().BeEmpty();
    }

    [Fact]
    public void An_empty_query_shows_the_most_recent_students()
    {
        StudentSearch.Find(Students, "").Should().NotBeEmpty();
    }

    [Fact]
    public void A_swap_of_two_letters_is_forgiven()
    {
        // The most common typing mistake there is, and plain edit distance calls it two edits.
        StudentSearch.Find(Students, "jacbos").Select(s => s.DisplayName).Should().Contain("Carl Jacobs");
        StudentSearch.Find(Students, "oscra").Select(s => s.DisplayName).Should().Contain("Oscar Lindh");
    }

    [Theory]
    [InlineData("toast", "tost", true)]      // deletion
    [InlineData("toast", "toasts", true)]    // insertion
    [InlineData("toast", "toast", true)]     // identical
    [InlineData("toast", "roast", true)]     // substitution
    [InlineData("jacobs", "jacbos", true)]   // transposition
    [InlineData("toast", "roasts", false)]   // two edits
    [InlineData("jacobs", "jcabos", false)]  // two transpositions
    public void One_edit_apart_is_recognised(string a, string b, bool expected)
    {
        StudentSearch.IsWithinOneEdit(a, b).Should().Be(expected);
    }
}
