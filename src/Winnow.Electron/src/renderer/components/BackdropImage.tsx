import { useLayoutEffect, useRef } from 'react'

export function BackdropImage({ source, className }: { source: string; className?: string }) {
  const image = useRef<HTMLImageElement>(null)
  useLayoutEffect(() => {
    const node = image.current!
    // Restore on effect reattachment as well as clearing before the controller releases pixels.
    node.setAttribute('src', source)
    return () => node.removeAttribute('src')
  }, [source])
  return <img ref={image} src={source} className={className} alt="" />
}
