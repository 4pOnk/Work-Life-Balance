using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WorkLifeBalance.Application;
using WorkLifeBalance.Domain;

namespace WorkLifeBalance.Windows;

public sealed class TrayContext : ApplicationContext
{
    private readonly Coordinator coordinator;
    private readonly ILocalLog log;
    private readonly Control dispatcher = new();
    private readonly MessageWindow window;
    private readonly NotifyIcon icon;
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 2000 };
    private readonly ToolStripMenuItem statusItem = new("Запуск...") { Enabled = false };
    private readonly ToolStripMenuItem pauseItem = new("Приостановить учёт");
    private readonly ToolStripMenuItem afkItem = new("Включить AFK");
    private readonly Action onExit;
    private string? pendingNotice;
    private bool unavailable;
    private bool closing;
    public string? HotkeyError { get; private set; }
    public string? ServerError { get; set; }
    public string Address { get; set; }

    public TrayContext(Coordinator coordinator, ILocalLog log, TrackerSettings settings, Action onExit)
    {
        this.coordinator = coordinator;
        this.log = log;
        this.onExit = onExit;
        Address = $"http://127.0.0.1:{settings.Port}";
        _ = dispatcher.Handle;
        window = new MessageWindow(Dispatch, settings.Hotkey);
        HotkeyError = window.Error;
        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Открыть статистику", null, (_, _) => Open());
        afkItem.Click += (_, _) => _ = coordinator.Step(Command.ToggleAfk);
        pauseItem.Click += (_, _) => _ = coordinator.Step(Command.TogglePause);
        menu.Items.Add(afkItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add("Настройки", null, (_, _) => Open("/#settings"));
        menu.Items.Add("Порт веб-интерфейса…", null, async (_, _) => await ConfigurePort());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        icon = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "Work Life Balance", ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => Open();
        coordinator.StatusChanged += OnStatus;
        statusTimer.Tick += async (_, _) => await Refresh();
        statusTimer.Start();
        if (HotkeyError is not null) Notify(HotkeyError);
    }

    public Task<string?> ConfigureHotkey(string value) => dispatcher.InvokeAsync(() =>
    {
        HotkeyError = window.Register(value);
        if (HotkeyError is not null) Notify(HotkeyError);
        return HotkeyError;
    });

    public void RequestOpen()
    {
        if (!closing) dispatcher.BeginInvoke(() => Open());
    }

    public void RequestExit()
    {
        if (!closing) dispatcher.BeginInvoke(ExitThread);
    }

    public void Open(string suffix = "")
    {
        if (ServerError is not null)
        {
            MessageBox.Show(ServerError + "\nИзмените порт через меню трея и перезапустите приложение.",
                "Work Life Balance", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(Address + suffix) { UseShellExecute = true }); }
        catch (Win32Exception e) { log.Error("open-browser", e); Notify("Не удалось открыть браузер. Адрес: " + Address); }
    }

    private async Task ConfigurePort()
    {
        var settings = await coordinator.GetSettings();
        using var form = new Form
        {
            Text = "Порт веб-интерфейса",
            Width = 340,
            Height = 160,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        var input = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = settings.Port, Left = 20, Top = 20, Width = 280 };
        var save = new Button { Text = "Сохранить", Left = 180, Top = 65, Width = 120, DialogResult = DialogResult.OK };
        form.Controls.Add(input); form.Controls.Add(save); form.AcceptButton = save;
        if (form.ShowDialog() != DialogResult.OK) return;
        try
        {
            await coordinator.UpdateSettings(settings with { Port = (int)input.Value });
            Notify("Порт сохранён. Перезапустите приложение, чтобы применить изменение.");
        }
        catch (Exception e) { log.Error("settings-port", e); Notify("Не удалось сохранить порт."); }
    }

    private void Dispatch(Command command)
    {
        if (command is Command.Lock or Command.Suspend) unavailable = true;
        if (command is Command.Unlock or Command.Resume) unavailable = false;
        _ = coordinator.Step(command);
    }

    private void OnStatus(StatusChange status)
    {
        if (closing) return;
        var message = status.Message ?? (status.Paused ? "Учёт приостановлен" : status.Presence switch
        {
            Presence.ManualAfk => "AFK включён вручную",
            Presence.AutomaticAfk => "Автоматический AFK с " + DateTimeOffset.FromUnixTimeMilliseconds(status.Since).ToLocalTime().ToString("HH:mm"),
            Presence.SystemAfk => "AFK: компьютер заблокирован или спит",
            _ => "AFK выключен. Учёт активности возобновлён"
        });
        dispatcher.BeginInvoke(() =>
        {
            if (unavailable) pendingNotice = message;
            else { pendingNotice = null; Notify(message); }
        });
    }

    private async Task Refresh()
    {
        statusTimer.Stop();
        try
        {
            var snapshot = await coordinator.Snapshot();
            var state = snapshot.State;
            unavailable = state.Locked || state.Suspended;
            var label = state.Paused ? "Учёт приостановлен" : state.Presence == Presence.Active ?
                "Учёт активен" : "AFK";
            statusItem.Text = label;
            icon.Text = "Work Life Balance: " + label;
            afkItem.Text = state.Presence == Presence.Active ? "Включить AFK" : "Выключить AFK";
            pauseItem.Text = state.Paused ? "Возобновить учёт" : "Приостановить учёт";
            if (!unavailable && pendingNotice is not null) { pendingNotice = null; Notify(label); }
        }
        catch (Exception e) { log.Error("tray-status", e); statusItem.Text = "Ошибка учёта"; }
        finally { if (!closing) statusTimer.Start(); }
    }

    public void Notify(string message) => icon.ShowBalloonTip(5000, "Work Life Balance", message, ToolTipIcon.Info);

    protected override void ExitThreadCore()
    {
        closing = true;
        statusTimer.Stop();
        coordinator.StatusChanged -= OnStatus;
        icon.Visible = false;
        onExit();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true;
            coordinator.StatusChanged -= OnStatus;
            statusTimer.Dispose(); icon.ContextMenuStrip?.Dispose(); icon.Dispose(); window.Dispose(); dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action<Command> dispatch;
        public string? Error { get; private set; }
        public MessageWindow(Action<Command> dispatch, string hotkey)
        {
            this.dispatch = dispatch;
            CreateHandle(new CreateParams { Caption = "WorkLifeBalance.Events" });
            if (!Native.WTSRegisterSessionNotification(Handle, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Register(hotkey);
        }
        public string? Register(string value)
        {
            var parsed = Hotkey.Parse(value);
            Native.UnregisterHotKey(Handle, 1);
            Error = Native.RegisterHotKey(Handle, 1, parsed.Modifiers, parsed.Key) ? null :
                $"Не удалось зарегистрировать {value} (код {Marshal.GetLastWin32Error()}). Выберите другой хоткей в настройках.";
            return Error;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312) dispatch(Command.ToggleAfk);
            if (message.Msg == 0x02B1)
            {
                if (message.WParam == 7) dispatch(Command.Lock);
                if (message.WParam == 8) dispatch(Command.Unlock);
            }
            if (message.Msg == 0x0218)
            {
                if (message.WParam == 4) dispatch(Command.Suspend);
                if (message.WParam == 18) dispatch(Command.Resume);
            }
            base.WndProc(ref message);
        }
        public void Dispose()
        {
            Native.UnregisterHotKey(Handle, 1);
            Native.WTSUnRegisterSessionNotification(Handle);
            DestroyHandle();
        }
    }
}
