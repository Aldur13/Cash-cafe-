# Cash Café

A small Windows program that replaces the school café's Excel sheet.

Instead of typing balances by hand, you pick a student, pick what they bought,
and press **Execute**. The program does the maths, writes down what happened,
and never lets an account go below **-10 kr**.

> **Status: working, not yet packaged.**
> The till, the admin panel, Excel import/export, backups and the student
> website are all written, with **278 automated tests**. What is left is the
> installer, and a pass over the Windows screens on a real Windows machine —
> they compile and are covered by tests, but nobody has clicked through them
> yet. See [Roadmap](docs/wiki/15-Roadmap.md).

---

## What it does

| The old Excel sheet | Cash Café |
|---|---|
| `Carl Jacobs 50 kr` — you edit the number by hand | You press a button, the number updates itself |
| Buys a toast → you subtract 10 in your head | You pick "Toast", the price appears, it subtracts 10 |
| A typo silently destroys someone's balance | Every change is written to a permanent history you can look at |
| No idea what sold today | Daily sales report, per item and per student |
| One person can have the file open | Balance is always correct because it is calculated, never typed |
| Nothing stops a -300 kr debt | Hard stop at -10 kr |

---

## How to use it (the 60-second version)

### First time only

1. Install **Cash Café** (double-click `CashCafe-Setup.exe`, click Next until it finishes).
2. Open it. It asks for an admin PIN — choose one and write it down.
3. Press **F9** for the admin panel, go to the **Students** tab, click **Import from Excel…**,
   pick your old `.xlsx` sheet, check the preview looks right, click **Import**.
4. On the **Items** tab click **Add item…** for what the café sells (Toast 10 kr, Juice 15 kr, …).

You are done. That was the setup.

### Every day, at the counter

1. **Student** — start typing a name. The list filters as you type. Pick the student.
   Their balance shows on the right.
2. **Items** — pick what they are buying. The price appears next to it.
   A new empty row opens automatically so you can add another item — up to **10 items** per purchase.
3. The total and the **new balance** update live at the bottom.
4. Press **Execute** (or `Enter`).

Done. The balance is saved. If the purchase would push the student below **-10 kr**,
the Execute button is disabled and tells you why.

Made a mistake? Press `Ctrl+Z` or click **Undo last purchase** — it is reversed
and the reversal is recorded (nothing is ever silently deleted).

### Putting money in

A parent Swishes money to the café → open **Deposit**, pick the student, type the
amount, press **Save**. (Automatic Swish matching is planned but not built — see
[Roadmap](docs/wiki/15-Roadmap.md).)

---

## The student website

Optional, **off until you turn it on**, and reachable **only from the school network**.

A student opens it on their phone, signs in with their **school Google or Microsoft
account** — no new password, the café never sees one — and sees:

- **their own balance**, and what they can still spend before −10 kr;
- **how busy the café is right now**, so they know whether to come or wait;
- **their own café history**, with anything that was cancelled shown struck through.

They can see nothing else, and nobody else's anything. The site cannot move money.

### The busyness slider

Café staff open `/Staff` on their phone and drag one slider:

```
Closed —— Quiet —— Steady —— Busy —— Packed
```

Every student's page updates. Staff also see today's totals and what sold. That's the
whole staff page — prices, corrections and reports stay in the till behind the admin PIN.

Setup, sign-in flow and the security details: **[The Student Site](docs/wiki/18-Student-Site.md)**.

## The admin panel

Open with **Admin** and your PIN.

- **Items** — add, edit, remove, change prices, set what is currently for sale.
- **Students** — add, rename, set class, deactivate someone who left.
- **Today** — what sold today, how much money came in, which students bought what.
- **Reports** — any date range, per item / per student / per class.
- **Import** — bring in an Excel sheet: student balances (Students tab) or the
  whole menu at once (Items tab), both with a preview before anything is written.
- **Export** — write everything back out to `.xlsx` or `.csv`, or just the menu
  on its own to hand to whoever prices the café's stock.
- **Backup & Cloud** — back up to a folder, a USB stick, Google Drive or OneDrive.
- **History (audit log)** — every purchase, deposit, correction and price change,
  who did it and when. Read-only, cannot be edited.

---

## Rules the program enforces

- A student's balance can never go below **-10 kr**. The purchase is simply refused.
- Balances are **never typed in directly** — they are the sum of every deposit and
  purchase. This is why the numbers can't drift the way the Excel sheet did.
- Nothing is deleted. A mistake is fixed with a **reversal**, which stays visible
  in the history.
- All prices are in Swedish kronor (kr / SEK), stored in **öre** (whole numbers)
  so there are no rounding errors.

---

## Documentation

Short version: this README.
Long version: the **[wiki](docs/wiki/Home.md)** — every screen, every rule, the
database, the Excel formats, and a full write-up for the school board on cloud
backup and data protection.

Start here:

- [Home / table of contents](docs/wiki/Home.md)
- [Quick start](docs/wiki/03-Quick-Start.md)
- [Café screen (the till)](docs/wiki/04-Cafe-Screen.md)
- [Admin panel](docs/wiki/05-Admin-Panel.md)
- [Excel import](docs/wiki/06-Excel-Import.md) / [Excel export](docs/wiki/07-Excel-Export.md)
- [Cloud sync & backup](docs/wiki/11-Cloud-Sync-and-Backup.md)
- **[Security & privacy — for the school board](docs/wiki/12-Security-and-Privacy.md)**
- [Troubleshooting](docs/wiki/13-Troubleshooting.md)

## Built with

Windows 10/11 · C# / .NET 8 · WPF (the till) · ASP.NET Core (the site) · SQLite · ClosedXML.
The till runs completely offline. The website is optional and never leaves the school
network. See [Architecture](docs/wiki/10-Architecture.md).

## Building it yourself

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download). Nothing else.

```bash
dotnet test                                          # all 278 tests
dotnet run --project tools/CashCafe.Demo -- demo.db  # a café of invented students
dotnet run --project src/CashCafe.Web \
    --urls http://127.0.0.1:5199 \
    Cafe:DatabasePath=demo.db \
    Cafe:EnableDevelopmentSignIn=true \
    Cafe:StaffEmails:0=cafe.staff@skola.example
```

Then open <http://127.0.0.1:5199> and sign in as one of the addresses the demo printed.
The development sign-in skips Google entirely so you can try it without credentials; it
only works on a Development build, from the machine itself, with the setting on.

To run the till itself, on Windows:

```powershell
dotnet run --project src/CashCafe.App
```

The whole solution builds on Linux and macOS too — the WPF project sets
`EnableWindowsTargeting` — but only Windows can run the till.

| Project | What it is |
|---|---|
| `src/CashCafe.Domain` | Money, the −10 kr floor, the purchase basket, name search. No dependencies |
| `src/CashCafe.Data` | SQLite schema, the append-only ledger, repositories, the verifier |
| `src/CashCafe.Excel` | Reads the old sheet, writes every export |
| `src/CashCafe.Backup` | Snapshots, AES-256 encryption, restore, retention |
| `src/CashCafe.Web` | The student site and the staff busyness slider |
| `src/CashCafe.App.Core` | The till and admin logic — tested, no Windows needed |
| `src/CashCafe.App` | The WPF windows themselves (Windows only to run) |
| `tools/CashCafe.Demo` | Makes a database of invented students to try things with |

## Licence

To be decided by the school. See [Roadmap](docs/wiki/15-Roadmap.md).
