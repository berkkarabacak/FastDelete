using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;
using Xunit;

namespace FastDelete.Core.Tests;

public class ScreenCopyTests
{
    [Theory]
    [InlineData(0, "Move to Recycle Bin")]
    [InlineData(1, "Move to Recycle Bin (1)")]
    [InlineData(3, "Move to Recycle Bin (3)")]
    [InlineData(14892, "Move to Recycle Bin (14,892)")]
    public void The_big_button_names_the_safe_action_and_the_count(int count, string label)
    {
        Assert.Equal(label, ScreenCopy.RecycleLabel(count));
    }

    [Fact]
    public void Nothing_chosen_and_a_drive_both_leave_the_buttons_doing_nothing()
    {
        Assert.False(ScreenCopy.CanAct(selectedCount: 0, driveRootOpen: false));
        Assert.False(ScreenCopy.CanAct(selectedCount: 4, driveRootOpen: true));
        Assert.True(ScreenCopy.CanAct(selectedCount: 1, driveRootOpen: false));
    }

    [Fact]
    public void A_drive_says_it_cannot_be_deleted()
    {
        Assert.Equal("The C: drive cannot be deleted.", ScreenCopy.DriveGuardTitle("C: drive"));
        Assert.Contains("does nothing", ScreenCopy.DriveGuardBody);
        Assert.Contains("Open a folder", ScreenCopy.DriveGuardBody);
    }

    [Fact]
    public void An_empty_folder_points_at_the_list_on_the_left()
    {
        Assert.Equal("This folder is empty.", ScreenCopy.EmptyTitle);
        Assert.Contains("list on the left", ScreenCopy.EmptyBody);
    }

    [Fact]
    public void First_open_offers_only_downloads_and_a_practice_folder()
    {
        Assert.Equal("Nothing of yours is open.", ScreenCopy.FirstTitle);
        Assert.Contains("two", ScreenCopy.FirstBody);
        Assert.Contains("only when you press this button", ScreenCopy.FirstPracticeHelp);
        Assert.Contains("Recycle Bin", ScreenCopy.FirstFooter);
    }

    [Fact]
    public void Recycle_confirm_names_every_item_and_keeps_cancel_with_yes()
    {
        var items = new[]
        {
            new ScreenItem("Tax form 2024.pdf", "412 KB", false),
            new ScreenItem("Setup installer.exe", "86 MB", false),
            new ScreenItem("Budget 2024.xlsx", "38 KB", false),
        };

        var copy = ScreenCopy.Confirm(DeletionMode.RecycleBin, items);

        Assert.Equal("Move these 3 files to the Recycle Bin?", copy.Title);
        Assert.Equal("You can get these back later from the Recycle Bin.", copy.Body);
        Assert.Equal("3 files", copy.ListLabel);
        Assert.Equal("Yes, move to Recycle Bin", copy.YesLabel);
        Assert.Equal("Cancel, keep my files", copy.CancelLabel);
        Assert.False(copy.UseRed);
        Assert.Equal(3, copy.Items.Count);
        Assert.Equal(items.Select(i => i.Name), copy.Items.Select(i => i.Name));
    }

    [Fact]
    public void Forever_confirm_is_the_only_red_screen_and_says_files_cannot_come_back()
    {
        var items = new[]
        {
            new ScreenItem("Old photos", "Folder, and all inside it", true),
            new ScreenItem("Holiday video.mp4", "1.2 GB", false),
            new ScreenItem("Setup installer.exe", "86 MB", false),
        };

        var copy = ScreenCopy.Confirm(DeletionMode.Permanent, items);

        Assert.Equal("Delete these forever?", copy.Title);
        Assert.Equal("You cannot get them back.", copy.Body);
        Assert.True(copy.BodyStrong);
        Assert.Equal(ScreenCopy.ForeverBody2, copy.Body2);
        Assert.Equal("1 folder and 2 files", copy.ListLabel);
        Assert.Equal("Yes, delete forever", copy.YesLabel);
        Assert.Equal(ScreenCopy.CancelLabel, copy.CancelLabel);
        Assert.True(copy.UseRed);
        Assert.Equal(3, copy.Items.Count);
    }

