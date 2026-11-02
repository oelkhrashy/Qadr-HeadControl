using System;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Qadr.Core;

namespace Qadr.Engine;

internal sealed class CameraCapture : IAsyncDisposable
{
	private readonly MemoryMappedFile memory;

	private readonly MemoryMappedViewAccessor view;

	private readonly object sync = new object();

	private MediaFoundation? capture;

	private long sequence;

	private double lastFrame;

	private double measuredAt;

	private double actualFps;

	private int measuredFrames;

	private string? error;

	private CancellationTokenSource? testFrames;

	public double ActualFps => Volatile.Read(in actualFps);

	public double NominalFps => capture?.NominalFps ?? 0.0;

	public double LastFrame => Volatile.Read(in lastFrame);

	public string? Error => Volatile.Read(in error);

	public CameraCapture(string session)
	{
		memory = MemoryMappedFile.CreateNew(session + ".frames", 1228864L);
		view = memory.CreateViewAccessor();
	}

	public static Task<CameraDevice[]> Discover()
	{
		return Task.Run((Func<CameraDevice[]>)MediaFoundation.Discover);
	}

	public async Task Start(string id, int targetFps = 30)
	{
		await Task.Run(() =>
		{
			capture?.Dispose();
		});
		capture = null;
		Volatile.Write(ref error, null);
		lock (sync)
		{
			view.Write(0L, 0L);
			sequence = 0L;
			measuredAt = 0.0;
			measuredFrames = 0;
			Volatile.Write(ref actualFps, 0.0);
		}
		capture = new MediaFoundation(id, Write, (string message) =>
		{
			Volatile.Write(ref error, message);
		}, targetFps);
		await capture.Ready.WaitAsync(TimeSpan.FromSeconds(12L));
		Volatile.Write(ref lastFrame, Arguments.Now);
	}

	public void StartTestFrames(int targetFps = 30)
	{
		testFrames?.Cancel();
		testFrames = new CancellationTokenSource();
		CancellationToken token = testFrames.Token;
		byte[] blank = new byte[1228800];
		Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				Write(blank, 640, 480);
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(1.0 / (double)((targetFps == 60) ? 60 : 30)), token);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		});
	}

	private void Write(byte[] pixels, int width, int height)
	{
		lock (sync)
		{
			double now = Arguments.Now;
			if (measuredAt == 0.0)
			{
				measuredAt = now;
			}
			measuredFrames++;
			if (now - measuredAt >= 1.0)
			{
				Volatile.Write(ref actualFps, (double)measuredFrames / (now - measuredAt));
				measuredAt = now;
				measuredFrames = 0;
			}
			view.Write(0L, ++sequence);
			Thread.MemoryBarrier();
			view.Write(8L, width);
			view.Write(12L, height);
			view.Write(16L, width * 4);
			view.Write(24L, Arguments.Now);
			view.WriteArray(64L, pixels, 0, pixels.Length);
			Thread.MemoryBarrier();
			view.Write(0L, ++sequence);
			Volatile.Write(ref lastFrame, Arguments.Now);
		}
	}

	public async ValueTask DisposeAsync()
	{
		testFrames?.Cancel();
		await Task.Run(() =>
		{
			capture?.Dispose();
		});
		lock (sync)
		{
			view.Dispose();
			memory.Dispose();
		}
	}
}
