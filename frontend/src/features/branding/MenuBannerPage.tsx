import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Link } from 'react-router'
import { ErrorBox, Field, PageHeader, Spinner } from '../../components/ui'
import { HEX_RE, onColor } from '../../lib/color'
import { formatMoney } from '../../lib/format'
import type { Item } from '../../lib/types'
import { isAdmin, useAuthStore } from '../auth/authStore'
import { useCategories, useItems } from '../catalog/catalogApi'
import { BrandLogo } from './BrandLogo'
import { DEFAULT_BRAND_NAME, assetUrl, useBranding } from './brandingApi'
import { buildSections } from './menuSections'

interface SizePreset {
  id: string
  label: string
  w: number
  h: number
  unit: 'in' | 'mm'
}
const SIZES: SizePreset[] = [
  { id: '2x3', label: '2 x 3 ft portrait', w: 24, h: 36, unit: 'in' },
  { id: '3x4', label: '3 x 4 ft portrait', w: 36, h: 48, unit: 'in' },
  { id: '4x3', label: '4 x 3 ft landscape', w: 48, h: 36, unit: 'in' },
  { id: 'a3', label: 'A3 portrait', w: 297, h: 420, unit: 'mm' },
]

/** Resolves once every image inside the element has loaded (or failed), so printing never captures blanks. */
async function waitForImages(root: HTMLElement | null) {
  if (!root) return
  const imgs = Array.from(root.querySelectorAll('img'))
  await Promise.all(
    imgs.map((img) => {
      img.loading = 'eager'
      if (img.complete && img.naturalWidth > 0) return Promise.resolve()
      return img.decode().catch(() => undefined)
    }),
  )
}

