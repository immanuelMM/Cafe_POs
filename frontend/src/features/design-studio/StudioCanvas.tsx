import { useCallback, useLayoutEffect, useMemo, useRef, useState, type DragEvent, type RefObject } from 'react'
import type Konva from 'konva'
import type { KonvaEventObject } from 'konva/lib/Node'
import { Image as KImage, Layer, Line, Rect, Stage, Transformer } from 'react-konva'
import { fillProps } from './geometry'
import { resolveSrc, useStudioImage } from './images'
import { useStudioEnv } from './env'
import { ElementNode, type NodeHandlers } from './ElementNode'
import { TextEditor } from './TextEditor'
import { DRAG_MIME } from './useAdd'
import { boundsOf, useStudio } from './store'
import type { Background, StudioElement, TextElement } from './types'

export interface DropPayload {
  json?: string
  files: File[]
  /** logical canvas coordinates of the drop point */
  at: { x: number; y: number }
}

interface Props {
  scale: number
  /** touch-friendly additive selection (no shift key on tablets) */
  multiMode: boolean
  stageRef: RefObject<Konva.Stage | null>
  onDropPayload: (p: DropPayload) => void
}

function BackgroundNode({ bg, width, height }: { bg: Background; width: number; height: number }) {
  const { logoUrl } = useStudioEnv()
  const { image } = useStudioImage(bg.type === 'image' ? resolveSrc(bg.src, logoUrl) : null)
  if (bg.type === 'transparent') return null
  if (bg.type === 'solid') return <Rect width={width} height={height} fill={bg.color} />
  if (bg.type === 'gradient')
    return (
      <Rect
        width={width}
        height={height}
        {...fillProps({ type: 'gradient', stops: bg.stops, angle: bg.angle }, width, height)}
      />
    )
  return (
    <>
      <Rect width={width} height={height} fill={bg.color} />
      {image &&
        (bg.fit === 'cover'
          ? (() => {
              const k = Math.max(width / image.naturalWidth, height / image.naturalHeight)
              const sw = width / k
              const sh = height / k
              return (
                <KImage
                  image={image}
                  width={width}
                  height={height}
                  crop={{ x: (image.naturalWidth - sw) / 2, y: (image.naturalHeight - sh) / 2, width: sw, height: sh }}
                />
              )
            })()
          : (() => {
              const k = Math.min(width / image.naturalWidth, height / image.naturalHeight)
              const w = image.naturalWidth * k
              const h = image.naturalHeight * k
              return <KImage image={image} x={(width - w) / 2} y={(height - h) / 2} width={w} height={h} />
            })())}
    </>
  )
}

interface Guide {
  points: number[]
}

const snap8 = 8

