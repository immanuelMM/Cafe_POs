import { expect, type Locator, type Page } from '@playwright/test'

/**
 * Layout assertions for docs/plan/tablet-ui-plan.md. Helpers return findings (or throw via expect) so specs can decide
 * whether a check is hard (passes today) or still a target (test.fixme / expect.soft until its milestone lands).
 */

/** The plan's tap-target floor in CSS px (section 2.1: no control under 48px). Enforced from M2/M3. */
export const MIN_TAP_PX = 48

/** The document must never scroll sideways. Hard check today. */
export async function expectNoHorizontalScroll(page: Page, label = 'page') {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
  expect(overflow, `${label} is wider than the viewport by ${overflow}px`).toBeLessThanOrEqual(1)
}

/**
 * The body must not scroll when the app shell is meant to fill the viewport (only inner panels scroll).
 * FAILS on the current UI (the page scrolls); tighten to a hard assertion at M2 (app shell).
 */
export async function expectBodyDoesNotScroll(page: Page, label = 'page') {
  const extra = await page.evaluate(() => document.documentElement.scrollHeight - window.innerHeight)
  expect(extra, `${label}: body scrolls by ${extra}px but the shell should fill the viewport`).toBeLessThanOrEqual(1)
}

export interface SmallTarget {
  desc: string
  width: number
  height: number
}

/** Visible interactive elements (in `scope`, default the page) smaller than `min` px in either dimension. */
export async function findSmallTargets(page: Page, min = MIN_TAP_PX, scope?: Locator): Promise<SmallTarget[]> {
  const root = scope ? await scope.elementHandle() : null
  return page.evaluate(
    ({ min, root }) => {
      const sel = 'button, a[href], input:not([type=hidden]), select, textarea, [role=button], [role=radio], [role=tab], [role=link]'
      const scopeEl: ParentNode = (root as Element | null) ?? document
      const out: { desc: string; width: number; height: number }[] = []
      for (const el of Array.from(scopeEl.querySelectorAll<HTMLElement>(sel))) {
        const cs = getComputedStyle(el)
        if (cs.visibility === 'hidden' || cs.display === 'none') continue
        // Radios/checkboxes are measured through their label, which is the real tap area.
        const box = (el instanceof HTMLInputElement && /radio|checkbox/.test(el.type) && el.closest('label') ? el.closest('label')! : el).getBoundingClientRect()
        if (box.width === 0 || box.height === 0) continue
        if (box.width < min - 0.5 || box.height < min - 0.5) {
          const name = (el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 30)
          out.push({ desc: `${el.tagName.toLowerCase()}${el.className ? '.' + String(el.className).split(' ')[0] : ''} "${name}"`, width: Math.round(box.width), height: Math.round(box.height) })
        }
      }
      return out
    },
    { min, root },
  )
}

/** Every visible interactive element must be at least `min` px. FAILS on the current UI; hard from M3 (tap sizes). */
export async function expectTapTargets(page: Page, min = MIN_TAP_PX, scope?: Locator) {
  const small = await findSmallTargets(page, min, scope)
  expect(small.map((s) => `${s.desc} ${s.width}x${s.height}`), `controls smaller than ${min}px`).toEqual([])
}

/** The element's whole box is inside the viewport (e.g. the Charge button must never be pushed off-screen). */
export async function expectFullyInViewport(page: Page, target: Locator, label = 'element') {
  await expect(target).toBeVisible()
  const box = await target.boundingBox()
  const vp = page.viewportSize()!
  expect(box, `${label} has no box`).not.toBeNull()
  expect(box!.x, `${label} left edge`).toBeGreaterThanOrEqual(-0.5)
  expect(box!.y, `${label} top edge`).toBeGreaterThanOrEqual(-0.5)
  expect(box!.x + box!.width, `${label} right edge`).toBeLessThanOrEqual(vp.width + 0.5)
  expect(box!.y + box!.height, `${label} bottom edge`).toBeLessThanOrEqual(vp.height + 0.5)
}

/** Smallest computed font-size (px) among the elements matching `target`. */
export async function minFontSize(target: Locator): Promise<number> {
  const sizes = await target.evaluateAll((els) => els.map((e) => parseFloat(getComputedStyle(e).fontSize)))
  expect(sizes.length, 'no elements matched for font-size check').toBeGreaterThan(0)
  return Math.min(...sizes)
}

export async function expectMinFontSize(target: Locator, minPx: number, label = 'text') {
  expect(await minFontSize(target), `${label} font-size`).toBeGreaterThanOrEqual(minPx)
}
