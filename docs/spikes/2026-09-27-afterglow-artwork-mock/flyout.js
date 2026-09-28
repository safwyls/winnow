'use strict';

(() => {
  const panel = document.querySelector('#card-flyout');
  const selector = '.poster-grid:not(.records) .game-card';
  const portal = new WinnowPortalSurface(panel);
  const motion = document.querySelector('#preview-motion');
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
    panel.style.maxHeight = `${Math.max(80, (bottomEdge - topEdge) * (docked ? 0.55 : 1))}px`;
    panel.style.setProperty('--flyout-max-height', panel.style.maxHeight);
    const height = panel.offsetHeight, width = panel.offsetWidth;
    const top = docked ? bottomEdge - height
      : Math.max(topEdge, Math.min(bottomEdge - height, box.top + box.height * 0.38 - height * 0.5));
    const left = docked ? (innerWidth - width) * 0.5 : side === 'right' ? box.right + gap : box.left - gap - width;
    panel.style.left = `${Math.round(left)}px`;
    panel.style.top = `${Math.round(top)}px`;
    portal.resize();
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
    panel.innerHTML = `${isPortal ? '<div class="portal-scene" aria-hidden="true"><div class="portal-fallback"></div></div>' : ''}<div class="flyout-scroll"><div class="flyout-body"><span class="eyebrow">In your collection</span>
      <h3>${escapeHtml(game.title)}</h3>
      <div class="flyout-meta"><span>${escapeHtml(game.store)}</span><span aria-hidden="true">·</span><span>${game.time === 'Unplayed' ? 'Never played' : `${escapeHtml(game.time)} played`}</span>${game.installed ? '<span class="flyout-installed">Installed</span>' : ''}</div>
      <p class="flyout-reason">${escapeHtml(game.reason)}</p>
      <p class="flyout-description">${escapeHtml(game.description || 'No description available yet.')}</p>
      <div class="flyout-hint">Select the cover to view game <span aria-hidden="true">↗</span></div></div></div>`;
    panel.hidden = false;
    panel.scrollTop = 0;
    card.setAttribute('aria-describedby', panel.id);
    position();
    const origin = pointer || {x: card.getBoundingClientRect().right, y: card.getBoundingClientRect().top};
    panel.style.setProperty('--portal-from-x', `${origin.x - panel.offsetLeft - panel.offsetWidth / 2}px`);
    panel.style.setProperty('--portal-from-y', `${origin.y - panel.offsetTop - panel.offsetHeight / 2}px`);
    if (isPortal && active) void portal.show(moving);
  }

  function queue(card, input) {
    clearTimeout(hideTimer);
    if (card === active || card === pendingCard || card === dismissed) return;
    clearTimeout(showTimer);
    pendingCard = card;
    if (input === 'keyboard') show(card, input);
    else showTimer = setTimeout(() => show(card, input), 180);
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
    const scroller = panel.hasAttribute('data-portal') ? panel.querySelector('.flyout-scroll') : panel;
    if (active && ['PageDown', 'PageUp'].includes(event.key) && scroller.scrollHeight > scroller.clientHeight) {
      scroller.scrollBy({top: scroller.clientHeight * (event.key === 'PageDown' ? 0.8 : -0.8)});
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
  motion.addEventListener('change', hide);
  document.querySelector('#reset-options').addEventListener('click', () => { motion.value='live'; hide(); });
  const changes = new MutationObserver(() => {
    const card = active || pendingCard;
    if (card && (!card.isConnected || card.closest('[hidden]') || !card.matches(selector)
        || document.querySelector('dialog[open]'))) hide();
  });
  changes.observe(document.body, {subtree:true, childList:true, attributes:true, attributeFilter:['hidden','open','class']});
  window.addEventListener('pagehide', event => { hide(); if (!event.persisted) { changes.disconnect(); portal.dispose(); } });
})();
