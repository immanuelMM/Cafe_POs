/** Design studio document model. Stored as opaque JSON by the API (PUT /branding/designs/{kind}). */
export const DOC_VERSION = 1

export type DesignKind = 'logo' | 'banner'

export type Fill = { type: 'solid'; color: string } | { type: 'gradient'; stops: string[]; angle: number }

export interface Shadow {
  color: string
  blur: number
  offsetX: number
  offsetY: number
  opacity: number
}

export type ShapeKind =
  | 'rect'
  | 'roundRect'
  | 'circle'
  | 'ellipse'
  | 'triangle'
  | 'star'
  | 'polygon'
  | 'line'
  | 'arrow'
  | 'heart'
  | 'bubble'
  | 'ring'
  | 'icon'

export type IconKind = 'cup' | 'bean' | 'leaf' | 'croissant' | 'cake' | 'steam'

/** Images are either embedded (data URL) or a catalog/branding asset path relative to the API origin. */
export type ImageSrc = { type: 'data'; url: string } | { type: 'api'; path: string }

interface Base {
  id: string
  name: string
  x: number
  y: number
  width: number
  height: number
  rotation: number
  opacity: number
  locked: boolean
  hidden: boolean
}

export interface ShapeElement extends Base {
  kind: 'shape'
  shape: ShapeKind
  icon?: IconKind
  fill: Fill | null
  stroke: string | null
  strokeWidth: number
  cornerRadius: number
  /** star: number of points */
  points: number
  /** star / ring: inner radius as a fraction of the outer radius */
  innerRatio: number
  /** polygon: number of sides */
  sides: number
  flipX: boolean
  flipY: boolean
  shadow: Shadow | null
}

export interface TextElement extends Base {
  kind: 'text'
  text: string
  fontFamily: string
  fontSize: number
  bold: boolean
  italic: boolean
  underline: boolean
  align: 'left' | 'center' | 'right'
  letterSpacing: number
  lineHeight: number
  fill: Fill
  stroke: string | null
  strokeWidth: number
  shadow: Shadow | null
  /** -100..100; 0 = straight text, otherwise the text follows an arc */
  curve: number
}

export interface ImageElement extends Base {
  kind: 'image'
  src: ImageSrc
  fit: 'cover' | 'contain' | 'fill'
  cornerRadius: number
  stroke: string | null
  strokeWidth: number
  flipX: boolean
  flipY: boolean
  shadow: Shadow | null
}

/** The live catalog (categories, items, prices) rendered as one movable block. Banner only. */
export interface MenuElement extends Base {
  kind: 'menu'
  columns: number
  fontFamily: string
  fontSize: number
  /** pick the largest font size that fits the block (fontSize is then only a starting point) */
  autoFit?: boolean
  textColor: string
  accentColor: string
  priceColor: string
  background: string | null
  showPhotos: boolean
  showDescriptions: boolean
}

export type StudioElement = ShapeElement | TextElement | ImageElement | MenuElement

export type Background =
  | { type: 'solid'; color: string }
  | { type: 'gradient'; stops: string[]; angle: number }
  | { type: 'transparent' }
  | { type: 'image'; src: ImageSrc; fit: 'cover' | 'contain'; color: string }

export interface StudioDoc {
  version: typeof DOC_VERSION
  kind: DesignKind
  presetId: string
  width: number
  height: number
  background: Background
  elements: StudioElement[]
}

export interface SavedDesign {
  kind: DesignKind
  document: unknown
  updatedAt: string
}
