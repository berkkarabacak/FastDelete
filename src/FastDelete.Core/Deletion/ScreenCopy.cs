using FastDelete.Core.Enumeration;

namespace FastDelete.Core.Deletion;

/// <summary>One named row in a confirm or result list.</summary>
public sealed class ScreenItem
{
    public ScreenItem(string name, string detail, bool isFolder)
    {
        Name = name;
        Detail = detail;
        IsFolder = isFolder;
    }

    public string Name { get; }
    public string Detail { get; }
    public bool IsFolder { get; }
}

public sealed record ConfirmCopy(
    string Title,
    string Body,
    bool BodyStrong,
    string? Body2,
    string ListLabel,
    string YesLabel,
    string CancelLabel,
    bool UseRed,
    IReadOnlyList<ScreenItem> Items);

public sealed record WorkCopy(
    string Title,
    string Body,
    string Counter,
    string Now,
    string PauseLabel,
    string Note,
    double Fraction);

public sealed record OutcomeCopy(
    string Title,
    string Body,
    bool ShowCheck,
    IReadOnlyList<ScreenItem> Items,
    string Note,
    string PrimaryLabel,
    bool ShowClose,
    bool IsRetry,
    DeletionMode RetryMode);

/// <summary>
/// Words for the screens. Recycle Bin is the safe action. Red is only the forever confirm.
/// A Recycle Bin failure retries Move to Recycle Bin and never offers delete forever.
/// </summary>
public static class ScreenCopy
{
    public const string FirstTitle = "Nothing of yours is open.";
    public const string FirstBody = "FastDelete has not looked at any of your files. Pick one of these two to begin.";
    public const string FirstDownloadsHelp = "Shows what is in your Downloads folder. Nothing is changed until you choose.";
    public const string FirstPracticeHelp = "Makes a new folder with fake practice files in it. It is made only when you press this button. Your own files are not used.";
    public const string FirstFooter = "Files you remove can be brought back from the Recycle Bin. FastDelete always asks you before it does anything.";

    public const string Tip = "Click the files you want. Then press the big button, Move to Recycle Bin. You can get them back later.";
    public const string TipHint = "Hold Ctrl or Shift to click more than one.";

    public const string EmptyTitle = "This folder is empty.";
    public const string EmptyBody = "There is nothing to move here. Use the list on the left to open another folder.";

    public const string DriveGuardBody = "The big button does nothing here. Open a folder inside it to choose what to remove.";

    public const string CancelLabel = "Cancel, keep my files";
    public const string YesRecycle = "Yes, move to Recycle Bin";
    public const string YesForever = "Yes, delete forever";
    public const string DeleteForeverLabel = "Delete forever";
    public const string FolderDetail = "Folder, and all inside it";

    public const string RecycleBody = "You can get these back later from the Recycle Bin.";
    public const string ForeverBody = "You cannot get them back.";
    public const string ForeverBody2 = "They will not go to the Recycle Bin. The folder and everything inside it will be deleted too.";
    public const string ForeverFilesBody2 = "They will not go to the Recycle Bin.";

    public const string InUseReason = "Another program is using it";
    public const string InUseBody = "Another program is using it. Close that program, then try again.";

    public const string PartialNoteRecycle = "Trying again does the same thing, Move to Recycle Bin. You will be asked to say yes again first.";
    public const string PartialNoteForever = "Trying again does the same thing, Delete forever. You will be asked to say yes again first.";
    public const string SingleNote = "Trying again asks you first, the same way as before.";

    public const string WorkNoteRecycle = "If you press Stop, the rest stay where they are. The ones already moved are in the Recycle Bin. Nothing is deleted forever.";
    public const string WorkNoteForever = "If you press Stop, the rest of your files stay where they are. The ones already deleted cannot be brought back.";

    public const string CheckingLine = "Checking what's inside…";

    public static string RecycleLabel(int selectedCount)
        => selectedCount > 0 ? $"Move to Recycle Bin ({selectedCount:N0})" : "Move to Recycle Bin";

    /// <summary>Both action buttons do nothing when nothing is chosen, or a whole drive is open.</summary>
    public static bool CanAct(int selectedCount, bool driveRootOpen)
        => selectedCount > 0 && !driveRootOpen;

    public static string DriveGuardTitle(string driveLabel)
        => $"The {driveLabel} cannot be deleted.";

    public static string ItemCount(int count)
        => count == 1 ? "1 item" : $"{count:N0} items";

