import fs from 'node:fs';
import path from 'node:path';
const input=process.argv[2];
if(!input)throw Error('Usage: node analyze-quality.mjs <quality.json> [output.json]');
const report=JSON.parse(fs.readFileSync(input,'utf8').replace(/^\uFEFF/,''));
const records=fs.readFileSync(input.replace(/\.json$/,'-head.jsonl'),'utf8').trim().split(/\r?\n/).map(s=>JSON.parse(s));
const head=records.filter(r=>r.kind==='sample');
const avg=a=>a.reduce((s,v)=>s+v,0)/Math.max(1,a.length);
const sd=a=>Math.sqrt(avg(a.map(v=>(v-avg(a))**2)));
function fit(rows,quadratic=false){
  // Separate intercept for each trial: head posture need not start at neutral.
  const groups=Object.groupBy(rows,r=>r.id),pairs=[];
  for(const group of Object.values(groups)){
    const mx=avg(group.map(r=>r.yaw)),mq=avg(group.map(r=>r.yaw*r.yaw)),my=avg(group.map(r=>r.pitch));
    pairs.push(...group.map(r=>[r.yaw-mx,r.yaw*r.yaw-mq,r.pitch-my]));
  }
  let xx=0,xq=0,qq=0,xy=0,qy=0;
  for(const [x,q,y] of pairs){xx+=x*x;xq+=x*q;qq+=q*q;xy+=x*y;qy+=q*y;}
  const det=xx*qq-xq*xq;
  const a=quadratic?(xy*qq-qy*xq)/det:xy/xx,b=quadratic?(qy*xx-xy*xq)/det:0;
  return {a,b,rms:Math.sqrt(avg(pairs.map(([x,q,y])=>(y-a*x-b*q)**2)))};
}
const all=[];
const trials=report.trials.map(t=>{
  const s=t.samples,travel=s.filter(p=>p.phase==='travel'),hold=s.filter(p=>p.phase==='hold');
  const within=pts=>pts.length?head.filter(h=>h.time>=pts[0].time&&h.time<=pts.at(-1).time):[];
  const h=within(travel),held=within(hold),group=h.map(r=>({id:t.id,yaw:r.face.yaw,pitch:r.face.pitch}));
  if(/^(right|left)-/.test(t.id))all.push(...group);
  const f=h.length>5?fit(group):null;
  return {id:t.id,completed:t.completed,quality:t.quality,travelTimes:travel.length?[travel[0].time,travel.at(-1).time]:[],headFrames:h.length,
    yawRange:h.length?[Math.min(...h.map(r=>r.face.yaw)),Math.max(...h.map(r=>r.face.yaw))]:[],
    pitchRange:h.length?[Math.min(...h.map(r=>r.face.pitch)),Math.max(...h.map(r=>r.face.pitch))]:[],
    rawPitchYawFit:f,hold: {frames:held.length,yawSD:sd(held.map(r=>r.face.yaw)),pitchSD:sd(held.map(r=>r.face.pitch)),outputPathX:held.reduce((s,r)=>s+Math.abs(r.output.dx),0),outputPathY:held.reduce((s,r)=>s+Math.abs(r.output.dy),0)},
    yawStart:h[0]?.face.yaw,yawEnd:h.at(-1)?.face.yaw,pitchStart:h[0]?.face.pitch,pitchEnd:h.at(-1)?.face.pitch};
});
const folds=[];
for(const id of [...new Set(all.map(r=>r.id))]){
 const training=all.filter(r=>r.id!==id),test=all.filter(r=>r.id===id);
 for(const quadratic of [false,true]){
   const model=fit(training,quadratic),mx=avg(test.map(r=>r.yaw)),mq=avg(test.map(r=>r.yaw*r.yaw)),my=avg(test.map(r=>r.pitch));
   folds.push({id,quadratic,model,before:sd(test.map(r=>r.pitch)),after:Math.sqrt(avg(test.map(r=>(r.pitch-my-model.a*(r.yaw-mx)-model.b*(r.yaw*r.yaw-mq))**2)))});
 }
}
const result={input,frames:head.length,configurationCount:records.filter(r=>r.kind==='configuration').length,linear:fit(all),quadratic:fit(all,true),trials,folds};
if(process.argv[3])fs.writeFileSync(process.argv[3],JSON.stringify(result,null,2));
console.log(JSON.stringify({...result,trials:trials.map(t=>({...t,quality:{rms:t.quality.crossTrackRmsPx,jitter:t.quality.holdJitterRmsPx}}))},null,2));
