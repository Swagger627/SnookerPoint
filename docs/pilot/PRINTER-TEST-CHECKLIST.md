# Snooker Point — Receipt Printer Test Checklist (manual, on-site)

> The developer has **not** validated a physical thermal printer. Complete this on-site with the real
> printer before go-live.

Set the receipt width in **Settings → Club profile** (58 mm or 80 mm) to match the printer.

| # | Check | 58 mm | 80 mm |
|---|-------|-------|-------|
| 1 | Windows printer selectable; test print works | ☐ | ☐ |
| 2 | Walk-in sale receipt prints and cuts | ☐ | ☐ |
| 3 | Table-only sale receipt prints | ☐ | ☐ |
| 4 | Split-payment breakdown (each portion + cash change) fits the width | ☐ | ☐ |
| 5 | Long product names wrap correctly (no truncation) | ☐ | ☐ |
| 6 | Club name / address / phone fit the width | ☐ | ☐ |
| 7 | Reprint is clearly marked **REPRINT** | ☐ | ☐ |
| 8 | Printer **offline**: friendly error; the completed sale is **not** rolled back | ☐ | ☐ |
| 9 | Printer **unavailable / not selected**: friendly message | ☐ | ☐ |
| 10 | User **cancels** the print dialog: sale remains completed; receipt reprintable | ☐ | ☐ |

Notes / printer model tested: ______________________________________________
