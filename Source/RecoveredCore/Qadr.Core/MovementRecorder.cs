using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Qadr.Core;

public sealed class MovementRecorder(string directory, int capacity = 1024, double maximumSeconds = 900.0, long maximumBytes = 33554432L) : IDisposable
{
	private sealed class Session(string stem, double start, bool synthetic)
	{
		public readonly string Stem = stem;

		public readonly double Start = start;

		public readonly bool Synthetic = synthetic;

		public Channel<MovementMeasurement> Queue;

		public Task Work = Task.CompletedTask;

		public long Dropped;

		public bool Limit;
	}

	private static readonly JsonSerializerOptions TraceJson = new JsonSerializerOptions(Protocol.Json)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private Session? current;

	private MovementRecordingStatus status = new MovementRecordingStatus(Recording: false, Saving: false, null, null, 0L);

	public MovementRecordingStatus Status
	{
		get
		{
			MovementRecordingStatus movementRecordingStatus = Volatile.Read(in status) with { };
			Session session = current;
			return movementRecordingStatus with { Dropped = session != null ? Interlocked.Read(in session.Dropped) : 0 };
		}
	}

	public bool Recording => Volatile.Read(in status).Recording;

	public string DirectoryPath => Path.GetFullPath(directory);

	public Task Completion => current?.Work ?? Task.CompletedTask;

	public bool Start(double now, bool synthetic = false)
	{
		if (double.IsFinite(now))
		{
			Session session = current;
			if (session != null)
			{
				Task work = session.Work;
				if (work != null && !work.IsCompleted)
				{
					goto IL_0031;
				}
			}
			string stem = "Qadr-Movement-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
			Session session2 = new Session(stem, now, synthetic)
			{
				Queue = Channel.CreateBounded<MovementMeasurement>(new BoundedChannelOptions(Math.Max(1, capacity))
				{
					FullMode = BoundedChannelFullMode.Wait,
					SingleReader = true
				})
			};
			current = session2;
			Volatile.Write(ref status, new MovementRecordingStatus(Recording: true, Saving: false, null, null, 0L));
			session2.Work = Task.Run(() => Write(session2));
			return true;
		}
		goto IL_0031;
		IL_0031:
		return false;
	}

	public void Add(MovementMeasurement measurement)
	{
		Session session = current;
		if (session != null && Recording && double.IsFinite(measurement.Time) && !(measurement.Time < session.Start))
		{
			if (measurement.Time - session.Start > maximumSeconds)
			{
				session.Limit = true;
				Stop();
			}
			else if (!session.Queue.Writer.TryWrite(measurement))
			{
				Interlocked.Increment(ref session.Dropped);
			}
		}
	}

	public void Stop()
	{
		Session session = current;
		if (session != null && Recording)
		{
			Volatile.Write(ref status, new MovementRecordingStatus(Recording: false, Saving: true, null, null, Interlocked.Read(in session.Dropped)));
			session.Queue.Writer.TryComplete();
		}
	}

	public void Tick(double now)
	{
		Session session = current;
		if (session != null && Recording && now - session.Start > maximumSeconds)
		{
			session.Limit = true;
			Stop();
		}
	}

