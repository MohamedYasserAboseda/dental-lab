# Dental Lab Management System

## Status
- M1: skeleton, themes, motion system, splash, shell, AR/EN + RTL, SQLite + EF Core.
- M2: Doctors + ledger-based doctor accounts.
- M3: Dental chart (FDI, reusable control), Patients, price list & materials (Settings), doctor-specific prices,
  Cases (auto numbering, price snapshot, workflow with history, filters, overdue).
- M4: Invoices (individual + consolidated, no double billing), Payments & receipts (overpayment/duplicate checks,
  reversals), Doctor Accounts (statement, adjustments) - all posted to the ledger in single transactions.

- M5: Dashboard (real figures, animated counters, native charts, date range) and Backup & Restore
  (manual + scheduled zip backups with checksums, verification, retention, staged safe restore, integrity check).

- M6: Printing & PDF (QuestPDF): invoice, receipt, doctor statement, case work order in A4 / A5 / 80 mm thermal,
  Arabic (RTL) and English, lab logo and details, print preview, print, save as PDF.
  NOTE: QuestPDF is used under its Community licence - check https://www.questpdf.com/license/ for your revenue level.

- M7: Case attachments (photos / STL / PLY, validated by content, stored outside the database, included in backups),
  Production Workflow board with statistics, doctor profile tabs (cases / invoices / payments), patient case history.

## Run
1. Install .NET 10 SDK.
2. Extract this zip OVER your existing DentalLab folder (merge/replace; keep `Persistence/Migrations`).
3. `setup.bat` (restore, creates missing migrations, builds), then `run.bat`. `test.bat` runs the unit tests.

Future model changes: `migrate.bat <MigrationName>`. Data lives in `%LOCALAPPDATA%\DentalLab\`.
