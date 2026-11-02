import fs from 'node:fs';
const [beforePath,afterPath,output]=process.argv.slice(2);
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''))[0].trials;
const before=read(beforePath),after=read(afterPath);
const rows=before.map(b=>{
 const a=after.find(t=>t.id===b.id);if(!a)throw Error('Missing trial: '+b.id);
 if(a.endpoint>b.endpoint+2)throw Error('Endpoint regression over 2px: '+b.id);
 if(a.rms>b.rms+1)throw Error('Cross-track regression over 1px: '+b.id);
 return {id:b.id,beforeRms:b.rms,afterRms:a.rms,beforeHold:b.jitter,afterHold:a.jitter,endpointChange:a.endpoint-b.endpoint};
});
const holdEnergy=key=>rows.reduce((s,r)=>s+r[key]**2,0)/rows.length;
if(holdEnergy('afterHold')>=holdEnergy('beforeHold'))throw Error('Aggregate holding vibration did not improve.');
const improvement=1-Math.sqrt(holdEnergy('afterHold')/holdEnergy('beforeHold'));
const summary={passed:improvement>=.05,minimumRelativeHoldImprovement:.05,relativeHoldImprovement:improvement,scope:'Offline open-loop replay of the completed tasks in one recording; not a live accuracy improvement claim.',tasks:rows.length,
 beforeHoldRms:Math.sqrt(holdEnergy('beforeHold')),afterHoldRms:Math.sqrt(holdEnergy('afterHold')),
 maxEndpointIncrease:Math.max(...rows.map(r=>r.endpointChange)),maxCrossTrackIncrease:Math.max(...rows.map(r=>r.afterRms-r.beforeRms)),rows};
if(output)fs.writeFileSync(output,JSON.stringify(summary,null,2));
console.log(JSON.stringify({...summary,rows:undefined},null,2));
if(!summary.passed)process.exitCode=1;
