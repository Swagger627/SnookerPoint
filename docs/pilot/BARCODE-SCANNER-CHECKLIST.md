# Snooker Point — Barcode Scanner Test Checklist (manual, on-site)

> The developer has **not** validated a physical scanner. Complete this on-site with the real
> keyboard-wedge scanner. Configure the scanner to send an **Enter** suffix after each scan.

| # | Check | Pass |
|---|-------|------|
| 1 | Focus starts in the New Sale search/scan box | ☐ |
| 2 | Scanning a known product adds it to the cart | ☐ |
| 3 | A barcode with a **leading zero** (e.g. 0012345) matches the right product | ☐ |
| 4 | **Rapid** consecutive scans each register (no dropped input) | ☐ |
| 5 | Scanning the **same** product again increments quantity by exactly 1 per scan | ☐ |
| 6 | The Enter suffix does **not** trigger a duplicate command / double add | ☐ |
| 7 | **Unknown** barcode shows a clear “not found” message with add/search options | ☐ |
| 8 | **Inactive** product is not sold; clear message | ☐ |
| 9 | **Out-of-stock** tracked product behaves per settings (blocked or warned) | ☐ |
| 10 | After a scan, focus **returns** to the scan box for the next item | ☐ |
| 11 | CSV export of products keeps barcode leading zeroes | ☐ |

Notes / scanner model tested: _____________________________________________
