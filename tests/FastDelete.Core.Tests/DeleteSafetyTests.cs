using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;
using FastDelete.Core.Interop;
using Xunit;

namespace FastDelete.Core.Tests;

public class DeleteSafetyTests
{
    private static DeleteFailure Failure(string path, int error, WorkItemKind kind = WorkItemKind.File)
        => new()
        {
            Path = path,
            ErrorCode = error,
            Message = "m",
            Kind = kind,
        };

    [Fact]
    public void Retry_keeps_recycle_bin_mode_and_asks_for_confirmation()
    {
        var failures = new[]
        {
            Failure(@"C:\Users\Mom\Downloads\a.txt", Win32.ERROR_SHARING_VIOLATION),
        };

        var plan = DeleteSafety.PlanRetry(DeletionMode.RecycleBin, failures, wasCancelled: false);

        Assert.Equal(DeletionMode.RecycleBin, plan.Mode);
        Assert.Equal(new[] { @"C:\Users\Mom\Downloads\a.txt" }, plan.Paths);
        Assert.True(plan.NeedsConfirmation);
    }

    [Fact]
    public void Retry_keeps_permanent_mode_and_names_the_failed_path()
    {
        var failures = new[]
        {
            Failure(@"C:\Users\Mom\Downloads\locked.txt", Win32.ERROR_SHARING_VIOLATION),
            Failure(@"C:\Users\Mom\Downloads\gone.txt", Win32.ERROR_FILE_NOT_FOUND),
        };

        var plan = DeleteSafety.PlanRetry(DeletionMode.Permanent, failures, wasCancelled: false);

        Assert.Equal(DeletionMode.Permanent, plan.Mode);
        Assert.Equal(new[] { @"C:\Users\Mom\Downloads\locked.txt" }, plan.Paths);
        Assert.True(plan.NeedsConfirmation);
    }

    [Fact]
    public void Cancelled_run_does_not_retry_directory_not_empty_leftovers()
    {
        var failures = new[]
        {
            Failure(@"C:\Users\Mom\Downloads\batch", Win32.ERROR_DIR_NOT_EMPTY, WorkItemKind.Directory),
            Failure(@"C:\Users\Mom\Downloads\locked.txt", Win32.ERROR_LOCK_VIOLATION),
        };

        var plan = DeleteSafety.PlanRetry(DeletionMode.Permanent, failures, wasCancelled: true);

        Assert.Equal(DeletionMode.Permanent, plan.Mode);
        Assert.Equal(new[] { @"C:\Users\Mom\Downloads\locked.txt" }, plan.Paths);
        Assert.DoesNotContain(plan.Paths, p => p.Contains("batch", StringComparison.OrdinalIgnoreCase));
        Assert.True(plan.NeedsConfirmation);
    }

    [Fact]
    public void Completed_run_can_still_retry_a_folder_that_was_not_empty()
    {
        var failures = new[]
        {
            Failure(@"C:\Users\Mom\Downloads\batch", Win32.ERROR_DIR_NOT_EMPTY, WorkItemKind.Directory),
        };

        var plan = DeleteSafety.PlanRetry(DeletionMode.Permanent, failures, wasCancelled: false);

        Assert.Equal(new[] { @"C:\Users\Mom\Downloads\batch" }, plan.Paths);
        Assert.True(plan.NeedsConfirmation);
    }

    [Fact]
    public void Cancelled_run_with_only_leftover_folders_has_nothing_to_confirm()
    {
        var failures = new[]
        {
            Failure(@"C:\Users\Mom\Downloads\batch", Win32.ERROR_DIR_NOT_EMPTY, WorkItemKind.Directory),
            Failure(@"C:\Users\Mom\Downloads\batch\inner", Win32.ERROR_DIR_NOT_EMPTY, WorkItemKind.Directory),
        };

        var plan = DeleteSafety.PlanRetry(DeletionMode.Permanent, failures, wasCancelled: true);

        Assert.Empty(plan.Paths);
        Assert.False(plan.NeedsConfirmation);
        Assert.Equal(DeletionMode.Permanent, plan.Mode);
    }

