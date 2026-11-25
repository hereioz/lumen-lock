using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumenLock;

static class Hsv
{
	public static Color ToRgb(float h, float s, float v)
	{
		h = (h % 360 + 360) % 360;
		float c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
		(float r, float g, float b) = h switch
		{
			< 60 => (c, x, 0f), < 120 => (x, c, 0f), < 180 => (0f, c, x),
			< 240 => (0f, x, c), < 300 => (x, 0f, c), _ => (c, 0f, x),
		};
		return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
	}

	public static (float h, float s, float v) FromRgb(Color c)
	{
		float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
		float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
		float h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
		return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
	}

	public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
	public static bool IsOff(Color c) => c.R == 0 && c.G == 0 && c.B == 0;
}

// Swatch button showing the current colour; black shows as "off" with a slash.
class Swatch : Smooth
{
	Color color;
	public Color Color { get => color; set { color = value; Invalidate(); } }
	public Swatch(int w, int h) { Size = new Size(w, h); Cursor = Cursors.Hand; }

	public static void Draw(Graphics g, RectangleF r, Color c, float rad, bool selected)
	{
		using var p = Theme.Round(r, rad);
		using var b = new SolidBrush(Hsv.IsOff(c) ? Theme.Bg : c);
		g.FillPath(b, p);
		using var pen = new Pen(selected ? Color.White : Theme.Border, selected ? 2f : 1f);
		g.DrawPath(pen, p);
		if (Hsv.IsOff(c))
		{
			using var sl = new Pen(Color.FromArgb(220, 80, 80), 2f);
			g.DrawLine(sl, r.Left + r.Width * 0.25f, r.Bottom - r.Height * 0.25f, r.Right - r.Width * 0.25f, r.Top + r.Height * 0.25f);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Prep(e.Graphics);
		e.Graphics.Clear(BackColor);
		Draw(e.Graphics, new RectangleF(1, 1, Width - 3, Height - 3), color, 8 * DeviceDpi / 96f, false);
	}
}

class PickerForm : Form
{
	static readonly Color[] Presets =
	{
		Color.FromArgb(255, 0, 0), Color.FromArgb(255, 90, 0), Color.FromArgb(255, 200, 0),
		Color.FromArgb(120, 255, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 200), Color.FromArgb(0, 0, 255),
		Color.FromArgb(140, 0, 255), Color.FromArgb(255, 0, 140), Color.FromArgb(255, 255, 255),
	};

	float h, s, v;
	Color current;
	readonly SvBox sv;
	readonly HueBar hue;
	readonly PresetRow presets;
	readonly TextBox hex;
	bool syncing;
	public event Action<Color> ColorChanged;

	[DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hw, int attr, ref int val, int size);

