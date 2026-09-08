using System.Net;
using CashCafe.Domain;

namespace CashCafe.Web.Tests;

public class SignInTests
{
    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_sign_in()
    {
        using var site = new SiteFixture();
        using var client = site.NewClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/SignIn");
    }

    [Fact]
    public async Task An_address_the_cafe_never_registered_sees_no_balance()
    {
        using var site = new SiteFixture();
        site.AddStudent("Carl", "Jacobs", 50);

        using var client = await site.SignedInAsAsync("stranger@skola.se");
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/SignIn");
    }

    [Fact]
    public async Task A_registered_student_sees_their_own_balance_and_history()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50, "9B");
        site.Buy(carl, "Toast", 10);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        var html = await client.GetStringAsync("/");

        html.Should().Contain("Carl Jacobs");
        html.Should().Contain("9B");
        html.Should().Contain("40,00 kr");            // balance
        html.Should().Contain("Toast");               // what they bought
        html.Should().Contain("50,00 kr");            // can spend, with the -10 kr floor
    }

    [Fact]
    public async Task One_student_cannot_see_another_students_page()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        var astrid = site.AddStudent("Astrid", "Lindqvist", 999);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        site.Logins.Register(astrid, "google", "astrid@skola.se", "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");

        // There is no parameter that names a student, so the only thing to try is adding one.
        foreach (var attempt in new[] { "/", "/?studentId=" + astrid, "/?id=" + astrid, "/Index?student=" + astrid })
        {
            var html = await client.GetStringAsync(attempt);
            html.Should().Contain("Carl Jacobs");
            html.Should().NotContain("Astrid");
            html.Should().NotContain("999,00 kr");
        }
    }

    [Fact]
    public async Task A_deactivated_student_is_signed_out_rather_than_shown_a_page()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.OK);

        site.Students.SetActive(carl, false, "admin");

        var after = await client.GetAsync("/");
        after.StatusCode.Should().Be(HttpStatusCode.Redirect);
        after.Headers.Location!.OriginalString.Should().Contain("SignOut");
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        await client.GetAsync("/SignOut");

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/SignIn");
    }
}

public class SiteSwitchTests
{
    [Fact]
    public async Task With_the_site_switched_off_nothing_is_served()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The school turns the site off while a student is signed in.
        site.Settings.SetFlag("student_site_enabled", false, "admin");

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Closed");
    }

    [Fact]
    public async Task History_can_be_turned_off_leaving_only_the_balance()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Buy(carl, "Toast", 10);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        site.Settings.SetFlag("student_site_show_history", false, "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        var html = await client.GetStringAsync("/");

        html.Should().Contain("40,00 kr");
        html.Should().NotContain("Toast");
    }
}

public class BusynessSiteTests
{
    [Fact]
    public async Task Students_see_how_busy_the_cafe_is()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        site.Settings.SetBusyness(BusynessLevel.Busy, "staff", "s");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        var html = await client.GetStringAsync("/");

        html.Should().Contain("Busy");
        html.Should().Contain("expect to wait");
        html.Should().Contain("busy level-3");
    }

    [Fact]
    public async Task The_busyness_indicator_can_be_turned_off()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        site.Settings.SetBusyness(BusynessLevel.Packed, "staff", "s");
        site.Settings.SetFlag("student_site_show_busyness", false, "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        var html = await client.GetStringAsync("/");

        html.Should().NotContain("Packed");
    }

    [Fact]
    public async Task A_student_cannot_open_the_staff_page()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        var response = await client.GetAsync("/Staff");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Problem");
    }

    [Fact]
    public async Task A_student_cannot_move_the_slider_by_posting_to_it()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");

        using var client = await site.SignedInAsAsync("carl.jacobs@skola.se");
        var response = await client.PostAsync("/Staff",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["level"] = "4" }));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.BadRequest);
        site.Settings.Load().Busyness.Should().Be(BusynessLevel.Closed);
    }

    [Fact]
    public async Task Staff_move_the_slider_and_students_see_it()
    {
        using var site = new SiteFixture();
        var carl = site.AddStudent("Carl", "Jacobs", 50);
        var staffMember = site.AddStudent("Cafe", "Staff");
        site.Logins.Register(carl, "google", "carl.jacobs@skola.se", "admin");
        site.Logins.Register(staffMember, "google", SiteFixture.StaffEmail, "admin");

        using var staff = await site.SignedInAsAsync(SiteFixture.StaffEmail);
        var page = await staff.GetStringAsync("/Staff");
        page.Should().Contain("How busy are we?");

        var post = await staff.PostAsync("/Staff", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["level"] = "2",
            ["__RequestVerificationToken"] = SiteFixture.AntiforgeryToken(page),
        }));

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        site.Settings.Load().Busyness.Should().Be(BusynessLevel.Steady);

        using var student = await site.SignedInAsAsync("carl.jacobs@skola.se");
        (await student.GetStringAsync("/")).Should().Contain("Steady");
    }

    [Fact]
    public async Task A_slider_value_outside_the_scale_is_ignored()
    {
        using var site = new SiteFixture();
        var staffMember = site.AddStudent("Cafe", "Staff");
        site.Logins.Register(staffMember, "google", SiteFixture.StaffEmail, "admin");
        site.Settings.SetBusyness(BusynessLevel.Quiet, "staff", "s");

        using var staff = await site.SignedInAsAsync(SiteFixture.StaffEmail);
        var page = await staff.GetStringAsync("/Staff");

        await staff.PostAsync("/Staff", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["level"] = "99",
            ["__RequestVerificationToken"] = SiteFixture.AntiforgeryToken(page),
        }));

        site.Settings.Load().Busyness.Should().Be(BusynessLevel.Quiet);
    }
}

public class SiteHeaderTests
{
    [Fact]
    public async Task The_site_sends_the_headers_it_promises()
    {
        using var site = new SiteFixture();
        using var client = site.NewClient();

        var response = await client.GetAsync("/SignIn");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("Content-Security-Policy").Single()
            .Should().Contain("default-src 'self'").And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task The_site_asks_not_to_be_indexed()
    {
        using var site = new SiteFixture();
        using var client = site.NewClient();

        (await client.GetStringAsync("/SignIn")).Should().Contain("noindex");
    }
}
