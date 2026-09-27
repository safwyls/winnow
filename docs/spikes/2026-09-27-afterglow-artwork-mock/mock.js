'use strict';

// Static review fixtures. These controls never call Winnow or launch a game.
const games = [
  {id:'borderlands2', title:'Borderlands 2', store:'Steam', time:'3.8h', installed:true, reason:'Another chance for Pandora.', description:'A familiar world, waiting for another visit.'},
  {id:'borderlands3', title:'Borderlands 3', store:'Epic', time:'4.2h', reason:'Your next vault is out there.'},
  {id:'bounty', title:'Borderlands 3: Bounty of Blood – A Fistful of Redemption', store:'Epic', time:'Unplayed', reason:'A new story in your collection.'},
  {id:'tentacles', title:'Borderlands 3: Guns, Love and Tentacles – The Marriage of Wainwright & Hammerlock', store:'Epic', time:'Unplayed', reason:'A new story in your collection.'},
  {id:'jackpot', title:'Borderlands 3: Moxxi’s Heist of the Handsome Jackpot', store:'Epic', time:'Unplayed', reason:'A new story in your collection.'},
  {id:'celeste', title:'Celeste', store:'Steam', time:'2.4h', installed:true, reason:'A mountain you started climbing.'},
  {id:'disco', title:'Disco Elysium – The Final Cut', store:'Steam', time:'1.6h', reason:'There is more to your story.'},
  {id:'enshrouded', title:'Enshrouded', store:'Steam', time:'8.2h', installed:true, reason:'A world worth returning to.'},
  {id:'hades', title:'Hades', store:'Steam', time:'12.6h', installed:true, reason:'There is always one more run.', description:'One more attempt. One more conversation. One more way out.'},
  {id:'hollow', title:'Hollow Knight', store:'Steam', time:'6.1h', installed:true, reason:'Hallownest still has its secrets.'},
  {id:'outer', title:'Outer Wilds', store:'Steam', time:'Unplayed', reason:'A solar system full of questions.', description:'A solar system full of questions. You already own the first step.'},
  {id:'sable', title:'Sable', store:'GOG', time:'1.2h', reason:'Take your time finding your way.'},
];
const byId = new Map(games.map(game => [game.id, game]));
const state = { page:'discover', filter:'all', query:'', view:'grid', hero:0 };
const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];
const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[character]));
const icon = name => `<i data-lucide="${name}" aria-hidden="true"></i>`;
const hydrateIcons = () => window.lucide.createIcons({attrs:{'aria-hidden':'true'}});
const say = message => { $('#live-message').textContent = message; };

function cover(game, sample = document.body.dataset.state) {
  const affected = ['borderlands3','tentacles','hades','disco'].includes(game.id);
  if (sample === 'missing' && affected) return `<span class="cover" role="img" aria-label="Artwork unavailable"><span class="cover-empty">${icon('image-off')}<small>No artwork yet</small></span></span>`;
  if (sample === 'loading' && affected) return `<span class="cover" role="img" aria-label="Artwork loading"><span class="cover-loading"><span class="loading-mark"></span></span></span>`;
  const file = sample === 'mixed' && game.id === 'hades' ? 'hades-landscape' : game.id;
  return `<span class="cover"><img src="assets/${file}.jpg" alt="" loading="lazy" decoding="async" draggable="false"></span>`;
}

function metadata(game) {
  return `<span class="card-meta"><span>${game.store}</span><span class="separator">·</span><span>${game.time}</span>${game.installed ? '<span class="installed" title="Installed" aria-label="Installed">'+icon('hard-drive')+'</span>' : ''}</span>`;
}

function card(game) {
  return `<button class="game-card" data-game="${game.id}" aria-label="View ${escapeHtml(game.title)}"><span class="card-surface">${cover(game)}<span class="card-caption"><span class="card-title">${escapeHtml(game.title)}</span>${metadata(game)}<span class="card-reason">${game.reason}</span></span></span>${game.title.length > 32 ? `<span class="card-tooltip" aria-hidden="true">${escapeHtml(game.title)}</span>` : ''}</button>`;
}

