using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Qadr.Core;

[SupportedOSPlatform("windows")]
public static class NativeInput
{
	public struct Point
	{
		public int X;

		public int Y;
	}

	private struct Mouse
	{
		public int X;

		public int Y;

		public uint Data;

		public uint Flags;

		public uint Time;

		public nuint Extra;
	}

	private struct Keyboard
	{
		public ushort Vk;

		public ushort Scan;

		public uint Flags;

		public uint Time;

		public nuint Extra;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct Data
	{
		[FieldOffset(0)]
		public Mouse Mouse;

		[FieldOffset(0)]
		public Keyboard Keyboard;
	}

	private struct Input
	{
		public uint Type;

		public Data Data;
	}

	private struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private struct MonitorInfo
	{
		public int Size;

		public Rect Monitor;

		public Rect Work;

		public uint Flags;
	}

	public static Point Cursor
	{
		get
		{
			GetCursorPos(out var point);
			return point;
		}
	}

	public static (int X, int Y, int Width, int Height) Desktop => (X: GetSystemMetrics(76), Y: GetSystemMetrics(77), Width: GetSystemMetrics(78), Height: GetSystemMetrics(79));

	[DllImport("user32.dll")]
	private static extern uint SendInput(uint count, Input[] inputs, int size);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out Point point);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int n);

	[DllImport("user32.dll")]
	private static extern bool SetProcessDpiAwarenessContext(nint context);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint window, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

	public static (int X, int Y, int Width, int Height) MonitorBounds(long window)
	{
		MonitorInfo info = new MonitorInfo
		{
			Size = Marshal.SizeOf<MonitorInfo>()
		};
		if (GetMonitorInfoW(MonitorFromWindow((nint)window, 2u), ref info))
		{
			return (X: info.Monitor.Left, Y: info.Monitor.Top, Width: info.Monitor.Right - info.Monitor.Left, Height: info.Monitor.Bottom - info.Monitor.Top);
		}
		return (X: 0, Y: 0, Width: GetSystemMetrics(0), Height: GetSystemMetrics(1));
	}

	[DllImport("user32.dll")]
	public static extern uint GetDoubleClickTime();

	[DllImport("user32.dll")]
	private static extern nint WindowFromPoint(Point point);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint window, uint flags);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint window, out uint process);

	public static uint WindowOwner(long window)
	{
		GetWindowThreadProcessId((nint)window, out var process);
		return process;
	}

	public static long WindowAt(Point point)
	{
		return ((IntPtr)GetAncestor(WindowFromPoint(point), 2u)).ToInt64();
	}

	public static void EnablePhysicalPixels()
	{
		SetProcessDpiAwarenessContext(-4);
	}

	public static bool Move(SafetyGate gate, double x, double y)
	{
		return Inject(gate, MouseEvent(49153u, x, y));
	}

	public static bool Button(SafetyGate gate, string button, bool down)
	{
		var (mask, flags, data) = button switch
		{
			"left" => (1, down ? 2u : 4u, 0u), 
			"right" => (2, down ? 8u : 16u, 0u), 
			"middle" => (4, down ? 32u : 64u, 0u), 
			"back" => (8, down ? 128u : 256u, 1u), 
			"forward" => (16, down ? 128u : 256u, 2u), 
			_ => (0, 0u, 0u), 
		};
		if (mask != 0)
		{
			return Inject(gate, MouseEvent(flags, 0.0, 0.0, data), () =>
			{
				gate.MouseButtons = (down ? (gate.MouseButtons | mask) : (gate.MouseButtons & ~mask));
			});
		}
		return false;
	}

	public static bool Scroll(SafetyGate gate, int amount)
	{
		return Inject(gate, MouseEvent(2048u, 0.0, 0.0, (uint)(amount * 120)));
	}

	public static bool ScrollHorizontal(SafetyGate gate, int amount)
	{
		return Inject(gate, MouseEvent(4096u, 0.0, 0.0, (uint)(amount * 120)));
	}

	public static bool Key(SafetyGate gate, ushort vk, bool down)
	{
		return Inject(gate, new Input
		{
			Type = 1u,
			Data = new Data
			{
				Keyboard = new Keyboard
				{
					Vk = vk,
					Flags = ((!down) ? 2u : 0u)
				}
			}
		}, () =>
		{
			gate.SetKey(vk, down);
		});
	}

	private static Input MouseEvent(uint flags, double x = 0.0, double y = 0.0, uint data = 0u)
	{
		(int, int, int, int) desktop = Desktop;
		int num = Math.Clamp((int)Math.Round(x), desktop.Item1, desktop.Item1 + desktop.Item3 - 1);
		int num2 = Math.Clamp((int)Math.Round(y), desktop.Item2, desktop.Item2 + desktop.Item4 - 1);
		return new Input
		{
			Type = 0u,
			Data = new Data
			{
				Mouse = new Mouse
				{
					X = (int)Math.Round((double)(num - desktop.Item1) * 65535.0 / (double)Math.Max(1, desktop.Item3 - 1)),
					Y = (int)Math.Round((double)(num2 - desktop.Item2) * 65535.0 / (double)Math.Max(1, desktop.Item4 - 1)),
					Flags = flags,
					Data = data
				}
			}
		};
	}

	private static bool Inject(SafetyGate gate, Input input, Action? own = null)
	{
		bool flag = false;
		try
		{
			try
			{
				flag = gate.InjectionMutex.WaitOne(10);
			}
			catch (AbandonedMutexException)
			{
				flag = true;
				gate.Stop();
			}
			if (!flag || gate.Stopped || gate.Hotkeys == 0 || Environment.TickCount64 - gate.Heartbeat > 250)
			{
				return false;
			}
			own?.Invoke();
			return SendInput(1u, new Input[1] { input }, Marshal.SizeOf<Input>()) == 1;
		}
		finally
		{
			if (flag)
			{
				gate.InjectionMutex.ReleaseMutex();
			}
		}
	}

	public static bool WithGate(SafetyGate gate, Action action)
	{
		bool flag = false;
		try
		{
			try
			{
				flag = gate.InjectionMutex.WaitOne(10);
			}
			catch (AbandonedMutexException)
			{
				flag = true;
				gate.Stop();
			}
			if (!flag || gate.Stopped || gate.Hotkeys == 0)
			{
				return false;
			}
			action();
			return true;
		}
		finally
		{
			if (flag)
			{
				gate.InjectionMutex.ReleaseMutex();
			}
		}
	}

	public static void ReleaseOwned(SafetyGate gate)
	{
		int mouseButtons = gate.MouseButtons;
		gate.MouseButtons = 0;
		if ((mouseButtons & 1) != 0)
		{
			SendInput(1u, new Input[1] { MouseEvent(4u) }, Marshal.SizeOf<Input>());
		}
		if ((mouseButtons & 2) != 0)
		{
			SendInput(1u, new Input[1] { MouseEvent(16u) }, Marshal.SizeOf<Input>());
		}
		if ((mouseButtons & 4) != 0)
		{
			SendInput(1u, new Input[1] { MouseEvent(64u) }, Marshal.SizeOf<Input>());
		}
		if ((mouseButtons & 8) != 0)
		{
			SendInput(1u, new Input[1] { MouseEvent(256u, 0.0, 0.0, 1u) }, Marshal.SizeOf<Input>());
		}
		if ((mouseButtons & 0x10) != 0)
		{
			SendInput(1u, new Input[1] { MouseEvent(256u, 0.0, 0.0, 2u) }, Marshal.SizeOf<Input>());
		}
		for (ushort num = 0; num < 256; num++)
		{
			if (gate.KeyOwned(num))
			{
				gate.SetKey(num, down: false);
				SendInput(1u, new Input[1]
				{
					new Input
					{
						Type = 1u,
						Data = new Data
						{
							Keyboard = new Keyboard
							{
								Vk = num,
								Flags = 2u
							}
						}
					}
				}, Marshal.SizeOf<Input>());
			}
		}
	}
}
