import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router'
import { z } from 'zod'
import { Field } from '../../components/ui'
import { errorMessage } from '../../lib/api'
import { HEX_RE, brandBackground, onBrand, type BrandPalette } from '../../lib/color'
import { LogoEditor } from './LogoEditor'
import { LogoMaker } from './LogoMaker'
import {
  assetUrl,
  useDeleteLogo,
  useDeleteTarpaulin,
  useUpdateBranding,
  useUploadLogo,
  useUploadTarpaulin,
  toPalette,
  type BrandingDto,
} from './brandingApi'

const LOGO_TYPES = ['image/png', 'image/jpeg', 'image/webp']
const TARP_TYPES = [...LOGO_TYPES, 'application/pdf']
const MB = 1024 * 1024
const PRESET_COLORS = ['#3b2417', '#7a1f1f', '#b4561f', '#1f5c3a', '#1d3f8a', '#4a2a6b', '#222222']
const PALETTES: { name: string; colors: string[] }[] = [
  { name: 'Coffee', colors: ['#3b2417', '#b4561f', '#f2c48d'] },
  { name: 'Matcha', colors: ['#2f5d3a', '#8fbf6a'] },
  { name: 'Berry', colors: ['#6b1f4a', '#e0567a', '#f7b267'] },
  { name: 'Ocean', colors: ['#12355b', '#2a9d8f'] },
]
const ANGLES = [
  { deg: 90, label: '→' },
  { deg: 135, label: '↘' },
  { deg: 180, label: '↓' },
  { deg: 45, label: '↗' },
]
const NEW_SECONDARY = '#b4561f'
const NEW_TERTIARY = '#f2c48d'

const colorMsg = 'Use a colour like #3b2417'
const optColor = z.string().regex(HEX_RE, colorMsg).nullable()
const schema = z
  .object({
    brandName: z.string().trim().min(1, 'Brand name is required').max(100, 'Max 100 characters'),
    tagline: z.string().trim().max(200, 'Max 200 characters'),
    primaryColor: z.string().regex(HEX_RE, colorMsg),
    secondaryColor: optColor,
    tertiaryColor: optColor,
    gradient: z.boolean(),
    gradientAngle: z.number().int().min(0).max(360),
  })
  // Same rules the server enforces (400 otherwise).
  .superRefine((v, ctx) => {
    if (v.tertiaryColor !== null && v.secondaryColor === null)
      ctx.addIssue({ code: 'custom', path: ['tertiaryColor'], message: 'Add a second colour first' })
    if (v.gradient && v.secondaryColor === null)
      ctx.addIssue({ code: 'custom', path: ['gradient'], message: 'Add a second color to use a gradient' })
  })
type Values = z.infer<typeof schema>

const valuesOf = (b: BrandingDto): Values => ({
  brandName: b.brandName,
  tagline: b.tagline ?? '',
  primaryColor: b.primaryColor,
  secondaryColor: b.secondaryColor,
  tertiaryColor: b.tertiaryColor,
  gradient: b.gradient,
  gradientAngle: b.gradientAngle,
})

/** Header + primary button preview with the unsaved colours, rendered with the same helpers the app uses. */
function BrandPreview({ name, palette, logo }: { name: string; palette: BrandPalette; logo: string | null }) {
  const valid = HEX_RE.test(palette.primary)
  const ink = valid ? onBrand(palette) : '#ffffff'
  const bg = valid ? brandBackground(palette) : undefined
  return (
    <div role="img" aria-label="Header and button preview" className="form">
      <div className="brand-preview" style={{ background: bg, color: ink }}>
        <span className="brand">
          {logo ? <img className="brand-logo" src={logo} alt="" /> : <span aria-hidden="true">☕</span>} <span>{name || 'Brand name'}</span>
        </span>
        <span className="brand-preview-pill" style={{ background: ink, color: valid ? palette.primary : undefined }}>
          POS
        </span>
        <span className="brand-preview-pill" style={{ border: `2px solid ${ink}` }}>
          Orders
        </span>
      </div>
      <span className="btn btn-primary" style={{ background: bg, color: ink, alignSelf: 'flex-start' }}>
        Primary button
      </span>
    </div>
  )
}

type ColorKey = 'primaryColor' | 'secondaryColor' | 'tertiaryColor'

