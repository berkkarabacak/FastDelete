using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;
using Xunit;

namespace FastDelete.Core.Tests;

/// <summary>Engine-level tests: multi-root, dedup, concurrency isolation, recycle bin, helpers.</summary>
public class EngineAdditionalTests : IDisposable
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

    private static DeletionEngine Engine(int workers = 4)
        => new(new DeletionOptions { MaxDegreeOfParallelism = workers });

    [Fact]
    public async Task Multiple_roots_are_all_deleted()
    {
        string a = NewRoot();
        string b = NewRoot();
        string c = NewRoot();
        TestTree.MakeFiles(a, 10, 1);
        TestTree.MakeFiles(b, 5, 2);
        File.WriteAllText(Path.Combine(c, "x.txt"), "x");

        var result = await Engine().DeleteAsync(new[] { a, b, c });

        Assert.Equal(21, result.FilesDeleted); // 10 + 10 + 1
        Assert.Equal(2 + 3 + 1, result.DirectoriesDeleted);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(a));
        Assert.False(Directory.Exists(b));
        Assert.False(Directory.Exists(c));
    }

    [Fact]
    public async Task Duplicate_roots_are_deduplicated_without_errors()
    {
        string root = NewRoot();
        TestTree.MakeFiles(root, 5, 1);

        var result = await Engine().DeleteAsync(new[] { root, root, root });

        Assert.Equal(5, result.FilesDeleted);
        Assert.Equal(2, result.DirectoriesDeleted);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Casing_variant_roots_are_deduplicated()
    {
        string root = NewRoot();
        TestTree.MakeFiles(root, 5, 1);
        string variant = root.ToUpperInvariant();

        var result = await Engine().DeleteAsync(new[] { root, variant });

        Assert.Equal(5, result.FilesDeleted);
        Assert.Equal(2, result.DirectoriesDeleted);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Single_file_selection_is_deleted()
    {
        string root = NewRoot();
        string file = Path.Combine(root, "one.txt");
        File.WriteAllText(file, "x");

        var result = await Engine().DeleteAsync(new[] { file });

        Assert.Equal(1, result.FilesDeleted);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public async Task Selecting_a_junction_directly_unlinks_it_and_keeps_target()
    {
        string root = NewRoot();
        string outside = NewRoot();
        File.WriteAllText(Path.Combine(outside, "precious.txt"), "x");
        string junction = Path.Combine(root, "thelink");
        TestTree.CreateJunction(junction, outside);

        var result = await Engine().DeleteAsync(new[] { junction });

        Assert.Equal(1, result.LinksDeleted);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(junction));
        Assert.True(File.Exists(Path.Combine(outside, "precious.txt")));
    }

    [Fact]
    public async Task Concurrent_runs_on_separate_trees_are_isolated()
    {
        const int trees = 4;
        var roots = Enumerable.Range(0, trees).Select(_ => NewRoot()).ToArray();
        foreach (var r in roots) TestTree.MakeFiles(r, 100, 2);

        var results = await Task.WhenAll(roots.Select(r => Engine(workers: 2).DeleteAsync(new[] { r })));

        foreach (var (result, root) in results.Zip(roots))
        {
            Assert.Equal(200, result.FilesDeleted);
            Assert.Equal(3, result.DirectoriesDeleted);
            Assert.Empty(result.Failures);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public async Task Result_reports_elapsed_and_rates()
    {
        string root = NewRoot();
        TestTree.MakeFiles(root, 1000, 5); // 5,000 files - run takes >0.1s so the rate is meaningful

        var result = await Engine().DeleteAsync(new[] { root });

        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.Equal(5006, result.TotalItems); // 5,000 files + 5 dirs + root
        Assert.True(result.ItemsPerSecond > 0);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task Recycle_bin_path_removes_file_without_failures()
    {
        string root = NewRoot();
        string file = Path.Combine(root, "recycle-me.txt");
        File.WriteAllText(file, "x");

        var result = await new DeletionEngine(new DeletionOptions { Mode = DeletionMode.RecycleBin })
            .DeleteAsync(new[] { file });

        Assert.False(File.Exists(file));
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData(Win32Shim.ERROR_ACCESS_DENIED)]
    [InlineData(Win32Shim.ERROR_SHARING_VIOLATION)]
    [InlineData(Win32Shim.ERROR_DIR_NOT_EMPTY)]
    [InlineData(Win32Shim.ERROR_LOCK_VIOLATION)]
    public void Failures_with_transient_errors_are_retryable(int errorCode)
    {
        var failure = new DeleteFailure
        {
            Path = "x",
            ErrorCode = errorCode,
            Message = "m",
        };
        Assert.True(failure.Retryable);
    }

    [Fact]
    public void Failure_message_is_human_readable()
    {
        var failure = new DeleteFailure { Path = "x", ErrorCode = Win32Shim.ERROR_ACCESS_DENIED, Message = "" };
        var collector = new FailureCollector();
        collector.Add(LongPathShim.Prefix("C:\\temp\\thing"), WorkItemKind.File, Win32Shim.ERROR_ACCESS_DENIED);
        var listed = collector.ToList();
        Assert.Single(listed);
        Assert.Equal("C:\\temp\\thing", listed[0].Path);          // no \\?\ prefix in display form
        Assert.False(string.IsNullOrWhiteSpace(listed[0].Message));
        Assert.True(listed[0].Message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}

// Tiny internal shims so the tests don't depend on Win32/LongPath being public beyond need.
internal static class Win32Shim
{
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_SHARING_VIOLATION = 32;
    public const int ERROR_LOCK_VIOLATION = 33;
    public const int ERROR_DIR_NOT_EMPTY = 145;
}

internal static class LongPathShim
{
    public static string Prefix(string path) => FastDelete.Core.Interop.LongPath.Prefix(path);
}
