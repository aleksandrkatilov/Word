using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Pero.Core.Files;
using Pero.Core.Packaging;
using Pero.Services;
using Syncfusion.Windows.Controls.RichTextBoxAdv;
using Syncfusion.Windows.Tools.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Pero;

/// <summary>
/// Окно документа. Каждый документ — отдельное окно со своей историей отмены (FILE-01).
/// Файловые команды ленты перехватываются до движка: открытие идёт через проверку пакета,
/// сохранение — через атомарную запись и перенос отделимых частей.
/// </summary>
public partial class EditorWindow : RibbonWindow
{
    private static readonly List<EditorWindow> OpenWindows = [];
    private static int _untitledCounter;

    private AppServices S => App.Services;

    // состояние документа
    private string? _path;                 // путь DOCX на диске; null — новый или импортированный
    private string _displayName;
    private string _docId = Guid.NewGuid().ToString("N");
    private bool _dirty;
    private bool _changedSinceRecovery;
    private bool _compatReadOnly;          // DOCX-01: открыт только для чтения
    private bool _fileReadOnly;            // файл на диске только для чтения (FILE-04)
    private bool _loadedSomething;
    private bool _suppressDirty;
    private bool _saving;
    private bool _closeAfterSave;
    private FileSnapshot? _snapshot;
    private DetachedParts _detached = new();
    private InspectionReport? _report;
    private string? _openedCopy;           // снимок исходного DOCX на момент открытия — эталон для проверки обмена

    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _wordTimer;
    private readonly DispatcherTimer _recoveryTimer;

