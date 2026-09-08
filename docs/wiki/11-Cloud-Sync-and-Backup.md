# 11 — Cloud Sync & Backup

How the café's data is protected against a broken laptop, a stolen laptop, a
deleted file, and a mistake — and how it can be copied to Google Drive, OneDrive
or a school server without that becoming a security problem.

Read together with [Security & Privacy](12-Security-and-Privacy.md), which is the
document written for the school board.

---

## The short version

- The real data is **one file** on **one school computer**:
  `C:\ProgramData\CashCafe\cashcafe.db`.
- The program takes **automatic local backups** — every night and on close.
- Backups can *also* be copied to a **cloud destination you choose**.
- Anything that leaves the computer is **encrypted first** (AES-256) with a
  passphrase the school holds. The cloud provider stores an unreadable blob.
- Cloud is **backup, never operation**. Unplug the internet and the café works
  exactly the same.
- **This is a one-way copy, not live sync.** Two computers do not share a
  database. Why, and what would be needed for that, is explained below.

---

## Backup vs. sync — and why the difference matters

**Backup (what this program does):** at a moment in time, the whole database is
copied to a second place. If the computer dies you take yesterday's copy and
carry on. One computer is always the single source of truth.

**Sync (what this program deliberately does not do):** two or more computers hold
live copies and changes flow between them. That means resolving conflicts —
two tills selling the last toast to two students at 11:14:07 — and a database
file in a folder that Google Drive or OneDrive is copying *while it is being
written to*.

That last point is the important one:

> **Never put `cashcafe.db` inside a Google Drive, OneDrive or Dropbox sync
> folder.** These clients copy files while they are open. SQLite keeps its
> journal in a companion `-wal` file, and a sync client that copies the database
> without the matching journal, or that restores an older pair, produces a
> **corrupt database**. This is the single most common way people lose data with
> desktop databases, and the program actively warns you if it detects that its
> database lives under a known sync folder.

What is safe, and what the program does, is to make a **consistent snapshot**
using SQLite's own online backup API (which cooperates with the running
database), zip it, encrypt it, and upload *that*. A backup file is written once
and never touched again, which is exactly the kind of file sync clients handle
perfectly.

---

## What a backup contains

Each backup is one file:

```
CashCafe_2026-09-12_2300.cafebak
```

Inside (before encryption) it is an ordinary zip:

| Entry | What |
|---|---|
| `cashcafe.db` | A consistent snapshot of the whole database |
| `settings.json` | Non-secret settings |
| `manifest.json` | Program version, schema version, timestamp, café name, row counts, SHA-256 of the database |
| `balances.csv` | A plain-text list of every student and balance |
| `transactions.csv` | A plain-text list of every transaction |

The two CSV files are there on purpose: if in ten years nothing can open a
`.cafebak` and no copy of this program exists, Notepad can still read who had
what. A backup you cannot restore is not a backup.

Secrets are **never** in a backup: no admin PIN (not even the hash), no cloud
tokens, no passwords. Restoring on a new computer asks you to set a new PIN.

Size: about **300 KB per school year**, compressed. A whole school's café history
fits in an email attachment.

## Schedule

| Trigger | Default | Configurable |
|---|---|---|
| Nightly | 23:00, or at the next start if the computer was off | Yes |
| On closing the program | On | Yes |
| Before a program update | Always | No |
| Before a database migration | Always | No |
| Before restoring a backup | Always | No |
| Before undoing an import | Always | No |
| Manual | **Backup now** button | — |

**Retention (the default):** keep the last 14 daily, the last 8 weekly (Sunday),
and the last 12 monthly backups. Older ones are deleted automatically. That is
roughly a year of history in about 10 MB.

## Destinations

You can enable any combination. Each is tested with **Test destination** before
being switched on, and each has its own health indicator.

### 1. Local folder (always on, cannot be disabled)

`C:\ProgramData\CashCafe\Backups\`. Protects against mistakes and corruption,
not against the computer being stolen or the disk dying. Never encrypted, because
it is on the same machine as the plain database anyway — encrypting it would add
nothing but a lost passphrase.

### 2. USB stick

Choose the drive. The program writes when the stick is plugged in and warns on
the Café screen if it has not seen it for 7 days. **Always encrypted** — a USB
stick is the single most likely thing to be lost. Keep it somewhere locked, not
in the café drawer next to the computer.

### 3. Network share (school server)

`\\server\cafe-backup\`. Uses the Windows credentials of the logged-in user or a
stored service account. **Encrypted by default**, and the program recommends
leaving that on even on a school server, so that a backup file cannot be read by
anyone who happens to have access to the share.

For most schools with a managed file server that is already backed up by IT,
this is the best option and no external cloud is needed at all.

### 4. Google Drive

For schools on Google Workspace for Education.

**How it is connected:**

1. **Admin → Backup & Cloud → Add destination → Google Drive**.
2. The system browser opens Google's own sign-in page. The program never sees the
   password — this is standard OAuth 2.0 with PKCE, the same mechanism any other
   desktop app uses.
3. Google asks for consent to the scope **`drive.file`**, described below.
4. The program receives a refresh token, which is stored **encrypted with Windows
   DPAPI** in Windows Credential Manager — readable only by that Windows user on
   that machine, and never written to a file that gets copied anywhere.
5. A folder `Cash Café Backups` is created in the chosen account's Drive.

**The scope, and why it is the important detail:**

> The program requests **`https://www.googleapis.com/auth/drive.file` only.**
>
> This scope grants access **exclusively to files the program itself created**.
> It cannot list, read, open, search or modify any other file in that Drive — not
> the school's documents, not anyone's mail, nothing. If the program's token were
> stolen, the worst an attacker could reach is the folder of encrypted café
> backups it made itself.
>
> The broad scopes (`drive`, `drive.readonly`) are **not** requested, and the
> application is built so that it cannot request them.

