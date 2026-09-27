using System.Threading.Channels;
using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;
using FastDelete.Core.Interop;
using Xunit;

namespace FastDelete.Core.Tests;

/// <summary>Direct walker-level tests: emission order, classification, reparse safety.</summary>
public class DirectoryWalkerTests : IDisposable
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

    private static async Task<List<DeleteWorkItem>> Walk(string path)
    {
        uint attrs = FileDeleter.GetAttributes(LongPath.Prefix(path));
        Assert.NotEqual(uint.MaxValue, attrs);
        var channel = Channel.CreateUnbounded<DeleteWorkItem>();
        var walk = DirectoryWalker.WalkAsync(LongPath.Prefix(path), attrs, channel.Writer, CancellationToken.None);
        await walk;
        channel.Writer.TryComplete();
        var items = new List<DeleteWorkItem>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            items.Add(item);
        return items;
    }

    [Fact]
    public async Task Emits_files_then_directories_in_post_order()
    {
        string root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        File.WriteAllText(Path.Combine(root, "root.txt"), "x");
        File.WriteAllText(Path.Combine(root, "a", "a1.txt"), "x");
        File.WriteAllText(Path.Combine(root, "a", "a2.txt"), "x");

        var items = await Walk(root);

        Assert.Equal(6, items.Count); // 3 files + 3 dirs
        var kinds = items.Select(i => i.Kind).ToList();
        // every directory must come after all its children
        int dirA = items.FindIndex(i => i.Kind == WorkItemKind.Directory && i.Path.EndsWith("\\a"));
        int dirB = items.FindIndex(i => i.Kind == WorkItemKind.Directory && i.Path.EndsWith("\\b"));
        int rootIdx = items.FindIndex(i => i.Kind == WorkItemKind.Directory && i.Path.EndsWith(NewRootName(root)));
        int a1 = items.FindIndex(i => i.Path.EndsWith("a1.txt"));
        int a2 = items.FindIndex(i => i.Path.EndsWith("a2.txt"));
        Assert.True(dirA > a1 && dirA > a2, "dir a must be emitted after its files");
        Assert.True(rootIdx > dirA && rootIdx > dirB, "root must be emitted after its subdirs");
        Assert.Equal(3, kinds.Count(k => k == WorkItemKind.Directory));
        Assert.Equal(3, kinds.Count(k => k == WorkItemKind.File));
    }

    private static string NewRootName(string root) => Path.GetFileName(root.TrimEnd('\\'));

    [Fact]
    public async Task Junction_is_emitted_as_link_and_never_traversed()
    {
        string root = NewRoot();
        string outside = NewRoot();
        Directory.CreateDirectory(Path.Combine(outside, "sub"));
        File.WriteAllText(Path.Combine(outside, "sub", "secret.txt"), "x");
        TestTree.CreateJunction(Path.Combine(root, "link"), outside);

        var items = await Walk(root);

        var link = items.Single(i => i.Kind == WorkItemKind.ReparseLink);
        Assert.EndsWith("link", link.Path);
        // nothing from the outside tree may appear
        Assert.DoesNotContain(items, i => i.Path.Contains("secret"));
        Assert.Equal(1, items.Count(i => i.Kind == WorkItemKind.Directory)); // only the root
    }

    [Fact]
    public async Task Single_file_root_is_emitted_as_file()
    {
        string root = NewRoot();
        File.WriteAllText(Path.Combine(root, "only.txt"), "x");

        var items = await Walk(Path.Combine(root, "only.txt"));

        var item = Assert.Single(items);
        Assert.Equal(WorkItemKind.File, item.Kind);
    }

    [Fact]
    public async Task Reparse_root_is_emitted_as_link_not_traversed()
    {
        string root = NewRoot();
        string outside = NewRoot();
        File.WriteAllText(Path.Combine(outside, "precious.txt"), "x");
        string junction = Path.Combine(root, "rootlink");
        TestTree.CreateJunction(junction, outside);

        var items = await Walk(junction);

        var item = Assert.Single(items);
        Assert.Equal(WorkItemKind.ReparseLink, item.Kind);
    }

    [Fact]
    public async Task Empty_directory_is_emitted_once()
    {
        string root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "empty"));

        var items = await Walk(root);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(WorkItemKind.Directory, i.Kind));
    }
}
