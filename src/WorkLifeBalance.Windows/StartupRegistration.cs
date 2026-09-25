using Microsoft.Win32;

namespace WorkLifeBalance.Windows;

public sealed class StartupRegistration(string executable, string dataDirectory)
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "WorkLifeBalance";
    private string CommandLine => $"\"{Path.GetFullPath(executable)}\" --data-dir \"{Path.GetFullPath(dataDirectory)}\"";

    public bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name) as string == CommandLine; }
    }

    public void Set(bool enabled)
    {
        if (CommandLine.Length > 260) throw new ArgumentException("Путь слишком длинный для автозапуска Windows.");
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        if (enabled) key.SetValue(Name, CommandLine, RegistryValueKind.String);
        else if (key.GetValue(Name) as string == CommandLine) key.DeleteValue(Name, false);
    }
}
