using FastDelete.Core.Interop;
using Xunit;

namespace FastDelete.Core.Tests;

public class LongPathTests
{
    [Fact]
    public void Prefix_adds_long_path_prefix()
    {
        Assert.Equal(@"\\?\C:\temp", LongPath.Prefix(@"C:\temp"));
    }

    [Fact]
    public void Prefix_is_idempotent()
    {
        Assert.Equal(@"\\?\C:\temp", LongPath.Prefix(@"\\?\C:\temp"));
    }

    [Fact]
    public void Prefix_converts_unc_paths()
    {
        Assert.Equal(@"\\?\UNC\server\share", LongPath.Prefix(@"\\server\share"));
    }

    [Fact]
    public void Join_appends_name()
    {
        Assert.Equal(@"\\?\C:\temp\file.txt", LongPath.Join(@"\\?\C:\temp", "file.txt"));
        Assert.Equal(@"\\?\C:\temp\file.txt", LongPath.Join(@"\\?\C:\temp\", "file.txt"));
    }

    [Fact]
    public void Display_strips_prefix()
    {
        Assert.Equal(@"C:\temp", LongPath.Display(@"\\?\C:\temp"));
        Assert.Equal(@"C:\temp", LongPath.Display(@"C:\temp"));
    }

    [Fact]
    public void NormalizeForCompare_is_case_insensitive_and_trims_separators()
    {
        Assert.Equal(LongPath.NormalizeForCompare(@"c:\Temp\"), LongPath.NormalizeForCompare(@"C:\TEMP\"));
        Assert.Equal(@"C:\TEMP", LongPath.NormalizeForCompare(@"\\?\C:\TEMP\"));
    }
}
