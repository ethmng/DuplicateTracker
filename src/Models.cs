namespace DuplicateTracker;

internal enum KeepPolicy
{
    Newest,
    Oldest,
    ShortestPath
}

internal enum DeleteStyle
{
    Ask,
    Recycle,
    Permanent
}

internal enum AppCommandKind
{
    Interactive,
    Scan,
    Hash,
    Help,
    Version
}

internal sealed class ScanSettings
{
    public bool Recurse { get; set; } = true;

    /// <summary>Files shorter than this are ignored, except empty files when IncludeEmpty is set.</summary>
    public long MinSize { get; set; } = 1;

    public bool IncludeEmpty { get; set; }

    public bool IncludeCloud { get; set; }

    public bool FollowLinks { get; set; }

    public bool SkipCacheFiles { get; set; } = true;

    public int Threads { get; set; } = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));

    public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ExcludeExtensions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ExcludeDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class AppCommand
{
    public AppCommandKind Kind { get; init; }

    public List<string> Paths { get; } = [];

    public string? HashPath { get; init; }

    public ScanSettings Settings { get; init; } = new();

    public KeepPolicy Keep { get; set; } = KeepPolicy.Newest;

    public bool KeepExplicit { get; set; }

    public bool Delete { get; set; }

    public DeleteStyle DeleteStyle { get; set; } = DeleteStyle.Ask;

    public string? MoveTo { get; set; }

    public bool RemoveEmptyDirs { get; set; }

    public bool Yes { get; set; }

    public bool DryRun { get; set; }

    public string? ExportPath { get; set; }

    public bool FailOnDuplicates { get; set; }

    public bool Quiet { get; set; }

    public bool ReviewAfter { get; set; }
}

internal sealed class SeenFile
{
    public required string FullPath { get; init; }

    public required long Length { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }
}

internal sealed class DuplicateFile
{
    public required string FullPath { get; init; }

    public required long Length { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    public required string Identity { get; init; }

    public required int LinkCount { get; init; }
}

internal sealed class DuplicateGroup
{
    public required string Signature { get; init; }

    public required long Length { get; init; }

    public required List<DuplicateFile> Files { get; init; }

    public int DistinctCopies => Files.Select(file => file.Identity).Distinct(StringComparer.Ordinal).Count();

    public long ReclaimableBytes => Length * Math.Max(0, DistinctCopies - 1);

    public bool IsHardLinkOnly => DistinctCopies <= 1 && Files.Count > 1;
}

internal sealed record ScanIssue(string Path, string Message);

internal sealed class ScanResult
{
    public required IReadOnlyList<string> Roots { get; init; }

    public required List<DuplicateGroup> Groups { get; init; }

    public IReadOnlyList<string> DroppedRoots { get; init; } = [];

    public long FilesSeen { get; init; }

    public long BytesSeen { get; init; }

    public long UniqueSize { get; init; }

    public long FilesHashed { get; init; }

    public long BytesRead { get; init; }

    public long SkippedSmall { get; init; }

    public long SkippedEmpty { get; init; }

    public long SkippedCache { get; init; }

    public long SkippedCloud { get; init; }

    public long SkippedExtension { get; init; }

    public long SkippedReparse { get; init; }

    public long SkippedOutsideLink { get; init; }

    public long ReadErrors { get; init; }

    public long ElapsedMilliseconds { get; init; }

    public bool DuplicatesFound { get; init; }

    public required List<ScanIssue> Issues { get; init; }

    public IEnumerable<DuplicateGroup> SpaceGroups => Groups.Where(group => !group.IsHardLinkOnly);

    public IEnumerable<DuplicateGroup> HardLinkGroups => Groups.Where(group => group.IsHardLinkOnly);
}

internal enum ScanPhase
{
    Walking,
    Hashing
}

internal readonly record struct ScanProgress(
    ScanPhase Phase,
    long FilesSeen,
    long BytesSeen,
    long CandidateTotal,
    long Attempted,
    long BytesRead,
    long ReadErrors,
    string? CurrentPath);

internal sealed class PlannedGroup
{
    public required DuplicateGroup Source { get; init; }

    public required DuplicateFile Keeper { get; init; }

    public required List<DuplicateFile> Extras { get; init; }
}

internal sealed class ActionPlan
{
    public required List<PlannedGroup> Groups { get; init; }

    public int ExtraFiles => Groups.Sum(group => group.Extras.Count);

    public long ReclaimableBytes => Groups.Sum(group => group.Source.ReclaimableBytes);
}

internal sealed class ActionOutcome
{
    public int Removed { get; set; }

    public int Failed { get; set; }

    public int DirectoriesRemoved { get; set; }

    public List<string> RemovedPaths { get; } = [];

    public List<string> Failures { get; } = [];

    public List<string> RemovedDirectories { get; } = [];
}

internal sealed class CommandException : Exception
{
    public CommandException(string message) : base(message)
    {
    }
}
