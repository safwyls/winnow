'use strict';

(() => {
  // One renderer serves the active cover. The rest remain ordinary, cached images.
  const scenes = {
    borderlands2: 'borderlands2-scene', celeste: 'celeste-scene', disco: 'disco-scene',
    enshrouded: 'enshrouded-scene', hades: 'hades-hero', hollow: 'hollow-scene',
    outer: 'outer-hero',
  };
  const motion = matchMedia('(prefers-reduced-motion: reduce)');
  const control = document.querySelector('#portal-mode');
  const help = document.querySelector('#portal-help');
  const selector = '.poster-grid:not(.records) .game-card, .return-item';
  let app, filter, plane, initialization, active, failed = false;
  let generation = 0, phase = 0, reveal = 0, x = 0, y = 0, targetX = 0, targetY = 0;
  let width = 0, height = 0, input = 'pointer';
  const loaded = new Set();

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
    uniform sampler2D uScene;
    uniform vec2 uSceneSize;
    uniform vec2 uSize;
    uniform vec2 uCursor;
    uniform float uTime;
    uniform float uReveal;
    uniform float uHasScene;

    float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

    void main() {
      vec2 p = vTextureCoord * uInputSize.xy;
      vec2 delta = p - uCursor;
      float angle = atan(delta.y, delta.x);
      float radius = min(uSize.x * 0.32, 100.0) * (0.65 + 0.35 * uReveal);
      float wave = sin(angle * 3.0 + uTime * 0.77) * 0.09
                 + sin(angle * 5.0 - uTime * 0.52) * 0.055
                 + sin(angle * 2.0 - uTime * 0.36) * 0.075;
      float distanceToEdge = length(delta) - radius * (1.0 + wave);
      float inside = 1.0 - smoothstep(-1.8, 1.8, distanceToEdge);

      // Reveal a second image, with a little lens distortion near the liquid rim.
      float fit = max(uSize.x / uSceneSize.x, uSize.y / uSceneSize.y);
      vec2 fitted = uSceneSize * fit;
      vec2 pan = (uCursor / uSize - 0.5) * max(fitted - uSize, vec2(0.0)) * 0.65;
      vec2 warp = delta / max(length(delta), 1.0) * sin(distanceToEdge * 0.12 - uTime)
                * exp(-abs(distanceToEdge) * 0.07) * 3.5;
      vec2 sceneUv = clamp((p - uSize * 0.5 + pan + warp) / fitted + 0.5, 0.001, 0.999);
      vec3 scene = texture(uScene, sceneUv).rgb;

      // A quiet, procedural night sky is the fallback where no second artwork exists.
      vec2 sky = p / max(uSize.x, 1.0);
      float cloud = sin(sky.x * 5.0 + sky.y * 7.0 + uTime * 0.06)
                  * sin(sky.y * 9.0 - sky.x * 3.0 - uTime * 0.08);
      vec3 cosmos = mix(vec3(0.018, 0.031, 0.075), vec3(0.15, 0.23, 0.30), cloud * 0.5 + 0.5);
      vec2 cell = floor(p / 15.0);
      float seed = hash(cell);
      vec2 star = fract(p / 15.0) - vec2(hash(cell + 7.0), hash(cell + 19.0));
      float light = (1.0 - smoothstep(0.015, 0.08, length(star))) * step(0.83, seed);
      cosmos += vec3(0.77, 0.85, 1.0) * light;
      vec3 interior = mix(cosmos, scene, uHasScene);
      interior *= 0.90 + 0.10 * smoothstep(0.0, 18.0, -distanceToEdge);

      float glow = exp(-abs(distanceToEdge) * 0.12) * 0.32;
      float rim = exp(-abs(distanceToEdge) * 0.65);
      vec3 edge = mix(vec3(0.43, 0.69, 0.86), vec3(1.0, 0.69, 0.43),
                      sin(angle * 2.0 + uTime * 0.24) * 0.5 + 0.5);
      float alpha = max(inside, max(glow, rim * 0.85)) * uReveal;
      vec3 color = interior * inside + edge * (glow + rim * 0.80);
      // Pixi's filter output uses premultiplied alpha, including the soft outer glow.
      finalColor = vec4(color * uReveal, alpha);
    }
  `;

  function isStill() { return motion.matches || control.value === 'still' || input === 'keyboard'; }

  function stop() {
    generation++;
    active = null;
    resize.disconnect();
    if (!app) return;
    app.stop();
    app.canvas.dataset.status = 'idle';
    app.canvas.remove();
  }

  function unavailable(error) {
    stop();
    failed = true;
    help.textContent = 'Portals are unavailable in this browser. Artwork and captions are still available.';
    console.warn('Cover portal disabled:', error);
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
      app = instance;
      app.stage.eventMode = 'none';
      app.canvas.className = 'portal-canvas';
      app.canvas.setAttribute('aria-hidden', 'true');
      app.canvas.addEventListener('webglcontextlost', () => unavailable('Graphics context lost'));
      plane = new PIXI.Sprite(PIXI.Texture.WHITE);
      filter = PIXI.Filter.from({
        gl: { vertex, fragment }, resolution: 'inherit', padding: 0,
        resources: {
          uScene: PIXI.Texture.WHITE.source,
          portalUniforms: {
            uSceneSize: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
            uSize: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
            uCursor: { value: new Float32Array([0, 0]), type: 'vec2<f32>' },
            uTime: { value: 0, type: 'f32' },
            uReveal: { value: 0, type: 'f32' },
            uHasScene: { value: 0, type: 'f32' },
          },
        },
      });
      plane.filters = [filter];
      app.stage.addChild(plane);
      app.ticker.maxFPS = 45;
      app.ticker.add(tick);
    })();
    return initialization;
  }

  function draw() {
    const uniforms = filter.resources.portalUniforms.uniforms;
    uniforms.uCursor[0] = x;
    uniforms.uCursor[1] = y;
    uniforms.uTime = isStill() ? 1.8 : phase;
    uniforms.uReveal = reveal;
  }

  function tick(ticker) {
    if (!active?.isConnected || active.closest('[hidden]') || document.querySelector('dialog[open]')) { stop(); return; }
    const seconds = Math.min(ticker.deltaMS, 50) / 1000;
    const follow = 1 - Math.exp(-seconds * 20);
    x += (targetX - x) * follow;
    y += (targetY - y) * follow;
    reveal += (1 - reveal) * follow;
    phase += seconds;
    draw();
  }

  const resize = new ResizeObserver(entries => {
    if (!active || !app?.canvas.isConnected) return;
    const box = entries[0].contentRect;
    if (Math.abs(box.width - width) > 1 || Math.abs(box.height - height) > 1) stop();
  });

  async function activate(cover, source, clientX, clientY) {
    if (failed || control.value === 'off' || document.hidden || document.querySelector('dialog[open]')) return;
    const bounds = cover.getBoundingClientRect();
    if (!bounds.width || !bounds.height) return;
    if (source === 'pointer' && (clientX < bounds.left || clientX > bounds.right || clientY < bounds.top || clientY > bounds.bottom)) { stop(); return; }
    input = source;
    targetX = source === 'keyboard' ? bounds.width * 0.5 : clientX - bounds.left;
    targetY = source === 'keyboard' ? bounds.height * 0.40 : clientY - bounds.top;
    if (active === cover) {
      if (app?.canvas.isConnected && isStill()) {
        app.stop(); x = targetX; y = targetY; reveal = 1;
        app.canvas.dataset.status = 'still';
        draw(); app.render();
      } else if (app?.canvas.isConnected) {
        app.canvas.dataset.status = 'flowing';
        app.start();
      }
      return;
    }
    stop();
    active = cover;
    const request = generation;
    x = targetX; y = targetY; reveal = isStill() ? 1 : 0;
    width = bounds.width; height = bounds.height;
    try {
      await initialize();
      if (generation !== request) return;
      const id = cover.closest('[data-game]').dataset.game;
      const url = scenes[id] ? `assets/${scenes[id]}.jpg` : null;
      const texture = url ? await PIXI.Assets.load(url) : PIXI.Texture.WHITE;
      if (url) loaded.add(url);
      if (generation !== request || !cover.isConnected) return;
      filter.resources.uScene = texture.source;
      const uniforms = filter.resources.portalUniforms.uniforms;
      uniforms.uSceneSize.set([texture.width, texture.height]);
      uniforms.uSize.set([width, height]);
      uniforms.uHasScene = url ? 1 : 0;
      plane.width = width; plane.height = height;
      plane.filterArea = new PIXI.Rectangle(0, 0, width, height);
      app.renderer.resize(width, height);
      app.canvas.dataset.status = isStill() ? 'still' : 'flowing';
      cover.append(app.canvas);
      resize.observe(cover);
      draw(); app.render();
      if (!isStill()) app.start();
    } catch (error) { unavailable(error); }
  }

  function coverFor(target) {
    if (!(target instanceof Element)) return null;
    const card = target.closest(selector);
    const cover = card?.querySelector('.cover');
    const image = cover?.querySelector('img');
    return image?.complete && image.naturalWidth > 0 ? cover : null;
  }

  document.addEventListener('pointermove', event => {
    if (event.pointerType === 'touch') return;
    const cover = coverFor(event.target);
    if (cover) void activate(cover, 'pointer', event.clientX, event.clientY);
    else if (input === 'pointer') stop();
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
  motion.addEventListener('change', stop);
  control.addEventListener('change', stop);
  document.querySelector('#reset-options').addEventListener('click', () => { control.value = 'flow'; stop(); });

  // Filters and sample controls replace card nodes, including while assets are loading.
  const changes = new MutationObserver(() => {
    if (active && (!active.isConnected || active.closest('[hidden]') || document.querySelector('dialog[open]'))) stop();
  });
  changes.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['hidden', 'open'] });
  window.addEventListener('pagehide', event => {
    stop();
    if (event.persisted) return;
    changes.disconnect();
    filter?.destroy();
    app?.destroy({ removeView: true, releaseGlobalResources: true }, { children: true });
    for (const url of loaded) void PIXI.Assets.unload(url);
  });
})();