    public static ConfirmCopy Confirm(DeletionMode mode, IReadOnlyList<ScreenItem> items)
    {
        int folders = 0;
        foreach (var item in items)
            if (item.IsFolder) folders++;
        int files = items.Count - folders;
        bool recycle = mode == DeletionMode.RecycleBin;
        bool anyFolder = folders > 0;

        string title = recycle
            ? RecycleTitle(files, folders)
            : "Delete these forever?";
        string listLabel = SelectionPhrase(files, folders);
        string? body2 = recycle ? null : anyFolder ? ForeverBody2 : ForeverFilesBody2;

        return new ConfirmCopy(
            title,
            recycle ? RecycleBody : ForeverBody,
            BodyStrong: !recycle,
            body2,
            listLabel,
            recycle ? YesRecycle : YesForever,
            CancelLabel,
            UseRed: !recycle,
            items);
    }

    public static ScreenItem ItemFromPath(string path)
    {
        string name = path;
        var named = DeleteSafety.ConfirmNames(new[] { path }, 1);
        if (named.Count > 0 && named[0].Name.Length > 0)
            name = named[0].Name;
        bool folder = false;
        try
        {
            folder = Directory.Exists(path);
        }
        catch
        {
            folder = false;
        }
        return new ScreenItem(name, folder ? FolderDetail : "", folder);
    }

    public static WorkCopy Working(DeletionMode mode, bool paused, long done, long total, string? currentItem)
    {
        bool forever = mode == DeletionMode.Permanent;
        string title = paused
            ? "Paused"
            : forever ? "Deleting, please wait" : "Moving to the Recycle Bin";
        string body = paused
            ? "Nothing is moving right now. Press Keep going when you are ready."
            : forever
                ? "This can take a few minutes. You can leave this window open."
                : "Please wait. This can take a few minutes.";
        string counter = total > 0
            ? $"{Math.Max(0, done):N0} of {total:N0} files"
            : done == 1 ? "1 file" : $"{Math.Max(0, done):N0} files";
        string name = FileNameOf(currentItem);
        string now = name.Length == 0 ? "" : paused ? $"Stopped at {name}" : $"Now: {name}";
        double fraction = total > 0 ? Math.Clamp((double)done / total, 0, 1) : -1;
        return new WorkCopy(
            title,
            body,
            counter,
            now,
            paused ? "Keep going" : "Pause",
            forever ? WorkNoteForever : WorkNoteRecycle,
            fraction);
    }

    public static OutcomeCopy DescribeOutcome(
        DeletionMode mode,
        DeletionResult result,
        DeleteSafety.RetryPlan plan,
        bool allowRetry = true)
    {
        bool recycle = mode == DeletionMode.RecycleBin;
        bool retry = allowRetry && plan.NeedsConfirmation && plan.Paths.Count > 0 && plan.Mode == mode;

        if (result.WasCancelled && !retry)
        {
            string stopped = result.TotalItems == 0
                ? recycle
                    ? "Nothing was moved. Nothing is deleted forever."
                    : "Nothing was deleted."
                : recycle
                    ? "The ones already moved are in the Recycle Bin. The rest stay where they are. Nothing is deleted forever."
                    : "The ones already deleted cannot be brought back. The rest of your files stay where they are.";
            return new OutcomeCopy("Stopped", stopped, false, Array.Empty<ScreenItem>(), "", "Done", false, false, mode);
        }

        if (result.Failures.Count == 0)
        {
            bool filesOnly = result.DirectoriesDeleted == 0 && result.LinksDeleted == 0;
            string things = CountNoun(result.TotalItems, filesOnly);
            string title = recycle ? $"Moved {things} to the Recycle Bin" : $"Deleted {things}";
            string body = recycle
                ? "You can get them back later from the Recycle Bin."
                : ForeverBody;
            return new OutcomeCopy(title, body, true, Array.Empty<ScreenItem>(), "", "Done", false, false, mode);
        }

        var (files, folders) = Split(result.Failures);
        bool single = result.Failures.Count == 1 && result.TotalItems == 0;
        string failThings = SelectionPhrase(files, folders);
        string titleFail = single
            ? SingleTitle(result.Failures[0], recycle)
            : recycle ? $"Could not move {failThings}" : $"Could not delete {failThings}";

        string bodyFail;
        if (single)
            bodyFail = FailureBody(result.Failures[0]);
        else if (result.TotalItems == 0)
            bodyFail = StillHere(result.Failures.Count);
        else
        {
            bool okFiles = result.DirectoriesDeleted == 0 && result.LinksDeleted == 0;
            bodyFail = OtherClause(result.TotalItems, okFiles, recycle) + " " + StillHere(result.Failures.Count);
        }

        var rows = new List<ScreenItem>(result.Failures.Count);
        foreach (var failure in result.Failures)
            rows.Add(new ScreenItem(ItemName(failure.Path), FailureReason(failure), failure.Kind == WorkItemKind.Directory));

        string note = "";
        string primary = "Close";
        bool showClose = false;
        if (retry)
        {
            note = single ? SingleNote : recycle ? PartialNoteRecycle : PartialNoteForever;
            primary = plan.Paths.Count <= 1 ? "Try again" : $"Try these {plan.Paths.Count:N0} again";
            showClose = true;
        }

        return new OutcomeCopy(titleFail, bodyFail, false, rows, note, primary, showClose, retry, mode);
    }

