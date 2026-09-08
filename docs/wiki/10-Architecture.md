# 10 — Architecture

For whoever builds or maintains the program.

## Choices, and why

| Choice | Reason |
|---|---|
| **C# / .NET 8** | Long-term support until Nov 2026 and an easy path to .NET 10 LTS; a huge standard library; free tooling; the most likely thing a future Swedish upper-secondary student or school IT person can already read |
| **WPF** | Native Windows desktop. Instant startup, no browser engine, works on old school hardware, keyboard handling that a till actually needs, and a UI defined declaratively in XAML |
| **Self-contained deployment** | The .NET runtime ships inside the program folder. Nothing to install, no Windows Update dependency, no "please install .NET" support call |
| **SQLite** | One file. Transactional. Crash-safe. Trivially backed up. Readable by countless tools in twenty years |
| **ClosedXML** | Reads and writes `.xlsx` without Excel or Office being installed on the machine (MIT licence) |
| **Offline-first** | The café must work when the school Wi-Fi is down, which is exactly when a queue forms |
| **No web server, no accounts, no cloud dependency** | Every one of those would be a thing to secure, patch and explain to the school board. The system is deliberately small |

**Rejected:** Electron (150 MB and a browser engine for a form with two
drop-downs), a web app (needs a server, a login and a security review before it
sells a single toast), Access/Excel VBA (the problem we are escaping),
SQL Server/PostgreSQL (a server to run and back up, for one computer).

## Solution layout

```
CashCafe.sln
├── src/
│   ├── CashCafe.Domain/          no dependencies at all
│   │   ├── Money.cs              öre value type, all arithmetic
│   │   ├── Student.cs, Item.cs, Transaction.cs, TransactionLine.cs
│   │   ├── Purchase.cs           the basket being built at the till
│   │   └── Rules/
│   │       ├── BalanceRules.cs   the −10 kr floor lives here, and only here
│   │       └── PurchaseValidator.cs
│   ├── CashCafe.Data/            SQLite, Dapper, migrations, repositories
│   │   ├── Migrations/001_initial.sql …
│   │   ├── StudentRepository.cs, TransactionRepository.cs, …
│   │   └── DatabaseVerifier.cs
│   ├── CashCafe.Excel/           ClosedXML import and export
│   │   ├── SheetLayoutDetector.cs
│   │   ├── AmountParser.cs       the "50 kr" / "1 250,50" rules
│   │   ├── NameParser.cs
│   │   └── WorkbookExporter.cs
│   ├── CashCafe.Backup/          snapshots, AES-256 encryption, destinations
│   │   ├── LocalFolderDestination.cs
│   │   ├── NetworkShareDestination.cs
│   │   ├── GoogleDriveDestination.cs
│   │   └── OneDriveDestination.cs
│   ├── CashCafe.Web/             ASP.NET Core: the student site and staff slider
│   ├── CashCafe.App.Core/        view models and services — plain net8.0, no WPF
│   │   ├── ViewModels/CafeViewModel.cs, AdminViewModel.cs
│   │   └── Services/CafeContext.cs, AdminSession.cs, AdminPin.cs
│   └── CashCafe.App/             WPF: XAML views and startup, nothing else
│       ├── Views/CafeView.xaml, AdminWindow.xaml, PinDialog.xaml, …
│       ├── Converters/           Money, balance colour, visibility
│       └── App.xaml.cs           the startup sequence
├── tools/
│   └── CashCafe.Demo/            writes a café of invented students to try things with
├── tests/
│   ├── CashCafe.Domain.Tests/    the rules — the most important tests
│   ├── CashCafe.Data.Tests/      against a real temporary SQLite file
│   ├── CashCafe.Excel.Tests/     a corpus of deliberately messy sheets
│   ├── CashCafe.Backup.Tests/    encrypt, restore, retention, corruption
│   ├── CashCafe.Web.Tests/       the site, booted for real
│   └── CashCafe.App.Tests/       the till and admin panel, against a real database
├── installer/                    Inno Setup script
└── docs/wiki/                    this documentation
```

Dependencies point one way only: `App → App.Core → Excel/Backup/Data → Domain`.
`Domain` references nothing, which is what lets the money rules be tested
exhaustively in milliseconds.

### Why the view models are not in the WPF project

The plan originally put them there. They were moved because a `net8.0-windows`
WPF assembly cannot be loaded by a test runner on anything but Windows — and,
more to the point, because the till's behaviour is the part most worth testing
and least worth eyeballing. `CashCafe.App.Core` is plain `net8.0` and references
no WPF at all, so `CafeViewModel` and `AdminViewModel` are driven by real tests
against a real database on any machine. The WPF project keeps XAML, converters
and the startup sequence: the parts a person has to look at anyway.

## Libraries