    public EditorWindow()
    {
        try { Syncfusion.SfSkinManager.SfSkinManager.SetTheme(this, new Syncfusion.SfSkinManager.Theme("Windows11Light")); }
        catch (Exception ex) { Log.Error("theme", ex); }
        InitializeComponent();
        OpenWindows.Add(this);
        _displayName = string.Format(Strings.Untitled, ++_untitledCounter);

        LanguageStatus.Text = Strings.StatusLanguage;
        MessageBarDetailsButton.Content = Strings.Details;
        MessageBarCopyButton.Content = Strings.SaveCopy;
        SetAutomationNames();

        // До первой разметки движок присылает ContentChanged без правок пользователя —
        // не помечаем новый документ как изменённый (иначе копия восстановления и вопрос при закрытии).
        _suppressDirty = true;

        Editor.ContentChanged += (_, _) => OnContentChanged();

        // Перехват файловых команд ленты и движка (тоннельное событие доходит до окна раньше редактора).
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewExecuted);
        AddShortcut(Key.N, ModifierKeys.Control, NewDocument);
        AddShortcut(Key.O, ModifierKeys.Control, ShowOpenDialog);
        AddShortcut(Key.S, ModifierKeys.Control, () => _ = SaveAsync(saveAs: false));
        AddShortcut(Key.S, ModifierKeys.Control | ModifierKeys.Shift, () => _ = SaveAsync(saveAs: true));
        AddShortcut(Key.F12, ModifierKeys.None, () => _ = SaveAsync(saveAs: true));
        AddShortcut(Key.P, ModifierKeys.Control, Print);
        InitNavigation();
        ApplySpellCheck();

        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
        Closing += OnClosing;
        Closed += OnClosed;
        Deactivated += (_, _) => _ = AutoRecoverAsync();
        // вкладку добавляем после применения шаблонов ленты
        Loaded += async (_, _) =>
        {
            Editor.Focus();
            await Dispatcher.InvokeAsync(CustomizeRibbon, DispatcherPriority.Loaded);   // после применения шаблонов ленты
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (!_loadedSomething) { _suppressDirty = false; _dirty = false; UpdateTitle(); }
        };

        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => UpdatePageStatus(), Dispatcher);
        _wordTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.ApplicationIdle, (_, _) => UpdateWordCount(), Dispatcher) { IsEnabled = false };
        _recoveryTimer = new DispatcherTimer(TimeSpan.FromSeconds(Math.Max(10, S.Settings.AutoRecoverSeconds)), DispatcherPriority.Background,
            (_, _) => _ = AutoRecoverAsync(), Dispatcher);

        UpdateTitle();
        UpdateWordCount();
    }

    // ── Открытие ─────────────────────────────────────────────────────────────

    /// <summary>Открыть файл: в этом окне, если оно пустое, иначе в новом; уже открытый файл активируется.</summary>
    public static void OpenInNewWindow(string path, EditorWindow? preferred = null)
    {
        string full = Path.GetFullPath(path);
        var existing = OpenWindows.FirstOrDefault(w => string.Equals(w._path, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { existing.Activate(); return; }

        var target = preferred is { IsPristine: true } ? preferred : new EditorWindow();
        if (!target.IsVisible) target.Show();
        _ = target.LoadAsync(full, recovered: null).ContinueWith(t =>
        {
            if (!t.Result && target != preferred && !target._loadedSomething && OpenWindows.Count > 1) target.Close();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public static void OpenRecovered(RecoveryEntry entry)
    {
        var w = new EditorWindow();
        w.Show();
        _ = w.LoadAsync(entry.OriginalPath ?? entry.Title, entry);
    }

    private bool IsPristine => _path is null && !_dirty && !_loadedSomething;

    private async Task<bool> LoadAsync(string path, RecoveryEntry? recovered)
    {
        string source = recovered?.DataPath(S.Recovery.Root) ?? path;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        bool isText = ext == ".txt";
        string copy = S.NewTempPath(isText ? ".txt" : ".docx");

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            using var _ = Log.Measure("open");

            // Снимок файла: исходник не блокируется, проверка и движок читают одни и те же байты.
            await Task.Run(() => File.Copy(source, copy, overwrite: true));

            InspectionReport? report = null;
            var detached = new DetachedParts();
            if (!isText)
            {
                report = await Task.Run(() => S.Inspector.Inspect(copy));
                if (report.Status == InspectionStatus.Rejected)
                {
                    Mouse.OverrideCursor = null;
                    MessageBox.Show(this, string.Format(Strings.CannotOpen, Path.GetFileName(path), report.ToUserText()),
                        Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                if (report.Status == InspectionStatus.ReadOnlyRecommended && recovered is null)
                {
                    Mouse.OverrideCursor = null;
                    var answer = MessageBox.Show(this, string.Format(Strings.ReadOnlyOpenPrompt, Path.GetFileName(path), report.ToUserText()),
                        Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (answer != MessageBoxResult.Yes) return false;
                    Mouse.OverrideCursor = Cursors.Wait;
                }
                try { detached = await Task.Run(() => DetachedParts.Capture(copy)); }
                catch (Exception ex) { Log.Error("capture-detached", ex); }
            }

            _suppressDirty = true;
            Editor.Load(copy);

            _report = report;
            _detached = detached;
            _loadedSomething = true;
            _openedCopy = isText ? null : copy;
            _compatReadOnly = report?.Status == InspectionStatus.ReadOnlyRecommended;
            Editor.IsReadOnly = _compatReadOnly;

            if (isText)
            {
                // Импорт TXT: новый документ, исходный текст не перезаписывается при Ctrl+S.
                _path = null;
                _displayName = Path.GetFileNameWithoutExtension(path);
            }
            else if (recovered is not null)
            {
                _path = recovered.OriginalPath is { } op && File.Exists(op) ? op : null;
                _displayName = recovered.Title;
                _docId = recovered.DocId;
                _snapshot = _path is null ? null : FileSnapshot.Take(_path);
            }
            else
            {
                _path = path;
                _displayName = Path.GetFileName(path);
                _snapshot = FileSnapshot.Take(path);
                _fileReadOnly = FileSnapshot.IsReadOnly(path);
                S.Recent.Add(path);
            }

            // ContentChanged от загрузки приходит асинхронно — снимаем подавление, когда разметка готова.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            _suppressDirty = false;
            _dirty = recovered is not null;
            _changedSinceRecovery = false;

            ShowMessageBarFor(recovered is not null);
            UpdateTitle();
            UpdateWordCount();
            Log.Info($"OPEN status={report?.Status} parts={report?.PartCount} detached={_detached.PartNames.Count()}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("open", ex);
            MessageBox.Show(this, string.Format(Strings.CannotOpen, Path.GetFileName(path), ex.Message),
                Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _suppressDirty = false;
            Mouse.OverrideCursor = null;
        }
    }

    private void ShowOpenDialog()
    {
        var dlg = new OpenFileDialog { Filter = Strings.FilterOpen, Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames) OpenInNewWindow(f, this);
    }

    private static void NewDocument() => new EditorWindow().Show();

    // ── Сохранение ───────────────────────────────────────────────────────────

    /// <returns>true, если документ сохранён.</returns>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_saving) return false;

        if (_compatReadOnly)
        {
            var answer = MessageBox.Show(this, string.Format(Strings.ConvertedCopyPrompt, _report?.ToUserText() ?? ""),
                Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return false;
            saveAs = true;
        }
        else if (!saveAs && _path is not null && _fileReadOnly)
        {
            MessageBox.Show(this, string.Format(Strings.FileIsReadOnly, Path.GetFileName(_path)), Strings.AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            saveAs = true;
        }
        else if (!saveAs && _path is not null && _snapshot is not null && _snapshot.ChangedOnDisk())
        {
            var answer = MessageBox.Show(this, string.Format(Strings.ExternalChange, Path.GetFileName(_path)), Strings.AppName,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) return false;
            saveAs = answer == MessageBoxResult.Yes;
        }

        string? target = _path;
        if (saveAs || target is null)
        {
            target = AskSavePath(Strings.FilterDocx, ".docx");
            if (target is null) return false;
            if ((_compatReadOnly || _fileReadOnly) && _path is not null &&
                string.Equals(Path.GetFullPath(target), _path, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, Strings.CopyMustDiffer, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        _saving = true;
        Title = $"{_displayName} — {Strings.StateSaving} — {Strings.AppName}";
        string engineOut = S.NewTempPath(".docx");
        try
        {
            using var _ = Log.Measure("save");
            Editor.Save(engineOut);                               // движок пишет только во временный файл
            var detached = _detached;
            var inspector = S.Inspector;
            await Task.Run(() => AtomicFileWriter.Write(target!, output =>
            {
                using var input = File.OpenRead(engineOut);
                var result = SavePipeline.Process(input, output, detached);
                if (result.MergeCellsRestored > 0 || result.PartsSkipped.Count > 0)
                    Log.Info($"SAVE fixups merges={result.MergeCellsRestored} skipped={result.PartsSkipped.Count}");
            },
            validate: p => inspector.Inspect(p) is { Status: InspectionStatus.Rejected } r ? r.RejectReason : null));

            _path = Path.GetFullPath(target!);
            _displayName = Path.GetFileName(_path);
            _snapshot = FileSnapshot.Take(_path);
            _fileReadOnly = false;
            _dirty = false;
            _changedSinceRecovery = false;
            if (_compatReadOnly)
            {
                // Преобразованная копия содержит только поддерживаемые конструкции — её можно править.
                _compatReadOnly = false;
                Editor.IsReadOnly = false;
                _report = S.Inspector.Inspect(_path);
                MessageBar.Visibility = Visibility.Collapsed;
            }
            S.RecoverySession.Remove(_docId);
            S.Recent.Add(_path);
            return true;
        }
        catch (SaveFailedException ex)
        {
            Log.Error("save", ex);
            MessageBox.Show(this, string.Format(Strings.SaveFailed, ex.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("save", ex);
            MessageBox.Show(this, string.Format(Strings.SaveFailed, ex.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            TryDelete(engineOut);
            _saving = false;
            UpdateTitle();
            if (_closeAfterSave) { _closeAfterSave = false; if (!_dirty) Close(); }
        }
    }

    private string? AskSavePath(string filter, string ext)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = true, // FILE-01: подтверждение замены существующего файла
            FileName = Path.ChangeExtension(_displayName, ext),
            InitialDirectory = _path is null ? null : Path.GetDirectoryName(_path),
        };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    // ── Экспорт ──────────────────────────────────────────────────────────────

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        string? target = AskSavePath(Strings.FilterPdf, ".pdf");
        if (target is null) return;
        string docx = S.NewTempPath(".docx");
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            using var _ = Log.Measure("export-pdf");
            Editor.Save(docx);
            await Task.Run(() => PdfExporter.Export(docx, target));
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, string.Format(Strings.PdfDone, target), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error("export-pdf", ex);
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, string.Format(Strings.PdfFailed, ex.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { TryDelete(docx); Mouse.OverrideCursor = null; }
    }

    private async void ExportTxt_Click(object sender, RoutedEventArgs e) => await ExportTxtAsync();

    private async Task ExportTxtAsync()
    {
        string? target = AskSavePath(Strings.FilterTxt, ".txt");
        if (target is null) return;
        string tmp = S.NewTempPath(".txt");
        try
        {
            Editor.Save(tmp);
            await Task.Run(() =>
            {
                string text = ReadTextAnyEncoding(tmp);
                AtomicFileWriter.Write(target, s =>
                {
                    using var w = new StreamWriter(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                    w.Write(text);
                });
            });
        }
        catch (Exception ex)
        {
            Log.Error("export-txt", ex);
            MessageBox.Show(this, string.Format(Strings.SaveFailed, ex.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { TryDelete(tmp); }
    }

    /// <summary>Текст из файла движка: UTF-8 (с BOM или без), иначе Windows-1251.</summary>
    private static string ReadTextAnyEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    // ── Автовосстановление (FILE-03) ─────────────────────────────────────────

    private async Task AutoRecoverAsync()
    {
        if (!_dirty || !_changedSinceRecovery || _saving) return;
        Log.Info("AUTORECOVER tick");
        _changedSinceRecovery = false;
        string tmp = S.NewTempPath(".docx");
        try
        {
            using var _ = Log.Measure("autorecover");
            Editor.Save(tmp);
            string id = _docId, title = _displayName;
            string? original = _path;
            await Task.Run(() => S.RecoverySession.Save(id, original, title, s =>
            {
                using var f = File.OpenRead(tmp);
                f.CopyTo(s);
            }));
        }
        catch (Exception ex)
        {
            _changedSinceRecovery = true; // попробуем при следующем тике
            Log.Error("autorecover", ex);
        }
        finally { TryDelete(tmp); }
    }

    // ── Закрытие (FILE-04) ───────────────────────────────────────────────────

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_saving) { e.Cancel = true; return; }
        if (!_dirty) return;
        var answer = MessageBox.Show(this, string.Format(Strings.AskSaveChanges, _displayName), Strings.AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (answer == MessageBoxResult.Yes)
        {
            e.Cancel = true;              // закроемся после успешного сохранения
            _closeAfterSave = true;
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (!await SaveAsync(saveAs: false)) _closeAfterSave = false;
            });
            return;
        }
        // «Не сохранять» — осознанный отказ, копия восстановления больше не нужна
        S.RecoverySession.Remove(_docId);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _wordTimer.Stop();
        _recoveryTimer.Stop();
        OpenWindows.Remove(this);
        if (_openedCopy is not null) TryDelete(_openedCopy);
    }

    // ── Перехват команд ленты ────────────────────────────────────────────────

    private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var cmd = e.Command;
        if (cmd == SfRichTextBoxAdv.NewDocumentCommand || cmd == ApplicationCommands.New)
        {
            e.Handled = true; NewDocument();
        }
        else if (cmd == SfRichTextBoxAdv.OpenDocumentCommand || cmd == ApplicationCommands.Open)
        {
            e.Handled = true;
            if (e.Parameter is string p && File.Exists(p)) OpenInNewWindow(p, this); else ShowOpenDialog();
        }
        else if (cmd == SfRichTextBoxAdv.SaveDocumentCommand || cmd == ApplicationCommands.Save)
        {
            e.Handled = true; _ = SaveAsync(saveAs: false);
        }
        else if (cmd == SfRichTextBoxAdv.SaveAsDocumentCommand || cmd == ApplicationCommands.SaveAs)
        {
            e.Handled = true;
            string fmt = e.Parameter?.ToString()?.ToLowerInvariant() ?? "";
            if (fmt.Contains("txt") || fmt.Contains("text")) _ = ExportTxtAsync();
            else if (fmt.Length == 0 || fmt.Contains("docx") || fmt == "word") _ = SaveAsync(saveAs: true);
            else
                // SCOPE-01: HTML, XAML и DOC не входят в P0 — команда не выполняет скрытое действие.
                MessageBox.Show(this, Strings.FormatNotInP0, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AddShortcut(Key key, ModifierKeys mods, Action action)
    {
        var cmd = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(cmd, (_, _) => action()));
        InputBindings.Add(new KeyBinding(cmd, key, mods));
    }

    // ── Перетаскивание файлов (FILE-01) ──────────────────────────────────────

    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (DroppedFiles(e).Length == 0) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        if (files.Length == 0) return;
        e.Handled = true;
        foreach (var f in files) OpenInNewWindow(f, this);
    }

    // ── Состояние и строка состояния ─────────────────────────────────────────

    private void OnContentChanged()
    {
        if (_suppressDirty) return;
        if (!_dirty) Log.Info("DIRTY first change");
        _dirty = true;
        _changedSinceRecovery = true;
        _wordTimer.Stop();
        _wordTimer.Start();
        UpdateTitle();
    }

    private string TitleState() =>
        _compatReadOnly || _fileReadOnly ? Strings.StateReadOnly
        : _dirty ? Strings.StateUnsaved
        : _path is null && !_loadedSomething ? ""
        : Strings.StateSaved;

    private void UpdateTitle()
    {
        string state = TitleState();
        Title = state.Length == 0 ? $"{_displayName} — {Strings.AppName}" : $"{_displayName} • {state} — {Strings.AppName}";
    }

    private void UpdatePageStatus()
    {
        try
        {
            int count = Editor.PageCount;
            int page = Math.Max(1, Editor.CurrentPageNumber);
            PageStatus.Text = string.Format(Strings.StatusPage, count == 0 ? 1 : page, Math.Max(1, count));
        }
        catch { /* до первой разметки */ }
    }

    private void UpdateWordCount()
    {
        _wordTimer.Stop();
        int? words = WordCounter.Count(Editor.Document);
        RefreshHeadings();
        WordStatus.Text = string.Format(Strings.StatusWords, words?.ToString("N0") ?? "—");
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(500, Math.Round(ZoomSlider.Value / 10) * 10 + 10);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Max(25, Math.Round(ZoomSlider.Value / 10) * 10 - 10);

    // ── Панель сообщений и отчёт совместимости ───────────────────────────────

    private void ShowMessageBarFor(bool recovered)
    {
        string? text = recovered ? Strings.RecoveredBanner
            : _compatReadOnly ? Strings.ReadOnlyBanner
            : _fileReadOnly ? Strings.FileReadOnlyBanner
            : _report?.Status == InspectionStatus.OkWithWarnings ? Strings.WarningsBanner
            : null;
        MessageBar.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        MessageBarText.Text = text ?? "";
        MessageBarDetailsButton.Visibility = _report is null || recovered ? Visibility.Collapsed : Visibility.Visible;
        MessageBarCopyButton.Visibility = _compatReadOnly || _fileReadOnly || recovered ? Visibility.Visible : Visibility.Collapsed;
        MessageBarCopyButton.Content = recovered ? Strings.Save : Strings.SaveCopy;
    }

    private void CloseMessageBar_Click(object sender, RoutedEventArgs e) => MessageBar.Visibility = Visibility.Collapsed;

    private void SaveCopy_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(saveAs: _compatReadOnly || _fileReadOnly || _path is null);

    private void ShowReport_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) { MessageBox.Show(this, Strings.NeedSavedFile, Strings.AppName); return; }
        var sb = new StringBuilder(_report.ToUserText()).AppendLine();
        foreach (var f in _report.Findings)
            sb.AppendLine($"{f.Entry.Title}: {f.Count} — чтение {f.Entry.Read}, отображение {f.Entry.Display}, правка {f.Entry.Edit}, сохранение {f.Entry.Save}{(f.Entry.Verified ? "" : " (предв.)")}");
        if (!_detached.IsEmpty)
            sb.AppendLine().AppendLine("Переносятся при сохранении: " + string.Join(", ", _detached.PartNames));
        ShowTextWindow(Strings.CompatReportTitle, sb.ToString());
    }

    /// <summary>QA-01 для текущего документа: записать DOCX и сравнить со снимком на момент открытия.</summary>
    private async void RoundTrip_Click(object sender, RoutedEventArgs e)
    {
        if (_openedCopy is null || !File.Exists(_openedCopy)) { MessageBox.Show(this, Strings.NeedSavedFile, Strings.AppName); return; }
        string engineOut = S.NewTempPath(".docx"), merged = S.NewTempPath(".docx");
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            Editor.Save(engineOut);
            var detached = _detached;
            string before = _openedCopy;
            var diffs = await Task.Run(() =>
            {
                using (var input = File.OpenRead(engineOut))
                using (var output = File.Create(merged))
                    SavePipeline.Process(input, output, detached);
                return DocxFingerprint.FromFile(before).CompareTo(DocxFingerprint.FromFile(merged));
            });
            Mouse.OverrideCursor = null;
            string text = diffs.Count == 0 ? Strings.RoundTripOk
                : string.Format(Strings.RoundTripLoss, string.Join(Environment.NewLine,
                    diffs.Select(d => $"{(d.IsLoss ? "✗" : "~")} {d.Property}: {d.Before} → {d.After}")));
            ShowTextWindow(Strings.RoundTrip, text + (_dirty ? Environment.NewLine + Environment.NewLine +
                Strings.UnsavedEditsNote : ""));
        }
        catch (Exception ex)
        {
            Log.Error("roundtrip", ex);
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, ex.Message, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { TryDelete(engineOut); TryDelete(merged); Mouse.OverrideCursor = null; }
    }

    private void ShowTextWindow(string title, string text)
    {
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = System.Windows.TextWrapping.Wrap, BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13, Padding = new Thickness(12),
        };
        new Window { Title = title, Owner = this, Width = 640, Height = 460, Content = box, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>Подписи для экранного диктора и UI Automation (раздел «Доступность»).</summary>
    private void SetAutomationNames()
    {
        void N(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);
        N(Editor, Strings.IsRussian ? "Документ" : "Document");
        N(MessageBarClose, Strings.IsRussian ? "Закрыть сообщение" : "Close message");
        N(ZoomSlider, Strings.IsRussian ? "Масштаб" : "Zoom");
        N(ZoomInButton, Strings.IsRussian ? "Увеличить масштаб" : "Zoom in");
        N(ZoomOutButton, Strings.IsRussian ? "Уменьшить масштаб" : "Zoom out");
        N(NavClose, Strings.IsRussian ? "Закрыть область навигации" : "Close navigation pane");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
