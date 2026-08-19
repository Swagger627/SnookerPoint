# Snooker Point — Pilot Acceptance Checklist

## Installation
- ☐ Fresh install runs the setup wizard; trial starts on completion (72 hours).
- ☐ Restart: app reopens to Login; trial time unchanged.
- ☐ Upgrade install over the old version preserves data and licence/trial.
- ☐ Uninstall then reinstall: data preserved; setup/login continue from existing state.
- ☐ Single instance: launching a second copy brings the first to the front (no second window).

## Users & security
- ☐ Owner, Administrator, Manager, Cashier, Floor Staff each see the correct capabilities.
- ☐ Password and PIN login work; credential reset works; lockout triggers after repeated failures.

## Operations
- ☐ Open shift → hourly table → pause/resume → transfer → finish → checkout.
- ☐ Fixed-price table session.
- ☐ Booking → check in → start session.
- ☐ Walk-in sale; cash, electronic, and split payments; receipt prints; stock deducts once.
- ☐ Close shift: expected vs counted cash and variance correct.

## Recovery
- ☐ Close app during a running table → session recovers with correct billable time.
- ☐ Close app with a held/draft sale → draft recovers, no stock deducted.
- ☐ Kill process before payment completes → nothing charged, no partial commit.
- ☐ Printer failure does not roll back a completed sale.
- ☐ Backup → validate → restore (auto-restart) → data restored.
- ☐ Failed/invalid restore preserves current data.
- ☐ Database check reports “ok”.

## Licensing
- ☐ Trial banner shows remaining time; expiring-soon warning within 24h.
- ☐ On expiry, new operations are blocked and route to Activation; data untouched.
- ☐ Offline activation with a valid pilot licence; survives restart.
- ☐ A licence for another computer is rejected; an edited/forged licence is rejected.

## UX
- ☐ Dark and light themes render on every screen.
- ☐ 1366×768 and 1920×1080 at 100/125/150% scaling: no cut-off controls, no horizontal scroll.
- ☐ Support bundle contains no secrets.
