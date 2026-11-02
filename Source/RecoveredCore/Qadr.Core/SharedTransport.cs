using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace Qadr.Core;

[SupportedOSPlatform("windows")]
public sealed class SharedTransport : IDisposable
{
	private const int Slots = 32;

	private const int SlotSize = 8192;

	private const int StatusSize = 1500000;

	private readonly MemoryMappedFile requests;

	private readonly MemoryMappedFile status;

	private readonly MemoryMappedViewAccessor requestView;

	private readonly MemoryMappedViewAccessor statusView;

	private readonly Mutex requestLock;

	private long sequence;

	public SharedTransport(string session, bool create)
	{
		requests = (create ? MemoryMappedFile.CreateNew(session + ".commands", 262208L) : MemoryMappedFile.OpenExisting(session + ".commands"));
		status = (create ? MemoryMappedFile.CreateNew(session + ".status", 1500064L) : MemoryMappedFile.OpenExisting(session + ".status"));
		requestView = requests.CreateViewAccessor();
		statusView = status.CreateViewAccessor();
		requestLock = new Mutex(initiallyOwned: false, session + ".commands-lock");
	}

	public bool Send(Command command)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(Protocol.Encode(command));
		if (bytes.Length > 8188)
		{
			return false;
		}
		bool flag = false;
		try
		{
			try
			{
				flag = requestLock.WaitOne(5);
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			if (!flag)
			{
				return false;
			}
			long num = requestView.ReadInt64(0L);
			long num2 = requestView.ReadInt64(8L);
			if (num - num2 >= 32)
			{
				return false;
			}
			long num3 = 64 + num % 32 * 8192;
			requestView.WriteArray(num3 + 4, bytes, 0, bytes.Length);
			requestView.Write(num3, bytes.Length);
			requestView.Write(0L, num + 1);
			return true;
		}
		finally
		{
			if (flag)
			{
				requestLock.ReleaseMutex();
			}
		}
	}

	public Command? Receive()
	{
		bool flag = false;
		try
		{
			try
			{
				flag = requestLock.WaitOne(5);
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			if (!flag)
			{
				return null;
			}
			long num = requestView.ReadInt64(0L);
			long num2 = requestView.ReadInt64(8L);
			if (num2 >= num)
			{
				return null;
			}
			long num3 = 64 + num2 % 32 * 8192;
			int num4 = requestView.ReadInt32(num3);
			if (num4 <= 0 || num4 > 8188)
			{
				throw new InvalidDataException("Invalid command buffer");
			}
			byte[] array = new byte[num4];
			requestView.ReadArray(num3 + 4, array, 0, num4);
			requestView.Write(8L, num2 + 1);
			Command command = Protocol.Decode<Command>(Encoding.UTF8.GetString(array));
			if (command.Version != 1)
			{
				throw new InvalidDataException("Protocol mismatch");
			}
			return command;
		}
		finally
		{
			if (flag)
			{
				requestLock.ReleaseMutex();
			}
		}
	}

	public void Publish(EngineStatus message)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(Protocol.Encode(message));
		if (bytes.Length > 1500000)
		{
			throw new InvalidDataException("Status buffer exceeded");
		}
		statusView.Write(0L, ++sequence);
		Thread.MemoryBarrier();
		statusView.Write(8L, bytes.Length);
		statusView.WriteArray(64L, bytes, 0, bytes.Length);
		Thread.MemoryBarrier();
		statusView.Write(0L, ++sequence);
	}

	public EngineStatus? Read(ref long previous)
	{
		long num = statusView.ReadInt64(0L);
		if (num == previous || num <= 0 || (num & 1) != 0L)
		{
			return null;
		}
		int num2 = statusView.ReadInt32(8L);
		if (num2 <= 0 || num2 > 1500000)
		{
			return null;
		}
		byte[] array = new byte[num2];
		statusView.ReadArray(64L, array, 0, num2);
		Thread.MemoryBarrier();
		if (statusView.ReadInt64(0L) != num)
		{
			return null;
		}
		previous = num;
		EngineStatus engineStatus = Protocol.Decode<EngineStatus>(Encoding.UTF8.GetString(array));
		if (engineStatus.Version != 1)
		{
			throw new InvalidDataException("Protocol mismatch");
		}
		return engineStatus;
	}

	public void Dispose()
	{
		requestView.Dispose();
		statusView.Dispose();
		requests.Dispose();
		status.Dispose();
		requestLock.Dispose();
	}
}
