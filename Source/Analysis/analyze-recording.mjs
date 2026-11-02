import fs from 'node:fs';
import path from 'node:path';
const [input,output]=process.argv.slice(2);
if(!input||!output)throw new Error('Usage: node analyze-recording.mjs recording.jsonl output-folder');
const rows=fs.readFileSync(input,'utf8').split(/\r?\n/).filter(Boolean).map(x=>JSON.parse(x));
const percentile=(a,q)=>a.length?[...a].sort((x,y)=>x-y)[Math.min(a.length-1,Math.ceil((a.length-1)*q))]:null;
const mean=a=>a.length?a.reduce((x,y)=>x+y,0)/a.length:null;
const rms=a=>a.length?Math.sqrt(mean(a.map(x=>x*x))):null;
let current=null,lastCursor=null,previousTarget=null;const trials=[];
for(const r of rows){
 if(r.kind==='assessment-target'){
  if(current)trials.push(current);
  current={id:r.label,time:r.time,target:[r.targetX+r.targetWidth/2,r.targetY+r.targetHeight/2],start:lastCursor,plannedStart:previousTarget,samples:[],faces:[]};
  previousTarget=current.target;
 }
 if(r.kind==='frame'&&Number.isFinite(r.cursorX)&&Number.isFinite(r.cursorY)){
  lastCursor=[r.cursorX,r.cursorY];
  if(current){current.samples.push({t:r.time,x:r.cursorX,y:r.cursorY,valid:r.qualityAccepted});if(r.face)current.faces.push(r.face);}
 }
 if(r.assessmentResult&&current){current.result=r.assessmentResult;trials.push(current);current=null;}
}
if(current)trials.push(current);
for(const t of trials){
 t.start??=t.samples.length?[t.samples[0].x,t.samples[0].y]:t.target;
 const [sx,sy]=t.start,[tx,ty]=t.target,dx=tx-sx,dy=ty-sy,distance=Math.hypot(dx,dy),ux=distance?dx/distance:0,uy=distance?dy/distance:0;
 const [planX,planY]=t.plannedStart??t.start,px=tx-planX,py=ty-planY;
 t.direction=distance<20?'already at target':Math.abs(py)<=Math.max(10,Math.abs(px)*.1)?'horizontal':Math.abs(px)<=Math.max(10,Math.abs(py)*.1)?'vertical':'diagonal';
 const travelEnd=t.result?.firstEntryMs==null?Infinity:t.time+t.result.firstEntryMs/1000;
 const valid=t.samples.filter(p=>p.valid),travel=valid.filter(p=>p.t<=travelEnd);
 const cross=travel.map(p=>Math.abs((p.x-sx)*uy-(p.y-sy)*ux));
 let length=0;for(let i=1;i<travel.length;i++){if(travel[i].t-travel[i-1].t<=.2)length+=Math.hypot(travel[i].x-travel[i-1].x,travel[i].y-travel[i-1].y);}
 const hf=t.faces.filter(p=>p.timestamp<=travelEnd);
 let xx=0,xy=0,yy=0;
 for(let i=1;i<hf.length;i++) {const x=hf[i].yaw-hf[i-1].yaw,y=hf[i].pitch-hf[i-1].pitch;xx+=x*x;xy+=x*y;yy+=y*y;}
 t.metrics={travelSamples:travel.length,straightDistancePx:distance,pathLengthToFirstEntryPx:length,crossTrackRmsPx:rms(cross),crossTrackP95Px:percentile(cross,.95),crossTrackMaxPx:cross.length?Math.max(...cross):null,
  yawPitchIncrementCorrelation:travel.length>=10&&xx&&yy?xy/Math.sqrt(xx*yy):null,pitchPerYawIncrementSlope:travel.length>=10&&xx?xy/xx:null,
  observedPathEfficiency:t.result?.pathEfficiency,insideTargetSpreadRmsPx:t.result?.insideJitterRmsPx,
  restJitterMeasured:false};
}
const frames=rows.filter(r=>r.kind==='frame'&&r.face),gaps=frames.slice(1).map((r,i)=>r.face.timestamp-frames[i].face.timestamp);
const summary={source:path.resolve(input),recordingType:rows[0]?.synthetic?'synthetic':'recorded',frames:frames.length,
 observedFps:frames.length>1?(frames.length-1)/(frames.at(-1).face.timestamp-frames[0].face.timestamp):0,captureIntervalP95Ms:percentile(gaps,.95)*1000,
 labelledRestSamples:rows.filter(r=>r.kind==='label'&&r.label==='rest').length,
 notes:'Inside-target spread includes intentional adjustment. It is not labelled resting jitter. Correlation is descriptive; it does not identify intended head direction or prove a calibration correction.',trials:trials.map(({samples,faces,...t})=>t)};
fs.mkdirSync(output,{recursive:true});fs.writeFileSync(path.join(output,'recording-analysis.json'),JSON.stringify(summary,null,2));
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;');
let html=`<!doctype html><meta charset="utf-8"><title>Qadr recorded paths</title><style>body{font:16px system-ui;color:#172c41;max-width:1200px;margin:30px}svg{width:100%;height:220px;background:#f1f6f6}table{border-collapse:collapse}td,th{padding:10px;border-bottom:1px solid #ddd;text-align:left}</style><h1>Qadr recorded movement</h1><p>${frames.length} face frames · ${summary.observedFps.toFixed(1)} FPS · no labelled rest segment.</p><p>${esc(summary.notes)}</p><table><tr><th>Trial</th><th>Direction</th><th>Cross-track RMS</th><th>Cross-track P95</th><th>Path efficiency</th></tr>`;
for(const t of trials)html+=`<tr><td>${esc(t.id)}</td><td>${t.direction}</td><td>${t.metrics.crossTrackRmsPx?.toFixed(1)} px</td><td>${t.metrics.crossTrackP95Px?.toFixed(1)} px</td><td>${(100*t.metrics.observedPathEfficiency).toFixed(1)}%</td></tr>`;
html+='</table>';
for(const t of trials){if(!t.samples.length)continue;const xs=t.samples.map(p=>p.x).concat(t.start[0],t.target[0]),ys=t.samples.map(p=>p.y).concat(t.start[1],t.target[1]);const left=Math.min(...xs)-20,top=Math.min(...ys)-20,width=Math.max(...xs)-left+20,height=Math.max(80,Math.max(...ys)-top+20);
 html+=`<h2>${esc(t.id)} · ${t.direction}</h2><svg viewBox="${left} ${top} ${width} ${height}"><line x1="${t.start[0]}" y1="${t.start[1]}" x2="${t.target[0]}" y2="${t.target[1]}" stroke="#82949d" stroke-dasharray="6 5" vector-effect="non-scaling-stroke"/><polyline fill="none" stroke="#c26d0c" stroke-width="2" vector-effect="non-scaling-stroke" points="${t.samples.filter(p=>p.valid).map(p=>p.x+','+p.y).join(' ')}"/></svg>`;}
fs.writeFileSync(path.join(output,'recording-analysis.html'),html);
console.log(JSON.stringify({frames:summary.frames,fps:summary.observedFps,intervalP95Ms:summary.captureIntervalP95Ms,trials:summary.trials.map(t=>({id:t.id,direction:t.direction,...t.metrics}))},null,2));
