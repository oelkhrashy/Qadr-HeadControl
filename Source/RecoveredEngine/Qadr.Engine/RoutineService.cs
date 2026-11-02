using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Qadr.Core;

namespace Qadr.Engine;

internal sealed class RoutineService : IDisposable
{
	private struct Point
	{
		public int X;

		public int Y;
	}

	private struct MouseData
	{
		public Point Point;

		public uint Data;

		public uint Flags;

		public uint Time;

		public nuint Extra;
	}

	private struct Message
	{
		public nint Window;

		public uint Id;

		public nuint WParam;

		public nint LParam;

		public uint Time;

		public Point Point;

		public uint Private;
	}

	private delegate nint Hook(int code, nuint message, nint data);

	private readonly Storage storage;

	private readonly SafetyGate gate;

	private readonly Action<string> done;

	private readonly int shellPid;

	private readonly List<RoutineStep> steps = new List<RoutineStep>();

	private readonly object sync = new object();

	private readonly CancellationTokenSource closing = new CancellationTokenSource();

	private CancellationTokenSource? playback;

	private Task? playTask;

	private readonly ManualResetEventSlim confirm = new ManualResetEventSlim();

	private readonly Channel<(int Kind, int X, int Y, int Generation)> events = Channel.CreateBounded<(int, int, int, int)>(64);

	private int recordingGeneration;

	private Thread? hookThread;

	private uint threadId;

	private nint mouseHook;

	private nint keyHook;

	private Hook? mouseProc;

	private Hook? keyProc;

	private string? recordingName;

	private bool keyboardEnabled;

	private string[] names = Array.Empty<string>();

	private double recordedAt;

	private double lastEvent;

	public bool WaitingForConfirmation { get; private set; }

	public string[] Names => Volatile.Read(in names);

	public event Action<string>? ConfirmationRequired;

	[DllImport("user32.dll")]
	private static extern nint SetWindowsHookEx(int id, Hook proc, nint module, uint thread);

