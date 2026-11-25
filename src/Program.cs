using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LumenLock;

static class Program
{
	[STAThread]
	static void Main(string[] args)
	{
		using var mutex = new Mutex(true, "LumenLock.SingleInstance", out bool first);
		if (!first)
		{
			// Ask the running copy to show itself.
			using var ev = EventWaitHandle.OpenExisting("LumenLock.Show");
			ev.Set();
			return;
		}
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new MainForm(args.Contains("--tray")));
	}
}

class MainForm : Form
{
	const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	static readonly string SettingsFile = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LumenLock", "settings.txt");

	readonly Hero hero = new();
	readonly Toggle guardT = new();
	readonly Toggle startT = new();
	readonly Swatch swatch = new(56, 30);
	PickerForm picker;
	long pickerClosed;
	readonly PillButton offBtn = new();
	ToolStripMenuItem trayOff;
	FileSystemWatcher watcher;
	long lastSave;
	readonly Label footer;
	readonly NotifyIcon tray;
	readonly ToolStripMenuItem trayGuard;
	readonly AutoResetEvent wake = new(false);
	readonly EventWaitHandle showEv = new(false, EventResetMode.AutoReset, "LumenLock.Show");

	volatile bool guard = true;
	volatile int interval = 1000;
	volatile int rgb;   // 0xRRGGBB, 0 = off
	int lastRgb = 0x0000FF;   // colour "Turn light on" goes back to
	volatile bool restoreLight;
	bool allowShow, exiting, hintShown;
	int applied;

	[DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

	public MainForm(bool startInTray)
	{
		allowShow = !startInTray;
		MigrateFromLedGuard();
		LoadSettings();

		SuspendLayout();
		AutoScaleDimensions = new SizeF(96, 96);
		AutoScaleMode = AutoScaleMode.Dpi;
		Text = "Lumen Lock";
		BackColor = Theme.Bg;
		ForeColor = Theme.Text;
		Font = new Font(Theme.FontName, 9f);
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		StartPosition = FormStartPosition.CenterScreen;
		ClientSize = new Size(380, 556);
		Icon = MakeIcon();

		Controls.Add(new Label { Text = "Lumen Lock", AutoSize = true, ForeColor = Theme.Text,
			Font = new Font(Theme.FontName, 16f, FontStyle.Bold), Location = new Point(18, 16) });
		Controls.Add(new Label { Text = "Controls your PS4 controller's light bar", AutoSize = true,
			ForeColor = Theme.Sub, Location = new Point(21, 50) });

		hero.Location = new Point(20, 80);
		hero.GuardOn = guard;
		hero.Light = LightColor;
		Controls.Add(hero);

		offBtn.Location = new Point(20, 248);
		offBtn.Size = new Size(340, 44);
		offBtn.Click += (_, _) => ToggleOff();
		Controls.Add(offBtn);

		guardT.Set(guard);
		guardT.Changed += (_, _) => SetGuard(guardT.On);
		Controls.Add(new Card("Lock light", "Re-applies it if anything changes it", guardT) { Location = new Point(20, 304) });

		swatch.Color = LightColor;
		swatch.Click += (_, _) => OpenPicker();
		Controls.Add(new Card("Light colour", "Pick any colour for the light bar", swatch) { Location = new Point(20, 378) });

		startT.Set(IsStartup());
		startT.Changed += (_, _) => SetStartup(startT.On);
		Controls.Add(new Card("Start with Windows", "Opens hidden in the tray", startT) { Location = new Point(20, 452) });

		footer = new Label { AutoSize = false, Size = new Size(340, 20), Location = new Point(20, 526),
			ForeColor = Color.FromArgb(100, 106, 116), Font = new Font(Theme.FontName, 8f), TextAlign = ContentAlignment.MiddleCenter,
			Text = "Close the window to keep it running in the tray" };
		Controls.Add(footer);
		ResumeLayout(false);
		UpdateOffButton();

		trayGuard = new ToolStripMenuItem("Lock light", null, (_, _) => SetGuard(!guard)) { Checked = guard };
		var menu = new ContextMenuStrip();
		menu.Items.Add("Open Lumen Lock", null, (_, _) => ShowMain());
		trayOff = new ToolStripMenuItem("", null, (_, _) => ToggleOff());
		menu.Items.Add(trayOff);
		menu.Items.Add(trayGuard);
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Advanced settings file…", null, (_, _) => OpenSettingsFile());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Exit", null, (_, _) => { exiting = true; Close(); });
		tray = new NotifyIcon { Icon = Icon, Text = "Lumen Lock", Visible = true, ContextMenuStrip = menu };
		UpdateOffButton();
		WatchSettings();
		tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

		if (!IsHandleCreated) CreateHandle();
		new Thread(Loop) { IsBackground = true, Name = "ds4" }.Start();
		new Thread(() => { while (true) { showEv.WaitOne(); BeginInvoke(ShowMain); } }) { IsBackground = true }.Start();
	}

