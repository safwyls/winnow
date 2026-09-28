'use strict';

// A persistent reading surface is driven by selection, independently of card materials.
(() => {
  const panel=document.querySelector('#world-portal');
  const portal=new WinnowPortalSurface(panel);
  const motion=document.querySelector('#preview-motion');
  const roundness=document.querySelector('#portal-roundness');
  const waviness=document.querySelector('#portal-waviness');
  const activity=document.querySelector('#portal-activity');
  const outline=document.querySelector('#portal-shape-outline');
  let previewFrame=0,previewTime=0,previewLast=0;
  let source='keyboard',game=byId.get(state.selected);
  let pointer=null,resizeFrame;
  document.addEventListener('pointerdown',event=>{pointer={x:event.clientX,y:event.clientY};},{passive:true});

  function show(input=source) {
    source=input;portal.hide();
    if(!game||document.hidden||document.querySelector('dialog[open]')||!panel.getClientRects().length)return;
    const plain=motion.value==='plain';
    panel.dataset.plain=String(plain);
    panel.querySelector('.portal-scene').hidden=plain;
    if(plain){panel.querySelector('.portal-content').style.clipPath='none';return;}
    const moving=input==='pointer'&&motion.value==='live'&&!window.riftMotion.matches;
    const box=panel.getBoundingClientRect();
    // Keep the opening local to the deck/portal seam instead of spanning the entire index.
    const origin=pointer?{x:Math.max(-100,Math.min(box.width,pointer.x-box.left)),y:Math.max(0,Math.min(box.height,pointer.y-box.top))}:{x:0,y:box.height*.5};
    void portal.show(moving,origin,portalArtwork(game));
  }

  function fit() {
    cancelAnimationFrame(resizeFrame);
    resizeFrame=requestAnimationFrame(()=>{
      const description=panel.querySelector('.portal-description');
      if(description){
        description.style.webkitLineClamp='unset';
        const lines=Math.max(1,Math.floor((description.clientHeight+1)/parseFloat(getComputedStyle(description).lineHeight)));
        description.style.webkitLineClamp=String(lines);
      }
      if(!panel.getClientRects().length){portal.hide();return;}
      if(!portal.active)show('keyboard');
      portal.resize();
    });
  }

  document.addEventListener('rift:selection',event=>{
    game=event.detail?.game;show(event.detail?.input||'keyboard');fit();
  });
  document.addEventListener('rift:layout',()=>{show('keyboard');fit();});
  function updateShape() {
    portal.configure(Number(roundness.value),Number(waviness.value),Number(activity.value));
    document.querySelector('#portal-roundness-amount').value=`${roundness.value}%`;
    document.querySelector('#portal-waviness-amount').value=`${waviness.value}%`;
    document.querySelector('#portal-activity-amount').value=activity.value==='0'?'Still':`${activity.value}%`;
    drawPreview();
    roundness.disabled=waviness.disabled=motion.value==='plain';
    activity.disabled=motion.value!=='live'||window.riftMotion.matches;
    document.querySelector('.portal-shape-preview').hidden=motion.value==='plain';
    updatePreview();
  }
  function drawPreview() {
    const points=WinnowPortalSurface.contour(160,66,112,51,portal.exponent,portal.wave*.45,previewTime);
    outline.setAttribute('points',points.map(point=>point.join(',')).join(' '));
  }
  function updatePreview() {
    cancelAnimationFrame(previewFrame);previewFrame=0;
    if(!document.querySelector('#studio').open||document.hidden||!document.hasFocus()||activity.disabled||portal.activityRate===0)return;
    previewLast=performance.now();
    const tick=now=>{
      if(now-previewLast>=WinnowPortalSurface.ambientFrameMs){
        previewTime+=(now-previewLast)/1000*portal.activityRate;previewLast=now;drawPreview();
      }
      previewFrame=requestAnimationFrame(tick);
    };
    previewFrame=requestAnimationFrame(tick);
  }
  roundness.addEventListener('input',updateShape);waviness.addEventListener('input',updateShape);activity.addEventListener('input',updateShape);
  motion.addEventListener('change',()=>{updateShape();show('keyboard');});
  window.riftMotion.addEventListener('change',()=>{updateShape();show('keyboard');});
  document.querySelector('#reset-options').addEventListener('click',()=>{
    motion.value='live';roundness.value='70';waviness.value='45';activity.value='40';updateShape();show('keyboard');
  });
  const observer=new ResizeObserver(fit);observer.observe(panel);
  const dialogs=new MutationObserver(()=>{if(document.querySelector('dialog[open]'))portal.hide();else show('keyboard');updatePreview();});
  document.querySelectorAll('dialog').forEach(dialog=>dialogs.observe(dialog,{attributes:true,attributeFilter:['open']}));
  document.addEventListener('visibilitychange',()=>{document.hidden?portal.hide():show('keyboard');updatePreview();});
  window.addEventListener('blur',()=>{show('keyboard');updatePreview();});
  window.addEventListener('focus',()=>{show('keyboard');updatePreview();});
  window.addEventListener('pagehide',event=>{portal.hide();cancelAnimationFrame(previewFrame);if(!event.persisted){observer.disconnect();dialogs.disconnect();cancelAnimationFrame(resizeFrame);portal.dispose();}});
  updateShape();show();document.fonts.ready.then(fit);
})();
