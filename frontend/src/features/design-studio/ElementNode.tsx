import { memo, useLayoutEffect, useMemo, useRef } from 'react'
import Konva from 'konva'
import type { KonvaEventObject } from 'konva/lib/Node'
import { Group, Image as KImage, Rect, Shape, Text, TextPath } from 'react-konva'
import { assetUrl } from '../branding/brandingApi'
import { useStudioEnv } from './env'
import { arcPathData, fillProps, hexToRgba, traceRoundRect, traceShape, usesEvenOdd, type PathCtx } from './geometry'
import { flippedImage, resolveSrc, useStudioImage } from './images'
import { layoutMenu } from './menuLayout'
import type { ImageElement, MenuElement, Shadow, ShapeElement, StudioElement, TextElement } from './types'

export interface NodeHandlers {
  onPointerDown: (id: string, e: KonvaEventObject<MouseEvent | TouchEvent>) => void
  onDragStart: (id: string) => void
  onDragMove: (id: string, node: Konva.Node) => void
  onDragEnd: (id: string, node: Konva.Node) => void
  onEditText: (id: string) => void
  onMeasured: (id: string, height: number) => void
}

interface Props {
  el: StudioElement
  /** id of the text element currently edited in the overlay (its canvas node is hidden meanwhile) */
  editingId: string | null
  handlers: NodeHandlers
}

const shadowProps = (s: Shadow | null) =>
  s
    ? {
        shadowColor: s.color,
        shadowBlur: s.blur,
        shadowOffsetX: s.offsetX,
        shadowOffsetY: s.offsetY,
        shadowOpacity: s.opacity,
        shadowEnabled: true,
      }
    : {}

function useCommon(el: StudioElement, editingId: string | null, h: NodeHandlers) {
  return {
    id: el.id,
    name: 'studio-el',
    x: el.x,
    y: el.y,
    rotation: el.rotation,
    opacity: el.opacity,
    visible: !el.hidden && editingId !== el.id,
    listening: !el.locked,
    draggable: !el.locked,
    onMouseDown: (e: KonvaEventObject<MouseEvent>) => h.onPointerDown(el.id, e),
    onTouchStart: (e: KonvaEventObject<TouchEvent>) => h.onPointerDown(el.id, e),
    onDragStart: () => h.onDragStart(el.id),
    onDragMove: (e: KonvaEventObject<DragEvent>) => h.onDragMove(el.id, e.target),
    onDragEnd: (e: KonvaEventObject<DragEvent>) => h.onDragEnd(el.id, e.target),
  }
}

function ShapeNode({ el, editingId, handlers }: Props & { el: ShapeElement }) {
  const common = useCommon(el, editingId, handlers)
  const stroked = el.stroke && el.strokeWidth > 0
  const isLine = el.shape === 'line' || el.shape === 'arrow'
  const extra: Record<string, unknown> = {}
  if (usesEvenOdd(el)) extra.fillRule = 'evenodd'
  return (
    <Shape
      {...common}
      width={el.width}
      height={el.height}
      {...fillProps(isLine ? null : el.fill, el.width, el.height)}
      {...extra}
      stroke={stroked ? (el.stroke ?? undefined) : undefined}
      strokeWidth={stroked ? el.strokeWidth : 0}
      lineCap="round"
      lineJoin="round"
      hitStrokeWidth={isLine ? Math.max(28, el.strokeWidth) : undefined}
      {...shadowProps(el.shadow)}
      sceneFunc={(ctx, shape) => {
        const c = ctx as unknown as Konva.Context & PathCtx
        c.save()
        if (el.flipX || el.flipY) {
          c.translate(el.flipX ? el.width : 0, el.flipY ? el.height : 0)
          c.scale(el.flipX ? -1 : 1, el.flipY ? -1 : 1)
        }
        traceShape(c, el)
        c.restore()
        c.fillStrokeShape(shape)
      }}
    />
  )
}

