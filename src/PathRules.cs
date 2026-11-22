namespace DuplicateTracker;

internal static class PathRules
{
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length == 3 && full[1] == ':' && (full[2] == '\\' || full[2] == '/'))
            return char.ToUpperInvariant(full[0]) + ":\\";

        return full.TrimEnd('\\', '/');
    }

    public static bool Same(string left, string right) =>
        StringComparer.OrdinalIgnoreCase.Equals(Normalize(left), Normalize(right));

    public static bool IsStrictChild(string child, string parent)
    {
        var childPath = Normalize(child);
        var parentPath = Normalize(parent);
        if (StringComparer.OrdinalIgnoreCase.Equals(childPath, parentPath))
            return false;

        var prefix = parentPath.EndsWith('\\') ? parentPath : parentPath + "\\";
        return childPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameOrChild(string path, string parent) =>
        Same(path, parent) || IsStrictChild(path, parent);

    public static string Win32(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
            return full;

        if (full.Length < 240)
            return full;

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + full[2..];

        return @"\\?\" + full;
    }

    public static (IReadOnlyList<string> Roots, IReadOnlyList<string> Dropped) NormalizeRoots(IReadOnlyList<string> roots)
    {
        var full = roots
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToList();

        var kept = new List<string>();
        var dropped = new List<string>();
        foreach (var root in full)
        {
            var container = kept.FirstOrDefault(parent => IsStrictChild(root, parent));
            if (container is null)
                kept.Add(root);
            else
                dropped.Add(root);
        }

        return (kept, dropped);
    }
}
