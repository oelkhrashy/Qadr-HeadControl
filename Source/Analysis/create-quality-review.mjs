import fs from 'node:fs';
import path from 'node:path';
const [input,out]=process.argv.slice(2);
if(!input||!out)throw Error('Usage: node create-quality-review.mjs quality.json output-directory');
const report=JSON.parse(fs.readFileSync(input,'utf8').replace(/^\uFEFF/,''));
const head=fs.readFileSync(input.replace(/\.json$/,'-head.jsonl'),'utf8').trim().split(/\r?\n/).map(JSON.parse).filter(r=>r.kind==='sample');
const avg=a=>a.reduce((s,v)=>s+v,0)/Math.max(1,a.length);
const quant=(a,q)=>a.toSorted((a,b)=>a-b)[Math.floor((a.length-1)*q)];
const first=report.trials[0].samples[0].time,last=report.trials.at(-1).samples.at(-1).time;
const active=head.filter(h=>h.time>=first&&h.time<=last);
const dt=active.slice(1).map((h,i)=>h.time-active[i].time);
const trials=report.trials.map(t=>{
 const p=t.samples.filter(s=>s.valid&&s.phase==='hold');let detrended=null;
 if(p.length>2){
  const mt=avg(p.map(s=>s.time-p[0].time)),mx=avg(p.map(s=>s.x)),my=avg(p.map(s=>s.y));
  const tt=p.reduce((sum,s)=>sum+(s.time-p[0].time-mt)**2,0);
  const bx=p.reduce((sum,s)=>sum+(s.time-p[0].time-mt)*(s.x-mx),0)/tt;
  const by=p.reduce((sum,s)=>sum+(s.time-p[0].time-mt)*(s.y-my),0)/tt;
  detrended=Math.sqrt(avg(p.map(s=>(s.x-mx-bx*(s.time-p[0].time-mt))**2+(s.y-my-by*(s.time-p[0].time-mt))**2)));
 }
 return {id:t.id,completed:t.completed,quality:t.quality,holdDetrendedRmsPx:detrended};
});
const summary={source:input,protocol:report.schema,completed:trials.filter(t=>t.completed).length,total:trials.length,
 trackingLosses:trials.reduce((s,t)=>s+t.quality.trackingLosses,0),fps:(active.length-1)/(active.at(-1).time-active[0].time),frameIntervalP95Ms:quant(dt,.95)*1000,
 longRightRms:avg(trials.filter(t=>/^right-1-/.test(t.id)).map(t=>t.quality.crossTrackRmsPx)),longLeftRms:avg(trials.filter(t=>/^left-1-/.test(t.id)).map(t=>t.quality.crossTrackRmsPx)),
 movementChanged:false,freeMovementPreserved:true,reason:'Fixed pose compensation and damping worsened some paths; stronger precision traded steadiness for accuracy; near-rest expansion improved replayed hold RMS by only 0.49%.',trials};
fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'review.json'),JSON.stringify(summary,null,2));
const n=x=>x==null?'—':x.toFixed(2),esc=s=>String(s).replace(/[&<>\"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
let html=`<!doctype html><html lang="en"><meta charset="utf-8"><title>Qadr guided movement review</title><style>body{font:16px/1.55 system-ui;color:#172c41;max-width:1180px;margin:36px auto;padding:0 24px}h1{font-size:30px}table{border-collapse:collapse;width:100%;font-size:14px}td,th{padding:9px;text-align:left;border-bottom:1px solid #d5dfe4}svg{width:100%;background:#f3f7f8;max-height:260px}article{margin-top:30px}.card{background:#eef6f4;padding:18px;border-left:4px solid #00826e}small{color:#526575}</style><h1>Qadr · guided movement review</h1><p>${esc(path.basename(input))}</p><div class="card"><b>${summary.completed}/${summary.total} tasks completed · ${summary.trackingLosses} detected tracking losses · ${n(summary.fps)} tracking FPS.</b><p>Long rightward strokes: ${n(summary.longRightRms)} px cross-track RMS. Long leftward strokes: ${n(summary.longLeftRms)} px.</p><b>Fully free movement retained. No new movement correction was deployed.</b></div><p>The unwanted pitch component changed sign between repeated rightward strokes. A fixed correction cannot reliably cancel it. Offline tests of direction damping, stronger precision, lower responsiveness, a median filter and a wider rest band did not improve the full set sufficiently. The smallest rest-band change reduced aggregate replay hold RMS by only 0.49%, so it was rejected.</p><p>Replay is an open-loop counterfactual using the same head trace. It cannot predict how you would correct your movement with a different controller. Your calibration and settings were preserved.</p><h2>Measurements</h2><p>Cross-track error measures travel relative to the actual start and target. Hold variation includes drift and vibration. The detrended value removes only a straight drift over time; it is not a measurement of camera noise alone.</p><table><thead><tr><th>Task</th><th>Complete</th><th>Cross-track RMS px</th><th>Efficiency %</th><th>Hold variation RMS px</th><th>Hold drift px</th><th>Hold RMS after linear drift removal px</th></tr></thead><tbody>`;
for(const t of trials)html+=`<tr><td>${esc(t.id)}</td><td>${t.completed?'Yes':'No'}</td><td>${n(t.quality.crossTrackRmsPx)}</td><td>${n(100*t.quality.pathEfficiency)}</td><td>${n(t.quality.holdJitterRmsPx)}</td><td>${n(t.quality.holdDriftPx)}</td><td>${n(t.holdDetrendedRmsPx)}</td></tr>`;
html+='</tbody></table><h2>Measurement correction</h2><p>This recording used protocol 1: a hold restarted whenever the cursor left the target. That can retain only the calmer segments and caused the first rest task to time out. Protocol 2 records the full instructed hold even when the cursor drifts outside the circle. A new 15-second still-head check collects a longer uninterrupted rest and numeric pose-matrix and inference timing data.</p><p>During the next still-head check, relax in a comfortable position and keep your head still. Let the cursor drift without correcting it. Reports remain numeric; no images, video or audio are recorded by the test.</p><h2>Recorded travel paths</h2><p><small>Orange: recorded cursor. Dashed: reference path. Invalid intervals are not joined.</small></p>';
for(const t of report.trials){
 const p=t.samples.filter(s=>s.phase==='travel');if(p.length<2||t.id.startsWith('rest-'))continue;
 const valid=p.filter(s=>s.valid);const xs=[t.startX,t.targetX,...valid.map(p=>p.x)],ys=[t.startY,t.targetY,...valid.map(p=>p.y)];
 const left=Math.min(...xs)-15,top=Math.min(...ys)-15,w=Math.max(...xs)-left+15,h=Math.max(50,Math.max(...ys)-top+15);
 let d='',previous=null;for(const row of p){if(!row.valid){previous=null;continue;}d+=(previous&&row.time-previous.time<=.2?'L':'M')+row.x+','+row.y+' ';previous=row;}
 html+=`<article><h3>${esc(t.id)}</h3><svg viewBox="${left} ${top} ${w} ${h}" role="img" aria-label="${esc(t.id)} cursor path"><line x1="${t.startX}" y1="${t.startY}" x2="${t.targetX}" y2="${t.targetY}" stroke="#8e9da9" stroke-dasharray="6 5" vector-effect="non-scaling-stroke"/><path d="${d}" stroke="#c26d0c" fill="none" stroke-width="2" vector-effect="non-scaling-stroke"/></svg></article>`;
}
fs.writeFileSync(path.join(out,'review.html'),html+'</html>');console.log(JSON.stringify({...summary,trials:undefined},null,2));