let textProbe: Konva.Text | null = null
function textLength(el: TextElement) {
  textProbe ??= new Konva.Text()
  textProbe.setAttrs({
    text: el.text.replace(/\s*\n\s*/g, ' '),
    fontFamily: el.fontFamily,
    fontSize: el.fontSize,
    fontStyle: `${el.italic ? 'italic ' : ''}${el.bold ? 'bold' : ''}`.trim() || 'normal',
    letterSpacing: el.letterSpacing,
    width: 'auto',
  })
  return textProbe.getTextWidth()
}

function TextNode({ el, editingId, handlers }: Props & { el: TextElement }) {
  const common = useCommon(el, editingId, handlers)
  const ref = useRef<Konva.Text>(null)
  const curved = Math.abs(el.curve) >= 1
  const style = `${el.italic ? 'italic ' : ''}${el.bold ? 'bold' : ''}`.trim() || 'normal'
  // Keep the stored height equal to the laid-out height (layers, alignment and the editor overlay rely on it).
  useLayoutEffect(() => {
    if (!curved && ref.current) handlers.onMeasured(el.id, ref.current.height())
  })
  const stroked = el.stroke && el.strokeWidth > 0
  const paint = {
    ...fillProps(el.fill, el.width, el.height),
    stroke: stroked ? (el.stroke ?? undefined) : undefined,
    strokeWidth: stroked ? el.strokeWidth : 0,
    fillAfterStrokeEnabled: true,
    lineJoin: 'round' as const,
    ...shadowProps(el.shadow),
  }
  if (curved) {
    return (
      <TextPath
        {...common}
        {...paint}
        data={arcPathData(el.width, el.curve, textLength(el))}
        text={el.text.replace(/\s*\n\s*/g, ' ')}
        fontFamily={el.fontFamily}
        fontSize={el.fontSize}
        fontStyle={style}
        letterSpacing={el.letterSpacing}
        onDblClick={() => handlers.onEditText(el.id)}
        onDblTap={() => handlers.onEditText(el.id)}
      />
    )
  }
  return (
    <Text
      {...common}
      {...paint}
      ref={ref}
      width={el.width}
      text={el.text}
      fontFamily={el.fontFamily}
      fontSize={el.fontSize}
      fontStyle={style}
      textDecoration={el.underline ? 'underline' : ''}
      align={el.align}
      lineHeight={el.lineHeight}
      letterSpacing={el.letterSpacing}
      onDblClick={() => handlers.onEditText(el.id)}
      onDblTap={() => handlers.onEditText(el.id)}
    />
  )
}

/** Shadow of an image, drawn far off-canvas and shifted back by the shadow offset so the clip never cuts it. */
const FAR = 100000

function ImageNode({ el, editingId, handlers }: Props & { el: ImageElement }) {
  const common = useCommon(el, editingId, handlers)
  const { logoUrl } = useStudioEnv()
  const { image, failed } = useStudioImage(resolveSrc(el.src, logoUrl))
  const source = image ? flippedImage(image, el.flipX, el.flipY) : null
  const stroked = el.stroke && el.strokeWidth > 0
  const w = el.width
  const h = el.height

  const draw = (c: Konva.Context, img: HTMLImageElement, src: CanvasImageSource) => {
    const nw = img.naturalWidth
    const nh = img.naturalHeight
    if (el.fit === 'fill') c.drawImage(src, 0, 0, nw, nh, 0, 0, w, h)
    else if (el.fit === 'contain') {
      const k = Math.min(w / nw, h / nh)
      c.drawImage(src, 0, 0, nw, nh, (w - nw * k) / 2, (h - nh * k) / 2, nw * k, nh * k)
    } else {
      const k = Math.max(w / nw, h / nh)
      const sw = w / k
      const sh = h / k
      c.drawImage(src, (nw - sw) / 2, (nh - sh) / 2, sw, sh, 0, 0, w, h)
    }
  }

  return (
    <Shape
      {...common}
      width={w}
      height={h}
      fill={source ? undefined : failed ? '#f3d4d0' : '#e8dfd2'}
      stroke={stroked ? (el.stroke ?? undefined) : undefined}
      strokeWidth={stroked ? el.strokeWidth : 0}
      hitFunc={(ctx, shape) => {
        const c = ctx as unknown as Konva.Context & PathCtx
        traceRoundRect(c, w, h, el.cornerRadius)
        c.fillShape(shape)
      }}
      sceneFunc={(ctx, shape) => {
        const c = ctx as unknown as Konva.Context & PathCtx
        if (image && source) {
          const sh = el.shadow
          if (sh) {
            const sc = shape.getAbsoluteScale()
            const pr = c.canvas.getPixelRatio()
            c.save()
            c.translate(-FAR, 0)
            c.setAttr('shadowColor', hexToRgba(sh.color, sh.opacity))
            c.setAttr('shadowBlur', sh.blur * Math.min(Math.abs(sc.x), Math.abs(sc.y)) * pr)
            c.setAttr('shadowOffsetX', (sh.offsetX + FAR) * sc.x * pr)
            c.setAttr('shadowOffsetY', sh.offsetY * sc.y * pr)
            if (el.cornerRadius > 0 || el.fit !== 'fill') {
              traceRoundRect(c, w, h, el.cornerRadius)
              c.setAttr('fillStyle', '#000')
              c.fill()
            } else {
              draw(c, image, source)
            }
            c.restore()
          }
          c.save()
          traceRoundRect(c, w, h, el.cornerRadius)
          c.clip()
          draw(c, image, source)
          c.restore()
          if (stroked) {
            traceRoundRect(c, w, h, el.cornerRadius)
            c.strokeShape(shape)
          }
        } else {
          traceRoundRect(c, w, h, el.cornerRadius)
          c.fillStrokeShape(shape)
        }
      }}
    />
  )
}

