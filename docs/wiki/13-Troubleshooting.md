# 13 — Troubleshooting

Things that go wrong, and what to do. Ordered by how often they happen.

---

## At the counter

### "Execute" is grey and I can't press it

Look at the message next to it. It is one of:

| Message | Fix |
|---|---|
| Choose a student | Pick someone in the student box |
| Add at least one item | Item row 1 is empty |
| Not enough money | The purchase breaks the −10 kr floor. Remove an item or take a deposit (`F4`) |
| This student is deactivated | An admin needs to reactivate them in Admin → Students |
| Quantity must be at least 1 | A row has an item but a blank quantity |

### A student isn't in the list

1. Check the spelling — search is forgiving but not psychic. Try just the first
   name, or the surname.
2. They may be **deactivated** (a leaver, or deactivated by mistake). Admin →
   Students → show inactive.
3. They may never have been imported. Use the drop-down's last row,
   **`+ Add "…" as a new student`**, and serve them now; tidy the record later.

### The balance on screen looks wrong

Do **not** correct it at the counter. Open **Admin → Students → Statement** for
that student and read the history — it will say exactly where the number came
from. Nine times out of ten the answer is a purchase the student forgot, or an
opening balance from the old Excel sheet that was already wrong. If a correction
is genuinely needed, an admin makes it with a written reason
([Balance Rules](08-Balance-Rules.md#corrections-and-reversals)).

### I charged the wrong student

Within 15 minutes: `Ctrl+Z`, or the Undo link on the confirmation, or the Undo
link in **Last purchases**. Then charge the right one.
After 15 minutes: an admin reverses it in **Admin → History**.

### The program is slow to find names

The search index is built at startup. If the café has thousands of students and
the computer is old, the first search after opening can take a moment; after that
it is instant. Persistent slowness usually means the database file is on a
network drive or in a sync folder — it should be on the local disk
(see [Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md)).

---

## Import

### "Could not read this row"

The preview shows the reason per row. Common ones:

| Reason | Fix |
|---|---|
| No name found | Blank row, or a row with only a number. Usually safe to skip |
| Amount not recognised | Something like `femtio` or `ca 50`. Fix it in the preview, or in the sheet |
| More than two decimals | e.g. `50,555`. The program will not guess a rounding. Fix the source |
| Looks like a total row | Correct behaviour — it is skipped. Untick if it really is a student called `Summa` |

### The wrong column was picked up

**Change column mapping** on the preview screen. The choice is remembered for
next time.

### The totals don't match my sheet

The preview header shows the file total and the resulting total. If they differ:

- Skipped rows — look at the skipped list; a "total row" that was really a
  student, or a blank row that had a name in a merged cell.
- Duplicate names inside the file, where you chose "keep the first".
- Students who already existed with a different balance and you chose
  "keep the program's value".

All of them are visible in the preview before you commit. Nothing is written
until you press Import.

### I imported the wrong file

**Admin → Import → Import history → Undo import.** The whole batch is reversed.
The reversal is itself visible in the history, which is intended.

---

## Backups and cloud

### The dot on the Café screen is amber or red

Click it. The Backup tab states the reason:

| Reason | Fix |
|---|---|
| Destination unreachable | The network share is down, or the USB stick is not plugged in |
| Token expired / access revoked | Someone removed the app's access in Google or Microsoft. Reconnect the destination |
| Disk full | Free space on the target, or reduce how many backups are kept |
| Verification failed | The last cloud backup could not be decrypted or failed its integrity check. **Take this seriously** — run **Backup now** and then **Verify last backup** |

The café keeps working in all of these cases.

### I've lost the encryption passphrase

The encrypted backups cannot be recovered. Nobody can decrypt them — that is the
point of the encryption. What you still have: the **live database** on the café
computer, and the **local, unencrypted** backups in
`C:\ProgramData\CashCafe\Backups\`. Immediately set a new passphrase, run
**Backup now**, and write the new one down somewhere two people can find it.

### Google or Microsoft asks me to sign in again

Normal — a refresh token expires if it is unused for a long time, or if the
school's policy requires re-consent. Reconnect the destination in Admin →
Backup & Cloud. Nothing is lost.

---

## The program itself

### Windows SmartScreen warns when installing

Expected while the build is unsigned: **More info → Run anyway**. Verify the file
against the SHA-256 published with the release first if you want to be careful.
This warning disappears once the school buys a code-signing certificate
([Architecture](10-Architecture.md#build-and-release)).

### "Another copy of Cash Café is already running"

Only one instance may open the database. Check the taskbar. If nothing is
visible, end `CashCafe.exe` in Task Manager and start again.

### The program refuses to start: "balances do not match the ledger"

The program has detected that a cached balance disagrees with the sum of that
student's transactions, and it will not run with numbers it does not trust. This
means the database file was modified outside the program, or a disk problem
corrupted it.

1. Note the student named in the message.
2. **Do not delete anything.**
3. Run `CashCafe.exe --repair`, which recomputes every cached balance from the
   ledger, after taking a backup first. In almost every case this is the fix, and
   the ledger — the actual record — was never damaged.
4. If that fails, restore the most recent backup.

### The program refuses to start: "database is newer than this program"

Someone restored a backup made by a later version, or an update was rolled back.
Install the current version again; the data is fine.

### The database is corrupt

Usually caused by the database file sitting in a Google Drive / OneDrive /
Dropbox folder, or by a power cut on a machine with disk write caching. In order:

1. Take a copy of `C:\ProgramData\CashCafe\` before doing anything.
2. **Admin → Backup & Cloud → Restore** the latest good backup.
3. Move the database off the sync folder if that is where it was
   ([why](11-Cloud-Sync-and-Backup.md#backup-vs-sync--and-why-the-difference-matters)).
4. Re-enter any sales made since the backup — the ledger design means this is
   just repeating the purchases, not recalculating balances.

### I lost the admin PIN

The PIN is stored as a hash and cannot be read back. Recovery requires local
administrator rights on the computer, which is the intended barrier:

1. Sign in to Windows as a local administrator.
2. Run `CashCafe.exe --reset-pin` from an elevated command prompt.
3. It asks for confirmation, **writes the reset to the audit log**, and lets you
   set a new PIN.

The reset cannot be hidden: the audit log entry is permanent, so a student who
somehow managed it would leave a record.

---

## Getting help

Before asking, collect:

- The program version (**Admin → Settings → About**).
- The log for the day: `C:\ProgramData\CashCafe\Logs\cashcafe-YYYYMMDD.log`.
  Logs contain student **ids**, not names — safe to share.
- What you were doing and what you expected.

Then open an issue in this repository. **Never attach the database file or a
backup to an issue** — those contain students' names.
