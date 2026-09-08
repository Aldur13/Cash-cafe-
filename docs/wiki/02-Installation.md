# 02 — Installation & First Run

## Requirements

| | Minimum | Recommended |
|---|---|---|
| Operating system | Windows 10 version 1809 (64-bit) | Windows 11 |
| Disk | 300 MB free | 2 GB free (room for backups) |
| RAM | 4 GB | 8 GB |
| Screen | 1280 × 720 | 1920 × 1080 or a touch screen |
| Internet | **Not required** | Only needed for cloud backup |
| Rights | Local administrator (install only) | — |

.NET is **not** a separate download. The installer ships a self-contained
.NET 8 runtime inside the program folder, so nothing else has to be installed
and no Windows Update is needed. See [Architecture](10-Architecture.md).

## Installing

1. Download `CashCafe-Setup-x.y.z.exe`.
2. Right-click → **Properties** → tick **Unblock** if Windows marked it as coming
   from the internet, then **OK**.
3. Double-click it. If SmartScreen appears, choose **More info → Run anyway**
   (the build is signed once the school buys a code-signing certificate;
   until then SmartScreen will warn — see [Troubleshooting](13-Troubleshooting.md)).
4. Choose **Install for all users on this computer** (default). This puts the
   database in a shared location so it does not matter which Windows account the
   café staff are logged into.
5. Finish. A **Cash Café** shortcut appears on the desktop and Start menu.

### Silent install (for school IT)

```
CashCafe-Setup-x.y.z.exe /S /D=C:\Program Files\CashCafe
```

`/S` = silent, `/D` = target directory (must be the last argument).

### Portable version

There is also `CashCafe-portable-x.y.z.zip`. Unzip it to a USB stick and run
`CashCafe.exe`. In portable mode the database is created **next to the exe** in
`.\data\`, so the whole café moves with the stick. Portable mode is convenient but
please read the warning in [Security & Privacy](12-Security-and-Privacy.md#usb-sticks-and-portable-mode)
— a lost USB stick is a lost student list.

## Where files are kept

| What | Path | Notes |
|---|---|---|
| Program | `C:\Program Files\CashCafe\` | Read-only in normal use |
| Database | `C:\ProgramData\CashCafe\cashcafe.db` | **This is the important file.** All data lives here |
| Write-ahead log | `C:\ProgramData\CashCafe\cashcafe.db-wal` | SQLite working file; keep with the database |
| Settings | `C:\ProgramData\CashCafe\settings.json` | Non-secret settings |
| Secrets | Windows Credential Manager / DPAPI | Admin PIN hash, cloud tokens — never in plain files |
| Local backups | `C:\ProgramData\CashCafe\Backups\` | Rolling, see [Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md) |
| Logs | `C:\ProgramData\CashCafe\Logs\cashcafe-YYYYMMDD.log` | Kept 30 days, no personal data beyond student IDs |
| Import history | `C:\ProgramData\CashCafe\Imports\` | A copy of every Excel file ever imported, for audit |

> **If you back up one thing, back up `C:\ProgramData\CashCafe\`.** Everything else
> can be reinstalled.

`ProgramData` is hidden by default. Paste the path into the Explorer address bar
to get there, or use **Admin → Backup & Cloud → Open data folder**.

## First run

The first time the program opens it runs a short setup wizard.

### Step 1 — Café name and currency
Café name (appears on exports and reports) and currency, which is fixed to
**SEK (kr)** in this version.

### Step 2 — Admin PIN
Choose a PIN of 4–12 digits.

- It is stored as a **PBKDF2-SHA256 hash with a random salt** (310,000 iterations)
  — the PIN itself is not saved anywhere and cannot be read back out of the file.
- Write it down and put it somewhere the responsible teacher can find it.
- If it is lost, there is a documented recovery procedure that requires local
  administrator rights on the computer — see
  [Troubleshooting → Lost admin PIN](13-Troubleshooting.md#i-lost-the-admin-pin).

### Step 3 — The minimum balance
Defaults to **-10,00 kr**, which is what the café asked for. An admin can change
it later (Admin → Settings), but it can never be set to a *positive* number, and
changing it is written to the audit log.

### Step 4 — Import your Excel sheet
Optional, and it can be done later. If you point it at your existing sheet now,
the wizard runs the full import preview described in
[Excel Import](06-Excel-Import.md).

### Step 5 — Backup destination
Choose where nightly backups go. The default is a local folder; you can add
Google Drive, OneDrive or a network share now or later
([Cloud Sync & Backup](11-Cloud-Sync-and-Backup.md)).

The wizard ends on the **Café** screen, ready to sell.

## Updating

**Admin → Settings → Check for updates**, or just run the new installer over
the old one. Updates never touch `C:\ProgramData\CashCafe\`. On first launch after
an update the program:

1. Takes an automatic backup labelled `pre-update-<old version>`.
2. Runs any database migrations (see [Data Model → Migrations](09-Data-Model.md#migrations)).
3. Verifies that every student's cached balance still equals the sum of their ledger.

If step 3 fails the program refuses to start and tells you which student is
affected, rather than carrying on with numbers it does not trust.

## Uninstalling

Windows **Settings → Apps → Cash Café → Uninstall** removes the program but
**keeps `C:\ProgramData\CashCafe\`** on purpose, so an accidental uninstall does
not destroy the café's records. To remove the data as well, tick
*"Also delete all café data"* in the uninstaller, or delete that folder by hand.
Do an **Admin → Export** first if there is any chance the numbers will be wanted
later.
