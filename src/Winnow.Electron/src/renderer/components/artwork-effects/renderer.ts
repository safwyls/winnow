// Despite its name, this replaces Pixi's generated uniform sync functions with
// ordinary functions. Electron's CSP continues to forbid unsafe-eval.
import 'pixi.js/unsafe-eval'
import { Application, Filter, Rectangle, Sprite, Texture, UniformGroup } from 'pixi.js'
import type { ArtworkEffectOptions } from '../../../shared/artworkEffects'
import { vertex, fragment } from './shaders'

export interface ArtworkRenderer {
  readonly attached: boolean
  attach(
    surface: HTMLElement,
    image: HTMLImageElement,
    width: number,
    height: number,
    options: ArtworkEffectOptions,
  ): void
  draw(x: number, y: number, reveal: number, status: string): void
  detach(): void
  destroy(): void
}

export async function createArtworkRenderer(onLost: () => void): Promise<ArtworkRenderer> {
  const app = new Application()
  try {
    await app.init({
      width: 1,
      height: 1,
      backgroundAlpha: 0,
      preference: 'webgl',
      autoStart: false,
      sharedTicker: false,
      antialias: false,
      resolution: Math.min(window.devicePixelRatio || 1, 2),
      autoDensity: true,
      eventFeatures: { move: false, globalMove: false, click: false, wheel: false },
    })
    return configureRenderer(app, onLost)
  } catch (error) {
    // Init can fail before the application has a renderer to destroy.
    if (app.renderer) app.destroy({ removeView: true }, { children: true })
    throw error
  }
}

function configureRenderer(app: Application, onLost: () => void): ArtworkRenderer {
  app.stage.eventMode = 'none'
  app.canvas.className = 'winnow-artwork-canvas'
  app.canvas.setAttribute('aria-hidden', 'true')
  app.canvas.addEventListener('webglcontextlost', onLost)
  const uniforms = new UniformGroup({
    uArtworkSize: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
    uSize: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
    uCursor: { value: new Float32Array([0, 0]), type: 'vec2<f32>' },
    uReveal: { value: 0, type: 'f32' },
    uIntensity: { value: 0.55, type: 'f32' },
    uFinish: { value: 1, type: 'f32' },
    uFoilStrength: { value: 0.65, type: 'f32' },
    uFoilThreshold: { value: 0.72, type: 'f32' },
    uFoilMetal: { value: 0, type: 'f32' },
  })
  const filter = Filter.from({
    gl: { vertex, fragment },
    resolution: 'inherit',
    padding: 0,
    resources: { uArtwork: Texture.WHITE.source, finishUniforms: uniforms },
  })
  const initialInputGroup = filter.groups[0]
  const plane = new Sprite(Texture.WHITE)
  plane.filters = [filter]
  app.stage.addChild(plane)
  let texture: Texture | undefined
  let destroyed = false
  const detach = () => {
    app.canvas.remove()
    filter.resources.uArtwork = Texture.WHITE.source
    // Do not retain every visited game's pixels on the GPU or in Pixi's asset cache.
    texture?.destroy(true)
    texture = undefined
  }
  return {
    get attached() {
      return !destroyed && app.canvas.isConnected
    },
    attach(surface, image, width, height, options) {
      detach()
      // This is the already-decoded DOM image, never a URL load or a second request.
      texture = Texture.from(image, true)
      filter.resources.uArtwork = texture.source
      uniforms.uniforms.uArtworkSize.set([image.naturalWidth, image.naturalHeight])
      uniforms.uniforms.uSize.set([width, height])
      uniforms.uniforms.uIntensity = options.finish === 'off' ? 0 : options.intensity / 100
      uniforms.uniforms.uFinish = { off: 0, matte: 0, satin: 1, foil: 2 }[options.finish]
      uniforms.uniforms.uFoilStrength = options.highlightFoil ? options.foilStrength / 100 : 0
      uniforms.uniforms.uFoilThreshold = options.foilThreshold / 100
      uniforms.uniforms.uFoilMetal = { silver: 0, gold: 1, holographic: 2 }[options.foilMetal]
      plane.width = width
      plane.height = height
      plane.filterArea = new Rectangle(0, 0, width, height)
      // Limit the offscreen target even if a developer wraps a large hero image.
      const resolution = Math.min(window.devicePixelRatio || 1, 2, 2048 / Math.max(width, height))
      app.renderer.resize(width, height, resolution)
      app.canvas.dataset.finish = options.finish
      app.canvas.dataset.highlight = options.highlightFoil ? options.foilMetal : 'off'
      // Start transparent so a reparented canvas cannot flash the previous card.
      uniforms.uniforms.uReveal = 0
      app.render()
      surface.append(app.canvas)
    },
    draw(x, y, reveal, status) {
      uniforms.uniforms.uCursor.set([x, y])
      uniforms.uniforms.uReveal = reveal
      app.canvas.dataset.status = status
      app.render()
    },
    detach() {
      if (!destroyed) detach()
    },
    destroy() {
      if (destroyed) return
      detach()
      destroyed = true
      app.canvas.removeEventListener('webglcontextlost', onLost)
      plane.filters = []
      // FilterSystem replaces its input group when drawing; Filter.destroy owns only
      // the original groups. Release this renderer's replacement before its textures.
      const inputGroup = filter.groups[0]
      filter.destroy()
      if (inputGroup && inputGroup !== initialInputGroup) inputGroup.destroy()
      // Do not drain global pools belonging to a developer theme's own Pixi app.
      app.destroy({ removeView: true }, { children: true })
    },
  }
}