function renderLibrary() {
  const filtered = games.filter(game => game.title.toLowerCase().includes(state.query.toLowerCase()) && (state.filter === 'all' || state.filter === 'unplayed' && game.time === 'Unplayed' || state.filter === 'installed' && game.installed));
  $('#collection').classList.toggle('records', state.view === 'list');
  $('#collection').innerHTML = filtered.map(card).join('') || '<p class="empty-result">No games match. Try another search.</p>';
  $('#result-count').textContent = `${filtered.length} ${filtered.length === 1 ? 'game' : 'games'}`;
  $('.collection-end').hidden = filtered.length === 0;
  $('.collection-end').textContent = filtered.length === games.length ? 'A dozen possibilities. All yours.' : 'More to discover in your collection.';
  hydrateIcons();
}

function renderArt() {
  $('#returning').innerHTML = ['hades','borderlands2','hollow','celeste','disco','enshrouded','borderlands3','sable'].map(id => {
    const game = byId.get(id);
    return `<button class="return-item" data-game="${id}" aria-label="View ${game.title}">${cover(game)}<span><strong>${game.title}</strong><small>${game.reason}</small></span>${icon('arrow-up-right')}</button>`;
  }).join('');
  $('#shelf').innerHTML = ['hollow','disco','celeste','enshrouded','bounty','jackpot'].map(id => card(byId.get(id))).join('');
  renderLibrary();
}

function navigate(page) {
  state.page = page;
  $$('.page').forEach(element => { element.hidden = element.id !== page; });
  $$('.app-bar nav button').forEach(button => {
    if (button.dataset.page === page) button.setAttribute('aria-current','page');
    else button.removeAttribute('aria-current');
  });
  const url = new URL(location.href);
  url.searchParams.set('page', page);
  history.replaceState(null,'',url);
  if (document.body.dataset.mode === 'fullscreen' && page === 'library') $('#collection .game-card')?.focus({preventScroll:true});
}

function selectHero(index) {
  state.hero = (index + 2) % 2;
  const game = byId.get(state.hero ? 'hades' : 'outer');
  $('#hero-art').src = `assets/${game.id}-hero.jpg`;
  $('#hero-title').textContent = game.title;
  $('#hero-reason').textContent = game.description;
  $('#hero-kicker').textContent = state.hero ? 'Pick up the thread' : 'Still waiting for you';
  $('#hero-position').textContent = `0${state.hero + 1} / 02`;
  $('#hero-meta').innerHTML = `${game.store}<span>${state.hero ? '2020' : '2019'}</span>${game.time === 'Unplayed' ? 'Never played' : game.time+' played'}`;
  $$('[data-hero]').forEach(button => button.setAttribute('aria-pressed',String(Number(button.dataset.hero) === state.hero)));
  say(`Featured game: ${game.title}`);
}

function openGame(id) {
  const game = byId.get(id);
  $('#game-preview').innerHTML = `<div class="preview-layout">${cover(game)}<div><span class="eyebrow">In your collection</span><h2 id="preview-title">${escapeHtml(game.title)}</h2><p>${game.description || game.reason}</p>${metadata(game)}<p class="preview-note">Design preview · sample activity.<br>Select a cover to inspect its complete title.</p></div></div>`;
  hydrateIcons();
  $('#game-dialog').showModal();
}

