using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Qadr.Core;

namespace Qadr.Engine;

internal sealed class Controller : IDisposable
{
	private sealed record TuningPersisted(TunedMovementProfile Profile, MovementTuningStatus Decision, int Epoch);

	private sealed record Devices(CameraDevice[] Cameras);

	private sealed record CameraStarted(bool Success, string? Error = null, int UserStopEpoch = 0);

	private sealed record VoiceIntent(string Text, int UserStopEpoch);

	private sealed record Failure(string Error);

	private sealed record Notice(string Text);

	private sealed record WorkerRestarted;

	private sealed record RoutineDone(string Text);

	private MovementParameters configuredMovement;

	private TunedMovementProfile? cameraTuning;

	private TuningContext? tuningContext;

	private MovementTuningStatus? movementTuning;

	private readonly List<AssessmentResult> tuningBaseline = new List<AssessmentResult>();

	private readonly List<AssessmentResult> tuningValidation = new List<AssessmentResult>();

	private readonly List<AssessmentTarget> tuningTargets = new List<AssessmentTarget>();

	private bool tuningGeometryValid;

	private bool tuningQualityValid;

	private bool tuningSavePending;

	private int tuningEpoch;

	private readonly ManualAdjustmentHistory manualAdjustments = new ManualAdjustmentHistory();

	private readonly string[] args;

	private readonly string session;

	private readonly Process? shell;

	private readonly long startedAt = Environment.TickCount64;

	private long shellCheckAt;

	private readonly SafetyGate gate;

	private readonly Storage storage;

	private readonly SharedTransport shared;

	private readonly CameraCapture capture;

	private VisionClient worker = new VisionClient();

	private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

	private readonly Channel<object> commands = Channel.CreateBounded<object>(new BoundedChannelOptions(64)
	{
		FullMode = BoundedChannelFullMode.Wait
	});

	private readonly Channel<EngineStatus> statuses = Channel.CreateBounded<EngineStatus>(new BoundedChannelOptions(1)
	{
		FullMode = BoundedChannelFullMode.DropOldest
	});

	private readonly Channel<Action> storageWrites = Channel.CreateBounded<Action>(32);

	private readonly MotionController motion = new MotionController();

	private readonly QualityGate quality = new QualityGate();

	private readonly RestNoiseLearner personal = new RestNoiseLearner();

	private readonly MovementRecorder movementRecorder;

	private string? recordedState;

	private Profile? recordedProfile;

	private Settings? recordedMovementSettings;

	private PointingAssessment? assessment;

	private AssessmentResult? assessmentResult;

	private bool assessmentSession;

	private long assessmentRequestSequence;

	private string? assessmentRequest;

	private readonly ResumeRest resumeRest = new ResumeRest();

	private readonly StartupPointer startupPointer = new StartupPointer();

	private bool centreOnReturn;
	private bool alignOnRecovery;
	private readonly RecoveryAlignment recoveryAlignment = new();
	private FaceGeometry? neutralGeometry;
	private Settings? recoveryExpressionSettings;

	private LaunchAssessmentFlow launchCheck;

	private Settings? launchSettings;

	private MouseZone? launchCanvas;

	private bool launchAssessmentRunning;

	private (int X, int Y, int Width, int Height)? controlMonitor;

	private bool restLesson;

	private RestNoiseModel? restBackup;

	private long savedModelSamples;

	private long restLessonSamples;

	private double restLessonStarted;

	private readonly FacePositioning positioning = new FacePositioning();

	private readonly CursorTrajectory trajectory = new CursorTrajectory();

	private readonly MouseActions mouse = new MouseActions();

	private readonly HeadScrollController headScroll = new HeadScrollController();

	private readonly StartupFlow startup;

	private bool cameraVoice;

	private bool tutorialVoice;

	private bool routineVoice;

	private readonly SpeechCommandGate speechCommands = new SpeechCommandGate();

	private (int Epoch, bool Blocked, bool HandsFree, bool Routines, int RoutinesRevision, bool Tutorial, bool Camera, bool Choice, int Cameras)? voiceKey;

	private long voiceRevision;

	private volatile bool voiceSuppressed;

	private int routineVoiceRevision;

	private readonly TutorialFlow tutorial = new TutorialFlow();

	private int userStops;

	private int returnUserStops;

	private readonly Queue<(double Due, MouseStep Step)> mouseEvents = new Queue<(double, MouseStep)>();

	private MouseZone[] mouseZones = Array.Empty<MouseZone>();

	private string? latchedZone;

	private double mouseX;

	private double mouseY;

	private double lastWheel;

	private bool scrollRepeating;

	private string mouseFeedback = "";

	private FacePosition position = new FacePosition("find", "Bring your face into the oval", Ready: false);

	private int fittedRevision;

	private long dockToggleSequence;

	private Observation? observation;

	private string cue = "position";

	private string clickFeedback = "Hold still on a target to left-click";

	private double positionedAt;

	private double guideX;

	private double guideY;

	private double lastPump = Arguments.Now;

	private double clickFeedbackUntil;

	private string? lesson;

	private ClickLearning? clickLearning;

	private VisionResult? latest;

	private string state = "Starting";

	private string title = "Finding your cameras";

	private string help = "Qadr will start calibration after you choose a camera.";

	private string distance = "Waiting for a face";

	private string voice = "Microphone off";

	private string? latestPreview;

	private string? preview;

	private string? activeCamera;

	private string? pendingRoutine;

	private string? lastFailure;

	private string recordingDraft = "new routine";

	private bool recordingText;

	private double pendingUntil;

	private double progress;

	private double lastResult;

	private double lastCapture;

	private double lastStatus;

	private double dwell;

	private long lastSequence = -1L;

	private long lastGeneration;

	private bool face;

	private bool active;

	private bool returning;

	private bool recording;

	private bool cameraRunning;

	private bool cameraStarting;

	private bool cameraNeedsSetup;

	private bool workerReady;

	private bool disposed;

	private bool workerFailed;

	private int workerRestarts;

	private double fps;

	private double latency;

	private double inferenceMs;

	private double frames;

	private double measuredAt = Arguments.Now;

	private bool fpsStarted;

	private CameraDevice[] cameras = Array.Empty<CameraDevice>();

	private Calibration? calibration;

	private RoutineService routines;

	private bool TuningEvidenceTest
	{
		get
		{
			if (DiagnosticControl)
			{
				return args.Contains("--tuning-test-evidence");
			}
			return false;
		}
	}

	private bool DiagnosticControl
	{
		get
		{
			if (args.Contains("--no-input") && args.Contains("--test-source"))
			{
				return args.Contains("--diagnostic-control");
			}
			return false;
		}
	}

	private void ApplyManualSettings(Settings requested)
	{
		requested = requested.Validate();
		manualAdjustments.Observe(requested);
		if (MovementParameters.From(requested) != MovementParameters.From(motion.Settings))
		{
			configuredMovement = MovementParameters.From(requested);
		}
		motion.Settings = requested;
		Profile profile = motion.Profile;
		if ((object)profile != null)
		{
			(int, int, int, int) desktop = NativeInput.Desktop;
			tuningContext = TuningContext.Create(profile, motion.Settings, desktop.Item3, desktop.Item4);
			TunedMovementProfile? tunedMovementProfile = cameraTuning;
			if ((object)tunedMovementProfile == null || !tunedMovementProfile.Matches(tuningContext, configuredMovement.Apply(motion.Settings), TuningEvidenceTest))
			{
				motion.Settings = configuredMovement.Apply(motion.Settings);
				cameraTuning = null;
				movementTuning = null;
			}
		}
		worker.ConfigureFps(motion.Settings.TrackingFps);
		Settings saved = configuredMovement.Apply(motion.Settings);
		if (!storageWrites.Writer.TryWrite(() =>
		{
			storage.Save("settings.json", saved);
		}))
		{
			Fail("Qadr could not queue your settings for saving.");
		}
		ClearMouse();
		motion.Reset();
		trajectory.Clear();
	}

	private void DiagnosticAssessmentResult(string? json)
	{
		if (TuningEvidenceTest && launchAssessmentRunning && assessment != null && json != null && !gate.Stopped)
		{
			AssessmentResult assessmentResult = Protocol.Decode<AssessmentResult>(json);
			if (!(assessmentResult.Id != launchCheck.NextId) && !(assessmentResult.Id != assessment.Progress.Target.Id) && !(assessmentResult.ExpectedAction != "move"))
			{
				RecordAssessment(assessmentResult);
			}
		}
	}

	private void LoadTunedMovement(Profile profile)
	{
		motion.Settings = configuredMovement.Apply(motion.Settings);
		(int, int, int, int) desktop = NativeInput.Desktop;
		tuningContext = TuningContext.Create(profile, motion.Settings, desktop.Item3, desktop.Item4);
		cameraTuning = ((activeCamera == null) ? null : storage.Load<TunedMovementProfile>("movement-tuning/" + Storage.DeviceKey(activeCamera) + ".json"));
		TunedMovementProfile? tunedMovementProfile = cameraTuning;
		if ((object)tunedMovementProfile != null && tunedMovementProfile.Matches(tuningContext, motion.Settings, TuningEvidenceTest))
		{
			motion.Settings = cameraTuning.Current.Apply(motion.Settings);
			movementTuning = new MovementTuningStatus("loaded", "Your verified movement settings were restored for this camera and screen.", cameraTuning.Previous, null, cameraTuning.Current);
		}
		else
		{
			cameraTuning = null;
			movementTuning = null;
		}
	}

	private void StartTuningEvidence()
	{
		tuningBaseline.Clear();
		tuningValidation.Clear();
		tuningTargets.Clear();
		tuningGeometryValid = (tuningQualityValid = true);
		tuningEpoch = gate.UserStops;
		movementTuning = new MovementTuningStatus("measuring", "Measuring your movement. Any adjustment will be checked automatically.", MovementParameters.From(launchSettings));
	}

	private bool AcceptTuningTarget(AssessmentTarget target)
	{
		if (launchCheck.Status.Round == 1)
		{
			tuningTargets.Add(target);
			return true;
		}
		int attempted = launchCheck.Status.Attempted;
		if (attempted >= tuningTargets.Count)
		{
			return false;
		}
		AssessmentTarget assessmentTarget = tuningTargets[attempted];
		int num;
		if (assessmentTarget.X == target.X && assessmentTarget.Y == target.Y && assessmentTarget.Width == target.Width)
		{
			num = ((assessmentTarget.Height == target.Height) ? 1 : 0);
			if (num != 0)
			{
				goto IL_00a5;
			}
		}
		else
		{
			num = 0;
		}
		tuningGeometryValid = false;
		InterruptLaunch();
		gate.UserStop();
		goto IL_00a5;
		IL_00a5:
		return (byte)num != 0;
	}

	private void RecordTuningResult(AssessmentResult result)
	{
		((launchCheck.Status.Round == 1) ? tuningBaseline : tuningValidation).Add(result);
	}

	private void LogTuning()
	{
		MovementTuningStatus movementTuningStatus = movementTuning;
		if ((object)movementTuningStatus != null)
		{
			MovementRecorder movementRecorder = this.movementRecorder;
			double now = Arguments.Now;
			long sequence = 0L;
			MovementTuningStatus tuning = movementTuningStatus;
			movementRecorder.Add(new MovementMeasurement("movement-tuning", now, sequence, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, tuning));
		}
	}

