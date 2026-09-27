using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FastDelete.App.Services;

/// <summary>
/// Adds (or removes) a "Delete with FastDelete" entry to the Windows Explorer
/// right-click menu for folders. Writes only to HKCU - no admin rights needed.
/// </summary>
public static class ExplorerIntegration
{
    private const string ShellKeyPath = @"Software\Classes\Directory\shell\FastDelete";
    private const string CommandKeyPath = ShellKeyPath + @"\command";

    public static bool IsEnabled =>
        Registry.CurrentUser.OpenSubKey(CommandKeyPath) is not null;

    public static void Enable()
    {
        string exePath = Environment.ProcessPath
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FastDelete.exe");

        using var shell = Registry.CurrentUser.CreateSubKey(ShellKeyPath);
        shell?.SetValue(null, "Delete with FastDelete");
        shell?.SetValue("Icon", exePath);

        using var command = Registry.CurrentUser.CreateSubKey(CommandKeyPath);
        command?.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    public static void Disable()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ShellKeyPath, throwOnMissingSubKey: false);
    }

    public static void OpenInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public static void OpenFileWithDefaultApp(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
