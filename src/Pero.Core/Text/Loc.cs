using System.Globalization;

namespace Pero.Core.Text;

/// <summary>
/// Interface language. Source strings are written in Russian; when English is selected
/// <see cref="L"/> and <see cref="F"/> return the English text. The application chooses the
/// language at start-up (English by default); libraries and tests keep Russian unless told otherwise.
/// </summary>
public static partial class Loc
{
    public static bool English { get; private set; }

    public static string Code => English ? "en" : "ru";

    public static void Use(string? code) =>
        English = !string.Equals(code?.Trim(), "ru", StringComparison.OrdinalIgnoreCase);

    public static string L(string ru) => English && En.TryGetValue(ru, out var en) ? en : ru;

    public static string F(string ru, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, L(ru), args);

    /// <summary>Russian source strings that have an English text (for tests).</summary>
    public static IReadOnlyCollection<string> Keys => En.Keys;

    public static string? EnglishFor(string ru) => En.TryGetValue(ru, out var en) ? en : null;
}
