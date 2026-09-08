using CashCafe.Data;
using CashCafe.Domain;
using CashCafe.Domain.Rules;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CashCafe.Web.Tests;

/// <summary>
/// Boots the real site against a real temporary database, with the development sign-in
/// switched on so a test can reach a signed-in page without Google credentials.
/// </summary>
public sealed class SiteFixture : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"cashcafe-web-{Guid.NewGuid():N}.db");

    public SiteFixture()
    {
        Database = new CafeDatabase(_databasePath);
        Database.Migrate("test");

        Students = new StudentRepository(Database);
        Items = new ItemRepository(Database);
        Ledger = new LedgerRepository(Database);
        Logins = new StudentLoginRepository(Database);
        Settings = new SettingsRepository(Database);

        // The site is off by default, which is the shipped behaviour; most tests want it on.
        Settings.SetFlag("student_site_enabled", true, "test");
    }

    public CafeDatabase Database { get; }
    public StudentRepository Students { get; }
    public ItemRepository Items { get; }
    public LedgerRepository Ledger { get; }
    public StudentLoginRepository Logins { get; }
    public SettingsRepository Settings { get; }

    public const string StaffEmail = "cafe.staff@skola.se";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cafe:DatabasePath"] = _databasePath,
            ["Cafe:EnableDevelopmentSignIn"] = "true",
            ["Cafe:StaffEmails:0"] = StaffEmail,
        }));

        return base.CreateHost(builder);
    }

    /// <summary>A client that keeps cookies, so a sign-in carries across requests.</summary>
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    public long AddStudent(string first, string last, decimal opening = 0, string? className = null)
    {
        var id = Students.Create(first, last, className, "test");
        if (opening != 0)
            Ledger.Deposit(id, Money.FromKronor(opening), DepositMethod.Swish, "opening", "test", "test");
        return id;
    }

    public void Buy(long studentId, string itemName, decimal price, int quantity = 1)
    {
        var itemId = Items.Create(itemName, Money.FromKronor(price), null, null, "test");
        Ledger.ExecutePurchase(studentId,
            new[]
            {
                new BasketLine
                {
                    LineNo = 1,
                    ItemId = itemId,
                    ItemName = itemName,
                    UnitPrice = Money.FromKronor(price),
                    Quantity = quantity,
                }
            },
            Settings.Load(), "Café (till 1)", "test");
    }

    /// <summary>Signs in through the site's own development sign-in, exactly as a browser would.</summary>
    public async Task<HttpClient> SignedInAsAsync(string email)
    {
        var client = NewClient();
        var page = await client.GetAsync("/SignIn");
        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        var response = await client.PostAsync("/SignIn?handler=Development", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["email"] = email,
                ["__RequestVerificationToken"] = token,
            }));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        return client;
    }

    public static string AntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, """name="__RequestVerificationToken"[^>]*value="([^"]+)""");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