    [Theory]
    [InlineData(ConfirmCountState.Checking, false, false)]
    [InlineData(ConfirmCountState.Finished, false, true)]
    [InlineData(ConfirmCountState.Failed, false, true)]
    [InlineData(ConfirmCountState.Checking, true, false)]
    [InlineData(ConfirmCountState.Finished, true, false)]
    [InlineData(ConfirmCountState.Failed, true, false)]
    public void Confirm_waits_for_the_count_and_never_accepts_a_blocked_target(
        ConfirmCountState state, bool blocked, bool canAccept)
    {
        Assert.Equal(canAccept, DeleteSafety.CanAccept(state, blocked));
    }

    [Fact]
    public void Path_list_shows_the_actual_paths()
    {
        string text = DeleteSafety.FormatPathList(new[]
        {
            @"C:\Users\Mom\Downloads\vacation",
            @"D:\Photos\2019",
        });

        Assert.Contains(@"C:\Users\Mom\Downloads\vacation", text);
        Assert.Contains(@"D:\Photos\2019", text);
        Assert.DoesNotContain("2 items", text);
    }

    [Fact]
    public void Long_path_list_keeps_the_first_paths_and_says_how_many_more()
    {
        var paths = Enumerable.Range(0, 25).Select(i => $@"C:\Users\Mom\Downloads\folder{i}").ToArray();

        string text = DeleteSafety.FormatPathList(paths, maxShown: 20);

        Assert.Contains(@"C:\Users\Mom\Downloads\folder0", text);
        Assert.Contains(@"C:\Users\Mom\Downloads\folder19", text);
        Assert.DoesNotContain(@"C:\Users\Mom\Downloads\folder20", text);
        Assert.Contains("and 5 more", text);
    }

