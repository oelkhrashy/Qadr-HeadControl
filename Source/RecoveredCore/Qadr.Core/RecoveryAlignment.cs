using System;

namespace Qadr.Core;

/// <summary>A recovery-only guide. It never restricts an active pointing stroke.</summary>
public sealed class RecoveryAlignment
{
    private Profile? reference;
    private double faceX,faceY,width=.34,height=.53;
    private double? noseX,noseY;
    private Observation? candidate,previous;
    private FaceGeometry? candidateGeometry;
    private int candidateSamples;
    public const double PostureAdoptionSeconds=4;
    public bool Pending { get; private set; }

    public void Begin(Profile profile,Observation? comfortable=null,FaceGeometry? geometry=null)
    {
        reference=profile with {Neutral=(double[])profile.Neutral.Clone(),Center=(double[])profile.Center.Clone()};
        faceX=comfortable is null?profile.Center[0]:comfortable.FaceX+profile.Center[0]-comfortable.X;
        faceY=comfortable is null?profile.Center[1]:comfortable.FaceY+profile.Center[1]-comfortable.Y;
        width=comfortable?.FaceWidth??.32;height=comfortable?.FaceHeight??.5;
        noseX=noseY=null;
        if(geometry is {IsValid:true})
        {
            noseX=geometry.NoseX+profile.Center[0]-geometry.EyeX;
            noseY=geometry.NoseY+profile.Center[1]-geometry.EyeY;
        }
        Pending=true;
        ClearCandidate();
    }

    private void ClearCandidate(){candidate=previous=null;candidateGeometry=null;candidateSamples=0;}
    public void BreakHold()=>ClearCandidate();
    public void Reset(){Pending=false;reference=null;noseX=noseY=null;ClearCandidate();}

    public FacePosition Check(Observation? o,Settings settings,FaceGeometry? geometry=null)=>Check(o,settings,geometry,true);

    public FacePosition Check(Observation? o,Settings settings,FaceGeometry? geometry,bool expressionNeutral)
    {
        FacePosition Result(string code,string hint,bool ready=false)=>new(code,hint,ready,
            faceX,faceY,Math.Clamp(width*settings.PositionGuidePadding,.1,.95),Math.Clamp(height*settings.PositionGuidePadding,.12,.96));
        FacePosition Block(string code,string hint){ClearCandidate();return Result(code,hint);}
        if(!Pending||reference is null||o is null||!o.IsUsable)return Block("find","Bring your whole face back into the camera view");
        if(o.Scale<settings.PositionMinimumScale||o.FaceHeight<settings.PositionMinimumHeight)return Block("closer","Bring the camera closer so your whole face can be tracked");
        if(o.Scale>settings.PositionMaximumScale||o.FaceHeight>settings.PositionMaximumHeight)return Block("farther","Move the camera farther away so your whole face fits");
        if(o.FaceX-o.FaceWidth/2<.015||o.FaceX+o.FaceWidth/2>.985||o.FaceY-o.FaceHeight/2<.01||o.FaceY+o.FaceHeight/2>.99)
            return Block("frame","Keep your whole face inside the camera view before control returns");
        if(!expressionNeutral)return Block("rest","Relax your click gestures and open your eyes before control returns");
        const double tolerance=.08;
        double dx=o.X-reference.Center[0],dy=o.Y-reference.Center[1];
        if(Math.Abs(dx)>tolerance)return Misaligned(dx<0?"right":"left",dx<0?"Move gently right toward your saved face outline":"Move gently left toward your saved face outline");
        if(Math.Abs(dy)>tolerance)return Misaligned(dy<0?"down":"up",dy<0?"Move gently down toward your saved face outline":"Move gently up toward your saved face outline");
        if(Math.Abs(o.Yaw-reference.Neutral[0])>.10)
            return Misaligned("angle",o.Yaw>reference.Neutral[0]?"Turn a little left toward your saved resting direction":"Turn a little right toward your saved resting direction");
        if(Math.Abs(o.Pitch-reference.Neutral[1])>.10)
            return Misaligned("angle",o.Pitch>reference.Neutral[1]?"Tilt gently down toward your saved resting direction":"Tilt gently up toward your saved resting direction");
        if(noseX.HasValue&&geometry is {IsValid:true}&&
            (Math.Abs(geometry.NoseX-noseX.Value)>tolerance||Math.Abs(geometry.NoseY-noseY!.Value)>tolerance))
            return Misaligned("angle","Align your nose with your saved resting position");
        ClearCandidate();
        return Result("good","Your face is aligned. Hold still briefly while movement is rebased",true);

        FacePosition Misaligned(string code,string hint)
        {
            // A fresh, neutral four-second hold provides a hands-free alternative
            // to matching the old posture. All bounds checks still apply.
            if(candidate is null||previous is null||o.Timestamp<=previous.Timestamp||o.Timestamp-previous.Timestamp>.2||
                Math.Abs(o.Yaw-candidate.Yaw)>.025||Math.Abs(o.Pitch-candidate.Pitch)>.025||
                Math.Abs(o.X-candidate.X)>.012||Math.Abs(o.Y-candidate.Y)>.012||
                Math.Abs(o.Scale/candidate.Scale-1)>.08||
                (geometry is {IsValid:true}&&candidateGeometry is {IsValid:true}&&
                 (Math.Abs(geometry.NoseX-candidateGeometry.NoseX)>.012||Math.Abs(geometry.NoseY-candidateGeometry.NoseY)>.012)))
            {candidate=o;candidateGeometry=geometry;candidateSamples=0;}
            previous=o;candidateSamples++;
            double elapsed=o.Timestamp-candidate.Timestamp;
            if(elapsed>=PostureAdoptionSeconds&&candidateSamples>=10)
                return Result("adopt","New comfortable posture held for 4 seconds. Keep still while movement is rebased",true);
            int remaining=(int)Math.Ceiling(Math.Max(0,PostureAdoptionSeconds-elapsed));
            return Result(code,hint+". Or hold this new posture still for "+remaining+"s to adopt it. Move to cancel; Esc stops control");
        }
    }
}
