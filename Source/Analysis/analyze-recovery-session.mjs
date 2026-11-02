import fs from 'node:fs';
import path from 'node:path';
const [input,out]=process.argv.slice(2);if(!input||!out)throw Error('Usage: node analyze-recovery-session.mjs quality.json output-directory');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const report=read(input),records=fs.readFileSync(input.replace(/\.json$/,'-head.jsonl'),'utf8').trim().split(/\r?\n/).map(JSON.parse);
const samples=records.filter(r=>r.kind==='sample'),poses=records.filter(r=>r.kind==='pose'),configs=records.filter(r=>r.kind==='configuration');
const avg=a=>a.length?a.reduce((s,x)=>s+x,0)/a.length:null;
const q=(a,p)=>a.length?a.toSorted((a,b)=>a-b)[Math.ceil((a.length-1)*p)]:null;
const first=report.trials[0].samples[0].time,last=report.trials.at(-1).samples.at(-1).time;
const active=samples.filter(s=>s.time>=first&&s.time<=last),activePoses=poses.filter(s=>s.time>=first&&s.time<=last);
const intervals=active.slice(1).map((s,i)=>s.time-active[i].time),poseIntervals=activePoses.slice(1).map((s,i)=>s.time-activePoses[i].time);
const gaps=samples.slice(1).flatMap((s,i)=>s.time-samples[i].time>.25?[{from:samples[i].time,to:s.time,seconds:s.time-samples[i].time}]:[]);
const trials=report.trials.map(t=>({id:t.id,completed:t.completed,reason:t.reason,quality:t.quality,
 holdSeconds:(p=>p.length>1?p.at(-1).time-p[0].time:0)(t.samples.filter(p=>p.phase==='hold')),
 actualStart:[t.startX,t.startY],target:[t.targetX,t.targetY]}));
const data={session:path.basename(input),schema:report.schema,completed:trials.filter(t=>t.completed).length,total:trials.length,mode:report.mode,
 guidedTrackingLosses:trials.reduce((s,t)=>s+t.quality.trackingLosses,0),guidedSeconds:last-first,
 trackingFps:active.length>1?(active.length-1)/(active.at(-1).time-active[0].time):null,
 captureIntervalMedianMs:q(poseIntervals,.5)*1000,captureIntervalP95Ms:q(poseIntervals,.95)*1000,
 inferenceMedianMs:q(activePoses.map(s=>s.inferenceMs).filter(Number.isFinite),.5),inferenceP95Ms:q(activePoses.map(s=>s.inferenceMs).filter(Number.isFinite),.95),
 poseAgeMedianMs:q(activePoses.map(s=>(s.received-s.time)*1000),.5),poseAgeP95Ms:q(activePoses.map(s=>(s.received-s.time)*1000),.95),
 controllerFrameP95Ms:q(intervals,.95)*1000,
 longRightRms:avg(trials.filter(t=>/^right-1-/.test(t.id)).map(t=>t.quality.crossTrackRmsPx)),longLeftRms:avg(trials.filter(t=>/^left-1-/.test(t.id)).map(t=>t.quality.crossTrackRmsPx)),
 controllerTraceGaps:gaps,gapMeaning:'Controller trace gaps can include setup/recovery/inactive intervals; they are not a count of lost faces.',
 configurations:configs.map(c=>({time:c.time,neutral:c.profile.neutral,center:c.profile.center,scale:c.profile.scale,ranges:c.profile.ranges,noise:c.profile.noise,width:c.width,height:c.height,precision:c.settings.precisionNoiseMultiplier,responsiveness:c.settings.responsiveness,requestedFps:c.settings.trackingFps})),trials};
fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'session-analysis.json'),JSON.stringify(data,null,2));
const n=v=>v==null?'n/a':v.toFixed(2);
let md=`# Recorded movement results\n\nSession: ${data.session}\n\n${data.completed}/${data.total} tasks completed; ${data.guidedTrackingLosses} detected losses inside labelled tasks. This was the full guided protocol, not the 15-second rest-only check.\n\nTracking: ${n(data.trackingFps)} FPS. Inference median/P95: ${n(data.inferenceMedianMs)}/${n(data.inferenceP95Ms)} ms. Pose capture-to-extraction age median/P95: ${n(data.poseAgeMedianMs)}/${n(data.poseAgeP95Ms)} ms. These timings do not include all display/native-input latency.\n\n| Case | Complete | Travel cross-track RMS px | Efficiency % | Hold total RMS px | Hold residual RMS px | Hold drift px | Hold seconds |\n|---|---|---:|---:|---:|---:|---:|---:|\n`;
for(const t of trials)md+=`| ${t.id} | ${t.completed?'Yes':'No'} | ${n(t.quality.crossTrackRmsPx)} | ${n(t.quality.pathEfficiency*100)} | ${n(t.quality.holdJitterRmsPx)} | ${n(t.quality.holdDetrendedRmsPx)} | ${n(t.quality.holdDriftPx)} | ${n(t.holdSeconds)} |\n`;
md+='\n## What each case measures\n\n- `rest-start` / `rest-end`: instructed still head for about 3 seconds; measure drift and residual variation. A 15-second still-head test has not been supplied in this session.\n- `right/left-0.55-1/2`: two repeats of shorter horizontal travel. Repeat differences reveal inconsistency.\n- `right/left-1-1/2`: two repeats of longer horizontal travel; more travel can expose curved paths.\n- `up/down-0.55`: shorter vertical travel; `up/down-1`: longer vertical travel. They check vertical control independently of horizontal control.\n- `diagonal-ne/sw/nw/se`: intended diagonals. A correction must preserve these.\n- Cross-track RMS: distance from the reference line through the actual start and target; smaller means straighter, not necessarily better endpoint accuracy.\n- Path efficiency: straight actual displacement divided by travelled length; inspect endpoint error as well, because a straight miss can score well.\n- Hold total RMS: variation around the hold mean, including drift.\n- Hold residual RMS: variation after subtracting a fitted linear drift. It is not pure camera noise and can contain involuntary or intentional movement.\n- Hold drift: displacement between the first and final 250 ms means.\n- Zero detected losses inside tasks does not disprove a loss between tasks or outside the recorded trial phases.\n- Helper mouse input cannot be distinguished from head-controlled cursor input.\n- Protocol 2 retains spatial drift for the full hold. Protocol 1 restarted a hold on leaving its target, so comparisons across those protocols are not controlled before/after experiments.\n';
fs.writeFileSync(path.join(out,'RESULTS.md'),md);
console.log(JSON.stringify({...data,trials:undefined,configurations:undefined,controllerTraceGaps:undefined},null,2));