	private void FinishTunedLaunchAssessment()
	{
		if (tuningSavePending || !launchAssessmentRunning || launchCheck.Status.Attempted != 5 || (object)launchSettings == null || gate.Stopped || gate.UserStops != tuningEpoch)
		{
			return;
		}
		TuningContext tuningContext = this.tuningContext;
		if ((object)tuningContext != null)
		{
			Profile profile = motion.Profile;
			if ((object)profile != null)
			{
				(int, int, int, int) desktop = NativeInput.Desktop;
				if (TuningContext.Create(profile, launchSettings, desktop.Item3, desktop.Item4) != tuningContext)
				{
					tuningQualityValid = false;
				}
			}
		}
		if (launchCheck.Status.Round == 1)
		{
			ref MovementTuningStatus? reference = ref movementTuning;
			MovementTuningStatus? movementTuningStatus;
			if (QuickTargetLayout.Comparable(tuningTargets))
			{
				movementTuningStatus = ((!tuningQualityValid) ? new MovementTuningStatus("kept", "Tracking was interrupted; previous settings kept.", MovementParameters.From(launchSettings), null, MovementParameters.From(launchSettings)) : MovementTuner.Propose(launchSettings, cameraTuning?.Anchor ?? configuredMovement, tuningBaseline, args.Contains("--no-input") && !TuningEvidenceTest));
			}
			else
			{
				movementTuningStatus = new MovementTuningStatus("kept", "Test targets were too close or misplaced; previous settings kept.", MovementParameters.From(launchSettings), null, MovementParameters.From(launchSettings));
			}
			reference = movementTuningStatus;
			MovementParameters proposed = movementTuning.Proposed;
			if ((object)proposed != null && movementTuning.Phase == "validating")
			{
				if (launchCheck.BeginValidation())
				{
					motion.Settings = proposed.Apply(motion.Settings);
					motion.Reset();
					trajectory.Clear();
					assessmentResult = null;
					LogTuning();
					title = "Checking your automatic adjustment";
					help = movementTuning.Message;
					MouseZone mouseZone = launchCanvas;
					if (!args.Contains("--no-input") && !NativeInput.Move(gate, mouseZone.X + mouseZone.Width / 2.0, mouseZone.Y + mouseZone.Height / 2.0))
					{
						Fail("Windows could not position the pointer for the repeat check.");
					}
				}
				return;
			}
		}
		else
		{
			MovementTuningStatus movementTuningStatus2 = movementTuning;
			if ((object)movementTuningStatus2 != null)
			{
				movementTuning = MovementTuner.Validate(movementTuningStatus2, tuningBaseline, tuningValidation, tuningGeometryValid && tuningQualityValid);
				if (movementTuning.Phase == "applied")
				{
					MovementParameters applied = movementTuning.Applied;
					if ((object)applied != null)
					{
						TuningContext tuningContext2 = this.tuningContext;
						if ((object)tuningContext2 != null)
						{
							string camera = activeCamera;
							if (camera != null)
							{
								TunedMovementProfile saved = new TunedMovementProfile(1, tuningContext2, cameraTuning?.Anchor ?? configuredMovement, MovementParameters.From(launchSettings), applied, movementTuning.Adjustment ?? "movement", DateTime.UtcNow, TuningEvidenceTest);
								MovementTuningStatus decision = movementTuning;
								int epoch = tuningEpoch;
								if (storageWrites.Writer.TryWrite(() =>
								{
									storage.Save("movement-tuning/" + Storage.DeviceKey(camera) + ".json", saved);
									Queue(new TuningPersisted(saved, decision, epoch));
								}))
								{
									tuningSavePending = true;
									movementTuning = decision with
									{
										Phase = "saving",
										Message = "The repeat check improved your movement. Saving verified settings."
									};
									title = "Saving your movement settings";
									help = movementTuning.Message;
									return;
								}
								movementTuning = movementTuningStatus2 with
								{
									Phase = "reverted",
									Message = "Settings could not be queued for saving. Previous settings restored.",
									Applied = movementTuningStatus2.Before
								};
							}
						}
					}
				}
			}
		}
		CompleteTunedLaunch();
	}

	private void TuningSaved(TuningPersisted saved)
	{
		if (tuningSavePending && launchAssessmentRunning && (object)launchSettings != null && !gate.Stopped && gate.UserStops == saved.Epoch)
		{
			tuningSavePending = false;
			cameraTuning = saved.Profile;
			movementTuning = saved.Decision;
			launchSettings = saved.Profile.Current.Apply(launchSettings);
			CompleteTunedLaunch();
		}
	}

	private void CompleteTunedLaunch()
	{
		if (launchCheck.Finish())
		{
			LogTuning();
			EndAssessment("launch assessment completed", save: true);
			RestoreLaunchSettings();
			title = ((movementTuning?.Phase == "applied") ? "Movement adjusted for you" : "Movement check saved");
			help = movementTuning?.Message ?? "Previous settings kept.";
		}
	}

	private void UndoTunedMovement()
	{
		if (launchCheck.Required || launchAssessmentRunning)
		{
			return;
		}
		TunedMovementProfile tunedMovementProfile = cameraTuning;
		if ((object)tunedMovementProfile == null)
		{
			return;
		}
		string camera = activeCamera;
		if (camera != null)
		{
			TunedMovementProfile restored = tunedMovementProfile with
			{
				Current = tunedMovementProfile.Previous,
				Previous = tunedMovementProfile.Current,
				Reason = "User restored previous verified movement",
				SavedUtc = DateTime.UtcNow
			};
			if (storageWrites.Writer.TryWrite(() =>
			{
				storage.Save("movement-tuning/" + Storage.DeviceKey(camera) + ".json", restored);
			}))
			{
				cameraTuning = restored;
				motion.Settings = restored.Current.Apply(motion.Settings);
				ClearMouse();
				motion.Reset();
				trajectory.Clear();
				movementTuning = new MovementTuningStatus("undone", "Your previous movement settings were restored.", tunedMovementProfile.Current, null, restored.Current);
				title = "Previous movement restored";
				help = movementTuning.Message;
				LogTuning();
			}
		}
	}

