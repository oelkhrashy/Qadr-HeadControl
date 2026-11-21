using System.IO.Compression;
using System.Threading.Channels;

namespace Qadr.Core;

/// <summary>Numeric diagnostic measurements only. No frames, audio, text input,
/// window titles, camera identifiers or routine content are accepted by this API.</summary>
public sealed record MovementMeasurement(string Kind,double Time,long Sequence=0,string? State=null,string? Label=null,
    Observation? Face=null,bool? QualityAccepted=null,string? Reason=null,PointerOutput? Output=null,
    double? CursorX=null,double? CursorY=null,double? RequestedX=null,double? RequestedY=null,bool? NativeAccepted=null,
    double? ProcessingMs=null,double? InferenceMs=null,double? CaptureFps=null,double? TrackingFps=null,
    Profile? Profile=null,Settings? MovementSettings=null,double? TargetX=null,double? TargetY=null,double? TargetWidth=null,double? TargetHeight=null,AssessmentResult? AssessmentResult=null,MovementTuningStatus? Tuning=null);

public sealed record MovementRecordingStatus(bool Recording=false,bool Saving=false,string? Report=null,string? Error=null,long Dropped=0);

/// <summary>Bounded background writer. The control loop never encodes JSON or
/// waits for disk I/O. Recording is opt in, time/size limited and locally zipped.</summary>
public sealed class MovementRecorder(string directory,int capacity=1024,double maximumSeconds=900,long maximumBytes=32*1024*1024):IDisposable
{
    static readonly System.Text.Json.JsonSerializerOptions TraceJson=new(Protocol.Json){DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    sealed class Session(string stem,double start,bool synthetic){
        public readonly string Stem=stem;
        public readonly double Start=start;
        public readonly bool Synthetic=synthetic;
        public Channel<MovementMeasurement> Queue=null!;
        public Task Work=Task.CompletedTask;
        public long Dropped;
        public bool Limit;
    }
    Session? current;
    MovementRecordingStatus status=new();
    public MovementRecordingStatus Status=>Volatile.Read(ref status) with{Dropped=current is {} s?Interlocked.Read(ref s.Dropped):0};
    public bool Recording=>Volatile.Read(ref status).Recording;
    public string DirectoryPath=>Path.GetFullPath(directory);
    public bool Start(double now,bool synthetic=false){
        if(!double.IsFinite(now)||current is {Work.IsCompleted:false})return false;
        string stem="Qadr-Movement-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6];
        var session=new Session(stem,now,synthetic){Queue=Channel.CreateBounded<MovementMeasurement>(new BoundedChannelOptions(Math.Max(1,capacity)){FullMode=BoundedChannelFullMode.Wait,SingleReader=true})};
        current=session;Volatile.Write(ref status,new(Recording:true));session.Work=Task.Run(()=>Write(session));return true;
    }
    public void Add(MovementMeasurement measurement){
        var s=current;if(s is null||!Recording)return;
        if(!double.IsFinite(measurement.Time)||measurement.Time<s.Start)return;
        if(measurement.Time-s.Start>maximumSeconds){s.Limit=true;Stop();return;}
        if(!s.Queue.Writer.TryWrite(measurement))Interlocked.Increment(ref s.Dropped);
    }
    public void Stop(){var s=current;if(s is null||!Recording)return;Volatile.Write(ref status,new(Saving:true,Dropped:Interlocked.Read(ref s.Dropped)));s.Queue.Writer.TryComplete();}
    public void Tick(double now){if(current is {} s&&Recording&&now-s.Start>maximumSeconds){s.Limit=true;Stop();}}
    public Task Completion=>current?.Work??Task.CompletedTask;
    async Task Write(Session s){
        try{
            Directory.CreateDirectory(DirectoryPath);
            string path=Path.Combine(DirectoryPath,s.Stem+".jsonl");
            long frames=0,accepted=0,pointers=0,nativeFailures=0;double last=s.Start;var processing=new List<double>();var inference=new List<double>();
            var reasons=new Dictionary<string,long>();var actions=new Dictionary<string,long>();var targetTimes=new List<double>();var targetErrors=new List<double>();
            long restN=0;double meanX=0,meanY=0,m2X=0,m2Y=0;double? firstCapture=null,lastCapture=null,targetAt=null;long captures=0,targetHits=0,targetMisses=0;string label="unlabelled";
            var trials=new List<AssessmentResult>();var tuningDecisions=new List<MovementTuningStatus>();
            await using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536,FileOptions.Asynchronous)){
                await using var writer=new StreamWriter(stream);
                string header=Protocol.Encode(new{kind="session",schema=1,version="0.4.28-native-rc",synthetic=s.Synthetic,clock="QPC seconds; relative to sessionStart",sessionStart=s.Start,images=false,audio=false,windowContent=false,limits=new{seconds=maximumSeconds,bytes=maximumBytes},notes="NativeAccepted means Windows accepted a request, not proof of target success. Capture age excludes sensor/driver delay. Target tests require the user to use face control; physical helper input is not distinguishable."});
                long written=System.Text.Encoding.UTF8.GetByteCount(header)+Environment.NewLine.Length;
                await writer.WriteLineAsync(header);
                long flushedAt=Environment.TickCount64;
                await foreach(var row in s.Queue.Reader.ReadAllAsync()){
                    string line=System.Text.Json.JsonSerializer.Serialize(row,TraceJson);int bytes=System.Text.Encoding.UTF8.GetByteCount(line)+Environment.NewLine.Length;
                    if(written+bytes>maximumBytes){s.Limit=true;Volatile.Write(ref status,new(Saving:true));s.Queue.Writer.TryComplete();Interlocked.Increment(ref s.Dropped);while(s.Queue.Reader.TryRead(out _))Interlocked.Increment(ref s.Dropped);break;}
                    written+=bytes;await writer.WriteLineAsync(line);last=Math.Max(last,row.Time);
                    if(row.Kind=="label")label=row.Label??"unlabelled";
                    if(row.Kind=="frame"){
                        frames++;if(row.QualityAccepted==true)accepted++;else{string reason=row.Reason??"not accepted";reasons[reason]=reasons.GetValueOrDefault(reason)+1;}
                        if(row.Face is {} face){if(lastCapture is null||face.Timestamp>lastCapture){firstCapture??=face.Timestamp;lastCapture=face.Timestamp;captures++;}}
                        if(row.ProcessingMs is >=0 and <60000)processing.Add(row.ProcessingMs.Value);
                        if(row.InferenceMs is >=0 and <60000)inference.Add(row.InferenceMs.Value);
                        if(label=="rest"&&row.State=="Tracking"&&row.QualityAccepted==true&&row.CursorX is {} x&&row.CursorY is {} y){restN++;double dx=x-meanX,dy=y-meanY;meanX+=dx/restN;meanY+=dy/restN;m2X+=dx*(x-meanX);m2Y+=dy*(y-meanY);}
                    }
                    if(row.Kind=="pointer")pointers++;
                    if(row.NativeAccepted==false)nativeFailures++;
                    if(row.Kind=="gesture"&&row.Label is {} action)actions[action]=actions.GetValueOrDefault(action)+1;
                    if(row.AssessmentResult is {} result)trials.Add(result);if(row.Tuning is {} decision)tuningDecisions.Add(decision);
                    if(row.Kind=="target")targetAt=row.Time;
                    if(row.Kind=="target-hit"&&row.State=="Tracking"&&row.TargetX is {} tx&&row.TargetY is {} ty&&row.TargetWidth is {} tw&&row.TargetHeight is {} th&&row.CursorX is {} cx&&row.CursorY is {} cy){
                        bool inside=cx>=tx&&cx<=tx+tw&&cy>=ty&&cy<=ty+th;
                        if(inside){targetHits++;targetErrors.Add(Math.Sqrt(Math.Pow(cx-tx-tw/2,2)+Math.Pow(cy-ty-th/2,2)));if(targetAt is {} began)targetTimes.Add((row.Time-began)*1000);}else targetMisses++;
                        targetAt=null;
                    }
                    if(Environment.TickCount64-flushedAt>=1000){await writer.FlushAsync();flushedAt=Environment.TickCount64;}
                }
                await writer.FlushAsync();
            }
            string summary=Path.Combine(DirectoryPath,s.Stem+"-summary.json");
            double? P95(List<double> values)=>values.Count>0?Calibration.Percentile(values,.95):null;
            await File.WriteAllTextAsync(summary,Protocol.Encode(new{schema=1,synthetic=s.Synthetic,durationSeconds=last-s.Start,frames,accepted,rejected=frames-accepted,rejectionReasons=reasons,
                observedTrackingFps=captures>1&&lastCapture>firstCapture?(captures-1)/(lastCapture-firstCapture):null,
                captureToEngineP95Ms=P95(processing),inferenceP95Ms=P95(inference),pointerRequests=pointers,nativeRequestFailures=nativeFailures,gestureRequests=actions,
                labelledRestSamples=restN,labelledRestRmsPx=restN>1?Math.Sqrt((m2X+m2Y)/(restN-1)):(double?)null,targetHits,targetMisses,
                targetCompletionP95Ms=P95(targetTimes),targetCentreErrorP95Px=P95(targetErrors),dropped=Interlocked.Read(ref s.Dropped),limited=s.Limit,
                movementTuning=tuningDecisions,pointingTest=new{trials,completed=trials.Count(t=>t.Completed),skippedOrCancelled=trials.Count(t=>!t.Completed)},
                accuracyMeasured=targetHits>0||trials.Any(t=>t.Completed),notes="Rest jitter is meaningful only for correctly labelled rest. Target results measure this labelled task, not universal accuracy. Timing excludes sensor/driver buffering. No images, audio, window content or identity data. No automatic ML training."}));
            string zip=Path.Combine(DirectoryPath,s.Stem+".zip");
            string temporaryZip=zip+".partial";
            using(var archive=ZipFile.Open(temporaryZip,ZipArchiveMode.Create)){
                archive.CreateEntryFromFile(path,"movement.jsonl",CompressionLevel.Fastest);archive.CreateEntryFromFile(summary,"summary.json",CompressionLevel.Fastest);
                using var instructions=new StreamWriter(archive.CreateEntry("READ_ME.txt").Open());
                instructions.Write("Qadr numeric movement report. Share this ZIP manually with your reviewer. movement.jsonl contains session/configuration, face pose/expression scores, requested pointer movement, sampled actual screen coordinates, tracking/stop states and labelled test targets. summary.json contains measured timings and task metrics. No camera pictures, sound, desktop capture, typed text or routine contents. Positions use physical screen pixels; face pose uses radians and normalized coordinates. NativeAccepted is SendInput acceptance, not application acknowledgement. Absence of labels/targets prevents inferring user intent or accuracy. The file does not train a model automatically.");
            }
            File.Move(temporaryZip,zip);
            Volatile.Write(ref status,new(Report:zip,Dropped:Interlocked.Read(ref s.Dropped)));
        }catch(Exception error){Volatile.Write(ref status,new(Error: error is IOException or UnauthorizedAccessException?"Could not save movement report. Check free space and folder permissions.":"Movement report could not be created.",Dropped:Interlocked.Read(ref s.Dropped)));}
    }
    public void Dispose(){Stop();try{Completion.Wait(TimeSpan.FromSeconds(2));}catch(AggregateException){}}
}
