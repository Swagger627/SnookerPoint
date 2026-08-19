using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SnookerPoint.App.Localization;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.App.ViewModels;
using SnookerPoint.App.Views;
using SnookerPoint.Application.Settings;
using SnookerPoint.Application.Setup;
using SnookerPoint.Infrastructure.DependencyInjection;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.App;

/// <summary>
/// Application composition root: builds the host, wires DI and file logging, runs
/// database migration, applies the saved (or default) theme/culture, and routes to
/// either the first-run setup wizard or the login screen.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private ISingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance: only one process may use the local database at a time. A second launch
        // asks the running instance to come forward, then exits. (The restore-restart releases the
        // lock before relaunching, so it is not blocked.)
        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.TryAcquire())
        {
            _singleInstance.SignalExistingInstance();
            MessageBox.Show("Snooker Point is already running. Bringing the existing window to the front.",
                "Snooker Point", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        var paths = new AppDataPaths();
        paths.EnsureLiveDirectories();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                paths.LogFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 20_000_000,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        RegisterGlobalExceptionHandlers();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(_singleInstance);
        builder.Services.AddSerilog();
        builder.Services.AddSnookerPointInfrastructure();

        // App-layer services
        builder.Services.AddSingleton<IThemeService>(_ => new ThemeService(Current));
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<ISessionContext, SessionContext>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IApplicationControl, ApplicationControl>();
        builder.Services.AddSingleton<SnookerPoint.App.Licensing.ILicensingService, SnookerPoint.App.Licensing.LicensingService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<SnookerPoint.App.Licensing.ILicenseGate, SnookerPoint.App.Licensing.LicenseGate>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        // Screen view models (fresh per navigation)
        builder.Services.AddTransient<SetupWizardViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<TablesViewModel>();
        builder.Services.AddTransient<SessionHistoryViewModel>();
        builder.Services.AddTransient<ManageTablesViewModel>();
        builder.Services.AddTransient<StaffViewModel>();
        builder.Services.AddTransient<AccountViewModel>();
        builder.Services.AddTransient<ProductsViewModel>();
        builder.Services.AddTransient<ManageCategoriesViewModel>();
        builder.Services.AddTransient<InventoryViewModel>();
        builder.Services.AddTransient<NewSaleViewModel>();
        builder.Services.AddTransient<SalesHistoryViewModel>();
        builder.Services.AddTransient<BookingsViewModel>();
        builder.Services.AddTransient<ReportsViewModel>();
        builder.Services.AddTransient<BackupViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<AdminViewModel>();
        builder.Services.AddTransient<AuditViewModel>();
        builder.Services.AddTransient<ActivationViewModel>();

        _host = builder.Build();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        var theme = _host.Services.GetRequiredService<IThemeService>();
        var localization = _host.Services.GetRequiredService<ILocalizationService>();
        var navigation = _host.Services.GetRequiredService<INavigationService>();

        // Ensure token dictionary exists before first paint.
        theme.Apply(SnookerPoint.App.Theming.ThemeMode.Dark);
        localization.SetCulture("en");

        try
        {
            _host.Services.GetRequiredService<DatabaseInitializer>().Initialize();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database initialisation failed.");
            var friendly = ex is SnookerPoint.Infrastructure.Persistence.DatabaseUpgradeException
                ? ex.Message
                : "Snooker Point could not open its database. Please restart the app.\n\nDetails have been written to the log.";
            MessageBox.Show(friendly, "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var setup = _host.Services.GetRequiredService<ISetupService>();
        var setupComplete = setup.IsSetupComplete();

        if (setupComplete)
        {
            // Apply the owner's saved theme/language.
            var settings = _host.Services.GetRequiredService<IClubSettingsService>().Get();
            if (settings is not null)
            {
                theme.Apply(settings.Theme == "Light"
                    ? SnookerPoint.App.Theming.ThemeMode.Light
                    : SnookerPoint.App.Theming.ThemeMode.Dark);
                localization.SetCulture(string.IsNullOrWhiteSpace(settings.Language) ? "en" : settings.Language);
            }

            // Evaluate the trial/licence and route accordingly. Business data is never touched here.
            var licensing = _host.Services.GetRequiredService<SnookerPoint.App.Licensing.ILicensingService>();
            var evaluation = licensing.Evaluate();
            if (evaluation.Status == SnookerPoint.Licensing.LicenseStatus.NotStarted)
            {
                // First Phase 7 startup on an already-set-up install: begin the 72-hour trial now.
                licensing.StartTrialIfNeeded();
                evaluation = licensing.Evaluate();
            }

            if (evaluation.OperationsAllowed)
            {
                navigation.ShowLogin();
            }
            else
            {
                // Expired / invalid / machine-mismatch / state error → the limited Activation screen.
                navigation.ShowActivation();
            }
        }
        else
        {
            navigation.ShowSetupWizard();
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();

        // A second launch signals us to come to the foreground.
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested += () => Dispatcher.Invoke(() =>
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
            });
        }

        // Run a due automatic (daily) backup in the background; never block or crash startup.
        if (setupComplete)
        {
            TryRunStartupBackup(logger);
            StartLicenseWatchdog();
        }

        logger.LogInformation("Snooker Point started. Setup complete: {SetupComplete}.", setupComplete);
    }

    private System.Windows.Threading.DispatcherTimer? _licenseWatchdog;

    /// <summary>
    /// Periodically re-evaluates the licence so the 72-hour trial cannot be bypassed by leaving the
    /// app open. If the trial expires while running, it routes to Activation once the user is not on
    /// a login/setup/activation screen (persisted drafts/sessions are never lost).
    /// </summary>
    private void StartLicenseWatchdog()
    {
        var gate = _host!.Services.GetRequiredService<SnookerPoint.App.Licensing.ILicenseGate>();
        var navigation = _host.Services.GetRequiredService<INavigationService>();
        var shell = _host.Services.GetRequiredService<ShellViewModel>();

        _licenseWatchdog = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15),
        };
        _licenseWatchdog.Tick += (_, _) =>
        {
            try
            {
                var evaluation = gate.Evaluate();
                if (!evaluation.OperationsAllowed && shell.Current is not (ActivationViewModel or LoginViewModel or SetupWizardViewModel))
                {
                    navigation.ShowActivation();
                }
            }
            catch
            {
                // A watchdog tick must never crash the app.
            }
        };
        _licenseWatchdog.Start();
    }

    /// <summary>
    /// Global crash logging. UI-thread exceptions are logged and shown as a friendly message and the
    /// app keeps running; a fatal non-UI exception is logged and the app exits rather than continuing
    /// in a possibly-unsafe state. Log entries never contain secrets.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception (recovered).");
            try
            {
                MessageBox.Show(
                    "Something went wrong with the last action, but Snooker Point is still running. If it keeps happening, please create a support bundle from Settings.",
                    "Snooker Point", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch
            {
                // ignore secondary UI failures
            }

            args.Handled = true; // a single UI glitch should not close the app
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Fatal unhandled exception; the application will close.");
            }

            Log.CloseAndFlush();
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved background task exception.");
            args.SetObserved();
        };
    }

    private void TryRunStartupBackup(ILogger<App> logger)
    {
        try
        {
            var backups = _host!.Services.GetRequiredService<SnookerPoint.Application.Backups.IBackupService>();
            var result = backups.RunAutomaticBackupIfDue(0);
            if (result.Failed)
            {
                logger.LogWarning("Automatic startup backup did not run: {Message}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Automatic startup backup check failed (ignored).");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TryRunOnCloseBackup();
        _host?.Dispose();
        _singleInstance?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void TryRunOnCloseBackup()
    {
        try
        {
            if (_host is null)
            {
                return;
            }

            var settings = _host.Services.GetRequiredService<SnookerPoint.Application.Settings.IOperationalSettingsService>().Get();
            if (settings is { AutoBackupEnabled: true, AutoBackupOnClose: true })
            {
                _host.Services.GetRequiredService<SnookerPoint.Application.Backups.IBackupService>()
                    .CreateBackup(string.IsNullOrWhiteSpace(settings.BackupFolder) ? null : settings.BackupFolder, "Automatic backup on close", 0, automatic: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Automatic on-close backup failed (ignored).");
        }
    }
}
