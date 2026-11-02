using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Threading;

namespace Qadr.Core;

[SupportedOSPlatform("windows")]
public sealed class SafetyGate : IDisposable
{
	private readonly MemoryMappedFile memory;

	private readonly MemoryMappedViewAccessor view;

	private unsafe byte* pointer;

	public Mutex InjectionMutex { get; }

	public bool Stopped
	{
		get
		{
			return ReadInt(0) != 0;
		}
		set
		{
			WriteInt(0, value ? 1 : 0);
		}
	}

	public int Hotkeys
	{
		get
		{
			return ReadInt(4);
		}
		set
		{
			WriteInt(4, value);
		}
	}

	public long Heartbeat
	{
		get
		{
			return ReadLong(8);
		}
		set
		{
			WriteLong(8, value);
		}
	}

	public long Generation
	{
		get
		{
			return ReadLong(16);
		}
		private set
		{
			WriteLong(16, value);
		}
	}

	public long Acknowledged
	{
		get
		{
			return ReadLong(24);
		}
		set
		{
			WriteLong(24, value);
		}
	}

	public long ShellHeartbeat
	{
		get
		{
			return ReadLong(32);
		}
		set
		{
			WriteLong(32, value);
		}
	}

	public int ResumeRequest
	{
		get
		{
			return ReadInt(40);
		}
		set
		{
			if (value != 0)
			{
				RequestResume(UserStops);
			}
			else
			{
				WriteInt(40, 0);
			}
		}
	}

	public long ResumeGeneration => ReadLong(96);

	public int ResumeUserStops => ReadInt(104);

	public int UserStops => ReadInt(60);

	public int MouseButtons
	{
		get
		{
			return ReadInt(44);
		}
		set
		{
			WriteInt(44, value);
		}
	}

	public int PauseRequest
	{
		get
		{
			return ReadInt(48);
		}
		set
		{
			WriteInt(48, value);
		}
	}

	public int Closing
	{
		get
		{
			return ReadInt(52);
		}
		set
		{
			WriteInt(52, value);
		}
	}

	public int SharedRpc
	{
		get
		{
			return ReadInt(56);
		}
		set
		{
			WriteInt(56, value);
		}
	}

	public unsafe SafetyGate(string session, bool create)
	{
		memory = (create ? MemoryMappedFile.CreateNew(session + ".gate", 512L) : MemoryMappedFile.OpenExisting(session + ".gate"));
		view = memory.CreateViewAccessor();
		view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
		InjectionMutex = new Mutex(initiallyOwned: false, session + ".injection");
		if (create)
		{
			Stopped = true;
			Hotkeys = 0;
			Heartbeat = 0L;
			ShellHeartbeat = 0L;
			Generation = 1L;
			Acknowledged = 0L;
		}
	}

	private unsafe int ReadInt(int offset)
	{
		return Volatile.Read(in *(int*)(pointer + offset));
	}

	private unsafe void WriteInt(int offset, int n)
	{
		Volatile.Write(ref *(int*)(pointer + offset), n);
	}

	private unsafe long ReadLong(int offset)
	{
		return Interlocked.Read(in *(long*)(pointer + offset));
	}

	private unsafe void WriteLong(int offset, long n)
	{
		Interlocked.Exchange(ref *(long*)(pointer + offset), n);
	}

	public bool RequestResume(int expectedUserStops)
	{
		if (UserStops != expectedUserStops)
		{
			return false;
		}
		WriteInt(104, expectedUserStops);
		WriteLong(96, Generation);
		WriteInt(40, 1);
		return true;
	}

	public bool KeyOwned(int vk)
	{
		int num = ReadInt(64 + vk / 32 * 4);
		return (num & (1 << vk % 32)) != 0;
	}

	public void SetKey(int vk, bool down)
	{
		int offset = 64 + vk / 32 * 4;
		int num = ReadInt(offset);
		WriteInt(offset, down ? (num | (1 << vk % 32)) : (num & ~(1 << vk % 32)));
	}

	public unsafe void Stop()
	{
		ResumeRequest = 0;
		Interlocked.Increment(ref *(long*)(pointer + 16));
		Stopped = true;
	}

	public unsafe void UserStop()
	{
		Interlocked.Increment(ref *(int*)(pointer + 60));
		Stop();
	}

	public unsafe void Dispose()
	{
		view.SafeMemoryMappedViewHandle.ReleasePointer();
		pointer = null;
		view.Dispose();
		memory.Dispose();
		InjectionMutex.Dispose();
	}
}
