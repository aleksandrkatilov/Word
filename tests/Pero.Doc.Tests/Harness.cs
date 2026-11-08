namespace Pero.Doc.Tests;

internal static class T
{
    public static readonly List<(string Name, Action Body)> All = [];
    public static void Test(string name, Action body) => All.Add((name, body));
    public static void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }
    public static void Eq<TV>(TV expected, TV actual, string what)
    {
        if (!EqualityComparer<TV>.Default.Equals(expected, actual)) throw new Exception($"{what}: ожидалось «{expected}», получено «{actual}»");
    }

    public static int Run()
    {
        int failed = 0;
        foreach (var (name, body) in All)
        {
            try { body(); Console.WriteLine($"  ✓ {name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"  ✗ {name}\n      {ex.Message.Replace("\n", "\n      ")}"); }
        }
        Console.WriteLine($"\n{All.Count - failed} из {All.Count} тестов пройдено.");
        return failed == 0 ? 0 : 1;
    }
}