function BannerItem({ item, photos }: { item: Item; photos: boolean }) {
  const price = (v: Item['variations'][number]) => formatMoney(v.effectivePrice ?? v.price)
  const photo = photos ? assetUrl(item.imageUrl) : null
  if (photo) {
    return (
      <li className="banner-item banner-item-photo">
        <img className="banner-photo" src={photo} alt="" />
        <div className="banner-item-text">
          <div className="banner-line">
            <span>{item.name}</span>
            {item.variations.length === 1 && <span className="banner-price">{price(item.variations[0])}</span>}
          </div>
          {item.description && <small className="banner-desc">{item.description}</small>}
          {item.variations.length > 1 && (
            <ul className="banner-vars">
              {item.variations.map((v) => (
                <li key={v.id} className="banner-line banner-var">
                  <span>{v.name}</span>
                  <span className="banner-price">{price(v)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </li>
    )
  }
  if (item.variations.length === 1) {
    return (
      <li className="banner-item">
        <div className="banner-line">
          <span>{item.name}</span>
          <span className="banner-price">{price(item.variations[0])}</span>
        </div>
        {item.description && <small className="banner-desc">{item.description}</small>}
      </li>
    )
  }
  return (
    <li className="banner-item">
      <div className="banner-line">
        <span>{item.name}</span>
      </div>
      {item.description && <small className="banner-desc">{item.description}</small>}
      <ul className="banner-vars">
        {item.variations.map((v) => (
          <li key={v.id} className="banner-line banner-var">
            <span>{v.name}</span>
            <span className="banner-price">{price(v)}</span>
          </li>
        ))}
      </ul>
    </li>
  )
}

/** Printing: hide the app chrome (without touching the receipt print rules) while this page is mounted. */
function usePrintBannerMode() {
  useEffect(() => {
    document.body.classList.add('print-banner')
    return () => document.body.classList.remove('print-banner')
  }, [])
}

export function MenuBannerPage() {
  usePrintBannerMode()
  const admin = useAuthStore((s) => isAdmin(s.user))
  const branding = useBranding()
  const [sizeId, setSizeId] = useState('2x3')
  const [columns, setColumns] = useState(2)
  const [showInactive, setShowInactive] = useState(false)
  const [view, setView] = useState<'generated' | 'uploaded'>('generated')
  const [overflow, setOverflow] = useState(false)
  const [photosChoice, setPhotosChoice] = useState<boolean | null>(null)
  const [printing, setPrinting] = useState(false)
  const bodyRef = useRef<HTMLDivElement>(null)
  const bannerRef = useRef<HTMLDivElement>(null)
  const cats = useCategories(showInactive)
  const items = useItems({ includeInactive: showInactive, pageSize: 100 })

  const anyPhoto = !!items.data?.data.some((i) => i.imageUrl)
  const showPhotos = photosChoice ?? anyPhoto

  const print = async () => {
    setPrinting(true)
    try {
      await waitForImages(bannerRef.current)
    } finally {
      setPrinting(false)
    }
    window.print()
  }

  const size = SIZES.find((s) => s.id === sizeId) ?? SIZES[0]
  const sections = useMemo(
    () => buildSections(cats.data ?? [], items.data?.data ?? [], showInactive),
    [cats.data, items.data, showInactive],
  )
  const b = branding.data
  const brandName = b?.brandName ?? DEFAULT_BRAND_NAME
  const uploadedUrl = assetUrl(b?.tarpaulinUrl)
  const isPdf = b?.tarpaulinContentType === 'application/pdf'
  // Secondary colour accents the category rules; a tertiary colour turns prices into small tags.
  const secondary = b?.secondaryColor && HEX_RE.test(b.secondaryColor) ? b.secondaryColor : null
  const tertiary = secondary && b?.tertiaryColor && HEX_RE.test(b.tertiaryColor) ? b.tertiaryColor : null
  const accentVars = {
    ...(secondary && { '--banner-rule': secondary }),
    ...(tertiary && { '--tag-bg': tertiary, '--tag-ink': onColor(tertiary), '--tag-pad': '0.2cqw 1cqw' }),
  } as CSSProperties
  const effectiveView = view === 'uploaded' && uploadedUrl ? 'uploaded' : 'generated'

  // Extra columns spill sideways when the content does not fit the banner height.
  useEffect(() => {
    const el = bodyRef.current
    setOverflow(!!el && el.scrollWidth > el.clientWidth + 2)
  }, [sections, columns, sizeId, effectiveView, showPhotos])

  const loading = cats.isLoading || items.isLoading || branding.isLoading
  const error = cats.error ?? items.error
  const truncated = !!items.data && items.data.totalCount > items.data.data.length

  return (
    <section className="page">
      <style>{`@media print { @page { size: ${size.w}${size.unit} ${size.h}${size.unit}; margin: 0; } .banner { width: ${size.w}${size.unit} !important; height: ${size.h}${size.unit} !important; aspect-ratio: auto !important; } }`}</style>
      <div className="no-print">
        <PageHeader title="Menu banner">
          {admin && (
            <Link className="btn" to="/admin/design-studio?kind=banner">
              Design your own banner
            </Link>
          )}
          {effectiveView === 'generated' && (
            <button type="button" className="btn btn-primary" onClick={() => void print()} disabled={loading || printing}>
              {printing ? 'Preparing…' : 'Print banner'}
            </button>
          )}
          {effectiveView === 'uploaded' && uploadedUrl && (
            <a className="btn btn-primary" href={uploadedUrl} target="_blank" rel="noreferrer" download>
              Download uploaded design
            </a>
          )}
        </PageHeader>
        {uploadedUrl && (
          <div className="tabs" role="group" aria-label="Banner source">
            <button type="button" className={effectiveView === 'generated' ? 'tab tab-on' : 'tab'} aria-pressed={effectiveView === 'generated'} onClick={() => setView('generated')}>
              Generated from menu
            </button>
            <button type="button" className={effectiveView === 'uploaded' ? 'tab tab-on' : 'tab'} aria-pressed={effectiveView === 'uploaded'} onClick={() => setView('uploaded')}>
              Uploaded design
            </button>
          </div>
        )}
        {effectiveView === 'generated' && (
          <div className="banner-options card">
            <Field label="Size">
              <select value={sizeId} onChange={(e) => setSizeId(e.target.value)}>
                {SIZES.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.label}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Columns">
              <select value={columns} onChange={(e) => setColumns(Number(e.target.value))}>
                {[1, 2, 3, 4].map((n) => (
                  <option key={n} value={n}>
                    {n}
                  </option>
                ))}
              </select>
            </Field>
            <label className="check">
              <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} /> Show inactive items
            </label>
            <label className="check">
              <input type="checkbox" checked={showPhotos} onChange={(e) => setPhotosChoice(e.target.checked)} /> Show photos
            </label>
          </div>
        )}
        {loading && <Spinner label="Loading menu…" />}
        {error && <ErrorBox error={error} onRetry={() => { void cats.refetch(); void items.refetch() }} />}
        {effectiveView === 'generated' && truncated && (
          <div className="alert alert-error" role="alert">Only the first {items.data?.data.length} items are shown; the menu has {items.data?.totalCount}.</div>
        )}
        {effectiveView === 'generated' && overflow && (
          <div className="alert alert-error" role="alert">The menu does not fit this size. Add columns or choose a bigger size.</div>
        )}
        {effectiveView === 'generated' && !loading && sections.length === 0 && <p className="muted">No active menu items to show yet.</p>}
      </div>

      {effectiveView === 'uploaded' && uploadedUrl ? (
        <div className="banner-upload no-print">
          {isPdf ? (
            <object data={uploadedUrl} type="application/pdf" className="banner-pdf" aria-label="Uploaded banner design">
              <a href={uploadedUrl} target="_blank" rel="noreferrer">Open the uploaded PDF</a>
            </object>
          ) : (
            <img src={uploadedUrl} alt="Uploaded banner design" />
          )}
        </div>
      ) : (
        !loading && (
          <div className="banner-stage">
            <div className="banner" ref={bannerRef} style={{ aspectRatio: `${size.w} / ${size.h}`, ...accentVars }}>
              <header className="banner-head">
                <BrandLogo className="banner-logo" />
                <div>
                  <div className="banner-title">{brandName}</div>
                  {b?.tagline && <div className="banner-tagline">{b.tagline}</div>}
                </div>
              </header>
              <div className="banner-body" ref={bodyRef} style={{ columnCount: columns }}>
                {sections.map((s) => (
                  <section key={s.category.id} className="banner-cat">
                    <h2 className="banner-cat-title">{s.category.name}</h2>
                    <ul className="banner-items">
                      {s.items.map((i) => (
                        <BannerItem key={i.id} item={i} photos={showPhotos} />
                      ))}
                    </ul>
                  </section>
                ))}
              </div>
            </div>
          </div>
        )
      )}
    </section>
  )
}
