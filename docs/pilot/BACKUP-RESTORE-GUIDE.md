# Snooker Point — Backup & Restore Guide

## What a backup contains
- The SQLite business database, product images, receipts and an integrity manifest.
- **NOT** included: machine licence/activation state (so a backup never clones activation to another
  computer — the manifest records this).

## Create a backup
- **Settings → Backup & restore** (Owner/Administrator) → **Create backup** (optionally choose a folder).
- Backups are saved by default under `%AppData%\SnookerPoint\Backups`.

## Automatic backups
- **Settings → Backup settings**: enable daily and/or on-close automatic backups and a retention count.
  Only automatic backups are pruned by retention; manual and safety backups are kept. Automatic-backup
  failures never crash the app.

## Validate a backup
- In the backup list, click **Validate**. Status is Valid / Invalid / Unsupported Version / Missing
  Files / Validation Failed. Never restore an unverified backup.

## Restore (high-risk, Owner/Administrator)
1. Open **Backup & restore**, type **RESTORE** in the confirmation box, and click **Restore** on a backup.
2. The app **validates** it, takes a **safety backup** of current data first, replaces the data
   atomically, then **restarts** automatically.
3. If a restore fails, your **original data is preserved**. If the app can’t restart itself, reopen it
   manually — the data was already restored.
- Restoring on a different computer still requires that computer’s own licence.

## Upgrades
- Installing a newer version takes a **verified pre-upgrade safety backup** before migrating and runs an
  integrity check. If migration fails, the original database is restored and a recovery message appears.
