'use strict';

// A persistent reading surface is driven by selection, independently of card materials.
(() => {
  const panel=document.querySelector('#world-portal');
  const portal=new WinnowPortalSurface(panel);
  const motion=document.querySelector('#preview-motion');
  const roundness=document.querySelector('#portal-roundness');
  const waviness=document.querySelector('#portal-waviness');
  let source='keyboard',game=byId.get(state.selected);
  let pointer=null,resizeFrame;
  document.addEventListener('pointerdown',event=>{pointer={x:event.clientX,y:event.clientY};},{passive:true});

  function show(input=source) {
    source=input;portal.hide();
    if(!game||document.hidden||document.querySelector('dialog[open]'))return;
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
      portal.resize();
    });
  }

  document.addEventListener('rift:selection',event=>{
    game=event.detail?.game;show(event.detail?.input||'keyboard');fit();
  });
  document.addEventListener('rift:layout',fit);
  function updateShape() {
    portal.configure(Number(roundness.value),Number(waviness.value));
    document.querySelector('#portal-roundness-amount').value=`${roundness.value}%`;
    document.querySelector('#portal-waviness-amount').value=`${waviness.value}%`;
    const points=WinnowPortalSurface.contour(160,66,112,51,portal.exponent,portal.wave*.45,0);
    document.querySelector('#portal-shape-outline').setAttribute('points',points.map(point=>point.join(',')).join(' '));
    roundness.disabled=waviness.disabled=motion.value==='plain';
    document.querySelector('.portal-shape-preview').hidden=motion.value==='plain';
  }
  roundness.addEventListener('input',updateShape);waviness.addEventListener('input',updateShape);
  motion.addEventListener('change',()=>{updateShape();show('keyboard');});
  window.riftMotion.addEventListener('change',()=>show('keyboard'));
  document.querySelector('#reset-options').addEventListener('click',()=>{
    motion.value='live';roundness.value='70';waviness.value='45';updateShape();show('keyboard');
  });
  const observer=new ResizeObserver(fit);observer.observe(panel);
  const dialogs=new MutationObserver(()=>{if(document.querySelector('dialog[open]'))portal.hide();else show('keyboard');});
  document.querySelectorAll('dialog').forEach(dialog=>dialogs.observe(dialog,{attributes:true,attributeFilter:['open']}));
  document.addEventListener('visibilitychange',()=>document.hidden?portal.hide():show('keyboard'));
  window.addEventListener('blur',()=>show('keyboard'));
  window.addEventListener('focus',()=>show('keyboard'));
  window.addEventListener('pagehide',event=>{portal.hide();if(!event.persisted){observer.disconnect();dialogs.disconnect();cancelAnimationFrame(resizeFrame);portal.dispose();}});
  updateShape();show();document.fonts.ready.then(fit);
})();
