'use strict';

// A static decorative layer. Resizing redraws it; brightness changes only its opacity.
window.WinnowStarfield=class {
  constructor(canvas) {
    this.canvas=canvas;
    this.resize=()=>{cancelAnimationFrame(this.frame);this.frame=requestAnimationFrame(()=>this.draw());};
    window.addEventListener('resize',this.resize);
    this.draw();
  }
  draw() {
    this.frame=0;
    const canvas=this.canvas,ctx=canvas.getContext('2d');
    if(!ctx)return;
    const width=innerWidth,height=innerHeight,dpr=Math.min(devicePixelRatio||1,2);
    canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);
    ctx.setTransform(dpr,0,0,dpr,0,0);
    let seed=4729;
    const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
    const count=Math.min(520,Math.ceil(width*height/4800));
    for(let i=0;i<count;i++){
      const x=random()*width,y=random()*height,r=.35+random()**3*1.1;
      const brightness=.22+random()*.6;
      ctx.fillStyle=`rgba(${i%5===0?'192,177,235':'181,213,226'},${brightness})`;
      ctx.beginPath();ctx.arc(x,y,r,0,Math.PI*2);ctx.fill();
      if(i%43===0){
        const glow=ctx.createRadialGradient(x,y,0,x,y,9);
        glow.addColorStop(0,'rgba(166,212,223,.3)');glow.addColorStop(1,'rgba(166,212,223,0)');
        ctx.fillStyle=glow;ctx.fillRect(x-9,y-9,18,18);
        ctx.strokeStyle='rgba(186,218,230,.25)';ctx.lineWidth=.5;
        ctx.beginPath();ctx.moveTo(x-3,y);ctx.lineTo(x+3,y);ctx.moveTo(x,y-3);ctx.lineTo(x,y+3);ctx.stroke();
      }
    }
  }
  configure(enabled,intensity) {
    this.canvas.hidden=!enabled;
    this.canvas.style.opacity=String(Math.max(0,Math.min(1,intensity/100)));
  }
  dispose(){cancelAnimationFrame(this.frame);window.removeEventListener('resize',this.resize);}
};

(() => {
  const field=new WinnowStarfield(document.querySelector('#starfield'));
  const enabled=document.querySelector('#stars-enabled'),intensity=document.querySelector('#stars-intensity');
  function update(){field.configure(enabled.checked,Number(intensity.value));intensity.disabled=!enabled.checked;document.querySelector('#stars-amount').value=`${intensity.value}%`;}
  enabled.addEventListener('change',update);intensity.addEventListener('input',update);
  document.querySelector('#reset-options').addEventListener('click',()=>{enabled.checked=true;intensity.value='65';update();});
  window.addEventListener('pagehide',event=>{if(!event.persisted)field.dispose();});
  update();
})();
