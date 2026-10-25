'use strict';

(() => {
  const panel = document.querySelector('#card-flyout');
  const selector = '.poster-grid:not(.records) .game-card';
  const portal = new WinnowPortalSurface(panel);
  const motion = document.querySelector('#preview-motion');
  const roundness = document.querySelector('#portal-roundness');
  const waviness = document.querySelector('#portal-waviness');
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let pointer = null;
  let active, pendingCard, dismissed, showTimer, hideTimer, frame;
  let source = 'pointer';

  function cardFor(target) {
    return target instanceof Element ? target.closest(selector) : null;
  }

  function hide() {
    clearTimeout(showTimer); clearTimeout(hideTimer); cancelAnimationFrame(frame);
    active?.removeAttribute('aria-describedby');
    active = pendingCard = null;
    portal.hide();
    panel.hidden = true;
  }

  function position() {
    if (!active?.isConnected || active.closest('[hidden]') || !active.matches(selector)
        || document.querySelector('dialog[open]')) { hide(); return; }
    const box = active.getBoundingClientRect();
    const gutter = 14, gap = 22;
    const topEdge = document.querySelector('.app-bar').getBoundingClientRect().bottom + gutter;
    const bottomEdge = document.querySelector('.app-footer').getBoundingClientRect().top - gutter;
    if (box.bottom <= topEdge || box.top >= bottomEdge) { hide(); return; }
    const availableRight = innerWidth - gutter - box.right - gap;
    const availableLeft = box.left - gutter - gap;
    const isPortal = motion.value !== 'plain';
    const preferredWidth = document.body.dataset.mode === 'fullscreen' ? (isPortal ? 460 : 370) : (isPortal ? 430 : 340);
    const side = availableRight >= preferredWidth || availableRight >= availableLeft ? 'right' : 'left';
    const available = side === 'right' ? availableRight : availableLeft;
    const docked = available < 240;
    panel.dataset.placement = docked ? 'docked' : side;
    panel.style.width = `${docked ? Math.min(370, innerWidth - gutter * 2) : Math.min(preferredWidth, available)}px`;
    const availableHeight = Math.max(80, (bottomEdge - topEdge) * (docked ? (isPortal ? 0.85 : 0.55) : 1));
    panel.style.maxHeight = `${availableHeight}px`;
    panel.style.height = isPortal ? `${Math.min(document.body.dataset.mode === 'fullscreen' ? 620 : 560, availableHeight)}px` : '';
    panel.dataset.compact = String(isPortal && (availableHeight < 500 || parseFloat(panel.style.width) < 330));
    panel.dataset.short = String(isPortal && availableHeight < 350);
    const height = panel.offsetHeight, width = panel.offsetWidth;
    const top = docked ? bottomEdge - height
      : Math.max(topEdge, Math.min(bottomEdge - height, box.top + box.height * 0.38 - height * 0.5));
    const left = docked ? (innerWidth - width) * 0.5 : side === 'right' ? box.right + gap : box.left - gap - width;
    panel.style.left = `${Math.round(left)}px`;
    panel.style.top = `${Math.round(top)}px`;
    if(isPortal) fitDescription();
    portal.resize();
  }

  function fitDescription() {
    const description=panel.querySelector('.flyout-description');
    description.style.webkitLineClamp='unset';
    const lines=Math.max(1,Math.floor(description.clientHeight/parseFloat(getComputedStyle(description).lineHeight)));
    description.style.webkitLineClamp=String(lines);
  }

  function show(card, input) {
    if (!card?.isConnected || card === dismissed || card.closest('[hidden]') || document.querySelector('dialog[open]')) return;
    const game = byId.get(card.dataset.game);
    if (!game) return;
    hide(); active = card; source = input;
    const isPortal = motion.value !== 'plain';
    const moving = isPortal && input === 'pointer' && motion.value === 'live' && !reduced.matches;
    if (isPortal) panel.dataset.portal = ''; else delete panel.dataset.portal;
    panel.dataset.motion = moving ? 'live' : 'still';
    panel.dataset.input = input;
    panel.dataset.game = game.id;
    panel.innerHTML = `${isPortal ? '<div class="portal-scene" aria-hidden="true"><div class="portal-fallback"></div></div>' : ''}<div class="${isPortal ? 'portal-content' : 'flyout-content'}"><div class="flyout-body"><span class="eyebrow">In your collection</span>
      <h3>${escapeHtml(game.title)}</h3>
      <div class="flyout-meta"><span>${escapeHtml(game.store)}</span><span aria-hidden="true">·</span><span>${game.time === 'Unplayed' ? 'Never played' : `${escapeHtml(game.time)} played`}</span>${game.installed ? '<span class="flyout-installed">Installed</span>' : ''}</div>
      <p class="flyout-reason">${escapeHtml(game.reason)}</p>
      <p class="flyout-description">${escapeHtml(game.description || 'No description available yet.')}</p>
      <div class="flyout-hint">Select the cover for full details <span aria-hidden="true">↗</span></div></div></div>`;
    panel.hidden = false;
    panel.scrollTop = 0;
    card.setAttribute('aria-describedby', panel.id);
    position();
    const origin = pointer || {x: card.getBoundingClientRect().right, y: card.getBoundingClientRect().top};
    if (isPortal && active) void portal.show(moving, {x:origin.x-panel.offsetLeft,y:origin.y-panel.offsetTop}, portalArtwork(game));
  }

  function queue(card, input) {
    clearTimeout(hideTimer);
    if (card === active || card === pendingCard || card === dismissed) return;
    clearTimeout(showTimer);
    pendingCard = card;
    if (input === 'keyboard') show(card, input);
    else {
      if(motion.value!=='plain') void portal.prepare(portalArtwork(byId.get(card.dataset.game)));
      showTimer = setTimeout(() => show(card, input), 120);
    }
  }

  function leave() {
    clearTimeout(showTimer); pendingCard = null;
    clearTimeout(hideTimer);
    hideTimer = setTimeout(hide, 180);
  }

  document.addEventListener('pointerover', event => {
    if (event.pointerType === 'touch') return;
    pointer = {x:event.clientX,y:event.clientY};
    if (panel.contains(event.target)) { clearTimeout(showTimer); clearTimeout(hideTimer); pendingCard = null; return; }
    const card = cardFor(event.target);
    if (card && !card.contains(event.relatedTarget)) queue(card, 'pointer');
  });
  document.addEventListener('pointermove', event => {
    if(event.pointerType !== 'touch' && (active || pendingCard)) pointer = {x:event.clientX,y:event.clientY};
  }, {passive:true});
  document.addEventListener('pointerout', event => {
    const card = cardFor(event.target);
    if (card && !card.contains(event.relatedTarget)) {
      if (dismissed === card) dismissed = null;
      if (!panel.contains(event.relatedTarget)) leave();
    } else if (panel.contains(event.target) && !panel.contains(event.relatedTarget)
        && !active?.contains(event.relatedTarget)) leave();
  });
  document.addEventListener('focusin', event => {
    const card = cardFor(event.target);
    if (card && event.target.matches(':focus-visible')) queue(card, 'keyboard');
    else hide();
  });
  document.addEventListener('focusout', event => {
    if (dismissed === event.target) dismissed = null;
    if (source === 'keyboard') hide();
  });
  // Consume Escape before the page's library-to-Discover shortcut.
  document.addEventListener('keydown', event => {
    if (active && !panel.hasAttribute('data-portal') && ['PageDown', 'PageUp'].includes(event.key) && panel.scrollHeight > panel.clientHeight) {
      panel.scrollBy({top: panel.clientHeight * (event.key === 'PageDown' ? 0.8 : -0.8)});
      event.preventDefault(); event.stopImmediatePropagation(); return;
    }
    if (event.key !== 'Escape' || (!active && !pendingCard)) return;
    dismissed = active || pendingCard;
    hide(); event.preventDefault(); event.stopImmediatePropagation();
  }, true);
  document.addEventListener('scroll', event => {
    if (panel.contains(event.target)) return;
    if (source === 'keyboard' && active) { cancelAnimationFrame(frame); frame = requestAnimationFrame(position); }
    else hide();
  }, { capture: true, passive: true });
  document.addEventListener('visibilitychange', hide);
  window.addEventListener('blur', hide);
  window.addEventListener('resize', hide);
  reduced.addEventListener('change', () => { if(active) show(active,source); });
  function updateShape() {
    portal.configure(Number(roundness.value),Number(waviness.value));
    document.querySelector('#portal-roundness-amount').value=`${roundness.value}%`;
    document.querySelector('#portal-waviness-amount').value=`${waviness.value}%`;
    const points=WinnowPortalSurface.contour(160,66,112,51,portal.exponent,portal.wave*.45,0);
    document.querySelector('#portal-shape-outline').setAttribute('points',points.map(point=>point.join(',')).join(' '));
    const disabled=motion.value==='plain';
    roundness.disabled=waviness.disabled=disabled;
    document.querySelector('.portal-shape-preview').hidden=disabled;
  }
  roundness.addEventListener('input',updateShape);
  waviness.addEventListener('input',updateShape);
  motion.addEventListener('change',()=>{hide();updateShape();});
  document.querySelector('#reset-options').addEventListener('click', () => {
    motion.value='live'; roundness.value='70'; waviness.value='45'; hide(); updateShape();
  });
  updateShape();
  document.fonts.ready.then(() => { if(active) position(); });
  const changes = new MutationObserver(() => {
    const card = active || pendingCard;
    if (card && (!card.isConnected || card.closest('[hidden]') || !card.matches(selector)
        || document.querySelector('dialog[open]'))) hide();
  });
  changes.observe(document.body, {subtree:true, childList:true, attributes:true, attributeFilter:['hidden','open','class']});
  window.addEventListener('pagehide', event => { hide(); if (!event.persisted) { changes.disconnect(); portal.dispose(); } });
})();
