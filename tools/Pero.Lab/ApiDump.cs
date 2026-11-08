using System.Reflection;
using System.Text;

namespace Pero.Lab;

/// <summary>Выгрузка публичного API сборок движка в текст — чтобы писать код по фактическим сигнатурам.</summary>
public static class ApiDump
{
    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var asms = new (Assembly Asm, Func<Type, bool> Filter)[]
        {
            (typeof(Syncfusion.Windows.Controls.RichTextBoxAdv.SfRichTextBoxAdv).Assembly, _ => true),
            (typeof(Syncfusion.Windows.Controls.RichTextBoxAdv.SfRichTextRibbon).Assembly, _ => true),
            (typeof(Syncfusion.Windows.Tools.Controls.Ribbon).Assembly,
                t => t.Name.Contains("Ribbon") || t.Name.Contains("BackStage") || t.Name.Contains("Backstage") || t.Name == "SizeForm"),
        };
        foreach (var (asm, filter) in asms)
        {
            var sb = new StringBuilder();
            Type[] types;
            try { types = asm.GetExportedTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            foreach (var t in types.Where(filter).OrderBy(t => t.FullName))
                DumpType(sb, t);
            File.WriteAllText(Path.Combine(outDir, asm.GetName().Name + ".txt"), sb.ToString());
        }
    }

    private static void DumpType(StringBuilder sb, Type t)
    {
        string kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : "class";
        sb.Append($"{kind} {t.FullName}");
        if (t.BaseType is { } b && b != typeof(object) && !t.IsEnum && !t.IsValueType) sb.Append($" : {Name(b)}");
        sb.AppendLine();
        if (t.IsEnum)
        {
            sb.AppendLine("  = " + string.Join(", ", Enum.GetNames(t)));
            return;
        }
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        try
        {
            foreach (var p in t.GetProperties(F))
                sb.AppendLine($"  P {(p.GetMethod?.IsStatic == true ? "static " : "")}{Name(p.PropertyType)} {p.Name} {{{(p.CanRead ? "get;" : "")}{(p.SetMethod?.IsPublic == true ? "set;" : "")}}}");
            foreach (var e in t.GetEvents(F))
                sb.AppendLine($"  E {e.Name} : {Name(e.EventHandlerType!)}");
            foreach (var f in t.GetFields(F).Where(f => !f.Name.EndsWith("Property", StringComparison.Ordinal) || f.FieldType.Name != "DependencyProperty"))
                sb.AppendLine($"  F {(f.IsStatic ? "static " : "")}{Name(f.FieldType)} {f.Name}");
            foreach (var m in t.GetMethods(F).Where(m => !m.IsSpecialName))
                sb.AppendLine($"  M {(m.IsStatic ? "static " : "")}{Name(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => Name(p.ParameterType) + " " + p.Name))})");
            foreach (var c in t.GetConstructors())
                sb.AppendLine($"  C ({string.Join(", ", c.GetParameters().Select(p => Name(p.ParameterType) + " " + p.Name))})");
        }
        catch (Exception ex) { sb.AppendLine($"  ! {ex.GetType().Name}: {ex.Message}"); }
    }

    private static string Name(Type t)
    {
        if (!t.IsGenericType) return t.Name;
        return t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">";
    }
}
