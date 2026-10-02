using System.Diagnostics;
using System.Text;
using FastDelete.Core.Enumeration;
using FastDelete.Core.Interop;

namespace FastDelete.Core.Deletion;

/// <summary>
/// Recycle Bin deletion via SHFileOperationW (FO_DELETE | FOF_ALLOWUNDO | FOF_NO_UI).
/// Batched in chunks so a single pathological path cannot abort a giant operation.
/// SHFileOperation does not report per-file errors, so a failed chunk is recorded
/// as one failure per path in that chunk.
/// </summary>
public static class RecycleBinDeleter
{
    private const int ChunkSize = 2000;

    public static Task<DeletionResult> DeleteAsync(
        IReadOnlyList<string> selectedPaths,
        CancellationToken cancellationToken,
        PauseToken? pauseToken = null,
        IProgress<DeletionProgress>? progress = null)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(CancelledResult());

        // SHFileOperation wants an STA thread. Running there, not on the UI thread,
        // lets Pause and Stop be clicked. The delete call itself is unchanged.
        try
        {
            var done = new TaskCompletionSource<DeletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { done.SetResult(DeleteCore(selectedPaths, cancellationToken, pauseToken, progress)); }
                catch (Exception ex) { done.SetException(ex); }
            })
            {
                IsBackground = true,
                Name = "FastDelete Recycle Bin",
            };
#pragma warning disable CA1416
            thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
            thread.Start();
            return done.Task;
        }
        catch (PlatformNotSupportedException)
        {
            return Task.Run(() => DeleteCore(selectedPaths, cancellationToken, pauseToken, progress));
        }
    }

    private static DeletionResult CancelledResult() => new()
    {
        FilesDeleted = 0,
        DirectoriesDeleted = 0,
        LinksDeleted = 0,
        Failures = Array.Empty<DeleteFailure>(),
        Elapsed = TimeSpan.Zero,
        WasCancelled = true,
    };

    private static DeletionResult DeleteCore(
        IReadOnlyList<string> selectedPaths,
        CancellationToken cancellationToken,
        PauseToken? pauseToken,
        IProgress<DeletionProgress>? progress)
    {
        var failures = new FailureCollector();
        var sw = Stopwatch.StartNew();
        long files = 0, dirs = 0, links = 0;
        bool wasCancelled = false;

        try
        {
            foreach (var chunk in Chunk(selectedPaths, ChunkSize))
            {
                pauseToken?.WaitIfPaused(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (chunk.Count > 0)
                    Report(progress, files, dirs, links, selectedPaths.Count, chunk[0], sw);

                var sb = new StringBuilder(chunk.Count * 64);
                foreach (var path in chunk)
                {
                    sb.Append(path).Append('\0');
                    // classify for counters
                    try
                    {
                        var attrs = System.IO.File.GetAttributes(path);
                        if (attrs.HasFlag(System.IO.FileAttributes.Directory))
                        {
                            if (attrs.HasFlag(System.IO.FileAttributes.ReparsePoint)) links++; else dirs++;
                        }
                        else files++;
                    }
                    catch { /* counter best-effort only */ }
                }
                sb.Append('\0'); // double-null termination

                var op = new Win32.SHFILEOPSTRUCT
                {
                    hwnd = IntPtr.Zero,
                    wFunc = Win32.FO_DELETE,
                    pFrom = sb.ToString(),
                    pTo = IntPtr.Zero,
                    fFlags = (ushort)(Win32.FOF_ALLOWUNDO | Win32.FOF_NO_UI | Win32.FOF_WANTNUKEWARNING),
                    fAnyOperationsAborted = 0,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = null,
                };

                int result = Win32.SHFileOperationW(ref op);
                if (result != 0 || op.fAnyOperationsAborted != 0)
                {
                    // No per-file detail available; record the chunk as failures.
                    foreach (var path in chunk)
                        failures.Add(path, WorkItemKind.File, result != 0 ? result : Win32.ERROR_ACCESS_DENIED);
                    files = Math.Max(0, files - chunk.Count(c =>
                    {
                        try { return !System.IO.File.GetAttributes(c).HasFlag(System.IO.FileAttributes.Directory); }
                        catch { return false; }
                    }));
                }

                Report(progress, files, dirs, links, selectedPaths.Count, chunk.Count > 0 ? chunk[^1] : "", sw);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Chunks already sent stay in the Recycle Bin. Later chunks are not
            // recorded as failures, so Retry cannot treat them as a new delete.
            wasCancelled = true;
        }

        sw.Stop();
        return new DeletionResult
        {
            FilesDeleted = files,
            DirectoriesDeleted = dirs,
            LinksDeleted = links,
            Failures = failures.ToList(),
            Elapsed = sw.Elapsed,
            WasCancelled = wasCancelled,
        };
    }

    private static void Report(
        IProgress<DeletionProgress>? progress,
        long files,
        long dirs,
        long links,
        int total,
        string current,
        Stopwatch sw)
    {
        progress?.Report(new DeletionProgress
        {
            FilesDeleted = files,
            DirectoriesDeleted = dirs,
            LinksDeleted = links,
            ItemsProcessed = files + dirs + links,
            TotalDiscovered = total,
            CurrentItem = current,
            Elapsed = sw.Elapsed,
        });
    }

    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> paths, int size)
    {
        for (int i = 0; i < paths.Count; i += size)
            yield return paths.Skip(i).Take(size).ToArray();
    }
}
