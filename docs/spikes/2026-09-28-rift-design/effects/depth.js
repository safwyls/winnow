'use strict';

(() => {
  const enabled = document.querySelector('#card-floating');
  const amount = document.querySelector('#card-tilt');
  const motion = window.riftMotion;
  const follow = document.querySelector('#finish-follow');
  const selector = '.poster-grid:not(.records) .game-card';
  const properties = ['--tilt-x', '--tilt-y', '--shadow-x', '--shadow-y'];
  let active, source = 'pointer', pending = 0, pointerX = 0, pointerY = 0;

  function clear(card) {
    if (!card) return;
    card.classList.remove('is-floating');
    delete card.dataset.depthInput;
    for (const property of properties) card.style.removeProperty(property);
  }

  function stop() {
    cancelAnimationFrame(pending);
    pending = 0;
    clear(active);
    active = null;
  }

  function cardFor(target) {
    const card = target instanceof Element ? target.closest(selector) : null;
    const image = card?.querySelector('.cover img');
    return image?.complete && image.naturalWidth > 0 ? card : null;
  }

  function paint() {
    pending = 0;
    if (!active?.isConnected || active.closest('[hidden]') || document.querySelector('dialog[open]')) { stop(); return; }
    const still = source === 'keyboard' || motion.matches || !follow.checked;
    // Read only the untransformed button, so a moving surface cannot feed back into its tilt.
    const box = active.getBoundingClientRect();
    const nx = still ? 0 : Math.max(-1, Math.min(1, (pointerX - box.left) / box.width * 2 - 1));
    const ny = still ? 0 : Math.max(-1, Math.min(1, (pointerY - box.top) / box.height * 2 - 1));
    const strength = Number(amount.value);
    active.style.setProperty('--tilt-x', `${(-ny * strength).toFixed(2)}deg`);
    active.style.setProperty('--tilt-y', `${(nx * strength).toFixed(2)}deg`);
    active.style.setProperty('--shadow-x', `${(-nx * 9).toFixed(2)}px`);
    active.style.setProperty('--shadow-y', `${(18 - ny * 6).toFixed(2)}px`);
    active.dataset.depthInput = still ? 'still' : 'pointer';
    active.classList.add('is-floating');
  }

  function activate(card, input, clientX = 0, clientY = 0) {
    if (!enabled.checked || document.hidden || document.querySelector('dialog[open]')) return;
    if (card !== active) { stop(); active = card; }
    source = input;
    pointerX = clientX; pointerY = clientY;
    if (!pending) pending = requestAnimationFrame(paint);
  }

  function focusCurrent() {
    const focused = document.activeElement;
    const card = cardFor(focused);
    if (card && focused.matches(':focus-visible')) activate(card, 'keyboard');
  }

  document.addEventListener('pointermove', event => {
    if (event.pointerType === 'touch') return;
    const card = cardFor(event.target);
    if (card) activate(card, 'pointer', event.clientX, event.clientY);
    else if (source === 'pointer') { stop(); focusCurrent(); }
  }, { passive: true });
  document.addEventListener('pointerout', event => { if (!event.relatedTarget) stop(); });
  document.addEventListener('focusin', event => {
    const card = cardFor(event.target);
    if (card && event.target.matches(':focus-visible')) activate(card, 'keyboard');
    else stop();
  });
  document.addEventListener('focusout', event => { if (source === 'keyboard' && event.target === active) stop(); });
  document.addEventListener('load', event => {
    if (event.target instanceof HTMLImageElement && document.activeElement?.contains(event.target)) focusCurrent();
  }, { capture: true });
  document.addEventListener('scroll', () => { if (source === 'pointer') stop(); }, { capture: true, passive: true });
  document.addEventListener('visibilitychange', stop);
  window.addEventListener('blur', stop);
  window.addEventListener('focus', focusCurrent);
  window.addEventListener('resize', () => { stop(); focusCurrent(); });

  function updateOptions() {
    stop();
    document.querySelector('#tilt-amount').value = `${amount.value}°`;
    amount.disabled = !enabled.checked;
    document.body.dataset.depthMotion = motion.matches || !follow.checked ? 'still' : 'follow';
    focusCurrent();
  }
  enabled.addEventListener('change', updateOptions);
  amount.addEventListener('input', updateOptions);
  follow.addEventListener('change', updateOptions);
  motion.addEventListener('change', updateOptions);
  document.querySelector('#reset-options').addEventListener('click', () => {
    enabled.checked = true; amount.value = '7'; updateOptions();
  });
  updateOptions();

  const changes = new MutationObserver(() => {
    if (active && (!active.isConnected || active.closest('[hidden]') || !active.matches(selector)
        || document.querySelector('dialog[open]'))) stop();
  });
  changes.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['hidden', 'open', 'class'] });
  window.addEventListener('pagehide', event => {
    stop();
    if (!event.persisted) changes.disconnect();
  });
})();
