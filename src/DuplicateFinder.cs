namespace DuplicateTracker;

internal static class DuplicateFinder
{
    public const int PrefixLength = 64 * 1024;

    public static async Task<ScanResult> ScanAsync(
        IReadOnlyList<string> roots,
        ScanSettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var (normalized, dropped) = PathRules.NormalizeRoots(roots);
        var counters = new ScanCounters();
        var issues = new List<ScanIssue>();
        var issueLock = new object();

        void AddIssue(string path, string message)
        {
            Interlocked.Increment(ref counters.ReadErrors);
            lock (issueLock)
            {
                if (issues.Count < 200)
                    issues.Add(new ScanIssue(path, Clean(message)));
            }
        }

        var seen = Walk(normalized, settings, counters, AddIssue, progress, cancellationToken);
        var candidates = CandidatesOf(seen);
        counters.UniqueSize = seen.Count - candidates.Count;
        counters.CandidateTotal = candidates.Count;
        progress?.Report(counters.Snapshot(ScanPhase.Hashing));

        var signatureCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var prefixGroups = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<HashedCandidate>>(StringComparer.Ordinal);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(settings.Threads, 1, 32),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(candidates, options, async (file, token) =>
        {
            counters.CurrentPath = file.FullPath;
            try
            {
                var read = await HashAsync(file, full: false, signatureCache, counters, token).ConfigureAwait(false);
                Interlocked.Increment(ref counters.FilesHashed);
                var key = file.Length.ToString(CultureInfo.InvariantCulture) + ":" + read.Hex;
                prefixGroups.GetOrAdd(key, _ => new System.Collections.Concurrent.ConcurrentBag<HashedCandidate>())
                    .Add(new HashedCandidate(file, read));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AddIssue(file.FullPath, ex.Message);
            }

            Interlocked.Increment(ref counters.Attempted);
            MaybeReport(counters, progress, ScanPhase.Hashing);
        }).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var groups = new List<DuplicateGroup>();
        var needFull = new List<HashedCandidate>();
        foreach (var bag in prefixGroups.Values)
        {
            var items = bag.ToArray();
            if (items.Length < 2)
                continue;

            if (items.All(item => item.Read.Complete))
                AddGroup(groups, items);
            else
                needFull.AddRange(items);
        }

        var fullGroups = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<HashedCandidate>>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(needFull, options, async (item, token) =>
        {
            counters.CurrentPath = item.File.FullPath;
            try
            {
                var read = await HashAsync(item.File, full: true, signatureCache, counters, token).ConfigureAwait(false);
                var key = item.File.Length.ToString(CultureInfo.InvariantCulture) + ":" + read.Hex;
                fullGroups.GetOrAdd(key, _ => new System.Collections.Concurrent.ConcurrentBag<HashedCandidate>())
                    .Add(new HashedCandidate(item.File, read));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AddIssue(item.File.FullPath, ex.Message);
            }

            MaybeReport(counters, progress, ScanPhase.Hashing);
        }).ConfigureAwait(false);

        foreach (var bag in fullGroups.Values)
        {
            var items = bag.ToArray();
            if (items.Length >= 2)
                AddGroup(groups, items);
        }

        groups.Sort(static (left, right) =>
        {
            var bySpace = right.ReclaimableBytes.CompareTo(left.ReclaimableBytes);
            if (bySpace != 0)
                return bySpace;

            var byCount = right.Files.Count.CompareTo(left.Files.Count);
            if (byCount != 0)
                return byCount;

            return StringComparer.Ordinal.Compare(left.Signature, right.Signature);
        });

        started.Stop();
        return new ScanResult
        {
            Roots = normalized,
            DroppedRoots = dropped,
            Groups = groups,
            FilesSeen = Interlocked.Read(ref counters.FilesSeen),
            BytesSeen = Interlocked.Read(ref counters.BytesSeen),
            UniqueSize = counters.UniqueSize,
            FilesHashed = Interlocked.Read(ref counters.FilesHashed),
            BytesRead = Interlocked.Read(ref counters.BytesRead),
            SkippedSmall = Interlocked.Read(ref counters.SkippedSmall),
            SkippedEmpty = Interlocked.Read(ref counters.SkippedEmpty),
            SkippedCache = Interlocked.Read(ref counters.SkippedCache),
            SkippedCloud = Interlocked.Read(ref counters.SkippedCloud),
            SkippedExtension = Interlocked.Read(ref counters.SkippedExtension),
            SkippedReparse = Interlocked.Read(ref counters.SkippedReparse),
            SkippedOutsideLink = Interlocked.Read(ref counters.SkippedOutsideLink),
            ReadErrors = Interlocked.Read(ref counters.ReadErrors),
            ElapsedMilliseconds = started.ElapsedMilliseconds,
            DuplicatesFound = groups.Count > 0,
            Issues = issues
        };
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new CommandException($"File not found: {path}");

        if ((info.Attributes & FileAttributes.Directory) != 0)
            throw new CommandException($"Expected a file, not a folder: {path}");

        var seen = new SeenFile
        {
            FullPath = Path.GetFullPath(path),
            Length = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc
        };

        var counters = new ScanCounters();
        var read = await HashAsync(
            seen,
            full: true,
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal),
            counters,
            cancellationToken).ConfigureAwait(false);

