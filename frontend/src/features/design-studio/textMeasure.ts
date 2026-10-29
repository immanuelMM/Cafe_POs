/** Text measuring for laying out templates before anything is on the canvas (browser canvas; rough estimate elsewhere). */
export interface TextStyle {
  family: string
  bold?: boolean
  italic?: boolean
}

let ctx: CanvasRenderingContext2D | null | undefined
const context = () => {
  if (ctx === undefined) ctx = typeof document === 'undefined' ? null : document.createElement('canvas').getContext('2d')
  return ctx
}

export function textWidth(text: string, size: number, s: TextStyle): number {
  const c = context()
  if (!c) return text.length * size * (s.bold ? 0.6 : 0.54)
  c.font = `${s.italic ? 'italic ' : ''}${s.bold ? 'bold ' : ''}${size}px ${s.family}`
  return c.measureText(text).width
}

/** Number of lines when word-wrapped into maxW (a single over-long word still counts as one line, like Konva). */
export function lineCount(text: string, size: number, maxW: number, s: TextStyle): number {
  let lines = 0
  for (const para of text.split('\n')) {
    let line = ''
    lines++
    for (const word of para.split(/\s+/).filter(Boolean)) {
      const next = line ? `${line} ${word}` : word
      if (line && textWidth(next, size, s) > maxW) {
        lines++
        line = word
      } else line = next
    }
  }
  return lines
}

/**
 * Largest size in [size * minScale, size] at which the text needs at most maxLines; below that it keeps the
 * smallest size and lets the text wrap further (callers size their box from the returned line count).
 */
export function fitText(text: string, size: number, maxW: number, maxLines: number, s: TextStyle, minScale = 0.7) {
  for (let f = size; f >= size * minScale; f *= 0.95) {
    const lines = lineCount(text, f, maxW, s)
    if (lines <= maxLines) return { size: f, lines }
  }
  const f = size * minScale
  return { size: f, lines: lineCount(text, f, maxW, s) }
}