function MenuPhoto({ path, x, y, size }: { path: string; x: number; y: number; size: number }) {
  const { image } = useStudioImage(assetUrl(path))
  if (!image)
    return <Rect x={x} y={y} width={size} height={size} fill="#e8dfd2" cornerRadius={size * 0.08} listening={false} />
  const k = Math.max(size / image.naturalWidth, size / image.naturalHeight)
  const sw = size / k
  const sh = size / k
  return (
    <KImage
      image={image}
      x={x}
      y={y}
      width={size}
      height={size}
      cornerRadius={size * 0.08}
      crop={{ x: (image.naturalWidth - sw) / 2, y: (image.naturalHeight - sh) / 2, width: sw, height: sh }}
      listening={false}
    />
  )
}

function MenuNode({ el, editingId, handlers }: Props & { el: MenuElement }) {
  const common = useCommon(el, editingId, handlers)
  const { sections } = useStudioEnv()
  const layout = useMemo(() => layoutMenu(el, sections), [el, sections])
  return (
    <Group {...common}>
      <Rect
        width={el.width}
        height={el.height}
        fill={el.background ?? undefined}
        hitFunc={(ctx, shape) => {
          ctx.beginPath()
          ctx.rect(0, 0, el.width, el.height)
          ctx.closePath()
          ctx.fillShape(shape)
        }}
      />
      {layout.draws.map((d, i) => {
        if (d.t === 'rule')
          return <Rect key={i} x={d.x} y={d.y} width={d.w} height={d.h} fill={d.color} listening={false} />
        if (d.t === 'photo') return <MenuPhoto key={i} path={d.path} x={d.x} y={d.y} size={d.size} />
        return (
          <Text
            key={i}
            x={d.x}
            y={d.y}
            width={d.w}
            text={d.text}
            fontFamily={el.fontFamily}
            fontSize={d.size}
            fontStyle={d.bold ? 'bold' : 'normal'}
            fill={d.color}
            opacity={d.opacity}
            align={d.align}
            lineHeight={1.2}
            listening={false}
          />
        )
      })}
    </Group>
  )
}

function ElementNodeImpl(props: Props) {
  const { el } = props
  switch (el.kind) {
    case 'shape':
      return <ShapeNode {...props} el={el} />
    case 'text':
      return <TextNode {...props} el={el} />
    case 'image':
      return <ImageNode {...props} el={el} />
    case 'menu':
      return <MenuNode {...props} el={el} />
  }
}

export const ElementNode = memo(ElementNodeImpl)
