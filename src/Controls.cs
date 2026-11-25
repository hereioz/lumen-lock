using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LumenLock;

static class Theme
{
	public static readonly Color Bg = Color.FromArgb(17, 19, 24);
	public static readonly Color Card = Color.FromArgb(26, 29, 36);
	public static readonly Color Border = Color.FromArgb(38, 42, 51);
	public static readonly Color Text = Color.FromArgb(232, 234, 237);
	public static readonly Color Sub = Color.FromArgb(139, 145, 156);
	public static readonly Color Accent = Color.FromArgb(61, 123, 255);
	public static readonly Color Track = Color.FromArgb(52, 57, 68);
	public static readonly Color Green = Color.FromArgb(52, 199, 123);
	public static readonly Color Amber = Color.FromArgb(240, 173, 60);
	public const string FontName = "Segoe UI";

	public static GraphicsPath Round(RectangleF r, float rad)
	{
		var p = new GraphicsPath();
		float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
		p.AddArc(r.X, r.Y, d, d, 180, 90);
		p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
		p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
		p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
		p.CloseFigure();
		return p;
	}

	public static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
		(int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

class Smooth : Control
{
	public Smooth()
	{
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
			ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
	}

	protected static void Prep(Graphics g)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
	}
}

// Rounded card that hosts a title, a subtitle and one control on the right.
class Card : Smooth
{
	public Card(string title, string sub, Control right)
	{
		BackColor = Theme.Bg;
		Size = new Size(340, 64);
		var t = new Label { Text = title, ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = true,
			Font = new Font(Theme.FontName, 10.5f, FontStyle.Bold), Location = new Point(16, 12) };
		var s = new Label { Text = sub, ForeColor = Theme.Sub, BackColor = Theme.Card, AutoSize = true,
			Font = new Font(Theme.FontName, 8.5f), Location = new Point(16, 35) };
		right.BackColor = Theme.Card;
		right.Location = new Point(340 - 16 - right.Width, (64 - right.Height) / 2);
		right.Anchor = AnchorStyles.Right;
		Controls.Add(t);
		Controls.Add(s);
		Controls.Add(right);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Prep(e.Graphics);
		var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
		using var path = Theme.Round(r, 12 * DeviceDpi / 96f);
		using var b = new SolidBrush(Theme.Card);
		using var pen = new Pen(Theme.Border);
		e.Graphics.FillPath(b, path);
		e.Graphics.DrawPath(pen, path);
	}
}

class Toggle : Smooth
{
	bool on;
	float pos;
	readonly Timer anim = new() { Interval = 15 };
	public event EventHandler Changed;

	public Toggle()
	{
		Size = new Size(44, 24);
		Cursor = Cursors.Hand;
		anim.Tick += (_, _) =>
		{
			float t = on ? 1 : 0;
			pos += (t - pos) * 0.3f;
			if (Math.Abs(t - pos) < 0.01f) { pos = t; anim.Stop(); }
			Invalidate();
		};
	}

	public bool On
	{
		get => on;
		set { if (on == value) return; on = value; anim.Start(); Changed?.Invoke(this, EventArgs.Empty); }
	}

	public void Set(bool v) { on = v; pos = v ? 1 : 0; Invalidate(); }

	protected override void OnClick(EventArgs e) { On = !On; base.OnClick(e); }

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		Prep(g);
		g.Clear(BackColor);
		var r = new RectangleF(1, 1, Width - 2, Height - 2);
		using (var path = Theme.Round(r, r.Height / 2))
		using (var b = new SolidBrush(Theme.Lerp(Theme.Track, Theme.Accent, pos)))
			g.FillPath(b, path);
		float pad = r.Height * 0.14f, d = r.Height - pad * 2;
		float x = r.X + pad + (r.Width - pad * 2 - d) * pos;
		g.FillEllipse(Brushes.White, x, r.Y + pad, d, d);
	}
}

class Segmented : Smooth
{
	readonly string[] items;
	int sel;
	int hover = -1;
	public event EventHandler Changed;

	public Segmented(params string[] items)
	{
		this.items = items;
		Size = new Size(156, 30);
		Cursor = Cursors.Hand;
		Font = new Font(Theme.FontName, 9f, FontStyle.Bold);
	}

	public int Selected
	{
		get => sel;
		set { sel = value; Invalidate(); }
	}

	int Hit(int x) => Math.Clamp(x * items.Length / Math.Max(1, Width), 0, items.Length - 1);

	protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.X); if (h != hover) { hover = h; Invalidate(); } }
	protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
	protected override void OnMouseDown(MouseEventArgs e)
	{
		int h = Hit(e.X);
		if (h == sel) return;
		sel = h;
		Invalidate();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		Prep(g);
		g.Clear(BackColor);
		var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
		float rad = r.Height / 2;
		using (var path = Theme.Round(r, rad))
		using (var b = new SolidBrush(Theme.Bg))
			g.FillPath(b, path);

		float w = r.Width / items.Length;
		for (int i = 0; i < items.Length; i++)
		{
			var cell = new RectangleF(r.X + i * w + 3, r.Y + 3, w - 6, r.Height - 6);
			if (i == sel || i == hover)
			{
				using var path = Theme.Round(cell, cell.Height / 2);
				using var b = new SolidBrush(i == sel ? Theme.Accent : Theme.Border);
				g.FillPath(b, path);
			}
			var col = i == sel ? Color.White : Theme.Sub;
			TextRenderer.DrawText(g, items[i], Font, Rectangle.Round(cell), col,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}
	}
}