	private async Task Write(Session s)
	{
		_ = 8;
		try
		{
			Directory.CreateDirectory(DirectoryPath);
			string path = Path.Combine(DirectoryPath, s.Stem + ".jsonl");
			long frames = 0L;
			long accepted = 0L;
			long pointers = 0L;
			long nativeFailures = 0L;
			double last = s.Start;
			List<double> processing = new List<double>();
			List<double> inference = new List<double>();
			Dictionary<string, long> reasons = new Dictionary<string, long>();
			Dictionary<string, long> actions = new Dictionary<string, long>();
			List<double> targetTimes = new List<double>();
			List<double> targetErrors = new List<double>();
			long restN = 0L;
			double meanX = 0.0;
			double meanY = 0.0;
			double m2X = 0.0;
			double m2Y = 0.0;
			double? firstCapture = null;
			double? lastCapture = null;
			double? targetAt = null;
			long captures = 0L;
			long targetHits = 0L;
			long targetMisses = 0L;
			string label = "unlabelled";
			List<AssessmentResult> trials = new List<AssessmentResult>();
			List<MovementTuningStatus> tuningDecisions = new List<MovementTuningStatus>();
			await using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous))
			{
				await using StreamWriter writer = new StreamWriter(stream);
				string text = Protocol.Encode(new
				{
					kind = "session",
					schema = 1,
					version = "qadr-head-control-quality-1",
					synthetic = s.Synthetic,
					clock = "QPC seconds; relative to sessionStart",
					sessionStart = s.Start,
					images = false,
					audio = false,
					windowContent = false,
					limits = new
					{
						seconds = maximumSeconds,
						bytes = maximumBytes
					},
					notes = "NativeAccepted means Windows accepted a request, not proof of target success. Capture age excludes sensor/driver delay. Target tests require the user to use face control; physical helper input is not distinguishable."
				});
				long written = Encoding.UTF8.GetByteCount(text) + Environment.NewLine.Length;
				await writer.WriteLineAsync(text);
				long flushedAt = Environment.TickCount64;
				await foreach (MovementMeasurement row in s.Queue.Reader.ReadAllAsync())
				{
					string text2 = JsonSerializer.Serialize(row, TraceJson);
					int num = Encoding.UTF8.GetByteCount(text2) + Environment.NewLine.Length;
					if (written + num > maximumBytes)
					{
						s.Limit = true;
						Volatile.Write(ref status, new MovementRecordingStatus(Recording: false, Saving: true, null, null, 0L));
						s.Queue.Writer.TryComplete();
						Interlocked.Increment(ref s.Dropped);
						MovementMeasurement item;
						while (s.Queue.Reader.TryRead(out item))
						{
							Interlocked.Increment(ref s.Dropped);
						}
						break;
					}
					written += num;
					await writer.WriteLineAsync(text2);
					last = Math.Max(last, row.Time);
					if (row.Kind == "label")
					{
						label = row.Label ?? "unlabelled";
					}
					if (row.Kind == "frame")
					{
						frames++;
						if (row.QualityAccepted == true)
						{
							accepted++;
						}
						else
						{
							string key = row.Reason ?? "not accepted";
							reasons[key] = reasons.GetValueOrDefault(key) + 1;
						}
						Observation face = row.Face;
						if ((object)face != null && (!lastCapture.HasValue || face.Timestamp > lastCapture))
						{
							firstCapture.GetValueOrDefault();
							if (!firstCapture.HasValue)
							{
								double timestamp = face.Timestamp;
								firstCapture = timestamp;
							}
							lastCapture = face.Timestamp;
							captures++;
						}
						double? processingMs = row.ProcessingMs;
						if (processingMs.HasValue)
						{
							double timestamp = processingMs.GetValueOrDefault();
							if (timestamp >= 0.0 && timestamp < 60000.0)
							{
								processing.Add(row.ProcessingMs.Value);
							}
						}
						processingMs = row.InferenceMs;
						if (processingMs.HasValue)
						{
							double timestamp = processingMs.GetValueOrDefault();
							if (timestamp >= 0.0 && timestamp < 60000.0)
							{
								inference.Add(row.InferenceMs.Value);
							}
						}
						if (label == "rest" && row.State == "Tracking" && row.QualityAccepted == true)
						{
							processingMs = row.CursorX;
							if (processingMs.HasValue)
							{
								double valueOrDefault = processingMs.GetValueOrDefault();
								processingMs = row.CursorY;
								if (processingMs.HasValue)
								{
									double valueOrDefault2 = processingMs.GetValueOrDefault();
									restN++;
									double num2 = valueOrDefault - meanX;
									double num3 = valueOrDefault2 - meanY;
									meanX += num2 / (double)restN;
									meanY += num3 / (double)restN;
									m2X += num2 * (valueOrDefault - meanX);
									m2Y += num3 * (valueOrDefault2 - meanY);
								}
							}
						}
					}
					if (row.Kind == "pointer")
					{
						pointers++;
					}
					if (row.NativeAccepted == false)
					{
						nativeFailures++;
					}
					if (row.Kind == "gesture")
					{
						string label2 = row.Label;
						if (label2 != null)
						{
							actions[label2] = actions.GetValueOrDefault(label2) + 1;
						}
					}
					AssessmentResult assessmentResult = row.AssessmentResult;
					if ((object)assessmentResult != null)
					{
						trials.Add(assessmentResult);
					}
					MovementTuningStatus tuning = row.Tuning;
					if ((object)tuning != null)
					{
						tuningDecisions.Add(tuning);
					}
					if (row.Kind == "target")
					{
						targetAt = row.Time;
					}
					if (row.Kind == "target-hit" && row.State == "Tracking")
					{
						double? processingMs = row.TargetX;
						if (processingMs.HasValue)
						{
							double valueOrDefault3 = processingMs.GetValueOrDefault();
							processingMs = row.TargetY;
							if (processingMs.HasValue)
							{
								double valueOrDefault4 = processingMs.GetValueOrDefault();
								processingMs = row.TargetWidth;
								if (processingMs.HasValue)
								{
									double valueOrDefault5 = processingMs.GetValueOrDefault();
									processingMs = row.TargetHeight;
									if (processingMs.HasValue)
									{
										double valueOrDefault6 = processingMs.GetValueOrDefault();
										processingMs = row.CursorX;
										if (processingMs.HasValue)
										{
											double valueOrDefault7 = processingMs.GetValueOrDefault();
											processingMs = row.CursorY;
											if (processingMs.HasValue)
											{
												double valueOrDefault8 = processingMs.GetValueOrDefault();
												if (valueOrDefault7 >= valueOrDefault3 && valueOrDefault7 <= valueOrDefault3 + valueOrDefault5 && valueOrDefault8 >= valueOrDefault4 && valueOrDefault8 <= valueOrDefault4 + valueOrDefault6)
												{
													targetHits++;
													targetErrors.Add(Math.Sqrt(Math.Pow(valueOrDefault7 - valueOrDefault3 - valueOrDefault5 / 2.0, 2.0) + Math.Pow(valueOrDefault8 - valueOrDefault4 - valueOrDefault6 / 2.0, 2.0)));
													if (targetAt.HasValue)
													{
														double valueOrDefault9 = targetAt.GetValueOrDefault();
														targetTimes.Add((row.Time - valueOrDefault9) * 1000.0);
													}
												}
												else
												{
													targetMisses++;
												}
												targetAt = null;
											}
										}
									}
								}
							}
						}
					}
					if (Environment.TickCount64 - flushedAt >= 1000)
					{
						await writer.FlushAsync();
						flushedAt = Environment.TickCount64;
					}
				}
				await writer.FlushAsync();
			}
			string summary = Path.Combine(DirectoryPath, s.Stem + "-summary.json");
			await File.WriteAllTextAsync(summary, Protocol.Encode(new
			{
				schema = 1,
				synthetic = s.Synthetic,
				durationSeconds = last - s.Start,
				frames = frames,
				accepted = accepted,
				rejected = frames - accepted,
				rejectionReasons = reasons,
				observedTrackingFps = ((captures <= 1 || !(lastCapture > firstCapture)) ? ((double?)null) : ((double)(captures - 1) / (lastCapture - firstCapture))),
				captureToEngineP95Ms = P(processing),
				inferenceP95Ms = P(inference),
				pointerRequests = pointers,
				nativeRequestFailures = nativeFailures,
				gestureRequests = actions,
				labelledRestSamples = restN,
				labelledRestRmsPx = ((restN > 1) ? new double?(Math.Sqrt((m2X + m2Y) / (double)(restN - 1))) : ((double?)null)),
				targetHits = targetHits,
				targetMisses = targetMisses,
				targetCompletionP95Ms = P(targetTimes),
				targetCentreErrorP95Px = P(targetErrors),
				dropped = Interlocked.Read(in s.Dropped),
				limited = s.Limit,
				movementTuning = tuningDecisions,
				pointingTest = new
				{
					trials = trials,
					completed = Enumerable.Count(trials, (AssessmentResult t) => t.Completed),
					skippedOrCancelled = Enumerable.Count(trials, (AssessmentResult t) => !t.Completed)
				},
				accuracyMeasured = (targetHits > 0 || trials.Any((AssessmentResult t) => t.Completed)),
				notes = "Rest jitter is meaningful only for correctly labelled rest. Target results measure this labelled task, not universal accuracy. Timing excludes sensor/driver buffering. No images, audio, window content or identity data. No automatic ML training."
			}));
			string text3 = Path.Combine(DirectoryPath, s.Stem + ".zip");
			string text4 = text3 + ".partial";
			using (ZipArchive zipArchive = ZipFile.Open(text4, ZipArchiveMode.Create))
			{
				zipArchive.CreateEntryFromFile(path, "movement.jsonl", CompressionLevel.Fastest);
				zipArchive.CreateEntryFromFile(summary, "summary.json", CompressionLevel.Fastest);
				using StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("READ_ME.txt").Open());
				streamWriter.Write("Qadr numeric movement report. Share this ZIP manually with your reviewer. movement.jsonl contains session/configuration, face pose/expression scores, requested pointer movement, sampled actual screen coordinates, tracking/stop states and labelled test targets. summary.json contains measured timings and task metrics. No camera pictures, sound, desktop capture, typed text or routine contents. Positions use physical screen pixels; face pose uses radians and normalized coordinates. NativeAccepted is SendInput acceptance, not application acknowledgement. Absence of labels/targets prevents inferring user intent or accuracy. The file does not train a model automatically.");
			}
			File.Move(text4, text3);
			Volatile.Write(ref status, new MovementRecordingStatus(Recording: false, Saving: false, text3, null, Interlocked.Read(in s.Dropped)));
		}
		catch (Exception ex)
		{
			bool flag = ((ex is IOException || ex is UnauthorizedAccessException) ? true : false);
			Volatile.Write(ref status, new MovementRecordingStatus(Recording: false, Saving: false, null, flag ? "Could not save movement report. Check free space and folder permissions." : "Movement report could not be created.", Interlocked.Read(in s.Dropped)));
		}
		static double? P(List<double> values)
		{
			if (values.Count <= 0)
			{
				return null;
			}
			return Calibration.Percentile(values, 0.95);
		}
	}

	public void Dispose()
	{
		Stop();
		try
		{
			Completion.Wait(TimeSpan.FromSeconds(2L));
		}
		catch (AggregateException)
		{
		}
	}
}
