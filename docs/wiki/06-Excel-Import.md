# 06 — Excel Import

Getting the old sheet into the program. This is the one part of the system that
has to cope with human-made spreadsheets, so it is deliberately forgiving —
and deliberately shows you everything before it writes anything.

**Nothing is ever written until you press Import on the preview screen.**

## Supported files

| Format | Extension | Notes |
|---|---|---|
| Excel workbook | `.xlsx` | Preferred. Read with ClosedXML |
| Excel 97–2003 | `.xls` | Detected, and you are asked to re-save as `.xlsx` (the old format is not read directly) |
| Excel macro workbook | `.xlsm` | Read; macros are ignored and never executed |
| Comma/semicolon separated | `.csv` | Encoding auto-detected (UTF-8, UTF-8 BOM, Windows-1252). Separator auto-detected (`,` `;` tab) |
| OpenDocument | `.ods` | **Planned**, not in version 1 |

If a workbook has several sheets, you choose which one. The program suggests the
sheet with the most name-like rows.

## What can be imported

| Import type | Creates | Used for |
|---|---|---|
| **Balances** | Students + one opening deposit each | First-time setup from the old sheet |
| **Students only** | Students with 0 kr | A class list from the school |
| **Items** | Items and prices | Setting up the menu |
| **Deposits** | Deposit transactions | A weekly Swish report |

The rest of this page is about **balances**, the important one.

---

## Step 1 — Pick the file

Drag the file onto the window, or **Admin → Import → From Excel**.

The program immediately takes a copy of the original file into
`C:\ProgramData\CashCafe\Imports\2026-09-12T09-14-02_gamla-listan.xlsx`, so the
source of every number can always be traced back. That copy is never modified.

## Step 2 — Layout detection

The program looks at the first 50 rows and decides which of these it is
looking at. You can always override the decision.

### Layout A — separate columns (most common)

```
        A                    B         C
1   Namn                 Klass     Saldo
2   Carl Jacobs          9B        50
3   Astrid Lindqvist     8A        12,50
4   Omar Haddad          9B        -10
```

Headers are recognised in Swedish and English:

| Meaning | Recognised headers |
|---|---|
| Name | `namn`, `elev`, `student`, `name`, `full name`, `för- och efternamn` |
| First name | `förnamn`, `first name` |
| Last name | `efternamn`, `last name`, `surname` |
| Class | `klass`, `class`, `grupp`, `group` |
| Balance | `saldo`, `belopp`, `balance`, `summa`, `kr`, `kronor`, `amount`, `tillgodo` |

If first and last name are in separate columns they are joined with a space.
Header matching ignores case, accents, punctuation and surrounding spaces.

### Layout B — one column, name and amount together

This is the layout the café's sheet actually uses:

```
        A
1   Carl Jacobs 50 kr
2   Astrid Lindqvist 12,50 kr
3   Omar Haddad -10 kr
```

Each cell is split with the rule: **the amount is the trailing number**, with an
optional currency word after it. Formally:

```
^(?<name>.+?)[\s.:\-–]*(?<amount>-?\(?\d{1,3}(?:[ \u00A0]?\d{3})*(?:[.,]\d{1,2})?\)?)\s*(kr|sek|:-|kronor)?\s*$
```

In words: everything up to the last number is the name; the number is the
balance; `kr`, `SEK`, `:-` or `kronor` after it is ignored. Examples of what this
correctly reads:

| Cell | Name | Balance |
|---|---|---|
| `Carl Jacobs 50 kr` | Carl Jacobs | 50,00 |
| `Carl Jacobs 50` | Carl Jacobs | 50,00 |
| `Carl Jacobs: 50:-` | Carl Jacobs | 50,00 |
| `Carl Jacobs -10 kr` | Carl Jacobs | −10,00 |
| `Carl Jacobs (10) kr` | Carl Jacobs | −10,00 (accounting negative) |
| `Carl Jacobs 1 250,50 kr` | Carl Jacobs | 1 250,50 |
| `Carl Jacobs 12.50` | Carl Jacobs | 12,50 |
| `Anna 9B 40 kr` | Anna 9B *(flagged — looks like it contains a class)* | 40,00 |

Anything the rule cannot split is listed as an error row with the reason, and you
can fix it inline in the preview.

### Layout C — a running Excel ledger

Some sheets keep a column per day, or a running list of +/− amounts:

```
        A               B      C      D      E
1   Namn            Start   12/9   13/9   Saldo
2   Carl Jacobs        50    -10    -15     25
```

The program detects a **Saldo/Balance** column at the far right and imports that,
ignoring the working columns. If there is no such column it offers to **sum the
numeric columns** instead, showing you the result per row before you commit.

## Step 3 — Column mapping

Whatever was detected, you get a mapping screen:

```
Column A  "Namn"     →  [ Name        ▾ ]
Column B  "Klass"    →  [ Class       ▾ ]
Column C  "Saldo"    →  [ Balance     ▾ ]
Column D  "Anteckn." →  [ Ignore      ▾ ]

Header row:      [ 1 ▾ ]     First data row: [ 2 ▾ ]
Sheet:           [ Blad1 ▾ ]
```

Mappings you choose are remembered per file name, so a repeat import of the same
sheet needs no setup.

## Step 4 — How numbers are read

Swedish spreadsheets are messy in specific, predictable ways. All of these are
handled:

| Input | Read as | Rule |
|---|---|---|
| `50` | 50,00 kr | Plain number |
| `50,5` | 50,50 kr | Comma decimal separator |
| `50.5` | 50,50 kr | Dot decimal separator |
| `1 250` / `1 250` | 1250,00 kr | Space or non-breaking space thousands separator |
| `1.250,50` | 1250,50 kr | Dot thousands + comma decimal |
| `50 kr`, `50kr`, `50 SEK`, `50:-` | 50,00 kr | Currency suffix stripped |
| `-10`, `−10` (en dash), `(10)` | −10,00 kr | Three ways of writing negative |
| `` (empty) | 0,00 kr | Imported as zero, and flagged |
| `50,555` | **Error** | More than 2 decimals — must be fixed, never silently rounded |
| `femtio` | **Error** | Not a number |
| A real Excel number cell | Exact value | Read as the underlying value, not the displayed text, so formatting cannot lie to you |
| An Excel **formula** cell | Its cached result | With a note in the preview that it was a formula |

Everything is converted to **whole öre** immediately (50,00 kr → `5000`). No
floating point arithmetic is used at any point.

## Step 5 — The preview

The most important screen in the import. It is a full table of what *would*
happen, with a status per row:

| Status | Meaning | Default action |
|---|---|---|
| **New student** | This name does not exist yet | Create, with the balance as an opening deposit |
| **Exists — same balance** | Already there, numbers agree | Skip |
| **Exists — different balance** | Already there, numbers disagree | **Ask** — see below |
| **Possible duplicate** | Very similar to an existing name (`Carl Jacobs` vs `Carl Jacobsson`) | Ask — merge or create separate |
| **Duplicate inside the file** | The same name twice in this sheet | Ask — sum the two, keep the first, or keep both |
| **Skipped — no name** | Blank row | Skip |
| **Skipped — looks like a total** | Row starts with `Total`, `Summa`, `S:a`, or is the last row and equals the sum of the others | Skip, but shown so you can override |
| **Error** | The cell could not be read | Must be fixed or excluded before Import is enabled |

The header of the preview shows the totals, which is the single best check that
the import is right:

```
82 rows read · 79 students · 3 skipped · 0 errors
Total balance in file:      3 480,50 kr
Total balance after import: 3 480,50 kr
```

Rows can be edited directly in the preview (fix a name, fix an amount, change
the action) before importing.

### When a student already exists with a different balance

For each such row you choose one of:

| Choice | Effect |
|---|---|
| **Set to the file's value** (default) | Writes an `ADJUSTMENT` for the difference, with the reason `Import from <filename>`. The balance ends up matching the sheet, and the history explains why |
| **Keep the program's value** | Nothing written |
| **Add the file's value** | Treats the number as a top-up rather than a total. Used when importing a list of *new* deposits |

There is a **Apply to all** control so 60 rows do not need 60 clicks.

### Name matching

An existing student is matched by, in order:

1. Exact match on the normalised name (lower case, accents folded, extra spaces
   and punctuation removed).
2. Same normalised name with first and last name swapped
   (`Jacobs Carl` = `Carl Jacobs`).
3. Fuzzy match above 92 % similarity (Jaro-Winkler) — **never applied
   automatically**; always shown as "Possible duplicate" for a human to decide.

## Step 6 — Import

Pressing **Import** does the whole thing in **one database transaction**. If
anything fails, nothing at all is written.

Each created or changed row is tagged with an **import id**, which means:

- The import history shows exactly what this run did.
- **Undo import** rolls the entire batch back — as reversals, so even the undo is
  visible in the history.

Opening balances are written as a deposit of type `IMPORT` dated with the import,
with the reference `Opening balance from <filename>, row N`. In other words, even
the very first number in the system can be traced back to a specific cell in a
specific archived file.

## After the import

Check these three things:

1. **Admin → Reports → Balance list** — the total matches the total in the
   preview and in your sheet.
2. **Admin → Students** — sort by name and look for near-duplicates the fuzzy
   matcher flagged; **Merge** any real ones.
3. **Admin → Export → Balances only** and open it next to the old sheet if you
   want a line-by-line comparison.

Then stop using the Excel sheet. Keep the file, but do not edit it: from now on
the program is the record, and the sheet is history. Re-importing an edited old
sheet later is the one reliable way to reintroduce the errors this system exists
to remove.
