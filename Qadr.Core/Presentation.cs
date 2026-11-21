namespace Qadr.Core;

/// <summary>One source for displayed and narrated setup instructions. No invented
/// lighting score: the tracker currently measures geometry and freshness only.</summary>
public sealed record SetupPresentation(bool Setup,int Step,string Heading,string Instruction,string FaceState,string Checks)
{
    public static SetupPresentation From(EngineStatus s){
        bool setup=s.State is "Starting" or "TutorialChoice" or "Tutorial" or "ChooseCamera" or "Positioning" or "Calibrating" or "LearnClick" or "LearnRest" or "Ready" or "ClickSetup";
        int step=s.State=="LearnClick"?3:s.State=="Ready"?4:s.State is "Calibrating" or "ClickSetup"?2:1;
        var p=s.Position;
        string faceState=!s.Face?"No face":p?.Ready==true?"Locked":p?.Code=="adapting"?"Fitting":"Found";
        string instruction=s.State switch{
            "Starting"=>"Wait while your camera opens",
            "LearnRest"=>"Rest naturally while Qadr learns your steadiness",
            "ChooseCamera"=>"Choose your camera by voice or manually",
            "Positioning"=>p?.Code switch{
                "find"=>"Bring your face into view",
                "closer"=>"Bring the camera a little closer",
                "farther"=>"Move the camera a little farther away",
                "frame"=>"Keep your whole face in view",
                "right"=>"Move gently right →","left"=>"Move gently left ←",
                "up"=>"Move gently up ↑","down"=>"Move gently down ↓",
                "angle"=>"Return to your comfortable angle",
                _=>"Hold comfortably while the guide fits"},
            "Calibrating"=>s.Cue=="rest"?"Relax and hold comfortably":s.Cue=="vertical"?"Look gently up and down ↑ ↓":"Turn gently left and right ← →",
            "LearnClick"=>s.Cue is "pucker" or "scroll"?(s.Title.StartsWith("Now")?"Relax your mouth":s.Cue=="pucker"?"Pucker gently, hold, then relax":"Open your mouth comfortably, hold, then relax"):s.Cue=="brows"?(s.Title.StartsWith("Now")?"Relax your eyebrows":"Raise your eyebrows gently and hold"):s.Cue=="rest"?"Relax your face and keep your eyes open":s.Title.StartsWith("Now")?"Relax and reopen your eyes":s.Cue=="smile"?"Smile gently, hold, then relax":"Close one or both eyes, hold, then reopen",
            "Ready"=>"Settle comfortably; control starts automatically",
            "Tracking"=>"Control on · Esc stops the mouse",
            "Lost" or "Degraded"=>"Tracking paused · return your face to view",
            "Stopped"=>"Control stopped · choose Resume when ready",
            _=>s.Title};
        string heading=setup?step switch{1=>"Position your face",2=>"Learn your comfortable movement",3=>"Learn your click gesture",_=>"Return to rest"}:s.State=="Tracking"?"Control on":s.State is "Lost" or "Degraded"?"Tracking paused":"Control stopped";
        string geometry=!s.Face?"Framing · bring your face into view":p?.Code=="frame"?"Framing · keep your whole face visible":"Framing · face detected";
        string distance=!s.Face?"Distance · waiting for geometry":p?.Code is "closer" or "farther"?"Distance · adjust camera":"Distance · "+(p?.Code=="adapting"?"fitting":"supported view");
        string stability="Steadiness · "+(faceState=="Locked"?"position learned":faceState=="Fitting"?"hold comfortably":"waiting for a stable position");
        return new(setup,step,heading,instruction,faceState,$"Face · {faceState}\n\n{geometry}\n\n{distance}\n\n{stability}\n\nTracking · {s.State}");
    }
}