	[DllImport("user32.dll")]
	private static extern bool UnhookWindowsHookEx(nint handle);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);

	[DllImport("user32.dll")]
	private static extern int GetMessage(out Message message, nint window, uint min, uint max);

	[DllImport("user32.dll")]
	private static extern bool PostThreadMessage(uint thread, uint message, nuint w, nint l);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern nint GetModuleHandle(string? module);

	public RoutineService(Storage storage, SafetyGate gate, Action<string> done, string? shellPid)
	{
		this.storage = storage;
		this.gate = gate;
		this.done = done;
		int.TryParse(shellPid, out this.shellPid);
		names = storage.Names("routines");
		Task.Run((Func<Task?>)Collect);
	}

	private static string Name(string name)
	{
		name = Regex.Replace(name.Trim().ToLowerInvariant(), "\\s+", " ");
		if (!Regex.IsMatch(name, "^[a-z][a-z0-9 ]{0,31}$"))
		{
			throw new ArgumentException("Use a routine name with 1–32 English letters, digits and spaces.");
		}
		return name;
	}

	public void StartRecording(string name, bool keyboard)
	{
		Task task = playTask;
		if (task != null && !task.IsCompleted)
		{
			throw new InvalidOperationException("Stop the current routine before recording another.");
		}
		Cancel();
		lock (sync)
		{
			recordingName = Name(name);
			steps.Clear();
			keyboardEnabled = keyboard;
			recordedAt = (lastEvent = Arguments.Now);
		}
		if (hookThread == null)
		{
			hookThread = new Thread(Hooks)
			{
				IsBackground = true,
				Name = "Qadr recording hooks"
			};
			hookThread.Start();
		}
	}

	private void Hooks()
	{
		threadId = GetCurrentThreadId();
		mouseProc = (int code, nuint num, nint data) =>
		{
			if (code >= 0 && (num == 514 || num == 517) && Volatile.Read(in recordingName) != null)
			{
				MouseData mouseData = Marshal.PtrToStructure<MouseData>(data);
				events.Writer.TryWrite(((num != 514) ? 1 : 0, mouseData.Point.X, mouseData.Point.Y, Volatile.Read(in recordingGeneration)));
			}
			return CallNextHookEx(mouseHook, code, num, data);
		};
		keyProc = (int code, nuint num, nint data) =>
		{
			if (code >= 0 && num == 257 && Volatile.Read(in recordingName) != null && keyboardEnabled)
			{
				int num2 = Marshal.ReadInt32(data);
				if (num2 != 117 && num2 != 27)
				{
					events.Writer.TryWrite((2, 0, 0, Volatile.Read(in recordingGeneration)));
				}
			}
			return CallNextHookEx(keyHook, code, num, data);
		};
		mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0u);
		keyHook = SetWindowsHookEx(13, keyProc, GetModuleHandle(null), 0u);
		if (mouseHook == 0 || keyHook == 0)
		{
			lock (sync)
			{
				recordingName = null;
				return;
			}
		}
		try
		{
			Message message;
			while (GetMessage(out message, 0, 0u, 0u) > 0)
			{
			}
		}
		finally
		{
			UnhookWindowsHookEx(mouseHook);
			UnhookWindowsHookEx(keyHook);
		}
	}

	private async Task Collect()
	{
		_ = 1;
		try
		{
			bool flag = default;
			await foreach ((int, int, int, int) item in events.Reader.ReadAllAsync(closing.Token))
			{
				if (recordingName == null || item.Item4 != Volatile.Read(in recordingGeneration))
				{
					continue;
				}
				try
				{
					AutomationElement automationElement = ((item.Item1 == 2) ? AutomationElement.FocusedElement : AutomationElement.FromPoint(new System.Windows.Point(item.Item2, item.Item3)));
					if (automationElement.Current.ProcessId == shellPid || automationElement.Current.ProcessId == Environment.ProcessId)
					{
						continue;
					}
					object patternObject = automationElement.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, ignoreDefaultValue: true);
					int num;
					if (patternObject is bool)
					{
						flag = (bool)patternObject;
						num = 1;
					}
					else
					{
						num = 0;
					}
					if ((num == 0) | flag)
					{
						continue;
					}
					Target target = Selector(automationElement, item.Item2, item.Item3);
					if ((object)target == null)
					{
						continue;
					}
					string text = "click";
					string text2 = null;
					if (item.Item1 == 2)
					{
						if (automationElement.Current.ControlType == ControlType.Edit && automationElement.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject2) && patternObject2 is ValuePattern { Current: { IsReadOnly: false } } valuePattern)
						{
							text = "value";
							text2 = valuePattern.Current.Value;
							if (text2.Length <= 2048)
							{
								goto IL_01eb;
							}
						}
						continue;
					}
					if (item.Item1 == 0 && automationElement.TryGetCurrentPattern(InvokePattern.Pattern, out patternObject))
					{
						text = "invoke";
					}
					goto IL_01eb;
					IL_01eb:
					double now = Arguments.Now;
					lock (sync)
					{
						if (recordingName == null || item.Item4 != recordingGeneration)
						{
							continue;
						}
						if (now - recordedAt > 120.0 || steps.Count >= 1000)
						{
							keyboardEnabled = false;
							continue;
						}
						RoutineStep routineStep = new RoutineStep(text, target, Math.Clamp(now - lastEvent, 0.0, 10.0), text2, (item.Item1 == 1) ? "right" : "left");
						if (text == "value" && steps.Count > 0)
						{
							List<RoutineStep> list = steps;
							if (list[list.Count - 1].Kind == "value")
							{
								List<RoutineStep> list2 = steps;
								if (Protocol.Encode(list2[list2.Count - 1].Target) == Protocol.Encode(target))
								{
									List<RoutineStep> list3 = steps;
									list3[list3.Count - 1] = routineStep;
									goto IL_0324;
								}
							}
						}
						steps.Add(routineStep);
						goto IL_0324;
						IL_0324:
						lastEvent = now;
					}
				}
				catch (Exception ex) when ((ex is ElementNotAvailableException || ex is InvalidOperationException || ex is Win32Exception || ex is UnauthorizedAccessException) ? true : false)
				{
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static Target? Selector(AutomationElement element, int x, int y)
	{
		List<ElementKey> list = new List<ElementKey>();
		AutomationElement automationElement = element;
		AutomationElement automationElement2 = null;
		for (int i = 0; i < 12; i++)
		{
			if ((object)automationElement == null)
			{
				break;
			}
			if (automationElement.Current.ControlType == ControlType.Window)
			{
				automationElement2 = automationElement;
				break;
			}
			list.Add(new ElementKey(automationElement.Current.Name, automationElement.Current.AutomationId, automationElement.Current.ControlType.Id));
			automationElement = TreeWalker.ControlViewWalker.GetParent(automationElement);
		}
		if ((object)automationElement2 == null || list.Count == 0)
		{
			return null;
		}
		Rect boundingRectangle = automationElement2.Current.BoundingRectangle;
		if (boundingRectangle.IsEmpty || boundingRectangle.Width <= 0.0 || boundingRectangle.Height <= 0.0)
		{
			return null;
		}
		string processName = Process.GetProcessById(automationElement2.Current.ProcessId).ProcessName;
		if (processName.StartsWith("Qadr", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return new Target(processName, automationElement2.Current.Name, list.ToArray(), Math.Clamp(((double)x - boundingRectangle.X) / boundingRectangle.Width, 0.0, 1.0), Math.Clamp(((double)y - boundingRectangle.Y) / boundingRectangle.Height, 0.0, 1.0));
	}

	public void SaveRecording()
	{
		lock (sync)
		{
			string text = recordingName ?? throw new InvalidOperationException("Recording is not active");
			recordingName = null;
			if (steps.Count == 0)
			{
				throw new InvalidOperationException("No supported actions were recorded. Try a standard Windows application.");
			}
			storage.Save("routines/" + text + ".json", new Routine(text, steps.ToArray()));
			Volatile.Write(ref names, storage.Names("routines"));
		}
	}

	private Routine Load(string name)
	{
		Routine routine = storage.Load<Routine>("routines/" + Name(name) + ".json") ?? throw new InvalidDataException("Routine is missing or damaged");
		int num = routine.Steps.Length;
		bool flag = ((num < 1 || num > 1000) ? true : false);
		if (flag || routine.Steps.Any((RoutineStep s) =>
		{
			bool flag2;
			switch (s.Kind)
			{
			case "invoke":
			case "value":
			case "click":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			bool flag3 = !flag2 || !double.IsFinite(s.Delay);
			if (!flag3)
			{
				double delay = s.Delay;
				bool flag4 = ((delay < 0.0 || delay > 10.0) ? true : false);
				flag3 = flag4;
			}
			bool flag5 = flag3;
			if (!flag5)
			{
				int num2 = s.Target.Path.Length;
				bool flag4 = ((num2 < 1 || num2 > 12) ? true : false);
				flag5 = flag4;
			}
			bool flag6 = flag5 || !double.IsFinite(s.Target.X) || !double.IsFinite(s.Target.Y);
			if (!flag6)
			{
				double delay = s.Target.X;
				bool flag4 = ((delay < 0.0 || delay > 1.0) ? true : false);
				flag6 = flag4;
			}
			bool flag7 = flag6;
			if (!flag7)
			{
				double delay = s.Target.Y;
				bool flag4 = ((delay < 0.0 || delay > 1.0) ? true : false);
				flag7 = flag4;
			}
			if (!flag7)
			{
				string? value = s.Value;
				if (value == null)
				{
					return false;
				}
				return value.Length > 2048;
			}
			return true;
		}))
		{
			throw new InvalidDataException("Routine contains invalid steps");
		}
		return routine;
	}

	public string Describe(string name)
	{
		Routine routine = Load(name);
		return string.Join("; ", routine.Steps.Take(12).Select((RoutineStep s, int i) => $"{i + 1}. {s.Kind} {s.Target.Path[0].Name} in {s.Target.Process}")) + ((routine.Steps.Length > 12) ? $"; and {routine.Steps.Length - 12} more steps" : "");
	}

	public void Run(string name, bool noInput, int expectedUserStops)
	{
		Task task = playTask;
		if (task != null && !task.IsCompleted)
		{
			throw new InvalidOperationException("The previous routine is still stopping. Try again in a moment.");
		}
		Routine routine = Load(name);
		Cancel();
		playback = new CancellationTokenSource();
		CancellationToken token = playback.Token;
		playTask = Task.Run(() =>
		{
			string obj = "Routine cancelled";
			try
			{
				if (gate.UserStops == expectedUserStops)
				{
					if (gate.Stopped)
					{
						if (!gate.RequestResume(expectedUserStops))
						{
							return;
						}
						long tickCount = Environment.TickCount64;
						while (gate.Stopped && Environment.TickCount64 - tickCount < 500)
						{
							if (token.WaitHandle.WaitOne(10))
							{
								return;
							}
						}
					}
					RoutineStep[] array = routine.Steps;
					foreach (RoutineStep step in array)
					{
						if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(step.Delay)) || gate.Stopped || gate.UserStops != expectedUserStops)
						{
							return;
						}
						var (automationElement, automationElement2) = Find(step.Target);
						if ((object)automationElement == null || (object)automationElement2 == null)
						{
							throw new InvalidOperationException("A routine target is missing or ambiguous. Qadr stopped before acting. Review or record this step again.");
						}
						string name2 = step.Target.Path[0].Name;
						if (Regex.IsMatch(name2, "\\b(send|delete|pay|purchase|buy|submit|confirm order)\\b|إرسال|ارسال|حذف|دفع|شراء|تأكيد الطلب", RegexOptions.IgnoreCase))
						{
							WaitingForConfirmation = true;
							confirm.Reset();
							ConfirmationRequired?.Invoke("Confirm step: " + name2 + " in " + step.Target.Process);
							if (!confirm.Wait(TimeSpan.FromSeconds(30L), token) || gate.Stopped)
							{
								return;
							}
							WaitingForConfirmation = false;
						}
						if (!noInput)
						{
							if (step.Kind == "invoke")
							{
								if (!automationElement2.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
								{
									throw new InvalidOperationException("The routine target no longer supports its recorded action.");
								}
								if (!NativeInput.WithGate(gate, () =>
								{
									((InvokePattern)invoke).Invoke();
								}))
								{
									throw new InvalidOperationException("Control stopped before invoking the routine target.");
								}
							}
							else if (step.Kind == "value")
							{
								object currentPropertyValue = automationElement2.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, ignoreDefaultValue: true);
								if (!(currentPropertyValue is bool) || (bool)currentPropertyValue || !automationElement2.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
								{
									throw new InvalidOperationException("Text entry is not permitted in this field.");
								}
								if (!NativeInput.WithGate(gate, () =>
								{
									((ValuePattern)value).SetValue(step.Value ?? "");
								}))
								{
									throw new InvalidOperationException("Control stopped before entering routine text.");
								}
							}
							else
							{
								Rect boundingRectangle = automationElement2.Current.BoundingRectangle;
								if (automationElement.Current.IsOffscreen || automationElement2.Current.IsOffscreen || boundingRectangle.IsEmpty || boundingRectangle.Width <= 0.0 || boundingRectangle.Height <= 0.0)
								{
									throw new InvalidOperationException("The routine target is not visible.");
								}
								if (!NativeInput.Move(gate, boundingRectangle.X + boundingRectangle.Width / 2.0, boundingRectangle.Y + boundingRectangle.Height / 2.0) || !NativeInput.Button(gate, step.Button, down: true) || !NativeInput.Button(gate, step.Button, down: false))
								{
									throw new InvalidOperationException("Windows could not complete the routine click. Control stopped.");
								}
							}
						}
					}
					obj = (noInput ? "Routine preview finished (input disabled)" : "Routine finished");
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex2)
			{
				obj = "Routine stopped: " + ex2.Message;
			}
			finally
			{
				WaitingForConfirmation = false;
				gate.Stop();
				done(obj);
			}
		});
	}

	private static (AutomationElement?, AutomationElement?) Find(Target target)
	{
		AutomationElementCollection automationElementCollection = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
		List<AutomationElement> list = new List<AutomationElement>();
		foreach (AutomationElement item in automationElementCollection)
		{
			try
			{
				if (item.Current.Name == target.Window && Process.GetProcessById(item.Current.ProcessId).ProcessName == target.Process)
				{
					list.Add(item);
				}
			}
			catch (ArgumentException)
			{
			}
		}
		if (list.Count != 1)
		{
			return (null, null);
		}
		AutomationElement automationElement2 = list[0];
		ElementKey elementKey = target.Path[0];
		AndCondition condition = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.LookupById(elementKey.Type)), new PropertyCondition(AutomationElement.NameProperty, elementKey.Name), new PropertyCondition(AutomationElement.AutomationIdProperty, elementKey.AutomationId));
		AutomationElementCollection automationElementCollection2 = automationElement2.FindAll(TreeScope.Descendants, condition);
		List<AutomationElement> list2 = new List<AutomationElement>();
		foreach (AutomationElement item2 in automationElementCollection2)
		{
			AutomationElement automationElement4 = item2;
			bool flag = true;
			for (int i = 0; i < target.Path.Length; i++)
			{
				if ((object)automationElement4 == null || automationElement4.Current.Name != target.Path[i].Name || automationElement4.Current.AutomationId != target.Path[i].AutomationId || automationElement4.Current.ControlType.Id != target.Path[i].Type)
				{
					flag = false;
					break;
				}
				automationElement4 = TreeWalker.ControlViewWalker.GetParent(automationElement4);
			}
			if (flag)
			{
				list2.Add(item2);
			}
		}
		return (automationElement2, (list2.Count == 1) ? list2[0] : null);
	}

	public void ConfirmStep()
	{
		confirm.Set();
	}

	public void Cancel()
	{
		lock (sync)
		{
			recordingName = null;
			Interlocked.Increment(ref recordingGeneration);
		}
		playback?.Cancel();
	}

	public void Dispose()
	{
		Cancel();
		closing.Cancel();
		if (threadId != 0)
		{
			PostThreadMessage(threadId, 18u, 0u, 0);
		}
		hookThread?.Join(200);
		closing.Dispose();
	}
}