**Recommended setup for a school:** connect it to a *shared drive or a dedicated
service account owned by the school*, not to a member of staff's personal
account. When that person leaves, the backups must not leave with them.

Revoking access at any time: the account owner removes the app at
<https://myaccount.google.com/permissions>, or a Workspace administrator blocks
it centrally. The program then reports a failed backup on the Café screen; no
data is lost and nothing else is affected.

### 5. Microsoft OneDrive / SharePoint

For schools on Microsoft 365 Education. Same shape:

- Sign-in through Microsoft's own page (MSAL, OAuth 2.0 + PKCE).
- Scope **`Files.ReadWrite.AppFolder`**, which limits the program to its own
  application folder — it cannot read the rest of OneDrive.
- Token in Windows Credential Manager, DPAPI-protected.
- Works with a school tenant, conditional access and MFA, because the sign-in is
  Microsoft's, not ours.
- A tenant administrator can see and revoke the app in Entra ID like any other
  registered application.

### 6. Any other cloud, without integration

Some schools use Nextcloud, Dropbox, Box, or a NAS. Rather than build an
integration for each, point the **local backup folder** or a
[scheduled export](07-Excel-Export.md#scheduled-export) at that service's sync
folder. The backup file is written once and never modified, so ordinary file
sync handles it safely. This is fully supported and is often the simplest answer.

Remember the rule: **the backup file may live in a sync folder; the live database
must not.**

## Encryption of backups

Anything leaving the computer is encrypted before it is uploaded.

| Property | Value |
|---|---|
| Algorithm | AES-256-GCM (authenticated encryption — tampering is detected, not just prevented) |
| Key derivation | PBKDF2-HMAC-SHA256, 310 000 iterations, 16-byte random salt per backup |
| Nonce | 12 random bytes per backup |
| Passphrase | Chosen by the school, minimum 12 characters |
| Where the passphrase is stored | In Windows Credential Manager on the café computer (DPAPI). **And written down by the school somewhere safe** |
| What the cloud provider sees | A `.cafebak` file of random-looking bytes, its size, and its timestamp |

> **The passphrase cannot be recovered.** There is no backdoor, no reset, and no
> way for anyone — including whoever wrote this program — to decrypt a backup
> without it. Write it down, put it where the school keeps other important
> credentials, and make sure at least two people know where that is. A school
> that loses the passphrase and the computer on the same day has lost the data.

**Admin → Backup & Cloud → Verify last backup** downloads the most recent cloud
backup, decrypts it in memory, checks the SHA-256 in the manifest and runs
`integrity_check` on it — proving the backup is genuinely restorable, not just
present. It runs automatically once a week.

## Restoring

**Admin → Backup & Cloud → Restore.**

1. Choose the source — local, USB, network, Google Drive, OneDrive — and the
   backup, listed with date, size and row counts.
2. Enter the passphrase if it is encrypted.
3. The program verifies the checksum and runs an integrity check on the copy
   **before** touching anything.
4. It takes a safety backup of the current database, labelled
   `pre-restore-<timestamp>`.
5. It replaces the database and restarts.
6. It re-verifies every balance against the ledger.

Restoring on a **new computer**: install the program, choose
**Restore from backup** on the first-run wizard instead of Import, and set a new
admin PIN. You are running again in a couple of minutes.

The restore procedure should be **tested once a term**, on a spare computer, by
the person responsible for the café. A backup nobody has ever restored is a
guess, not a plan.

## Health and warnings

The dot on the Café screen and the Backup tab show:

| State | Meaning |
|---|---|
| 🟢 | Last backup succeeded, within schedule |
| 🟡 | No successful backup for over 48 hours, or a destination is unreachable |
| 🔴 | The last backup failed, or the weekly verification failed |

Amber and red state the reason in plain language and what to do about it. The
café keeps selling either way — a backup problem never stops the till.

## What is deliberately *not* built

- **Live multi-computer sync.** It needs conflict resolution and a shared server;
  see [Roadmap](15-Roadmap.md).
- **A cloud service of ours.** There is no vendor server anywhere in this design,
  so there is no vendor to trust, to breach, or to go out of business.
- **Automatic upload of anything unencrypted.** The only way personal data leaves
  the computer in the clear is if an administrator deliberately points a
  scheduled *export* at a shared folder — and the program warns when that folder
  is a synced one.
