// Uses ordinary uniform sync functions while Electron continues to forbid unsafe-eval.
import 'pixi.js/unsafe-eval'
import { Application, Filter, Rectangle, Sprite, Texture, UniformGroup } from 'pixi.js'
import type { PortalGeometry } from './geometry'
import { fragment, vertex } from './shaders'

export interface PortalRenderer {
  attach(element: HTMLElement): void
  resize(width: number, height: number): void
  draw(geometry: PortalGeometry, colorA: Float32Array, colorB: Float32Array): void
  destroy(): void
}

export async function createPortalRenderer(onLost: () => void): Promise<PortalRenderer> {
  const app = new Application()
  try {
    await app.init({
      width: 1,
      height: 1,
      backgroundAlpha: 0,
      preference: 'webgl',
      resolution: Math.min(window.devicePixelRatio || 1, 1.5),
      autoDensity: true,
      autoStart: false,
      sharedTicker: false,
      antialias: false,
      eventFeatures: { move: false, globalMove: false, click: false, wheel: false },
    })
    return configure(app, onLost)
  } catch (error) {
    if (app.renderer) app.destroy({ removeView: true }, { children: true })
    throw error
  }
}

function configure(app: Application, onLost: () => void): PortalRenderer {
  app.stage.eventMode = 'none'
  app.canvas.className = 'winnow-portal-canvas'
  app.canvas.setAttribute('aria-hidden', 'true')
  app.canvas.addEventListener('webglcontextlost', onLost)
  const uniforms = new UniformGroup({
    uCenter: { value: new Float32Array([0, 0]), type: 'vec2<f32>' },
    uRadius: { value: new Float32Array([1, 1]), type: 'vec2<f32>' },
    uShape: { value: new Float32Array([3.8, 3.6]), type: 'vec2<f32>' },
    uTime: { value: 0, type: 'f32' },
    uColorA: { value: new Float32Array([0.46, 0.81, 0.74]), type: 'vec3<f32>' },
    uColorB: { value: new Float32Array([0.7, 0.64, 0.95]), type: 'vec3<f32>' },
  })
  const filter = Filter.from({
    gl: { vertex, fragment },
    resolution: 'inherit',
    padding: 0,
    resources: { portalUniforms: uniforms },
  })
  const initialInputGroup = filter.groups[0]
  const plane = new Sprite(Texture.WHITE)
  plane.filters = [filter]
  app.stage.addChild(plane)
  let destroyed = false
  return {
    attach(element) {
      element.append(app.canvas)
    },
    resize(width, height) {
      plane.width = width
      plane.height = height
      plane.filterArea = new Rectangle(0, 0, width, height)
      app.renderer.resize(
        width,
        height,
        Math.min(window.devicePixelRatio || 1, 1.5, 2560 / Math.max(width, height)),
      )
    },
    draw({ center, radius, exponent, wave, time }, colorA, colorB) {
      uniforms.uniforms.uCenter.set([center.x, center.y])
      uniforms.uniforms.uRadius.set([radius.x, radius.y])
      uniforms.uniforms.uShape.set([exponent, wave])
      uniforms.uniforms.uTime = time
      uniforms.uniforms.uColorA.set(colorA)
      uniforms.uniforms.uColorB.set(colorB)
      app.render()
    },
    destroy() {
      if (destroyed) return
      destroyed = true
      app.canvas.removeEventListener('webglcontextlost', onLost)
      plane.filters = []
      // Pixi replaces this group with its per-renderer filter input group during a
      // draw. Filter.destroy only releases the groups it originally owned; release
      // the replacement before this renderer's temporary textures are disposed.
      const inputGroup = filter.groups[0]
      filter.destroy()
      if (inputGroup && inputGroup !== initialInputGroup) inputGroup.destroy()
      // Other theme components can own Pixi renderers and shared pools concurrently.
      app.destroy({ removeView: true }, { children: true })
    },
  }
}
