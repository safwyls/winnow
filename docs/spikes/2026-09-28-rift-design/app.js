'use strict';
const assetRoot='../2026-09-27-afterglow-artwork-mock/assets/';
const systemMotion=matchMedia('(prefers-reduced-motion: reduce)');
const stillControl=document.querySelector('#reduce-motion');
window.riftMotion={
  get matches(){return systemMotion.matches||stillControl.checked;},
  addEventListener(type,handler){systemMotion.addEventListener(type,handler);stillControl.addEventListener(type,handler);}
};
stillControl.addEventListener('change',()=>{document.body.dataset.still=String(stillControl.checked);});


// Static review fixtures. These controls never call Winnow or launch a game.
const games = [
  {id:'borderlands2', title:'Borderlands 2', store:'Steam', time:'3.8h', installed:true, reason:'Another chance for Pandora.', description:'Return to Pandora as a Vault Hunter, chasing strange weapons and taking on Handsome Jack. Explore its wastelands alone or with friends in a story-driven shooter.'},
  {id:'borderlands3', title:'Borderlands 3', store:'Epic', time:'4.2h', reason:'Your next vault is out there.', description:'Choose a Vault Hunter and take the hunt beyond Pandora. Fight across new worlds, build an arsenal of outlandish weapons and face the Calypso Twins alone or in co-op.'},
  {id:'bounty', title:'Borderlands 3: Bounty of Blood – A Fistful of Redemption', store:'Epic', time:'Unplayed', reason:'A new story in your collection.', description:'Travel to Gehenna, a frontier world caught between a struggling town and the Devil Riders. This Borderlands 3 expansion mixes a revenge story with new creatures, weapons and dusty trails.'},
  {id:'tentacles', title:'Borderlands 3: Guns, Love and Tentacles – The Marriage of Wainwright & Hammerlock', store:'Epic', time:'Unplayed', reason:'A new story in your collection.', description:'Join Wainwright and Hammerlock for a wedding on the frozen planet Xylourgos. Help the couple confront an unsettling cult and the creatures that haunt this Borderlands 3 expansion.'},
  {id:'jackpot', title:'Borderlands 3: Moxxi’s Heist of the Handsome Jackpot', store:'Epic', time:'Unplayed', reason:'A new story in your collection.', description:'Assemble a crew for Moxxi and break into Handsome Jack’s abandoned casino. This Borderlands 3 expansion sends you through neon halls filled with security robots, stranded gamblers and loot.'},
  {id:'celeste', title:'Celeste', store:'Steam', time:'2.4h', installed:true, reason:'A mountain you started climbing.', description:'Help Madeline climb Celeste Mountain through precise jumps, dashes and rooms full of small challenges. A story about persistence and self-discovery unfolds along the ascent.'},
  {id:'disco', title:'Disco Elysium – The Final Cut', store:'Steam', time:'1.6h', reason:'There is more to your story.', description:'Wake up as a detective with a fractured memory and a murder to solve. Explore a troubled city, question its residents and let competing parts of your own mind shape the investigation.'},
  {id:'enshrouded', title:'Enshrouded', store:'Steam', time:'8.2h', installed:true, reason:'A world worth returning to.', description:'Explore a ruined kingdom consumed by a mysterious fog. Gather resources, build a home and develop your combat skills as you venture into the Shroud, alone or with friends.'},
  {id:'hades', title:'Hades', store:'Steam', time:'12.6h', installed:true, reason:'There is always one more run.', heroDescription:'One more attempt. One more conversation. One more way out.', description:'Fight your way out of the Underworld as Zagreus, with weapons and blessings from the Olympian gods. Each attempt brings new combinations, conversations and another piece of the family story.'},
  {id:'hollow', title:'Hollow Knight', store:'Steam', time:'6.1h', installed:true, reason:'Hallownest still has its secrets.', description:'Descend into Hallownest, a fallen kingdom beneath the surface. Explore interconnected caverns, master precise combat and uncover the histories of the insects who still call it home.'},
  {id:'outer', title:'Outer Wilds', store:'Steam', time:'Unplayed', reason:'A solar system full of questions.', heroDescription:'A solar system full of questions. You already own the first step.', description:'Explore a small, changing solar system trapped in a time loop. Follow the traces of an ancient civilization and use what you learn on each journey to piece together its mysteries.'},
  {id:'sable', title:'Sable', store:'GOG', time:'1.2h', reason:'Take your time finding your way.', description:'Set out on a coming-of-age journey across a vast desert. Glide between dunes, climb old ruins and meet the people whose stories help Sable decide where she belongs.'},
];
const byId = new Map(games.map(game => [game.id, game]));
const portalHeroes = {
  borderlands2:'borderlands2-scene', celeste:'celeste-scene', disco:'disco-scene',
  enshrouded:'enshrouded-scene', hades:'hades-hero', hollow:'hollow-scene', outer:'outer-hero', sable:'sable',
};
const portalArtwork = game => {
  if(!game) return null;
  const unavailable=['missing','loading'].includes(document.body.dataset.state)
    && ['borderlands3','tentacles','hades','disco'].includes(game.id);
  return unavailable?null:`${assetRoot}${portalHeroes[game.id]||game.id}.jpg`;
};
const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];
const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[character]));
const icon = name => `<i data-lucide="${name}" aria-hidden="true"></i>`;
const hydrateIcons = () => window.lucide.createIcons({attrs:{'aria-hidden':'true'}});
const say = message => { $('#live-message').textContent = message; };
const discoveryOrder = ['outer','hades','hollow','celeste','enshrouded','disco','sable','borderlands2','borderlands3','bounty','tentacles','jackpot'];
const state = {page:'library',filter:'all',query:'',selected:'outer',input:'keyboard'};
const lensTitles = {all:'Every game, an opening.',unplayed:'Begin somewhere new.',returning:'There is more to your story.',installed:'Your next world is ready.'};

