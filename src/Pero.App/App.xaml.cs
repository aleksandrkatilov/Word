using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Pero.Core.Files;
using Pero.Services;

namespace Pero;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    public App()
    {
        var settings = AppSettings.Load();
        // Язык интерфейса: английский по умолчанию; RWRITE_LANG=ru|en (тесты, отладка) важнее настройки.
        string lang = AppSettings.NormalizeLanguage(Environment.GetEnvironmentVariable("RWRITE_LANG"))
                      ?? AppSettings.NormalizeLanguage(settings.Language) ?? "en";
        Loc.Use(lang);
        var culture = new CultureInfo(lang == "ru" ? "ru-RU" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Strings.Use(culture);
        Services = new AppServices(settings);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Log.Info($"START v{typeof(App).Assembly.GetName().Version}");
        // каталог шрифтов собирается в фоне, пока открывается окно
        _ = System.Threading.Tasks.Task.Run(() => Pero.Doc.Fonts.FontCatalog.Shared);

        int opened = RestoreAfterCrash();
        foreach (var arg in e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
            if (File.Exists(arg)) { EditorWindow.OpenInNewWindow(arg); opened++; }
        if (opened == 0) new EditorWindow().Show();
    }

    /// <summary>REL-02: предложить восстановление копий после аварийного завершения.</summary>
    private static int RestoreAfterCrash()
    {
        IReadOnlyList<RecoveryEntry> orphans;
        try { orphans = Services.Recovery.FindOrphans(Services.RecoverySession.Id); }
        catch (Exception ex) { Log.Error("recovery-scan", ex); return 0; }
        if (orphans.Count == 0) return 0;

        string list = string.Join(Environment.NewLine, orphans.Select(o => $"• {o.Title} — {o.SavedUtc.ToLocalTime():g}"));
        var answer = MessageBox.Show(string.Format(Strings.RecoverPrompt, list), Strings.AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            foreach (var o in orphans) EditorWindow.OpenRecovered(o);
            return orphans.Count;
        }
        if (answer == MessageBoxResult.No)
            foreach (var o in orphans) Services.Recovery.Remove(o.DocId);
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
