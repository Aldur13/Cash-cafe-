# 04 — The Café Screen (the till)

This is the screen the café runs on all day. It has one job: turn
"Carl is buying a toast and a juice" into a correct balance, in a few seconds,
without a mouse if you prefer.

## Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│  CASH CAFÉ                       Skolan Café          Fri 12 Sep  11:14  │
│                                            [ Deposit F4 ] [ Admin F9 ]   │
├──────────────────────────────────────────────────────────────────────────┤
│  STUDENT                                                                 │
│  ┌────────────────────────────────────┐    ┌──────────────────────────┐  │
│  │ car|                            🔍 │    │  Carl Jacobs             │  │
│  ├────────────────────────────────────┤    │  9B                      │  │
│  │ ▸ Carl Jacobs        9B   50,00 kr │    │                          │  │
│  │   Carla Nyström      7A   12,00 kr │    │  Balance    50,00 kr     │  │
│  │   Oscar Lindh        9B    3,50 kr │    │  Can spend  60,00 kr     │  │
│  └────────────────────────────────────┘    └──────────────────────────┘  │
├──────────────────────────────────────────────────────────────────────────┤
│  THEY BUY                                                                │
│   1 │ Toast                    │ ×│1│ │  10,00 kr │        10,00 kr │ ✕ │
│   2 │ Juice                    │ ×│2│ │  15,00 kr │        30,00 kr │ ✕ │
│   3 │ type to add an item…     │  │ │ │           │                 │   │
│                                                        (3 of 10 rows)    │
├──────────────────────────────────────────────────────────────────────────┤
│  Items 3          Total  40,00 kr                                        │
│  Balance  50,00 kr  →  10,00 kr                                          │
│                                                                          │
│  [ Clear  Esc ]                                    [   EXECUTE  ⏎   ]    │
└──────────────────────────────────────────────────────────────────────────┘
```

Everything below is what each part does, exactly.

---

## 1. The student box (live search)

A single text box with a drop-down list that filters **as you type**, from the
first keystroke. There is no "Search" button.

### How the search works

Typing filters the student list with the following rules, applied in this order.
The list is re-sorted on every keystroke and shows at most **8** results.

| Rank | Match type | Example: typing `car` finds |
|---|---|---|
| 1 | Exact match on full name | — |
| 2 | Any **word** in the name starts with what you typed | **Car**l Jacobs |
| 3 | Initials match (`cj` → **C**arl **J**acobs) | — |
| 4 | The name **contains** what you typed | Os**car** Lindh |
| 5 | Fuzzy — one typo away (Levenshtein distance ≤ 1 per word, only for 4+ characters) | `carl jacbos` → Carl Jacobs |

Within the same rank, students who have been served **most recently** come first,
then alphabetical order. In practice the regulars end up at the top by themselves.

Other details:

- **Case and accents are ignored.** `asa` finds `Åsa`, `oe` finds `Ö`
  (Swedish folding: å→a, ä→a, ö→o, é→e).
- **Multiple words** are matched independently and in any order:
  `jac carl` finds Carl Jacobs.
- Only **active** students are searched. Someone who has left the school is
  deactivated in Admin and disappears from here, without their history being deleted.
- The search runs against an in-memory index, so it stays instant with thousands
  of students — no database query per keystroke.

### What each row shows

```
Carl Jacobs        9B        50,00 kr
```
Name, class (if set), and current balance. The balance is **colour coded**:

| Colour | Meaning |
|---|---|
| Black | Balance ≥ 20 kr |
| Amber | Balance between 0 and 20 kr — running low |
| Red | Balance below 0 kr — in debt |

### Selecting

`↑` / `↓` to move, `Enter` or click to select. Once selected:

- The student's card appears on the right with **Balance** and **Can spend**
  (= balance + 10 kr, the amount they can still spend before hitting the floor).
- Focus jumps automatically to item row 1.
- A warning banner appears if the student is already negative, or is flagged
  (Admin can put a note on a student, e.g. "cash only — see teacher").

Press `Backspace` in an empty item row, or click the student's name, to go back
and pick someone else. Nothing is written to the database until Execute.

### If the student isn't there

The drop-down's last row is always **`+ Add "carl jacobs" as a new student`**.
Choosing it opens a small dialog (name, class, starting balance 0 kr) so a new
student can be served without leaving the counter. Creating a student this way
is recorded in the audit log, and admins can see a list of counter-created
students in **Admin → Students → Recently added** to tidy up spellings.

---

## 2. The item rows

The purchase is built from **up to 10 rows**. Row 1 is there from the start.

**A new empty row appears automatically as soon as you fill the one above it** —
this is the "opens another menu if the student wants to buy more items" behaviour.
You never have to press "Add row". After row 10 no further row opens and a small
note reads *"Maximum 10 items per purchase"*; take the rest as a second purchase.

### Each row has

| Column | What it does |
|---|---|
| **#** | Row number, 1–10 |
| **Item** | A drop-down of everything on sale — click, or use the keyboard (below) |
| **Qty** | Quantity, 1–99. Defaults to 1. `+`/`-` buttons, or type a number |
| **Price** | The item's current unit price. Filled in **the moment you select the item** — read-only here |
| **Line total** | Price × quantity |
| **✕** | Remove this row (rows below shift up) |

### The item drop-down

- Every row shows **name** on the left and **price** on the right, so the price
  is visible before you even pick anything, not just after.
- Only items marked **available** appear. An item that is sold out for the day is
  toggled off in Admin and vanishes from the till, without being deleted.
- Items with a **shortcut key** can still be added the fast way: press that key
  while an empty row has focus and nothing selected — `T` → Toast, with no need
  to open the drop-down at all.
- Typing a letter with the drop-down closed jumps straight to the first item
  whose name starts with it (the ordinary Windows combo-box behaviour), so a
  long menu does not have to be scrolled by hand.
- There is no "Other / custom amount" entry in the drop-down yet — a one-off
  charge with no fixed price is not currently something the till screen can
  ring up. See [Roadmap](15-Roadmap.md).

### Quantity vs. more rows

Both work. Two toasts can be one row with quantity 2 or two rows with quantity 1;
the ledger stores them identically (see [Data Model](09-Data-Model.md)). The
10-row limit is a limit on *rows*, not on items — 10 rows × 99 qty is allowed,
though a confirmation appears for any purchase over 500 kr.

---

## 3. The summary bar

Recalculated on every change, before anything is saved:

```
Items 3          Total  40,00 kr
Balance  50,00 kr  →  10,00 kr
```

- **Items** — the sum of the quantities.
- **Total** — the sum of the line totals.
- **Balance now → after** — the student's balance before and after this purchase.
  The "after" figure is colour coded with the same rules as the search list, so
  the person at the counter can see at a glance that they are about to push
  someone into the red.

---

## 4. Execute

The **Execute** button (or `Enter` from any row, or `Ctrl+Enter` anywhere) commits
the purchase.

### Before it will run, all of these must be true

| Check | If it fails |
|---|---|
| A student is selected | Button disabled, "Choose a student" |
| At least one row has an item | Button disabled, "Add at least one item" |
| Every filled row has a quantity ≥ 1 | Button disabled, the bad row is outlined red |
| `balance − total ≥ minimum balance` (default −10,00 kr) | **Button disabled and red**, with the message below |
| The student is active | Button disabled, "This student is deactivated" |

### The -10 kr message

If the purchase would take Carl to -12,50 kr, the button turns red and reads:

```
✋ Not enough money
   Carl Jacobs has 50,00 kr and can spend 60,00 kr before reaching the
   -10,00 kr limit. This purchase is 72,50 kr — 12,50 kr too much.

   Remove an item, or take a deposit first  [ Deposit F4 ]
