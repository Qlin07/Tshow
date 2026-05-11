using System.IO;
using Microsoft.Win32;

namespace Tshow.Services;

public static class StartupService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Tshow";

    public static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(AppName);
        if (value is string path)
        {
            return string.Equals(path, GetExePath(), StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static void EnableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.SetValue(AppName, $"\"{GetExePath()}\"");
    }

    public static void DisableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key?.GetValue(AppName) != null)
        {
            key.DeleteValue(AppName, false);
        }
    }

    private static string GetExePath()
    {
        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Tshow.exe");
    }
}