// Big status card: a drawn light bar plus connection state.
class Hero : Smooth
{
	public bool GuardOn;
	public Color Light = Color.Black;
	public Conn Conn;
	public string Detail = "";

	public Hero()
	{
		BackColor = Theme.Bg;
		Size = new Size(340, 156);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		Prep(g);
		float s = Width / 340f;
		var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
		using (var path = Theme.Round(r, 14 * s))
		using (var b = new SolidBrush(Theme.Card))
		using (var pen = new Pen(Theme.Border))
		{
			g.FillPath(b, path);
			g.DrawPath(pen, path);
		}

		Color shown = GuardOn ? Light : Color.FromArgb(0, 0, 64);
		bool lit = Conn != Conn.None && !Hsv.IsOff(shown);
		// Scale dim colours up so the preview stays visible, like the real LED.
		float peak = Math.Max(shown.R, Math.Max(shown.G, shown.B));
		Color glow = lit ? Color.FromArgb((int)(shown.R * 255 / peak), (int)(shown.G * 255 / peak), (int)(shown.B * 255 / peak)) : Color.Empty;
		var bar = new RectangleF(Width / 2f - 100 * s, 34 * s, 200 * s, 14 * s);
		if (lit)
		{
			for (int i = 6; i >= 1; i--)
			{
				var gr = RectangleF.Inflate(bar, i * 3 * s, i * 3 * s);
				using var p = Theme.Round(gr, gr.Height / 2);
				using var b = new SolidBrush(Color.FromArgb(14, glow));
				g.FillPath(b, p);
			}
		}
		using (var p = Theme.Round(bar, bar.Height / 2))
		using (var b = new SolidBrush(lit ? glow : Color.FromArgb(36, 40, 48)))
		using (var pen = new Pen(lit ? Theme.Lerp(glow, Color.White, 0.4f) : Theme.Border))
		{
			g.FillPath(b, p);
			g.DrawPath(pen, p);
		}

		string head = Conn == Conn.None ? (GuardOn ? "Waiting for controller" : "No controller")
			: !GuardOn ? "Not locked" : Hsv.IsOff(Light) ? "Light off" : "#" + Hsv.Hex(Light);
		using (var f = new Font(Theme.FontName, 17f, FontStyle.Bold))
			TextRenderer.DrawText(g, head, f, new Rectangle(0, (int)(64 * s), Width, (int)(36 * s)), Theme.Text,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

		string line = Conn switch
		{
			Conn.Usb => "DualShock 4 connected over USB",
			Conn.Bluetooth => "Bluetooth isn't supported yet, plug in the cable",
			_ => "Plug in your PS4 controller with a USB cable",
		};
		Color dot = Conn switch { Conn.Usb => Theme.Green, Conn.Bluetooth => Theme.Amber, _ => Theme.Sub };
		using var lf = new Font(Theme.FontName, 9f);
		var sz = TextRenderer.MeasureText(line, lf);
		float total = sz.Width + 14 * s;
		float x0 = (Width - total) / 2f, y = 108 * s;
		using (var b = new SolidBrush(dot))
			g.FillEllipse(b, x0, y + sz.Height / 2f - 4 * s, 8 * s, 8 * s);
		TextRenderer.DrawText(g, line, lf, new Point((int)(x0 + 14 * s), (int)y), Theme.Sub);

		if (Detail.Length > 0)
		{
			using var df = new Font(Theme.FontName, 8f);
			TextRenderer.DrawText(g, Detail, df, new Rectangle(0, (int)(128 * s), Width, (int)(18 * s)),
				Color.FromArgb(100, 106, 116), TextFormatFlags.HorizontalCenter);
		}
	}
}

// Full-width rounded button. Primary = accent fill, otherwise a dark outlined button.
class PillButton : Smooth
{
	public bool Primary;
	bool hover, down;

	public PillButton()
	{
		BackColor = Theme.Bg;
		Cursor = Cursors.Hand;
		Font = new Font(Theme.FontName, 10.5f, FontStyle.Bold);
	}

	protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
	protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
	protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
	protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		Prep(g);
		g.Clear(BackColor);
		var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
		Color fill = Primary ? Theme.Accent : Theme.Card;
		if (hover) fill = Theme.Lerp(fill, Color.White, Primary ? 0.12f : 0.05f);
		if (down) fill = Theme.Lerp(fill, Color.Black, 0.15f);
		using (var p = Theme.Round(r, 12 * DeviceDpi / 96f))
		using (var b = new SolidBrush(fill))
		using (var pen = new Pen(Primary ? fill : Theme.Border))
		{
			g.FillPath(b, p);
			g.DrawPath(pen, p);
		}

		// power glyph + label, centred together
		var sz = TextRenderer.MeasureText(Text, Font);
		float ic = sz.Height * 0.8f, gap = sz.Height * 0.45f;
		float x = (Width - (ic + gap + sz.Width)) / 2f, cy = Height / 2f;
		Color fg = Primary ? Color.White : Theme.Text;
		using (var pen = new Pen(fg, Math.Max(1.6f, ic / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
		{
			g.DrawArc(pen, x, cy - ic / 2, ic, ic, -50, 280);
			g.DrawLine(pen, x + ic / 2, cy - ic / 2 - 1, x + ic / 2, cy);
		}
		TextRenderer.DrawText(g, Text, Font, new Point((int)(x + ic + gap), (int)(cy - sz.Height / 2f)), fg);
	}
}