function ColorSlot({
  label,
  value,
  error,
  onChange,
  onRemove,
}: {
  label: string
  value: string
  error?: string
  onChange: (v: string) => void
  onRemove?: () => void
}) {
  return (
    <div className="field">
      <div className="color-slot" role="group" aria-label={`${label} colour`}>
        <span className="color-slot-name">{label}</span>
        <input type="color" className="color-input" aria-label={`Pick ${label.toLowerCase()} colour`} value={HEX_RE.test(value) ? value : '#000000'} onChange={(e) => onChange(e.target.value)} />
        <input className="hex-input" aria-label={`${label} hex colour`} maxLength={7} spellCheck={false} value={value} onChange={(e) => onChange(e.target.value)} />
        {onRemove && (
          <button type="button" className="btn btn-ghost btn-icon" aria-label={`Remove ${label.toLowerCase()} colour`} onClick={onRemove}>
            ×
          </button>
        )}
      </div>
      {error && <span className="field-error" role="alert">{error}</span>}
    </div>
  )
}

function BrandForm({ brand }: { brand: BrandingDto }) {
  const [saved, setSaved] = useState(false)
  const save = useUpdateBranding()
  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { errors, isDirty },
  } = useForm<Values>({ resolver: zodResolver(schema), defaultValues: valuesOf(brand) })
  const [name, primary, secondary, tertiary, gradient, angle] = watch(['brandName', 'primaryColor', 'secondaryColor', 'tertiaryColor', 'gradient', 'gradientAngle'])
  const opts = { shouldDirty: true, shouldValidate: true }
  const setColor = (k: ColorKey, v: string) => setValue(k, v, opts)
  const count = 1 + (secondary !== null ? 1 : 0) + (tertiary !== null ? 1 : 0)
  // Removing the second colour shifts the third into its place, so the "tertiary needs secondary" rule always holds.
  const removeSecond = () => {
    setValue('secondaryColor', tertiary, opts)
    setValue('tertiaryColor', null, opts)
    if (tertiary === null) setValue('gradient', false, opts)
  }
  const removeThird = () => setValue('tertiaryColor', null, opts)
  const addColor = () => (secondary === null ? setValue('secondaryColor', NEW_SECONDARY, opts) : setValue('tertiaryColor', NEW_TERTIARY, opts))
  const applyPalette = (cs: string[]) => {
    setValue('primaryColor', cs[0], opts)
    setValue('secondaryColor', cs[1] ?? null, opts)
    setValue('tertiaryColor', cs[2] ?? null, opts)
  }
  const palette: BrandPalette = {
    primary,
    secondary: secondary && HEX_RE.test(secondary) ? secondary : null,
    tertiary: tertiary && HEX_RE.test(tertiary) ? tertiary : null,
    gradient,
    angle,
  }
  const hasSecond = secondary !== null
  return (
    <form
      className="form card"
      noValidate
      onSubmit={handleSubmit((v) => {
        setSaved(false)
        const lower = (c: string | null) => (c ? c.toLowerCase() : null)
        save.mutate(
          {
            brandName: v.brandName,
            tagline: v.tagline || null,
            primaryColor: v.primaryColor.toLowerCase(),
            secondaryColor: lower(v.secondaryColor),
            tertiaryColor: v.secondaryColor ? lower(v.tertiaryColor) : null,
            gradient: v.gradient && v.secondaryColor !== null,
            gradientAngle: v.gradientAngle,
          },
          {
            onSuccess: (r) => {
              setSaved(true)
              reset(valuesOf(r))
            },
          },
        )
      })}
    >
      {save.error && <div className="alert alert-error" role="alert">{errorMessage(save.error)}</div>}
      {saved && <div className="alert alert-ok" role="status">Saved.</div>}
      <Field label="Brand name" error={errors.brandName?.message} hint="Shown in the header and on the login screen at every location.">
        <input {...register('brandName')} />
      </Field>
      <Field label="Tagline" error={errors.tagline?.message} hint="Optional. Printed on the menu banner.">
        <input {...register('tagline')} />
      </Field>

      <fieldset className="opt-group colors">
        <legend>Brand colours</legend>
        <ColorSlot label="Primary" value={primary} error={errors.primaryColor?.message} onChange={(v) => setColor('primaryColor', v)} />
        {secondary !== null && (
          <ColorSlot label="Secondary" value={secondary} error={errors.secondaryColor?.message} onChange={(v) => setColor('secondaryColor', v)} onRemove={removeSecond} />
        )}
        {tertiary !== null && (
          <ColorSlot label="Tertiary" value={tertiary} error={errors.tertiaryColor?.message} onChange={(v) => setColor('tertiaryColor', v)} onRemove={removeThird} />
        )}
        {count < 3 && (
          <button type="button" className="btn btn-sm" style={{ alignSelf: 'flex-start' }} onClick={addColor}>
            + Add color
          </button>
        )}
        <div className="chips swatch-row" role="group" aria-label="Primary colour quick picks">
          {PRESET_COLORS.map((c) => (
            <button
              key={c}
              type="button"
              className={`swatch${primary?.toLowerCase() === c ? ' swatch-on' : ''}`}
              style={{ background: c }}
              aria-label={`Use ${c} as primary`}
              aria-pressed={primary?.toLowerCase() === c}
              onClick={() => setColor('primaryColor', c)}
            />
          ))}
        </div>
        <div className="palettes" role="group" aria-label="Preset palettes">
          {PALETTES.map((p) => (
            <button key={p.name} type="button" className="palette" onClick={() => applyPalette(p.colors)}>
              <span className="palette-chips" aria-hidden="true">
                {p.colors.map((c) => (
                  <i key={c} style={{ background: c }} />
                ))}
              </span>
              {p.name}
            </button>
          ))}
        </div>
      </fieldset>

      <fieldset className="opt-group">
        <legend>Gradient</legend>
        <label className="check">
          <input type="checkbox" role="switch" checked={gradient && hasSecond} disabled={!hasSecond} onChange={(e) => setValue('gradient', e.target.checked, opts)} />
          Use a gradient across your colours
        </label>
        {!hasSecond && <small className="muted">Add a second color to use a gradient</small>}
        {gradient && hasSecond && (
          <div className="angle-row" role="group" aria-label="Gradient angle">
            <input
              type="range"
              min={0}
              max={360}
              step={1}
              aria-label="Gradient angle in degrees"
              value={angle}
              onChange={(e) => setValue('gradientAngle', Number(e.target.value), opts)}
            />
            <output>{angle}°</output>
            {ANGLES.map((a) => (
              <button
                key={a.deg}
                type="button"
                className="btn btn-sm angle-btn"
                aria-pressed={angle === a.deg}
                aria-label={`${a.deg} degrees`}
                onClick={() => setValue('gradientAngle', a.deg, opts)}
              >
                {a.label} {a.deg}°
              </button>
            ))}
          </div>
        )}
      </fieldset>

      <BrandPreview name={name} palette={palette} logo={assetUrl(brand.logoUrl)} />
      <button className="btn btn-primary" disabled={!isDirty || save.isPending}>
        {save.isPending ? 'Saving…' : 'Save brand'}
      </button>
    </form>
  )
}