document.addEventListener('click', event => {
  const button = event.target.closest('button');
  if (!button) return;
  if (button.dataset.page) navigate(button.dataset.page);
  if (button.dataset.game) openGame(button.dataset.game);
  if (button.dataset.hero) selectHero(Number(button.dataset.hero));
  if (button.dataset.filter) {
    state.filter = button.dataset.filter;
    $$('[data-filter]').forEach(item => item.setAttribute('aria-pressed',String(item === button)));
    renderLibrary();
    $('#collection-scroll').scrollTop = 0;
    say(`${$('#result-count').textContent} shown`);
  }
  if (button.dataset.size) {
    document.body.dataset.size = button.dataset.size;
    $$('button[data-size]').forEach(item => item.setAttribute('aria-pressed',String(item === button)));
  }
  if (button.dataset.palette) {
    document.body.dataset.palette = button.dataset.palette;
    $$('button[data-palette]').forEach(item => item.setAttribute('aria-pressed',String(item === button)));
  }
});
$('#search').addEventListener('input', event => { state.query = event.target.value; renderLibrary(); $('#collection-scroll').scrollTop = 0; });
$('#grid-view').addEventListener('click', () => setView('grid'));
$('#list-view').addEventListener('click', () => setView('list'));
function setView(view) {
  state.view = view;
  $('#grid-view').setAttribute('aria-pressed',String(view === 'grid'));
  $('#list-view').setAttribute('aria-pressed',String(view === 'list'));
  renderLibrary();
}
$('#find-game').addEventListener('click', () => { navigate('library'); $('#search').focus(); });
$('#appearance').addEventListener('click', () => $('#studio').showModal());
$('#library-appearance').addEventListener('click', () => $('#studio').showModal());
$('#hero-prev').addEventListener('click', () => selectHero(state.hero-1));
$('#hero-next').addEventListener('click', () => selectHero(state.hero+1));
$('#hero-skip').addEventListener('click', () => selectHero(state.hero+1));
$('#hero-view').addEventListener('click', () => openGame(state.hero ? 'hades' : 'outer'));
$('#sample').addEventListener('change', event => { document.body.dataset.state = event.target.value; renderArt(); say(`Showing ${event.target.selectedOptions[0].textContent.toLowerCase()}`); });
$('#surface').addEventListener('change', event => {
  document.body.dataset.mode = event.target.value;
  const url = new URL(location.href);
  url.searchParams.set('mode',event.target.value);
  history.replaceState(null,'',url);
  if (state.page === 'library' && event.target.value === 'fullscreen') $('#collection .game-card')?.focus({preventScroll:true});
});
$$('[name=caption]').forEach(input => input.addEventListener('change', () => { document.body.dataset.caption = input.value; }));
$('#reset-options').addEventListener('click', () => {
  document.body.dataset.size = 'balanced';
  document.body.dataset.caption = 'essential';
  document.body.dataset.palette = 'afterglow';
  $$('button[data-size]').forEach(button => button.setAttribute('aria-pressed',String(button.dataset.size === 'balanced')));
  $$('button[data-palette]').forEach(button => button.setAttribute('aria-pressed',String(button.dataset.palette === 'afterglow')));
  $('[name=caption][value=essential]').checked = true;
});

document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && !$('dialog[open]') && state.page === 'library') navigate('discover');
  if (event.key === '/' && !$('dialog[open]') && !event.target.matches('input,select,textarea')) { event.preventDefault(); navigate('library'); $('#search').focus(); }
  if (document.body.dataset.mode !== 'fullscreen' || !$('button.game-card:focus') || !['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)) return;
  const current = document.activeElement;
  const grid = current.parentElement;
  const cards = [...grid.querySelectorAll('.game-card')];
  const index = cards.indexOf(current);
  const columns = grid.classList.contains('records') ? 1 : getComputedStyle(grid).gridTemplateColumns.split(' ').length;
  const step = {ArrowLeft:-1, ArrowRight:1, ArrowUp:-columns, ArrowDown:columns}[event.key];
  event.preventDefault();
  cards[Math.max(0,Math.min(cards.length-1,index+step))]?.focus();
});
// Background clicks close dialogs; native <dialog> supplies focus trapping and Escape.
$$('dialog').forEach(dialog => dialog.addEventListener('click', event => {
  if (event.target !== dialog) return;
  const bounds = dialog.getBoundingClientRect();
  if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
}));
const params = new URLSearchParams(location.search);
$('#studio').setAttribute('aria-label', 'Display options');
$('#game-dialog').setAttribute('aria-labelledby', 'preview-title');
if (params.get('mode') === 'fullscreen') { document.body.dataset.mode = 'fullscreen'; $('#surface').value = 'fullscreen'; }
renderArt();
navigate(params.get('page') === 'library' ? 'library' : 'discover');
