namespace DuplicateTracker;

internal static class ReportWriter
{
    public static void Write(
        string path,
        ScanResult result,
        KeepPolicy keep,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        switch (extension)
        {
            case ".csv":
                WriteCsv(path, result, keep, overrides);
                break;
            case ".json":
                WriteJson(path, result, keep, overrides);
                break;
            case ".txt":
                WriteText(path, result, keep, overrides);
                break;
            default:
                throw new CommandException("The report path must end in .csv, .json, or .txt.");
        }
    }

    public static void EnsureExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".csv" and not ".json" and not ".txt")
            throw new CommandException("The report path must end in .csv, .json, or .txt.");
    }

    static void WriteCsv(string path, ScanResult result, KeepPolicy keep, IReadOnlyDictionary<string, string>? overrides)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("Group,Signature,SizeBytes,ReclaimableBytes,HardLinkOnly,Path,LastWriteUtc,Keeper,LinkCount,FileId");
        var number = 1;
        foreach (var group in result.Groups)
        {
            var keeper = Keeper.Resolve(group, keep, overrides);
            foreach (var file in group.Files)
            {
                var isKeeper = PathRules.Same(file.FullPath, keeper.FullPath);
                writer.WriteLine(string.Join(',',
                    number.ToString(CultureInfo.InvariantCulture),
                    Csv(group.Signature),
                    group.Length.ToString(CultureInfo.InvariantCulture),
                    group.ReclaimableBytes.ToString(CultureInfo.InvariantCulture),
                    group.IsHardLinkOnly ? "true" : "false",
                    Csv(file.FullPath),
                    Csv(Stamp(file.LastWriteTimeUtc)),
                    isKeeper ? "true" : "false",
                    file.LinkCount.ToString(CultureInfo.InvariantCulture),
                    Csv(file.Identity)));
            }

            number++;
        }
    }

    static void WriteJson(string path, ScanResult result, KeepPolicy keep, IReadOnlyDictionary<string, string>? overrides)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("tool", AppInfo.Name);
        writer.WriteString("version", AppInfo.Version);
        writer.WriteString("scannedAt", Stamp(DateTime.UtcNow));
        writer.WriteString("keep", keep switch
        {
            KeepPolicy.Oldest => "oldest",
            KeepPolicy.ShortestPath => "shortest-path",
            _ => "newest"
        });

        writer.WriteStartArray("roots");
        foreach (var root in result.Roots)
            writer.WriteStringValue(root);
        writer.WriteEndArray();

        writer.WriteStartObject("stats");
        writer.WriteNumber("filesSeen", result.FilesSeen);
        writer.WriteNumber("bytesSeen", result.BytesSeen);
        writer.WriteNumber("uniqueSize", result.UniqueSize);
        writer.WriteNumber("filesHashed", result.FilesHashed);
        writer.WriteNumber("bytesRead", result.BytesRead);
        writer.WriteNumber("readErrors", result.ReadErrors);
        writer.WriteNumber("groups", result.Groups.Count);
        writer.WriteNumber("reclaimableBytes", result.SpaceGroups.Sum(group => group.ReclaimableBytes));
        writer.WriteNumber("elapsedMilliseconds", result.ElapsedMilliseconds);
        writer.WriteEndObject();

        writer.WriteStartArray("groups");
        foreach (var group in result.Groups)
        {
            var keeper = Keeper.Resolve(group, keep, overrides);
            writer.WriteStartObject();
            writer.WriteString("signature", group.Signature);
            writer.WriteNumber("sizeBytes", group.Length);
            writer.WriteNumber("reclaimableBytes", group.ReclaimableBytes);
            writer.WriteBoolean("hardLinkOnly", group.IsHardLinkOnly);
            writer.WriteStartArray("files");
            foreach (var file in group.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.FullPath);
                writer.WriteString("lastWriteUtc", Stamp(file.LastWriteTimeUtc));
                writer.WriteBoolean("keeper", PathRules.Same(file.FullPath, keeper.FullPath));
                writer.WriteNumber("linkCount", file.LinkCount);
                writer.WriteString("fileId", file.Identity);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("issues");
        foreach (var issue in result.Issues)
        {
            writer.WriteStartObject();
            writer.WriteString("path", issue.Path);
            writer.WriteString("message", issue.Message);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    static void WriteText(string path, ScanResult result, KeepPolicy keep, IReadOnlyDictionary<string, string>? overrides)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        writer.WriteLine($"Scanned {Stamp(DateTime.UtcNow)}");
        writer.WriteLine($"Keep rule: {Keeper.Label(keep)}");
        writer.WriteLine($"Files walked: {result.FilesSeen.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"Reclaimable: {ByteSize.Format(result.SpaceGroups.Sum(group => group.ReclaimableBytes))}");
        writer.WriteLine();

        var number = 1;
        foreach (var group in result.Groups)
        {
            var keeper = Keeper.Resolve(group, keep, overrides);
            writer.WriteLine($"Group {number}");
            writer.WriteLine($"Signature {group.Signature}");
            writer.WriteLine($"Size {ByteSize.Format(group.Length)}");
            writer.WriteLine(group.IsHardLinkOnly
                ? "Hard links. Removing a name does not free space while another name remains."
                : $"Reclaimable {ByteSize.Format(group.ReclaimableBytes)}");

            foreach (var file in group.Files)
            {
                var mark = PathRules.Same(file.FullPath, keeper.FullPath) ? "KEEP " : "EXTRA";
                writer.WriteLine($"  {mark}  {file.FullPath}");
            }

            writer.WriteLine();
            number++;
        }

        if (result.Groups.Count == 0)
            writer.WriteLine("No duplicates.");
    }

    static string Stamp(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        return value;
    }
}
