# Snooker Point 1.0.1 (Pilot) — Known Limitations

This is an **internal/pilot** release. It is stable for real use but has deliberate boundaries.

## Licensing (offline, best-effort)
- Offline licensing is **deterrence, not perfect protection**. A determined administrator with full
  machine access can still remove every protected state copy and reset an offline trial. The online
  activation service and Owner Portal (a later phase) add server-side enforcement.
- The trial state is kept in two protected copies (per-user DPAPI and a machine-level ProgramData
  checkpoint). Switching Windows users, reinstalling, upgrading, or restoring a business backup does
  **not** restart the trial. Deleting one copy does not restart it; deleting *all* protected copies on
  the machine can.
- Major hardware or Windows changes may change the machine fingerprint and require a licence reissue.
- The production public verification key must be set before a public release; the pilot uses a
  pilot key. A build without an embedded key runs **trial-only** (no activation possible).

## Runtime trial expiry
- New operational work (opening a shift, starting a table, new sale, new/started booking, inventory
  and settings changes) is blocked once the trial expires; already-open drafts/sessions can still be
  completed so no money or data is lost. A background check runs every ~15 minutes, so leaving the app
  open does not extend the 72-hour trial.

## Printing & scanning
- **No physical thermal printer or barcode scanner has been validated by the developer.** 58/80 mm
  receipt *previews* render and Windows printer selection works, but a real print/scan must be verified
  on-site using the printer and scanner checklists.

## Not in this release (by design)
- Online Licensing API, Owner Portal, cloud sync, memberships, loyalty, online ordering, automatic
  internet updates, refunds, and advanced accounting integrations.

## Performance
- Reports aggregate completed data client-side (SQLite cannot sum money or order timestamps
  server-side). Measured on developer hardware at ~20k sales / 20k movements / 20k audit events:
  product search <0.2 s, sales history ~0.5 s, dashboard ~0.5 s, audit page ~0.1 s, backup ~0.7 s,
  integrity check ~0.3 s. Very large multi-year datasets will scale roughly linearly.
