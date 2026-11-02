using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Qadr.Core;

namespace Qadr.Engine;

internal sealed class VisionClient : IDisposable
{
	private Process? process;

	private readonly object writeLock = new object();

	private volatile bool stopping;

	public bool Exited => process?.HasExited ?? false;

	public event Action<VisionResult>? Result;

	public void Start(string session, string? python)
	{
		string baseDirectory = AppContext.BaseDirectory;
		ProcessStartInfo processStartInfo = new ProcessStartInfo(python ?? Path.Combine(baseDirectory, "vision", "Qadr.Vision.exe"))
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardInput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = baseDirectory
		};
		processStartInfo.Environment["OPENBLAS_NUM_THREADS"] = "1";
		processStartInfo.Environment["OMP_NUM_THREADS"] = "2";
		processStartInfo.Environment["MKL_NUM_THREADS"] = "1";
		if (python != null)
		{
			processStartInfo.ArgumentList.Add(Path.Combine(baseDirectory, "vision", "worker.py"));
		}
		processStartInfo.ArgumentList.Add("--compact-results");
		string[] array = new string[6]
		{
			"--session",
			session,
			"--model",
			Path.Combine(baseDirectory, "models", "face_landmarker.task"),
			"--speech-model",
			Path.Combine(baseDirectory, "models", "vosk-model-small-en-us-0.15")
		};
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		Process child = Process.Start(processStartInfo) ?? throw new IOException("Vision worker could not start");
		process = child;
		Task.Run(async () =>
		{
			try
			{
				string text = default;
				while (true)
				{
					bool flag = !stopping;
					if (flag)
					{
						text = await child.StandardOutput.ReadLineAsync();
						flag = text != null;
					}
					if (!flag)
					{
						break;
					}
					if (text.Length > 1500000)
					{
						throw new InvalidDataException("Worker message too large");
					}
					VisionResult visionResult = Protocol.Decode<VisionResult>(text);
					if (visionResult.Version != 1)
					{
						throw new InvalidDataException("Worker protocol version mismatch");
					}
					Result?.Invoke(visionResult);
				}
			}
			catch (Exception ex)
			{
				if (!stopping)
				{
					Result?.Invoke(new VisionResult(1, "error", 0L, 0.0, null, null, null, null, 0.0, ex.Message));
				}
			}
		});
		Task.Run(async () =>
		{
			try
			{
				bool flag;
				do
				{
					flag = !stopping;
					if (flag)
					{
						flag = await child.StandardError.ReadLineAsync() != null;
					}
				}
				while (flag);
			}
			catch (Exception ex) when ((ex is IOException || ex is ObjectDisposedException) ? true : false)
			{
			}
		});
	}

	public void Voice(bool enabled, string[] commands, string[]? phrases = null, int userStopEpoch = 0, long voiceRevision = 0L)
	{
		lock (writeLock)
		{
			if (process != null && !process.HasExited)
			{
				process.StandardInput.WriteLine(Protocol.Encode(new
				{
					version = 1,
					type = "voice",
					enabled = enabled,
					commands = commands,
					phrases = phrases,
					userStopEpoch = userStopEpoch,
					voiceRevision = voiceRevision
				}));
				process.StandardInput.Flush();
			}
		}
	}

	public void ConfigureFps(int fps)
	{
		lock (writeLock)
		{
			if (process != null && !process.HasExited)
			{
				process.StandardInput.WriteLine(Protocol.Encode(new
				{
					version = 1,
					type = "performance",
					fps = ((fps == 60) ? 60 : 30)
				}));
				process.StandardInput.Flush();
			}
		}
	}

	public void Dispose()
	{
		stopping = true;
		Process process;
		lock (writeLock)
		{
			process = this.process;
			if (process != null && !process.HasExited)
			{
				try
				{
					this.process.StandardInput.WriteLine(Protocol.Encode(new
					{
						version = 1,
						type = "shutdown"
					}));
					this.process.StandardInput.Close();
				}
				catch (IOException)
				{
				}
			}
		}
		process = this.process;
		if (process != null && !process.HasExited && !this.process.WaitForExit(800))
		{
			this.process.Kill(entireProcessTree: true);
		}
		this.process?.Dispose();
		this.process = null;
	}
}
