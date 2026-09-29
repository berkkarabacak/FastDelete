using FastDelete.Core.Enumeration;
using Xunit;

namespace FastDelete.Core.Tests;

public class TreeCounterTests : IDisposable
{
    private readonly List<string> _cleanup = new();

    public void Dispose()
    {
        foreach (var dir in _cleanup)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private string NewRoot([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var root = TestTree.CreateTempRoot(name);
        _cleanup.Add(root);
        return root;
    }

    [Fact]
    public async Task Counts_items_and_bytes_exactly()
    {
        string root = NewRoot();
        TestTree.MakeFiles(root, 10, 3); // 30 files of 1 byte + 3 dirs + root
        // MakeFiles writes "x" (1 byte each)

        var stats = await TreeCounter.CountAsync(new[] { root });

        Assert.Equal(30, stats.Files);
        Assert.Equal(4, stats.Directories); // 3 + root
        Assert.Equal(0, stats.Links);
        Assert.Equal(30, stats.Bytes);
        Assert.Equal(34, stats.TotalItems);
    }

    [Fact]
    public async Task Never_follows_junctions()
    {
        string root = NewRoot();
        string outside = NewRoot();
        TestTree.MakeFiles(outside, 50, 2); // 100 files outside
        TestTree.CreateJunction(Path.Combine(root, "link"), outside);

        var stats = await TreeCounter.CountAsync(new[] { root });

        Assert.Equal(1, stats.Links);
        Assert.Equal(1, stats.Directories); // root only
        Assert.Equal(0, stats.Files);
    }

    [Fact]
    public async Task Cap_stops_counting_early()
    {
        string root = NewRoot();
        TestTree.MakeFiles(root, 100, 5); // 500 files

        var stats = await TreeCounter.CountAsync(new[] { root }, cap: 42);

        Assert.True(stats.TotalItems >= 42);
        Assert.True(stats.TotalItems < 500);
    }

    [Fact]
    public async Task Multiple_roots_are_summed()
    {
        string a = NewRoot();
        string b = NewRoot();
        File.WriteAllText(Path.Combine(a, "f1.txt"), "12345"); // 5 bytes
        File.WriteAllText(Path.Combine(b, "f2.txt"), "x");

        var stats = await TreeCounter.CountAsync(new[] { a, b });

        Assert.Equal(2, stats.Files);
        Assert.Equal(2, stats.Directories);
        Assert.Equal(6, stats.Bytes);
    }

    [Fact]
    public async Task Single_file_root_reports_its_size()
    {
        string root = NewRoot();
        string file = Path.Combine(root, "blob.bin");
        File.WriteAllBytes(file, new byte[1234]);

        var stats = await TreeCounter.CountAsync(new[] { file });

        Assert.Equal(1, stats.Files);
        Assert.Equal(1234, stats.Bytes);
        Assert.Equal(0, stats.Directories);
    }

    [Fact]
    public async Task Cancellation_aborts_promptly()
    {
        string root = NewRoot();
        TestTree.MakeFiles(root, 2000, 10); // 20,000 files
        using var cts = new CancellationTokenSource();

        // Machine-speed independent: cancel immediately after starting, while the
        // count is guaranteed to still be running (20k real files cannot enumerate
        // in microseconds). A fixed millisecond delay was flaky - a fast machine
        // can finish the whole count before the timer fires.
        Task<TreeStats> countTask = TreeCounter.CountAsync(new[] { root }, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => countTask);
    }
}
