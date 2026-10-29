import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type Konva from 'konva'
import { ErrorBox, Spinner } from '../../components/ui'
import { errorMessage } from '../../lib/errors'
import {
  DEFAULT_BRAND_NAME,
  assetUrl,
  toPalette,
  useBranding,
  useUploadLogo,
  useUploadTarpaulin,
} from '../branding/useBranding'
import { buildSections } from '../branding/menuSections'
import { useCategories, useItems } from '../catalog/useCatalog'
import { useCurrentLocation } from '../locations/useLocations'
import { AddPanel } from './AddPanel'
import { StudioEnvContext, type StudioEnv } from './env'
import { LayersPanel } from './LayersPanel'
import { PrintSheet } from './PrintSheet'
import { PropertiesPanel } from './PropertiesPanel'
import { StudioCanvas, type DropPayload } from './StudioCanvas'
import { TemplatePicker } from './TemplatePicker'
import type { MenuSectionCtx } from './templates'
import { formatMoney } from '../../lib/format'
import { ItemPhotoPicker } from './ItemPhotoPicker'
import { MAX_DESIGN_BYTES, WARN_DESIGN_BYTES, docBytes, formatBytes, useSaveDesign } from './useDesign'
import { clearDraft, readDraft, useDraftAutosave, useUnsavedGuard } from './draft'
import { blankDoc, parseDoc } from './factory'
import { downloadBlob, exportBanner, exportDownload, exportLogo, exportPrint } from './exporter'
import { defaultPreset, findPreset } from './presets'
import { useStudio } from './store'
import type { DesignKind, SavedDesign, StudioDoc } from './types'
import { useAddActions, type AddPayload } from './useAdd'
import { useShortcuts } from './useShortcuts'
import { Modal } from '../../components/Modal'

type Status = { type: 'ok' | 'error'; text: string } | null
type Sheet = 'add' | 'layers' | 'props'

/** Three-column layout from 1100px; below that (phones, tablet portrait) the panels are bottom sheets. */
const useIsWide = () => {
  const [wide, setWide] = useState(() => window.matchMedia('(min-width: 1100px)').matches)
  useEffect(() => {
    const m = window.matchMedia('(min-width: 1100px)')
    const on = () => setWide(m.matches)
    m.addEventListener('change', on)
    return () => m.removeEventListener('change', on)
  }, [])
  return wide
}

/** Physical print size of the page: the preset's, or a 75 dpi equivalent for custom sizes. */
const printSize = (doc: StudioDoc) =>
  findPreset(doc.presetId)?.print ?? {
    w: Math.round((doc.width / 75) * 10) / 10,
    h: Math.round((doc.height / 75) * 10) / 10,
    unit: 'in' as const,
  }

interface Props {
  kind: DesignKind
  saved: SavedDesign | null
  /** the kind tabs, rendered at the start of the toolbar */
  tabs: ReactNode
}

