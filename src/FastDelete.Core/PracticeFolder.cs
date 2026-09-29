namespace FastDelete.Core;

/// <summary>
/// A throwaway folder for learning the buttons. It is created only when someone
/// asks for it, and it does not contain their photos or documents.
/// </summary>
public static class PracticeFolder
{
    public const string ReadMeName = "Read me.txt";
    public const string NoteFolderName = "Practice notes";
    public const string NoteFileName = "A practice note.txt";

    public const string ReadMeText =
        "These files are only for practice.\r\n" +
        "It is safe to move them to the Recycle Bin.\r\n" +
        "Your photos and documents are not in this folder.\r\n";

    public const string NoteText =
        "You can delete this. It is not a real document.\r\n";

    public static void Create(string root)
    {
        Directory.CreateDirectory(root);
        string notes = Path.Combine(root, NoteFolderName);
        Directory.CreateDirectory(notes);
        File.WriteAllText(Path.Combine(root, ReadMeName), ReadMeText);
        File.WriteAllText(Path.Combine(notes, NoteFileName), NoteText);
    }
}
