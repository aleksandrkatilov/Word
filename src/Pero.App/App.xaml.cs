using System.Globalization;
using System.IO;
using System.Windows;
using System.Threading;
using System.Windows.Threading;
using Pero.Core.Files;
using Pero.Services;

namespace Pero;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    public App()
    {
        // До любого обращения к компонентам Syncfusion (SEC-01: без сетевых обращений).
        Syncfusion.Telemetry.Telemetry.Disable();

        // Тема применяется ко всем элементам Syncfusion как стиль по умолчанию.
        try { Syncfusion.SfSkinManager.SfSkinManager.ApplyThemeAsDefaultStyle = true; } catch { }
        var settings = AppSettings.Load();
        RegisterLicense(settings);
        ApplyLanguage(settings);
        Services = new AppServices(settings);
    }

    private static void RegisterLicense(AppSettings settings)
    {
        // Ключ не хранится в репозитории: переменная окружения, файл рядом с exe или settings.json.
        string? key = Environment.GetEnvironmentVariable("PERO_SYNCFUSION_KEY");
        string keyFile = Path.Combine(AppContext.BaseDirectory, "syncfusion-license.txt");
        if (string.IsNullOrWhiteSpace(key) && File.Exists(keyFile)) key = File.ReadAllText(keyFile).Trim();
        if (string.IsNullOrWhiteSpace(key)) key = settings.SyncfusionLicenseKey;
        if (!string.IsNullOrWhiteSpace(key))
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(key);
    }

    private static void ApplyLanguage(AppSettings settings)
    {
        string lang = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))?[7..] ?? settings.Language;
        var culture = new CultureInfo(lang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Strings.Use(culture);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Log.Info($"START v{typeof(App).Assembly.GetName().Version} culture={CultureInfo.CurrentUICulture.Name}");

        int opened = RestoreAfterCrash();

        foreach (var arg in e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
        {
            if (File.Exists(arg)) { EditorWindow.OpenInNewWindow(arg); opened++; }
        }
        if (opened == 0) new EditorWindow().Show();
    }

    /// <summary>FILE-03 / QA-04: предложить восстановление копий после аварийного завершения.</summary>
    private static int RestoreAfterCrash()
    {
        IReadOnlyList<RecoveryEntry> orphans;
        try { orphans = Services.Recovery.FindOrphans(Services.RecoverySession.Id); }
        catch (Exception ex) { Log.Error("recovery-scan", ex); return 0; }
        if (orphans.Count == 0) return 0;

        string list = string.Join(Environment.NewLine,
            orphans.Select(o => $"• {o.Title} — {o.SavedUtc.ToLocalTime():g}"));
        var answer = MessageBox.Show(
            string.Format(Strings.RecoverPrompt, list), Strings.AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            foreach (var o in orphans) EditorWindow.OpenRecovered(o);
            return orphans.Count;
        }
        if (answer == MessageBoxResult.No)
            foreach (var o in orphans) Services.Recovery.Remove(o.DocId);
        // Cancel — оставить копии до следующего запуска
        return 0;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("unhandled", e.Exception);
        MessageBox.Show(string.Format(Strings.UnexpectedError, e.Exception.Message), Strings.AppName,
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // документ и копия восстановления остаются доступными
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("EXIT");
        Services.Dispose();
        base.OnExit(e);
    }
}