	public PickerForm(Color start)
	{
		SuspendLayout();
		AutoScaleDimensions = new SizeF(96, 96);
		AutoScaleMode = AutoScaleMode.Dpi;
		FormBorderStyle = FormBorderStyle.None;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		BackColor = Theme.Card;
		ClientSize = new Size(260, 276);
		KeyPreview = true;

		sv = new SvBox(this) { Location = new Point(16, 16), Size = new Size(228, 140) };
		hue = new HueBar(this) { Location = new Point(16, 168), Size = new Size(228, 16) };
		presets = new PresetRow(Presets) { Location = new Point(14, 196), Size = new Size(232, 26), BackColor = Theme.Card };
		presets.Picked += c => SetColor(c, true);

		Controls.Add(new Label { Text = "#", ForeColor = Theme.Sub, AutoSize = true, BackColor = Theme.Card,
			Font = new Font(Theme.FontName, 10f, FontStyle.Bold), Location = new Point(16, 238) });
		hex = new TextBox { Location = new Point(32, 236), Width = 90, BorderStyle = BorderStyle.FixedSingle,
			BackColor = Theme.Bg, ForeColor = Theme.Text, Font = new Font("Consolas", 10.5f), MaxLength = 6,
			CharacterCasing = CharacterCasing.Upper };
		hex.TextChanged += (_, _) =>
		{
			if (syncing || hex.Text.Length != 6) return;
			if (int.TryParse(hex.Text, NumberStyles.HexNumber, null, out int x))
				SetColor(Color.FromArgb((x >> 16) & 255, (x >> 8) & 255, x & 255), false);
		};
		var done = new Label { Text = "Done", AutoSize = false, Size = new Size(70, 26), Location = new Point(174, 235),
			TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.Accent, ForeColor = Color.White, Cursor = Cursors.Hand,
			Font = new Font(Theme.FontName, 9f, FontStyle.Bold) };
		done.Click += (_, _) => Close();

		Controls.AddRange(new Control[] { sv, hue, presets, hex, done });
		ResumeLayout(false);
		SetColor(start, true);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);   // rounded corners (Win 11)
		int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);
	}

	protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
	protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Escape or Keys.Enter) Close(); base.OnKeyDown(e); }

	public float H => h; public float S => s; public float V => v;

	public void SetHsv(float nh, float ns, float nv)
	{
		h = nh; s = ns; v = nv;
		Emit(Hsv.ToRgb(h, s, v), true);
	}

	void SetColor(Color c, bool updateHex)
	{
		var (nh, ns, nv) = Hsv.FromRgb(c);
		if (ns > 0 && nv > 0) h = nh;   // keep hue when picking greys/black
		s = ns; v = nv;
		Emit(c, updateHex);
	}

	void Emit(Color c, bool updateHex)
	{
		current = c;
		if (updateHex) { syncing = true; hex.Text = Hsv.Hex(c); syncing = false; }
		presets.Current = c;
		sv.Invalidate(); hue.Invalidate();
		ColorChanged?.Invoke(c);
	}

	class SvBox : Smooth
	{
		readonly PickerForm f;
		public SvBox(PickerForm f) { this.f = f; Cursor = Cursors.Cross; }
		protected override void OnMouseDown(MouseEventArgs e) { Pick(e); Capture = true; }
		protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Pick(e); }
		void Pick(MouseEventArgs e) => f.SetHsv(f.H, Math.Clamp(e.X / (float)(Width - 1), 0, 1), 1 - Math.Clamp(e.Y / (float)(Height - 1), 0, 1));

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Theme.Card);
			Prep(g);
			var r = new Rectangle(0, 0, Width, Height);
			using var clip = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8 * DeviceDpi / 96f);
			g.SetClip(clip);
			var gr = Rectangle.Inflate(r, 1, 1);   // avoids GDI+ wrapping the gradient's first pixel
			using (var b = new LinearGradientBrush(gr, Color.White, Hsv.ToRgb(f.H, 1, 1), 0f) { WrapMode = WrapMode.TileFlipXY }) g.FillRectangle(b, r);
			using (var b = new LinearGradientBrush(gr, Color.Transparent, Color.Black, 90f) { WrapMode = WrapMode.TileFlipXY }) g.FillRectangle(b, r);
			g.ResetClip();
			float x = f.S * (Width - 1), y = (1 - f.V) * (Height - 1), rad = 7 * DeviceDpi / 96f;
			using var pen = new Pen(f.V > 0.5f && f.S < 0.5f ? Color.Black : Color.White, 2);
			g.DrawEllipse(pen, x - rad, y - rad, rad * 2, rad * 2);
		}
	}

	class HueBar : Smooth
	{
		readonly PickerForm f;
		public HueBar(PickerForm f) { this.f = f; Cursor = Cursors.Hand; }
		protected override void OnMouseDown(MouseEventArgs e) { Pick(e); Capture = true; }
		protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Pick(e); }
		void Pick(MouseEventArgs e)
		{
			float nh = Math.Clamp(e.X / (float)(Width - 1), 0, 0.999f) * 360;
			// Dragging hue on black/grey should give a visible colour.
			f.SetHsv(nh, f.S < 0.05f ? 1 : f.S, f.V < 0.05f ? 1 : f.V);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Theme.Card);
			Prep(g);
			var r = new RectangleF(0, 2, Width - 1, Height - 5);
			using var path = Theme.Round(r, r.Height / 2);
			using var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Color.Red, Color.Red, 0f);
			var blend = new ColorBlend(7);
			for (int i = 0; i < 7; i++) { blend.Colors[i] = Hsv.ToRgb(i * 60, 1, 1); blend.Positions[i] = i / 6f; }
			b.InterpolationColors = blend;
			g.FillPath(b, path);
			float x = f.H / 360f * (Width - 1), rad = (Height - 2) / 2f;
			x = Math.Clamp(x, rad, Width - 1 - rad);
			using var fill = new SolidBrush(Hsv.ToRgb(f.H, 1, 1));
			g.FillEllipse(fill, x - rad, 1, rad * 2, rad * 2);
			using var pen = new Pen(Color.White, 2);
			g.DrawEllipse(pen, x - rad, 1, rad * 2, rad * 2);
		}
	}

	class PresetRow : Smooth
	{
		readonly Color[] colors;
		Color cur;
		public event Action<Color> Picked;
		public Color Current { set { cur = value; Invalidate(); } }
		public PresetRow(Color[] colors) { this.colors = colors; Cursor = Cursors.Hand; }

		protected override void OnMouseDown(MouseEventArgs e)
		{
			int i = Math.Clamp(e.X * colors.Length / Math.Max(1, Width), 0, colors.Length - 1);
			Picked?.Invoke(colors[i]);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(BackColor);
			Prep(g);
			float w = Width / (float)colors.Length, d = Math.Min(w, Height) - 4;
			for (int i = 0; i < colors.Length; i++)
			{
				var r = new RectangleF(i * w + (w - d) / 2, (Height - d) / 2, d, d);
				bool sel = colors[i].ToArgb() == cur.ToArgb();
				Swatch.Draw(g, r, colors[i], d / 2, sel);
			}
		}
	}
}