function LogoSection({ brand }: { brand: BrandingDto }) {
  const [tab, setTab] = useState<'upload' | 'create'>('upload')
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const upload = useUploadLogo()
  const remove = useDeleteLogo()
  const logo = assetUrl(brand.logoUrl)
  const palette = useMemo<BrandPalette>(() => toPalette(brand) ?? { primary: '#3b2417' }, [brand])

  const pick = (f: File | undefined) => {
    setFileError(null)
    if (input.current) input.current.value = ''
    if (!f) return
    if (!LOGO_TYPES.includes(f.type)) return setFileError('Please choose a PNG, JPEG or WebP image.')
    if (f.size > MB) return setFileError('That image is larger than 1 MB. Please choose a smaller one.')
    upload.reset()
    setFile(f)
  }
  const send = (blob: Blob) => upload.mutate(blob, { onSuccess: () => setFile(null) })

  return (
    <div className="card form">
      <h2>Logo</h2>
      <div className="logo-current">
        {logo ? <img src={logo} alt="Current logo" className="logo-thumb" /> : <span className="logo-thumb logo-thumb-empty" aria-hidden="true">☕</span>}
        <p className="muted">{logo ? 'Shown in the header, login screen, favicon and menu banner.' : 'No logo yet. The coffee cup is used until you add one.'}</p>
      </div>
      <div className="tabs" role="tablist" aria-label="Logo source">
        <button type="button" role="tab" aria-selected={tab === 'upload'} className={tab === 'upload' ? 'tab tab-on' : 'tab'} onClick={() => setTab('upload')}>
          Upload image
        </button>
        <button type="button" role="tab" aria-selected={tab === 'create'} className={tab === 'create' ? 'tab tab-on' : 'tab'} onClick={() => setTab('create')}>
          Create logo
        </button>
      </div>
      {remove.error && <div className="alert alert-error" role="alert">{errorMessage(remove.error)}</div>}
      {tab === 'upload' ? (
        <>
          {fileError && <div className="alert alert-error" role="alert">{fileError}</div>}
          {!file && upload.error && <div className="alert alert-error" role="alert">{errorMessage(upload.error)}</div>}
          {upload.isSuccess && !file && <div className="alert alert-ok" role="status">Logo updated.</div>}
          <input ref={input} type="file" accept={LOGO_TYPES.join(',')} className="sr-only" id="logo-file" onChange={(e) => pick(e.target.files?.[0])} />
          <div className="row-actions">
            <label htmlFor="logo-file" className="btn btn-primary">
              {logo ? 'Replace logo' : 'Upload logo'}
            </label>
            {logo && (
              <button type="button" className="btn btn-danger-ghost" disabled={remove.isPending} onClick={() => window.confirm('Remove the logo?') && remove.mutate()}>
                Remove
              </button>
            )}
          </div>
          <small className="muted">PNG, JPEG or WebP, up to 1 MB. You can crop and zoom before it is saved.</small>
          <Link className="btn" to="/admin/design-studio?kind=logo" style={{ alignSelf: 'flex-start' }}>
            Design your own logo
          </Link>
        </>
      ) : (
        <>
          {upload.isSuccess && <div className="alert alert-ok" role="status">Logo updated.</div>}
          <LogoMaker palette={palette} busy={upload.isPending} error={upload.error} onUse={(b) => upload.mutate(b)} />
          <Link className="btn" to="/admin/design-studio?kind=logo" style={{ alignSelf: 'flex-start' }}>
            Design your own logo
          </Link>
          {logo && (
            <button type="button" className="btn btn-danger-ghost" disabled={remove.isPending} onClick={() => window.confirm('Remove the logo?') && remove.mutate()}>
              Remove current logo
            </button>
          )}
        </>
      )}
      {file && <LogoEditor file={file} busy={upload.isPending} error={upload.error} onUse={send} onClose={() => setFile(null)} />}
    </div>
  )
}