    public static string FailureReason(DeleteFailure failure)
        => IsInUse(failure) ? InUseReason
            : string.IsNullOrWhiteSpace(failure.Message) ? "It could not be changed." : failure.Message.Trim();

    public static string FailureBody(DeleteFailure failure)
    {
        if (IsInUse(failure))
            return InUseBody;
        string message = FailureReason(failure);
        return message.EndsWith('.') ? message + " Then try again." : message + ". Then try again.";
    }

    public static string FileNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        string value = path.Trim().Replace('/', '\\');
        if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
            value = value[4..];
        value = value.TrimEnd('\\');
        int slash = value.LastIndexOf('\\');
        return slash >= 0 && slash < value.Length - 1 ? value[(slash + 1)..] : value;
    }

    private static bool IsInUse(DeleteFailure failure)
        => failure.ErrorCode is 32 or 33;

    private static string ItemName(string path)
    {
        var named = DeleteSafety.ConfirmNames(new[] { path }, 1);
        return named.Count > 0 && named[0].Name.Length > 0 ? named[0].Name : path;
    }

    private static string RecycleTitle(int files, int folders)
    {
        if (files == 1 && folders == 0)
            return "Move this file to the Recycle Bin?";
        if (folders == 1 && files == 0)
            return "Move this folder to the Recycle Bin?";
        if (files > 0 && folders > 0)
            return $"Move these {files + folders:N0} items to the Recycle Bin?";
        return $"Move these {SelectionPhrase(files, folders)} to the Recycle Bin?";
    }

    private static string SingleTitle(DeleteFailure failure, bool recycle)
    {
        string thing = failure.Kind == WorkItemKind.Directory ? "folder" : "file";
        string verb = recycle ? "moved" : "deleted";
        return $"This {thing} could not be {verb}";
    }

    private static string StillHere(int count)
        => count == 1 ? "This one is still in the folder." : $"These {count:N0} are still in the folder.";

    private static string OtherClause(long count, bool filesOnly, bool recycle)
    {
        bool one = count == 1;
        string noun = filesOnly ? (one ? "file" : "files") : (one ? "item" : "items");
        if (recycle)
            return one ? "The other file is in the Recycle Bin." : $"The other {count:N0} {noun} are in the Recycle Bin.";
        return one ? "The other file was deleted." : $"The other {count:N0} {noun} were deleted.";
    }

    private static string SelectionPhrase(int files, int folders)
    {
        if (files > 0 && folders == 0)
            return files == 1 ? "1 file" : $"{files:N0} files";
        if (folders > 0 && files == 0)
            return folders == 1 ? "1 folder" : $"{folders:N0} folders";
        string folderPart = folders == 1 ? "1 folder" : $"{folders:N0} folders";
        string filePart = files == 1 ? "1 file" : $"{files:N0} files";
        return $"{folderPart} and {filePart}";
    }

    private static string CountNoun(long count, bool filesOnly)
    {
        if (filesOnly)
            return count == 1 ? "1 file" : $"{count:N0} files";
        return count == 1 ? "1 item" : $"{count:N0} items";
    }

    private static (int files, int folders) Split(IReadOnlyList<DeleteFailure> failures)
    {
        int folders = 0;
        foreach (var failure in failures)
            if (failure.Kind == WorkItemKind.Directory)
                folders++;
        return (failures.Count - folders, folders);
    }
}