    [Fact]
    public void A_long_confirm_list_is_not_cut_down_to_a_count()
    {
        var items = Enumerable.Range(1, 15)
            .Select(i => new ScreenItem($"File {i}.txt", "1 KB", false))
            .ToList();

        var copy = ScreenCopy.Confirm(DeletionMode.RecycleBin, items);

        Assert.Equal(15, copy.Items.Count);
        Assert.Contains("File 15.txt", copy.Items.Select(i => i.Name));
        Assert.DoesNotContain("more", copy.ListLabel);
    }

    [Fact]
    public void Working_recycle_says_stop_never_deletes_forever()
    {
        var copy = ScreenCopy.Working(DeletionMode.RecycleBin, paused: false, done: 6250, total: 14892, currentItem: @"C:\Pictures\IMG_6250.jpg");

        Assert.Equal("Moving to the Recycle Bin", copy.Title);
        Assert.Equal("Please wait. This can take a few minutes.", copy.Body);
        Assert.Equal("6,250 of 14,892 files", copy.Counter);
        Assert.Equal("Now: IMG_6250.jpg", copy.Now);
        Assert.Equal("Pause", copy.PauseLabel);
        Assert.Contains("Nothing is deleted forever.", copy.Note);
        Assert.DoesNotContain("cannot be brought back", copy.Note);
    }

    [Fact]
    public void Working_forever_and_paused_use_the_screen_words()
    {
        var forever = ScreenCopy.Working(DeletionMode.Permanent, paused: false, done: 6250, total: 14892, @"\\?\C:\Pictures\IMG_6250.jpg");
        Assert.Equal("Deleting, please wait", forever.Title);
        Assert.Equal("This can take a few minutes. You can leave this window open.", forever.Body);
        Assert.Contains("cannot be brought back", forever.Note);

        var paused = ScreenCopy.Working(DeletionMode.RecycleBin, paused: true, done: 6250, total: 14892, "IMG_6250.jpg");
        Assert.Equal("Paused", paused.Title);
        Assert.Equal("Nothing is moving right now. Press Keep going when you are ready.", paused.Body);
        Assert.Equal("Keep going", paused.PauseLabel);
        Assert.Equal("Stopped at IMG_6250.jpg", paused.Now);
    }

    [Fact]
    public void A_finished_recycle_is_one_done_button()
    {
        var copy = Outcome(DeletionMode.RecycleBin, files: 28, failures: Array.Empty<DeleteFailure>());

        Assert.Equal("Moved 28 files to the Recycle Bin", copy.Title);
        Assert.Equal("You can get them back later from the Recycle Bin.", copy.Body);
        Assert.True(copy.ShowCheck);
        Assert.Equal("Done", copy.PrimaryLabel);
        Assert.False(copy.ShowClose);
        Assert.False(copy.IsRetry);
        Assert.Empty(copy.Items);
    }

