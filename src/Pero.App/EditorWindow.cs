using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Pero.Core.Files;
using Pero.Core.Packaging;
using Pero.Doc.Docx;
using Pero.Doc.Editing;
using Pero.Doc.Fonts;
using Pero.Doc.Model;
using Orientation = System.Windows.Controls.Orientation;
using Pero.Editor;
using Pero.Services;
using Pero.Ui;

namespace Pero;

/// <summary>
/// Окно документа (UI-01…UI-07): название и статус, меню, одна компактная панель, две линейки и страницы.
/// Каждый документ — отдельное окно со своей историей отмены. Открытие проходит проверку пакета,
/// сохранение — атомарную запись с проверкой результата и резервной версией прежнего файла.
/// </summary>
public sealed partial class EditorWindow : Window
{
    private static readonly List<EditorWindow> OpenWindows = [];
    private static int _untitledCounter;

    private AppServices S => App.Services;

    private readonly DocumentView _view;
    private readonly ScrollViewer _scroll;
    private readonly HorizontalRuler _hRuler;
    private readonly VerticalRuler _vRuler;
    private readonly TextBlock _nameText = new() { FontSize = 17, Foreground = Theme.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _statusText = new() { FontSize = 12, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 2, 0, 0) };
    private readonly TextBlock _pageText = new() { FontSize = 12, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Border _messageBar = new() { Background = Theme.Banner, Padding = new Thickness(12, 6, 8, 6), Visibility = Visibility.Collapsed };
    private readonly TextBlock _messageText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Ink };
    private readonly Button _messageAction = new() { Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), Visibility = Visibility.Collapsed };
    private Action? _messageActionHandler;
    private readonly ColumnDefinition _navColumn = new() { Width = new GridLength(0) };
    private readonly ColumnDefinition _vRulerColumn = new() { Width = new GridLength(22) };
    private readonly RowDefinition _hRulerRow = new() { Height = new GridLength(22) };

    // состояние документа
    private string? _path;
    private string _displayName;
    private string _docId = Guid.NewGuid().ToString("N");
    private bool _changedSinceRecovery;
    private bool _compatReadOnly;
    private bool _fileReadOnly;
    private bool _lockedElsewhere;
    private bool _loadedSomething;
    private bool _saving;
    private bool _closeAfterSave;
    private FileSnapshot? _snapshot;
    private InspectionReport? _report;
    private string? _openedCopy;
    private FileLock? _lock;
    private bool _forceDirty;

    private readonly DispatcherTimer _recoveryTimer;
    private readonly DispatcherTimer _uiTimer;

    private DocEditor Ed => _view.Editor;
    private bool ReadOnly => _compatReadOnly || _lockedElsewhere;
    private bool Dirty => Ed.IsDirty || _forceDirty;

    public EditorWindow()
    {
        OpenWindows.Add(this);
        int n = ++_untitledCounter;
        _displayName = n == 1 ? Loc.L("Новый документ") : Loc.F("Новый документ {0}", n);
        Title = Strings.AppName;
        Width = 1366;
        Height = 768;
        MinWidth = 760;
        MinHeight = 480;
        Background = Theme.Window;
        try { Icon = AppIcon(256); } catch (Exception ex) { Log.Error("icon", ex); }
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowStartupLocation = OpenWindows.Count > 1 ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;
        if (OpenWindows.Count > 1) { Left = 40 + 24 * (OpenWindows.Count % 8); Top = 40 + 24 * (OpenWindows.Count % 8); }

        _view = new DocumentView(new DocEditor(Doc.Model.Document.CreateBlank()));
        AutomationProperties.SetName(_view, Loc.L("Документ"));
        AutomationProperties.SetAutomationId(_view, "DocumentView");
        _scroll = new ScrollViewer
        {
            CanContentScroll = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = _view,
            Focusable = false,
        };
        _hRuler = new HorizontalRuler(_view);
        _vRuler = new VerticalRuler(_view);
        AutomationProperties.SetName(_hRuler, Loc.L("Горизонтальная линейка"));
        AutomationProperties.SetName(_vRuler, Loc.L("Вертикальная линейка"));

        BuildCommands();
        Content = BuildLayout();
        BindShortcuts();
        HookView();
        ApplyRulerSettings();

        AllowDrop = true;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
        Closing += OnClosing;
        Closed += OnClosed;
        Deactivated += (_, _) => _ = AutoRecoverAsync();
        Loaded += (_, _) => { _view.Focus(); UpdateAll(); TuneToolbarOverflow(); };

        _recoveryTimer = new DispatcherTimer(TimeSpan.FromSeconds(Math.Max(10, S.Settings.AutoRecoverSeconds)), DispatcherPriority.Background,
            (_, _) => _ = AutoRecoverAsync(), Dispatcher);
        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => { _uiTimer!.Stop(); RefreshNavigation(); }, Dispatcher);
        UpdateTitle();
    }

    /// <summary>Значок приложения из ресурсов сборки (работает и при создании окна из другой сборки).</summary>
    private static System.Windows.Media.Imaging.BitmapImage AppIcon(int size)
    {
        var bi = new System.Windows.Media.Imaging.BitmapImage();
        bi.BeginInit();
        bi.UriSource = new Uri($"pack://application:,,,/RWrite;component/Assets/rilvo-{(size <= 64 ? 64 : 256)}.png", UriKind.Absolute);
        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { LastChildFill = true };

        // строка названия: значок, имя файла, статус
        var title = new Grid { Height = 36, Margin = new Thickness(12, 2, 0, 0) };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new System.Windows.Controls.Image { Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0), Source = AppIcon(64) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var names = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(_nameText);
        names.Children.Add(_statusText);
        AutomationProperties.SetAutomationId(_nameText, "DocumentName");
        AutomationProperties.SetAutomationId(_statusText, "DocumentStatus");
        Grid.SetColumn(names, 1);
        Grid.SetColumn(_pageText, 2);
        title.Children.Add(icon);
        title.Children.Add(names);
        title.Children.Add(_pageText);
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // панель сообщений (только чтение, восстановление, замена шрифтов)
        var mb = new DockPanel();
        var close = Theme.ToolButton(Loc.L("Закрыть сообщение"), Theme.IconPath(Theme.Icon.Close, 14), null, (_, _) => _messageBar.Visibility = Visibility.Collapsed);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_messageAction, Dock.Right);
        _messageAction.Click += (_, _) => _messageActionHandler?.Invoke();
        mb.Children.Add(close);
        mb.Children.Add(_messageAction);
        mb.Children.Add(_messageText);
        _messageBar.Child = mb;
        AutomationProperties.SetAutomationId(_messageBar, "MessageBar");
        DockPanel.SetDock(_messageBar, Dock.Top);
        root.Children.Add(_messageBar);

        var find = BuildFindBar();
        DockPanel.SetDock(find, Dock.Top);
        root.Children.Add(find);

        // линейки и страницы
        var grid = new Grid();
        grid.ColumnDefinitions.Add(_navColumn);
        grid.ColumnDefinitions.Add(_vRulerColumn);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(_hRulerRow);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var nav = BuildNavigation();
        Grid.SetRowSpan(nav, 2);
        Grid.SetColumn(_hRuler, 2);
        Grid.SetRow(_vRuler, 1); Grid.SetColumn(_vRuler, 1);
        Grid.SetRow(_scroll, 1); Grid.SetColumn(_scroll, 2);
        var corner = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF8)) };
        Grid.SetColumn(corner, 1);
        grid.Children.Add(nav);
        grid.Children.Add(corner);
        grid.Children.Add(_hRuler);
        grid.Children.Add(_vRuler);
        grid.Children.Add(_scroll);
        root.Children.Add(grid);
        return root;
    }

    private void HookView()
    {
        _view.CaretMoved += (_, _) => { UpdateFormatUi(); UpdatePageText(); PainterApply(); };
        _view.LayoutChanged += (_, _) => { UpdatePageText(); _uiTimer.Stop(); _uiTimer.Start(); };
        _view.ReadOnlyAttempt += (_, _) => ShowMessage(ReadOnlyReason(), null, null);
        _view.StoryChanged += (_, _) => UpdateFormatUi();
        _view.LinkClicked += (_, link) => OpenLink(link);
        _view.ContextMenu = new ContextMenu();
        _view.ContextMenuOpening += (_, _) => BuildContextMenu(_view.ContextMenu);
        _view.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(PainterApply, DispatcherPriority.Input);
        HookEditor(Ed);
    }

    private void HookEditor(DocEditor ed)
    {
        ed.Changed += (_, _) =>
        {
            _changedSinceRecovery = true;
            UpdateTitle();
            UpdateFormatUi();
            if (_findBar.Visibility == Visibility.Visible) RefreshMatches();
        };
    }

    // ── открытие ─────────────────────────────────────────────────────────────

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

    private bool IsPristine => _path is null && !Dirty && !_loadedSomething;

    private async Task<bool> LoadAsync(string path, RecoveryEntry? recovered, string? versionTitle = null)
    {
        string source = recovered?.DataPath(S.Recovery.Root) ?? path;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        bool isText = ext == ".txt";
        string copy = S.NewTempPath(isText ? ".txt" : ".docx");
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            using var _ = Log.Measure("open");
            await Task.Run(() => File.Copy(source, copy, overwrite: true));

            Doc.Model.Document doc;
            InspectionReport? report = null;
            if (isText)
            {
                string text = await Task.Run(() => ReadTextAnyEncoding(copy));
                doc = Doc.Model.Document.CreateBlank();
                var tmp = new DocEditor(doc);
                tmp.InsertPlainText(text);
                doc = tmp.Doc;
            }
            else
            {
                report = await Task.Run(() => S.Inspector.Inspect(copy));
                if (report.Status == InspectionStatus.Rejected)
                {
                    Mouse.OverrideCursor = null;
                    MessageBox.Show(this, string.Format(Strings.CannotOpen, Path.GetFileName(path), report.ToUserText()),
                        Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                doc = await Task.Run(() => DocxReader.Read(copy));
                await Task.Run(() => FontCatalog.Shared);
            }

            var editor = new DocEditor(doc);
            _view.Attach(editor);
            HookEditor(editor);
            _report = report;
            _loadedSomething = true;
            _openedCopy = isText || recovered is not null ? null : copy;
            _compatReadOnly = report?.Status == InspectionStatus.ReadOnlyRecommended;
            _lockedElsewhere = false;
            _lock?.Dispose();
            _lock = null;

            if (isText || versionTitle is not null)
            {
                // импорт TXT и открытие резервной версии — новый документ, исходник не перезаписывается
                _path = null;
                _displayName = versionTitle ?? Path.GetFileNameWithoutExtension(path);
                _forceDirty = versionTitle is not null;
            }
            else if (recovered is not null)
            {
                _path = recovered.OriginalPath is { } op && File.Exists(op) ? op : null;
                _displayName = recovered.Title;
                _docId = recovered.DocId;
                _snapshot = _path is null ? null : FileSnapshot.Take(_path);
                _forceDirty = true;
            }
            else
            {
                _path = path;
                _displayName = Path.GetFileName(path);
                _snapshot = FileSnapshot.Take(path);
                _fileReadOnly = FileSnapshot.IsReadOnly(path);
                _lock = new FileLock(path);
                _lockedElsewhere = !_lock.Acquired;
                S.Recent.Add(path);
            }
            _view.IsReadOnly = ReadOnly;
            _changedSinceRecovery = false;
            ShowOpenMessages(recovered is not null, doc);
            UpdateAll();
            Log.Info($"OPEN status={report?.Status} pages={_view.Layout.Pages.Count}");
            _view.Focus();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("open", ex);
            MessageBox.Show(this, string.Format(Strings.CannotOpen, Path.GetFileName(path), Loc.L("Файл повреждён или имеет неподдерживаемый формат (") + ex.Message + ")."),
                Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private string ReadOnlyReason() =>
        _lockedElsewhere ? Loc.L("Файл уже открыт в другом окне RWrite. Здесь он открыт только для чтения — изменения можно сохранить копией.")
        : _compatReadOnly ? Loc.L("Только чтение: в документе есть содержимое, которое RWrite пока не умеет править безопасно (") + (_report?.ToUserText() ?? "") + Loc.L("). Исходный файл не изменяется; можно сохранить копию.")
        : Loc.L("Документ открыт только для чтения.");

    private void ShowOpenMessages(bool recovered, Doc.Model.Document doc)
    {
        if (recovered) { ShowMessage(Strings.RecoveredBanner, Loc.L("Сохранить"), () => _ = SaveAsync(false)); return; }
        if (ReadOnly) { ShowMessage(ReadOnlyReason(), Loc.L("Сохранить копию"), () => _ = SaveAsync(true)); return; }
        if (_fileReadOnly) { ShowMessage(Strings.FileReadOnlyBanner, Loc.L("Сохранить копию"), () => _ = SaveAsync(true)); return; }
        // SAVE-04: отсутствующие шрифты — обычными словами
        var missing = doc.UsedFonts().Where(f => !FontCatalog.Shared.HasFamily(f)).Take(3)
            .Select(f => Loc.F("«{0}» (показан «{1}»)", f, FontCatalog.Shared.SubstituteFor(f) ?? FontCatalog.DefaultFamily)).ToList();
        if (missing.Count > 0)
        {
            ShowMessage(Loc.F("На компьютере нет шрифта {0}. Вид может немного отличаться от Word; в файле шрифт сохранится.", string.Join(", ", missing)), null, null);
            return;
        }
        if (_report?.Status == InspectionStatus.OkWithWarnings) { ShowMessage(Strings.WarningsBanner, Loc.L("Подробнее"), ShowReport); return; }
        _messageBar.Visibility = Visibility.Collapsed;
    }

    private void ShowMessage(string text, string? action, Action? handler)
    {
        _messageText.Text = text;
        _messageAction.Content = action;
        _messageAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        _messageActionHandler = handler;
        _messageBar.Visibility = Visibility.Visible;
    }

    private void ShowOpenDialog()
    {
        var dlg = new OpenFileDialog { Filter = Strings.FilterOpen, Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames) OpenInNewWindow(f, this);
    }

    private static void NewDocument() => new EditorWindow().Show();

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

    // ── сохранение ───────────────────────────────────────────────────────────

    /// <returns>true, если документ сохранён.</returns>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_saving) return false;
        bool mustCopy = ReadOnly || _fileReadOnly;
        if (!saveAs && _path is not null && mustCopy)
        {
            MessageBox.Show(this, _fileReadOnly ? string.Format(Strings.FileIsReadOnly, Path.GetFileName(_path)) : ReadOnlyReason(),
                Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (mustCopy && _path is not null && string.Equals(Path.GetFullPath(target), _path, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, Strings.CopyMustDiffer, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            if (OpenWindows.Any(w => w != this && string.Equals(w._path, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, Loc.L("Этот файл открыт в другом окне. Выберите другое имя."), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        _saving = true;
        _statusText.Text = Strings.StateSaving;
        try
        {
            using var _ = Log.Measure("save");
            var snapshot = Ed.Doc.SnapshotForSave();
            var inspector = S.Inspector;
            var backups = S.Backups;
            string dest = target!;
            await Task.Run(() => SafeSave.Save(snapshot, dest, inspector, backups));

            string full = Path.GetFullPath(dest);
            if (!string.Equals(full, _path, StringComparison.OrdinalIgnoreCase))
            {
                _lock?.Dispose();
                _lock = new FileLock(full);
            }
            _path = full;
            _displayName = Path.GetFileName(_path);
            _snapshot = FileSnapshot.Take(_path);
            _fileReadOnly = false;
            if (_lockedElsewhere && _lock?.Acquired == true) { _lockedElsewhere = false; _view.IsReadOnly = ReadOnly; }
            Ed.MarkSaved();
            _forceDirty = false;
            _changedSinceRecovery = false;
            if (!ReadOnly) _messageBar.Visibility = Visibility.Collapsed;
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
            MessageBox.Show(this, string.Format(Strings.SaveFailed, ex.Message + Loc.L(" Исходный файл не изменён.")), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _saving = false;
            UpdateTitle();
            if (_closeAfterSave) { _closeAfterSave = false; if (!Dirty) Close(); }
        }
    }

    private string? AskSavePath(string filter, string ext)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.ChangeExtension(_displayName, ext),
            InitialDirectory = _path is null ? null : Path.GetDirectoryName(_path),
        };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private async Task ExportPdfAsync()
    {
        string? target = AskSavePath(Strings.FilterPdf, ".pdf");
        if (target is null) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            using var _ = Log.Measure("export-pdf");
            var snap = Ed.Doc.SnapshotForSave();
            string title = Path.GetFileNameWithoutExtension(_displayName);
            await Task.Run(() =>
            {
                var cat = FontCatalog.Shared;
                var layout = Doc.Layout.DocumentLayout.Build(snap, new FontMeasurer(cat));
                var drawer = new Doc.Render.PageDrawer(cat, (img, story) => snap.ImageBytes(img.RelId, story?.PartPath));
                string tmp = target + ".tmp";
                Doc.Render.PdfExport.Write(layout, drawer, cat, tmp, title);
                File.Move(tmp, target, overwrite: true);
            });
            Mouse.OverrideCursor = null;
            ShowMessage(string.Format(Strings.PdfDone, target), Loc.L("Открыть папку"), () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true }); } catch { }
            });
        }
        catch (Exception ex)
        {
            Log.Error("export-pdf", ex);
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, string.Format(Strings.PdfFailed, ex.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private void ExportTxt()
    {
        string? target = AskSavePath(Strings.FilterTxt, ".txt");
        if (target is null) return;
        try
        {
            string text = string.Join("\r\n", Ed.Doc.AllParagraphs().Select(p => p.Text.Replace("￼", "").Replace('\n', '\v')));
            AtomicFileWriter.Write(target, s =>
            {
                using var w = new StreamWriter(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                w.Write(text);
            });
        }
        catch (Exception ex)
        {
            Log.Error("export-txt", ex);
            MessageBox.Show(this, string.Format(Strings.SaveFailed, ex.Message), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── автовосстановление (REL-02) ──────────────────────────────────────────

    private async Task AutoRecoverAsync()
    {
        if (!Dirty || !_changedSinceRecovery || _saving) return;
        _changedSinceRecovery = false;
        try
        {
            using var _ = Log.Measure("autorecover");
            var snap = Ed.Doc.SnapshotForSave();
            string id = _docId, title = _displayName;
            string? original = _path;
            await Task.Run(() => S.RecoverySession.Save(id, original, title, s => DocxWriter.Write(snap, s)));
        }
        catch (Exception ex)
        {
            _changedSinceRecovery = true;
            Log.Error("autorecover", ex);
        }
    }

    // ── закрытие ─────────────────────────────────────────────────────────────

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_saving) { e.Cancel = true; return; }
        if (!Dirty) return;
        var answer = MessageBox.Show(this, string.Format(Strings.AskSaveChanges, _displayName), Strings.AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (answer == MessageBoxResult.Yes)
        {
            e.Cancel = true;
            _closeAfterSave = true;
            _ = Dispatcher.InvokeAsync(async () => { if (!await SaveAsync(saveAs: false)) _closeAfterSave = false; });
            return;
        }
        S.RecoverySession.Remove(_docId);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _recoveryTimer.Stop();
        _uiTimer.Stop();
        _lock?.Dispose();
        OpenWindows.Remove(this);
        if (_openedCopy is not null) try { File.Delete(_openedCopy); } catch { }
    }

    // ── перетаскивание файлов ────────────────────────────────────────────────

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

    // ── состояние окна ───────────────────────────────────────────────────────

    private string TitleState() =>
        _saving ? Strings.StateSaving
        : ReadOnly || _fileReadOnly ? Strings.StateReadOnly
        : Dirty ? Strings.StateUnsaved
        : _path is null ? ""
        : Strings.StateSaved;

    private void UpdateTitle()
    {
        _nameText.Text = _displayName;
        _statusText.Text = TitleState();
        string state = TitleState();
        Title = state.Length == 0 ? $"{_displayName} — {Strings.AppName}" : $"{_displayName} • {state} — {Strings.AppName}";
        if (Dirty && !_recoveryTimer.IsEnabled) _recoveryTimer.Start();
    }

    private void UpdatePageText()
    {
        int count = Math.Max(1, _view.Layout.Pages.Count);
        _pageText.Text = string.Format(Strings.StatusPage, Math.Min(count, _view.CaretPage + 1), count);
    }

    private void UpdateAll()
    {
        UpdateTitle();
        UpdateFormatUi();
        UpdatePageText();
        RefreshNavigation();
    }

    private void ApplyRulerSettings()
    {
        _hRulerRow.Height = new GridLength(S.Settings.ShowHorizontalRuler ? 22 : 0);
        _vRulerColumn.Width = new GridLength(S.Settings.ShowVerticalRuler ? 22 : 0);
        _hRuler.Visibility = S.Settings.ShowHorizontalRuler ? Visibility.Visible : Visibility.Collapsed;
        _vRuler.Visibility = S.Settings.ShowVerticalRuler ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenLink(LinkRef link)
    {
        if (link.Url is null)
        {
            // закладка внутри документа: переход к абзацу с закладкой
            ShowMessage(Loc.L("Переход по закладкам внутри документа пока не поддерживается."), null, null);
            return;
        }
        var (decision, uri, reason) = Pero.Core.Security.LinkPolicy.Evaluate(link.Url);
        if (decision == Pero.Core.Security.LinkDecision.Block || uri is null)
        {
            MessageBox.Show(this, string.Format(Strings.LinkBlocked, link.Url, reason), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this, string.Format(Strings.LinkConfirm, uri), Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("open-link", ex); }
    }
}