    [Fact]
    public void Count_failure_message_is_visible_plain_english()
    {
        Assert.False(string.IsNullOrWhiteSpace(DeleteSafety.CountFailedMessage));
        Assert.Contains("inside", DeleteSafety.CountFailedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", DeleteSafety.CountFailedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"c:/")]
    [InlineData(@"C:")]
    [InlineData(@"\\?\C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"E:/")]
    public void Drive_roots_are_blocked(string path)
    {
        var guard = DeleteSafety.CheckTargets(new[] { path }, Array.Empty<string>());

        Assert.False(guard.Allowed);
        Assert.Contains("whole drive", guard.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@":\", guard.Explanation);
        Assert.NotEmpty(guard.BlockedPaths);
    }

    [Fact]
    public void Network_share_root_is_blocked()
    {
        var guard = DeleteSafety.CheckTargets(new[] { @"\\server\share" }, Array.Empty<string>());

        Assert.False(guard.Allowed);
        Assert.Contains(@"\\server\share", guard.Explanation);
        Assert.Contains("network", guard.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Folder_inside_a_network_share_is_allowed()
    {
        var guard = DeleteSafety.CheckTargets(new[] { @"\\server\share\Public\old" }, Array.Empty<string>());

        Assert.True(guard.Allowed);
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\")]
    [InlineData(@"c:\windows\system32")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\SomeApp")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\Program Files (x86)\Vendor\tool.exe")]
    [InlineData(@"D:\Windows\Temp")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"C:/Program Files/Old")]
    public void Windows_and_program_files_are_blocked(string path)
    {
        var guard = DeleteSafety.CheckTargets(new[] { path }, Array.Empty<string>());

        Assert.False(guard.Allowed);
        Assert.Contains("system folder", guard.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(DeleteSafety.Canonical(path), guard.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\WindowsOld")]
    [InlineData(@"C:\Windows.old")]
    [InlineData(@"C:\ProgramFiles")]
    [InlineData(@"C:\Users\Mom\Downloads\vacation")]
    [InlineData(@"C:\Users\Mom\Documents\Taxes")]
    [InlineData(@"D:\Photos\2019")]
    [InlineData(@"C:\notes.txt")]
    [InlineData(@"C:\Users\Mom\Windows")]
    [InlineData(@"C:\Users\Mom\My Programs\old")]
    [InlineData(@"C:\Program Files Extra\notes")]
    public void A_normal_folder_the_user_picked_is_allowed(string path)
    {
        var guard = DeleteSafety.CheckTargets(new[] { path }, Array.Empty<string>());

        Assert.True(guard.Allowed);
        Assert.Empty(guard.BlockedPaths);
    }

    [Fact]
    public void Mixed_selection_names_the_system_path_and_blocks_the_whole_delete()
    {
        var guard = DeleteSafety.CheckTargets(new[]
        {
            @"C:\Users\Mom\Downloads\vacation",
            @"C:\Windows",
            @"D:\Photos\2019",
        }, Array.Empty<string>());

        Assert.False(guard.Allowed);
        Assert.Contains(@"C:\Windows", guard.Explanation);
        Assert.DoesNotContain(@"C:\Users\Mom\Downloads\vacation", guard.BlockedPaths);
        Assert.Single(guard.BlockedPaths);
    }

    [Fact]
    public void Select_all_on_a_drive_is_blocked_because_it_includes_the_system_folders()
    {
        var guard = DeleteSafety.CheckTargets(new[]
        {
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\Users",
        }, Array.Empty<string>());

        Assert.False(guard.Allowed);
        Assert.Contains(@"C:\Windows", guard.Explanation);
        Assert.Contains(@"C:\Program Files", guard.Explanation);
        Assert.Contains("one click", guard.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Localized_system_directory_from_this_pc_is_blocked()
    {
        var guard = DeleteSafety.CheckTargets(
            new[] { @"D:\WinNT\System32\config" },
            new[] { @"D:\WinNT" });

        Assert.False(guard.Allowed);
        Assert.Contains(@"D:\WinNT\System32\config", guard.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_drive_root_in_the_extra_list_does_not_block_every_folder_on_that_drive()
    {
        var guard = DeleteSafety.CheckTargets(
            new[] { @"C:\Users\Mom\Downloads\vacation" },
            new[] { @"C:\" });

        Assert.True(guard.Allowed);
    }

    [Fact]
    public void Extra_protected_root_does_not_block_a_normal_folder()
    {
        var guard = DeleteSafety.CheckTargets(
            new[] { @"C:\Users\Mom\Downloads\vacation" },
            new[] { @"C:\Windows" });

        Assert.True(guard.Allowed);
    }

    [Fact]
    public void Cancelled_summary_says_already_deleted_files_stay_deleted()
    {
        var result = new DeletionResult
        {
            FilesDeleted = 12,
            DirectoriesDeleted = 0,
            LinksDeleted = 0,
            Failures = Array.Empty<DeleteFailure>(),
            Elapsed = TimeSpan.FromSeconds(1.2),
            WasCancelled = true,
        };

        string text = DeleteSafety.Summarize(result);

        Assert.Contains("Stopped", text);
        Assert.Contains("stay deleted", text);
        Assert.Contains("left alone", text);
    }

    [Fact]
    public void Successful_summary_keeps_the_done_wording()
    {
        var result = new DeletionResult
        {
            FilesDeleted = 4,
            DirectoriesDeleted = 1,
            LinksDeleted = 0,
            Failures = Array.Empty<DeleteFailure>(),
            Elapsed = TimeSpan.FromSeconds(1.5),
            WasCancelled = false,
        };

        string text = DeleteSafety.Summarize(result);

        Assert.StartsWith("Done — deleted", text);
        Assert.Contains(result.Elapsed.TotalSeconds.ToString("F1"), text);
        Assert.Contains("seconds", text);
    }

    [Fact]
    public async Task Already_cancelled_permanent_delete_is_not_a_retryable_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await new DeletionEngine().DeleteAsync(
            new[] { @"C:\Users\Mom\Downloads\batch" },
            cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Empty(result.Failures);
        Assert.Equal(0, result.TotalItems);
        var plan = DeleteSafety.PlanRetry(DeletionMode.Permanent, result.Failures, result.WasCancelled);
        Assert.Empty(plan.Paths);
        Assert.False(plan.NeedsConfirmation);
    }

    [Fact]
    public async Task Already_cancelled_recycle_bin_delete_is_not_a_permanent_retry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await RecycleBinDeleter.DeleteAsync(
            new[] { @"C:\Users\Mom\Downloads\notes.txt" },
            cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Empty(result.Failures);
        var plan = DeleteSafety.PlanRetry(DeletionMode.RecycleBin, result.Failures, result.WasCancelled);
        Assert.Equal(DeletionMode.RecycleBin, plan.Mode);
        Assert.Empty(plan.Paths);
    }
}