    [Fact]
    public void A_recycle_failure_retries_recycle_and_does_not_offer_forever()
    {
        var copy = Outcome(DeletionMode.RecycleBin, files: 26, failures: new[]
        {
            Fail(@"C:\Users\Sam\Downloads\Budget 2024.xlsx", 32),
            Fail(@"C:\Users\Sam\Downloads\Scan0021.pdf", 32),
        });

        Assert.Equal("Could not move 2 files", copy.Title);
        Assert.Equal("The other 26 files are in the Recycle Bin. These 2 are still in the folder.", copy.Body);
        Assert.Equal(ScreenCopy.PartialNoteRecycle, copy.Note);
        Assert.DoesNotContain("forever", copy.Note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("forever", copy.PrimaryLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Try these 2 again", copy.PrimaryLabel);
        Assert.True(copy.ShowClose);
        Assert.True(copy.IsRetry);
        Assert.Equal(DeletionMode.RecycleBin, copy.RetryMode);
        Assert.Equal(new[] { "Budget 2024.xlsx", "Scan0021.pdf" }, copy.Items.Select(i => i.Name));
        Assert.All(copy.Items, i => Assert.Equal(ScreenCopy.InUseReason, i.Detail));
    }

    [Fact]
    public void A_forever_failure_retries_forever_and_asks_first()
    {
        var copy = Outcome(DeletionMode.Permanent, files: 26, failures: new[]
        {
            Fail(@"C:\Users\Sam\Downloads\Budget 2024.xlsx", 32),
            Fail(@"C:\Users\Sam\Downloads\Scan0021.pdf", 33),
        });

        Assert.Equal("Could not delete 2 files", copy.Title);
        Assert.Equal("The other 26 files were deleted. These 2 are still in the folder.", copy.Body);
        Assert.Equal(ScreenCopy.PartialNoteForever, copy.Note);
        Assert.Contains("say yes again", copy.Note);
        Assert.Equal(DeletionMode.Permanent, copy.RetryMode);
        Assert.Equal("Try these 2 again", copy.PrimaryLabel);
    }

    [Fact]
    public void One_file_that_could_not_be_moved_is_the_single_failure_screen()
    {
        var copy = Outcome(DeletionMode.RecycleBin, files: 0, failures: new[]
        {
            Fail(@"C:\Users\Sam\Downloads\Budget 2024.xlsx", 32),
        });

        Assert.Equal("This file could not be moved", copy.Title);
        Assert.Equal(ScreenCopy.InUseBody, copy.Body);
        Assert.Equal("Try again", copy.PrimaryLabel);
        Assert.Equal(ScreenCopy.SingleNote, copy.Note);
        Assert.True(copy.ShowClose);
        Assert.Equal(DeletionMode.RecycleBin, copy.RetryMode);
        Assert.Equal("Budget 2024.xlsx", copy.Items[0].Name);
    }

    [Fact]
    public void A_second_look_at_the_same_failure_does_not_offer_another_try()
    {
        var copy = Outcome(DeletionMode.RecycleBin, files: 0, failures: new[]
        {
            Fail(@"C:\Users\Sam\Downloads\Budget 2024.xlsx", 32),
        }, allowRetry: false);

        Assert.False(copy.IsRetry);
        Assert.Equal("Close", copy.PrimaryLabel);
        Assert.False(copy.ShowClose);
        Assert.Equal("", copy.Note);
    }

    [Fact]
    public void Breadcrumbs_use_plain_names()
    {
        var crumbs = DeleteSafety.Breadcrumbs(@"C:\Users\Sam\Downloads\Receipts 2023", @"C:\Users\Sam");

        Assert.Equal(new[] { "This PC", "Sam", "Downloads", "Receipts 2023" }, crumbs.Select(c => c.Name));
        Assert.Equal(@"C:\Users\Sam\Downloads", crumbs[2].Path);
    }

    [Fact]
    public void A_drive_crumb_does_not_look_like_a_folder()
    {
        var crumbs = DeleteSafety.Breadcrumbs(@"D:\Photos", @"C:\Users\Sam");

        Assert.Equal(new[] { "This PC", "D: drive", "Photos" }, crumbs.Select(c => c.Name));
    }

    [Fact]
    public void The_left_list_highlights_the_open_place()
    {
        var places = new DeleteSafety.NavPlaceRef[]
        {
            new("C: drive", @"C:\"),
            new("D: drive", @"D:\"),
            new("Downloads", @"C:\Users\Sam\Downloads"),
            new("Pictures", @"C:\Users\Sam\Pictures"),
        };

        Assert.Equal("Downloads", DeleteSafety.ActiveNavLabel(@"C:\Users\Sam\Downloads\Receipts 2023", places));
        Assert.Equal("Pictures", DeleteSafety.ActiveNavLabel(@"C:\Users\Sam\Pictures\Camera uploads", places));
        Assert.Equal("C: drive", DeleteSafety.ActiveNavLabel(@"C:\", places));
        Assert.Null(DeleteSafety.ActiveNavLabel(@"C:\Users\Sam", places));
    }

    private static OutcomeCopy Outcome(DeletionMode mode, long files, IReadOnlyList<DeleteFailure> failures, bool allowRetry = true)
    {
        var result = new DeletionResult
        {
            FilesDeleted = files,
            DirectoriesDeleted = 0,
            LinksDeleted = 0,
            Failures = failures,
            Elapsed = TimeSpan.FromSeconds(1),
            WasCancelled = false,
        };
        var plan = DeleteSafety.PlanRetry(mode, failures, wasCancelled: false);
        return ScreenCopy.DescribeOutcome(mode, result, plan, allowRetry);
    }

    private static DeleteFailure Fail(string path, int code) => new()
    {
        Path = path,
        ErrorCode = code,
        Message = "The process cannot access the file because it is being used by another process.",
        Kind = WorkItemKind.File,
    };
}
