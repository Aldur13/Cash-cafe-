using CashCafe.Data;
using CashCafe.Domain;
using CashCafe.Domain.Rules;

// Creates a café database with invented students and a day of trading, so the till and the
// site can be tried, demonstrated to the school board, or screenshotted without touching
// anybody's real data. Every name here is made up.
//
//   dotnet run --project tools/CashCafe.Demo -- ./demo.db

var path = args.FirstOrDefault() ?? "demo.db";
if (File.Exists(path))
{
    Console.Error.WriteLine($"{path} already exists. Delete it first, or name another file.");
    return 1;
}

var database = new CafeDatabase(path);
database.Migrate("demo");

var students = new StudentRepository(database);
var items = new ItemRepository(database);
var ledger = new LedgerRepository(database);
var logins = new StudentLoginRepository(database);
var settings = new SettingsRepository(database);

settings.SetFlag("student_site_enabled", true, "demo");
settings.SetBusyness(BusynessLevel.Steady, "demo", "demo");

var menu = new (string Name, decimal Price, string Category)[]
{
    ("Toast", 10m, "Mat"),
    ("Bulle", 12m, "Fika"),
    ("Juice", 15m, "Dryck"),
    ("Kaffe", 10m, "Dryck"),
    ("Smörgås", 20m, "Mat"),
    ("Frukt", 5m, "Mat"),
};

var menuIds = menu.ToDictionary(
    m => m.Name,
    m => items.Create(m.Name, Money.FromKronor(m.Price), m.Category, "demo"));

var roster = new (string First, string Last, string Class, decimal Opening, string Email)[]
{
    ("Carl", "Jacobs", "9B", 50m, "carl.jacobs@skola.example"),
    ("Astrid", "Lindqvist", "8A", 120m, "astrid.lindqvist@skola.example"),
    ("Omar", "Haddad", "9B", 15m, "omar.haddad@skola.example"),
    ("Åsa", "Öberg", "8A", 200m, "asa.oberg@skola.example"),
    ("Oscar", "Lindh", "9B", 8m, "oscar.lindh@skola.example"),
    ("Carla", "Nyström", "7A", 65m, "carla.nystrom@skola.example"),
};

var settingsNow = settings.Load();
var random = new Random(20260912);

foreach (var (first, last, className, opening, email) in roster)
{
    var id = students.Create(first, last, className, "demo");
    ledger.Deposit(id, Money.FromKronor(opening), DepositMethod.Swish, "Demo opening balance", "demo", "demo");
    logins.Register(id, "google", email, "demo");

    // A few purchases spread over the morning, so the reports have something to show.
    for (var visit = 0; visit < random.Next(1, 4); visit++)
    {
        var choice = menu[random.Next(menu.Length)];
        var quantity = random.Next(1, 3);

        ledger.ExecutePurchase(id,
            new[]
            {
                new BasketLine
                {
                    LineNo = 1,
                    ItemId = menuIds[choice.Name],
                    ItemName = choice.Name,
                    UnitPrice = Money.FromKronor(choice.Price),
                    Quantity = quantity,
                }
            },
            settingsNow, "Café (till 1)", "demo",
            occurredUtc: DateTimeOffset.UtcNow.AddHours(-random.Next(1, 5)));
    }
}

// One staff member, so the staff page and the busyness slider can be tried too.
var staff = students.Create("Café", "Personal", null, "demo");
logins.Register(staff, "google", "cafe.staff@skola.example", "demo");

Console.WriteLine($"Demo café written to {Path.GetFullPath(path)}");
Console.WriteLine();
Console.WriteLine("Students (all invented):");
foreach (var student in students.All())
    Console.WriteLine($"  {student.DisplayName,-20} {student.ClassName,-4} {student.Balance,12}");

Console.WriteLine();
Console.WriteLine("Sign in on the site as any of:");
foreach (var (_, _, _, _, email) in roster) Console.WriteLine($"  {email}");
Console.WriteLine("  cafe.staff@skola.example   (staff — the busyness slider)");

return 0;
