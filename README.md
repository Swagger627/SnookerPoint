🎱 Snooker Point

**Snooker Point** is a fully offline Windows desktop Point-of-Sale and club management system built for snooker and pool clubs.

It combines table-session billing, product sales, inventory, staff management, bookings, reporting, receipts, backups, and offline licensing in a single desktop application.

> **Current Version:** 1.0.1 Pilot
> **Platform:** Windows 64-bit
> **Framework:** .NET 10 / WPF
> **Database:** SQLite
> **Internet Required:** No

---

✨ Features

🎱 Table Management

* Manage multiple snooker or pool tables
* Different hourly rates for individual tables
* Hourly billing
* Fixed-price sessions
* Start, pause, resume, finish, and transfer sessions
* Automatic session price calculation
* Running sessions survive application restarts
* Completed sessions move to checkout
* Session history

🛒 Point of Sale

* Walk-in product sales
* Table-only checkout
* Table + product checkout
* Discounts and price overrides
* Multiple payment methods:

  * Cash
  * EasyPaisa
  * JazzCash
  * Bank Transfer
* Split payments
* Sales history
* Receipt preview
* Receipt reprinting

📦 Products & Inventory

* Product creation and editing
* Categories
* SKU support
* Barcode support
* Barcode scanner compatible
* Stock In
* Stock adjustments
* Waste and damage tracking
* Returns
* Low-stock indicators
* Out-of-stock indicators
* Complete stock movement history
* CSV import/export
* Optional product images

📅 Bookings

* Create table bookings
* Prevent overlapping bookings
* Check-in customers
* Start table sessions directly from bookings
* Hourly or fixed booking sessions
* Cancel bookings
* Mark bookings as No Show
* Booking data survives application restart

👥 Staff & Permissions

Multiple staff roles are supported:

* Owner
* Administrator
* Manager
* Cashier
* Floor Staff

Features include:

* Password login
* Optional PIN login
* Staff creation and management
* Role-based permissions
* Disable staff accounts
* Account lockout protection
* Password management
* Owner account protection

💰 Shift Management

* Open Shift
* Cash In
* Cash Out
* Expenses
* Cash Drops
* Close Shift
* Expected cash calculation
* Cash variance tracking

📊 Reports

Includes reporting for:

* Sales
* Table Sessions
* Products
* Inventory
* Cashiers
* Shifts

Reports support filtering and CSV export.

💾 Backup & Recovery

* Manual database backups
* Backup validation
* Database integrity checking
* Safe restore process
* Automatic safety backup before restore
* Business data remains separate from machine activation state

🌗 Interface

* Dark theme
* Light theme
* English interface
* Urdu localization support
* Keyboard-friendly desktop workflow
* Designed for club staff and owners

---

🔐 Offline Licensing

Snooker Point uses an offline machine-bound licensing system.

A new installation begins with a limited trial period. A licensed customer receives a signed licence file that is associated with their installation.

No internet connection, online account, or cloud licensing server is required.

> Owner-side licence issuance utilities and private signing material are intentionally not included in this public repository.

---

🛠️ Technology Stack

* **C#**
* **.NET 10**
* **WPF**
* **MVVM**
* **CommunityToolkit.Mvvm**
* **Entity Framework Core**
* **SQLite**
* **Serilog**
* **xUnit**
* **Inno Setup**

---

🏗️ Project Structure

```text
SnookerPoint/
│
├── src/
│   ├── SnookerPoint.App/
│   ├── SnookerPoint.Application/
│   ├── SnookerPoint.Domain/
│   ├── SnookerPoint.Infrastructure/
│   └── SnookerPoint.Licensing/
│
├── tests/
│   └── SnookerPoint.Tests/
│
├── docs/
│   └── pilot/
│
├── installer/
│
├── SnookerPoint.sln
├── Directory.Build.props
├── global.json
└── .gitignore
```

---

🚀 Building From Source

### Requirements

Install:

* Windows 10 or Windows 11
* .NET 10 SDK
* Git

Clone the repository:

```powershell
git clone <YOUR-REPOSITORY-URL>
cd SnookerPoint
```

Restore dependencies:

```powershell
dotnet restore
```

Build the solution:

```powershell
dotnet build SnookerPoint.sln
```

Run the application:

```powershell
dotnet run --project src/SnookerPoint.App/SnookerPoint.App.csproj
```

---

🧪 Running Tests

Run the complete automated test suite:

```powershell
dotnet test SnookerPoint.sln
```

The project includes tests covering areas such as:

* Authentication
* Account security
* Billing
* Table sessions
* Inventory
* Products
* Bookings
* Sales
* Payments
* Reporting
* Database migrations
* Backups
* Licensing
* UI ViewModels
* Application reliability

---

💻 Publishing

Example self-contained Windows x64 publish:

```powershell
dotnet publish src/SnookerPoint.App/SnookerPoint.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

Generated binaries, installers, private owner tools, customer licence files, databases, and local development files are intentionally excluded from this repository.

---

📖 Documentation

Pilot documentation includes:

* Installation guide
* Cashier quick-start guide
* Backup and restore guide
* Barcode scanner checklist
* Printer testing checklist
* Known limitations
* Pilot acceptance checklist
* Third-party notices

See:

```text
docs/pilot/
```

---

🔒 Security

This repository does **not** intentionally contain:

* Private signing keys
* Customer licence files
* Customer databases
* Real passwords or PINs
* Recovery codes
* Environment secrets
* Owner-side licence-generation utilities
* Generated release artifacts

Never commit secrets, private keys, customer data, or generated licence files.

---

🗄️ Local Data

Snooker Point stores its operational data locally using SQLite.

The application is designed to function without a permanent internet connection or external database server.

Users should maintain regular backups, especially before software upgrades or major configuration changes.

---

🧾 Supported Payment Types

Currently supported:

* Cash
* EasyPaisa
* JazzCash
* Bank Transfer
* Split Payment

Snooker Point records these payments locally and does not directly process online transactions.

---

🖨️ Hardware

The application is designed to support common club hardware including:

* USB keyboard-wedge barcode scanners
* 58 mm thermal receipt printers
* 80 mm thermal receipt printers

Hardware compatibility may vary by device and driver.

---

⚠️ Pilot Status

Version **1.0.1** is currently considered a pilot release.

Before using the software in a live club environment, testing should include:

* Full cashier shift
* Real barcode scanner
* Physical receipt printer
* Backup and restore
* Windows display scaling
* Club-specific table rates
* Payment workflows

---

📄 Copyright

Copyright © 2026 Snooker Point.

All rights reserved.

The source code is publicly visible for development, demonstration, and portfolio purposes. Public availability of the source code does not automatically grant permission to redistribute, resell, relicense, or commercially exploit the software.

A separate licence may be required for commercial use or distribution.

---

🎱 About Snooker Point

Snooker Point was created to provide snooker and pool clubs with a dedicated desktop management solution without requiring cloud subscriptions, permanent internet access, or complicated infrastructure.

The goal is simple:

**Manage tables, staff, products, payments, inventory, bookings, and club operations from one offline application.**