export function StudioCanvas({ scale, multiMode, stageRef, onDropPayload }: Props) {
  const doc = useStudio((s) => s.doc)
  const selected = useStudio((s) => s.selected)
  const trRef = useRef<Konva.Transformer>(null)
  const contentRef = useRef<Konva.Layer>(null)
  const wrapRef = useRef<HTMLDivElement>(null)
  const scaleRef = useRef(scale)
  scaleRef.current = scale
  const multiRef = useRef(multiMode)
  multiRef.current = multiMode
  const anchorRef = useRef<string | null>(null)
  const dragRef = useRef<{
    ids: string[]
    start: Map<string, { x: number; y: number }>
    origin: { x: number; y: number }
  } | null>(null)
  const [guides, setGuides] = useState<Guide[]>([])
  const [marquee, setMarquee] = useState<{ x: number; y: number; w: number; h: number } | null>(null)
  const marqueeStart = useRef<{ x: number; y: number; additive: boolean } | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)

  const findNode = useCallback((id: string) => stageRef.current?.findOne<Konva.Node>(`#${id}`) ?? null, [stageRef])

  // Attach the transformer to the selected (unlocked, visible) nodes after every render that could change them.
  useLayoutEffect(() => {
    const tr = trRef.current
    if (!tr) return
    const nodes = doc.elements
      .filter((e) => selected.includes(e.id) && !e.locked && !e.hidden && e.id !== editingId)
      .map((e) => findNode(e.id))
      .filter((n): n is Konva.Node => !!n)
    tr.nodes(nodes)
    tr.getLayer()?.batchDraw()
  }, [doc.elements, selected, editingId, findNode])

  const selectedEls = useMemo(() => doc.elements.filter((e) => selected.includes(e.id)), [doc.elements, selected])
  const anchors = useMemo(() => {
    if (selectedEls.length === 1) {
      const e = selectedEls[0]
      if (e.kind === 'shape' && (e.shape === 'line' || e.shape === 'arrow')) return ['middle-left', 'middle-right']
      if (e.kind === 'text')
        return ['top-left', 'top-right', 'bottom-left', 'bottom-right', 'middle-left', 'middle-right']
    }
    return undefined
  }, [selectedEls])

  const handlers: NodeHandlers = useMemo(
    () => ({
      onPointerDown: (id, e) => {
        e.cancelBubble = true
        const st = useStudio.getState()
        const evt = e.evt as MouseEvent
        const additive = multiRef.current || evt.shiftKey || evt.ctrlKey || evt.metaKey
        if (additive) st.toggleSelect(id)
        else if (!st.selected.includes(id)) st.select([id])
      },
      onDragStart: (id) => {
        const st = useStudio.getState()
        const ids = (st.selected.includes(id) ? st.selected : [id]).filter((i) =>
          st.doc.elements.find((e) => e.id === i && !e.locked),
        )
        const start = new Map<string, { x: number; y: number }>()
        for (const i of ids) {
          const n = stageRef.current?.findOne<Konva.Node>(`#${i}`)
          if (n) start.set(i, { x: n.x(), y: n.y() })
        }
        dragRef.current = { ids, start, origin: start.get(id) ?? { x: 0, y: 0 } }
      },
      onDragMove: (id, node) => {
        const d = dragRef.current
        const layer = contentRef.current
        if (!d || !layer) return
        const st = useStudio.getState()
        const T = snap8 / scaleRef.current
        const r = node.getClientRect({ relativeTo: layer, skipShadow: true })
        const xs = [0, st.doc.width / 2, st.doc.width]
        const ys = [0, st.doc.height / 2, st.doc.height]
        for (const e of st.doc.elements) {
          if (d.ids.includes(e.id) || e.hidden) continue
          const b = boundsOf(e)
          xs.push(b.x, b.x + b.width / 2, b.x + b.width)
          ys.push(b.y, b.y + b.height / 2, b.y + b.height)
        }
        const mineX = [r.x, r.x + r.width / 2, r.x + r.width]
        const mineY = [r.y, r.y + r.height / 2, r.y + r.height]
        let bx: { d: number; at: number } | null = null
        let by: { d: number; at: number } | null = null
        for (const m of mineX)
          for (const t of xs)
            if (Math.abs(t - m) < T && (!bx || Math.abs(t - m) < Math.abs(bx.d))) bx = { d: t - m, at: t }
        for (const m of mineY)
          for (const t of ys)
            if (Math.abs(t - m) < T && (!by || Math.abs(t - m) < Math.abs(by.d))) by = { d: t - m, at: t }
        if (bx) node.x(node.x() + bx.d)
        if (by) node.y(node.y() + by.d)
        const gs: Guide[] = []
        if (bx) gs.push({ points: [bx.at, 0, bx.at, st.doc.height] })
        if (by) gs.push({ points: [0, by.at, st.doc.width, by.at] })
        setGuides(gs)
        // Move the rest of a multi-selection by the same delta.
        const dx = node.x() - d.origin.x
        const dy = node.y() - d.origin.y
        for (const i of d.ids) {
          if (i === id) continue
          const s0 = d.start.get(i)
          const n = stageRef.current?.findOne<Konva.Node>(`#${i}`)
          if (s0 && n) n.position({ x: s0.x + dx, y: s0.y + dy })
        }
        trRef.current?.forceUpdate()
      },
      onDragEnd: (id, node) => {
        const d = dragRef.current
        dragRef.current = null
        setGuides([])
        if (!d || node.id() !== id) return
        const pos = new Map<string, { x: number; y: number }>()
        for (const i of d.ids) {
          const n = stageRef.current?.findOne<Konva.Node>(`#${i}`)
          if (n) pos.set(i, { x: Math.round(n.x() * 10) / 10, y: Math.round(n.y() * 10) / 10 })
        }
        useStudio.getState().update([...pos.keys()], (el) => pos.get(el.id) ?? {})
      },
      onEditText: (id) => {
        const el = useStudio.getState().doc.elements.find((e) => e.id === id)
        if (el && !el.locked) setEditingId(id)
      },
      onMeasured: (id, h) => useStudio.getState().setMeasured(id, h),
    }),
    [stageRef],
  )

  const onTransformEnd = () => {
    const tr = trRef.current
    if (!tr) return
    const st = useStudio.getState()
    const patches = new Map<string, Record<string, unknown>>()
    const side = anchorRef.current === 'middle-left' || anchorRef.current === 'middle-right'
    for (const node of tr.nodes()) {
      const el = st.doc.elements.find((e) => e.id === node.id())
      if (!el) continue
      const sx = Math.abs(node.scaleX())
      const sy = Math.abs(node.scaleY())
      const patch: Record<string, unknown> = {
        x: Math.round(node.x() * 10) / 10,
        y: Math.round(node.y() * 10) / 10,
        rotation: Math.round(node.rotation() * 10) / 10,
      }
      if (el.kind === 'text') {
        patch.width = Math.max(20, Math.round(el.width * sx))
        if (!side && Math.abs(sy - 1) > 0.001) patch.fontSize = Math.max(4, Math.round(el.fontSize * sy * 10) / 10)
      } else if (el.kind === 'shape' && (el.shape === 'line' || el.shape === 'arrow')) {
        patch.width = Math.max(4, Math.round(el.width * sx))
      } else {
        patch.width = Math.max(4, Math.round(el.width * sx))
        patch.height = Math.max(4, Math.round(el.height * sy))
      }
      node.scaleX(1)
      node.scaleY(1)
      patches.set(el.id, patch)
    }
    st.update([...patches.keys()], (el) => patches.get(el.id) ?? {})
  }

  // Stage background interactions: deselect / rubber-band selection (mouse); tap on empty canvas deselects (touch).
  const onStageDown = (e: KonvaEventObject<MouseEvent | TouchEvent>) => {
    if (e.target !== e.target.getStage()) return
    const evt = e.evt as MouseEvent
    const additive = multiRef.current || evt.shiftKey || evt.ctrlKey || evt.metaKey
    if (!additive) useStudio.getState().select([])
    if (e.type === 'mousedown') {
      const p = stageRef.current?.getRelativePointerPosition()
      if (p) marqueeStart.current = { x: p.x, y: p.y, additive }
    }
  }
  const onStageMove = () => {
    const s = marqueeStart.current
    const p = stageRef.current?.getRelativePointerPosition()
    if (!s || !p) return
    setMarquee({ x: Math.min(s.x, p.x), y: Math.min(s.y, p.y), w: Math.abs(p.x - s.x), h: Math.abs(p.y - s.y) })
  }
  const endMarquee = () => {
    const s = marqueeStart.current
    marqueeStart.current = null
    const m = marquee
    setMarquee(null)
    if (!s || !m || m.w * scaleRef.current < 4 || m.h * scaleRef.current < 4) return
    const st = useStudio.getState()
    const hit = st.doc.elements
      .filter((e) => !e.hidden && !e.locked)
      .filter((e) => {
        const b = boundsOf(e)
        return b.x < m.x + m.w && b.x + b.width > m.x && b.y < m.y + m.h && b.y + b.height > m.y
      })
      .map((e) => e.id)
    st.select(s.additive ? [...new Set([...st.selected, ...hit])] : hit)
  }

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault()
    const rect = wrapRef.current?.getBoundingClientRect()
    if (!rect) return
    onDropPayload({
      json: e.dataTransfer.getData(DRAG_MIME) || undefined,
      files: Array.from(e.dataTransfer.files),
      at: { x: (e.clientX - rect.left) / scale, y: (e.clientY - rect.top) / scale },
    })
  }

  const editing = editingId ? (doc.elements.find((e) => e.id === editingId) as TextElement | undefined) : undefined
  const commitText = (text: string) => {
    if (editing && text !== editing.text) useStudio.getState().update([editing.id], { text })
    setEditingId(null)
  }
  const uiScale = 1 / scale

  return (
    <div
      ref={wrapRef}
      className="studio-canvas"
      style={{ width: doc.width * scale, height: doc.height * scale }}
      onDragOver={(e) => {
        e.preventDefault()
        e.dataTransfer.dropEffect = 'copy'
      }}
      onDrop={onDrop}
    >
      <Stage
        ref={stageRef}
        width={doc.width * scale}
        height={doc.height * scale}
        scaleX={scale}
        scaleY={scale}
        onMouseDown={onStageDown}
        onTouchStart={onStageDown}
        onMouseMove={onStageMove}
        onMouseUp={endMarquee}
        onMouseLeave={endMarquee}
      >
        <Layer listening={false} name="studio-bg">
          <BackgroundNode bg={doc.background} width={doc.width} height={doc.height} />
        </Layer>
        <Layer ref={contentRef} name="studio-content">
          {doc.elements.map((el: StudioElement) => (
            <ElementNode key={el.id} el={el} editingId={editingId} handlers={handlers} />
          ))}
        </Layer>
        <Layer name="studio-ui">
          {guides.map((g, i) => (
            <Line
              key={i}
              points={g.points}
              stroke="#e0457b"
              strokeWidth={1.5 * uiScale}
              dash={[6 * uiScale, 4 * uiScale]}
              listening={false}
            />
          ))}
          {marquee && (
            <Rect
              x={marquee.x}
              y={marquee.y}
              width={marquee.w}
              height={marquee.h}
              fill="rgba(180,86,31,0.12)"
              stroke="#b4561f"
              strokeWidth={1.5 * uiScale}
              listening={false}
            />
          )}
          <Transformer
            ref={trRef}
            keepRatio={false}
            shiftBehavior="inverted"
            flipEnabled={false}
            ignoreStroke
            rotationSnaps={[0, 45, 90, 135, 180, 225, 270, 315]}
            rotationSnapTolerance={4}
            enabledAnchors={anchors}
            anchorSize={14}
            anchorStrokeWidth={2}
            anchorCornerRadius={3}
            borderStrokeWidth={2}
            rotateAnchorOffset={32}
            anchorStroke="#b4561f"
            borderStroke="#b4561f"
            padding={2}
            boundBoxFunc={(o, n) => (n.width < 6 || n.height < 6 ? o : n)}
            onTransformStart={() => {
              anchorRef.current = trRef.current?.getActiveAnchor() ?? null
            }}
            onTransformEnd={onTransformEnd}
          />
        </Layer>
      </Stage>
      {editing && <TextEditor el={editing} scale={scale} onCommit={commitText} onCancel={() => setEditingId(null)} />}
    </div>
  )
}
