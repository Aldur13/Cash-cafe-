using CashCafe.Domain;
using FluentAssertions;
using Xunit;

namespace CashCafe.Data.Tests;

/// <summary>
/// The rule these tests exist to hold: a person can only see a balance the café
/// deliberately connected to their school address, and only ever one student's.
/// </summary>
public class StudentLoginTests
{
    [Fact]
    public void An_unregistered_address_sees_nothing()
    {
        using var cafe = new TestCafe();
        cafe.AddStudent("Carl", "Jacobs", 50);

        var result = cafe.Logins.SignIn("google", "sub-123", "someone.else@skola.se");

        result.IsSignedIn.Should().BeFalse();
        result.Outcome.Should().Be(SignInOutcome.NotRegistered);
        result.Student.Should().BeNull();
    }

    [Fact]
    public void A_registered_address_is_claimed_on_first_sign_in_and_recognised_after()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        cafe.Logins.Register(carl, "google", "Carl.Jacobs@skola.se", "admin");

        var first = cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se");
        first.Outcome.Should().Be(SignInOutcome.Claimed);
        first.Student!.Id.Should().Be(carl);

        var second = cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se");
        second.Outcome.Should().Be(SignInOutcome.Recognised);
        second.Student!.Balance.Should().Be(Money.FromKronor(50));
    }

    [Fact]
    public void The_address_is_matched_regardless_of_capitals()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs");
        cafe.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        cafe.Logins.SignIn("google", "sub-1", "  Carl.Jacobs@Skola.SE ").IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public void A_second_account_cannot_take_over_a_claimed_address()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        cafe.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        cafe.Logins.SignIn("google", "sub-real", "carl.jacobs@skola.se").IsSignedIn.Should().BeTrue();

        // Someone else's Google account presenting the same address string.
        var impostor = cafe.Logins.SignIn("google", "sub-impostor", "carl.jacobs@skola.se");

        impostor.IsSignedIn.Should().BeFalse();
        impostor.Outcome.Should().Be(SignInOutcome.AlreadyClaimedByAnotherAccount);
    }

    [Fact]
    public void A_renamed_school_address_still_works_because_matching_is_by_account()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        cafe.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se");

        // The school renames the mailbox; the account behind it is the same.
        var later = cafe.Logins.SignIn("google", "sub-123", "carl.jacobs2@skola.se");

        later.Outcome.Should().Be(SignInOutcome.Recognised);
        later.Student!.Id.Should().Be(carl);
    }

    [Fact]
    public void A_disabled_link_cannot_sign_in()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var loginId = cafe.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se");

        cafe.Logins.SetEnabled(loginId, false, "admin");

        cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se")
            .Outcome.Should().Be(SignInOutcome.Disabled);
    }

    [Fact]
    public void A_deactivated_student_cannot_sign_in()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        cafe.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        cafe.Students.SetActive(carl, false, "admin");

        cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se")
            .Outcome.Should().Be(SignInOutcome.StudentInactive);
    }

    [Fact]
    public void The_same_address_cannot_be_registered_to_two_students()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs");
        var carla = cafe.AddStudent("Carla", "Nyström");

        cafe.Logins.Register(carl, "google", "shared@skola.se", "admin");
        var act = () => cafe.Logins.Register(carla, "google", "shared@skola.se", "admin");

        act.Should().Throw<Microsoft.Data.Sqlite.SqliteException>();
    }

    [Fact]
    public void Removing_a_link_frees_the_address_and_ends_access()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs", 50);
        var loginId = cafe.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se");

        cafe.Logins.Remove(loginId, "admin");

        cafe.Logins.SignIn("google", "sub-123", "carl.jacobs@skola.se")
            .Outcome.Should().Be(SignInOutcome.NotRegistered);
        cafe.Student(carl).HasLogin.Should().BeFalse();
    }

    [Fact]
    public void A_student_with_a_link_is_flagged_for_the_admin_list()
    {
        using var cafe = new TestCafe();
        var carl = cafe.AddStudent("Carl", "Jacobs");

        cafe.Student(carl).HasLogin.Should().BeFalse();
        cafe.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        cafe.Student(carl).HasLogin.Should().BeTrue();
    }
}

public class BusynessTests
{
    [Fact]
    public void The_cafe_starts_closed()
    {
        using var cafe = new TestCafe();
        cafe.Config.Busyness.Should().Be(BusynessLevel.Closed);
    }

    [Fact]
    public void Moving_the_slider_is_stored_and_timestamped()
    {
        using var cafe = new TestCafe();

        cafe.Settings.SetBusyness(BusynessLevel.Busy, "Admin", "session-1");

        var settings = cafe.Config;
        settings.Busyness.Should().Be(BusynessLevel.Busy);
        settings.BusynessUpdatedUtc.Should().NotBeNull();
        settings.BusynessUpdatedUtc!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Every_move_of_the_slider_is_audited()
    {
        using var cafe = new TestCafe();

        cafe.Settings.SetBusyness(BusynessLevel.Quiet, "Admin", "s");
        cafe.Settings.SetBusyness(BusynessLevel.Packed, "Admin", "s");

        using var connection = cafe.Database.Open();
        var entries = Dapper.SqlMapper.Query<(string old_value, string new_value, string detail)>(
            connection, "SELECT old_value, new_value, detail FROM audit_log WHERE action = 'BUSYNESS' ORDER BY id")
            .ToList();

        entries.Should().HaveCount(2);
        entries[1].old_value.Should().Be("1");
        entries[1].new_value.Should().Be("4");
        entries[1].detail.Should().Be("Packed");
    }

    [Fact]
    public void The_site_is_off_until_the_school_turns_it_on()
    {
        using var cafe = new TestCafe();

        cafe.Config.StudentSiteEnabled.Should().BeFalse();

        cafe.Settings.SetFlag("student_site_enabled", true, "Admin");
        cafe.Config.StudentSiteEnabled.Should().BeTrue();
    }
}
