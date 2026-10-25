'use strict';

// A fixed page is revealed by the same aperture used for cover previews.
(() => {
  const panel=$('#game-details'),content=panel.querySelector('.portal-content');
  const scroller=panel.querySelector('.details-scroll'),workspace=$('.workspace');
  const portal=new WinnowPortalSurface(panel);
  let returnTarget,game,opening=false;

  function finish() {
    if(panel.hidden)return;
    const wasOpening=opening;opening=false;portal.hide();
    content.style.clipPath='none';scroller.inert=false;
    panel.dataset.expanding='false';panel.removeAttribute('aria-busy');
    if(wasOpening)$('#back-to-collection')?.focus({preventScroll:true});
  }
  function close(restore=true) {
    if(panel.hidden)return;
    opening=false;portal.hide();panel.hidden=true;workspace.inert=false;
    delete document.body.dataset.details;
    document.dispatchEvent(new Event('rift:details'));
    if(restore){
      const target=returnTarget?.isConnected?returnTarget:$(state.page==='library'?`.gallery-card[data-game="${game.id}"]`:'.game-card[data-slot=selected]');
      (target||$('#toggle-index')).focus({preventScroll:true});
    }
  }
  function render() {
    const artwork=portalArtwork(game),played=game.time!=='Unplayed';
    scroller.innerHTML=`<div class="details-backdrop" aria-hidden="true">${artwork?`<img src="${artwork}" alt="" decoding="async">`:''}</div>
      <div class="details-wrap">
        <div class="details-navigation"><button id="back-to-collection" class="text-button">${icon('arrow-left')} Back to ${state.page==='library'?'your library':'Discover'}</button><span class="eyebrow">YOUR COLLECTION / ${game.store.toUpperCase()}</span></div>
        <header class="details-hero"><div class="details-heading"><span class="eyebrow"><span class="signal-dot"></span> ${played?'A WORLD YOU HAVE ALREADY BEGUN':'YOUR FIRST CHAPTER IS WAITING'}</span>
          <h1 id="details-title" tabindex="-1" class="${game.title.length>45?'long-title':''}">${escapeHtml(game.title)}</h1>
          <p class="details-tagline">${escapeHtml(game.heroDescription||game.reason)}</p>
          <div class="details-tags"><span>${game.store}</span><span>${played?game.time+' played':'Never played'}</span><span>${game.installed?'Installed':'In your library'}</span></div>
          <a class="details-explore" href="#details-about">Explore this world ${icon('arrow-down')}</a>
        </div><div class="details-cover">${cover(game)}<span class="eyebrow">ALREADY YOURS.</span></div></header>
        <div class="details-body"><section class="details-about" id="details-about"><span class="eyebrow">THE WORLD ON THE OTHER SIDE</span><h2>About the game</h2><p>${escapeHtml(game.description)}</p>
          <div class="details-artwork"><span class="eyebrow">A CLOSER LOOK</span>${artwork?`<img src="${artwork}" alt="Artwork for ${escapeHtml(game.title)}" decoding="async">`:'<div class="details-art-empty">Artwork is unavailable. Your game details are still here.</div>'}</div></section>
          <aside class="details-record"><span class="eyebrow">YOUR PLACE IN THIS WORLD</span><div class="details-playtime"><strong>${played?game.time:'Unplayed'}</strong><span>${played?'Time spent here':'A new beginning'}</span></div>
            <dl><div><dt>In your collection</dt><dd>${game.store}</dd></div><div><dt>Availability</dt><dd>${game.installed?'Installed':'Owned'}</dd></div></dl><p>${escapeHtml(game.reason)}</p><span class="details-fixture-note">Design preview · sample library</span>
          </aside></div>
      </div>`;
    hydrateIcons();scroller.scrollTop=0;
    $('#back-to-collection').addEventListener('click',()=>close());
    $('.details-explore').addEventListener('click',event=>{event.preventDefault();$('#details-about').scrollIntoView({behavior:riftMotion.matches?'instant':'smooth',block:'start'});});
  }
  document.addEventListener('rift:open-game',event=>{
    if(!event.detail?.game||!panel.hidden)return;
    game=event.detail.game;
    const preview=window.riftCoverPreview.takeOrigin(game.id);
    returnTarget=preview.card||event.detail.trigger;
    const source=preview.origin||(state.page==='discover'?$('#world-portal').getBoundingClientRect():event.detail.trigger?.getBoundingClientRect());
    render();panel.hidden=false;document.body.dataset.details='open';workspace.inert=true;
    document.dispatchEvent(new Event('rift:details'));
    const moving=!riftMotion.matches&&$('#preview-motion').value==='live';
    if(!moving){content.style.clipPath='none';$('#back-to-collection').focus({preventScroll:true});return;}
    opening=true;scroller.inert=true;panel.dataset.expanding='true';panel.setAttribute('aria-busy','true');
    const bounds=panel.getBoundingClientRect();
    const start=source?{x:source.left-bounds.left,y:source.top-bounds.top,width:source.width,height:source.height}:{x:bounds.width/2-40,y:bounds.height/2-60,width:80,height:120};
    portal.configure(Number($('#portal-roundness').value),Number($('#portal-waviness').value),Number($('#portal-activity').value));
    void portal.show(true,{x:start.x+start.width/2,y:start.y+start.height/2},portalArtwork(game),start);
  });
  panel.addEventListener('portal:expanded',finish);
  document.addEventListener('keydown',event=>{
    if(panel.hidden||$('dialog[open]'))return;
    if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();close();}
  },true);
  document.addEventListener('rift:navigate',()=>close(false));
  document.addEventListener('rift:layout',()=>{if(opening)finish();});
  riftMotion.addEventListener('change',()=>{if(riftMotion.matches)finish();});
  document.addEventListener('visibilitychange',()=>{if(document.hidden)finish();});
  window.addEventListener('resize',finish);window.addEventListener('blur',finish);
  window.addEventListener('pagehide',event=>{portal.hide();if(!event.persisted)portal.dispose();});
})();