| Package | Purpose | Licence |
|---|---|---|
| `Microsoft.Data.Sqlite` | Database | MIT |
| `Dapper` | Thin SQL mapping — the SQL stays visible and reviewable | Apache-2.0 |
| `ClosedXML` | `.xlsx` read/write | MIT |
| `CommunityToolkit.Mvvm` | MVVM plumbing | MIT |
| `Microsoft.Extensions.Hosting` / `DependencyInjection` | Startup, DI, config | MIT |
| `Serilog` | File logging with rotation | Apache-2.0 |
| `Google.Apis.Drive.v3` | Google Drive backup (optional feature) | Apache-2.0 |
| `Microsoft.Identity.Client` + Graph | OneDrive backup (optional feature) | MIT |
| `QuestPDF` | PDF statements | MIT (Community licence) |
| `xunit`, `FluentAssertions` | Tests | Apache-2.0 / Apache-2.0 |

All permissive licences, all suitable for a school to use and modify. The
dependency list is kept short on purpose: every package is something someone has
to keep patched.

## The one type that matters most

```csharp
public readonly record struct Money(long Ore) : IComparable<Money>
{
    public static Money FromKronor(decimal kr) => new((long)Math.Round(kr * 100m, MidpointRounding.ToEven));
    public static Money Zero => new(0);

    public static Money operator +(Money a, Money b) => new(a.Ore + b.Ore);
    public static Money operator -(Money a, Money b) => new(a.Ore - b.Ore);
    public static Money operator *(Money a, int qty)  => new(a.Ore * qty);

    public override string ToString() => (Ore / 100m).ToString("N2", SwedishCulture) + " kr";
}
```

There is no `double` and no `float` anywhere in the solution. This is enforced by
an analyzer rule so it cannot creep back in.

## The rule that matters most

```csharp
public static PurchaseCheck Check(Money balance, Money floor, Money total)
{
    var after = balance - total;
    return after >= floor
        ? PurchaseCheck.Allowed(after)
        : PurchaseCheck.Refused(shortfall: floor - after, canSpend: balance - floor);
}
```

It lives in `CashCafe.Domain` and is called from exactly two places: the view
model (to enable/disable the button and show the message) and the repository
(inside the database transaction, immediately before writing). Both call the same
function, so the message on screen and the rule that is enforced can never
diverge.

## Concurrency

Version 1 is **one computer, one program instance**. A named mutex prevents a
second instance opening the same database. Even so, writes use
`BEGIN IMMEDIATE` and the optimistic balance check shown in
[Data Model](09-Data-Model.md#worked-example), because two windows inside one
process (a till and an open admin panel) can still race.

Multi-till is a real design change, not a setting — see [Roadmap](15-Roadmap.md).

## Startup sequence

1. Single-instance mutex.
2. Locate the database (installed mode vs. portable mode).
3. Apply migrations, after an automatic pre-migration backup.
4. `PRAGMA integrity_check` and the balance-vs-ledger verification.
5. Build the in-memory student search index.
6. Load settings; start the background backup scheduler.
7. Open the Café screen with focus in the student box.

Target: **under 2 seconds** on a school laptop with a spinning disk.

## Testing

- **Domain**: exhaustive tests of the floor rule, including the exact boundary
  (a purchase landing on exactly −10,00 kr is *allowed*; one öre more is not),
  quantity limits, reversal rules, and property-based tests asserting that the
  sum of any random ledger equals the reported balance.
- **Data**: every repository against a real temporary SQLite file; explicit tests
  that `UPDATE` and `DELETE` on `transactions` are rejected by the triggers.
- **Excel**: a checked-in corpus of deliberately awful sheets — merged cells,
  totals rows, `50 kr` in one cell, blank rows, non-breaking spaces, formulas,
  a duplicated student, `femtio` — each with an expected result file.
- **Smoke test**: a scripted end-to-end run — import, sell, hit the floor, undo,
  export, back up, restore, verify — executed on every release build.

## Building on a machine that is not Windows

The WPF project sets `<EnableWindowsTargeting>true</EnableWindowsTargeting>`, so
`dotnet build` succeeds on Linux and macOS and in CI. That gives compile and XAML
verification everywhere; **running** it still needs Windows. Everything else in
the solution — domain, data, Excel, backups, the website, and every test project
— builds and runs anywhere.

## Build and release

```
dotnet test
dotnet publish src/CashCafe.App -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:Version=1.0.0
iscc installer/CashCafe.iss
```

Produces `CashCafe-Setup-1.0.0.exe` (~70 MB) and the portable zip. Releases are
tagged in git and the release notes list every change, because a café tool that
changes silently is a café tool nobody trusts.

Code signing: unsigned builds trigger a SmartScreen warning. If the school buys
an OV code-signing certificate the same script signs the output; until then, the
warning and how to get past it are documented in
[Troubleshooting](13-Troubleshooting.md).
