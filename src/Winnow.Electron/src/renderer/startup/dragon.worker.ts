import { DragonRenderer, type DragonAppearance, type DragonGeometry } from './dragon-renderer'

type DragonMessage =
  | ({ kind: 'start'; canvas: OffscreenCanvas } & DragonAppearance & DragonGeometry)
  | ({ kind: 'appearance' } & DragonAppearance)
let renderer: DragonRenderer | undefined
self.onmessage = ({ data }: MessageEvent<DragonMessage>) => {
  if (data.kind === 'appearance') renderer?.update(data)
  else {
    renderer?.stop()
    renderer = new DragonRenderer(data.canvas, data, data, (progress) => self.postMessage(progress))
    renderer.start()
  }
}
