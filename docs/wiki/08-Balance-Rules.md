# 08 — Balance Rules

The exact rules that govern money in the system. If two parts of this wiki ever
disagree, this page wins.

## How money is stored

- Every amount is a **whole number of öre** (`long`), never a decimal type and
  never a floating point number. 10 kr is `1000`. 12,50 kr is `1250`.
- Amounts are formatted for display as Swedish kronor: `12,50 kr`, thousands
  separated with a space: `1 250,00 kr`.
- No amount anywhere in the system can have more than two decimals; the import
  refuses such values rather than rounding them.
- Because all arithmetic is integer addition, **the totals always add up
  exactly**. There is no rounding drift, ever.

## How a balance is defined

```
balance(student) = SUM(amount) of all transactions for that student
```

That is the definition. A copy of the result is kept on the student row so the
till is fast, but that copy is:

- recalculated inside the same database transaction as every write,
- verified against the ledger on every program start,
- verified by **Admin → Backup & Cloud → Verify database**,
- and never editable by any part of the user interface.

If the cached balance and the ledger ever disagree, the program treats the
**ledger as the truth**, refuses to start, and reports the affected student. This
is the whole reason the system exists: a balance cannot be wrong without there
being a visible, findable reason.

## Transaction types

| Type | Sign | Created by | Can be reversed |
|---|---|---|---|
| `DEPOSIT` | + | Deposit dialog, bulk deposit | Yes |
| `PURCHASE` | − | Execute on the Café screen | Yes |
| `ADJUSTMENT` | + or − | Admin correction — **requires a reason** | Yes |
| `IMPORT` | + or − | Excel import (opening balances and import corrections) | Yes, as a batch |
| `REVERSAL` | opposite of its target | Undo, or admin reversal | No — reverse the reversal instead |

Every transaction row also stores: the timestamp, who did it, the balance after
it, and — where relevant — the transaction it reverses and the import it
belongs to.

## The minimum balance

The rule the café asked for:

> **A student's balance can never go below −10,00 kr.**

Precisely:

```
A PURCHASE is allowed only if:
    balance_before − purchase_total  ≥  effective_floor(student)

effective_floor(student) = student.credit_limit ?? settings.minimum_balance
                           (default −1000 öre = −10,00 kr)
```

Notes:

- The check is made **twice**: live on screen as the purchase is built (so the
  Execute button is already disabled), and again inside the database transaction
  at the moment of writing (so a race between two windows cannot slip past it).
- The floor applies to **purchases only**. A deposit can never be refused. An
  admin `ADJUSTMENT` can take a student below the floor — that is intentional,
  because a correction sometimes has to record reality — but the program warns
  clearly, and the student then cannot buy anything until they are back above it.
- **There is no override at the counter.** Not with a PIN, not with a
  confirmation, not with a manager key. The only way to allow a bigger debt is an
  admin changing that student's credit limit, which is a deliberate, logged act.
- `settings.minimum_balance` can be changed by an admin but **can never be
  positive** — the program rejects that, because a positive floor would block
  students who legitimately have 0 kr.
- A per-student `credit_limit` overrides the café default in **either**
  direction: `0` means "this student may never go negative", `−50,00 kr` means
  "this student is allowed a larger tab".

### "Can spend"

The number shown on the till next to the balance:

```
can_spend = balance − effective_floor
```

Carl at 50,00 kr with a −10,00 kr floor can spend **60,00 kr**. This is the
number staff actually need, so it is the one on screen.

## Price changes

Changing an item's price **never changes anything that already happened.**

- Each `transaction_lines` row stores the `unit_price` that was charged and the
  `item_name` as it read at the time.
- The `items` table holds the *current* price only; `price_history` holds every
  price with the period it was valid for.
- So a report of last month's sales uses last month's prices, automatically, with
  no special handling.

The same applies to renaming or archiving an item: history keeps the old name,
and reports over an old period show what the receipt would have said.

## Corrections and reversals

There are two ways to fix something, and no third way.

### Reversal — "that transaction should not have happened"

Writes an opposite transaction linked to the original. Both stay visible.
Used for: wrong student, wrong item, double charge, undo at the counter.

```
#1043  11:14  PURCHASE  Carl Jacobs  −40,00 kr
#1044  11:16  REVERSAL  Carl Jacobs  +40,00 kr   reverses #1043
```

A transaction can be reversed only **once**; the program blocks a second attempt.

### Adjustment — "the balance itself is wrong"

Writes an `ADJUSTMENT` with a **mandatory free-text reason**. Used for: the
opening balances from the old Excel sheet not matching what a student says,
writing off a small debt at the end of the year, correcting an amount agreed with
a parent.

Adjustments are listed separately in **Reports → Audit summary**, precisely
because they are the one place a person's judgement enters the numbers.

### What is never possible

- Editing a transaction after it is written.
- Deleting a transaction.
- Typing a balance directly.
- Changing the date of an existing transaction.
- Deleting a student who has any transactions (deactivate, or merge, instead).

## Refunds

A student returns an unopened juice. Two acceptable ways:

1. **Reverse** the purchase if it was the whole purchase and it just happened.
2. Otherwise write an `ADJUSTMENT` of +15,00 kr with the reason
   `Returned juice, transaction #1043`.

Do not "sell a negative item" — there is no such thing, and it would corrupt the
sales reports.

## Money and the end of the school year

The program does not do anything automatically at year end; that is a decision
for the school. The tools it gives you:

- **Reports → Inactive money** — balances belonging to students who have not
  bought anything in N months.
- **Students → Bulk deactivate** — hide a leaving class from the till while
  keeping their history.
- **Adjustment with a reason** — the correct way to zero out a written-off
  balance, leaving a permanent record of what was written off and why.

Whatever the school decides — refund, carry over, or donate to the café — do it
through adjustments with a clear reason, so the audit summary explains the whole
year in one page.
