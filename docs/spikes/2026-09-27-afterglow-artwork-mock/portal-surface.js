'use strict';

// A content-independent surface: the flyout owns placement, text and interaction.
window.WinnowPortalSurface = class {
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
    uniform vec2 uSize;
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
      vec2 p=pixel-uSize*.5;
      vec2 radius=uSize*.5-vec2(32.0);
      vec2 q=p/radius;
      float angle=atan(q.y,q.x);
      float contour=pow(pow(abs(q.x),3.4)+pow(abs(q.y),3.4),1.0/3.4);
      float ripple=sin(angle*3.0+uTime*.32)*3.4+sin(angle*7.0-uTime*.23)*2.1;
      float distance=(contour-1.0)*min(radius.x,radius.y)+ripple;
      float inside=1.0-smoothstep(-.8,1.0,distance);
      float edge=exp(-abs(distance)*.8);
      float halo=exp(-abs(distance)*.13)*.18;
      vec2 space=pixel+vec2(uTime*.6,-uTime*.3);
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
          uSize:{value:new Float32Array([1,1]),type:'vec2<f32>'},
          uTime:{value:0,type:'f32'},
        }}});
      this.plane.filters=[this.filter];
      app.stage.addChild(this.plane);
      app.ticker.maxFPS=30;
      app.ticker.add(ticker=>{
        if(!this.active||this.panel.hidden||document.hidden) {this.hide();return;}
        this.time+=Math.min(ticker.deltaMS,80)/1000;
        this.filter.resources.portalUniforms.uniforms.uTime=this.time;
      });
    })();
    return this.initialization;
  }

  async show(moving) {
    const generation=++this.generation;
    this.active=true;
    this.panel.dataset.renderer='fallback';
    if(this.failed||this.disposed) return;
    try {
      await this.initialize();
      if(!this.active||this.disposed||generation!==this.generation) return;
      const scene=this.panel.querySelector('.portal-scene');
      if(!scene) return;
      scene.append(this.app.canvas);
      this.resize();
      this.filter.resources.portalUniforms.uniforms.uTime=moving?this.time:0;
      this.app.render();
      this.panel.dataset.renderer='webgl';
      this.panel.dataset.portalRunning=String(moving);
      if(moving) this.app.start(); else this.app.stop();
    } catch { this.fallback(); }
  }

  resize() {
    if(!this.app||!this.active||this.panel.hidden) return;
    const width=this.panel.offsetWidth+48, height=this.panel.offsetHeight+48;
    if(this.app.screen.width!==width||this.app.screen.height!==height) this.app.renderer.resize(width,height);
    this.plane.width=width; this.plane.height=height;
    this.filter.resources.portalUniforms.uniforms.uSize.set([width,height]);
    this.app.render();
  }

  hide() {
    this.active=false;
    this.generation++;
    this.app?.stop();
    this.app?.canvas.remove();
    this.panel.dataset.portalRunning='false';
  }

  fallback() {
    this.hide();
    this.failed=true;
    this.panel.dataset.renderer='fallback';
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