        return read.Hex;
    }

    static List<SeenFile> CandidatesOf(List<SeenFile> seen)
    {
        var counts = new Dictionary<long, int>();
        foreach (var file in seen)
        {
            counts.TryGetValue(file.Length, out var count);
            counts[file.Length] = count + 1;
        }

        var candidates = new List<SeenFile>();
        foreach (var file in seen)
        {
            if (counts[file.Length] >= 2)
                candidates.Add(file);
        }

        return candidates;
    }

    static void AddGroup(List<DuplicateGroup> groups, IReadOnlyList<HashedCandidate> items)
    {
        var files = items
            .Select(item => new DuplicateFile
            {
                FullPath = item.File.FullPath,
                Length = item.File.Length,
                LastWriteTimeUtc = item.File.LastWriteTimeUtc,
                Identity = item.Read.Identity,
                LinkCount = item.Read.LinkCount
            })
            .GroupBy(file => PathRules.Normalize(file.FullPath), StringComparer.OrdinalIgnoreCase)
            .Select(paths => paths.First())
            .ToList();

        files.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.FullPath, right.FullPath));
        if (files.Count < 2)
            return;

        groups.Add(new DuplicateGroup
        {
            Signature = items[0].Read.Hex,
            Length = items[0].File.Length,
            Files = files
        });
    }

    static async Task<HashRead> HashAsync(
        SeenFile file,
        bool full,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> signatureCache,
        ScanCounters counters,
        CancellationToken cancellationToken)
    {
        var openPath = PathRules.Win32(file.FullPath);
        await using var stream = new FileStream(
            openPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var (identity, links) = FileIdentity.Read(stream.SafeFileHandle, file.FullPath);
        var wantFull = full || file.Length <= PrefixLength;
        if (wantFull && signatureCache.TryGetValue(identity, out var cached))
            return new HashRead(cached, true, identity, links);

        var target = wantFull ? file.Length : PrefixLength;
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            long got = 0;
            while (got < target)
            {
                var want = (int)Math.Min(buffer.Length, target - got);
                var read = await stream.ReadAsync(buffer.AsMemory(0, want), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                hasher.AppendData(buffer.AsSpan(0, read));
                got += read;
                Interlocked.Add(ref counters.BytesRead, read);
            }

            if (got != target)
                throw new IOException("The file changed size while it was being read.");

            var hex = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (wantFull)
                signatureCache[identity] = hex;

            return new HashRead(hex, wantFull, identity, links);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    static List<SeenFile> Walk(
        IReadOnlyList<string> roots,
        ScanSettings settings,
        ScanCounters counters,
        Action<string, string> addIssue,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<SeenFile>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0
        };

        var stack = new Stack<string>();
        foreach (var root in roots)
        {
            visited.Add(root);
            stack.Push(root);
        }

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                addIssue(directory, ex.Message);
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    addIssue(entry.FullName, ex.Message);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        if (!settings.FollowLinks)
                        {
                            Interlocked.Increment(ref counters.SkippedReparse);
                            continue;
                        }

                        var target = ResolveLink(entry);
                        if (target is null || !roots.Any(root => PathRules.IsSameOrChild(target, root)))
                        {
                            Interlocked.Increment(ref counters.SkippedOutsideLink);
                            continue;
                        }

                        var normalized = PathRules.Normalize(target);
                        if (!visited.Add(normalized))
                            continue;

                        stack.Push(normalized);
                        continue;
                    }

                    if (AlwaysSkippedDirectory(entry.Name) || settings.ExcludeDirectories.Contains(entry.Name))
                        continue;

                    if (!settings.Recurse)
                        continue;

                    var child = PathRules.Normalize(entry.FullName);
                    if (visited.Add(child))
                        stack.Push(child);

                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0 && IsFileLink(entry))
                {
                    Interlocked.Increment(ref counters.SkippedReparse);
                    continue;
                }

                if (entry is not FileInfo file)
                    continue;

                Interlocked.Increment(ref counters.FilesSeen);
                long length;
                DateTime written;
                try
                {
                    length = file.Length;
                    written = file.LastWriteTimeUtc;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    addIssue(file.FullName, ex.Message);
                    continue;
                }

                Interlocked.Add(ref counters.BytesSeen, length);
                counters.CurrentPath = file.FullName;

                if (settings.SkipCacheFiles && CacheFiles.Names.Contains(file.Name))
                {
                    Interlocked.Increment(ref counters.SkippedCache);
                    MaybeReport(counters, progress, ScanPhase.Walking);
                    continue;
                }

                if (!settings.IncludeCloud && IsCloudOnly(attributes))
                {
                    Interlocked.Increment(ref counters.SkippedCloud);
                    MaybeReport(counters, progress, ScanPhase.Walking);
                    continue;
                }

                var extension = ExtensionOf(file.Name);
                if (settings.Extensions.Count > 0 && !settings.Extensions.Contains(extension))
                {
                    Interlocked.Increment(ref counters.SkippedExtension);
                    MaybeReport(counters, progress, ScanPhase.Walking);
                    continue;
                }

                if (extension.Length > 0 && settings.ExcludeExtensions.Contains(extension))
                {
                    Interlocked.Increment(ref counters.SkippedExtension);
                    MaybeReport(counters, progress, ScanPhase.Walking);
                    continue;
                }

                if (length == 0)
                {
                    if (!settings.IncludeEmpty)
                    {
                        Interlocked.Increment(ref counters.SkippedEmpty);
                        MaybeReport(counters, progress, ScanPhase.Walking);
                        continue;
                    }
                }
                else if (length < settings.MinSize)
                {
                    Interlocked.Increment(ref counters.SkippedSmall);
                    MaybeReport(counters, progress, ScanPhase.Walking);
                    continue;
                }

                files.Add(new SeenFile
                {
                    FullPath = Path.GetFullPath(file.FullName),
                    Length = length,
                    LastWriteTimeUtc = written
                });

                MaybeReport(counters, progress, ScanPhase.Walking);
            }
        }

        progress?.Report(counters.Snapshot(ScanPhase.Walking));
        return files;
    }

    static bool AlwaysSkippedDirectory(string name) =>
        name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)
        || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);

    static bool IsFileLink(FileSystemInfo entry)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(entry.LinkTarget);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsCloudOnly(FileAttributes attributes)
    {
        const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
        const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
        const FileAttributes cloud = FileAttributes.Offline | recallOnOpen | recallOnDataAccess;
        return (attributes & cloud) != 0;
    }

    static string? ResolveLink(FileSystemInfo entry)
    {
        try
        {
            var target = entry.LinkTarget;
            if (string.IsNullOrWhiteSpace(target))
                return null;

            var full = Path.IsPathRooted(target)
                ? target
                : Path.GetFullPath(Path.Combine(entry.FullName, "..", target));

            return Directory.Exists(full) ? full : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string ExtensionOf(string name)
    {
        var extension = Path.GetExtension(name);
        if (extension.Length <= 1)
            return "";

        return extension[1..];
    }

    static void MaybeReport(ScanCounters counters, IProgress<ScanProgress>? progress, ScanPhase phase)
    {
        if (progress is null)
            return;

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref counters.LastReportTick);
        if (now - last < 80)
            return;

        if (Interlocked.CompareExchange(ref counters.LastReportTick, now, last) != last)
            return;

        progress.Report(counters.Snapshot(phase));
    }

    static string Clean(string message)
    {
        message = message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return message.Length <= 240 ? message : message[..240] + "...";
    }

    sealed class ScanCounters
    {
        public long FilesSeen;
        public long BytesSeen;
        public long UniqueSize;
        public long CandidateTotal;
        public long Attempted;
        public long FilesHashed;
        public long BytesRead;
        public long SkippedSmall;
        public long SkippedEmpty;
        public long SkippedCache;
        public long SkippedCloud;
        public long SkippedExtension;
        public long SkippedReparse;
        public long SkippedOutsideLink;
        public long ReadErrors;
        public long LastReportTick;
        public string? CurrentPath;

        public ScanProgress Snapshot(ScanPhase phase) => new(
            phase,
            Interlocked.Read(ref FilesSeen),
            Interlocked.Read(ref BytesSeen),
            CandidateTotal,
            Interlocked.Read(ref Attempted),
            Interlocked.Read(ref BytesRead),
            Interlocked.Read(ref ReadErrors),
            CurrentPath);
    }

    readonly record struct HashRead(string Hex, bool Complete, string Identity, int LinkCount);

    sealed record HashedCandidate(SeenFile File, HashRead Read);
}
