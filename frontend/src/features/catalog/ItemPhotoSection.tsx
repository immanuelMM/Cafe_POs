import { useRef, useState } from 'react'
import { errorMessage } from '../../lib/errors'
import type { Item } from '../../api/types'
import { assetUrl } from '../branding/brandingApi'
import { LogoEditor } from '../branding/LogoEditor'
import { catalogCalls, useCatalogMutation } from './useCatalog'

const PHOTO_TYPES = ['image/png', 'image/jpeg', 'image/webp']
const MB = 1024 * 1024

/** Small square thumbnail for item lists; falls back to a neutral placeholder. */
export function ItemThumb({ item }: { item: Pick<Item, 'name' | 'imageUrl'> }) {
  const src = assetUrl(item.imageUrl)
  return src ? (
    <img className="item-thumb" src={src} alt="" loading="lazy" />
  ) : (
    <span className="item-thumb item-thumb-empty" aria-hidden="true">
      ☕
    </span>
  )
}

/** Photo preview + upload (with square crop) / replace / remove for a saved item. */
export function ItemPhotoSection({ item }: { item: Item }) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const upload = useCatalogMutation(catalogCalls.uploadItemImage)
  const remove = useCatalogMutation(catalogCalls.deleteItemImage)
  const src = assetUrl(item.imageUrl)

  const pick = (f: File | undefined) => {
    setFileError(null)
    if (inputRef.current) inputRef.current.value = ''
    if (!f) return
    if (!PHOTO_TYPES.includes(f.type)) return setFileError('Please choose a PNG, JPEG or WebP image.')
    if (f.size > 20 * MB) return setFileError('That image is too large. Choose one under 20 MB.')
    setFile(f)
  }

  const err = fileError ?? (remove.error ? errorMessage(remove.error) : null)
  return (
    <fieldset className="opt-group">
      <legend>Photo</legend>
      <div className="photo-section">
        {src ? (
          <img className="photo-preview" src={src} alt={`Photo of ${item.name}`} />
        ) : (
          <div className="photo-preview photo-empty">No photo</div>
        )}
        <div className="photo-actions">
          <input
            ref={inputRef}
            type="file"
            accept={PHOTO_TYPES.join(',')}
            hidden
            onChange={(e) => pick(e.target.files?.[0])}
          />
          <button type="button" className="btn" onClick={() => inputRef.current?.click()}>
            {src ? 'Replace photo' : 'Upload photo'}
          </button>
          {src && (
            <button
              type="button"
              className="btn btn-danger-ghost"
              disabled={remove.isPending}
              onClick={() => window.confirm(`Remove the photo of "${item.name}"?`) && remove.mutate(item.id)}
            >
              {remove.isPending ? 'Removing…' : 'Remove photo'}
            </button>
          )}
          <p className="field-hint">PNG, JPEG or WebP. You can crop and zoom before uploading.</p>
        </div>
      </div>
      {err && (
        <div className="alert alert-error" role="alert">
          {err}
        </div>
      )}
      {file && (
        <LogoEditor
          file={file}
          title="Adjust photo"
          noun="photo"
          outSize={800}
          mime="image/jpeg"
          quality={0.85}
          maxBytes={2 * MB}
          allowTransparent={false}
          busy={upload.isPending}
          error={upload.error}
          onUse={(blob) => upload.mutate({ id: item.id, file: blob }, { onSuccess: () => setFile(null) })}
          onClose={() => setFile(null)}
        />
      )}
    </fieldset>
  )
}
