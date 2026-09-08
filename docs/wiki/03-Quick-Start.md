# 03 — Quick Start

Your first day with Cash Café, in order.

---

## 1. Get the students in

**Admin → Import → From Excel**, or the button on the first-run wizard.

Pick your old sheet. The program shows you a preview like this:

```
  Row  Name                Balance    Status
   2   Carl Jacobs         50,00 kr   New student
   3   Astrid Lindqvist    12,50 kr   New student
   4   Omar Haddad        -10,00 kr   New student
   5   (blank)                        Skipped — no name
   6   TOTAL              340,00 kr   Skipped — looks like a total row
```

Read the preview. Nothing is written until you press **Import**. If a column was
detected wrongly, use **Change column mapping**. Full detail:
[Excel Import](06-Excel-Import.md).

## 2. Get the items in

**Admin → Items → New item**. For each thing the café sells:

| Field | Example |
|---|---|
| Name | `Toast` |
| Price | `10,00` |
| Category (optional) | `Food` |
| Shortcut key (optional) | `T` |

Do this for everything: Toast 10 kr, Juice 15 kr, Bulle 12 kr, Kaffe 10 kr…
You can add more at any time, and change prices at any time — old sales keep the
price they were sold at ([Balance Rules](08-Balance-Rules.md#price-changes)).

## 3. Sell something

Go to the **Café** screen (the program opens here).

1. Click the **Student** box, or just start typing — the box has focus already.
   Type `car` → the list shows `Carl Jacobs — 50,00 kr`. Press `Enter`.
2. The **Items** area shows one empty row. Type `to` → `Toast — 10,00 kr`.
   Press `Enter`.
3. A second empty row opens by itself. If Carl is only buying a toast, ignore it.
4. The bottom of the screen shows:
   ```
   Total          10,00 kr
   Balance now    50,00 kr  →  40,00 kr
   ```
5. Press `Enter` again (or click **Execute**).

A green confirmation appears for two seconds, the screen clears, and it is ready
for the next student. That's the whole job.

## 4. Take a deposit

A parent Swishes 100 kr for Astrid.

**Deposit** (button on the Café screen, or `F4`) → pick Astrid → type `100` →
choose method `Swish` → optionally paste the Swish reference into the note →
**Save**.

Astrid's balance goes up by 100 kr and the deposit is in the history with the
reference, so it can be matched against the café's Swish report later.

## 5. Check the day

**Admin → Today**:

- Money taken in today (deposits).
- Value sold today, per item.
- Every purchase, with student and time.
- Students currently in the red.

## 6. Close up

Nothing to do — the program saves continuously and takes its own backup.
If you want a copy for the folder: **Admin → Export → Everything (.xlsx)**.

---

## The five things worth knowing

1. **`Enter` moves you forward, `Esc` clears the whole purchase.** You can serve a
   queue without touching the mouse ([Keyboard Shortcuts](14-Keyboard-Shortcuts.md)).
2. **You cannot push someone below -10 kr.** The Execute button greys out and
   says why. Take cash or ask for a Swish instead.
3. **`Ctrl+Z` undoes the last purchase** you made, for 15 minutes afterwards. It
   is recorded as a reversal, not erased.
4. **Never edit a balance directly.** There is no field for it. Use a deposit, or
   an admin correction with a reason.
5. **The database file is the café.** `C:\ProgramData\CashCafe\cashcafe.db`.
   Make sure backups are switched on ([Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md)).
