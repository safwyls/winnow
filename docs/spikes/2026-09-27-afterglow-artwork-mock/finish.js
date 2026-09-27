'use strict';

(() => {
  const motion = matchMedia('(prefers-reduced-motion: reduce)');
  const control = document.querySelector('#cover-finish');
  const intensity = document.querySelector('#finish-intensity');
  const follow = document.querySelector('#finish-follow');
  const help = document.querySelector('#finish-help');
  const selector = '.poster-grid:not(.records) .game-card, .return-item';
  const textures = new Map();
  let app, filter, plane, initialization, active, failed = false, disposed = false;
  let generation = 0, reveal = 0, goal = 1, x = 0, y = 0, targetX = 0, targetY = 0;
  let width = 0, height = 0, input = 'pointer';

  const vertex = `
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

  const fragment = `
    precision highp float;
    in vec2 vTextureCoord;
    out vec4 finalColor;
    uniform vec4 uInputSize;
    uniform sampler2D uArtwork;
    uniform vec2 uArtworkSize;
    uniform vec2 uSize;
    uniform vec2 uCursor;
    uniform float uReveal;
    uniform float uIntensity;
    uniform float uFinish;

    float luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

    void main() {
      vec2 p = vTextureCoord * uInputSize.xy;
      vec2 uv = p / uSize;
      // Match the DOM cover's centered crop exactly. Light never displaces the print.
      float fit = max(uSize.x / uArtworkSize.x, uSize.y / uArtworkSize.y);
      vec2 artUv = clamp((p - uSize * 0.5) / (uArtworkSize * fit) + 0.5, 0.001, 0.999);
      vec3 ink = texture(uArtwork, artUv).rgb;
      float luma = luminance(ink);
      vec2 cursor = uCursor / uSize;
      vec2 delta = (p - uCursor) / uSize.x;
      float light = exp(-dot(delta, delta) * 1.8);
      vec3 pigment = mix(vec3(luma), ink, 1.0 + light * 0.12 * uIntensity);
      vec3 lit = pigment * (1.0 + light * 0.17 * uIntensity);

      // Fine contrast in the artwork breaks the reflection, like printed ink under satin.
      vec2 texel = 1.5 / uArtworkSize;
      float grain = abs(luminance(texture(uArtwork, artUv + vec2(texel.x, 0.0)).rgb) - luma)
                  + abs(luminance(texture(uArtwork, artUv + vec2(0.0, texel.y)).rgb) - luma);
      float slope = -0.35 + (cursor.x - 0.5) * 0.25;
      float diagonal = (uv.x - cursor.x) + (uv.y - cursor.y) * slope;
      float satin = exp(-diagonal * diagonal * 18.0) * light;
      float gloss = satin * (0.20 + min(grain, 0.2) * 0.5);
      vec3 reflection = mix(vec3(0.92, 0.86, 0.77), normalize(ink + 0.25), 0.34);

      if (uFinish > 1.5) {
        float spectrum = diagonal * 5.8 + cursor.x * 1.7 - cursor.y * 0.9;
        vec3 iridescence = 0.64 + 0.36 * cos(spectrum + vec3(0.0, 2.1, 4.2));
        reflection = mix(reflection, iridescence, 0.65);
        gloss = satin * 0.40 + pow(satin, 5.0) * 0.12;
      }
      if (uFinish < 0.5) gloss = 0.0;

      // Screen-like reflection preserves deep pigments and avoids blowing out white logos.
      lit += (1.0 - lit) * reflection * gloss * uIntensity * (1.0 - luma * 0.35);
      float edge = min(min(p.x, uSize.x - p.x), min(p.y, uSize.y - p.y));
      float glancing = exp(-max(edge, 0.0) * 0.75) * light * step(0.5, uFinish);
      lit += (1.0 - lit) * reflection * glancing * 0.16 * uIntensity;
      finalColor = vec4(clamp(lit, 0.0, 1.0) * uReveal, uReveal);
    }
  `;

  function isStill() { return motion.matches || !follow.checked || input === 'keyboard'; }
  function enabled() { return !failed && !disposed && control.value !== 'off' && Number(intensity.value) > 0; }

  function stop() {
    generation++;
    active = null;
    resize.disconnect();
    if (!app) return;
    app.stop();
    app.canvas.dataset.status = 'idle';
    app.canvas.remove();
  }

  function leave() {
    if (!active || goal === 0) return;
    generation++;
    if (isStill() || !app?.canvas.isConnected) { stop(); return; }
    goal = 0;
    app.canvas.dataset.status = 'fading';
    app.start();
  }

  function unavailable(error) {
    stop();
    failed = true;
    help.textContent = 'Cover lighting is unavailable here. The original artwork and captions remain available.';
    console.warn('Cover finish disabled:', error);
  }

  async function initialize() {
    if (!initialization) initialization = (async () => {
      const instance = new PIXI.Application();
      await instance.init({
        width: 1, height: 1, backgroundAlpha: 0, preference: 'webgl',
        resolution: Math.min(devicePixelRatio || 1, 2), autoDensity: true,
        autoStart: false, sharedTicker: false, antialias: false,
        eventFeatures: { move: false, globalMove: false, click: false, wheel: false },
      });
      if (disposed) { instance.destroy({ removeView: true, releaseGlobalResources: true }); return; }
      app = instance;
      app.stage.eventMode = 'none';
      app.canvas.className = 'finish-canvas';
      app.canvas.setAttribute('aria-hidden', 'true');
      app.canvas.addEventListener('webglcontextlost', () => unavailable('Graphics context lost'));
      plane = new PIXI.Sprite(PIXI.Texture.WHITE);
      filter = PIXI.Filter.from({
        gl: { vertex, fragment }, resolution: 'inherit', padding: 0,
        resources: {
          uArtwork: PIXI.Texture.WHITE.source,
          finishUniforms: {
            uArtworkSize: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
            uSize: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
            uCursor: { value: new Float32Array([0, 0]), type: 'vec2<f32>' },
            uReveal: { value: 0, type: 'f32' },
            uIntensity: { value: 0.55, type: 'f32' },
            uFinish: { value: 1, type: 'f32' },
          },
        },
      });
      plane.filters = [filter];
      app.stage.addChild(plane);
      app.ticker.maxFPS = 60;
      app.ticker.add(tick);
    })();
    return initialization;
  }

  function draw() {
    const uniforms = filter.resources.finishUniforms.uniforms;
    uniforms.uCursor.set([x, y]);
    uniforms.uReveal = reveal;
    uniforms.uIntensity = Number(intensity.value) / 100;
    uniforms.uFinish = { matte: 0, satin: 1, foil: 2 }[control.value] ?? 0;
  }

  function tick(ticker) {
    if (!active?.isConnected || active.closest('[hidden]') || document.querySelector('dialog[open]')) { stop(); return; }
    const seconds = Math.min(ticker.deltaMS, 50) / 1000;
    const ease = 1 - Math.exp(-seconds * 16);
    x += (targetX - x) * ease;
    y += (targetY - y) * ease;
    reveal += (goal - reveal) * ease;
    draw();
    if (Math.abs(x - targetX) + Math.abs(y - targetY) < 0.15 && Math.abs(reveal - goal) < 0.005) {
      if (!goal) { stop(); return; }
      x = targetX; y = targetY; reveal = goal;
      draw(); app.render(); app.stop();
      app.canvas.dataset.status = 'settled';
    }
  }

  const resize = new ResizeObserver(entries => {
    if (!active || !app?.canvas.isConnected) return;
    const box = entries[0].contentRect;
    if (Math.abs(box.width - width) > 1 || Math.abs(box.height - height) > 1) {
      const cover = active;
      const restore = isStill();
      stop();
      if (restore) void activate(cover, 'keyboard');
    }
  });

  async function activate(cover, source, clientX, clientY) {
    if (!enabled() || document.hidden || document.querySelector('dialog[open]')) return;
    const bounds = cover.getBoundingClientRect();
    if (!bounds.width || !bounds.height) return;
    if (source === 'pointer' && (clientX < bounds.left || clientX > bounds.right || clientY < bounds.top || clientY > bounds.bottom)) { leave(); return; }
    input = source;
    targetX = isStill() ? bounds.width * 0.5 : clientX - bounds.left;
    targetY = isStill() ? bounds.height * 0.36 : clientY - bounds.top;
    goal = 1;
    if (active === cover && app?.canvas.isConnected) {
      if (isStill()) {
        if (app.canvas.dataset.status === 'still') return;
        app.stop(); x = targetX; y = targetY; reveal = 1;
        app.canvas.dataset.status = 'still';
        draw(); app.render();
      } else if (Math.abs(x - targetX) + Math.abs(y - targetY) > 0.15 || reveal < 0.995) {
        app.canvas.dataset.status = 'following';
        app.start();
      }
      return;
    }
    if (active === cover) return;
    stop();
    active = cover;
    const request = generation;
    x = targetX; y = targetY; reveal = isStill() ? 1 : 0;
    width = bounds.width; height = bounds.height;
    try {
      await initialize();
      if (generation !== request || disposed || !cover.isConnected) return;
      const image = cover.querySelector('img');
      const key = image.currentSrc;
      // Reuse the decoded DOM image. There is no second artwork request or image swap.
      if (!textures.has(key)) textures.set(key, PIXI.Texture.from(image, true));
      const texture = textures.get(key);
      filter.resources.uArtwork = texture.source;
      const uniforms = filter.resources.finishUniforms.uniforms;
      uniforms.uArtworkSize.set([image.naturalWidth, image.naturalHeight]);
      uniforms.uSize.set([width, height]);
      plane.width = width; plane.height = height;
      plane.filterArea = new PIXI.Rectangle(0, 0, width, height);
      app.renderer.resize(width, height);
      app.canvas.dataset.status = isStill() ? 'still' : 'following';
      app.canvas.dataset.finish = control.value;
      cover.append(app.canvas);
      resize.observe(cover);
      draw(); app.render();
      if (!isStill()) app.start();
    } catch (error) { if (generation === request) unavailable(error); }
  }

  function coverFor(target) {
    if (!(target instanceof Element)) return null;
    const cover = target.closest(selector)?.querySelector('.cover');
    const image = cover?.querySelector('img');
    return image?.complete && image.naturalWidth > 0 ? cover : null;
  }

  document.addEventListener('pointermove', event => {
    if (event.pointerType === 'touch') return;
    const cover = coverFor(event.target);
    if (cover) void activate(cover, 'pointer', event.clientX, event.clientY);
    else if (input === 'pointer') leave();
  }, { passive: true });
  document.addEventListener('focusin', event => {
    const cover = coverFor(event.target);
    if (cover && event.target.matches(':focus-visible')) void activate(cover, 'keyboard');
    else stop();
  });
  document.addEventListener('load', event => {
    const focused = document.activeElement;
    if (event.target instanceof HTMLImageElement && focused?.matches(selector)
        && focused.matches(':focus-visible') && focused.contains(event.target)) {
      const cover = coverFor(focused);
      if (cover) void activate(cover, 'keyboard');
    }
  }, { capture: true });
  document.addEventListener('focusout', () => { if (input === 'keyboard') stop(); });
  document.addEventListener('pointerout', event => { if (!event.relatedTarget) stop(); });
  document.addEventListener('pointerdown', stop, { capture: true });
  document.addEventListener('scroll', () => { if (input === 'pointer') stop(); }, { capture: true, passive: true });
  document.addEventListener('visibilitychange', stop);
  window.addEventListener('blur', stop);
  window.addEventListener('focus', () => {
    const focused = document.activeElement;
    const cover = coverFor(focused);
    if (cover && focused.matches(':focus-visible')) void activate(cover, 'keyboard');
  });

  function updateOptions() {
    stop();
    document.querySelector('#finish-amount').value = `${intensity.value}%`;
    document.body.dataset.finishMotion = motion.matches || !follow.checked ? 'still' : 'follow';
    intensity.disabled = control.value === 'off';
    follow.disabled = control.value === 'off';
  }
  motion.addEventListener('change', updateOptions);
  control.addEventListener('change', updateOptions);
  intensity.addEventListener('input', updateOptions);
  follow.addEventListener('change', updateOptions);
  document.querySelector('#reset-options').addEventListener('click', () => {
    control.value = 'satin'; intensity.value = '55'; follow.checked = true; updateOptions();
  });
  updateOptions();

  const changes = new MutationObserver(() => {
    if (active && (!active.isConnected || active.closest('[hidden]') || document.querySelector('dialog[open]'))) stop();
  });
  changes.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['hidden', 'open'] });
  window.addEventListener('pagehide', event => {
    stop();
    if (event.persisted) return;
    disposed = true;
    changes.disconnect();
    filter?.destroy();
    app?.destroy({ removeView: true, releaseGlobalResources: true }, { children: true });
    for (const texture of textures.values()) texture.destroy(true);
    textures.clear();
  });
})();