	private bool CanAdjustMovement()
	{
		bool flag = launchAssessmentRunning || assessment != null;
		if (!flag)
		{
			bool flag2;
			switch (state)
			{
			case "Routine":
			case "Calibrating":
			case "LearnClick":
			case "LearnRest":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag || mouse.Dragging || headScroll.Active)
		{
			manualAdjustments.Explain("Finish or pause the test, gesture lesson, drag or scrolling before adjusting movement.");
			return false;
		}
		return true;
	}

	private void AdjustMovement(string? choice)
	{
		if (CanAdjustMovement())
		{
			Settings settings = manualAdjustments.Adjust(motion.Settings, choice);
			if ((object)settings != null)
			{
				ApplyManualSettings(settings);
			}
		}
	}

	private void UndoAdjustment()
	{
		if (CanAdjustMovement())
		{
			Settings settings = manualAdjustments.Undo(motion.Settings);
			if ((object)settings != null)
			{
				ApplyManualSettings(settings);
			}
		}
	}

	private void RestoreLaunchSettings()
	{
		tuningSavePending = false;
		Settings settings = launchSettings;
		if ((object)settings != null)
		{
			motion.Settings = settings;
			launchSettings = null;
			motion.Reset();
			trajectory.Clear();
		}
		launchCanvas = null;
		launchAssessmentRunning = false;
	}

	private void InterruptLaunch()
	{
		if (launchAssessmentRunning || !(launchCheck.Status.Phase != "ready"))
		{
			if (launchAssessmentRunning)
			{
				movementTuning = new MovementTuningStatus("interrupted", "Check interrupted. Previous movement settings restored.", MovementParameters.From(launchSettings));
				LogTuning();
			}
			EndAssessment("launch interrupted", save: true);
			RestoreLaunchSettings();
			launchCheck.Interrupt();
		}
	}

	private long ShellWindow()
	{
		long result = 0L;
		try
		{
			shell?.Refresh();
			result = shell?.MainWindowHandle.ToInt64() ?? 0;
		}
		catch (InvalidOperationException)
		{
		}
		return result;
	}

	private (int X, int Y, int Width, int Height) ControlMonitor()
	{
		(int, int, int, int) valueOrDefault = controlMonitor.GetValueOrDefault();
		if (!controlMonitor.HasValue)
		{
			valueOrDefault = NativeInput.MonitorBounds(ShellWindow());
			controlMonitor = valueOrDefault;
			return valueOrDefault;
		}
		return valueOrDefault;
	}

	private void PumpLaunchStartup(double now)
	{
		if (!active || returning || gate.Stopped || now - lastCapture >= 0.2)
		{
			return;
		}
		if (startupPointer.Pending)
		{
			(int, int, int, int) value = NativeInput.MonitorBounds(ShellWindow());
			controlMonitor = value;
			(double, double)? tuple = startupPointer.Plan(gate.UserStops, inputReady: true, value.Item1, value.Item2, value.Item3, value.Item4);
			if (tuple.HasValue)
			{
				(double, double) valueOrDefault = tuple.GetValueOrDefault();
				bool flag = args.Contains("--no-input");
				bool flag2 = flag || NativeInput.Move(gate, valueOrDefault.Item1, valueOrDefault.Item2);
				MovementRecorder movementRecorder = this.movementRecorder;
				long sequence = 0L;
				string text = state;
				double? requestedX = valueOrDefault.Item1;
				double? requestedY = valueOrDefault.Item2;
				bool? nativeAccepted = (flag ? ((bool?)null) : new bool?(flag2));
				movementRecorder.Add(new MovementMeasurement("startup-centre", now, sequence, text, null, null, null, null, null, null, null, requestedX, requestedY, nativeAccepted));
				if (!flag2)
				{
					Fail("Windows could not centre the pointer. Control has stopped.");
					return;
				}
				startupPointer.Complete();
				motion.Reset();
				trajectory.Clear();
			}
		}
		if (startupPointer.Completed && launchCheck.Ready())
		{
			assessmentRequest = "launch";
			assessmentRequestSequence++;
			title = "Quick movement check";
			help = "Five large targets start automatically. Esc stops and saves.";
		}
		if (launchAssessmentRunning)
		{
			PointingAssessment pointingAssessment = assessment;
			if (pointingAssessment != null && pointingAssessment.Progress.ElapsedSeconds >= 8.0)
			{
				EndAssessment("time limit", save: false);
			}
		}
	}

	private void BeginLaunchAssessment(Command c)
	{
		if (launchCheck.Status.Phase != "ready" || state != "Tracking" || gate.Stopped)
		{
			return;
		}
		MouseZone[] zones = c.Zones;
		if (zones == null || zones.Length != 1)
		{
			return;
		}
		MouseZone mouseZone = zones[0];
		if (!new double[4] { mouseZone.X, mouseZone.Y, mouseZone.Width, mouseZone.Height }.All(double.IsFinite) || mouseZone.Width < 100.0 || mouseZone.Height < 100.0 || mouseZone.Width > 20000.0 || mouseZone.Height > 20000.0 || movementRecorder.Status.Saving)
		{
			return;
		}
		StartMovementRecording();
		if (movementRecorder.Recording && launchCheck.Begin())
		{
			ClearMouse();
			launchSettings = motion.Settings;
			motion.Settings = motion.Settings with
			{
				ClickMethod = "none",
				RightClickMethod = "none",
				EyebrowDoubleClick = false,
				PuckerDrag = false,
				MouthScroll = false
			};
			motion.Reset();
			trajectory.Clear();
			launchCanvas = mouseZone;
			launchAssessmentRunning = true;
			assessmentResult = null;
			StartTuningEvidence();
			movementRecorder.Add(new MovementMeasurement("launch-assessment", Arguments.Now, 0L, null, "required every launch; five movement-only targets; eight-second target limit; gestures temporarily suspended"));
			if (!args.Contains("--no-input") && !NativeInput.Move(gate, mouseZone.X + mouseZone.Width / 2.0, mouseZone.Y + mouseZone.Height / 2.0))
			{
				Fail("Windows could not position the pointer for the quick test.");
			}
		}
	}

	private void FinishLaunchAssessment()
	{
		FinishTunedLaunchAssessment();
	}

	public Controller(string[] args)
	{
		this.args = args;
		session = Arguments.Get(args, "--session") ?? throw new ArgumentException("Session required");
		startup = new StartupFlow(!args.Contains("--no-input") || DiagnosticControl);
		launchCheck = new LaunchAssessmentFlow(!args.Contains("--no-input") || (DiagnosticControl && args.Contains("--launch-assessment-check")));
		if (int.TryParse(Arguments.Get(args, "--shell-pid"), out var result) && result > 0)
		{
			shell = Process.GetProcessById(result);
		}
		gate = new SafetyGate(session, create: false);
		shared = new SharedTransport(session, create: true);
		storage = new Storage(Arguments.DataDirectory(args));
		capture = new CameraCapture(session);
		userStops = gate.UserStops;
		motion.Settings = (storage.Load<Settings>("settings.json") ?? new Settings()).Validate();
		configuredMovement = MovementParameters.From(motion.Settings);
		movementRecorder = new MovementRecorder(Path.Combine(Arguments.DataDirectory(args), "movement-reports"), 1024, 900.0, 33554432L);
		if (motion.Settings.MotionDiagnosticsOnStartup)
		{
			StartMovementRecording();
		}
		routines = new RoutineService(storage, gate, (string text) =>
		{
			Queue(new RoutineDone(text));
		}, Arguments.Get(args, "--shell-pid"));
		routines.ConfirmationRequired += (string text) =>
		{
			Queue(new Notice(text));
		};
		lastGeneration = gate.Generation;
		StartWorker();
		Task.Run(async () =>
		{
			_ = 1;
			try
			{
				await foreach (Action item in storageWrites.Reader.ReadAllAsync(cancellation.Token))
				{
					try
					{
						item();
					}
					catch (Exception ex)
					{
						Queue(new Failure("Qadr could not save your settings: " + ex.Message));
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
		});
		Serve();
		if (DiagnosticControl)
		{
			Queue(new Devices(new CameraDevice[1]
			{
				new CameraDevice("diagnostic-camera", "Diagnostic camera")
			}));
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				Queue(new Devices(await CameraCapture.Discover()));
			}
			catch (Exception ex)
			{
				Queue(new Failure(ex.Message));
			}
		});
	}

	private void StartWorker()
	{
		voiceKey = null;
		worker.Result += (VisionResult r) =>
		{
			if (r.Type == "result")
			{
				if (!DiagnosticControl)
				{
					Volatile.Write(ref latest, r);
				}
			}
			else if (r.Type == "preview")
			{
				Volatile.Write(ref latestPreview, r.Preview);
			}
			else if (r.Type == "voice" && r.VoiceRevision == Interlocked.Read(in voiceRevision) && !voiceSuppressed)
			{
				if (VoiceControl.IsStop(r.Text ?? ""))
				{
					gate.UserStop();
				}
				else
				{
					Queue(new VoiceIntent(r.Text ?? "", r.UserStopEpoch ?? (-1)));
				}
			}
			else
			{
				Queue(r);
			}
		};
		worker.Start(session, Arguments.Get(args, "--python"));
		worker.ConfigureFps(motion.Settings.TrackingFps);
		lastResult = Arguments.Now;
	}

	private void Queue(object command)
	{
		if (!disposed && !cancellation.IsCancellationRequested && !commands.Writer.TryWrite(command))
		{
			gate.Stop();
		}
	}

	private async Task Serve()
	{
		try
		{
			using NamedPipeServerStream pipe = new NamedPipeServerStream(session + ".engine", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			await pipe.WaitForConnectionAsync(cancellation.Token);
			StreamReader reader = new StreamReader(pipe);
			try
			{
				using StreamWriter writer = new StreamWriter(pipe)
				{
					AutoFlush = true
				};
				gate.ShellHeartbeat = Environment.TickCount64;
				Task receive = Task.Run(async () =>
				{
					string text = default;
					while (true)
					{
						bool flag = !cancellation.IsCancellationRequested;
						if (flag)
						{
							text = await reader.ReadLineAsync(cancellation.Token);
							flag = text != null;
						}
						if (!flag)
						{
							break;
						}
						if (text.Length > 65536)
						{
							throw new InvalidDataException("Command too large");
						}
						Command command = Protocol.Decode<Command>(text);
						if (command.Version != 1)
						{
							throw new InvalidDataException("Command protocol version mismatch");
						}
						if (command.Type == "heartbeat")
						{
							gate.ShellHeartbeat = Environment.TickCount64;
						}
						else if (command.Type == "stop")
						{
							gate.UserStop();
						}
						else
						{
							if (command.Type == "shutdown")
							{
								gate.Stop();
								gate.Closing = 1;
								cancellation.Cancel();
								break;
							}
							Queue(command);
						}
					}
					if (!cancellation.IsCancellationRequested)
					{
						gate.Stop();
						cancellation.Cancel();
					}
				}, cancellation.Token);
				while (!cancellation.IsCancellationRequested)
				{
					await writer.WriteLineAsync(Protocol.Encode(await statuses.Reader.ReadAsync(cancellation.Token)));
					if (receive.IsCompleted)
					{
						await receive;
					}
				}
			}
			finally
			{
				if (reader != null)
				{
					((IDisposable)reader).Dispose();
				}
			}
		}
		catch (UnauthorizedAccessException)
		{
		}
		catch (Exception ex2) when ((ex2 is IOException || ex2 is OperationCanceledException || ex2 is JsonException || ex2 is InvalidDataException) ? true : false)
		{
			if (!cancellation.IsCancellationRequested && gate.SharedRpc == 0)
			{
				gate.Stop();
				cancellation.Cancel();
			}
		}
	}

	public void Run()
	{
		Protocol.Decode<Command>(Protocol.Encode(new Command("heartbeat")));
		Protocol.Encode(new EngineStatus(state, title, help, 0.0, 0.0, 0.0, Face: false, distance, 0.0, 0.0, "", Array.Empty<CameraDevice>(), null, Array.Empty<string>(), voice, null, 0.0, 1, motion.Settings, position, "position", 0.0, "", 0.0, 0.0, "left", Dragging: false, "Left click", null, 0L, HeadScrolling: false, RoutineVoiceEnabled: false, 0.0, 0.0, 0.0, null, null, null, 0L));
		while (!cancellation.IsCancellationRequested && gate.Closing == 0)
		{
			if (Environment.TickCount64 - shellCheckAt > 500)
			{
				shellCheckAt = Environment.TickCount64;
				Process process = shell;
				if ((process != null && process.HasExited) || (gate.ShellHeartbeat == 0L && shellCheckAt - startedAt > 30000))
				{
					gate.Stop();
					cancellation.Cancel();
					break;
				}
			}
			if (gate.SharedRpc != 0)
			{
				for (int i = 0; i < 16; i++)
				{
					Command command = shared.Receive();
					if ((object)command == null)
					{
						break;
					}
					if (command.Type == "heartbeat")
					{
						gate.ShellHeartbeat = Environment.TickCount64;
						continue;
					}
					if (command.Type == "stop")
					{
						gate.UserStop();
						continue;
					}
					if (command.Type == "shutdown")
					{
						gate.Stop();
						gate.Closing = 1;
						cancellation.Cancel();
						break;
					}
					Queue(command);
				}
			}
			if (gate.UserStops != userStops)
			{
				alignOnRecovery = false;
				recoveryAlignment.Reset();
				startupPointer.Cancel();
				centreOnReturn = false;
				InterruptLaunch();
				if (assessmentSession)
				{
					EndAssessment("user stop", save: true);
					assessmentRequest = "finish";
					assessmentRequestSequence++;
				}
				userStops = gate.UserStops;
				startup.Cancel();
				tutorial.Complete();
				gate.Stop();
			}
			if (gate.Generation != lastGeneration)
			{
				alignOnRecovery = false;
				recoveryAlignment.Reset();
				startupPointer.Cancel();
				centreOnReturn = false;
				InterruptLaunch();
				lastGeneration = gate.Generation;
				gate.ResumeRequest = 0;
				startup.Cancel();
				tutorial.Complete();
				tutorialVoice = (cameraVoice = false);
				voiceKey = null;
				active = (returning = false);
				if (restLesson)
				{
					personal.Load(restBackup);
					restBackup = null;
				}
				restLesson = false;
				personal.Break();
				calibration = null;
				lesson = null;
				ClearMouse();
				motion.Reset();
				trajectory.Clear();
				routines.Cancel();
				recording = false;
				pendingRoutine = null;
				pendingUntil = 0.0;
				cue = "stop";
				state = "Stopped";
				if (lastFailure == null)
				{
					title = "Control is stopped";
					help = "Say resume when you are ready, or choose Resume. Esc stops control; it never resumes.";
				}
			}
			if (gate.Stopped)
			{
				AcknowledgeStop();
			}
			if (gate.PauseRequest != 0)
			{
				gate.PauseRequest = 0;
				gate.UserStop();
			}
			if (gate.ShellHeartbeat != 0L && Environment.TickCount64 - gate.ShellHeartbeat > 1500)
			{
				gate.Stop();
				cancellation.Cancel();
				break;
			}
			for (int j = 0; j < 12; j++)
			{
				if (!commands.Reader.TryRead(out object item))
				{
					break;
				}
				try
				{
					Handle(item);
				}
				catch (Exception ex)
				{
					Fail(ex.Message);
				}
			}
			VisionResult visionResult = Volatile.Read(in latest);
			if ((object)visionResult != null && visionResult.Sequence != lastSequence)
			{
				lastSequence = visionResult.Sequence;
				ProcessObservation(visionResult);
			}
			double now = Arguments.Now;
			this.movementRecorder.Tick(now);
			if (launchAssessmentRunning && !this.movementRecorder.Recording)
			{
				InterruptLaunch();
				gate.UserStop();
			}
			else if (assessmentSession && !this.movementRecorder.Recording)
			{
				EndAssessment("recording ended", save: false);
			}
			if (recordedState != state)
			{
				recordedState = state;
				MovementRecorder movementRecorder = this.movementRecorder;
				long sequence = 0L;
				string text = state;
				string reason = (gate.Stopped ? "input stopped" : "input permitted");
				movementRecorder.Add(new MovementMeasurement("state", now, sequence, text, null, null, null, reason));
			}
			if (cameraRunning && (capture.Error != null || now - capture.LastFrame > 2.0))
			{
				cameraRunning = false;
				Fail(capture.Error ?? "The camera stopped providing frames. Choose another camera or reconnect it.");
			}
			if (!workerFailed && ((cameraRunning && workerReady && now - lastResult > 2.0) || worker.Exited))
			{
				gate.Stop();
				active = false;
				if (workerRestarts++ == 0)
				{
					workerReady = false;
					workerFailed = true;
					VisionClient old = worker;
					Task.Run(() =>
					{
						old.Dispose();
						Queue(new WorkerRestarted());
					});
					title = "Restarting the face tracker";
					help = "Control stays stopped. Resume after your face returns.";
				}
				else
				{
					Fail("The face tracker stopped. Close Qadr and open it again.");
					cameraRunning = false;
					workerFailed = true;
				}
			}
			if (now - lastCapture > 0.2 && (mouse.Dragging || mouseEvents.Count > 0))
			{
				ClearMouse();
			}
			if (face && now - lastResult > 0.25)
			{
				face = false;
				ClearMouse();
				motion.Reset();
				trajectory.Clear();
				resumeRest.Reset();
				calibration?.Lost();
				if (active)
				{
					StartRecoveryAlignment();
					centreOnReturn = true;
					state = "Lost";
					active = false;
					resumeRest.Reset();
					returning = true;
					help = "Bring your face back into view and rest comfortably.";
				}
			}
			PumpLaunchStartup(now);
			if (active && !returning && !gate.Stopped && now - lastCapture < 0.2)
			{
				PumpMouse();
			}
			if (active && !returning && !gate.Stopped && mouseEvents.Count == 0 && trajectory.Ready && now - lastCapture < 0.2 && !args.Contains("--no-input"))
			{
				(double, double) tuple = trajectory.Step(now - lastPump, now, motion.Settings.DirectResponse && motion.Settings.MovementAlgorithm == "adaptive");
				NativeInput.Point cursor = NativeInput.Cursor;
				if ((int)Math.Round(tuple.Item1) != cursor.X || (int)Math.Round(tuple.Item2) != cursor.Y)
				{
					bool flag = NativeInput.Move(gate, tuple.Item1, tuple.Item2);
					NativeInput.Point cursor2 = NativeInput.Cursor;
					RecordAssessment(assessment?.Feed(Arguments.Now, cursor2.X, cursor2.Y, flag && !gate.Stopped));
					if (this.movementRecorder.Recording)
					{
						MovementRecorder movementRecorder2 = this.movementRecorder;
						double now2 = Arguments.Now;
						long sequence2 = lastSequence;
						string text2 = state;
						double? cursorX = cursor2.X;
						double? cursorY = cursor2.Y;
						double? requestedX = tuple.Item1;
						double? requestedY = tuple.Item2;
						bool? nativeAccepted = flag;
						movementRecorder2.Add(new MovementMeasurement("pointer", now2, sequence2, text2, null, null, null, null, null, cursorX, cursorY, requestedX, requestedY, nativeAccepted));
					}
					if (!flag)
					{
						Fail("Windows could not move the pointer. Control has stopped.");
					}
				}
			}
			lastPump = now;
			SyncVoice();
			if (pendingRoutine != null && now > pendingUntil)
			{
				pendingRoutine = null;
			}
			if (now - lastStatus >= 1.0 / (double)motion.Settings.TrackingFps)
			{
				lastStatus = now;
				Publish();
			}
			gate.Heartbeat = Environment.TickCount64;
			Thread.Sleep(8);
		}
		gate.Stop();
		routines.Cancel();
		AcknowledgeStop();
	}

	private void AcknowledgeStop()
	{
		bool flag = false;
		try
		{
			try
			{
				flag = gate.InjectionMutex.WaitOne(5);
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			if (flag)
			{
				NativeInput.ReleaseOwned(gate);
				gate.Acknowledged = gate.Generation;
			}
		}
		finally
		{
			if (flag)
			{
				gate.InjectionMutex.ReleaseMutex();
			}
		}
	}

	private void Handle(object command)
	{
		if (!(command is Devices devices))
		{
			if (!(command is CameraStarted cameraStarted))
			{
				if (!(command is Failure failure))
				{
					if (!(command is Notice notice))
					{
						if (!(command is WorkerRestarted))
						{
							if (!(command is TuningPersisted saved))
							{
								if (!(command is RoutineDone routineDone))
								{
									if (!(command is VoiceIntent voiceIntent))
									{
										if (!(command is VisionResult visionResult))
										{
											Command command2 = command as Command;
											if ((object)command2 == null || !CommandIntent.IsCurrent(command2, gate.UserStops))
											{
												return;
											}
											string text = state;
											bool flag = ((text == "TutorialChoice" || text == "Tutorial") ? true : false);
											bool flag2 = flag;
											if (flag2)
											{
												bool flag3;
												switch (command2.Type)
												{
												case "tutorialStart":
												case "tutorialVoice":
												case "tutorialBack":
												case "tutorialNext":
												case "tutorialSkip":
												case "settings":
												case "shutdown":
												case "stop":
												case "heartbeat":
												case "mouseZones":
												case "speechActivity":
												case "diagnosticVoice":
													flag3 = true;
													break;
												default:
													flag3 = false;
													break;
												}
												flag2 = !flag3;
											}
											if (flag2)
											{
												return;
											}
											flag2 = launchCheck.Required;
											if (flag2)
											{
												switch (command2.Type)
												{
												case "learnEyebrows":
												case "reviewRoutine":
												case "mouseMode":
												case "learnRest":
												case "resetRest":
												case "learnDrag":
												case "learnEyes":
												case "record":
												case "confirm":
												case "restoreProfile":
												case "learnScroll":
												case "learnSmile":
												case "useDwell":
												case "comfortablePosition":
													flag = true;
													break;
												default:
													flag = false;
													break;
												}
												flag2 = flag;
											}
											if (flag2)
											{
												return;
											}
											text = command2.Type;
											if (text == null)
											{
												return;
											}
											MouseZone[] zones;
											switch (text.Length)
											{
											case 22:
												switch (text[16])
												{
												case 'F':
													if (text == "launchAssessmentFinish")
													{
														FinishLaunchAssessment();
													}
													break;
												case 'C':
													if (text == "launchAssessmentCancel")
													{
														InterruptLaunch();
														gate.UserStop();
													}
													break;
												}
												break;
											case 12:
												switch (text[10])
												{
												case 'r':
													if (text == "motionRecord")
													{
														if (command2.Enabled)
														{
															StartMovementRecording();
														}
														else if (launchAssessmentRunning)
														{
															InterruptLaunch();
															gate.UserStop();
														}
														else
														{
															EndAssessment("recording saved", save: false);
															this.movementRecorder.Stop();
														}
													}
													return;
												case 'e':
													break;
												case 'i':
													if (text == "tutorialSkip")
													{
														string text2 = state;
														if ((text2 == "TutorialChoice" || text2 == "Tutorial") ? true : false)
														{
															EndTutorial();
														}
													}
													return;
												case 'x':
													if (text == "tutorialNext" && state == "Tutorial")
													{
														if (tutorial.Next())
														{
															EndTutorial();
														}
														else
														{
															ShowTutorialPage();
														}
													}
													return;
												case 'c':
													if (text == "tutorialBack" && state == "Tutorial")
													{
														tutorial.Back();
														ShowTutorialPage();
													}
													return;
												case 'f':
													if (text == "routineDraft")
													{
														recordingDraft = command2.Name ?? "new routine";
														recordingText = command2.Enabled;
													}
													return;
												default:
													return;
												}
												if (!(text == "motionTarget"))
												{
													break;
												}
												goto IL_13f5;
											case 16:
												switch (text[0])
												{
												case 'a':
												{
													if (!(text == "assessmentTarget") || !(state == "Tracking") || gate.Stopped || !this.movementRecorder.Recording)
													{
														break;
													}
													zones = command2.Zones;
													if (zones == null || zones.Length != 1)
													{
														break;
													}
													MouseZone mouseZone2 = zones[0];
													if ((!launchCheck.Required || (launchAssessmentRunning && command2.Action == "move" && command2.Name == launchCheck.NextId)) && (!launchAssessmentRunning || assessment == null))
													{
														AssessmentTarget assessmentTarget = new AssessmentTarget(command2.Name ?? "", command2.Action ?? "left", mouseZone2.X, mouseZone2.Y, mouseZone2.Width, mouseZone2.Height);
														if (assessmentTarget.Valid && (!launchAssessmentRunning || AcceptTuningTarget(assessmentTarget)))
														{
															EndAssessment("target replaced", save: false);
															NativeInput.Point cursor2 = NativeInput.Cursor;
															assessment = new PointingAssessment(assessmentTarget, Arguments.Now, cursor2.X, cursor2.Y, (double)NativeInput.GetDoubleClickTime() / 1000.0 + 0.06);
															assessmentResult = null;
															assessmentSession = true;
															MovementRecorder movementRecorder3 = this.movementRecorder;
															double now3 = Arguments.Now;
															long sequence3 = 0L;
															string label = assessmentTarget.Id + ":" + assessmentTarget.Action;
															double? targetY = assessmentTarget.X;
															double? targetX = assessmentTarget.Y;
															double? cursorY = assessmentTarget.Width;
															double? inputX = assessmentTarget.Height;
															movementRecorder3.Add(new MovementMeasurement("assessment-target", now3, sequence3, null, label, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, targetY, targetX, cursorY, inputX));
														}
													}
													break;
												}
												case 'd':
													if (text == "diagnosticResult" && DiagnosticControl && (object)command2.DiagnosticResult != null)
													{
														ProcessObservation(command2.DiagnosticResult);
													}
													break;
												}
												break;
											case 15:
												switch (text[0])
												{
												default:
													return;
												case 'a':
												{
													if (!(text == "assessmentClick"))
													{
														return;
													}
													PointingAssessment pointingAssessment = assessment;
													flag2 = pointingAssessment != null && pointingAssessment.Progress.Target.Id == command2.Name && state == "Tracking" && !gate.Stopped && Arguments.Now - lastCapture < 0.2;
													if (flag2)
													{
														switch (command2.Action)
														{
														case "left":
														case "double":
														case "right":
															flag = true;
															break;
														default:
															flag = false;
															break;
														}
														flag2 = flag;
													}
													if (!flag2)
													{
														return;
													}
													double? inputX = command2.InputX;
													if (!inputX.HasValue)
													{
														return;
													}
													double valueOrDefault = inputX.GetValueOrDefault();
													inputX = command2.InputY;
													if (inputX.HasValue)
													{
														double valueOrDefault2 = inputX.GetValueOrDefault();
														if (double.IsFinite(valueOrDefault) && double.IsFinite(valueOrDefault2))
														{
															MovementRecorder movementRecorder = this.movementRecorder;
															double now = Arguments.Now;
															long sequence = 0L;
															string text3 = state;
															string? action = command2.Action;
															inputX = valueOrDefault;
															double? cursorY = valueOrDefault2;
															movementRecorder.Add(new MovementMeasurement("assessment-click", now, sequence, text3, action, null, null, null, null, inputX, cursorY));
															RecordAssessment(pointingAssessment.Click(command2.Action, Arguments.Now, valueOrDefault, valueOrDefault2));
														}
													}
													return;
												}
												case 'm':
													break;
												case 'd':
													if (text == "diagnosticVoice" && DiagnosticControl)
													{
														Voice(command2.Name ?? "", command2.UserStopEpoch ?? (-1));
													}
													return;
												}
												if (!(text == "motionTargetHit"))
												{
													break;
												}
												goto IL_13f5;
											case 14:
												switch (text[1])
												{
												case 's':
													if (text == "assessmentSkip")
													{
														EndAssessment("skipped", save: false);
													}
													break;
												case 'p':
													if (text == "speechActivity")
													{
														speechCommands.Set(command2.Enabled, Arguments.Now);
														voiceSuppressed = speechCommands.Blocked(Arguments.Now);
														voiceKey = null;
													}
													break;
												case 'd':
													if (text == "adjustMovement")
													{
														AdjustMovement(command2.Name);
													}
													break;
												case 'n':
													if (text == "undoAdjustment")
													{
														UndoAdjustment();
													}
													break;
												case 'e':
													if (!(text == "restoreProfile"))
													{
														break;
													}
													if (activeCamera != null)
													{
														Profile profile = storage.Load<Profile>("profiles/" + Storage.DeviceKey(activeCamera) + ".json")?.Upgrade();
														if ((object)profile != null && profile.IsValid)
														{
															motion.SetProfile(profile);
															gate.Stop();
															title = "Saved movement restored";
															break;
														}
													}
													title = "Relearn your movement";
													help = "This saved profile uses the earlier direction mapping. Choose Relearn my movement.";
													break;
												}
												break;
											case 13:
												switch (text[8])
												{
												case 'n':
													if (text == "assessmentEnd")
													{
														if (launchAssessmentRunning)
														{
															InterruptLaunch();
															gate.UserStop();
														}
														else
														{
															EndAssessment("test ended", save: true);
														}
													}
													break;
												case 'S':
													if (text == "tutorialStart" && state == "TutorialChoice" && tutorial.Start())
													{
														ShowTutorialPage();
													}
													break;
												case 'V':
													if (text == "tutorialVoice")
													{
														flag2 = command2.Enabled;
														if (flag2)
														{
															string text2 = state;
															flag = ((text2 == "TutorialChoice" || text2 == "Tutorial") ? true : false);
															flag2 = flag;
														}
														tutorialVoice = flag2;
														voiceKey = null;
													}
													break;
												case 'b':
													if (text == "learnEyebrows")
													{
														StartLesson("brows");
													}
													break;
												case 'r':
													if (text == "saveRecording" && recording)
													{
														routines.SaveRecording();
														recording = false;
														title = "Routine saved";
														routineVoiceRevision++;
														voiceKey = null;
													}
													break;
												case 'u':
													if (text == "reviewRoutine" && command2.Name != null)
													{
														pendingRoutine = command2.Name;
														pendingUntil = Arguments.Now + 30.0;
														title = "Review: " + command2.Name;
														help = routines.Describe(command2.Name) + " Choose Confirm to run.";
													}
													break;
												}
												break;
											case 11:
												switch (text[0])
												{
												case 'm':
													if (text == "motionLabel")
													{
														switch (command2.Name)
														{
														case "rest":
														case "movement":
														case "clicks":
															flag2 = true;
															break;
														default:
															flag2 = false;
															break;
														}
														if (flag2)
														{
															this.movementRecorder.Add(new MovementMeasurement("label", Arguments.Now, 0L, null, command2.Name));
														}
													}
													break;
												case 'l':
													if (text == "learnScroll")
													{
														if (motion.Settings.ClickMethod == "mouth")
														{
															title = "Choose another left-click method first";
															help = "Mouth opening is already assigned to left-click. Select dwell or smile before learning scrolling.";
														}
														else
														{
															StartLesson("scroll");
														}
													}
													break;
												case 'c':
													if (text == "cameraVoice")
													{
														cameraVoice = command2.Enabled && state == "ChooseCamera";
														voiceKey = null;
													}
													break;
												}
												break;
											case 6:
												switch (text[2])
												{
												case 'm':
													if (text == "camera" && command2.CameraId != null && cameras.Any((CameraDevice d) => d.Id == command2.CameraId))
													{
														OpenCamera(command2.CameraId);
													}
													break;
												case 's':
													if (!(text == "resume"))
													{
														break;
													}
													if (active && !returning && !gate.Stopped && state == "Tracking")
													{
														this.movementRecorder.Add(new MovementMeasurement("control-intent", Arguments.Now, 0L, state, "resume", null, null, "ignored: control already running"));
														break;
													}
													this.movementRecorder.Add(new MovementMeasurement("control-intent", Arguments.Now, 0L, state, "resume", null, null, "resume requested"));
													startup.Begin();
													if (cameraRunning && cameraNeedsSetup)
													{
														StartCameraSetup();
													}
													else if (!cameraRunning)
													{
														if (activeCamera != null)
														{
															OpenCamera(activeCamera);
														}
														else if (cameras.Length == 1)
														{
															OpenCamera(cameras[0].Id);
														}
														else if (cameras.Length > 1)
														{
															state = "ChooseCamera";
															title = "Choose your camera";
															help = "Choose a camera manually or by voice.";
														}
														else
														{
															Fail("No camera was found. Connect a camera and choose Refresh cameras.");
														}
													}
													else if ((object)motion.Profile == null)
													{
														StartCalibration();
													}
													else
													{
														BeginReturn(automatic: false, command2.UserStopEpoch);
													}
													break;
												case 'c':
													if (text == "record" && command2.Name != null)
													{
														routines.StartRecording(command2.Name, command2.Enabled);
														recording = true;
														title = "Recording your routine";
														help = "Perform the steps, then choose Save recording. Password fields are excluded.";
													}
													break;
												}
												break;
											case 7:
												switch (text[0])
												{
												case 'r':
													if (!(text == "refresh"))
													{
														break;
													}
													Task.Run(async () =>
													{
														try
														{
															Queue(new Devices(await CameraCapture.Discover()));
														}
														catch (Exception ex)
														{
															Queue(new Failure(ex.Message));
														}
													});
													break;
												case 'c':
													if (text == "confirm")
													{
														if (routines.WaitingForConfirmation)
														{
															routines.ConfirmStep();
														}
														else if (pendingRoutine != null && Arguments.Now <= pendingUntil)
														{
															string name = pendingRoutine;
															pendingRoutine = null;
															routines.Run(name, args.Contains("--no-input"), command2.UserStopEpoch.Value);
															state = "Routine";
															active = false;
														}
													}
													break;
												}
												break;
											case 8:
												switch (text[0])
												{
												case 'r':
													if (text == "recentre")
													{
														centreOnReturn = true;
														controlMonitor = null;
														alignOnRecovery = false;
														recoveryAlignment.Reset();
														this.movementRecorder.Add(new MovementMeasurement("control-intent", Arguments.Now, 0L, state, "recentre", null, null, "explicit recentre requested"));
														InterruptLaunch();
														if ((object)motion.Profile != null && face)
														{
															ClearMouse();
															motion.Reset();
															trajectory.Clear();
															active = false;
															returning = true;
															resumeRest.Reset();
															returnUserStops = gate.UserStops;
															state = "Ready";
															title = "Rest to recentre";
															help = "Your learned movement range is preserved.";
														}
													}
													break;
												case 's':
													if (text == "settings" && !launchAssessmentRunning && (object)command2.Settings != null)
													{
														ApplyManualSettings(command2.Settings);
													}
													break;
												case 'u':
													if (text == "useDwell")
													{
														motion.Settings = motion.Settings with
														{
															ClickMethod = "dwell",
															RightClickMethod = "none"
														};
														Settings dwellSettings = configuredMovement.Apply(motion.Settings);
														storageWrites.Writer.TryWrite(() =>
														{
															storage.Save("settings.json", dwellSettings);
														});
														lesson = null;
														clickLearning = null;
														motion.Reset();
														trajectory.Clear();
														if ((object)motion.Profile != null)
														{
															calibration = null;
															BeginReturn(automatic: false, command2.UserStopEpoch);
														}
														else if (cameraRunning)
														{
															StartCalibration();
														}
													}
													break;
												case 't':
													break;
												}
												break;
											case 9:
												switch (text[5])
												{
												case 'R':
													if (!(text == "learnRest"))
													{
														if (text == "resetRest")
														{
															personal.Load(null);
															motion.PersonalModel = personal.Model;
															SavePersonal(force: true);
															title = "Personal noise learning reset";
														}
													}
													else if ((object)motion.Profile != null && face)
													{
														gate.Stop();
														lastGeneration = gate.Generation;
														ClearMouse();
														trajectory.Clear();
														motion.Reset();
														active = (returning = false);
														restBackup = personal.Model;
														restLessonSamples = personal.Model.Samples;
														restLesson = true;
														restLessonStarted = 0.0;
														personal.Break();
														state = "LearnRest";
														cue = "rest";
														title = "Learn your resting steadiness";
														help = "Rest naturally for four seconds. No mouse actions are sent. Esc cancels.";
													}
													break;
												case 'r':
													if (text == "calibrate")
													{
														gate.Stop();
														lastGeneration = gate.Generation;
														routines.Cancel();
														pendingRoutine = null;
														if (cameraRunning)
														{
															StartCalibration();
														}
													}
													break;
												case 'D':
													if (text == "learnDrag")
													{
														StartLesson("pucker");
													}
													break;
												case 'E':
													if (text == "learnEyes")
													{
														StartLesson("eyes");
													}
													break;
												case 'M':
													if (text == "mouseMode" && command2.Name != null)
													{
														SelectMouse(command2.Name);
													}
													break;
												}
												break;
											case 10:
												switch (text[0])
												{
												case 'l':
													if (text == "learnSmile")
													{
														StartLesson("smile");
													}
													break;
												case 'm':
													if (!(text == "mouseZones"))
													{
														break;
													}
													zones = command2.Zones;
													if (zones != null && zones.Length <= 48)
													{
														mouseZones = command2.Zones.Where((MouseZone z) => new double[4] { z.X, z.Y, z.Width, z.Height }.All(double.IsFinite) && z.Width > 0.0 && z.Height > 0.0 && z.Width < 10000.0 && z.Height < 10000.0 && z.Window != 0).ToArray();
													}
													break;
												}
												break;
											case 21:
												if (text == "launchAssessmentBegin")
												{
													BeginLaunchAssessment(command2);
												}
												break;
											case 18:
												if (text == "movementTuningUndo")
												{
													UndoTunedMovement();
												}
												break;
											case 26:
												if (text == "diagnosticAssessmentResult")
												{
													DiagnosticAssessmentResult(command2.Name);
												}
												break;
											case 23:
												if (text == "diagnosticRestartLaunch" && TuningEvidenceTest && !launchCheck.Required && !this.movementRecorder.Status.Saving)
												{
													launchCheck = new LaunchAssessmentFlow();
													movementTuning = null;
												}
												break;
											case 19:
												if (text == "comfortablePosition" && (object)observation != null)
												{
													positioning.UseComfortablePosition(observation);
													if ((object)motion.Profile != null)
													{
														motion.Rebase(observation);
														BeginReturn(automatic: false, command2.UserStopEpoch);
													}
													else if (cameraRunning)
													{
														StartCalibration();
													}
												}
												break;
											case 5:
												if (text == "voice")
												{
													routineVoice = command2.Enabled;
													voiceKey = null;
												}
												break;
											case 17:
											case 20:
											case 24:
											case 25:
												break;
												IL_13f5:
												zones = command2.Zones;
												if (zones != null && zones.Length == 1)
												{
													MouseZone mouseZone = zones[0];
													if (new double[4] { mouseZone.X, mouseZone.Y, mouseZone.Width, mouseZone.Height }.All(double.IsFinite) && mouseZone.Width > 0.0 && mouseZone.Height > 0.0 && mouseZone.Width < 10000.0 && mouseZone.Height < 10000.0)
													{
														NativeInput.Point cursor = NativeInput.Cursor;
														MovementRecorder movementRecorder2 = this.movementRecorder;
														string kind = ((command2.Type == "motionTarget") ? "target" : "target-hit");
														double now2 = Arguments.Now;
														long sequence2 = 0L;
														string text4 = state;
														string? name2 = command2.Name;
														double? cursorY = cursor.X;
														double? inputX = cursor.Y;
														double? targetX = mouseZone.X;
														double? targetY = mouseZone.Y;
														double? targetWidth = mouseZone.Width;
														double? targetHeight = mouseZone.Height;
														movementRecorder2.Add(new MovementMeasurement(kind, now2, sequence2, text4, name2, null, null, null, null, cursorY, inputX, null, null, null, null, null, null, null, null, null, targetX, targetY, targetWidth, targetHeight));
													}
												}
												break;
											}
										}
										else if (visionResult.Type == "ready")
										{
											workerReady = true;
											lastResult = Arguments.Now;
										}
										else if (visionResult.Type == "error")
										{
											Fail("Face tracker: " + visionResult.Text);
										}
										else if (visionResult.Type == "voice_status" || visionResult.Type == "voice_error")
										{
											ref string reference = ref voice;
											string text5;
											if (!(visionResult.Type == "voice_status") || !tutorialVoice || !(visionResult.Text == "Listening for English commands"))
											{
												if (visionResult.Type == "voice_error")
												{
													text5 = (cameraVoice ? "Microphone unavailable. Choose manually, or check Windows microphone permission and select Read choices and listen again." : "Microphone unavailable. Check Windows microphone permission and your input device, then switch Voice commands off and on to retry.");
												}
												else
												{
													text5 = ((cameraVoice && visionResult.Text == "Listening for English commands") ? "Listening for camera choice" : (visionResult.Text ?? "Microphone off"));
												}
											}
											else
											{
												text5 = ((state == "TutorialChoice") ? "Listening for tutorial choice" : "Listening for tutorial navigation");
											}
											reference = text5;
										}
									}
									else if (voiceIntent.UserStopEpoch == gate.UserStops)
									{
										Voice(voiceIntent.Text, voiceIntent.UserStopEpoch);
									}
								}
								else
								{
									state = "Stopped";
									title = routineDone.Text;
									help = "Resume pointer control when you are ready.";
									gate.Stop();
									lastGeneration = gate.Generation;
								}
							}
							else
							{
								TuningSaved(saved);
							}
						}
						else
						{
							worker = new VisionClient();
							workerFailed = false;
							StartWorker();
						}
					}
					else
					{
						title = notice.Text;
						help = "Review this step, then choose Confirm to continue, or Stop to cancel.";
					}
				}
				else
				{
					Fail(failure.Error);
				}
				return;
			}
			cameraStarting = false;
			cameraRunning = cameraStarted.Success;
			if (cameraStarted.UserStopEpoch == gate.UserStops)
			{
				if (cameraStarted.Success)
				{
					StartCameraSetup();
				}
				else
				{
					Fail(cameraStarted.Error ?? "The camera could not start.");
				}
			}
		}
		else
		{
			cameras = devices.Cameras;
			if (args.Contains("--offer-tutorial") && !tutorial.Completed)
			{
				state = ((tutorial.Page < 0) ? "TutorialChoice" : "Tutorial");
				title = "Would you like a tutorial?";
				help = "Say yes for a guide, or no to start camera setup.";
				cue = "tutorial:0";
			}
			else
			{
				ChooseStartupCamera();
			}
		}
	}

	private void SyncVoice()
	{
		if (!workerReady)
		{
			return;
		}
		bool flag = (voiceSuppressed = speechCommands.Blocked(Arguments.Now));
		(int, bool, bool, bool, int, bool, bool, bool, int) tuple = (gate.UserStops, flag, motion.Settings.HandsFreeVoice, routineVoice, routineVoiceRevision, tutorialVoice, cameraVoice, state == "TutorialChoice", cameras.Length);
		if (voiceKey.HasValue)
		{
			(int, bool, bool, bool, int, bool, bool, bool, int) value = voiceKey.Value;
			(int, bool, bool, bool, int, bool, bool, bool, int) tuple2 = tuple;
			if (value.Item1 == tuple2.Item1 && value.Item2 == tuple2.Item2 && value.Item3 == tuple2.Item3 && value.Item4 == tuple2.Item4 && value.Item5 == tuple2.Item5 && value.Item6 == tuple2.Item6 && value.Item7 == tuple2.Item7 && value.Item8 == tuple2.Item8 && value.Item9 == tuple2.Item9)
			{
				return;
			}
		}
		voiceKey = tuple;
		long num = Interlocked.Increment(ref voiceRevision);
		string[] phase;
		if (tutorialVoice)
		{
			phase = TutorialFlow.Phrases(state == "TutorialChoice");
		}
		else
		{
			phase = (cameraVoice ? CameraVoice.Phrases(cameras.Length) : null);
		}
		string[] phrases = (flag ? Array.Empty<string>() : VoiceControl.Phrases(motion.Settings.HandsFreeVoice, routineVoice, routines.Names, phase));
		if (args.Contains("--no-input"))
		{
			phrases = Array.Empty<string>();
		}
		bool flag2 = !args.Contains("--no-input") && (motion.Settings.HandsFreeVoice || routineVoice || cameraVoice || tutorialVoice);
		worker.Voice(flag2, Array.Empty<string>(), phrases, gate.UserStops, num);
		ref string reference = ref voice;
		string text;
		if (flag2)
		{
			if (flag)
			{
				text = "Speaking guidance; listening resumes after the prompt";
			}
			else if (tutorialVoice)
			{
				text = ((state == "TutorialChoice") ? "Listening for tutorial choice" : "Listening for tutorial navigation");
			}
			else
			{
				text = (cameraVoice ? "Listening for camera choice" : "Listening: say resume or stop the app");
			}
		}
		else
		{
			text = "Microphone off";
		}
		reference = text;
	}

	private void OpenCamera(string id)
	{
		InterruptLaunch();
		if (cameraStarting)
		{
			return;
		}
		fpsStarted = false;
		fps = (frames = 0.0);
		CancelRestLesson();
		cameraVoice = false;
		voiceKey = null;
		lastFailure = null;
		startup.Begin();
		if (cameraRunning)
		{
			gate.Stop();
			lastGeneration = gate.Generation;
		}
		int selectionEpoch = gate.UserStops;
		cameraStarting = true;
		cameraNeedsSetup = true;
		cameraRunning = false;
		active = false;
		calibration = null;
		activeCamera = id;
		state = "Starting";
		title = "Opening your camera";
		Task.Run(async () =>
		{
			try
			{
				if (!args.Contains("--test-source"))
				{
					await capture.Start(id, motion.Settings.TrackingFps);
				}
				else
				{
					capture.StartTestFrames(motion.Settings.TrackingFps);
				}
				Queue(new CameraStarted(Success: true, null, selectionEpoch));
			}
			catch (Exception ex)
			{
				Queue(new CameraStarted(Success: false, (ex is UnauthorizedAccessException || ex.HResult == -2147024891) ? "Windows denied camera access. Run Qadr normally outside the test sandbox, then check camera permissions if needed." : ex.Message, selectionEpoch));
			}
		});
	}

	private void StartCameraSetup()
	{
		cameraNeedsSetup = false;
		string text = ((activeCamera == null) ? null : Storage.DeviceKey(activeCamera));
		personal.Load((text == null) ? null : storage.Load<RestNoiseModel>("learning/" + text + ".json"));
		motion.PersonalModel = personal.Model;
		savedModelSamples = personal.Model.Samples;
		Profile profile = ((text == null) ? null : storage.Load<Profile>("profiles/" + text + ".json")?.Upgrade());
		if ((object)profile == null || !profile.IsValid)
		{
			StartCalibration();
			return;
		}
		motion.SetProfile(profile);
		LoadTunedMovement(profile);
		calibration = null;
		lesson = null;
		active = false;
		returning = true;
		resumeRest.Reset();
		returnUserStops = gate.UserStops;
		state = "Ready";
		cue = "rest";
		title = "Welcome back â€” your movement is saved";
		help = "Rest comfortably. Qadr will recentre without repeating movement calibration.";
	}

	private void SavePersonal(bool force = false)
	{
		if (activeCamera != null && personal.Model.Samples != savedModelSamples && (force || personal.Model.Samples - savedModelSamples >= 30))
		{
			RestNoiseModel model = personal.Model;
			string path = "learning/" + Storage.DeviceKey(activeCamera) + ".json";
			if (storageWrites.Writer.TryWrite(() =>
			{
				storage.Save(path, model);
			}))
			{
				savedModelSamples = model.Samples;
			}
		}
	}

	private void CancelRestLesson()
	{
		if (restLesson)
		{
			personal.Load(restBackup);
			motion.PersonalModel = personal.Model;
			restBackup = null;
			restLesson = false;
		}
	}

	private void StartCalibration()
	{
		InterruptLaunch();
		CancelRestLesson();
		gate.Stop();
		lastGeneration = gate.Generation;
		routines.Cancel();
		pendingRoutine = null;
		ClearMouse();
		lastFailure = null;
		restLesson = false;
		resumeRest.Reset();
		personal.Break();
		lesson = null;
		active = false;
		returning = false;
		motion.Reset();
		trajectory.Clear();
		calibration = new Calibration();
		positionedAt = 0.0;
		progress = 0.0;
		state = "Positioning";
		cue = "position";
		title = "Place your face inside the oval";
		help = "Follow the arrows. You can also use your comfortable position.";
	}

	private void StartRecoveryAlignment()
	{
		if (motion.Profile is null) return;
		alignOnRecovery = true;
		recoveryExpressionSettings = motion.EffectiveSettings;
		recoveryAlignment.Begin(motion.Profile, positioning.FittedObservation, neutralGeometry);
	}

	private void BeginReturn(bool automatic = false, int? expectedUserStops = null)
	{
		CancelRestLesson();
		int num = (automatic ? userStops : (expectedUserStops ?? gate.UserStops));
		if (gate.UserStops != num)
		{
			startup.Cancel();
			return;
		}
		alignOnRecovery = false;
		recoveryAlignment.Reset();
		recoveryExpressionSettings = null;
		returnUserStops = num;
		motion.PersonalModel = personal.Model;
		ClearMouse();
		if (gate.Hotkeys == 0)
		{
			Fail("No global stop shortcut is available. Close conflicting apps and restart Qadr before controlling the pointer.");
			return;
		}
		if (motion.Settings.ClickMethod == "smile" && !motion.Settings.SmileLearned)
		{
			motion.Settings = motion.Settings with
			{
				ClickMethod = "dwell"
			};
		}
		lesson = null;
		lastFailure = null;
		returning = true;
		active = false;
		resumeRest.Reset();
		progress = 0.0;
		state = "Ready";
		cue = "rest";
		title = "Return to your relaxed position";
		help = "Settle in your current comfortable position briefly. Esc stops control everywhere.";
		motion.Reset();
		trajectory.Clear();
	}

	private void StartLesson(string kind)
	{
		CancelRestLesson();
		if (!face)
		{
			title = "Bring your face into view to learn clicks";
			return;
		}
		gate.Stop();
		lastGeneration = gate.Generation;
		ClearMouse();
		active = (returning = false);
		calibration = null;
		trajectory.Clear();
		routines.Cancel();
		lesson = kind;
		clickLearning = new ClickLearning(kind == "eyes", (kind == "smile") ? motion.Settings : motion.Settings with
		{
			SubtleSmile = false
		});
		state = "LearnClick";
		cue = "rest";
		title = "First, relax your face";
		help = "Keep your eyes open and rest naturally for a moment.";
	}

	private void LearnClick(Observation o)
	{
		double value;
		if (lesson == "pucker")
		{
			value = o.Pucker;
		}
		else if (lesson == "scroll")
		{
			value = o.Mouth;
		}
		else if (lesson == "brows")
		{
			value = o.Eyebrow;
		}
		else
		{
			value = ((lesson == "smile") ? o.Smile : Math.Max(o.LeftEye, o.RightEye));
		}
		ClickLessonFeedback clickLessonFeedback = clickLearning.Feed(value, o.Timestamp);
		progress = clickLessonFeedback.Progress;
		if (clickLessonFeedback.Phase == "rest")
		{
			cue = "rest";
			title = "First, relax your face";
			help = "Keep your eyes open and rest naturally for a moment.";
			return;
		}
		string text = lesson;
		bool flag = ((text == "pucker" || text == "scroll") ? true : false);
		ref string reference = ref cue;
		string text2;
		if (flag)
		{
			text2 = lesson;
		}
		else if (lesson == "brows")
		{
			text2 = "brows";
		}
		else
		{
			text2 = ((lesson == "smile") ? "smile" : "eyes");
		}
		reference = text2;
		text = lesson;
		flag = ((text == "pucker" || text == "scroll") ? true : false);
		ref string reference2 = ref title;
		string text3;
		if (flag)
		{
			if (clickLessonFeedback.Phase == "release")
			{
				text3 = "Now relax your mouth";
			}
			else
			{
				text3 = ((lesson == "pucker") ? "Pucker your lips gently" : "Open your mouth comfortably");
			}
		}
		else if (lesson == "brows")
		{
			text3 = ((clickLessonFeedback.Phase == "release") ? "Now relax your eyebrows" : "Raise your eyebrows gently");
		}
		else if (clickLessonFeedback.Phase == "release")
		{
			text3 = "Now relax and reopen your eyes";
		}
		else
		{
			text3 = ((lesson == "smile") ? "Make the smallest comfortable smile" : "Close one eye, or both eyes");
		}
		reference2 = text3;
		help = "Hold gently until the bar fills, then relax. Take as much time as you need.";
		double? threshold = clickLessonFeedback.Threshold;
		if (threshold.HasValue)
		{
			double valueOrDefault = threshold.GetValueOrDefault();
			MotionController motionController = motion;
			Settings settings;
			if (lesson == "pucker")
			{
				settings = motion.Settings with
				{
					PuckerThreshold = Math.Max(0.05, valueOrDefault),
					PuckerRestLevel = clickLessonFeedback.RestLevel,
					PuckerLearned = true,
					PuckerDrag = true
				};
			}
			else if (lesson == "scroll")
			{
				settings = motion.Settings with
				{
					ScrollGestureThreshold = Math.Max(0.05, valueOrDefault),
					ScrollGestureRestLevel = clickLessonFeedback.RestLevel,
					ScrollGestureLearned = true,
					MouthScroll = true
				};
			}
			else if (lesson == "brows")
			{
				settings = motion.Settings with
				{
					EyebrowThreshold = Math.Max(0.05, valueOrDefault),
					EyebrowRestLevel = clickLessonFeedback.RestLevel,
					EyebrowLearned = true,
					EyebrowDoubleClick = true
				};
			}
			else
			{
				settings = ((lesson == "smile") ? motion.Settings with
				{
					SmileThreshold = valueOrDefault,
					SmileRestLevel = clickLessonFeedback.RestLevel,
					SmileLearned = true,
					ClickMethod = "smile"
				} : motion.Settings with
				{
					BlinkThreshold = valueOrDefault,
					EyeRestLevel = clickLessonFeedback.RestLevel,
					EyesLearned = true,
					RightClickMethod = ((motion.Settings.RightClickMethod == "none") ? "either" : motion.Settings.RightClickMethod)
				});
			}
			motionController.Settings = settings.Validate();
			Settings saved = configuredMovement.Apply(motion.Settings);
			storageWrites.Writer.TryWrite(() =>
			{
				storage.Save("settings.json", saved);
			});
			bool num = lesson == "smile" && motion.Settings.RightClickMethod != "none" && !motion.Settings.EyesLearned;
			lesson = null;
			clickLearning = null;
			state = "ClickSetup";
			cue = "click";
			motion.Reset();
			title = "Your click gesture is learned";
			help = "Choose Resume, then try the large practice target.";
			if (num)
			{
				StartLesson("eyes");
			}
			else if (startup.Advance())
			{
				BeginReturn(automatic: true);
			}
		}
	}

	private void ProcessObservation(VisionResult result)
	{
		double num = (lastResult = Arguments.Now);
		lastCapture = result.Captured;
		if (!fpsStarted)
		{
			measuredAt = num;
			fpsStarted = true;
		}
		if (result.Preview != null)
		{
			preview = result.Preview;
		}
		frames++;
		if (num - measuredAt >= 1.0)
		{
			fps = frames / (num - measuredAt);
			frames = 0.0;
			measuredAt = num;
		}
		latency = (num - result.Captured) * 1000.0;
		inferenceMs = Math.Max(0.0, result.InferenceMs);
		Observation observation = (this.observation = Features.Extract(result));
		face = (object)observation != null;
		positioning.Settings = motion.Settings;
		FacePositioning facePositioning = positioning;
		Calibration? calibration = this.calibration;
		position = alignOnRecovery
			? recoveryAlignment.Check(observation, motion.Settings, result.Geometry,
				observation is not null&&RecoveryExpression.Neutral(observation,recoveryExpressionSettings??motion.EffectiveSettings))
			: facePositioning.Check(observation, (calibration != null && calibration.Resting) || state == "Positioning");
		if ((object)observation != null)
		{
			Calibration? calibration2 = this.calibration;
			if ((calibration2 != null && calibration2.Resting) || state == "Positioning")
			{
				guideX = Math.Clamp((observation.FaceX - position.X) / Math.Max(0.05, position.Width / 2.0), -1.0, 1.0);
				guideY = Math.Clamp((observation.FaceY - position.Y) / Math.Max(0.05, position.Height / 2.0), -1.0, 1.0);
				goto IL_02b7;
			}
		}
		if ((object)observation != null)
		{
			Profile profile = motion.Profile;
			if ((object)profile != null)
			{
				double[] array = profile.Signals(observation);
				guideX = Math.Clamp(array[0] / profile.Ranges[0][(!(array[0] < 0.0)) ? 1u : 0u], -1.0, 1.0);
				guideY = Math.Clamp(array[1] / profile.Ranges[1][(!(array[1] < 0.0)) ? 1u : 0u], -1.0, 1.0);
			}
		}
		goto IL_02b7;
		IL_02b7:
		bool flag = quality.Accept(observation, num);
		if (launchAssessmentRunning && !flag)
		{
			tuningQualityValid = false;
		}
		NativeInput.Point cursor = NativeInput.Cursor;
		if (this.movementRecorder.Recording)
		{
			MovementRecorder movementRecorder = this.movementRecorder;
			long sequence = result.Sequence;
			string text = state;
			bool? qualityAccepted = flag;
			object reason;
			if (flag)
			{
				reason = null;
			}
			else if ((object)observation == null)
			{
				reason = "face unavailable";
			}
			else if (num - observation.Timestamp > 0.2)
			{
				reason = "stale frame";
			}
			else
			{
				reason = ((num < observation.Timestamp) ? "future frame" : "stabilizing or discontinuity");
			}
			double? cursorX = cursor.X;
			double? cursorY = cursor.Y;
			double? processingMs = Math.Max(0.0, latency);
			double? num2 = inferenceMs;
			double? captureFps = capture.ActualFps;
			double? trackingFps = fps;
			movementRecorder.Add(new MovementMeasurement("frame", num, sequence, text, null, observation, qualityAccepted, (string?)reason, null, cursorX, cursorY, null, null, null, processingMs, num2, captureFps, trackingFps));
		}
		RecordAssessment(assessment?.Feed(num, cursor.X, cursor.Y, flag && active && !returning && !gate.Stopped && state == "Tracking"));
		if (!flag)
		{
			if(alignOnRecovery)recoveryAlignment.BreakHold();
			dwell = 0.0;
			ClearMouse();
			motion.Reset();
			trajectory.Clear();
			resumeRest.Reset();
			positionedAt = 0.0;
			this.calibration?.Lost();
			clickLearning?.Lost();
			if (restLesson)
			{
				restLessonStarted = 0.0;
			}
			if ((object)observation == null)
			{
				distance = "Bring your face into view";
				cue = "position";
			}
			if (active)
			{
				StartRecoveryAlignment();
				centreOnReturn = true;
				active = false;
				returning = true;
				resumeRest.Reset();
				state = "Degraded";
				title = "Tracking paused â€” your calibration is kept";
				help = "Bring your whole face back into view and rest briefly.";
			}
			personal.Break();
			resumeRest.Reset();
			return;
		}
		if ((object)observation == null)
		{
			return;
		}
		if (positioning.Revision != fittedRevision)
		{
			fittedRevision = positioning.Revision;
			if (!active && this.calibration == null && (object)motion.Profile != null)
			{
				Observation fittedObservation = positioning.FittedObservation;
				if ((object)fittedObservation != null)
				{
					motion.Rebase(fittedObservation);
					ClearMouse();
					trajectory.Clear();
					resumeRest.Reset();
					if (active || returning)
					{
						active = false;
						returning = true;
						state = "Ready";
						cue = "rest";
						title = "Rest in your updated comfortable position";
						help = "The face guide and movement now share the same tracked position.";
					}
				}
			}
		}
		distance = position.Hint;
		if (restLesson)
		{
			Profile profile2 = motion.Profile;
			if ((object)profile2 != null)
			{
				if (!(observation.Smile < motion.Settings.SmileThreshold) || !(observation.Mouth < motion.Settings.MouthThreshold) || !(Math.Max(observation.LeftEye, observation.RightEye) < motion.Settings.BlinkThreshold) || (motion.Settings.PuckerDrag && motion.Settings.PuckerLearned && !(observation.Pucker < motion.Settings.PuckerThreshold)) || (motion.Settings.MouthScroll && motion.Settings.ScrollGestureLearned && !(observation.Mouth < motion.Settings.ScrollGestureThreshold)) || (motion.Settings.EyebrowDoubleClick && motion.Settings.EyebrowLearned && !(observation.Eyebrow < motion.Settings.EyebrowThreshold)))
				{
					restLessonStarted = 0.0;
					personal.Break();
					progress = 0.0;
					return;
				}
				if (restLessonStarted == 0.0)
				{
					restLessonStarted = num;
				}
				personal.Feed(observation, profile2, labeledRest: true);
				progress = Math.Clamp((num - restLessonStarted) / 4.0, 0.0, 1.0);
				if (progress >= 1.0 && personal.Model.Samples - restLessonSamples >= 60)
				{
					restLesson = false;
					restBackup = null;
					motion.PersonalModel = personal.Model;
					SavePersonal(force: true);
					state = "Stopped";
					title = "Resting model saved on this PC";
					help = "Choose Resume to try your personalized movement. Preferences can switch it off or reset it.";
				}
				return;
			}
		}
		if (lesson != null)
		{
			LearnClick(observation);
			return;
		}
		if (this.calibration == null && (active || returning) && !position.Ready)
		{
			if (active)
			{
				StartRecoveryAlignment();
				centreOnReturn = true;
			}
			ClearMouse();
			active = false;
			returning = true;
			resumeRest.Reset();
			trajectory.Clear();
			motion.Reset();
			state = "Degraded";
			cue = "position";
			title = position.Hint;
			help = alignOnRecovery?position.Hint:"The guide adapts while you rest. Control waits until tracking is stable.";
			return;
		}
		if (this.calibration != null)
		{
			if (!position.Ready)
			{
				positionedAt = 0.0;
				this.calibration.Lost();
				state = "Positioning";
				cue = "position";
				title = position.Hint;
				help = "Use the oval and arrow, or choose Use my comfortable position.";
				return;
			}
			if (positionedAt == 0.0)
			{
				positionedAt = num;
			}
			if (num - positionedAt < 0.65)
			{
				return;
			}
			state = "Calibrating";
			CalibrationFeedback calibrationFeedback = this.calibration.Feed(observation);
			title = calibrationFeedback.Title;
			help = calibrationFeedback.Help;
			progress = calibrationFeedback.Progress;
			ref string reference = ref cue;
			string text2;
			if (this.calibration.Resting)
			{
				text2 = "rest";
			}
			else
			{
				text2 = (calibrationFeedback.Help.Contains("up or down") ? "vertical" : "sides");
			}
			reference = text2;
			guideX = calibrationFeedback.X;
			guideY = calibrationFeedback.Y;
			if ((object)calibrationFeedback.Profile == null)
			{
				return;
			}
			motion.SetProfile(calibrationFeedback.Profile);
			LoadTunedMovement(calibrationFeedback.Profile);
			if (activeCamera != null)
			{
				string key = "profiles/" + Storage.DeviceKey(activeCamera) + ".json";
				Profile profile3 = calibrationFeedback.Profile;
				storageWrites.Writer.TryWrite(() =>
				{
					storage.Save(key, profile3);
				});
			}
			motion.Settings = (motion.Settings with
			{
				MouthThreshold = Math.Max(0.3, this.calibration.MouthRest + 0.2),
				MouthRestLevel = this.calibration.MouthRest
			}).Validate();
			Settings clickSettings = configuredMovement.Apply(motion.Settings);
			storageWrites.Writer.TryWrite(() =>
			{
				storage.Save("settings.json", clickSettings);
			});
			this.calibration = null;
			state = "ClickSetup";
			cue = "click";
			title = "Your movement is learned";
			help = "Choose your left- and right-click methods, then Resume and try the large practice target.";
			if (startup.Advance())
			{
				BeginReturn(automatic: true);
			}
			return;
		}
		if (returning)
		{
			Profile profile4 = motion.Profile;
			if ((object)profile4 != null)
			{
				if(!RecoveryExpression.Neutral(observation,recoveryExpressionSettings??motion.EffectiveSettings))
				{
					resumeRest.Reset();progress=0;cue="rest";
					title="Relax your click gestures before control returns";
					help="Open your eyes and relax your smile or mouth gesture. Esc keeps control stopped.";
					return;
				}
				bool flag2 = resumeRest.Feed(observation, profile4);
				progress = resumeRest.Progress;
				cue = "rest";
				title = "Settle comfortably to start";
				if(alignOnRecovery&&position.Code=="adopt")title=position.Hint;
				help = "Keep a comfortable position briefly. You do not need to match the old centre or force your eyes open.";
				if (!flag2)
				{
					return;
				}
				if (!gate.RequestResume(returnUserStops))
				{
					startup.Cancel();
					return;
				}
				Observation restingCentre = resumeRest.Centre();
				motion.Rebase(restingCentre);
				positioning.UseComfortablePosition(restingCentre);
				neutralGeometry = result.Geometry;
				alignOnRecovery = false;
				recoveryAlignment.Reset();
				motion.SetExpressionRest(resumeRest.Samples);
				recoveryExpressionSettings = null;
				MovementRecorder movementRecorder2 = this.movementRecorder;
				long sequence2 = 0L;
				Profile profile5 = motion.Profile;
				Settings effectiveSettings = motion.EffectiveSettings;
				movementRecorder2.Add(new MovementMeasurement("recentre", num, sequence2, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, profile5, effectiveSettings));
				if (motion.Settings.PersonalizeMovement)
				{
					foreach (Observation sample in resumeRest.Samples)
					{
						personal.Feed(sample, motion.Profile, labeledRest: true);
					}
					SavePersonal();
				}
				startupPointer.Arm(returnUserStops, centreOnReturn && !assessmentSession);
				centreOnReturn = false;
				resumeRest.Reset();
				startup.Started();
				returning = false;
				active = true;
				state = "Tracking";
				cue = "control";
				title = "Your movement. Your control.";
				ref string reference2 = ref help;
				string text3;
				if (motion.Settings.Mapping == "rate")
				{
					text3 = "Hold a small head turn to move; return to your relaxed centre to stop. Esc stops control.";
				}
				else
				{
					text3 = ((motion.Settings.Mapping == "absolute") ? "Point with your head; your relaxed position is the screen centre. Say \"center pointer\" or use Recentre if your posture changes. Esc stops control." : "Move your head to guide the pointer, then settle to stop. Use Recentre if your resting position changes. Esc stops control.");
				}
				reference2 = text3;
				motion.Reset();
				trajectory.Clear();
				return;
			}
		}
		if (!active || gate.Stopped || state == "Routine" || startupPointer.Pending || (launchCheck.Required && !launchAssessmentRunning))
		{
			return;
		}
		(int, int, int, int) desktop = NativeInput.Desktop;
		(double, double, double, double) valueTuple;
		if (launchAssessmentRunning)
		{
			MouseZone mouseZone = launchCanvas;
			if ((object)mouseZone != null)
			{
				valueTuple = (mouseZone.X, mouseZone.Y, mouseZone.Width, mouseZone.Height);
				goto IL_0f3b;
			}
		}
		valueTuple = (desktop.Item1, desktop.Item2, desktop.Item3, desktop.Item4);
		goto IL_0f3b;
		IL_0f3b:
		(double, double, double, double) tuple = valueTuple;
		motion.Width = desktop.Item3;
		motion.Height = desktop.Item4;
		NativeInput.Point cursor2 = NativeInput.Cursor;
		(double, double, double, double) tuple3;
		if (!launchAssessmentRunning || (object)launchCanvas == null)
		{
			(int, int, int, int) tuple2 = ControlMonitor();
			tuple3 = (tuple2.Item1, tuple2.Item2, tuple2.Item3, tuple2.Item4);
		}
		else
		{
			tuple3 = tuple;
		}
		(double, double, double, double) tuple4 = tuple3;
		motion.ControlArea = (tuple4.Item1, tuple4.Item2, tuple4.Item3, tuple4.Item4);
		PointerOutput pointerOutput = motion.Feed(observation, cursor2.X, cursor2.Y, headScroll.Active);
		dwell = pointerOutput.Dwell;
		if (this.movementRecorder.Recording)
		{
			if ((object)recordedProfile != motion.Profile || (object)recordedMovementSettings != motion.EffectiveSettings)
			{
				recordedProfile = motion.Profile;
				recordedMovementSettings = motion.EffectiveSettings;
				MovementRecorder movementRecorder3 = this.movementRecorder;
				long sequence3 = 0L;
				Profile profile5 = recordedProfile;
				Settings effectiveSettings = recordedMovementSettings;
				movementRecorder3.Add(new MovementMeasurement("configuration", num, sequence3, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, profile5, effectiveSettings));
			}
			MovementRecorder movementRecorder4 = this.movementRecorder;
			long sequence4 = result.Sequence;
			string text4 = state;
			PointerOutput output = pointerOutput;
			double? trackingFps = cursor2.X;
			double? captureFps = cursor2.Y;
			movementRecorder4.Add(new MovementMeasurement("control", num, sequence4, text4, null, null, null, null, output, trackingFps, captureFps));
		}
		if (pointerOutput.DistanceChanged)
		{
			ClearMouse();
			trajectory.Clear();
			distance = "Adjusting to your new distance â€” rest briefly";
			cue = "rest";
			return;
		}
		cue = "control";
		if (num > clickFeedbackUntil)
		{
			ref string reference3 = ref clickFeedback;
			string text5;
			if (motion.RightProgress > 0.0)
			{
				text5 = "Keep closed, then reopen";
			}
			else if (motion.Settings.EyebrowDoubleClick && motion.Settings.EyebrowLearned)
			{
				text5 = "Raise eyebrows, hold briefly, then relax to double-click";
			}
			else
			{
				text5 = ((motion.Settings.ClickMethod == "dwell") ? "Rest on your target to left-click" : "Use your learned left-click gesture");
			}
			reference3 = text5;
		}
		if (pointerOutput.Click == "scroll-toggle")
		{
			if (mouse.Dragging)
			{
				clickFeedback = "Drop your item before starting scroll mode";
				clickFeedbackUntil = num + 2.0;
				return;
			}
			bool flag3 = headScroll.Active;
			ClearMouse();
			trajectory.Reset(cursor2.X, cursor2.Y);
			if (!flag3)
			{
				Profile profile6 = motion.Profile;
				if ((object)profile6 != null)
				{
					headScroll.Start(observation, profile6, cursor2.X, cursor2.Y);
				}
			}
			clickFeedback = (flag3 ? "Scroll mode off" : "Scroll mode on â€” move head up/down; repeat mouth gesture to exit");
			clickFeedbackUntil = num + 2.0;
			return;
		}
		if (headScroll.Active)
		{
			trajectory.Reset(cursor2.X, cursor2.Y);
			if (Math.Abs((double)cursor2.X - headScroll.AnchorX) > 8.0 || Math.Abs((double)cursor2.Y - headScroll.AnchorY) > 8.0)
			{
				ClearMouse();
				return;
			}
			if (pointerOutput.Click == "drag")
			{
				ClearMouse();
				StartMouse("drag", pointerOutput.ClickX, pointerOutput.ClickY);
				return;
			}
			Profile profile7 = motion.Profile;
			if ((object)profile7 != null && mouseEvents.Count == 0)
			{
				int num3 = headScroll.Feed(observation, profile7, motion.Settings, pointerOutput.Frozen);
				if (num3 != 0)
				{
					mouse.Select((num3 > 0) ? "scroll-up" : "scroll-down");
					StartMouse("left", cursor2.X, cursor2.Y);
				}
			}
			return;
		}
		if (pointerOutput.Frozen || mouseEvents.Count > 0)
		{
			trajectory.Reset(cursor2.X, cursor2.Y);
		}
		else if (pointerOutput.IsAbsolute)
		{
			trajectory.SetAbsolute(pointerOutput.AbsoluteX, pointerOutput.AbsoluteY, cursor2.X, cursor2.Y, tuple4.Item1, tuple4.Item2, tuple4.Item3, tuple4.Item4, observation.Timestamp);
		}
		else if (pointerOutput.IsRate)
		{
			trajectory.SetRate(pointerOutput.VelocityX, pointerOutput.VelocityY, cursor2.X, cursor2.Y, tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, observation.Timestamp);
		}
		else
		{
			trajectory.Add(pointerOutput.Dx, pointerOutput.Dy, cursor2.X, cursor2.Y, tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, pointerOutput.StopX, pointerOutput.StopY, observation.Timestamp);
		}
		MouseZone[] array2 = mouseZones.Where((MouseZone z) => (double)cursor2.X >= z.X && (double)cursor2.X < z.X + z.Width && (double)cursor2.Y >= z.Y && (double)cursor2.Y < z.Y + z.Height).ToArray();
		MouseZone mouseZone2 = ((array2.Length == 0 || shell == null) ? null : array2.FirstOrDefault((MouseZone z) => z.Window == NativeInput.WindowAt(cursor2) && NativeInput.WindowOwner(z.Window) == shell.Id));
		if ((object)mouseZone2 == null)
		{
			latchedZone = null;
		}
		if (pointerOutput.Click != null && (object)mouseZone2 != null)
		{
			scrollRepeating = false;
			if (latchedZone != mouseZone2.Id)
			{
				string text6 = mouseZone2.Id.Split(':').Last();
				if (text6 == "stop")
				{
					gate.UserStop();
				}
				else if (text6 == "toggle")
				{
					dockToggleSequence++;
				}
				else
				{
					SelectMouse(text6);
				}
				latchedZone = mouseZone2.Id;
			}
		}
		else
		{
			if (pointerOutput.Click != null && mouseEvents.Count == 0)
			{
				StartMouse(pointerOutput.Click, pointerOutput.ClickX, pointerOutput.ClickY);
			}
			else if ((object)mouseZone2 == null && scrollRepeating && mouse.Scrolling && motion.HoldingClick && motion.RightProgress == 0.0 && num - lastWheel >= motion.Settings.ScrollRepeatSeconds)
			{
				StartMouse("left", cursor2.X, cursor2.Y);
			}
			if (!motion.HoldingClick || (object)mouseZone2 != null)
			{
				scrollRepeating = false;
			}
		}
	}

	private void ClearMouse()
	{
		headScroll.Reset();
		bool flag = mouse.Dragging || mouseEvents.Count > 0 || (state != "Routine" && gate.MouseButtons != 0);
		mouseEvents.Clear();
		scrollRepeating = false;
		latchedZone = null;
		if (!args.Contains("--no-input") & flag)
		{
			NativeInput.ReleaseOwned(gate);
		}
		mouse.Reset();
	}

	private void SelectMouse(string mode)
	{
		headScroll.Reset();
		if (!(mode != "release") || MouseActions.Modes.Any((MouseModeDefinition m) => m.Id == mode))
		{
			ClearMouse();
			if (mode != "release")
			{
				mouse.Select(mode);
			}
			clickFeedback = ((mode == "release") ? "Released â€” left click" : mouse.Help);
			clickFeedbackUntil = Arguments.Now + 2.0;
		}
	}

	private void StartMouse(string source, double x, double y)
	{
		MovementRecorder movementRecorder = this.movementRecorder;
		double now = Arguments.Now;
		long sequence = lastSequence;
		string text = state;
		double? cursorX = x;
		double? cursorY = y;
		movementRecorder.Add(new MovementMeasurement("gesture", now, sequence, text, source, null, null, null, null, cursorX, cursorY));
		MousePlan mousePlan = mouse.Trigger(source, motion.Settings.ScrollNotches, Math.Min(0.06, (double)NativeInput.GetDoubleClickTime() / 4000.0));
		scrollRepeating = mouse.Scrolling;
		lastWheel = Arguments.Now;
		mouseX = x;
		mouseY = y;
		mouseFeedback = mousePlan.Feedback;
		trajectory.Reset(x, y);
		if (args.Contains("--no-input"))
		{
			clickFeedback = "Detected: " + mousePlan.Feedback + " (preview only)";
			clickFeedbackUntil = Arguments.Now + 1.3;
			return;
		}
		MouseStep[] steps = mousePlan.Steps;
		foreach (MouseStep mouseStep in steps)
		{
			mouseEvents.Enqueue((Arguments.Now + mouseStep.Delay, mouseStep));
		}
		PumpMouse();
	}

	private void PumpMouse()
	{
		if (mouseEvents.Count == 0)
		{
			return;
		}
		(double, MouseStep) result;
		while (mouseEvents.TryPeek(out result) && result.Item1 <= Arguments.Now)
		{
			if (gate.Stopped)
			{
				ClearMouse();
				return;
			}
			mouseEvents.Dequeue();
			MouseStep item = result.Item2;
			bool flag = NativeInput.Move(gate, mouseX, mouseY);
			if (flag)
			{
				flag = item.Kind switch
				{
					"down" => NativeInput.Button(gate, item.Button, down: true), 
					"up" => NativeInput.Button(gate, item.Button, down: false), 
					"vertical" => NativeInput.Scroll(gate, item.Amount), 
					"horizontal" => NativeInput.ScrollHorizontal(gate, item.Amount), 
					_ => false, 
				};
			}
			bool flag2 = flag;
			MovementRecorder movementRecorder = this.movementRecorder;
			double now = Arguments.Now;
			long sequence = lastSequence;
			string text = state;
			string label = item.Kind + ":" + item.Button;
			double? requestedX = mouseX;
			double? requestedY = mouseY;
			bool? nativeAccepted = flag2;
			movementRecorder.Add(new MovementMeasurement("mouse-action", now, sequence, text, label, null, null, null, null, null, null, requestedX, requestedY, nativeAccepted));
			if (!flag2)
			{
				ClearMouse();
				Fail("Windows could not send the mouse action. Control has stopped. Elevated apps can block input.");
				return;
			}
		}
		if (mouseEvents.Count == 0)
		{
			clickFeedback = mouseFeedback + " sent âœ“";
			clickFeedbackUntil = Arguments.Now + 1.3;
		}
	}

	private void Voice(string text, int epoch)
	{
		if (speechCommands.Blocked(Arguments.Now))
		{
			return;
		}
		text = VoiceControl.Normalize(text);
		bool flag;
		switch (text)
		{
		case "undo movement settings":
			Handle(new Command("movementTuningUndo", null, null, null, Enabled: false, 1, null, epoch));
			return;
		case "start motion test":
			StartMovementRecording();
			return;
		case "save motion test":
			if (launchAssessmentRunning)
			{
				InterruptLaunch();
				gate.UserStop();
			}
			else
			{
				EndAssessment("recording saved", save: true);
			}
			return;
		case "start pointing test":
			assessmentRequest = (launchCheck.Required ? "launch" : "start");
			assessmentRequestSequence++;
			return;
		case "skip target":
			EndAssessment("skipped", save: false);
			return;
		case "save pointing test":
			if (launchAssessmentRunning)
			{
				InterruptLaunch();
				gate.UserStop();
			}
			else
			{
				EndAssessment("test ended", save: true);
				assessmentRequest = "finish";
				assessmentRequestSequence++;
			}
			return;
		case "center pointer":
			Handle(new Command("recentre", null, null, null, Enabled: false, 1, null, epoch));
			return;
		case "mark rest":
		case "mark movement":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			movementRecorder.Add(new MovementMeasurement("label", Arguments.Now, 0L, null, (text == "mark rest") ? "rest" : "movement"));
			return;
		}
		if (VoiceControl.IsStop(text))
		{
			gate.UserStop();
			return;
		}
		if (VoiceControl.IsResume(text))
		{
			string text2 = state;
			if ((text2 == "TutorialChoice" || text2 == "Tutorial") ? true : false)
			{
				EndTutorial();
			}
			else
			{
				Handle(new Command("resume", null, null, null, Enabled: false, 1, null, epoch));
			}
			return;
		}
		flag = tutorialVoice;
		if (flag)
		{
			string text2 = state;
			bool flag2 = ((text2 == "TutorialChoice" || text2 == "Tutorial") ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			string text3 = TutorialFlow.Resolve(text, state == "TutorialChoice");
			if (text3 != null)
			{
				Handle(new Command(text3, null, null, null, Enabled: false, 1, null, epoch));
			}
			return;
		}
		if (cameraVoice)
		{
			int num = CameraVoice.Resolve(text, cameras.Length);
			if (state == "ChooseCamera" && num >= 0)
			{
				OpenCamera(cameras[num].Id);
			}
			return;
		}
		switch (text)
		{
		case "computer pause pointer":
			gate.Stop();
			return;
		case "computer resume pointer":
			Handle(new Command("resume", null, null, null, Enabled: false, 1, null, epoch));
			return;
		case "computer confirm":
			Handle(new Command("confirm", null, null, null, Enabled: false, 1, null, epoch));
			return;
		case "computer stop recording":
			Handle(new Command("saveRecording"));
			return;
		case "computer start recording":
			Handle(new Command("record", null, null, recordingDraft, recordingText, 1, null, epoch));
			return;
		}
		if (text.StartsWith("computer run "))
		{
			pendingRoutine = text.Substring(13);
			pendingUntil = Arguments.Now + 10.0;
			title = "Confirm routine: " + pendingRoutine;
			help = "Say computer confirm within ten seconds, or choose Confirm.";
		}
	}

	private void ChooseStartupCamera()
	{
		if (cameras.Length == 1)
		{
			OpenCamera(cameras[0].Id);
			return;
		}
		if (cameras.Length == 0)
		{
			Fail("No camera was found. Connect a camera and choose Refresh cameras.");
			return;
		}
		state = "ChooseCamera";
		cue = "position";
		title = "Choose your camera";
		help = "Select the camera that has the clearest view of your face.";
	}

	private void ShowTutorialPage()
	{
		state = "Tutorial";
		cue = "tutorial:" + tutorial.Page;
		TutorialPage tutorialPage = TutorialFlow.Pages[tutorial.Page];
		title = tutorialPage.Title;
		help = tutorialPage.Instruction;
		progress = ((double)tutorial.Page + 1.0) / (double)TutorialFlow.Pages.Length;
	}

	private void EndTutorial()
	{
		tutorial.Complete();
		tutorialVoice = false;
		voiceKey = null;
		ChooseStartupCamera();
	}

	private void Fail(string error)
	{
		startupPointer.Cancel();
		centreOnReturn = false;
		InterruptLaunch();
		CancelRestLesson();
		startup.Cancel();
		tutorial.Complete();
		tutorialVoice = (cameraVoice = false);
		voiceKey = null;
		lastFailure = error;
		gate.Stop();
		active = (returning = false);
		calibration = null;
		state = "Stopped";
		title = "Qadr needs your attention";
		help = error;
	}

	private void RecordAssessment(AssessmentResult? result)
	{
		if ((object)result != null)
		{
			assessmentResult = result;
			assessment = null;
			if (launchAssessmentRunning)
			{
				RecordTuningResult(result);
				launchCheck.Record(result);
			}
			movementRecorder.Add(new MovementMeasurement("assessment-result", Arguments.Now, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, result));
		}
	}

	private void EndAssessment(string reason, bool save)
	{
		if (assessment != null)
		{
			RecordAssessment(assessment.Cancel(Arguments.Now, reason));
		}
		assessment = null;
		if (reason != "skipped")
		{
			assessmentSession = false;
		}
		if (save)
		{
			movementRecorder.Stop();
		}
	}

	private void StartMovementRecording()
	{
		if (this.movementRecorder.Start(Arguments.Now, args.Contains("--no-input") || args.Contains("--test-source")))
		{
			recordedState = null;
			recordedProfile = null;
			recordedMovementSettings = null;
			MovementRecorder movementRecorder = this.movementRecorder;
			double now = Arguments.Now;
			long sequence = 0L;
			Settings settings = motion.Settings;
			movementRecorder.Add(new MovementMeasurement("configuration", now, sequence, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, settings));
		}
	}

	private void Publish()
	{
		preview = Volatile.Read(in latestPreview) ?? preview;
		string text = state;
		string text2 = title;
		string text3 = help;
		double num = progress;
		double motionX = guideX;
		double motionY = guideY;
		bool flag = face;
		string text4 = distance;
		double num2 = fps;
		double latencyMs = latency;
		string stopShortcut;
		if (gate.Hotkeys == 3)
		{
			stopShortcut = "Esc or Ctrl+Alt+Esc";
		}
		else if (gate.Hotkeys == 1)
		{
			stopShortcut = "Esc";
		}
		else
		{
			stopShortcut = ((gate.Hotkeys == 2) ? "Ctrl+Alt+Esc (Esc is unavailable)" : "No stop shortcut is available â€” control is blocked");
		}
		CameraDevice[] array = cameras;
		string? text5 = preview;
		string[] names = routines.Names;
		string text6 = voice;
		string? pending = pendingRoutine;
		double num3 = dwell;
		Settings settings = motion.Settings;
		FacePosition facePosition = position;
		string text7 = cue;
		double rightProgress = motion.RightProgress;
		string text8 = clickFeedback;
		double leftEye = Math.Max(0.0, this.observation?.LeftEye ?? 0.0);
		double rightEye = Math.Max(0.0, this.observation?.RightEye ?? 0.0);
		string mode = mouse.Mode;
		bool dragging = mouse.Dragging;
		string mouseHelp = (headScroll.Active ? "Move head up/down to scroll; repeat the mouth gesture to exit" : mouse.Help);
		Observation observation = this.observation;
		FacePosition detectedFace = (((object)observation != null) ? new FacePosition("face", "Detected face", Ready: true, observation.FaceX, observation.FaceY, observation.FaceWidth, observation.FaceHeight) : null);
		long num4 = dockToggleSequence;
		bool headScrolling = headScroll.Active;
		bool routineVoiceEnabled = routineVoice;
		double num5 = inferenceMs;
		double actualFps = capture.ActualFps;
		double nominalFps = capture.NominalFps;
		MovementRecordingStatus status = movementRecorder.Status;
		AssessmentProgress assessmentProgress = assessment?.Progress;
		AssessmentResult? assessmentResult = this.assessmentResult;
		long num6 = assessmentRequestSequence;
		string? text9 = assessmentRequest;
		LaunchAssessmentStatus status2 = launchCheck.Status;
		MovementTuningStatus movementTuningStatus = movementTuning;
		EngineStatus engineStatus = new EngineStatus(text, text2, text3, num, motionX, motionY, flag, text4, num2, latencyMs, stopShortcut, array, text5, names, text6, pending, num3, 1, settings, facePosition, text7, rightProgress, text8, leftEye, rightEye, mode, dragging, mouseHelp, detectedFace, num4, headScrolling, routineVoiceEnabled, num5, actualFps, nominalFps, status, assessmentProgress, assessmentResult, num6, text9, status2, ((object)movementTuningStatus != null) ? movementTuningStatus with
		{
			UndoAvailable = ((object)cameraTuning != null)
		} : null, manualAdjustments.Status(motion.Settings));
		if (gate.SharedRpc != 0)
		{
			shared.Publish(engineStatus);
		}
		else
		{
			statuses.Writer.TryWrite(engineStatus);
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			cancellation.Cancel();
			gate.Stop();
			movementRecorder.Dispose();
			routines.Dispose();
			worker.Dispose();
			capture.DisposeAsync().AsTask().Wait(2000);
			gate.Closing = 1;
			shared.Dispose();
			gate.Dispose();
			shell?.Dispose();
			cancellation.Dispose();
		}
	}
}
