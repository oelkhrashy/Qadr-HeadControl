using Qadr.Core;

namespace Qadr.Engine;

internal sealed partial class Controller
{
    readonly ManualAdjustmentHistory manualAdjustments=new();
    bool CanAdjustMovement(){
        if(launchAssessmentRunning||assessment is not null||state is "Routine" or "Calibrating" or "LearnClick" or "LearnRest"||mouse.Dragging||headScroll.Active){
            manualAdjustments.Explain("Finish or pause the test, gesture lesson, drag or scrolling before adjusting movement.");return false;
        }
        return true;
    }
    void AdjustMovement(string? choice){
        if(!CanAdjustMovement())return;
        if(manualAdjustments.Adjust(motion.Settings,choice) is {} adjusted)ApplyManualSettings(adjusted);
    }
    void UndoAdjustment(){
        if(!CanAdjustMovement())return;
        if(manualAdjustments.Undo(motion.Settings) is {} restored)ApplyManualSettings(restored);
    }
}
