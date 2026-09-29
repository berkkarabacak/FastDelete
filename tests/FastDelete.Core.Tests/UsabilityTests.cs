using FastDelete.Core;
using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;
using Xunit;

namespace FastDelete.Core.Tests;

public class UsabilityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files\App")]
    [InlineData(@"C:\Program Files (x86)")]
    public void Startup_does_not_open_a_drive_or_windows_folder(string? path)
    {
        Assert.True(DeleteSafety.IsUnsafePlaceToOpenFirst(path, userProfile: @"C:\Users\Mom"));
    }

    [Fact]
    public void Startup_does_not_open_the_user_profile()
    {
        Assert.True(DeleteSafety.IsUnsafePlaceToOpenFirst(@"C:\Users\Mom", @"C:\Users\Mom"));
        Assert.True(DeleteSafety.IsUnsafePlaceToOpenFirst(@"C:\Users\Mom\", @"c:\users\mom"));
    }

    [Theory]
    [InlineData(@"C:\Users\Mom\Downloads")]
    [InlineData(@"D:\Photos\2019")]
    [InlineData(@"C:\Users\Mom\Documents\Taxes")]
    public void Startup_can_open_an_ordinary_folder(string path)
    {
        Assert.False(DeleteSafety.IsUnsafePlaceToOpenFirst(path, userProfile: @"C:\Users\Mom"));
    }

    [Theory]
    [InlineData(@"C:\", "C: drive")]
    [InlineData(@"d:/", "D: drive")]
    public void A_drive_is_labeled_as_a_drive(string path, string label)
    {
        Assert.Equal(label, DeleteSafety.DriveDisplayName(path));
    }

    [Fact]
    public void A_drive_that_is_open_does_not_look_like_a_normal_folder()
    {
        string warning = DeleteSafety.OpenFolderWarning(@"C:\");

        Assert.Contains("whole drive", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be deleted", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_ordinary_folder_has_no_place_warning()
    {
        Assert.Equal(string.Empty, DeleteSafety.OpenFolderWarning(@"C:\Users\Mom\Downloads"));
    }

    [Fact]
    public void Confirm_shows_the_folder_name_and_where_it_is()
    {
        string text = DeleteSafety.FormatConfirmNames(new[]
        {
            @"C:\Users\Mom\Downloads\vacation",
        });

        Assert.Contains("vacation", text);
        Assert.Contains(@"C:\Users\Mom\Downloads\vacation", text);
        Assert.DoesNotContain("1 item", text);
    }

    [Fact]
    public void Hidden_and_dot_folders_stay_out_of_the_list()
    {
        Assert.False(ListingRules.ShouldShow(".ssh", hiddenAttribute: false, systemAttribute: false, includeHidden: false));
        Assert.False(ListingRules.ShouldShow(".cursor", hiddenAttribute: true, systemAttribute: false, includeHidden: false));
        Assert.False(ListingRules.ShouldShow("AppData", hiddenAttribute: true, systemAttribute: false, includeHidden: false));
        Assert.True(ListingRules.ShouldShow(".ssh", hiddenAttribute: false, systemAttribute: false, includeHidden: true));
        Assert.True(ListingRules.ShouldShow("Downloads", hiddenAttribute: false, systemAttribute: false, includeHidden: false));
    }

    [Fact]
    public void An_empty_folder_says_empty_instead_of_a_blank_size()
    {
        Assert.Equal("Empty", FolderSizeLabel.FromStats(bytes: 0, itemsSeen: 3));
    }

    [Fact]
    public void A_folder_with_files_shows_a_size()
    {
        string label = FolderSizeLabel.FromStats(bytes: 1536, itemsSeen: 4);

        Assert.Contains("KB", label);
        Assert.NotEqual(string.Empty, label);
    }

    [Fact]
    public void A_huge_folder_does_not_pretend_to_have_an_exact_size()
    {
        Assert.Equal("Very large", FolderSizeLabel.FromStats(bytes: 50_000, itemsSeen: FolderSizeLabel.ItemCap));
    }

    [Fact]
    public void Practice_folder_is_only_practice_files()
    {
        string root = Path.Combine(Path.GetTempPath(), "FastDeleteTests", "practice_" + Guid.NewGuid().ToString("N"));
        try
        {
            PracticeFolder.Create(root);

            string readMe = File.ReadAllText(Path.Combine(root, PracticeFolder.ReadMeName));
            Assert.Contains("practice", readMe, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Recycle Bin", readMe);
            Assert.Contains("not in this folder", readMe, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, PracticeFolder.NoteFolderName, PracticeFolder.NoteFileName)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

}