	void Loop()
	{
		while (true)
		{
			(Conn conn, bool wrote) res;
			try
			{
				if (restoreLight) { restoreLight = false; res = Ds4.Apply(true, 0, 0, 64); res.wrote = false; }
				else { int c = rgb; res = Ds4.Apply(guard, (byte)(c >> 16), (byte)(c >> 8), (byte)c); }
			}
			catch { res = (Conn.None, false); }

			if (IsHandleCreated && !IsDisposed)
				BeginInvoke(() => OnTick(res.conn, res.wrote));
			wake.WaitOne(interval);
		}
	}

	void OnTick(Conn conn, bool wrote)
	{
		if (wrote) applied++;
		bool changed = hero.Conn != conn || hero.GuardOn != guard;
		hero.Conn = conn;
		hero.GuardOn = guard;
		hero.Detail = guard && applied > 0 ? $"Applied {applied:N0} times · last {DateTime.Now:HH:mm:ss}" : "";
		hero.Invalidate();
		if (changed)
			tray.Text = conn == Conn.None ? "Lumen Lock · no controller" : !guard ? "Lumen Lock · paused" : rgb == 0 ? "Lumen Lock · light off" : "Lumen Lock · #" + rgb.ToString("X6");
	}

	void SetGuard(bool on)
	{
		guard = on;
		if (!on) restoreLight = true;
		guardT.Set(on);
		trayGuard.Checked = on;
		hero.GuardOn = on;
		hero.Invalidate();
		SaveSettings();
		wake.Set();
	}

	void ToggleOff() => ApplyColor(rgb == 0 ? lastRgb : 0, true);

	void ApplyColor(int c, bool save)
	{
		rgb = c;
		if (c != 0) lastRgb = c;
		swatch.Color = LightColor;
		hero.Light = LightColor;
		hero.Invalidate();
		UpdateOffButton();
		if (!guard) SetGuard(true);   // SetGuard saves too
		else if (save) SaveSettings();
		wake.Set();
	}

	void UpdateOffButton()
	{
		bool off = rgb == 0;
		offBtn.Text = off ? "Turn light on" : "Turn light off";
		offBtn.Primary = off;
		offBtn.Invalidate();
		if (trayOff != null) trayOff.Text = offBtn.Text;
	}

