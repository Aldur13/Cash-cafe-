using CashCafe.App.Core.Services;
using CashCafe.Domain;

namespace CashCafe.App.Tests;

public class AdminPinTests
{
    [Fact]
    public void A_pin_is_stored_only_as_a_hash()
    {
        var stored = AdminPin.Hash("1234");

        stored.Should().StartWith("pbkdf2-sha256$310000$");
        stored.Should().NotContain("1234", "the PIN itself must not be recoverable from the file");
    }

    [Fact]
    public void The_right_pin_verifies_and_a_wrong_one_does_not()
    {
        var stored = AdminPin.Hash("4711");

        AdminPin.Verify("4711", stored).Should().BeTrue();
        AdminPin.Verify("4712", stored).Should().BeFalse();
        AdminPin.Verify(string.Empty, stored).Should().BeFalse();
    }

    [Fact]
    public void Two_identical_pins_hash_differently()
    {
        AdminPin.Hash("1234").Should().NotBe(AdminPin.Hash("1234"), "each hash gets its own salt");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567890123")]
    [InlineData("abcd")]
    [InlineData("12a4")]
    [InlineData("")]
    public void A_pin_that_breaks_the_rules_is_refused(string pin)
    {
        var act = () => AdminPin.Hash(pin);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_damaged_stored_hash_fails_closed()
    {
        AdminPin.Verify("1234", "nonsense").Should().BeFalse();
        AdminPin.Verify("1234", "pbkdf2-sha256$310000$not-base64$also-not").Should().BeFalse();
    }
}

public class AdminSessionTests
{
    [Fact]
    public void Admin_is_not_configured_until_a_pin_is_set()
    {
        using var fixture = new TillFixture();

        fixture.Session.IsConfigured.Should().BeFalse();
        fixture.Session.Enter("1234").Should().Be(PinResult.NotSetUp);

        fixture.Session.SetPin("1234", "setup");
        fixture.Session.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void The_right_pin_opens_the_panel()
    {
        using var fixture = new TillFixture();
        fixture.Session.SetPin("4711", "setup");

        fixture.Session.Enter("4711").Should().Be(PinResult.Accepted);
        fixture.Session.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Five_wrong_pins_lock_admin_out()
    {
        using var fixture = new TillFixture();
        fixture.Session.SetPin("4711", "setup");

        for (var attempt = 1; attempt <= 4; attempt++)
            fixture.Session.Enter("0000").Should().Be(PinResult.Wrong);

        fixture.Session.Enter("0000").Should().Be(PinResult.LockedOut);
        fixture.Session.IsLockedOut.Should().BeTrue();

        // Even the right PIN waits out the lockout.
        fixture.Session.Enter("4711").Should().Be(PinResult.LockedOut);
        fixture.Session.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void A_lockout_never_stops_the_till()
    {
        using var fixture = new TillFixture();
        var carl = fixture.AddStudent("Carl", "Jacobs", 50);
        var toast = fixture.AddItem("Toast", 10);
        fixture.Session.SetPin("4711", "setup");

        for (var attempt = 0; attempt < 5; attempt++) fixture.Session.Enter("0000");
        fixture.Session.IsLockedOut.Should().BeTrue();

        var till = fixture.NewTill();
        till.SelectStudent(fixture.Students.Find(carl));
        till.SetItem(1, toast);

        till.Execute().Should().BeTrue("a café that stops selling over a mistyped PIN is worse than a spreadsheet");
    }

    [Fact]
    public void Every_wrong_pin_is_written_to_the_audit_log()
    {
        using var fixture = new TillFixture();
        fixture.Session.SetPin("4711", "setup");

        fixture.Session.Enter("0000");
        fixture.Session.Enter("1111");

        fixture.Audit.Read(action: "LOGIN_FAILED").Should().HaveCount(2);
    }

    [Fact]
    public void The_panel_locks_itself_after_sitting_idle()
    {
        using var fixture = new TillFixture();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var session = new AdminSession(fixture.SettingsRepository, fixture.Audit, clock)
        {
            IdleTimeout = TimeSpan.FromMinutes(10),
        };

        session.SetPin("4711", "setup");
        session.Enter("4711").Should().Be(PinResult.Accepted);

        clock.Advance(TimeSpan.FromMinutes(9));
        session.HasGoneIdle().Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(2));
        session.HasGoneIdle().Should().BeTrue("an admin panel must not be left open on a counter");
    }

    [Fact]
    public void A_lockout_expires_on_its_own()
    {
        using var fixture = new TillFixture();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var session = new AdminSession(fixture.SettingsRepository, fixture.Audit, clock);

        session.SetPin("4711", "setup");
        for (var attempt = 0; attempt < 5; attempt++) session.Enter("0000");
        session.IsLockedOut.Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(6));

        session.IsLockedOut.Should().BeFalse();
        session.Enter("4711").Should().Be(PinResult.Accepted);
    }
}

internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
