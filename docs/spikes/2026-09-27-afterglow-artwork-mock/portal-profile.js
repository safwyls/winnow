'use strict';

// Opt-in review probe. CPU submission timings do not measure GPU completion or paint.
if(new URLSearchParams(location.search).has('portalProfile')) {
  const prototype=WinnowPortalSurface.prototype;
  const originalShow=prototype.show, originalDraw=prototype.draw;
  const percentile=(samples,p)=>[...samples].sort((a,b)=>a-b)[Math.floor((samples.length-1)*p)]||0;
  prototype.show=function(...args) {
    this.profile={started:performance.now(),last:0,intervals:[],costs:[],openingMs:null};
    return originalShow.apply(this,args);
  };
  prototype.draw=function(...args) {
    const start=performance.now();
    const result=originalDraw.apply(this,args);
    const profile=this.profile;
    if(!profile||!this.active||profile.costs.length>=400) return result;
    const cost=performance.now()-start;
    if(profile.last&&start-profile.last>5) profile.intervals.push(start-profile.last);
    profile.last=start; profile.costs.push(cost);
    if(this.progress===1&&profile.openingMs===null) profile.openingMs=performance.now()-profile.started;
    if(profile.costs.length%15===0||profile.costs.length===1) {
      this.panel.dataset.portalProfile=JSON.stringify({samples:profile.costs.length,
        openingMs:profile.openingMs,intervalP50:percentile(profile.intervals,.5),intervalP95:percentile(profile.intervals,.95),
        submitP50:percentile(profile.costs,.5),submitP95:percentile(profile.costs,.95)});
    }
    return result;
  };
}