	void OpenSettingsFile()
	{
		SaveSettings();
		try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{SettingsFile}\"")); }
		catch { }
	}

	// Reload when someone edits settings.txt by hand while the app runs.
	void WatchSettings()
	{
		try
		{
			watcher = new FileSystemWatcher(Path.GetDirectoryName(SettingsFile), Path.GetFileName(SettingsFile))
			{ NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, SynchronizingObject = this, EnableRaisingEvents = true };
			watcher.Changed += (_, _) =>
			{
				if (Environment.TickCount64 - lastSave < 1000) return;   // our own write
				LoadSettings();
				guardT.Set(guard);
				trayGuard.Checked = guard;
				swatch.Color = LightColor;
				hero.Light = LightColor;
				hero.GuardOn = guard;
				hero.Invalidate();
				UpdateOffButton();
				wake.Set();
			};
		}
		catch { }
	}

	Color LightColor => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

	void OpenPicker()
	{
		if (Environment.TickCount64 - pickerClosed < 250) return;   // click on swatch just closed it
		picker = new PickerForm(LightColor);
		var pt = swatch.PointToScreen(new Point(swatch.Width, swatch.Height + 6));
		var area = Screen.FromControl(this).WorkingArea;
		picker.Location = new Point(Math.Clamp(pt.X - picker.Width, area.Left, area.Right - picker.Width),
			Math.Clamp(pt.Y, area.Top, area.Bottom - picker.Height));
		picker.ColorChanged += c => ApplyColor(c.R << 16 | c.G << 8 | c.B, false);
		picker.FormClosed += (_, _) => { pickerClosed = Environment.TickCount64; SaveSettings(); };
		picker.Show(this);
	}

	void ShowMain()
	{
		allowShow = true;
		Show();
		WindowState = FormWindowState.Normal;
		Activate();
	}

	protected override void SetVisibleCore(bool value)
	{
		if (!allowShow)
		{
			value = false;
			if (!IsHandleCreated) CreateHandle();
		}
		base.SetVisibleCore(value);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		int dark = 1;
		DwmSetWindowAttribute(Handle, 20, ref dark, 4);                     // dark title bar
		int cap = Theme.Bg.R | Theme.Bg.G << 8 | Theme.Bg.B << 16;
		DwmSetWindowAttribute(Handle, 35, ref cap, 4);                      // caption colour (Win 11)
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (!exiting && e.CloseReason == CloseReason.UserClosing)
		{
			e.Cancel = true;
			Hide();
			if (!hintShown)
			{
				hintShown = true;
				tray.ShowBalloonTip(2500, "Lumen Lock is still running", "Right-click the tray icon to exit.", ToolTipIcon.None);
			}
			return;
		}
		tray.Visible = false;
		base.OnFormClosing(e);
	}

	// ---- settings ----

	void LoadSettings()
	{
		try
		{
			foreach (var line in File.ReadAllLines(SettingsFile))
			{
				var kv = line.Split('=', 2);
				if (kv.Length != 2) continue;
				string k = kv[0].Trim(), val = kv[1].Trim();
				var hex = System.Globalization.NumberStyles.HexNumber;
				if (k == "guard") guard = val == "1";
				if (k == "color" && int.TryParse(val, hex, null, out int c)) rgb = c & 0xFFFFFF;
				if (k == "lastcolor" && int.TryParse(val, hex, null, out int lc) && lc != 0) lastRgb = lc & 0xFFFFFF;
				if (k == "interval" && int.TryParse(val, out int ms)) interval = Math.Clamp(ms, 100, 60000);
			}
		}
		catch { }
	}

	void SaveSettings()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
			lastSave = Environment.TickCount64;
			File.WriteAllLines(SettingsFile, new[]
			{
				"# Lumen Lock settings. You can edit this while the app runs; it reloads on save.",
				"# interval  = milliseconds between resends of the colour (100 - 60000, default 1000)",
				"# color     = light bar colour as RRGGBB hex, 000000 = off",
				"# lastcolor = colour that \"Turn light on\" restores",
				"# guard     = 1 to keep re-applying the colour, 0 to leave the light alone",
				$"interval={interval}",
				$"color={rgb:X6}",
				$"lastcolor={lastRgb:X6}",
				$"guard={(guard ? 1 : 0)}",
			});
		}
		catch { }
	}

	// The app used to be called LED Guard: carry over its settings and startup entry.
	static void MigrateFromLedGuard()
	{
		try
		{
			string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedGuard", "settings.txt");
			if (!File.Exists(SettingsFile) && File.Exists(old))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
				File.Copy(old, SettingsFile);
			}
			using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
			if (k?.GetValue("LedGuard") is string)
			{
				k.DeleteValue("LedGuard", false);
				k.SetValue("LumenLock", $"\"{Environment.ProcessPath}\" --tray");
			}
		}
		catch { }
	}

	static bool IsStartup()
	{
		using var k = Registry.CurrentUser.OpenSubKey(RunKey);
		return k?.GetValue("LumenLock") is string;
	}

	static void SetStartup(bool on)
	{
		using var k = Registry.CurrentUser.CreateSubKey(RunKey);
		if (on) k.SetValue("LumenLock", $"\"{Environment.ProcessPath}\" --tray");
		else k.DeleteValue("LumenLock", false);
	}

	static Icon MakeIcon()
	{
		using var bmp = new Bitmap(64, 64);
		using (var g = Graphics.FromImage(bmp))
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using var bg = Theme.Round(new RectangleF(2, 2, 60, 60), 14);
			using var b = new SolidBrush(Theme.Accent);
			g.FillPath(b, bg);
			using var bar = Theme.Round(new RectangleF(13, 27, 38, 10), 5);
			using var w = new SolidBrush(Color.FromArgb(17, 19, 24));
			g.FillPath(w, bar);
		}
		return Icon.FromHandle(bmp.GetHicon());
	}
}
