# 05 — The Admin Panel

Everything that is not "sell something" lives here. Opened with **Admin** on the
Café screen or `F9`, and protected by the admin PIN.

- The PIN is asked for every time the panel is opened.
- The panel locks itself after **10 minutes** of no activity and returns to the
  Café screen, so it cannot be left open at a counter.
- Five wrong PINs in a row locks admin access for 5 minutes (the Café screen keeps
  working normally — the café must never stop because of a login).
- **Every action in this panel is written to the audit log**, including the ones
  that look harmless.

The panel has eight tabs.

---

## Items

The list of everything the café sells.

| Column | Notes |
|---|---|
| Name | Shown at the till |
| Price | In kr, e.g. `10,00` |
| Category | Optional — Food, Drink, Sweets… used for grouping and reports |
| Shortcut | Optional single key for fast entry at the till |
| Available | Toggle. Off = hidden from the till, still in reports |
| Sold today / this month | Read-only, so you can see what actually moves |

### Actions

- **New item** — name and price are required; everything else optional.
- **Edit** — change name, category, shortcut, availability.
- **Change price** — deliberately a separate action from Edit, with its own
  dialog and an optional "from when" date. **Old sales keep the old price**;
  changing a price never rewrites history
  ([Balance Rules](08-Balance-Rules.md#price-changes)).
- **Price history** — every price this item has ever had, when it changed and who
  changed it.
- **Remove** — an item that has never been sold is deleted outright. An item that
  *has* been sold is **archived** instead (hidden everywhere except historical
  reports), because deleting it would break the record of past purchases. The
  program explains which of the two it is doing before you confirm.
- **Duplicate** — copy an item as a starting point for a similar one.
- **Import / export item list** — a small `.xlsx` with the columns above, so a
  whole menu can be prepared in Excel and pasted in.

### Sold out for the day

Toggling **Available** off is the intended way to handle "we're out of toast".
It takes effect at the till immediately, on the next screen refresh.

---

## Students

| Column | Notes |
|---|---|
| Name | First and last name as it should appear at the till |
| Class | Optional, e.g. `9B`. Used for grouping in reports |
| Balance | Read-only. Calculated, never editable here |
| Credit limit | Per-student floor. Blank = use the café default (−10,00 kr) |
| Active | Off = hidden from the till, history kept |
| Note | Free text shown as a banner at the till, e.g. "cash only" |

### Actions

- **New student** / **Edit** / **Deactivate**.
- **Merge students** — for when the same person was imported twice
  (`Carl Jacobs` and `carl jacobs`). Merging moves all transactions onto one
  record, adds the balances, and keeps a pointer from the old id so nothing is
  lost. This is the correct fix for a duplicated import.
- **Correction** — the only way to change a balance without a purchase or a
  deposit. Requires an amount **and a written reason**, and writes an
  `ADJUSTMENT` transaction that shows up in the history like everything else.
  Use it for "the old Excel sheet said 43 and we've agreed it should be 40".
- **Statement** — one student's full history, printable/exportable as PDF or
  Excel. This is what you hand a parent who asks.
- **Set credit limit** — allows a specific student to go below the default floor
  (or restricts them to 0). Logged, with a reason.
- **Bulk actions** — deactivate a whole class at the end of the year, set a class
  on many students at once, export a class list.

### Recently added at the counter

A filtered view of students created from the till, so an admin can correct
spelling and class afterwards.

---

## Deposits

Recording money that came in. Also reachable from the Café screen with `F4`.

| Field | Notes |
|---|---|
| Student | Same live search as the till |
| Amount | Positive only. A negative "deposit" is not possible — use a Correction |
| Method | `Swish`, `Cash`, `Bank transfer`, `Correction`, `Other` |
| Reference | Free text — the Swish message or reference number |
| Date | Defaults to now; can be back-dated by an admin, which is logged |
| Note | Optional |

**Bulk deposit** takes a pasted list or a small Excel file:

```
Carl Jacobs      100
Astrid Lindqvist  50
```

…matches the names (with the same fuzzy matching as import, and a preview you
must confirm), and writes one deposit per row with a shared batch reference. This
is how you handle "here is the Swish report for the week".

> Automatic Swish reconciliation is **planned, not built** — see [Roadmap](15-Roadmap.md).
> Today the café reads its Swish report and enters the deposits.

---

## Today

The at-a-glance day view. Everything here is for **today** by default, with a
date picker to look at any other day.

- **Money in** — total deposits today, split by method.
- **Value sold** — total purchases today.
- **Per item** — a table: item, quantity sold, value. Sorted by value.
- **Per student** — every student who bought something today, what they bought,
  and their total. This is the "watch how much we sold today to which students"
  view.
- **Per class** — the same, grouped by class.
- **Transactions** — the raw list, newest first, with reversals shown struck
  through and linked to their original.
- **In the red** — students currently below zero, and how far.
- **Busiest times** — a simple bar chart by hour, useful for staffing the counter.

Buttons: **Print**, **Export this view to Excel**, **Copy summary** (a few lines
of text for a group chat).

---

## Reports

The same information as **Today**, over any date range, with filters.

| Report | Answers |
|---|---|
| Sales by item | What do we actually sell? What should we order? |
| Sales by student | Who spends what — and who is spending a surprising amount |
| Sales by class | Useful when classes take turns running the café |
| Deposits | What came in, by method and by day |
| Balance list | Everyone's balance right now, sorted |
| Negative balances | Who owes money, and since when |
| Inactive money | Balances belonging to students who have not bought anything for N months — end-of-year cleanup |
| Cash-up / reconciliation | Opening total balance + deposits − sales = closing total balance. If this does not add up, something is wrong and the report says where |
| Audit summary | Corrections, reversals, price changes and custom amounts in the period — the report to look at if something feels off |

Every report can be printed, exported to `.xlsx` or `.csv`, and shows the exact
filters used in its header so a printed copy is self-explanatory.

---

## Import

See [Excel Import](06-Excel-Import.md) for the full detail. From this tab you can:

- Import **balances** from the old sheet (first-time setup, or a re-sync).
- Import a **student list** (names and classes, no money).
- Import an **item list**.
- Import **deposits** in bulk.
- See the **import history** — every file ever imported, when, by whom, how many
  rows, and a link to the archived copy of the original file.
- **Undo an import** — the last import can be rolled back as a batch, because
  every row it created carries the same import id.

---

## Export

See [Excel Export](07-Excel-Export.md). Quick version:

- **Everything (.xlsx)** — one workbook with all the sheets. The
  "export the excel sheet to a file" button.
- **Balances only (.xlsx / .csv)** — the modern equivalent of the old sheet.
- **Transactions (.xlsx / .csv)** — for a date range.
- **One student's statement (.pdf / .xlsx)**.
- **Scheduled export** — write a chosen export to a folder automatically every
  day/week at a set time. Point it at a synced folder and the school always has a
  current copy without anyone remembering to press a button.

---

## Backup & Cloud

Covered in full in [Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md). The tab
provides:

- **Backup now**, and the list of existing backups with their size and age.
- **Restore from backup** — with a mandatory confirmation, and an automatic
  safety backup of the current state first.
- **Schedule** — how often, how many to keep.
- **Destinations** — local folder, USB, network share, Google Drive, OneDrive.
- **Encryption** — on/off and the passphrase (on by default for anything leaving
  the computer).
- **Open data folder**, **Verify database**, **Test destination**.
- The health indicator that also appears on the Café screen.

---

## Settings

| Setting | Default | Notes |
|---|---|---|
| Café name | *(from setup)* | Appears on exports and reports |
| Minimum balance | −10,00 kr | Café-wide floor. Cannot be positive. Change is logged |
| Per-student credit limits | Allowed | Set on the Students tab |
| Undo window | 15 minutes | How long counter staff can undo their own purchase |
| Max item rows per purchase | 10 | The café asked for 10; 1–20 is permitted |
| Large purchase warning | 500 kr | Asks for confirmation above this |
| Low balance warning | 20 kr | When the till starts showing amber |
| Admin PIN | *(from setup)* | Change requires the current PIN |
| Admin idle lock | 10 minutes | |
| Kiosk mode | Off | Hides the Admin button |
| Large text mode | Off | For small counter screens |
| Language | Swedish / English | Interface language; both shipped |
| Date & number format | Swedish | `12 sep 2026`, `40,00 kr` |
| Log retention | 30 days | Application logs, not the ledger |
| Audit log retention | Forever | Cannot be shortened from the UI on purpose |
| Check for updates | On | |

---

## History (audit log)

The complete, read-only record of everything that has happened. It cannot be
edited or deleted from anywhere in the program.

Each entry shows **when**, **who** (admin, or the Café session), **what**, and
**the before/after values** where relevant:

```
12 Sep 11:14  Café (till 1)   PURCHASE   Carl Jacobs  −40,00 kr  (Toast ×1, Juice ×2)   → 10,00 kr
12 Sep 11:16  Café (till 1)   REVERSAL   Carl Jacobs  +40,00 kr  (undo of #1043)        → 50,00 kr
12 Sep 12:01  Admin           PRICE      Toast  10,00 kr → 12,00 kr   reason: "new supplier"
12 Sep 12:30  Admin           ADJUST     Omar Haddad  +7,00 kr  reason: "Excel rounding, agreed with Omar"
12 Sep 16:00  System          BACKUP     ok, 2,4 MB → Google Drive
```

Filter by date, student, admin, or type. Export to Excel. This tab is the answer
to almost every "what happened here?" question, and it is what makes the system
defensible to a parent or the school board.
