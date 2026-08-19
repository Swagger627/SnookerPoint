# Snooker Point — Pilot Installation Guide

## Requirements
- Windows 10 or 11, 64-bit. No separate .NET runtime is needed (the app is self-contained).
- Administrator rights to **install** (per-machine). Everyday use and activation do **not** need admin.

## Building the pilot installer (owner/developer)
1. Set the approved **public** verification key in `src/SnookerPoint.App/Licensing/PublicKey.txt`.
2. Publish the customer app:
   ```
   dotnet publish src/SnookerPoint.App -c Release -r win-x64 --self-contained true ^
     -p:LicenseProfile=Pilot -o publish/win-x64
   ```
   The build fails if the public key is empty or the development override is enabled — this is intended.
3. Install Inno Setup 6 (https://jrsoftware.org/isdl.php) and compile:
   ```
   iscc installer/SnookerPoint.iss
   ```
   The installer is written to `installer/Output/SnookerPoint-1.0.0-setup.exe`.

## Installing (pilot site)
1. Run `SnookerPoint-1.0.0-setup.exe` and follow the wizard (optional desktop shortcut).
2. Launch Snooker Point from the Start Menu.
3. Complete the **first-run setup wizard** (club, owner account, tables, printer). The **72-hour trial
   starts when setup completes**.
4. Sign in as the owner.

## Data locations (preserved across upgrades/uninstall)
- Business data, images, receipts, exports, backups, logs: `%AppData%\SnookerPoint\`
- Per-user licence/trial state: `%AppData%\SnookerPoint\License\`
- Machine-level licence checkpoint: `%ProgramData%\SnookerPoint\License\`

## Upgrading
- Run a newer installer over the old version. Business data and licence/activation are preserved.
- On first launch the app takes a **verified pre-upgrade safety backup**, applies pending database
  migrations, runs an integrity check, and only then continues. If a migration fails, the original
  database is preserved and a recovery message points to diagnostics and the backups folder.

## Uninstalling
- Uninstall from Add/Remove Programs. Only the application binaries are removed; **business data,
  backups and activation are preserved**. Reinstalling continues from the existing data and trial/licence.