function cover(game) {
  const sample = document.body.dataset.state;
  const affected = ['borderlands3','tentacles','hades','disco'].includes(game.id);
  if (sample === 'missing' && affected) return `<span class="cover" role="img" aria-label="Artwork unavailable"><span class="cover-empty">${icon('image-off')}<small>No artwork yet</small></span></span>`;
  if (sample === 'loading' && affected) return '<span class="cover" role="img" aria-label="Artwork loading"><span class="cover-loading"><span class="loading-mark"></span></span></span>';
  const file = sample === 'mixed' && game.id === 'hades' ? 'hades-landscape' : game.id;
  return `<span class="cover"><img src="${assetRoot}${file}.jpg" alt="" loading="lazy" decoding="async" draggable="false"></span>`;
}

function visibleGames() {
  const ordered = state.page === 'discover' ? discoveryOrder.map(id=>byId.get(id)) : [...games].sort((a,b)=>a.title.localeCompare(b.title));
  return ordered.filter(game=>game.title.toLowerCase().includes(state.query.toLowerCase()) &&
    (state.filter==='all' || state.filter==='unplayed' && game.time==='Unplayed' ||
     state.filter==='returning' && game.time!=='Unplayed' || state.filter==='installed' && game.installed));
}

function renderIndex() {
  const visible = visibleGames();
  if (!visible.some(game=>game.id===state.selected)) state.selected=visible[0]?.id||null;
  $('#game-index').innerHTML=visible.map(game=>`<button class="index-game" data-select="${game.id}" aria-label="Select ${escapeHtml(game.title)}" aria-pressed="${game.id===state.selected}">${cover(game)}<span><strong>${escapeHtml(game.title)}</strong><small>${game.store} · ${game.time==='Unplayed'?'Never played':game.time+' played'}</small><span class="index-reason">${escapeHtml(game.reason)}</span></span></button>`).join('')||'<p class="empty-index">No matching games.</p>';
  $('#result-count').textContent=String(visible.length);
  $('#gallery-count').textContent=`${visible.length} ${visible.length===1?'game':'games'} · Alphabetical`;
  $('#gallery').innerHTML=visible.map(game=>`<button class="game-card gallery-card" data-game="${game.id}" data-select="${game.id}" aria-label="Inspect ${escapeHtml(game.title)}" aria-pressed="${game.id===state.selected}"><span class="card-surface">${cover(game)}</span><span class="gallery-caption"><span class="gallery-title">${escapeHtml(game.title)}</span><span class="gallery-meta">${game.store} <span>·</span> ${game.time}</span><span class="gallery-reason">${escapeHtml(game.reason)}</span></span></button>`).join('');
  $$('[data-filter]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.filter===state.filter)));
  $('#browse-title').textContent=state.page==='library'?({all:'The whole collection.',unplayed:'Still unexplored.',returning:'Worlds to return to.',installed:'Ready when you are.'}[state.filter]):lensTitles[state.filter];
  renderSelection('keyboard');
}

function deckCard(game, slot) {
  const selected=slot==='selected';
  return `<button class="game-card" data-slot="${slot}" data-game="${game.id}" ${selected?'data-view':'data-select'}="${game.id}" aria-label="${selected?'View':'Select'} ${escapeHtml(game.title)}"><span class="card-surface">${cover(game)}</span></button>`;
}

function renderSelection(input='keyboard') {
  const visible=visibleGames(),game=byId.get(state.selected);
  state.input=input;
  $('#world-stage').hidden=!game;
  $('.browse-bottom').hidden=!game||state.page==='library';
  $('#empty-workspace').hidden=!!game;
  $$('[data-select]').forEach(button=>{if(button.matches('.index-game,.gallery-card'))button.setAttribute('aria-pressed',String(button.dataset.select===state.selected));});
  $('.index-game[aria-pressed=true]')?.scrollIntoView({block:'nearest'});
  if(!game){$('#position').textContent='00 / 00';document.dispatchEvent(new CustomEvent('rift:selection'));say('No matching games.');return;}
  const index=visible.findIndex(item=>item.id===game.id);
  const previous=visible[(index-1+visible.length)%visible.length],next=visible[(index+1)%visible.length];
  $('#deck').innerHTML=(visible.length>2?deckCard(previous,'previous'):'')+(visible.length>1?deckCard(next,'next'):'')+deckCard(game,'selected');
  $('#deck-title').textContent=game.title;
  $('#position').textContent=`${String(index+1).padStart(2,'0')} / ${String(visible.length).padStart(2,'0')}`;
  $('#sequence-dots').innerHTML=visible.map((item,i)=>`<span class="${i===index?'active':''}" aria-hidden="true"></span>`).join('');
  $('#sequence-dots').setAttribute('aria-label',`Game ${index+1} of ${visible.length}`);
  const label=game.time==='Unplayed'?'A WORLD YOU HAVE YET TO EXPLORE':'PICK UP THE THREAD';
  $('#portal-copy').innerHTML=`<div class="portal-reading"><span class="eyebrow">${label}</span><h2 id="selected-title" class="${game.title.length>45?'long-title':''}">${escapeHtml(game.title)}</h2><div class="portal-meta"><span>${game.store}</span><span>${game.time==='Unplayed'?'Never played':game.time+' played'}</span></div><p class="portal-reason">${escapeHtml(game.reason)}</p><p class="portal-description">${escapeHtml(game.description)}</p><div class="portal-actions"><button class="primary" data-view="${game.id}">View game ${icon('arrow-up-right')}</button>${game.installed?`<span class="portal-installed">${icon('hard-drive')} Installed</span>`:''}</div></div>`;
  $('#portal-foot-status').textContent=game.time==='Unplayed'?'Your first chapter is waiting':game.time+' in your story';
  $('#previous').disabled=$('#next').disabled=$('#surprise').disabled=visible.length<2;
  hydrateIcons();
  document.dispatchEvent(new CustomEvent('rift:selection',{detail:{game,input}}));
  say(`${game.title}. ${game.reason}`);
}

function selectGame(id,input='keyboard',fromIndex=false) {
  if(id!==state.selected){state.selected=id;renderSelection(input);}
  if(fromIndex&&(document.body.dataset.mode==='fullscreen'||innerWidth<=800)) {
    setIndex(false);
    if(input==='keyboard')focusSelectedCard();
  }
}

function moveSelection(step,input='keyboard') {
  const visible=visibleGames(); if(!visible.length)return;
  const index=visible.findIndex(game=>game.id===state.selected);
  selectGame(visible[(index+step+visible.length)%visible.length].id,input);
}

function setIndex(open) {
  document.body.dataset.index=open?'open':'closed';
  $('#toggle-index').setAttribute('aria-expanded',String(open));
  document.dispatchEvent(new Event('rift:layout'));
}

function navigate(page) {
  document.dispatchEvent(new Event('rift:navigate'));
  state.page=page; state.query=''; $('#search').value=''; state.filter='all';
  document.body.dataset.page=page;
  $('.library-gallery').hidden=page!=='library';
  $('.deck-zone').hidden=page==='library';
  $('#position').hidden=page==='library';
  state.selected=page==='discover'?'outer':state.selected||'outer';
  $$('[data-page]').forEach(button=>{if(button.dataset.page===page)button.setAttribute('aria-current','page');else button.removeAttribute('aria-current');});
  $('#index-kicker').textContent=page==='discover'?'FIND YOUR NEXT GAME':'YOUR COLLECTION';
  $('#index-title').textContent=page==='discover'?'Where to next?':'Your library.';
  $('#browse-kicker').textContent=page==='discover'?'DISCOVER / FROM YOUR COLLECTION':'LIBRARY / ALL YOUR WORLDS';
  $('#index-list-label').textContent=page==='discover'?'PLACES TO BEGIN':'IN YOUR COLLECTION';
  const url=new URL(location.href);url.searchParams.set('page',page);history.replaceState(null,'',url);
  renderIndex();
  document.dispatchEvent(new Event('rift:layout'));
}

function focusSelectedCard() {
  $(state.page==='library'?'.gallery-card[aria-pressed=true]':'.game-card[data-slot=selected]')?.focus({preventScroll:true});
}

function openGame(id,trigger,input='keyboard') {
  if(id!==state.selected)selectGame(id,input);
  document.dispatchEvent(new CustomEvent('rift:open-game',{detail:{game:byId.get(id),trigger,input}}));
}

document.addEventListener('click',event=>{
  const button=event.target.closest('button');if(!button)return;
  const input=event.detail===0?'keyboard':'pointer';
  if(button.dataset.page)navigate(button.dataset.page);
  if(button.dataset.select){
    const fromDeck=button.closest('#deck');
    selectGame(button.dataset.select,input,button.classList.contains('index-game'));
    if(fromDeck&&input==='keyboard')focusSelectedCard();
    if(button.classList.contains('gallery-card'))openGame(button.dataset.select,button,input);
  }
  if(button.dataset.view)openGame(button.dataset.view,button,input);
  if(button.dataset.filter){state.filter=button.dataset.filter;renderIndex();$('#game-index').scrollTop=0;say(`${visibleGames().length} games shown`);}
  if(button.dataset.size){document.body.dataset.size=button.dataset.size;$$('button[data-size]').forEach(item=>item.setAttribute('aria-pressed',String(item===button)));}
  if(button.dataset.palette){document.body.dataset.palette=button.dataset.palette;$$('button[data-palette]').forEach(item=>item.setAttribute('aria-pressed',String(item===button)));}
});
$('#search').addEventListener('input',event=>{state.query=event.target.value;renderIndex();});
$('#clear-search').addEventListener('click',()=>{state.query='';state.filter='all';$('#search').value='';renderIndex();$('#toggle-index').focus();});
$('#previous').addEventListener('click',event=>moveSelection(-1,event.detail?'pointer':'keyboard'));
$('#next').addEventListener('click',event=>moveSelection(1,event.detail?'pointer':'keyboard'));
$('#surprise').addEventListener('click',event=>moveSelection(1+Math.floor(Math.random()*(visibleGames().length-1)),event.detail?'pointer':'keyboard'));
$('#appearance').addEventListener('click',()=>$('#studio').showModal());
$('#toggle-index').addEventListener('click',()=>setIndex(getComputedStyle($('.collection-index')).display==='none'));
$('#sample').addEventListener('change',event=>{document.body.dataset.state=event.target.value;renderIndex();});
$('#surface').addEventListener('change',event=>{
  document.body.dataset.mode=event.target.value;document.body.dataset.index='auto';
  $('#toggle-index').setAttribute('aria-expanded',String(event.target.value==='desktop'&&innerWidth>800));
  const url=new URL(location.href);url.searchParams.set('mode',event.target.value);history.replaceState(null,'',url);
  document.dispatchEvent(new Event('rift:layout'));
  document.dispatchEvent(new Event('rift:layout'));
});
$$('[name=caption]').forEach(input=>input.addEventListener('change',()=>{document.body.dataset.caption=input.value;}));
$('#reset-options').addEventListener('click',()=>{
  document.body.dataset.size='balanced';document.body.dataset.caption='essential';document.body.dataset.palette='rift';
  stillControl.checked=false;stillControl.dispatchEvent(new Event('change'));
  $$('button[data-size]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.size==='balanced')));
  $$('button[data-palette]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.palette==='rift')));
  $('[name=caption][value=essential]').checked=true;
});
document.addEventListener('keydown',event=>{
  if($('dialog[open]')||document.body.dataset.details==='open'||event.target.matches('input,select,textarea'))return;
  if(event.key==='/'){event.preventDefault();setIndex(true);$('#search').focus();return;}
  if(event.key==='Escape'&&document.body.dataset.index==='open'){setIndex(false);$('#toggle-index').focus();return;}
  if(event.target.matches('.gallery-card')&&['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(event.key)){
    event.preventDefault();
    const cards=$$('.gallery-card'),index=cards.indexOf(event.target);
    const columns=getComputedStyle($('#gallery')).gridTemplateColumns.split(' ').length;
    const step={ArrowLeft:-1,ArrowRight:1,ArrowUp:-columns,ArrowDown:columns}[event.key];
    const next=cards[event.key==='Home'?0:event.key==='End'?cards.length-1:Math.max(0,Math.min(cards.length-1,index+step))];
    next?.focus();selectGame(next.dataset.select,'keyboard');return;
  }
  if(['ArrowLeft','ArrowRight'].includes(event.key)){
    event.preventDefault();const onCard=!!event.target.closest('.game-card');
    moveSelection(event.key==='ArrowLeft'?-1:1);
    if(onCard)focusSelectedCard();
  }
  if(event.target.matches('.index-game')&&['ArrowUp','ArrowDown'].includes(event.key)){
    event.preventDefault();const buttons=$$('.index-game'),index=buttons.indexOf(event.target);
    const next=buttons[Math.max(0,Math.min(buttons.length-1,index+(event.key==='ArrowDown'?1:-1)))];
    next?.focus();state.selected=next.dataset.select;renderSelection();
  }
});
$$('dialog').forEach(dialog=>dialog.addEventListener('click',event=>{
  if(event.target!==dialog)return;const box=dialog.getBoundingClientRect();
  if(event.clientX<box.left||event.clientX>box.right||event.clientY<box.top||event.clientY>box.bottom)dialog.close();
}));
const params=new URLSearchParams(location.search);
$('#studio').setAttribute('aria-label','Display options');
document.body.dataset.index='auto';
if(params.get('mode')==='fullscreen'){document.body.dataset.mode='fullscreen';$('#surface').value='fullscreen';}
$('#toggle-index').setAttribute('aria-expanded',String(document.body.dataset.mode!=='fullscreen'&&innerWidth>800));
navigate(params.get('page')==='library'?'library':'discover');
function syncIndexVisibility() {
  $('.index-game[aria-pressed=true]')?.scrollIntoView({block:'nearest'});
  $('#toggle-index').setAttribute('aria-expanded',String(getComputedStyle($('.collection-index')).display!=='none'));
  document.dispatchEvent(new Event('rift:layout'));
}
document.fonts.ready.then(syncIndexVisibility);
window.addEventListener('resize',()=>requestAnimationFrame(syncIndexVisibility));
