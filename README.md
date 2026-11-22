# DuplicateTracker

DuplicateTracker finds files that have the same bytes, then lets you look at them, move the extras, or delete them. A match is a SHA-256 signature of the file contents. The name is ignored.

It runs on Windows. One copy in each group is always kept. The usual choice is the newest file. You can keep the oldest, or the one with the shortest path, instead.

The program is a console application. Start it with no arguments and it opens a menu. Start it with a folder and it scans that folder.

## Download

Version 1.0.0 is built as a single `.exe`. You do not need a separate .NET install.

| File | Machine |
| --- | --- |
| `DuplicateTracker-1.0.0-win-x64.zip` | 64-bit Windows on Intel or AMD |
| `DuplicateTracker-1.0.0-win-arm64.zip` | 64-bit Windows on ARM |

`SHA256SUMS.txt` sits next to the archives. Each line is the SHA-256 of one zip, then two spaces, then the file name.

On GitHub, those files are attached when a tag such as `v1.0.0` is pushed. The workflow in `.github/workflows/release.yml` runs the tests, builds both archives, and publishes the release. If you have the source on this machine and no release yet, `build.ps1` writes the same archives into `dist\`.

Unzip the archive. It contains:

- `DuplicateTracker.exe`
- `DuplicateTracker.cmd`, which forwards its arguments to the exe
- `Scan-Duplicates.ps1`
- this README
- `LICENSE`

The exe is not signed. Windows SmartScreen may say it is unrecognized. Choose **More info**, then **Run anyway**, if you trust the copy you downloaded. Check the hash in `SHA256SUMS.txt` before you do that.

The exe is compressed into one file. The first start unpacks it, so that start takes longer than later ones.

The copy of the .NET runtime inside the exe is under the MIT license.

## Run it

From the unzipped folder:

```text
DuplicateTracker.exe
```

Or from a command prompt, with the folder on `PATH` or with a full path:

```text
DuplicateTracker scan D:\Photos
```

A path as the first argument is the same as `scan`:

```text
DuplicateTracker D:\Photos
```

The lines below show the shape of the output. The counts are an illustration, not a scan of a particular disk.

```text
+------------------------------------------------------------------+
|                                                                  |
|   ######  ######   DuplicateTracker                              |
|   ##  ##    ##     Identical files, matched by SHA-256           |
|   ##  ##    ##     1.0.0                                         |
|   ######    ##     Keep one copy.                                |
|                                                                  |
+------------------------------------------------------------------+

Scan
  Finished                         4.2s
  Files walked                     1,840
  Data walked                      12 GB
  Unique size, not opened          1,790
  Opened and hashed                50

Duplicates
  Duplicate groups                 3
  Extra paths                      4
  Reclaimable                      86 MB
```

A narrow window drops the monogram and uses a shorter card. Color is used when the console supports it. Set `NO_COLOR` to turn color off. `--quiet` skips the banner and the progress line. The summary is still printed when duplicates were found or a file could not be read.

## What counts as a duplicate

1. Files are grouped by size. A size that appears once is not opened.
2. Files that share a size are hashed on the first 64 KB.
3. A full SHA-256 is computed only when that prefix also matches, or when the file is 64 KB or smaller.

Two files are duplicates when the full signature matches. The group can contain more than two files.

Hard links are different names for the same data. They show up as their own groups, marked `names`, and the reclaimable size is zero. Deleting one of those names does not free disk space. A bulk delete or move leaves them alone. The review command `l` can remove the extra names if you want fewer paths.

When several copies exist, one stays:

| Rule | What stays | Tie |
| --- | --- | --- |
| `newest` (default) | Latest last-write time | Shorter path, then the path itself, ignoring case |
| `oldest` | Earliest last-write time | Same tie |
| `shortest-path` | Shortest path | Path, ignoring case, then the newer file |

In the review screen you can mark a specific file with `keep 2`. That choice overrides the rule for that group.

If you pass two folders and one is inside the other, only the outer folder is scanned. The program prints a line for the folder it dropped.

## What the scan skips

| Left out | Unless |
| --- | --- |
| Empty files (0 bytes) | `--include-empty` |
| Files smaller than the minimum (default 1 byte) | You lower `--min-size`, or the file is empty and `--include-empty` is set |
| `thumbs.db`, `ehthumbs.db`, `ehthumbs_vista.db`, `desktop.ini`, `.DS_Store` | `--include-cache-files` |
| Cloud-only files (Offline, recall-on-open, recall-on-data-access) | `--include-cloud` |
| `$RECYCLE.BIN` and `System Volume Information` | Always skipped |
| Directory junctions and directory symlinks | `--follow-links`, and only when the target stays inside a folder you are scanning |
| File symlinks | Always skipped |

A OneDrive file that is already on disk is hashed. A placeholder that would have to be downloaded is not, unless you pass `--include-cloud`.

Files are opened with share read, write, and delete, so a file another program still has open can usually be hashed. Paths that cannot be listed or read are counted. The first few are printed. The scan continues.

Hashing uses up to four threads by default, and never more than the processor count unless you set `--threads`. The limit is 32.

## Review

After an interactive scan, or after `scan` with no `--delete`, `--move`, or `--dry-run`, the groups are listed eight at a time. They are ordered by space you can reclaim, then by how many paths are in the group.

```text
    #  Paths     Reclaim  Signature     Sample
    1      3      42 MB  9f2a11c0b7e4  D:\Photos\2024\IMG_1042.jpg

  g <n> open    n next    p prev    d delete    m move
  l link names  k keep    e export  q leave     ? help