function TarpaulinSection({ brand }: { brand: BrandingDto }) {
  const [fileError, setFileError] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const upload = useUploadTarpaulin()
  const remove = useDeleteTarpaulin()
  const url = assetUrl(brand.tarpaulinUrl)
  const isPdf = brand.tarpaulinContentType === 'application/pdf'

  const pick = (f: File | undefined) => {
    setFileError(null)
    if (input.current) input.current.value = ''
    if (!f) return
    if (!TARP_TYPES.includes(f.type)) return setFileError('Please choose a PNG, JPEG, WebP or PDF file.')
    if (f.size > 15 * MB) return setFileError('That file is larger than 15 MB. Please choose a smaller one.')
    upload.reset()
    upload.mutate(f)
  }

  return (
    <div className="card form">
      <h2>Tarpaulin (menu banner)</h2>
      <p className="muted">Upload your own design, or print the banner generated from your menu.</p>
      {fileError && <div className="alert alert-error" role="alert">{fileError}</div>}
      {upload.error && <div className="alert alert-error" role="alert">{errorMessage(upload.error)}</div>}
      {remove.error && <div className="alert alert-error" role="alert">{errorMessage(remove.error)}</div>}
      {url &&
        (isPdf ? (
          <p>
            <a href={url} target="_blank" rel="noreferrer">Open uploaded PDF design</a>
          </p>
        ) : (
          <img src={url} alt="Uploaded tarpaulin design" className="tarp-preview" />
        ))}
      <input ref={input} type="file" accept={TARP_TYPES.join(',')} className="sr-only" id="tarp-file" onChange={(e) => pick(e.target.files?.[0])} />
      <div className="row-actions">
        <label htmlFor="tarp-file" className="btn btn-primary" aria-disabled={upload.isPending}>
          {upload.isPending ? 'Uploading…' : url ? 'Replace design' : 'Upload design'}
        </label>
        {url && (
          <button type="button" className="btn btn-danger-ghost" disabled={remove.isPending} onClick={() => window.confirm('Remove the uploaded design?') && remove.mutate()}>
            Remove
          </button>
        )}
        <Link className="btn" to="/menu-banner">
          Open generated banner
        </Link>
        <Link className="btn" to="/admin/design-studio?kind=banner">
          Design your own banner
        </Link>
      </div>
      <small className="muted">PNG, JPEG, WebP or PDF, up to 15 MB.</small>
    </div>
  )
}

export function BrandSettings({ brand }: { brand: BrandingDto }) {
  return (
    <>
      <BrandForm brand={brand} />
      <LogoSection brand={brand} />
      <TarpaulinSection brand={brand} />
    </>
  )
}
