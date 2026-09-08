# 07 — Excel Export

Getting data back out — for the school's records, for a parent, for a
spreadsheet-minded teacher, or as a plain-text safety net that will still be
readable in twenty years when this program is gone.

**Admin → Export.**

## Formats

| Format | When to use it |
|---|---|
| `.xlsx` | Normal case. Formatted, multiple sheets, opens in Excel and Google Sheets |
| `.csv` | Feeding another system, or long-term archiving. UTF-8 with BOM, `;` separator (what Swedish Excel expects), `,` decimal |
| `.pdf` | A student statement or a printed report |
| `.json` | A complete machine-readable dump, for migrating to another system later |

Every export writes a header block with the café name, the export date, the
program version, the filters used, and a row count — so a printed or emailed file
explains itself.

## Export types

### Everything (`.xlsx`)

One workbook, one sheet per table. This is the "export the excel sheet to a
file" button and the file to keep as an archive.

**Sheet `Students`**

| Column | Example |
|---|---|
| `StudentId` | `41` |
| `Name` | `Carl Jacobs` |
| `Class` | `9B` |
| `Balance` | `40,00` |
| `CreditLimit` | `-10,00` |
| `Active` | `Ja` |
| `Created` | `2026-09-12 09:14` |
| `LastActivity` | `2026-09-12 11:14` |
| `Note` | |

**Sheet `Transactions`** — one row per transaction

| Column | Example |
|---|---|
| `TransactionId` | `1043` |
| `DateTime` | `2026-09-12 11:14:07` |
| `StudentId` / `StudentName` | `41` / `Carl Jacobs` |
| `Type` | `PURCHASE`, `DEPOSIT`, `ADJUSTMENT`, `REVERSAL`, `IMPORT` |
| `Amount` | `-40,00` |
| `BalanceAfter` | `10,00` |
| `Method` | `Swish` (deposits only) |
| `Reference` | |
| `Reason` | (adjustments and reversals) |
| `ReversesTransactionId` | |
| `Operator` | `Café (till 1)` / `Admin` |
| `ImportId` | |

**Sheet `TransactionLines`** — one row per item sold

| Column | Example |
|---|---|
| `TransactionId` | `1043` |
| `LineNo` | `2` |
| `ItemId` / `ItemName` | `7` / `Juice` |
| `UnitPrice` | `15,00` |
| `Quantity` | `2` |
| `LineTotal` | `30,00` |

`ItemName` and `UnitPrice` are the values **as they were at the time of sale**,
not today's values.

**Sheet `Items`** — id, name, category, current price, shortcut, available, archived.
**Sheet `PriceHistory`** — item, price, valid from, valid to, changed by.
**Sheet `Deposits`** — the deposit subset of transactions, with methods and references.
**Sheet `AuditLog`** — the full audit trail.
**Sheet `Summary`** — totals, counts, and the reconciliation check
(opening + deposits − sales = closing), so the workbook proves its own consistency.

### Balances only

Two columns, `Name` and `Balance`, plus `Class` if used. This is the direct
replacement for the old sheet — the file to send to someone who just wants to
see who has what. Optionally sorted by name, class or balance.

There is also a **"legacy format"** tick box that produces the single-column
`Carl Jacobs 40 kr` layout, for anyone who still wants to read it the old way.

### Transactions (date range)

Everything that happened between two dates. Filterable by student, class, item
and type.

### Student statement

One student's complete history as a `.pdf` (printable, with the café's name at
the top) or `.xlsx`. This is what to give a parent who asks where the money went.

### Day report

Exactly what the **Today** tab shows, as a file: totals, per item, per student,
per class, plus the transaction list.

## Scheduled export

**Admin → Export → Schedule.**

| Setting | Options |
|---|---|
| What | Any of the export types above |
| When | Every day at a time / every week on a day / on closing the program |
| Where | Any folder — including a Google Drive or OneDrive sync folder, or a network share |
| File naming | `CashCafe_{type}_{yyyy-MM-dd}.xlsx` (pattern editable) |
| Keep | Last N files, older ones deleted |

Pointing a scheduled export at a synced folder is the simplest possible off-site
copy: the school always has a current, human-readable spreadsheet of the café's
state, with no manual step. Note that a scheduled export of *personal* data
lands in that folder in the clear — read
[Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md) and
[Security & Privacy](12-Security-and-Privacy.md) before choosing where it goes.

## Notes on the files

- Money is written as a **number formatted as `# ##0,00`**, not as text, so Excel
  can sum a column without anyone reformatting anything.
- Dates are written as real date/time values in ISO order (`2026-09-12 11:14`),
  which sorts correctly everywhere.
- Ids are included in every export. They are what make it possible to
  re-import or cross-reference later without relying on names.
- Nothing that identifies a student beyond name and class is ever exported,
  because nothing else is stored.
- Exports are **read-only operations**: an export never changes any data, and a
  failed export cannot corrupt anything.

### Menu only (`.xlsx`)

**Admin → Items → Export menu to Excel…** — one sheet, `Varor`, with exactly the
columns the menu importer above reads back: `Namn`, `Kategori`, `Pris`,
`Kortkommando`, `Till salu`. Edit prices in Excel, save, and import the same
file back in — it round-trips.

Archived items are left out on purpose: there is no way to bring one back
through import, so including it in an export meant to be re-imported would be
a promise the import cannot keep.
