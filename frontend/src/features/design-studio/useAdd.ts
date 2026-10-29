import { useCallback, useState } from 'react'
import { errorMessage } from '../../lib/errors'
import type { Item } from '../../api/types'
import { inkFor, makeImage, makeMenu, makeShape, makeText, type Palette, type TextPreset } from './factory'
import type { StudioEnv } from './env'
import { fileToEmbedded, loadImage, resolveSrc } from './images'
import { useStudio } from './store'
import type { IconKind, ImageSrc, ShapeKind } from './types'
import { assetUrl } from '../branding/useBranding'

export const DRAG_MIME = 'application/x-studio-add'

/** What the Add panel can create (also the drag-and-drop payload, serialized as JSON). */
export type AddPayload =
  { t: 'text'; preset: TextPreset } | { t: 'shape'; shape: ShapeKind; icon?: IconKind } | { t: 'menu' } | { t: 'logo' }

/** Creates elements (centered, or at a drop point) and puts them on the canvas. */
export function useAddActions({ palette, logoUrl }: StudioEnv) {
  const [error, setError] = useState<string | null>(null)

  const colors = useCallback((): Palette => {
    const doc = useStudio.getState().doc
    return { brand: palette.primary, ink: inkFor(doc.background) }
  }, [palette.primary])

  const addImage = useCallback(
    async (src: ImageSrc, name: string, at?: { x: number; y: number }) => {
      const url = resolveSrc(src, logoUrl)
      if (!url) throw new Error('That image is not available.')
      const img = await loadImage(url)
      const st = useStudio.getState()
      st.add(makeImage(src, { w: img.naturalWidth, h: img.naturalHeight }, st.doc, name, at))
    },
    [logoUrl],
  )

  const addPayload = useCallback(
    (p: AddPayload, at?: { x: number; y: number }) => {
      setError(null)
      const st = useStudio.getState()
      const pal = colors()
      if (p.t === 'text') st.add(makeText(p.preset, st.doc, pal, at))
      else if (p.t === 'shape') st.add(makeShape(p.icon ? 'icon' : p.shape, st.doc, pal, at, p.icon))
      else if (p.t === 'menu') st.add(makeMenu(st.doc, pal, palette.primary))
      else if (p.t === 'logo') {
        if (!logoUrl) return setError('No logo uploaded yet.')
        addImage({ type: 'api', path: '/api/v1/branding/logo' }, 'Logo', at).catch((e) => setError(errorMessage(e)))
      }
    },
    [colors, palette.primary, logoUrl, addImage],
  )

  const addFiles = useCallback(async (files: File[], at?: { x: number; y: number }) => {
    setError(null)
    const imgs = files.filter((f) => f.type.startsWith('image/'))
    for (const f of imgs) {
      try {
        const e = await fileToEmbedded(f)
        const st = useStudio.getState()
        st.add(makeImage(e.src, { w: e.width, h: e.height }, st.doc, f.name.replace(/\.[^.]+$/, '') || 'Image', at))
      } catch (err) {
        setError(errorMessage(err))
      }
    }
  }, [])

  const addItemPhoto = useCallback(
    (item: Item) => {
      if (!item.imageUrl || !assetUrl(item.imageUrl)) return
      setError(null)
      addImage({ type: 'api', path: item.imageUrl }, item.name).catch((e) => setError(errorMessage(e)))
    },
    [addImage],
  )

  return { addPayload, addFiles, addItemPhoto, error, clearError: () => setError(null) }
}
