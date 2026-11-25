using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LumenLock;

enum Conn { None, Usb, Bluetooth }

// Finds DualShock 4 controllers and writes the light bar colour over USB.
static class Ds4
{
	[StructLayout(LayoutKind.Sequential)]
	struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

	[StructLayout(LayoutKind.Sequential)]
	struct HIDP_CAPS
	{
		public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] R;
		public ushort a, b, c, d, e, f, g, h, i, j;
	}

	[DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
	[DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
	[DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
	[DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS c);
	[DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr e, IntPtr w, int f);
	[DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA o);
	[DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr o, int sz, out int req, IntPtr di);
	[DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
	[DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] b, int n, out int w, IntPtr o);

	static readonly string[] Pids = { "pid_05c4", "pid_09cc", "pid_0ba0" };

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
				SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out int req, IntPtr.Zero);
				IntPtr buf = Marshal.AllocHGlobal(req);
				string path;
				try
				{
					Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
					if (!SetupDiGetDeviceInterfaceDetailW(set, ref d, buf, req, out req, IntPtr.Zero)) continue;
					path = Marshal.PtrToStringUni(buf + 4);
				}
				finally { Marshal.FreeHGlobal(buf); }

				string p = path.ToLowerInvariant();
				if (!p.Contains("vid_054c") || Array.TrueForAll(Pids, x => !p.Contains(x))) continue;

				using var h = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
				if (h.IsInvalid) continue;
				if (!HidD_GetPreparsedData(h, out IntPtr pp)) continue;
				var caps = new HIDP_CAPS();
				HidP_GetCaps(pp, ref caps);
				HidD_FreePreparsedData(pp);

				if (caps.OutLen == 32)
				{
					best = Conn.Usb;
					if (!write) continue;
					var rep = new byte[32];
					rep[0] = 0x05;
					rep[1] = 0x06;           // light bar + flash fields only (rumble left alone)
					rep[6] = r; rep[7] = g; rep[8] = b;
					rep[9] = 0; rep[10] = 0; // no flashing
					if (WriteFile(h, rep, rep.Length, out _, IntPtr.Zero)) wrote = true;
				}
				else if (caps.OutLen > 32 && best == Conn.None)
				{
					best = Conn.Bluetooth;
				}
			}
		}
		finally { SetupDiDestroyDeviceInfoList(set); }
		return (best, wrote);
	}
}
