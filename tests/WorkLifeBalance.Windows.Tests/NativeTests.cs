using System.Runtime.InteropServices;
using WorkLifeBalance.Windows;
using Xunit;

namespace WorkLifeBalance.Windows.Tests;

public sealed class NativeTests
{
    [Fact]
    public void Foreground_and_process_catalog_use_real_windows_sources()
    {
        var source = new WindowsActivitySource();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var observation = source.Observe(now);
        Assert.True(observation.LastInput <= now);
        Assert.NotEmpty(observation.Activity.Executable);
        Assert.NotEmpty(source.List());
        Assert.Contains(source.List(), p => p.Executable.Contains("testhost", StringComparison.OrdinalIgnoreCase));
        _ = WindowsActivitySource.IsSessionLocked();
    }

    [Fact]
    public void Native_hotkey_registration_reports_a_collision()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var owner = new Control();
                using var competitor = new Control();
                var key = Hotkey.Parse("Ctrl+Alt+Shift+F11");
                Assert.True(RegisterHotKey(owner.Handle, 81, key.Modifiers, key.Key));
                try { Assert.False(RegisterHotKey(competitor.Handle, 82, key.Modifiers, key.Key)); }
                finally { UnregisterHotKey(owner.Handle, 81); UnregisterHotKey(competitor.Handle, 82); }
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) throw failure;
    }

    [Theory]
    [InlineData("Shift+N")]
    [InlineData("Win+Win+N")]
    [InlineData("Ctrl+F25")]
    [InlineData("Win+N+Z")]
    public void Invalid_hotkeys_are_rejected(string value) => Assert.Throws<ArgumentException>(() => Hotkey.Parse(value));

    [Fact]
    public void Preferred_hotkey_includes_no_repeat()
    {
        var key = Hotkey.Parse("Win+Shift+N");
        Assert.Equal(0x400Cu, key.Modifiers);
        Assert.Equal(78u, key.Key);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint handle, int id);
}
