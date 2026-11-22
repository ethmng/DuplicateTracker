namespace DuplicateTracker;

internal static class FileActions
{
    public static ActionOutcome Delete(ActionPlan plan, IReadOnlyList<string> roots, bool recycle, bool removeEmptyDirectories)
    {
        var outcome = new ActionOutcome();
        foreach (var group in plan.Groups)
        {
            foreach (var extra in group.Extras)
            {
                if (PathRules.Same(extra.FullPath, group.Keeper.FullPath))
                {
                    outcome.Failed++;
                    outcome.Failures.Add($"{extra.FullPath}: refused, this is the copy marked to keep.");
                    continue;
                }

                try
                {
                    if (!File.Exists(extra.FullPath))
                    {
                        outcome.Removed++;
                        outcome.RemovedPaths.Add(extra.FullPath);
                        continue;
                    }

                    if (recycle)
                        RecycleBin.Send(extra.FullPath);
                    else
                        DeleteFile(extra.FullPath);

                    outcome.Removed++;
                    outcome.RemovedPaths.Add(extra.FullPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    outcome.Failed++;
                    outcome.Failures.Add($"{extra.FullPath}: {OneLine(ex.Message)}");
                }
            }
        }

        if (removeEmptyDirectories)
            RemoveEmpty(roots, outcome);

        return outcome;
    }

    public static ActionOutcome Move(ActionPlan plan, IReadOnlyList<string> roots, string batch, bool removeEmptyDirectories)
    {
        var outcome = new ActionOutcome();
        if (roots.Any(root => PathRules.IsSameOrChild(batch, root)))
            throw new IOException("The destination is inside a folder you scanned. Pick a folder outside it.");

        if (plan.ExtraFiles > 0)
            Directory.CreateDirectory(batch);

        foreach (var group in plan.Groups)
        {
            foreach (var extra in group.Extras)
            {
                if (PathRules.Same(extra.FullPath, group.Keeper.FullPath))
                {
                    outcome.Failed++;
                    outcome.Failures.Add($"{extra.FullPath}: refused, this is the copy marked to keep.");
                    continue;
                }

                try
                {
                    if (!File.Exists(extra.FullPath))
                    {
                        outcome.Failed++;
                        outcome.Failures.Add($"{extra.FullPath}: the file is already gone.");
                        continue;
                    }

                    var root = roots.FirstOrDefault(candidate => PathRules.IsSameOrChild(extra.FullPath, candidate))
                        ?? Path.GetPathRoot(extra.FullPath)
                        ?? "";

                    var relative = Path.GetRelativePath(root, extra.FullPath);
                    var rootName = RootLabel(root);
                    var target = UniquePath(Path.Combine(batch, rootName, relative));
                    var parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    MoveFile(extra.FullPath, target);
                    outcome.Removed++;
                    outcome.RemovedPaths.Add(extra.FullPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    outcome.Failed++;
                    outcome.Failures.Add($"{extra.FullPath}: {OneLine(ex.Message)}");
                }
            }
        }

        if (removeEmptyDirectories)
            RemoveEmpty(roots, outcome);

        return outcome;
    }

    public static string BatchFolder(string destination) =>
        Path.Combine(
            Path.GetFullPath(destination),
            DateTime.Now.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture));

    public static List<string> PredictEmptyDirectories(IReadOnlyList<string> roots, IReadOnlyCollection<string> removedFiles)
    {
        var removed = new HashSet<string>(removedFiles.Select(PathRules.Normalize), StringComparer.OrdinalIgnoreCase);
        var rootSet = roots.Select(PathRules.Normalize).ToList();
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in removed)
        {
            var directory = Path.GetDirectoryName(file);
            while (!string.IsNullOrEmpty(directory) && IsUnderRoot(directory, rootSet))
            {
                candidates.Add(PathRules.Normalize(directory));
                directory = Path.GetDirectoryName(directory);
            }
        }

        var ordered = candidates
            .OrderByDescending(path => path.Count(character => character is '\\' or '/'))
            .ThenByDescending(path => path.Length)
            .ToList();

        var willRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in ordered)
        {
            if (WouldBeEmpty(directory, removed, willRemove))
                willRemove.Add(directory);
        }

        return willRemove
            .OrderByDescending(path => path.Count(character => character is '\\' or '/'))
            .ThenByDescending(path => path.Length)
            .ToList();
    }

    public static void ForgetRemoved(List<DuplicateGroup> groups, IReadOnlyCollection<string> removedPaths)
    {
        var removed = new HashSet<string>(removedPaths.Select(PathRules.Normalize), StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
            group.Files.RemoveAll(file => removed.Contains(PathRules.Normalize(file.FullPath)));

        groups.RemoveAll(group => group.Files.Count < 2);
    }

    static void RemoveEmpty(IReadOnlyList<string> roots, ActionOutcome outcome)
    {
        var predicted = PredictEmptyDirectories(roots, outcome.RemovedPaths);
        foreach (var directory in predicted)
        {
            try
            {
                if (!Directory.Exists(directory))
                    continue;

                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (Directory.EnumerateFileSystemEntries(directory).Any())
                    continue;

                Directory.Delete(directory, recursive: false);
                outcome.DirectoriesRemoved++;
                outcome.RemovedDirectories.Add(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                outcome.Failures.Add($"{directory}: {OneLine(ex.Message)}");
            }
        }
    }

    static bool WouldBeEmpty(string directory, HashSet<string> removedFiles, HashSet<string> removedDirectories)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var full = PathRules.Normalize(entry);
                var attributes = File.GetAttributes(entry);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
                if (isDirectory && !isLink)
                {
                    if (removedDirectories.Contains(full))
                        continue;

                    return false;
                }

                if (!removedFiles.Contains(full))
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        return true;
    }

    static bool IsUnderRoot(string directory, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (PathRules.IsStrictChild(directory, root))
                return true;
        }

        return false;
    }

    static void DeleteFile(string path)
    {
        var win32 = PathRules.Win32(path);
        try
        {
            File.Delete(win32);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClearReadOnly(win32);
            File.Delete(win32);
        }
    }

    static void MoveFile(string source, string destination)
    {
        var from = PathRules.Win32(source);
        var to = PathRules.Win32(destination);
        try
        {
            File.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClearReadOnly(from);
            File.Move(from, to);
        }
    }

    static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException($"Could not find a free name for {path}.");
    }

    static string RootLabel(string root)
    {
        var trimmed = PathRules.Normalize(root);
        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(name) && name != trimmed)
            return name;

        var cleaned = trimmed.TrimEnd('\\').Replace(":", "", StringComparison.Ordinal).Replace("\\", "_", StringComparison.Ordinal);
        return string.IsNullOrEmpty(cleaned) ? "root" : cleaned;
    }

    static string OneLine(string message)
    {
        message = message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return message.Length <= 240 ? message : message[..240] + "...";
    }
}
