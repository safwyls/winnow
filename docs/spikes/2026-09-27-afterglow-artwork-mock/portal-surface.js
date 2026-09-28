'use strict';

// One aperture clips a fixed HTML plane and shades its rim; content never scales.
window.WinnowPortalSurface = class {
  static openingMs = 360;
  static ambientFrameMs = 1000/30;
  static contour(cx, cy, rx, ry, exponent, wave, time) {
    return Array.from({length:128}, (_, i) => {
      const angle=i*Math.PI/64, x=Math.cos(angle), y=Math.sin(angle);
      const ripple=(Math.sin(angle*3+time*.32)+Math.sin(angle*7-time*.23)*.62)*wave;
      const r=(1-ripple/Math.min(rx,ry))/Math.pow(Math.abs(x)**exponent+Math.abs(y)**exponent,1/exponent);
      return [cx+x*rx*r,cy+y*ry*r];
    });
  }

  static vertex = `
    precision highp float;
    in vec2 aPosition;
    out vec2 vTextureCoord;
    uniform vec4 uInputSize;
    uniform vec4 uOutputFrame;
    uniform vec4 uOutputTexture;
    void main() {
      vec2 position = aPosition * uOutputFrame.zw + uOutputFrame.xy;
      position.x = position.x * (2.0 / uOutputTexture.x) - 1.0;
      position.y = position.y * (2.0 * uOutputTexture.z / uOutputTexture.y) - uOutputTexture.z;
      gl_Position = vec4(position, 0.0, 1.0);
      vTextureCoord = aPosition * uOutputFrame.zw * uInputSize.zw;
    }
  `;
  static fragment = `
    precision highp float;
    in vec2 vTextureCoord;
    out vec4 finalColor;
    uniform vec4 uInputSize;
    uniform vec2 uCenter;
    uniform vec2 uRadius;
    uniform vec2 uRestCenter;
    uniform vec2 uShape;
    uniform float uTime;
    float hash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
    float noise(vec2 p) {
      vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
      return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
    }
    float stars(vec2 p, float scale) {
      vec2 grid=p/scale; vec2 cell=floor(grid); float light=0.0;
      for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++) {
        vec2 neighbor=cell+vec2(float(x),float(y));
        float seed=hash(neighbor);
        vec2 at=neighbor+vec2(hash(neighbor+7.3),hash(neighbor+19.1));
        float d=length((grid-at)*scale);
        float core=1.0-smoothstep(.25,.85+seed*.45,d);
        float glow=exp(-d*d*.35)*.18;
        float shimmer=.82+.18*sin(uTime*.6+seed*40.0);
        light+=(core+glow)*step(.67,seed)*shimmer;
      }
      return light;
    }
    void main() {
      vec2 pixel=vTextureCoord*uInputSize.xy;
      vec2 q=(pixel-uCenter)/uRadius;
      float angle=atan(q.y,q.x);
      float contour=pow(pow(abs(q.x),uShape.x)+pow(abs(q.y),uShape.x),1.0/uShape.x);
      float ripple=(sin(angle*3.0+uTime*.32)+sin(angle*7.0-uTime*.23)*.62)*uShape.y;
      float distance=(contour-1.0)*min(uRadius.x,uRadius.y)+ripple;
      // Pixels beyond the faint halo do not need the star and cloud calculations.
      if(distance>48.0) { finalColor=vec4(0.0); return; }
      float inside=1.0-smoothstep(-.8,1.0,distance);
      float edge=exp(-abs(distance)*.8);
      float halo=exp(-abs(distance)*.13)*.18;
      vec2 space=pixel-uRestCenter+vec2(uTime*.6,-uTime*.3);
      float cloud=noise(space*.008)+noise(space*.016)*.5;
      float nebula=pow(max(0.0,cloud-.35),2.0);
      float fringe=smoothstep(.18,.97,contour);
      vec3 sky=vec3(.023,.032,.060);
      sky+=mix(vec3(.055,.08,.15),vec3(.13,.06,.15),noise(space*.004))*nebula*(.23+fringe*.7);
      float starlight=stars(space,33.0)*.72+stars(space+131.0,69.0)*.48;
      // The clear center gives words a quiet reading plane, with depth around them.
      sky+=vec3(.72,.81,1.0)*starlight*(.18+fringe*.7);
      float shift=.5+.5*sin(angle*2.0+uTime*.19);
      vec3 rim=mix(vec3(.43,.68,.79),vec3(.93,.66,.50),shift);
      vec3 rgb=sky*inside+rim*(edge*.62+halo);
      float alpha=clamp(inside+edge*.7+halo*.85,0.0,1.0);
      finalColor=vec4(rgb,alpha);
    }
  `;

  constructor(panel) {
    this.panel=panel;
    this.generation=0;
    this.time=0;
    this.active=false;
    this.failed=false;
    this.disposed=false;
    this.configure(70,45);
  }

  configure(roundness, waviness) {
    this.exponent=5.8-Math.max(0,Math.min(100,roundness))*.028;
    this.wave=Math.max(0,Math.min(100,waviness))*.08;
    this.fallbackMaskDirty=true;
    if(this.active) this.draw();
  }

  async prepare() {
    if(this.failed||this.disposed) return;
    try { await this.initialize(); } catch { this.fallback(); }
  }

  async initialize() {
    if (!this.initialization) this.initialization=(async () => {
      const app=new PIXI.Application();
      await app.init({width:1,height:1,backgroundAlpha:0,preference:'webgl',
        resolution:Math.min(devicePixelRatio||1,1.5),autoDensity:true,autoStart:false,
        sharedTicker:false,antialias:false,
        eventFeatures:{move:false,globalMove:false,click:false,wheel:false}});
      if(this.disposed) {app.destroy({removeView:true},{children:true});return;}
      this.app=app;
      app.stage.eventMode='none';
      app.canvas.setAttribute('aria-hidden','true');
      app.canvas.className='portal-canvas';
      app.canvas.addEventListener('webglcontextlost',()=>this.fallback());
      this.plane=new PIXI.Sprite(PIXI.Texture.WHITE);
      this.filter=PIXI.Filter.from({gl:{vertex:WinnowPortalSurface.vertex,fragment:WinnowPortalSurface.fragment},
        resolution:'inherit',padding:0,resources:{portalUniforms:{
          uCenter:{value:new Float32Array([0,0]),type:'vec2<f32>'},
          uRadius:{value:new Float32Array([1,1]),type:'vec2<f32>'},
          uRestCenter:{value:new Float32Array([0,0]),type:'vec2<f32>'},
          uShape:{value:new Float32Array([3.4,3.6]),type:'vec2<f32>'},
          uTime:{value:0,type:'f32'},
        }}});
      this.plane.filters=[this.filter];
      app.stage.addChild(this.plane);
      // Compile the tiny first frame during hover intent, before the visible entrance.
      app.render();
    })();
    return this.initialization;
  }

  async show(moving, origin) {
    const generation=++this.generation;
    this.active=true;
    this.moving=moving;
    this.origin=moving?origin:null;
    this.progress=moving?0:1;
    this.started=performance.now();
    this.lastFrame=this.started;
    this.timeAtStart=this.time;
    this.content=this.panel.querySelector('.portal-content');
    this.fallbackElement=this.panel.querySelector('.portal-fallback');
    this.scene=this.panel.querySelector('.portal-scene');
    this.contentRevealed=false;
    this.panel.dataset.renderer='fallback';
    this.resize();
    if(moving) this.frame=requestAnimationFrame(now=>this.tick(now));
    if(this.failed||this.disposed) return;
    try {
      await this.initialize();
      if(!this.active||this.disposed||generation!==this.generation) return;
      if(!this.scene) return;
      this.scene.append(this.app.canvas);
      this.panel.dataset.renderer='webgl';
      this.resize();
      if(moving&&!this.frame) this.frame=requestAnimationFrame(now=>this.tick(now));
    } catch { this.fallback(); }
  }

  resize() {
    if(!this.active||this.panel.hidden) return;
    this.width=this.panel.offsetWidth; this.height=this.panel.offsetHeight;
    const origin=this.origin||{x:this.width/2,y:this.height/2};
    // Include the cursor and its halo so the opening can travel outside the final panel.
    this.sceneX=Math.min(-24,origin.x-24); this.sceneY=Math.min(-24,origin.y-24);
    const width=Math.ceil(Math.max(this.width+24,origin.x+24)-this.sceneX);
    const height=Math.ceil(Math.max(this.height+24,origin.y+24)-this.sceneY);
    if(!this.scene) return;
    Object.assign(this.scene.style,{left:`${this.sceneX}px`,top:`${this.sceneY}px`,width:`${width}px`,height:`${height}px`});
    this.fallbackMaskDirty=true;
    if(this.app) {
      if(this.app.screen.width!==width||this.app.screen.height!==height) this.app.renderer.resize(width,height);
      this.plane.width=width; this.plane.height=height;
    }
    this.draw();
  }

  tick(now) {
    this.frame=0;
    if(!this.active||this.panel.hidden||document.hidden) {this.hide();return;}
    const delta=now-this.lastFrame, interval=WinnowPortalSurface.ambientFrameMs;
    const opening=this.progress<1;
    if(opening||delta>=interval-.1) {
      // Entrance follows each display frame. Keep the remainder for the quiet ambient loop.
      this.lastFrame=opening?now:this.lastFrame+Math.max(1,Math.floor((delta+.1)/interval))*interval;
      this.progress=Math.min(1,Math.max(0,now-this.started)/WinnowPortalSurface.openingMs);
      this.time=this.timeAtStart+Math.max(0,now-this.started)/1000;
      this.draw();
    }
    // The CSS fallback completes the entrance, then remains still without a loop.
    if(this.progress<1||this.panel.dataset.renderer==='webgl') this.frame=requestAnimationFrame(next=>this.tick(next));
    else this.panel.dataset.portalRunning='false';
  }

  draw() {
    if(!this.active||!this.width) return;
    const restX=this.width/2, restY=this.height/2;
    const origin=this.origin||{x:restX,y:restY};
    const reveal=1-(1-this.progress)**3;
    const scale=.025+.975*reveal;
    const cx=origin.x+(restX-origin.x)*reveal, cy=origin.y+(restY-origin.y)*reveal;
    const rx=Math.max(1,(restX-8)*scale), ry=Math.max(1,(restY-8)*scale);
    const wave=this.wave*scale, time=this.moving?this.time:0;
    const opening=this.progress<1;
    const fallbackMask=this.panel.dataset.renderer==='fallback'&&(opening||this.fallbackMaskDirty);
    if(opening||fallbackMask) {
      const points=WinnowPortalSurface.contour(cx,cy,rx,ry,this.exponent,wave,time);
      const polygon=(dx=0,dy=0)=>`polygon(${points.map(([x,y])=>`${(x-dx).toFixed(2)}px ${(y-dy).toFixed(2)}px`).join(',')})`;
      if(opening) this.content.style.clipPath=polygon();
      if(fallbackMask) {
        this.fallbackElement.style.clipPath=polygon(this.sceneX,this.sceneY);
        this.fallbackMaskDirty=false;
      }
    }
    if(!opening&&!this.contentRevealed) {
      // Text is inset from the moving rim; after reveal it needs no animated DOM mask.
      this.content.style.clipPath='none';
      this.contentRevealed=true;
    }
    const running=String(this.moving&&(opening||this.panel.dataset.renderer==='webgl'));
    if(this.panel.dataset.portalOpening!==String(opening)) this.panel.dataset.portalOpening=String(opening);
    if(this.panel.dataset.portalRunning!==running) this.panel.dataset.portalRunning=running;
    if(this.app&&!this.failed) {
      const uniforms=this.filter.resources.portalUniforms.uniforms;
      uniforms.uCenter.set([cx-this.sceneX,cy-this.sceneY]);
      uniforms.uRestCenter.set([restX-this.sceneX,restY-this.sceneY]);
      uniforms.uRadius.set([rx,ry]);
      uniforms.uShape.set([this.exponent,wave]);
      uniforms.uTime=time;
      this.app.render();
    }
  }

  hide() {
    this.active=false;
    this.generation++;
    cancelAnimationFrame(this.frame); this.frame=0;
    this.app?.stop();
    this.app?.canvas.remove();
    this.panel.dataset.portalRunning='false';
  }

  fallback() {
    this.failed=true;
    this.fallbackMaskDirty=true;
    this.app?.stop();
    this.app?.canvas.remove();
    this.panel.dataset.renderer='fallback';
    this.draw();
  }

  dispose() {
    this.disposed=true;
    this.hide();
    if(this.plane) this.plane.filters=[];
    this.filter?.destroy();
    // The cover finish owns another renderer; its shared resources must remain valid.
    this.app?.destroy({removeView:true},{children:true});
  }
};
