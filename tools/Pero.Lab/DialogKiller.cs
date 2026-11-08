using System.Runtime.InteropServices;
using System.Text;

namespace Pero.Lab;

/// <summary>
/// Закрывает всплывающие окна лицензии Syncfusion (без ключа движок показывает модальное сообщение,
/// которое блокирует автоматический прогон). Каждое закрытие записывается в журнал.
/// </summary>
public sealed class DialogKiller : IDisposable
{
    private readonly int _pid;
    private readonly Action<string> _log;
    private readonly Timer _timer;
    public int Closed { get; private set; }

    public DialogKiller(int pid, Action<string> log)
    {
        _pid = pid;
        _log = log;
        _timer = new Timer(_ => Sweep(), null, 500, 700);
    }

    private static readonly string[] Markers = ["syncfusion", "license", "licence", "trial", "лиценз"];

    private void Sweep()
    {
        try
        {
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != _pid || !IsWindowVisible(h)) return true;
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();
                string text = title + " " + ChildText(h);
                if (Markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
                {
                    _log($"DIALOG closed: «{title}» {Trim(ChildText(h))}");
                    PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
                    Closed++;
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;

    private static string ChildText(IntPtr parent)
    {
        var all = new StringBuilder();
        EnumChildWindows(parent, (h, _) =>
        {
            var sb = new StringBuilder(1024);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.Length > 0) all.Append(sb).Append(' ');
            return true;
        }, IntPtr.Zero);
        return all.ToString();
    }

    public void Dispose() => _timer.Dispose();

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
