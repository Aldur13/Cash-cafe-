# 01 — Overview

## The problem

The café's balances live in an Excel sheet that is edited by hand:

```
Carl Jacobs   50 kr
```

Carl buys a toast (10 kr). Someone opens the sheet, works out 50 − 10, and types:

```
Carl Jacobs   40 kr
```

That single manual step is where everything goes wrong:

1. **The maths is done by a human**, often while a queue is waiting. 50 − 10 = 45
   happens more often than anyone admits.
2. **The old value is destroyed.** Once `50` is overwritten with `40`, there is no
   record that a toast was ever sold. If Carl says "I only bought one thing today",
   nobody can check.
3. **There is no history**, so there is no way to answer "how much did we sell
   today?", "how many toasts do we need to order?", or "why is this student at
   -85 kr?".
4. **Two people cannot work at once.** Excel locks the file, or worse, silently
   creates `sheet (1).xlsx` and the two versions diverge.
5. **Nothing stops a large debt.** A student can quietly reach -300 kr, and the
   café has no way to notice until it is a problem for a parent.
6. **Everything is one wrong keystroke away from disaster** — a sorted column, a
   dragged cell, a deleted row.

## The solution

Cash Café keeps the same idea (each student has a balance in kronor) but changes
*how the number is produced*.

> **The balance is never typed. It is calculated.**

Every event is a row in a permanent, append-only list called the **ledger**:

| When | Student | What | Amount |
|---|---|---|---|
| 12 Sep 08:02 | Carl Jacobs | Deposit (Swish) | +50,00 kr |
| 12 Sep 11:14 | Carl Jacobs | Purchase: Toast | −10,00 kr |

Carl's balance is the sum of those rows: **40,00 kr**. Nobody typed 40. It cannot
be 45 by accident. And if you want to know why it is 40, the two rows are right
there, with a timestamp and the name of the person who served him.

Fixing a mistake never means editing history. It means adding a **reversal** row,
which is also permanent and also visible. The history is therefore always a true
account of what happened, including the mistakes.

## Who uses it

| Role | What they do | How they get in |
|---|---|---|
| **Café staff** (students/volunteers at the counter) | Sell items, take deposits, undo their own last purchase | Just open the program — no login for the Café screen |
| **Café administrator** (the responsible teacher) | Prices, items, student list, reports, import/export, backups, corrections | Admin PIN |
| **School IT** | Install, backup destination, restore, and — if the site is used — DNS, a certificate and the app registration | Windows admin rights on the café PC |
| **Students** | See their own balance and how busy the café is, on the optional website | Their existing school Google or Microsoft account |

The Café screen is deliberately **not** behind a login. A queue of hungry students
is not the moment for a password, and staff change constantly. Everything that
could cause harm — changing a price, changing a balance directly, deleting
anything — is behind the admin PIN, and the audit log records which
Café *session* performed each sale (see [Data Model](09-Data-Model.md)).

## What is in scope right now

**In scope (version 1):**

- Import the existing Excel sheet.
- Sell items to students at a counter (up to 10 items per purchase).
- Manual deposits (someone Swishes the café, staff records it).
- The -10 kr floor.
- Admin panel: items, prices, students, reports, audit log.
- Excel/CSV export and import.
- Local backup + optional encrypted backup to Google Drive / OneDrive / a network folder.
- An optional [student website](18-Student-Site.md) on the school network: a student signs
  in with their school account to see their own balance and how busy the café is, and
  staff set the busyness with a slider.

**Explicitly out of scope for now:**

- Any live connection to Swish or a bank. Deposits are entered by hand.
  (See [Roadmap](15-Roadmap.md) for how this would be added.)
- A **guardian**-facing app or website. (The student site is in scope; extending it to
  parents needs guardian identity checks and its own assessment.)
- Reaching the student site from outside the school network.
- Card readers, barcode scanners, receipt printers (the design leaves room — see Roadmap).
- Several tills selling at the same time from different computers.

## Design principles

These are the rules the program is built on. If a future change conflicts with
one of them, the change is wrong.

1. **Money is never stored as a floating point number.** Everything is whole
   öre (`long`). 10 kr is `1000`.
2. **The ledger is append-only.** No `UPDATE`, no `DELETE` on transaction rows, ever.
3. **A balance is derived, not stored** (a cached copy exists for speed, but it is
   always recomputable from the ledger, and the program verifies this on startup).
4. **It works with the network unplugged.** Cloud is a backup, never a dependency.
5. **The counter flow must be usable one-handed, keyboard-only, in under 5 seconds.**
6. **Nothing about a student is stored that the café doesn't need**
   — a name, an optional class, an optional note. Nothing else. See
   [Security & Privacy](12-Security-and-Privacy.md).