export function StudioEditor({ kind, saved, tabs }: Props) {
  const branding = useBranding()
  const cats = useCategories(false)
  const items = useItems({ includeInactive: false, pageSize: 100 })
  const location = useCurrentLocation()
  const saveDesign = useSaveDesign(kind)
  const uploadLogo = useUploadLogo()
  const uploadTarp = useUploadTarpaulin()
  const wide = useIsWide()
  const stageRef = useRef<Konva.Stage | null>(null)
  const areaRef = useRef<HTMLDivElement>(null)

  const doc = useStudio((s) => s.doc)
  const dirty = useStudio((s) => s.dirty)
  const canUndo = useStudio((s) => s.past.length > 0)
  const canRedo = useStudio((s) => s.future.length > 0)
  const selectedCount = useStudio((s) => s.selected.length)

  // Decide what to open with once: saved design, a newer local draft, or a blank page with the template picker.
  const [boot] = useState(() => {
    const draft = readDraft(kind)
    const parsed = saved ? parseDoc(saved.document, kind) : null
    const draftNewer = !!draft && (!saved || draft.savedAt > Date.parse(saved.updatedAt))
    return {
      doc: parsed ?? blankDoc(kind),
      isNew: !parsed,
      unreadable: !!saved && !parsed,
      draft: draftNewer ? draft : null,
    }
  })
  const [ready, setReady] = useState(false)
  const [modal, setModal] = useState<'draft' | 'templates' | 'photo' | null>(
    boot.draft ? 'draft' : boot.isNew ? 'templates' : null,
  )
  useLayoutEffect(() => {
    useStudio.getState().load(boot.doc)
    setReady(true)
  }, [boot])

  const [zoom, setZoom] = useState<'fit' | number>('fit')
  const [area, setArea] = useState({ w: 0, h: 0 })
  useLayoutEffect(() => {
    const el = areaRef.current
    if (!el) return
    const measure = () => setArea({ w: el.clientWidth, h: el.clientHeight })
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [ready])
  const fit = Math.max(0.02, Math.min((area.w - 32) / doc.width, (area.h - 32) / doc.height, 3))
  const scale = zoom === 'fit' ? fit : zoom
  const zoomBy = (f: number) => setZoom(Math.min(4, Math.max(0.02, scale * f)))

  const [leftTab, setLeftTab] = useState<'add' | 'layers'>('add')
  const [sheet, setSheet] = useState<Sheet | null>(null)
  const [multiMode, setMultiMode] = useState(false)
  const [status, setStatus] = useState<Status>(null)
  const [busy, setBusy] = useState<'save' | 'publish' | 'download' | 'print' | null>(null)
  const [draftOk, setDraftOk] = useState(true)
  const [everSaved, setEverSaved] = useState(saved !== null && !boot.unreadable)
  const [printing, setPrinting] = useState<{ url: string } | null>(null)

  // Live data the canvas draws from.
  const b = branding.data
  const palette = useMemo(() => toPalette(b) ?? { primary: '#3b2417' }, [b])
  const logoUrl = assetUrl(b?.logoUrl)
  const sections = useMemo(() => buildSections(cats.data ?? [], items.data?.data ?? [], false), [cats.data, items.data])
  const env: StudioEnv = useMemo(() => ({ logoUrl, sections, palette }), [logoUrl, sections, palette])
  const add = useAddActions(env)

  useUnsavedGuard(dirty)
  useDraftAutosave(kind, doc, dirty, ready && modal !== 'draft', setDraftOk)

  const bytes = useMemo(() => docBytes(doc), [doc])
  const tooBig = bytes > MAX_DESIGN_BYTES

  const save = useCallback(async () => {
    const st = useStudio.getState()
    const size = docBytes(st.doc)
    if (size > MAX_DESIGN_BYTES)
      return setStatus({
        type: 'error',
        text: `This design is ${formatBytes(size)}; the limit is 8 MB. Remove or replace large images, then save again.`,
      })
    if (
      size > WARN_DESIGN_BYTES &&
      !window.confirm(`This design is ${formatBytes(size)} and the limit is 8 MB. Save anyway?`)
    )
      return
    setBusy('save')
    setStatus(null)
    try {
      await saveDesign.mutateAsync(st.doc)
      useStudio.getState().markSaved()
      clearDraft(kind)
      setDraftOk(true)
      setEverSaved(true)
      setStatus({ type: 'ok', text: 'Design saved.' })
    } catch (e) {
      setStatus({ type: 'error', text: errorMessage(e) })
    } finally {
      setBusy(null)
    }
  }, [kind, saveDesign])

  const publish = async () => {
    const stage = stageRef.current
    if (!stage) return
    const what = kind === 'logo' ? 'your logo' : 'your uploaded tarpaulin'
    if (!window.confirm(`Replace ${what} with this design? This changes it everywhere it is used.`)) return
    setBusy('publish')
    setStatus(null)
    try {
      if (kind === 'logo') {
        const ex = await exportLogo(stage, doc)
        await uploadLogo.mutateAsync(ex.blob)
        setStatus({ type: 'ok', text: `Logo updated (${ex.width} x ${ex.height} PNG, ${formatBytes(ex.blob.size)}).` })
      } else {
        const ex = await exportBanner(stage, doc)
        await uploadTarp.mutateAsync(ex.blob)
        setStatus({
          type: 'ok',
          text: `Tarpaulin updated (${ex.width} x ${ex.height} ${ex.mime === 'image/png' ? 'PNG' : 'JPEG'}, ${formatBytes(ex.blob.size)}).`,
        })
      }
    } catch (e) {
      setStatus({ type: 'error', text: errorMessage(e) })
    } finally {
      setBusy(null)
    }
  }

  const download = async () => {
    const stage = stageRef.current
    if (!stage) return
    setBusy('download')
    setStatus(null)
    try {
      const ex = await exportDownload(stage, doc)
      downloadBlob(ex.blob, `${(b?.brandName ?? DEFAULT_BRAND_NAME).replace(/[^\w-]+/g, '-')}-${kind}.png`)
      setStatus({ type: 'ok', text: `Downloaded ${ex.width} x ${ex.height} PNG (${formatBytes(ex.blob.size)}).` })
    } catch (e) {
      setStatus({ type: 'error', text: errorMessage(e) })
    } finally {
      setBusy(null)
    }
  }

  const print = async () => {
    const stage = stageRef.current
    if (!stage) return
    setBusy('print')
    setStatus(null)
    try {
      const blob = await exportPrint(stage, doc)
      setPrinting({ url: URL.createObjectURL(blob) })
    } catch (e) {
      setStatus({ type: 'error', text: errorMessage(e) })
    } finally {
      setBusy(null)
    }
  }
  const printUrl = printing?.url
  const donePrinting = useCallback(() => {
    setPrinting((p) => {
      if (p) URL.revokeObjectURL(p.url)
      return null
    })
  }, [])

  useShortcuts({ onSave: () => void save(), onPasteFiles: (f) => void add.addFiles(f) })

  const onDropPayload = (p: DropPayload) => {
    if (p.json) {
      try {
        add.addPayload(JSON.parse(p.json) as AddPayload, p.at)
      } catch {
        /* not ours */
      }
    }
    if (p.files.length) void add.addFiles(p.files, p.at)
  }
  // On phones the sheet closes after adding something so the canvas is visible again.
  const addAndClose = (p: AddPayload) => {
    add.addPayload(p)
    if (!wide) setSheet(null)
  }

  const applyTemplate = (next: StudioDoc) => {
    useStudio.getState().commit(next)
    useStudio.getState().select([])
    setModal(null)
    setZoom('fit')
  }

  const brandName = b?.brandName ?? DEFAULT_BRAND_NAME
  const tagline = b?.tagline ?? null
  const address = location.data?.address ?? null
  const phone = location.data?.contactPhone ?? null
  const menuSections = useMemo<MenuSectionCtx[]>(
    () =>
      buildSections(cats.data ?? [], items.data?.data ?? [], false).map((s) => ({
        name: s.category.name,
        items: s.items.map((i) => ({
          name: i.name,
          description: i.description?.trim() || null,
          path: i.imageUrl,
          prices: i.variations.map((v) => ({
            label: i.variations.length > 1 ? v.name : '',
            price: formatMoney(v.effectivePrice ?? v.price),
          })),
        })),
      })),
    [cats.data, items.data],
  )
  const tplCtx = useMemo(
    () => ({ brandName, tagline, palette, hasLogo: !!logoUrl, address, phone, menuSections }),
    [brandName, tagline, palette, logoUrl, address, phone, menuSections],
  )

  if (!ready) return <Spinner label="Opening the studio…" />

  const addPanel = (
    <AddPanel
      kind={kind}
      hasLogo={!!logoUrl}
      onAdd={wide ? add.addPayload : addAndClose}
      onFiles={(f) => void add.addFiles(f)}
      onPickItem={() => setModal('photo')}
      error={add.error}
    />
  )
  const props = <PropertiesPanel onTemplates={() => setModal('templates')} />

  return (
    <StudioEnvContext.Provider value={env}>
      <div className="studio">
        <div className="studio-bar" role="toolbar" aria-label="Design studio">
          {tabs}
          <div className="studio-group">
            <button
              type="button"
              className="btn btn-sm"
              disabled={!canUndo}
              onClick={() => useStudio.getState().undo()}
              aria-label="Undo"
              title="Undo (Ctrl+Z)"
            >
              Undo
            </button>
            <button
              type="button"
              className="btn btn-sm"
              disabled={!canRedo}
              onClick={() => useStudio.getState().redo()}
              aria-label="Redo"
              title="Redo (Ctrl+Shift+Z)"
            >
              Redo
            </button>
            <button
              type="button"
              className="btn btn-sm"
              aria-pressed={multiMode}
              onClick={() => setMultiMode((m) => !m)}
              title="Tap elements to add them to the selection"
            >
              Multi-select
            </button>
          </div>
          <div className="studio-group" role="group" aria-label="Zoom">
            <button
              type="button"
              className="btn btn-sm btn-icon"
              onClick={() => zoomBy(1 / 1.25)}
              aria-label="Zoom out"
            >
              −
            </button>
            <output className="studio-zoom" aria-live="polite">
              {Math.round(scale * 100)}%
            </output>
            <button type="button" className="btn btn-sm btn-icon" onClick={() => zoomBy(1.25)} aria-label="Zoom in">
              +
            </button>
            <button type="button" className="btn btn-sm" aria-pressed={zoom === 'fit'} onClick={() => setZoom('fit')}>
              Fit
            </button>
            <button type="button" className="btn btn-sm" onClick={() => setZoom(1)}>
              100%
            </button>
          </div>
          <div className="studio-group studio-group-end">
            <span
              className={`studio-size${tooBig ? ' studio-size-bad' : ''}`}
              title="Saved design size (server limit 8 MB)"
            >
              {formatBytes(bytes)} / 8 MB
            </span>
            <button type="button" className="btn btn-sm" onClick={() => setModal('templates')}>
              Templates
            </button>
            <button type="button" className="btn btn-sm" disabled={!!busy} onClick={() => void download()}>
              {busy === 'download' ? 'Rendering…' : 'Download PNG'}
            </button>
            {kind === 'banner' && (
              <button type="button" className="btn btn-sm" disabled={!!busy} onClick={() => void print()}>
                {busy === 'print' ? 'Preparing…' : 'Print / PDF'}
              </button>
            )}
            <button type="button" className="btn btn-sm" disabled={!!busy} onClick={() => void publish()}>
              {busy === 'publish' ? 'Publishing…' : kind === 'logo' ? 'Use as my logo' : 'Use as my tarpaulin'}
            </button>
            <button
              type="button"
              className="btn btn-sm btn-primary"
              disabled={!!busy || (!dirty && everSaved)}
              onClick={() => void save()}
            >
              {busy === 'save' ? 'Saving…' : !dirty && everSaved ? 'Saved' : 'Save design'}
            </button>
          </div>
        </div>

        <div className="studio-notices">
          {status && (
            <div
              className={`alert ${status.type === 'ok' ? 'alert-ok' : 'alert-error'}`}
              role={status.type === 'ok' ? 'status' : 'alert'}
            >
              <span>{status.text}</span>
              <button type="button" className="btn btn-sm btn-ghost" onClick={() => setStatus(null)}>
                Dismiss
              </button>
            </div>
          )}
          {boot.unreadable && (
            <div className="alert alert-error" role="alert">
              The saved design could not be read, so a blank page was opened. Saving will replace it.
            </div>
          )}
          {dirty && !draftOk && (
            <div className="alert alert-error" role="alert">
              Your browser could not keep a local draft (storage is full). Save the design to keep your work.
            </div>
          )}
          {(cats.error || items.error) && (
            <ErrorBox
              error={cats.error ?? items.error}
              onRetry={() => {
                void cats.refetch()
                void items.refetch()
              }}
            />
          )}
        </div>

        <div className="studio-body">
          {wide && (
            <aside className="studio-left" aria-label="Add and layers">
              <div className="tabs studio-tabs" role="tablist">
                <button
                  type="button"
                  role="tab"
                  aria-selected={leftTab === 'add'}
                  className={leftTab === 'add' ? 'tab tab-on' : 'tab'}
                  onClick={() => setLeftTab('add')}
                >
                  Add
                </button>
                <button
                  type="button"
                  role="tab"
                  aria-selected={leftTab === 'layers'}
                  className={leftTab === 'layers' ? 'tab tab-on' : 'tab'}
                  onClick={() => setLeftTab('layers')}
                >
                  Layers
                </button>
              </div>
              <div className="studio-panel-body">{leftTab === 'add' ? addPanel : <LayersPanel />}</div>
            </aside>
          )}

          <main className="studio-area" ref={areaRef} aria-label="Design canvas">
            <div className={`studio-sheetwrap${doc.background.type === 'transparent' ? ' studio-checker' : ''}`}>
              <StudioCanvas scale={scale} multiMode={multiMode} stageRef={stageRef} onDropPayload={onDropPayload} />
            </div>
          </main>

          {wide && (
            <aside className="studio-right" aria-label="Properties">
              <div className="studio-panel-body">{props}</div>
            </aside>
          )}
        </div>

        {!wide && (
          <>
            {sheet && (
              <div
                className="studio-sheet"
                role="dialog"
                aria-label={sheet === 'add' ? 'Add' : sheet === 'layers' ? 'Layers' : 'Properties'}
              >
                <div className="studio-sheet-head">
                  <strong>
                    {sheet === 'add' ? 'Add' : sheet === 'layers' ? 'Layers' : selectedCount ? 'Properties' : 'Page'}
                  </strong>
                  <button type="button" className="btn btn-sm btn-ghost" onClick={() => setSheet(null)}>
                    Close
                  </button>
                </div>
                <div className="studio-panel-body">
                  {sheet === 'add' ? addPanel : sheet === 'layers' ? <LayersPanel /> : props}
                </div>
              </div>
            )}
            <nav className="studio-bottom" aria-label="Panels">
              {(['add', 'layers', 'props'] as Sheet[]).map((s) => (
                <button
                  key={s}
                  type="button"
                  className={`btn btn-sm${sheet === s ? ' btn-primary' : ''}`}
                  aria-pressed={sheet === s}
                  onClick={() => setSheet(sheet === s ? null : s)}
                >
                  {s === 'add' ? 'Add' : s === 'layers' ? 'Layers' : selectedCount ? 'Edit' : 'Page'}
                </button>
              ))}
            </nav>
          </>
        )}

        {modal === 'draft' && boot.draft && (
          <Modal
            title="Restore unsaved draft?"
            onClose={() => undefined}
            footer={
              <>
                <button
                  type="button"
                  className="btn"
                  onClick={() => {
                    clearDraft(kind)
                    setModal(boot.isNew ? 'templates' : null)
                  }}
                >
                  Discard draft
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => {
                    useStudio.getState().load(boot.draft!.doc, true)
                    setModal(null)
                  }}
                >
                  Restore draft
                </button>
              </>
            }
          >
            <p>
              This browser has changes from {new Date(boot.draft.savedAt).toLocaleString()} that were never saved
              {saved ? ', newer than the saved design' : ''}.
            </p>
          </Modal>
        )}
        {modal === 'templates' && (
          <TemplatePicker
            kind={kind}
            presetId={doc.presetId || defaultPreset(kind).id}
            ctx={tplCtx}
            hasContent={doc.elements.length > 0}
            onPick={applyTemplate}
            onBlank={() => setModal(null)}
            onClose={() => setModal(null)}
          />
        )}
        {modal === 'photo' && <ItemPhotoPicker onPick={add.addItemPhoto} onClose={() => setModal(null)} />}
        {printUrl && <PrintSheet url={printUrl} size={printSize(doc)} onDone={donePrinting} />}
      </div>
    </StudioEnvContext.Provider>
  )
}