```

| Command | Effect |
| --- | --- |
| `g 4` or `4` | Open group 4 |
| `n` / `p` | Next or previous page |
| `d` | Delete the extra copies that take space |
| `m D:\Quarantine` | Move those copies under that folder |
| `l` | Remove extra hard-link names |
| `k` | Change the keep rule |
| `e report.csv` | Write a report of the groups still listed |
| `q` | Leave the scan |
| `?` | Print the list again |

Inside a group the lines are numbered. `KEEP` marks the file that will stay.

| Command | Effect |
| --- | --- |
| `keep 2` | Keep file 2. The others are the extras |
| `open 2` | Open file 2 in the registered program |
| `show 2` | Open File Explorer with that file selected |
| `d` | Delete the other files in this group |
| `m D:\Quarantine` | Move the other files in this group |
| `b` | Back to the group list |

The menu you get with no arguments is:

1. Scan a folder. You can add more folders before the scan starts.
2. Print the SHA-256 of one file.
3. Settings: minimum size, extensions, folder names to skip, subfolders, empty files, cloud-only files, directory links, threads, cache files.
4. The same help text as `--help`.
5. Quit.

## Delete, move, and empty folders

A delete or a move always leaves the keeper. The plan is printed first: the keep rule, how many extra paths, the space that would be freed, and up to 24 paths.

Delete goes to the Recycle Bin. That is the default. Permanent delete asks you to type `DELETE` when you are at a console and you did not pass `--yes`. `--permanent --yes` deletes without that prompt. If the Recycle Bin refuses a file, the file is left in place. It is not deleted permanently as a fallback.

A read-only extra is retried once after the read-only flag is cleared.

Move asks for a folder that is outside every folder you scanned. The files land here:

```text
D:\Quarantine\2026-03-18_1430\Photos\2024\IMG_1042.jpg
```

`2026-03-18_1430` is the time the batch folder was created. `Photos` is the name of the scanned folder. The rest is the path under that folder. If the destination name already exists, ` (2)`, ` (3)`, and so on is added, up to ` (9999)`.

The batch folder is chosen once and shown in the plan, so the whole move uses the same timestamp.

When files are about to be removed, and you are at a console without `--yes`, the program lists folders that will be empty afterwards and asks:

```text
Remove those empty folders? [y/N]
```

`--remove-empty-dirs` answers yes without asking. Folders that were already empty are left alone. The folder you scanned is never removed. Only folders that held a removed file, and that become empty because of that, are candidates. The deepest folder is removed first. Directory reparse points are not removed.

`--dry-run` prints the plan and changes nothing. If you pass `--dry-run` and `--yes` together, nothing is changed.

Without `--yes`, and without a console (a script, a redirected input), `--delete` and `--move` print the plan and change nothing. Add `--yes` when a script should apply the plan. `--quiet` on its own does not apply it.

Press Ctrl+C once to stop the scan or the prompts. The program prints `Stopping. Press Ctrl+C again to quit now.` A second Ctrl+C exits immediately.

## Reports

`--export` writes the report before any delete or move. The path must end in `.csv`, `.json`, or `.txt`.

CSV is UTF-8 with a byte order mark. Columns:

`Group`, `Signature`, `SizeBytes`, `ReclaimableBytes`, `HardLinkOnly`, `Path`, `LastWriteUtc`, `Keeper`, `LinkCount`, `FileId`

`Keeper` is `true` on the file the keep rule would leave. JSON uses the same facts, plus the roots and the scan counters. Text is a readable dump of the same groups.

Do not write the report inside a folder you are scanning if you plan to scan that folder again. The report is a new file, and a second identical report would match it.

From the review screen, `e report.csv` writes the groups that are still listed, after any deletes you already made in that session.

## Command line

```text
DuplicateTracker
DuplicateTracker scan <folder> [more folders] [options]
DuplicateTracker hash <file>
DuplicateTracker --help
DuplicateTracker --version
```

Scan options:

| Option | Meaning |
| --- | --- |
| `--min-size <size>` | Skip smaller files. `512`, `10KB`, `1MB`, `1.5GB` |
| `--include-empty` | Treat empty files as one duplicate group |
| `--ext <list>` | Only these extensions. `jpg,png,pdf` |
| `--exclude-ext <list>` | Skip these extensions |
| `--exclude-dir <list>` | Skip folders with these names. `.git,node_modules` |
| `--include-cloud` | Hash files that are not stored on this PC |
| `--include-cache-files` | Include `thumbs.db`, `desktop.ini`, and `.DS_Store` |
| `--follow-links` | Follow directory links that stay inside the scan |
| `--no-recurse` | Do not enter subfolders |
| `--threads <n>` | Hashing threads, from 1 to 32. Default is up to 4 |

After the scan:

| Option | Meaning |
| --- | --- |
| `--export <file>` | Write `.csv`, `.json`, or `.txt` before any delete or move |
| `--delete` | Remove extra copies. One file in each group stays |
| `--recycle` | Send those copies to the Recycle Bin. This is the default |
| `--permanent` | Delete those copies without the Recycle Bin |
| `--move <folder>` | Move extra copies into a dated folder under this path |
| `--keep <rule>` | `newest`, `oldest`, or `shortest-path` |
| `--remove-empty-dirs` | Remove folders left empty inside the scanned folders |
| `--yes` | Apply `--delete` or `--move`. Without it, a console asks first |
| `--dry-run` | Print the plan and change nothing |
| `--fail-on-duplicates` | Exit with code 1 when any duplicate group is found |
| `--quiet` | Skip the banner and the progress line |

`--delete` and `--move` cannot be used together. Hard-link groups count for `--fail-on-duplicates`. They are not part of a bulk delete.

```text
DuplicateTracker scan D:\Photos
DuplicateTracker scan D:\Photos E:\Backup --min-size 1MB --export dupes.csv
DuplicateTracker scan D:\Photos --delete --keep newest --remove-empty-dirs --yes
DuplicateTracker scan D:\Photos --move D:\Quarantine --keep shortest-path --yes
DuplicateTracker hash D:\Photos\IMG_1042.jpg
```

`hash` prints the SHA-256, the size, and the full path. It does not look for duplicates.

## Scan-Duplicates.ps1

The script is a thin wrapper for people who prefer PowerShell parameters. If `DuplicateTracker.exe` is in the same folder, the script runs that. If you are in a clone of the repository and the exe is not there, the script uses `dotnet run` on `DuplicateTracker.csproj`.

```text
.\Scan-Duplicates.ps1 D:\Photos
.\Scan-Duplicates.ps1 D:\Photos -Export D:\Reports\dupes.csv -FailOnDuplicates
.\Scan-Duplicates.ps1 D:\Photos -Delete -RemoveEmptyDirs -Yes
.\Scan-Duplicates.ps1 D:\Photos -MoveTo D:\Quarantine -Keep shortest-path -DryRun
```

`-Permanent` requires `-Delete`. `-Delete` and `-MoveTo` cannot be combined. If execution policy blocks the script:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\Scan-Duplicates.ps1 D:\Photos
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Done |
| 1 | Duplicates were found, and `--fail-on-duplicates` was set |
| 2 | The command line could not be read |
| 3 | Some files could not be read |
| 4 | A delete or a move did not finish for every file |
| 130 | Stopped with Ctrl+C |

If more than one of these applies, a failed delete or move wins, then `--fail-on-duplicates`, then unreadable files. Deleting the extras later in the same run does not clear code 1. The duplicates were found.

## Build the zip yourself

You need the .NET 10 SDK. `global.json` asks for SDK 10.0.100 or a later 10.0 feature band.

```text
dotnet test -c Release
dotnet run -c Release -- scan D:\Photos --dry-run
```

`build.ps1` runs the tests, then publishes a self-contained single-file exe for `win-x64` and `win-arm64`. The archives land in `dist\`, with `SHA256SUMS.txt`.

```text
pwsh -File .\build.ps1
```

If the ARM64 runtime pack is not available on your machine, the script warns and still writes the x64 archive. On GitHub Actions the ARM64 archive is required.

The project file does not set a runtime identifier. Tests stay framework-dependent. The publish flags live only in `build.ps1`, which the release workflow calls.

## Notes for Windows

DuplicateTracker refuses to start on anything other than Windows. Recycle Bin delete uses the shell and has to run on an STA thread, which is the process main thread.

The application manifest sets `longPathAware`. Paths of about 240 characters or more are passed to Windows with the `\\?\` prefix. Very long paths still depend on the long-path setting of the machine.

There is no installer. Unzip the archive and run the exe, or call `Scan-Duplicates.ps1`.

## License

MIT. Copyright (c) 2026 Ethan. See `LICENSE`.
