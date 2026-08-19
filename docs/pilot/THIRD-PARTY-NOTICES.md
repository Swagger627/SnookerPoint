# Snooker Point — Third-Party Notices

Snooker Point uses the following third-party components. Each is used under its respective licence.

| Component | Purpose | Licence |
|-----------|---------|---------|
| .NET 10 runtime & libraries (Microsoft) | Application runtime (self-contained) | MIT |
| Microsoft.EntityFrameworkCore (+ Sqlite provider) | Data access / migrations | MIT |
| Microsoft.Data.Sqlite / SQLite | Local database engine | MIT / Public Domain (SQLite) |
| CommunityToolkit.Mvvm | MVVM helpers | MIT |
| Microsoft.Extensions.Hosting / DependencyInjection / Logging | App host & DI | MIT |
| Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.File | Structured file logging | Apache-2.0 |
| Windows Presentation Foundation (WPF) | Desktop UI | MIT |
| Inno Setup (build-time only) | Windows installer authoring | Inno Setup licence (free) |

Notes:
- Inno Setup is used only to build the installer; it is not distributed inside the application.
- No third-party cryptography library is used — licence signing/verification uses the built-in .NET
  `System.Security.Cryptography` (ECDSA P-256 / SHA-256).

Full licence texts for the .NET components are available from Microsoft; the SQLite source is public
domain. This notice is provided for the pilot release and will be finalised for public distribution.