```

There is **no override on the Café screen** — not with the admin PIN, not with a
confirmation. This is deliberate: the floor is the point. An administrator who
genuinely needs to allow a larger debt raises the limit for that one student in
**Admin → Students → Credit limit**, which is logged. See
[Balance Rules](08-Balance-Rules.md#the-minimum-balance).

### What happens on Execute

All of this happens inside a **single database transaction** — either all of it
is written or none of it is:

1. Prices are re-read from the database and compared with what was on screen. If
   an admin changed a price on another window in the meantime, the purchase is
   stopped and the screen refreshes rather than charging the wrong amount.
2. The balance is re-checked against the floor with the fresh figures.
3. One `transactions` row is written (type `PURCHASE`) with the total.
4. One `transaction_lines` row per item row, each storing item id, item **name as
   it was**, unit price **as it was**, and quantity.
5. The student's cached balance is updated.
6. An `audit_log` row is written.

Then the screen shows a green toast for 2 seconds:

```
✓ Carl Jacobs — 40,00 kr paid. New balance 10,00 kr.        [ Undo Ctrl+Z ]
```

…clears itself, and puts focus back in the student box. Total time for an
experienced user, keyboard only: about **4 seconds**.

If anything fails, nothing at all is written, a red banner explains why, and the
purchase stays on screen so it can be retried.

---

## 5. Undo

`Ctrl+Z`, the **Undo** link on the confirmation toast, or the **Last purchases**
list at the bottom of the screen.

- Available for **15 minutes** after a purchase (configurable in Admin), and only
  for purchases made from this Café session.
- Undo **does not delete anything.** It writes a new `REVERSAL` transaction that
  cancels the original out, links the two together, and restores the balance.
  Both rows stay in the history and in reports for the day.
- The reason is recorded as `Undo at counter`; an optional note can be typed.
- Older than 15 minutes, or made by someone else? An administrator can reverse
  any transaction from **Admin → History**, with a mandatory reason.

## 6. The rest of the screen

- **Last purchases** — a short list of the last 10 purchases from this session,
  each with an Undo link. Useful when a student comes back to say "that wasn't me".
- **Deposit `F4`** — opens the deposit dialog ([Admin Panel → Deposits](05-Admin-Panel.md#deposits)).
  This is on the Café screen on purpose, because a parent Swishing money is a
  counter event, not an admin task. It is limited to positive amounts and is
  fully logged.
- **Admin `F9`** — asks for the PIN, then opens the [Admin Panel](05-Admin-Panel.md).
- **Clock and café name** — a large clock helps with "was that before or after
  the break?" questions.
- **Offline/backup indicator** — a small dot: green = last backup succeeded,
  amber = backup overdue, red = last backup failed. Clicking it opens
  [Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md) settings.

## 7. Accessibility and hardware

- Every control is reachable with `Tab` and has a screen-reader label.
- The layout works on a touch screen: all targets are at least 44 × 44 px, and
  there is a **Large text** mode in Admin → Settings for a small counter monitor.
- The window can run full-screen (`F11`) to keep the desktop out of the way.
- **Kiosk mode** (Admin → Settings) hides the Admin button entirely; it can then
  only be opened with `Ctrl+Shift+F9` and the PIN.
