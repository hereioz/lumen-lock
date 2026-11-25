using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LumenLock;

enum Conn { None, Usb, Bluetooth }

// Finds DualShock 4 controllers and writes the light bar colour over USB or Bluetooth.
static class Ds4
{
	[StructLayout(LayoutKind.Sequential)]
	struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

	[StructLayout(LayoutKind.Sequential)]
	struct HIDD_ATTRIBUTES { public int Size; public ushort Vid; public ushort Pid; public ushort Ver; }

	[StructLayout(LayoutKind.Sequential)]
	struct HIDP_CAPS
	{
		public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] R;
		public ushort a, b, c, d, e, f, g, h, i, j;
	}

	[DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
	[DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
	[DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
	[DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
	[DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS c);
	[DllImport("hid.dll")] static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] b, int n);
	[DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr e, IntPtr w, int f);
	[DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA o);
	[DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr o, int sz, out int req, IntPtr di);
	[DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] b, int n, out int w, IntPtr o);

	const ushort SonyVid = 0x054C;
	static readonly ushort[] Pids = { 0x05C4, 0x09CC, 0x0BA0 };   // DS4 v1, DS4 v2, wireless adapter
	const int BtLen = 78;

	// Short description of the last write attempt, for diagnostics.
	public static string LastInfo = "";

	// write=false only detects. Returns the best connection found and whether a write succeeded.
	public static (Conn conn, bool wrote) Apply(bool write, byte r, byte g, byte b)
	{
		Conn best = Conn.None;
		bool wrote = false;
		HidD_GetHidGuid(out Guid hid);
		IntPtr set = SetupDiGetClassDevsW(ref hid, IntPtr.Zero, IntPtr.Zero, 0x12);
		try
		{
			for (int i = 0; ; i++)
			{
				var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
				if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d)) break;
				string path = DevicePath(set, ref d);
				if (path == null || !IsDs4(path)) continue;

				using var h = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
				if (h.IsInvalid) continue;
				if (!HidD_GetPreparsedData(h, out IntPtr pp)) continue;
				var caps = new HIDP_CAPS();
				HidP_GetCaps(pp, ref caps);
				HidD_FreePreparsedData(pp);

				Conn conn = caps.OutLen == 32 ? Conn.Usb : caps.OutLen >= BtLen ? Conn.Bluetooth : Conn.None;
				if (conn == Conn.None) continue;
				if (best != Conn.Usb) best = conn;
				if (!write) continue;

				bool ok = conn == Conn.Usb ? WriteUsb(h, r, g, b) : WriteBt(h, caps.OutLen, r, g, b);
				wrote |= ok;
			}
		}
		finally { SetupDiDestroyDeviceInfoList(set); }
		return (best, wrote);
	}

	static string DevicePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d)
	{
		SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out int req, IntPtr.Zero);
		if (req <= 0) return null;
		IntPtr buf = Marshal.AllocHGlobal(req);
		try
		{
			Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
			return SetupDiGetDeviceInterfaceDetailW(set, ref d, buf, req, out _, IntPtr.Zero)
				? Marshal.PtrToStringUni(buf + 4) : null;
		}
		finally { Marshal.FreeHGlobal(buf); }
	}

	// Checks vendor/product through HID attributes, so USB and Bluetooth paths both match.
	static bool IsDs4(string path)
	{
		using var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
		if (h.IsInvalid) return false;
		var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
		return HidD_GetAttributes(h, ref a) && a.Vid == SonyVid && Array.IndexOf(Pids, a.Pid) >= 0;
	}

	static bool WriteUsb(SafeFileHandle h, byte r, byte g, byte b)
	{
		var rep = new byte[32];
		rep[0] = 0x05;
		rep[1] = 0x06;                  // light bar + flash fields only (rumble left alone)
		rep[6] = r; rep[7] = g; rep[8] = b;
		return WriteFile(h, rep, rep.Length, out _, IntPtr.Zero);
	}

	// Bluetooth output report 0x11: same fields shifted by 2, plus a CRC-32 at the end.
	static bool WriteBt(SafeFileHandle h, int outLen, byte r, byte g, byte b)
	{
		var rep = new byte[BtLen];
		rep[0] = 0x11;
		rep[1] = 0xC0;                  // HID report + CRC present
		rep[3] = 0x06;                  // light bar + flash fields only
		rep[8] = r; rep[9] = g; rep[10] = b;
		uint crc = Crc32(new byte[] { 0xA2 }, 0xFFFFFFFF);
		crc = Crc32(rep.AsSpan(0, BtLen - 4), crc) ^ 0xFFFFFFFF;
		rep[74] = (byte)crc; rep[75] = (byte)(crc >> 8); rep[76] = (byte)(crc >> 16); rep[77] = (byte)(crc >> 24);

		// Exact-size write first; if Windows insists on the device's declared output
		// length, pad to it; last resort is the control-channel SetOutputReport.
		if (WriteFile(h, rep, rep.Length, out _, IntPtr.Zero)) { LastInfo = $"bt write78 (outLen {outLen})"; return true; }
		int e1 = Marshal.GetLastWin32Error(), e2 = 0;
		if (outLen > BtLen)
		{
			var padded = new byte[outLen];
			Array.Copy(rep, padded, BtLen);
			if (WriteFile(h, padded, padded.Length, out _, IntPtr.Zero)) { LastInfo = $"bt write{outLen} (78 failed {e1})"; return true; }
			e2 = Marshal.GetLastWin32Error();
		}
		bool ok = HidD_SetOutputReport(h, rep, rep.Length);
		LastInfo = $"bt setOutputReport ok={ok} (write78 err {e1}, padded err {e2}, outLen {outLen})";
		return ok;
	}

	static readonly uint[] CrcTable = BuildCrcTable();

	static uint[] BuildCrcTable()
	{
		var t = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
			t[n] = c;
		}
		return t;
	}

	// Raw CRC-32 step (no final xor) so it can be chained.
	static uint Crc32(ReadOnlySpan<byte> data, uint crc)
	{
		foreach (byte x in data) crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
		return crc;
	}
}
