# Cash Café

A small Windows program that replaces the school café's Excel sheet.

Instead of typing balances by hand, you pick a student, pick what they bought,
and press **Execute**. The program does the maths, writes down what happened,
and never lets an account go below **-10 kr**.

> **Status:** design/planning stage. This repository currently contains the
> full specification (README + wiki). No code has been written yet — the wiki
> is written precisely enough that the app can be built straight from it.

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
3. Click **Import from Excel**, pick your old `.xlsx` sheet, check the preview looks right, click **Import**.
4. Go to **Admin → Items** and add what the café sells (Toast 10 kr, Juice 15 kr, …).

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

## The admin panel

Open with **Admin** and your PIN.

- **Items** — add, edit, remove, change prices, set what is currently for sale.
- **Students** — add, rename, set class, deactivate someone who left.
- **Today** — what sold today, how much money came in, which students bought what.
- **Reports** — any date range, per item / per student / per class.
- **Import** — bring in an Excel sheet (balances or a student list).
- **Export** — write everything back out to `.xlsx` or `.csv`.
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

Windows 10/11 · C# / .NET 8 · WPF · SQLite · ClosedXML.
Runs completely offline. No account, no server, no internet required.
See [Architecture](docs/wiki/10-Architecture.md).

## Licence

To be decided by the school. See [Roadmap](docs/wiki/15-Roadmap.md).
