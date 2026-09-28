'use strict';

// The preview owns hover intent and placement; the portal owns its material and reveal.
(() => {
  const panel=document.querySelector('#card-flyout');
  const portal=new WinnowPortalSurface(panel);
  const motion=document.querySelector('#preview-motion');
  let active,pending,dismissed,showTimer,hideTimer,frame,source='pointer',pointer;
  const cardFor=target=>target instanceof Element?target.closest('.gallery-card'):null;
  const available=()=>state.page==='library'&&document.body.dataset.details!=='open'&&!document.querySelector('dialog[open]')&&!document.hidden;

  function hide() {
    clearTimeout(showTimer);clearTimeout(hideTimer);cancelAnimationFrame(frame);
    active?.removeAttribute('aria-describedby');active=pending=null;
    portal.hide();panel.hidden=true;
  }
  function configure() {
    portal.configure(Number($('#portal-roundness').value),Number($('#portal-waviness').value),Number($('#portal-activity').value));
  }
  function position() {
    if(!active?.isConnected||!available()){hide();return;}
    const box=active.getBoundingClientRect(),gap=20,gutter=18;
    const topEdge=$('.app-bar').getBoundingClientRect().bottom+gutter;
    const bottomEdge=$('.app-footer').getBoundingClientRect().top-gutter;
    if(box.bottom<=topEdge||box.top>=bottomEdge){hide();return;}
    const right=innerWidth-box.right-gap-gutter,left=box.left-gap-gutter;
    const preferred=document.body.dataset.mode==='fullscreen'?430:400;
    const side=right>=preferred||right>=left?'right':'left';
    const room=side==='right'?right:left,docked=room<280;
    const width=docked?Math.min(400,innerWidth-gutter*2):Math.min(preferred,room);
    const height=Math.min(540,bottomEdge-topEdge);
    panel.dataset.placement=docked?'docked':side;
    panel.dataset.compact=String(width<365||height<490);
    panel.dataset.short=String(height<370);
    Object.assign(panel.style,{width:`${width}px`,height:`${height}px`,
      left:`${Math.round(docked?(innerWidth-width)/2:side==='right'?box.right+gap:box.left-gap-width)}px`,
      top:`${Math.round(docked?bottomEdge-height:Math.max(topEdge,Math.min(bottomEdge-height,box.top+box.height*.45-height/2)))}px`});
    const description=panel.querySelector('.flyout-description');
    description.style.webkitLineClamp='unset';
    description.style.webkitLineClamp=String(Math.max(1,Math.floor(description.clientHeight/parseFloat(getComputedStyle(description).lineHeight))));
    portal.resize();
  }
  function show(card,input) {
    if(!card?.isConnected||card===dismissed||!available())return;
    const game=byId.get(card.dataset.game);if(!game)return;
    hide();active=card;source=input;configure();
    panel.dataset.game=game.id;
    panel.dataset.portal='';panel.dataset.plain=String(motion.value==='plain');
    panel.innerHTML=`<div class="portal-scene" aria-hidden="true"><div class="portal-fallback"></div></div><div class="portal-content"><div class="flyout-body">
      <span class="eyebrow">A WINDOW INTO THIS WORLD</span><h3 class="${game.title.length>45?'long-title':''}">${escapeHtml(game.title)}</h3>
      <div class="flyout-meta"><span>${game.store}</span><span>·</span><span>${game.time==='Unplayed'?'Never played':game.time+' played'}</span></div>
      <p class="flyout-reason">${escapeHtml(game.reason)}</p><p class="flyout-description">${escapeHtml(game.description)}</p>
      <div class="flyout-actions"><button class="primary" data-view="${game.id}">View game ${icon('arrow-up-right')}</button><button class="icon-button" data-dismiss-preview aria-label="Dismiss preview">${icon('x')}</button></div>
      </div></div>`;
    panel.hidden=false;hydrateIcons();position();
    if(!active)return;
    card.setAttribute('aria-describedby',panel.id);
    const moving=input==='pointer'&&motion.value==='live'&&!riftMotion.matches;
    if(motion.value==='plain'){
      panel.querySelector('.portal-scene').hidden=true;
      panel.querySelector('.portal-content').style.clipPath='none';return;
    }
    const box=card.getBoundingClientRect(),origin=pointer||{x:box.right,y:box.top+box.height/2};
    void portal.show(moving,{x:origin.x-panel.offsetLeft,y:Math.max(0,Math.min(panel.offsetHeight,origin.y-panel.offsetTop))},portalArtwork(game));
  }
  function queue(card,input) {
    clearTimeout(hideTimer);
    if(card===active||card===pending||card===dismissed||!available())return;
    clearTimeout(showTimer);pending=card;
    if(input==='keyboard')show(card,input);
    else {void portal.prepare(portalArtwork(byId.get(card.dataset.game)));showTimer=setTimeout(()=>show(card,input),140);}
  }
  function leave() {
    clearTimeout(showTimer);pending=null;clearTimeout(hideTimer);
    hideTimer=setTimeout(()=>{if(!panel.contains(document.activeElement))hide();},240);
  }
  function dismiss() {
    const card=active;dismissed=card;hide();card?.focus({preventScroll:true});
  }
  document.addEventListener('pointerover',event=>{
    if(event.pointerType==='touch')return;
    pointer={x:event.clientX,y:event.clientY};
    if(panel.contains(event.target)){clearTimeout(hideTimer);return;}
    const card=cardFor(event.target);
    if(card&&!card.contains(event.relatedTarget))queue(card,'pointer');
  });
  document.addEventListener('pointermove',event=>{if(active||pending)pointer={x:event.clientX,y:event.clientY};},{passive:true});
  document.addEventListener('pointerout',event=>{
    const card=cardFor(event.target);
    if(card&&!card.contains(event.relatedTarget)){
      if(dismissed===card)dismissed=null;
      if(!panel.contains(event.relatedTarget))leave();
    }else if(panel.contains(event.target)&&!panel.contains(event.relatedTarget)&&!active?.contains(event.relatedTarget))leave();
  });
  document.addEventListener('focusin',event=>{
    if(panel.contains(event.target)){clearTimeout(hideTimer);return;}
    const card=cardFor(event.target);
    if(card&&card.matches(':focus-visible'))queue(card,'keyboard');else if(!card)hide();
  });
  document.addEventListener('focusout',event=>{if(event.target===dismissed)dismissed=null;});
  document.addEventListener('keydown',event=>{
    if(!active)return;
    if(event.key==='Escape'){dismiss();event.preventDefault();event.stopImmediatePropagation();}
    if(event.key==='Tab'&&event.target===active&&!event.shiftKey){
      event.preventDefault();panel.querySelector('[data-view]').focus();
    }else if(event.key==='Tab'&&panel.contains(event.target)){
      const action=panel.querySelector('[data-view]');
      if(event.shiftKey&&event.target===action){event.preventDefault();active.focus({preventScroll:true});}
      else if(!event.shiftKey&&event.target.matches('[data-dismiss-preview]')){
        const next=active.nextElementSibling;event.preventDefault();hide();(next||$('#appearance')).focus();
      }
    }
  },true);
  panel.addEventListener('click',event=>{if(event.target.closest('[data-dismiss-preview]'))dismiss();});
  document.addEventListener('scroll',event=>{
    if(panel.contains(event.target))return;
    if(source==='keyboard'&&active){cancelAnimationFrame(frame);frame=requestAnimationFrame(position);}else hide();
  },{capture:true,passive:true});
  document.addEventListener('rift:layout',hide);
  document.addEventListener('rift:navigate',hide);
  document.addEventListener('rift:details',hide);
  document.addEventListener('rift:portal-options',()=>{configure();if(active)show(active,source);});
  document.addEventListener('visibilitychange',hide);window.addEventListener('blur',hide);window.addEventListener('resize',hide);
  const changes=new MutationObserver(()=>{if(!available()||active&&!active.isConnected)hide();});
  changes.observe($('#gallery'),{childList:true});changes.observe($('#studio'),{attributes:true,attributeFilter:['open']});
  window.riftCoverPreview={
    takeOrigin(id){
      const origin=!panel.hidden&&panel.dataset.game===id?panel.getBoundingClientRect():null;
      const card=active;dismissed=card;hide();return {origin,card};
    },hide
  };
  window.addEventListener('pagehide',event=>{hide();if(!event.persisted){changes.disconnect();portal.dispose();}});
})();
