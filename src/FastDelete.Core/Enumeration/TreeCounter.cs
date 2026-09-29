using System.Threading.Channels;
using FastDelete.Core.Interop;

namespace FastDelete.Core.Enumeration;

/// <summary>Recursive totals for one or more paths: items and bytes.</summary>
public readonly record struct TreeStats(long Files, long Directories, long Links, long Bytes)
{
    public long TotalItems => Files + Directories + Links;
}

/// <summary>
/// Counts files/folders/links/bytes inside paths WITHOUT following reparse points.
/// Reuses the deletion walker, so counting and deleting see exactly the same tree.
/// Stops early once <paramref name="cap"/> items are seen (callers show "more than…").
/// </summary>
public static class TreeCounter
{
    public static async Task<TreeStats> CountAsync(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken = default,
        long cap = 500_000)
    {
        long files = 0, dirs = 0, links = 0, bytes = 0;

        foreach (var raw in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string prefixed = LongPath.Prefix(raw);
            uint attrs = Win32.GetFileAttributesW(prefixed);
            if (attrs == uint.MaxValue)
                continue;

            if (Win32.IsReparsePoint(attrs))
            {
                links++;
            }
            else if (!Win32.IsDirectory(attrs))
            {
                files++;
                bytes += QuerySize(prefixed);
            }
            else
            {
                var channel = Channel.CreateBounded<DeleteWorkItem>(new BoundedChannelOptions(4096)
                {
                    SingleReader = true,
                    SingleWriter = true,
                });
                // Cancelling stops the walker early when the cap is reached; without this
                // the walker would block forever on the full channel.
                var walkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // WalkAsync does NOT complete the writer (the engine does); here we must,
                // otherwise the read loop below pends forever once the walk ends.
                async Task WalkAndComplete()
                {
                    try
                    {
                        await DirectoryWalker.WalkAsync(prefixed, attrs, channel.Writer, walkCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (walkCts.IsCancellationRequested) { /* cap */ }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                }

                var walkerTask = WalkAndComplete();
                try
                {
                    var reader = channel.Reader;
                    while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var item))
                        {
                            switch (item.Kind)
                            {
                                case WorkItemKind.Directory: dirs++; break;
                                case WorkItemKind.ReparseLink: links++; break;
                                default: files++; bytes += item.Size; break;
                            }
                            if (files + dirs + links >= cap)
                            {
                                walkCts.Cancel(); // early exit - the walker stops with the channel
                                goto DoneRoot;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (walkCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { /* cap */ }
                DoneRoot:
                await walkerTask.ConfigureAwait(false);
                // The user token and the linked walkCts fire together; if the walker's
                // TryComplete() wins the race, the read loop exits normally and the
                // cancellation would be silently swallowed (partial count returned as
                // complete). Re-check here so user cancellation ALWAYS throws.
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (files + dirs + links >= cap)
                return new TreeStats(files, dirs, links, bytes);
        }

        return new TreeStats(files, dirs, links, bytes);
    }

    private static long QuerySize(string prefixedPath)
    {
        IntPtr h = Win32.FindFirstFileExW(
            prefixedPath,
            Win32.FINDEX_INFO_LEVELS.FindExInfoBasic,
            out var data,
            Win32.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
            IntPtr.Zero,
            0);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
            return 0;
        Win32.FindClose(h);
        return ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
    }
}
