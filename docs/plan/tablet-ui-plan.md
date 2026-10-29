# Tablet-first UI Refactor Plan

- **App:** cafe-pos-system (Brew & Bean POS)
- **Type:** Full stack web app (existing). **Frontend-only change.** `backend/openapi/v1.json` does not change.
- **App folder:** `C:\Users\merin\AI\Agentic-fullstack-web-dev\cafe-pos-system\` (work in `frontend/` and `e2e/`)
- **Author / date:** research-planner, 2026-10-29
- **Builds on:** `docs/plan/cafe-pos-inventory-plan.md` §1 (touch targets 44–48px, grid + persistent cart, bottom sheet on phones, `clamp()` and container queries). This plan keeps all of that. Where it goes further, it says so (§2.1 raises the default target size; §2.4 adds a 960px landscape breakpoint).

**User decisions this plan is built on**
- Landscape tablet is the primary device (held sideways at the till). Portrait tablet and phone must work, but are secondary.
- Every screen is redesigned. The POS selling flow goes first.
- Main design size is 10–11" landscape (about 1180×820 CSS px). It must scale up to 12–13" (1366×1024) and down to 8" (1133×744) and phones (390×844).

---

## 0. Summary (read this if nothing else)

1. **Use the whole screen.** The app shell is always `100dvh × 100vw`, with no page-level max-width and no outer margins. Content areas scroll inside the shell; the page body never scrolls. The global top bar goes away on tablets: a **left navigation rail** (88px) carries the logo, nav, account and log out. Phones get a **bottom tab bar**.
2. **Bigger everything on tablets.** Body text is 18px, button and tile labels 20px, totals 30–56px. Buttons are 56px by default, primary actions 64px, keypad keys 80px. No control is ever under 48px. The 48px floor is the same as Material and above Apple's 44pt; WCAG's legal floor is 24px.
3. **POS keeps the industry layout.** In landscape the menu grid sits on the left and a persistent order panel (380–420px) on the right, as in Square. In portrait the order panel docks at the bottom. On phones it is a bottom bar that opens a sheet (as today).
4. **No on-screen keyboard for money.** Cash payment and the PIN pad share an on-screen `Keypad`. On touch devices the cash input uses `inputmode="none"`, so the OS keyboard never covers the dialog. A hardware keyboard still works.
5. **Styling: keep plain CSS and restructure it.** Split `styles.css` into **cascade-layered** files: tokens, base, layout, components, per-feature CSS. Class names stay global, so the ~20 E2E selectors that use them keep working. **Do not adopt Tailwind** (reasons in §4).
6. **Ten milestones** (M1–M10), each sized for one frontend agent and each ending green plus a commit. QA adds Playwright projects at 1180×820, 1366×1024, 820×1180 and 390×844, axe with WCAG 2.2 tags, and screenshot checks.

---

## 1. Research findings: current tablet POS practice

### 1.1 How the big tablet POS apps lay out the selling screen
| Product | Layout finding | Source |
|---|---|---|
| **Square POS (iPad)** | The item grid is the default checkout screen, but **only in landscape**; portrait shows an item list. The cart is a **right-hand column**. Sellers complained when a redesign narrowed it and shrank its text, so the cart column width is worth protecting. Tabs and categories sit above the grid. | Square Seller Community ([item grid only horizontal](https://community.squareup.com/t5/Square-Point-of-Sale/Add-Item-Categories-to-Item-Grid-on-Square-POS-iPad-with-Stand/m-p/672115), [cart column complaint](https://community.squareup.com/t5/Archived-Discussions-Read-Only/Updated-iPad-checkout-screen-is-unreadable-and-unnecessary/m-p/376170)). These are user and moderator posts, not official docs. |
| **Toast POS** (2026 docs) | Quick Order: the **check panel sits on the left**. A search bar is above the menu items, with drill-down Menu → Group → Item. Send / Pay / Print sit at the **bottom of the check**. Required modifiers appear at the bottom of the screen. On the Toast Go handheld, the check stacks above the menu and Pay stays visible at all times. The All Checks screen shows a permanent check-details pane (master-detail). | [Toast: Manage Orders](https://support.toasttab.com/article/New-POS-Experience-Ordering-Screens) (updated Mar 2026), [Toast release note](https://updates.toasttab.com/announcements/ann_jVtuHPHD3WcpD) |
| **Shopify POS** | "Smart grid" of fixed-size tiles. Tile size is set by the system, not the merchant. There are separate tablet and phone layouts, light and dark preview, and per-location layouts. Interactions are tap and long-press only, with no swipe gestures. | [Shopify smart grid help](https://help.shopify.com/en/manual/sell-in-person/getting-started/smart-grid), [POS UI tile docs](https://shopify.dev/docs/api/pos-ui-extensions/latest/web-components/actions/tile) |
| **Lightspeed Restaurant** | The Register screen opens on login. "+ New order" sits top-right on iPad. Menu items show photos, prices and modifiers. | [Lightspeed K-Series support](https://k-series-support.lightspeedhq.com/hc/articles/43162671781659), [App Store listing](https://apps.apple.com/app/id1486190847). Marketing and support copy only; no layout diagrams were found. **Weak source.** |

**Takeaways for us**
- Keep the menu grid and a **persistent** cart in landscape (Square, and Toast mirrored).
- We keep the cart on the **right**, as today. That matches Square, our existing tests, and the existing plan. Toast puts it on the left, so either side is defensible.
- The pay/charge button lives **at the bottom of the cart, always visible**.
- Tiles are fixed-size and big. Interactions are tap only (long-press is optional later). **No swipe-only actions.**
- In portrait, don't squeeze a side cart; stack it instead, as Toast Go does and as Square's portrait change suggests.

### 1.2 Touch target sizes
- **WCAG 2.2 SC 2.5.8 (AA): 24×24 CSS px minimum**, or enough spacing that a 24px circle around each target overlaps nothing. Exceptions: inline text links, controls styled by the browser, equivalent controls, essential cases. ([W3C Understanding 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html))
- **Apple HIG: 44×44 pt minimum**, with about 8pt between targets. Larger is better. (Secondary sources: [Deque iOS rule](https://dequeuniversity.com/rules/attest-ios/1.0/touch-target-size), [frontendchecklist](https://frontendchecklist.io/rules/accessibility/touch-targets). I could not load Apple's own page; the figure is long-standing and consistent across sources.)
- **Material 3: 48×48 dp minimum** interactive size (`LocalMinimumInteractiveComponentSize = 48.dp`). ([Android Compose a11y defaults](https://developer.android.com/develop/ui/compose/accessibility/api-defaults), [minimumInteractiveComponentSize](https://developer.android.com/reference/kotlin/androidx/compose/material3/minimumInteractiveComponentSize.modifier))
- **What this means on an iPad:** about 132 CSS px per inch (264 ppi ÷ DPR 2), so 48px ≈ 9.2 mm, 56px ≈ 10.8 mm, 64px ≈ 12.3 mm. One kiosk designer's notes suggest about 20 mm for kiosk buttons ([Ying Design, kiosk notes](https://yingdesign.medium.com/kiosk-user-interface-ui-design-notes-for-ya-4b58a03240bb)). That is a single source, but it supports going **above** 48px for fast, repeated counter work.

### 1.3 Type scale for an arm's-length counter screen
- No authoritative standard was found for POS text size at arm's length. **Flagged as a judgment call.**
- Inputs used for the judgment:
  - A touchscreen vendor suggests a 12–14pt minimum and 16–18pt preferred, and puts standing interaction at about 24–36" viewing distance ([Faytech](https://www.faytech.us/touchscreen-monitor/capacitive/capacitive-touch-screen-ergonomics-and-user-comfort/)). Unsourced vendor blog.
  - Kiosk notes used a 4 mm minimum for button text, which is about 21 CSS px on an iPad ([Ying Design](https://yingdesign.medium.com/kiosk-user-interface-ui-design-notes-for-ya-4b58a03240bb)).
  - Civic ballot guidance gives 14–16pt as a base ([Center for Civic Design](https://civicdesign.org/wp-content/uploads/2023/11/Whitepaper-TextSize-2016-11-2.pdf)).
- **Reasoning:** a phone is read at about 30–35 cm and a counter tablet at about 50–70 cm, so text needs to be roughly 1.15–1.25× phone size. The result is **body 18px, labels 20px, money 24px and up** on tablets, and body 16px on phones. Validate with a 5-minute test on the real tablet at the till (open question Q6).

### 1.4 Navigation pattern by size
- Material guidance:
  - Compact (<600dp): **bottom navigation bar**.
  - Medium (600–839dp) and expanded (≥840dp): **navigation rail**. The Material 3 Expressive update recommends the rail for medium windows and larger.
  - Android's adaptive scaffold picks the bottom bar when **width or height** is compact, which covers a phone in landscape.
  - Sources: [Android: build adaptive navigation](https://developer.android.com/develop/ui/compose/layouts/adaptive/build-adaptive-navigation), [9to5Google on M3 Expressive nav](https://9to5google.com/2025/05/14/material-3-expressive-navigation/).
- **Our use:** rail in tablet portrait and landscape (same nav in both orientations, so rotating doesn't swap the navigation). Bottom tabs on phones and on any screen under 500px tall.

### 1.5 Viewport units, safe areas, keyboard, zoom
- **`dvh` / `svh` / `lvh`:** supported in Safari 15.4+, Chrome 108+, Firefox 101+. Declare `100vh` first as a fallback. `dvh` tracks the browser chrome showing and hiding. ([OpenReplay](https://blog.openreplay.com/fix-100vh-mobile-viewport/), [intro-web-tech](https://intro-web-tech.pages.dev/css/tutorials/12-mobile/03_mobile-viewport.html))
- **Safe areas:** `env(safe-area-inset-*)` only returns non-zero values with `viewport-fit=cover` in the viewport meta. We already use `env(safe-area-inset-bottom)` but the meta lacks `viewport-fit=cover`, so it is currently always 0.
- **On-screen keyboard:**
  - Android Chrome honours `interactive-widget=resizes-content`: the keyboard shrinks the layout viewport, so `dvh` layouts move up above the keyboard.
  - **iOS ignores it.** The keyboard overlays the page, and a WebKit bug was still open in 2025. Workaround: `visualViewport` resize events plus scrolling the focused input into view. ([Chromium intent to ship](https://groups.google.com/a/chromium.org/g/blink-dev/c/ge7xTu-VhJ0), [WebKit bug 259770](https://bugs.webkit.org/show_bug.cgi?id=259770))
  - **Design rule: don't make cashiers type with the OS keyboard on the hot path.** Use on-screen keypads.
- **iOS input zoom:** Safari zooms the page when a focused input's rendered font size is under 16px. Fix it by keeping every input at **16px or more**. Do **not** use `user-scalable=no` or `maximum-scale=1`: they break pinch zoom (WCAG 1.4.4), and iOS 10+ ignores `maximum-scale` anyway. ([CSS-Tricks / Josh Comeau](https://css-tricks.com/?p=339455), [Rick Strahl](https://weblog.west-wind.com/posts/3805118.aspx))
- **Double-tap zoom and tap delay:**
  - `width=device-width` removes the 300ms tap delay (iOS 9.3+, Chrome 32+). ([Chrome: 300ms tap delay gone away](https://developer.chrome.com/blog/300ms-tap-delay-gone-away))
  - Add `touch-action: manipulation` on buttons, tiles and keypads so a fast double tap on "+" counts as two taps, not a zoom.
  - The Chrome article (2013) said Safari lacked `touch-action`. **Current Safari supports `manipulation`, but verify on a real iPad (Q7).**
- **PWA display:**
  - iOS supports only `display: standalone`. `fullscreen` falls back to standalone and `display_override` is ignored. Android and desktop Chrome support `fullscreen`.
  - On iOS, detect standalone mode with `navigator.standalone`; elsewhere use `@media (display-mode: standalone)`.
  - Sources: [firt.dev iOS PWA notes](https://firt.dev/notes/pwa-ios/), [Frontend Masters display modes](https://frontendmasters.com/courses/pwas-v2/display-modes), [Chrome display_override](https://developer.chrome.com/docs/capabilities/display-override).
- **Orientation lock:** `screen.orientation.lock()` does **not** work on iOS Safari, and on Android it needs fullscreen. Don't depend on it. Use manifest `orientation: "any"`, and on the tablet itself use iPadOS Guided Access or Android screen pinning. ([caniuse](https://caniuse.com/wf-screen-orientation-lock))
- **Container queries** are **Baseline Widely Available** since Aug 2025 (Safari 16+, Chrome 105+, Firefox 110+). Safe to use for components. ([web.dev Baseline digest Aug 2025](https://web.dev/blog/baseline-digest-aug-2025), [web-features explorer](https://web-platform-dx.github.io/web-features-explorer/features/container-queries))

### 1.6 Target viewports (CSS px, landscape W×H)
| Device class | Examples | Landscape | Portrait |
|---|---|---|---|
| 8" | iPad mini (A17 Pro) | 1133×744 | 744×1133 |
| **10–11" (primary)** | iPad 10th gen / A16, iPad Air 11" | **1180×820** | 820×1180 |
| 11" Pro | iPad Pro 11" M4 | 1210×834 | 834×1210 |
| 11" Android | Galaxy Tab S9 / A9+ | ~1280×800 (**unverified**; depends on display scaling) | ~800×1280 |
| 12.9–13" | iPad Air 13", iPad Pro 12.9"/13" | 1366×1024 / 1376×1032 | 1024×1366 |
| Phone | iPhone 14/15/16 class | 844×390 | **390×844** |

Source: [screensizechecker iPad viewport chart](https://screensizechecker.com/devices/ipad-viewport-sizes) (values self-labelled "unverified reference"; iPad figures match [yesviz](https://yesviz.com/devices/ipad-air-11-2024/)). **QA should read `window.innerWidth/innerHeight` on the real counter tablet once (Q1).**

---

## 2. Design system

### 2.1 Tokens (CSS custom properties, in `@layer tokens`)
**Rules**
- All sizes are stepped by size class. On `:root`, values are redefined in media queries. Use no `vw`-only fluid type, because it would break user zoom (WCAG 1.4.4).
- Feature CSS uses **semantic tokens only**. Raw hex values are allowed only in `tokens.css`.

**Type** (`--fs-*`; line-height 1.3 for UI, 1.5 for prose; `font-variant-numeric: tabular-nums` on all money and quantities)

| Token | Use | Compact (phone) | Medium / Expanded / Large (tablet) |
|---|---|---|---|
| `--fs-xs` | meta only (timestamps, VAT note). Never on a tappable label | 13px | 14px |
| `--fs-sm` | secondary text, badges | 15px | 16px |
| `--fs-md` | body, **all inputs** (≥16 means no iOS zoom) | 16px | 18px |
| `--fs-lg` | buttons, tile names, list titles | 18px | 20px |
| `--fs-xl` | h2, cart line price, prices on tiles | 20px | 24px |
| `--fs-2xl` | h1, cart total | 24px | 30px |
| `--fs-3xl` | payment amounts | 32px | 40px |
| `--fs-4xl` | "Change due", PIN-pad result | 44px | 56px |

Weights: 400 body, 600 labels and tile names, 700 totals.

**Touch**

| Token | Value | Use |
|---|---|---|
| `--tap-min` | 48px | absolute floor for any control (icon buttons, steppers, chips, nav items) |
| `--tap` | 56px on tablet, 48px on phone | default button and input height (replaces today's `--tap: 48px`) |
| `--tap-lg` | 64px | Charge, Complete payment, Sign in, Add to order, primary form submit |
| `--tap-xl` | 80px on tablet, 64px on phone | keypad keys (cash, PIN) |
| `--tile-min-w` / `--tile-min-h` | 160px / 120px (tablet), 140px / 104px (phone) | POS item tiles, time-clock staff tiles |
| `--gap-touch` | 8px minimum, 12px in grids | gap between adjacent targets |

**Spacing:** a 4px scale. `--sp-1` 4, `--sp-2` 8, `--sp-3` 12, `--sp-4` 16, `--sp-5` 24, `--sp-6` 32, `--sp-7` 48.
- `--pad-page`: 12px on phone, 16px on medium, 20px on expanded and large.
- This is the only outer padding. There is **no `max-width` on page containers**, except prose and forms (`--form-max: 760px`, see §3.9).

**Radii:** `--r-sm` 8, `--r-md` 12 (= current `--radius`, kept as an alias), `--r-lg` 16, `--r-xl` 24 (sheets), `--r-pill` 999.
**Elevation:** `--shadow-1` (cards), `--shadow-sheet` (sheets and dialogs), `--scrim: rgb(0 0 0 / .45)`.
**Motion:** `--dur-fast` 100ms (press feedback), `--dur` 200ms (sheets). Both drop to 0ms under `prefers-reduced-motion`.

**Colour.** Keep every existing variable name, because `useBranding.ts` sets `--brand`, `--brand-2`, `--on-brand`, `--brand-bg`, `--brand-secondary`, `--brand-tertiary` and `--brand-highlight` at runtime. Add semantic aliases on top.

| Semantic token | Maps to | Notes |
|---|---|---|
| `--bg`, `--surface`, `--ink`, `--muted` | existing | `--muted` #5e4d43 on `--bg` #f7f1e8 ≈ 7:1 (my calculation) |
| `--line` | existing #d9cbbb | **decorative dividers only.** About 1.5:1 on white, so it fails 1.4.11 as an input border |
| `--line-strong` **(new)** | #8c7a6b | input, select and checkbox borders, card outlines that signal interactivity. About 4.1:1 on white (my calculation) |
| `--nav-bg` / `--nav-fg` | `var(--brand-bg)` / `var(--on-brand)` | rail and bottom tabs. `--brand-bg` may be a **gradient**, so use it only with `background:`, never `background-color` or `border-color` |
| `--nav-active-bg` | `var(--brand-highlight)`, falling back to `rgb(255 255 255 / .18)` | active rail item. Mirror today's topbar `.active` logic |
| `--selected-bg` / `--selected-fg` | `var(--brand)` / `var(--on-brand)` | selected category chip, selected modifier, primary button |
| `--accent` | existing #b4561f (≈ 4.9:1 on white) | links, secondary emphasis |
| `--ok/--ok-bg`, `--warn/--warn-bg`, `--bad/--bad-bg` | existing | pairs ≈ 5.6:1, 5.2:1 and 6.3:1 (my calculation). Never rely on colour alone: always add a text label or icon |
| `--focus-ring` **(new)** | `0 0 0 2px var(--surface), 0 0 0 5px var(--ink)` | double ring, visible on any brand background |

**Focus:** `:focus-visible { outline: none; box-shadow: var(--focus-ring) }`. Inside the rail, use the inverse: `0 0 0 2px var(--nav-fg)`. **WCAG 2.4.11 Focus Not Obscured:** sticky footers and bars set `scroll-padding-bottom` on their scroll container, so a focused field is never hidden under them.

**Dark mode decision: not in this refactor.**
- Build the tokens so a theme is a pure token swap (`[data-theme="dark"]` on `<html>`), but ship light only.
- Reasons:
  1. No validated evidence for dark at bright counters (the earlier plan §1 flagged this too).
  2. Owner-chosen brand colours are tuned for light surfaces, and `onBrand()` would need a second contrast pass.
  3. It doubles the visual QA.
- It is offered as optional milestone M10 (Q3).

### 2.2 Size classes and breakpoints
Native `@custom-media` isn't available, so the literal values are written in CSS with a comment. They are mirrored in `src/lib/breakpoints.ts` for the few places JS needs them.

| Class | Media query | Devices | Shell | POS |
|---|---|---|---|---|
| **compact** | `(max-width: 599px), (max-height: 499px)` | phones in either orientation | top app bar + bottom tabs | grid with 2–3 columns, cart = bottom bar + sheet |
| **medium** | `(min-width: 600px) and (max-width: 959px) and (min-height: 500px)` | tablet portrait (744–834 wide) | 80px rail | grid on top, **cart docked at bottom** (about 40% height) |
| **expanded** | `(min-width: 960px) and (min-height: 500px)` | **tablet landscape 1133–1280** (primary), small laptops | 88px rail | grid on left, **cart column 380px** on right |
| **large** | `(min-width: 1300px) and (min-height: 500px)` | 12.9–13" landscape (1366/1376), desktop | 88px rail | cart column 420px, 5–6 grid columns |

- **Change from the old plan:** the old anchors were 768/1024/1440, and the CSS today uses 768/1100. 960 is chosen so every 8–13" tablet in landscape (≥1133) lands in *expanded* and every portrait tablet (744–834) in *medium*, with margin on both sides.
- The existing `responsive.spec.ts` "768 = wide, cart docked" expectation **still holds**: at 768 the cart is docked (bottom), so the `complementary` region is visible and the cart bar is hidden.
- **Container queries** are used **inside** components whose layout depends on their own width, not the viewport:
  - `.pos-menu` sets tile columns and photo vs text-only tiles.
  - `.cart` switches between compact and roomy line layout.
  - List and row containers switch between a table-like row and a stacked card.
  - `.sheet` sets a 1- or 2-column form grid.
- Use `container-type: inline-size` on these wrappers.

### 2.3 Global touch and kiosk rules (`@layer base`)
- `index.html` viewport: `width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content`. **Never** `user-scalable=no` or `maximum-scale`.
- `html, body { height: 100%; overscroll-behavior: none }`. This stops pull-to-refresh and rubber-band in kiosk use; verify on iPad (Q7).
- `button, [role=tab], a.btn, .tile, .key { touch-action: manipulation; -webkit-tap-highlight-color: transparent; user-select: none }`.
- Pressed feedback on `:active`: darken and `scale(.97)`, within `--dur-fast`.
- Hover styles only inside `@media (hover: hover) and (pointer: fine)`, to avoid "sticky hover" on touch.
- `img` in tiles: `-webkit-touch-callout: none; draggable="false"` (no long-press save-image menu).
- Every `input, select, textarea`: `font-size: max(16px, var(--fs-md)); min-height: var(--tap)`.
- Text in receipts, order details and forms stays selectable. Disable selection only on controls.

### 2.4 Layout shell (`AppShell`)
One DOM for all sizes, re-laid out by CSS. There are **no duplicate nav trees**: duplicates would break `getByRole('link', { name: 'Orders' })` in jsdom, where all CSS is off (`vitest css: false`).

```
<div class="shell">                      grid; height: 100vh; height: 100dvh
  <nav class="rail" aria-label="Main">   expanded/medium: left column | compact: fixed bottom bar
    brand logo (rail only)
    primary links: POS, Orders, Time clock
    manage links (role-gated): Menu banner, Catalog, Inventory, Staff, Locations, Settings
    [More] button (compact only; opens a sheet with the manage links + account)
    footer: [Account] button (opens a popover: name, role, LocationSwitcher, Full screen), [Log out] button
  </nav>
  <header class="appbar">                compact only: brand, page title, location chip, Log out icon button
  <main class="main">                    the only scroll container for normal pages; POS manages its own panes
</div>
```

- **expanded/large:**
  - `grid-template-columns: calc(88px + env(safe-area-inset-left)) 1fr`.
  - Rail items are 72px tall: 28px icon with a 14px label under it (Material rail pattern).
  - The manage group sits under a divider. Its middle section scrolls (`overflow-y: auto`) if the window is short. An owner sees 9 links plus Account and Log out, which needs about 760px, so on an 8" tablet in landscape (744px tall) the middle section scrolls.
- **medium:** 80px rail with the same items. Rotating keeps the same navigation.
- **compact:**
  - Rail becomes a bottom bar 64px tall plus `env(safe-area-inset-bottom)`: POS, Orders, Time clock, More.
  - Manage links get `display: none` in the bar. They are rendered in the More sheet **only while it is open**, so there is never more than one copy in the DOM at rest.
  - The 56px `appbar` appears on top.
  - **Hide the bottom bar while a text input is focused** (`:has(input:focus, textarea:focus)` on `.shell`), so the iOS keyboard plus a fixed bar don't stack.
- **"Log out" stays a directly visible button at every size** (rail footer, or app bar on phones), with the accessible name "Log out" unchanged. `e2e/support/ui.ts` waits for it.
- The **Full screen** toggle moves from TimeClockPage to the Account popover. Hide it when `display-mode` is standalone or fullscreen, or when the Fullscreen API is missing (iPhone Safari).
- **Icons:** today's emoji (🛒, 🗑, ✕) render inconsistently. Add **`lucide-react`** (tree-shakable SVG components; last seen around 0.548, **check `npm view lucide-react version` and React 19 peer support before installing**). The alternative is a 15-icon inline-SVG file with no dependency. Icons are always `aria-hidden` next to a visible or `aria-label` name.
- **Sheets and dialogs** (`components/Modal.tsx`) gain a `variant` prop: `'dialog' | 'side' | 'full'`. The default is responsive:
  - compact: bottom sheet (today), or full-screen for `full`.
  - medium: centred dialog, or a side sheet 70% wide.
  - expanded/large: centred dialog (payment, PIN, modifiers) or a **right side sheet** 560–720px wide and full height (admin edit forms).
  - The header is sticky. The footer is sticky with primary actions at `--tap-lg`.
  - `role="dialog"`, `aria-label`, Escape to close, focus return: **all unchanged**, so existing dialog queries keep working.

### 2.5 Branding still applies
- Runtime branding writes inline styles on `<html>`, which beat any `@layer` rule. All new tokens **reference** the brand variables and never copy their values.
- Affected surfaces:
  - Rail and bottom bar background: `--brand-bg`, which can be a gradient.
  - Primary buttons, selected chips and selected modifiers: `--brand` with `--on-brand`.
  - Login brand panel: `--login-bg`.
  - Active nav: `--brand-highlight`.
- QA adds one E2E check with a light brand colour (for example #f5d76e) to confirm `--on-brand` keeps rail and button text readable (axe `color-contrast`). See M2.
- The PWA manifest `theme_color` is static (#3b2417). A brand-coloured manifest would need a dynamic manifest endpoint, which is out of scope (Q5).

---

## 3. Per-screen layouts (tablet landscape first)

**Shared conventions**
- **Page header row:** title (`--fs-2xl`) on the left, actions on the right as 56px buttons. On compact the actions wrap below the title, or collapse into a "⋯" menu when there are more than 2.
- **Lists:**
  - The container query on the list wrapper is the switch. At **≥720px container width** a row is a single-line grid with columns and inline 48–56px action buttons. Below **720px** it is a stacked card: title line, meta line, then a full-width actions row.
  - The whole row is never a tap target for destructive actions.
- **Real `<table>`** (TimeRecords; MenuBanner has one):
  - Keep the table at ≥600px container width.
  - Below that, render a `<ul>` of cards via a `useMediaQuery('(min-width: 600px)')` hook, because CSS-only `display:block` on tables strips table semantics in some screen readers.
  - **The hook must default to "wide" when `matchMedia` is absent (jsdom)**, so existing tests see the table.
- Empty, loading and error states fill the pane, centred, with a 56px retry button.

### 3.1 POS: menu + cart (milestone M3)
**Expanded, 1180×820** (rail 88 | menu about 712 | cart 380)
```
+------+----------------------------------------------------+---------------------------+
| LOGO | [All] [Coffee] [Tea] [Pastry] [Cold] [...] >  [🔍 Search] | Current order      [Clear] |
|      +----------------------------------------------------+---------------------------+
| POS  | +----------+ +----------+ +----------+ +----------+ | Latte              ₱300.00|
|------| |  photo   | |          | |          | |          | | Large · Oat milk          |
|Orders| |          | |  Mocha   | | Americano| |  Iced Tea| | [ − ]  2  [ + ]     [🗑]  |
|------| | Latte    | |          | |          | |          | |---------------------------|
|Clock | | from ₱120| |   ₱140   | |   ₱100   | |   ₱90    | | Butter Croissant    ₱95.00|
|------| +----------+ +----------+ +----------+ +----------+ | [ − ]  1  [ + ]     [🗑]  |
|Catalg| +----------+ +----------+ +----------+ +----------+ |                           |
|Invent| |   ...    | |   ...    | |   ...    | |   ...    | |      (lines scroll)       |
| ...  | +----------+ +----------+ +----------+ +----------+ |---------------------------|
|------|                (grid scrolls; rail and cart fixed)  | Total (3 items)   ₱395.00 |
|[Acct]|                                                    | Prices include 12% VAT    |
|[Out] |                                                    | [    Charge ₱395.00    ]  |  64px, always visible
+------+----------------------------------------------------+---------------------------+
```
- **Grid:** `repeat(auto-fill, minmax(var(--tile-min-w), 1fr))` gives 4 columns at 1180, 5–6 at 1366, and 4 at 1133.
  - A tile is at least 120px tall. The name uses `--fs-lg` 600 and wraps to 2 lines (clamped). The price uses `--fs-xl`.
  - Photo tiles have a 4:3 image above the text. When the `.pos-menu` container is narrower than 520px, photos switch to a thumbnail on the left.
  - Unavailable tiles are disabled, at 50% opacity, with an "Unavailable" text label.
- **Category bar:** a horizontally scrolling **chip row**, 56px tall, keeping `role="tablist"` / `role="tab"` / `aria-selected`, as today.
  - The selected chip uses `--selected-bg`. Show a fade at the scroll edges as an overflow hint.
  - If a café has more than about 8 categories, consider a vertical category column instead (Q4).
- **Search:** a 56px input at the right of the tools row. It sits at the top of the screen, so the OS keyboard covers only the grid. Add a clear "×" button (48px).
- **Cart panel:**
  - Keep the accessible name "Current order".
  - A line is a 2-row grid: name + line total on row 1, variation/modifiers + stepper on row 2.
  - Stepper buttons are 48×48 (`--tap-min`). The quantity output is 48px wide, `--fs-lg`.
  - Remove is an icon button (48px) with `aria-label="Remove …"`, unchanged. Clear asks for confirmation when the cart has more than 3 lines (a small inline "Clear all? [Yes] [No]").
  - Footer is sticky: total `--fs-2xl` 700, VAT note `--fs-xs`, **Charge** at `--tap-lg`, full width.
- **Large (1366):** cart 420px, tiles 170–180px.
- **Medium (820×1180 portrait)**, rail 80:
```
+-----+-------------------------------------------+
|LOGO | [All][Coffee][Tea][Pastry] >   [🔍]        |
| POS | +--------+ +--------+ +--------+ +--------+|
|Ordrs| | Latte  | | Mocha  | | Amer.  | | Tea    ||   grid: about 58% height, scrolls
|Clock| +--------+ +--------+ +--------+ +--------+|
| ... | ...                                        |
|     +-------------------------------------------+
|     | Current order (3)                 [Clear] |   cart: about 42% height
|     | Latte · Large · Oat   [−] 2 [+]  ₱300 [🗑] |   lines scroll
|     | Croissant             [−] 1 [+]   ₱95 [🗑] |
|[Out]| Total ₱395.00      [   Charge ₱395.00   ] |   sticky, 64px
+-----+-------------------------------------------+
```
  `.pos { grid-template-rows: 1fr minmax(280px, 42%) }`. Cart lines go single-row when the container is ≥600px wide.
- **Compact (390×844):** today's pattern, kept.
  - 2-column grid (3 columns at ≥560px container width).
  - Sticky cart bar (`aria-label="Open current order"`, unchanged) sits **above** the bottom tabs: 64px, showing count + total + "View order ›".
  - The cart opens as a bottom sheet at about 85dvh.
  - Search is an icon button that expands to a full-width field over the chips row.
- **Modifier sheet:** a centred dialog of about 720×680 on expanded.
  - Option groups show as chip buttons of at least 56px, 3–4 per row, using the right ARIA semantics: radio group for single-select, checkbox group for multi.
  - Required groups are marked "Required" until satisfied.
  - The footer holds a quantity stepper (56px) and **"Add to order · ₱X"** (64px).
  - Compact: full-height sheet.
- "Choose a location" prompt (owner, All locations): location buttons as large tiles (min 120px tall, `--fs-xl`) in a responsive grid filling the pane.

### 3.2 Cash payment (M4)
**Expanded:** a centred dialog of about 900×700 (fits 1180×820 with margin). On 1133×744 the dialog is `max-height: calc(100dvh - 32px)`, with the keypad shrinking first (key height uses `clamp(56px, 9dvh, 80px)`).
```
+------------------------------------------------------------------------+
| Cash payment                                                     [ ✕ ] |
+-----------------------------------+------------------------------------+
| Total due (VAT incl.)             |  [  Exact  ] [ ₱400 ] [ ₱500 ] [₱1,000] |  quick tender, 64px
|            ₱395.00   (--fs-3xl)   |  +----------+----------+----------+ |
|                                   |  |    7     |    8     |    9     | |
| Cash tendered (₱)                 |  +----------+----------+----------+ |
| [                 500.00       ]  |  |    4     |    5     |    6     | |  keys --tap-xl (80px)
|                                   |  +----------+----------+----------+ |
| Change                            |  |    1     |    2     |    3     | |
|            ₱105.00   (--fs-3xl,ok)|  +----------+----------+----------+ |
|                                   |  |    .     |    0     |   ⌫      | |
| (error: Tendered amount is less…) |  +----------+----------+----------+ |
+-----------------------------------+------------------------------------+
| [                 Complete payment · ₱395.00   (64px)               ] |
+------------------------------------------------------------------------+
```
- **Shared `components/Keypad.tsx`:**
  - Buttons are named "0"–"9", ".", "Backspace", plus "Clear" on long-press or a separate key.
  - Both PIN and cash use it.
  - It is a `role="group"` with an `aria-label`.
- The **input stays a real `<input>` labelled "Cash tendered (₱)"**, so the existing tests (`getByLabelText(/Cash tendered/)`) and hardware-keyboard typing still work.
  - On `(pointer: coarse)`: set `inputMode="none"` and **don't autofocus**, so the OS keyboard never opens.
  - On fine pointers: autofocus, as today.
  - **Verify `inputmode="none"` on iPad Safari (Q7).**
- **Done state:** a full-dialog success view.
  - "Change due" at `--fs-4xl` in `--ok`, with the total and tendered amount below.
  - Two 64px buttons: **Print receipt** and **New order**. "New order" is the primary action, since the cashier's next action is usually the next customer (Q8).
  - Keep `role="status"` on the paid banner.
- **Compact:** full-screen sheet stacked as summary, input, quick-tender row (2×2), keypad (64px keys), sticky Complete button.

### 3.3 Orders + Receipt (M6)
**Expanded:** master-detail. The list is 440px and the detail fills the rest. This follows Toast's "All Checks" permanent details pane.
```
+------+---------------------------------------------------------------------------+
| rail | Orders                                       [Status ▾][From][To][Loc ▾] |  filter bar, 56px controls
|      +---------------------------+-----------------------------------------------+
|      | #00123  10:42  Completed  | Order #00123 · Completed · 10:42 · Ana        |
|      |            ₱395.00   ▸    |-----------------------------------------------|
|      |---------------------------| 2 × Latte (Large, Oat)                ₱300.00 |
|      | #00122  10:31  Canceled   | 1 × Butter Croissant                   ₱95.00 |
|      |            ₱140.00        |                                               |
|      |---------------------------| Total ₱395.00 · Cash · Tendered ₱500 · ₱105   |
|      | ...   (list scrolls)      |                                               |
|      |                           | [ View receipt ]  [ Cancel order ]   (56px)   |
|      | 248 orders                |                                               |
+------+---------------------------+-----------------------------------------------+
```
- Each list item is a single button (`aria-pressed`, or `aria-current` for the selection) at least 72px tall. The first order is selected by default on expanded.
- **Medium and compact:** list only. Tapping opens the detail as a side sheet (medium) or a full-screen sheet (compact) with a back button.
- The filter bar keeps the labels "Location", "Status", "From", "To" (`OrdersPage.test`, `owner.spec`). On compact it collapses into a "Filters (2)" button that opens a sheet.
- No API change: the detail uses the order already in the list payload. The current `<details>` expansion already renders lines from it.
- **Receipt page:** a centred 80mm receipt preview, with a sticky action bar holding **Print** and **Back to orders** (64px). Print CSS is untouched.

### 3.4 Login + location picker (M5)
**Expanded:** split screen. The brand panel is 45% (`--login-bg`, logo at 160px, brand name `--fs-3xl`). The form panel is 55%.
```
+--------------------------------------+-----------------------------------------------+
|                                      |   Sign in                                     |  form is top-aligned
|            [   LOGO   ]              |   Email     [_______________________] 56px    |  (not centred), so with
|                                      |   Password  [___________________][👁] 56px     |  the iPad keyboard open
|         Brew & Bean POS              |   [            Sign in (64px)            ]    |  (~400px) Sign in stays
|                                      |                                               |  visible
|    (brand gradient / colour)         |   -- after "Location required" --             |
|                                      |   Choose a location                           |
|                                      |   [  Main St  ] [  Uptown  ] [  Airport  ]    |  radio tiles, 88px tall
|                                      |   [            Sign in (64px)            ]    |
+--------------------------------------+-----------------------------------------------+
```
- **Location picker:** replace the native `<select>` with **large radio tiles** (`role="radiogroup"`, `aria-label="Location"`, each an `<input type="radio">` with a label). They are faster to hit than an iPad select popover.
  - **This changes selectors:** `e2e/support/ui.ts:13` and `e2e/tests/auth.spec.ts:46` use `getByLabel('Location').selectOption(...)`, and `LoginPage.test.tsx:115` uses `selectOptions`. They become `getByRole('radio', { name })`.
  - If the user prefers zero test churn, keep the select styled at 56px (Q9).
- Add a show/hide password toggle (48px).
- **Keyboard:** the form column is `overflow-y: auto`. On input focus, call `el.scrollIntoView({ block: 'center' })` after a `visualViewport` resize, to cover the iOS overlay.
- **Medium:** brand panel becomes a 200px header band, with the form below.
- **Compact:** a single column with the logo at 96px.

### 3.5 Time clock + PIN pad (M5)
**Expanded:** the staff grid fills the pane, 4–5 tiles per row. Each tile is at least 140px tall, with the name in `--fs-xl`, the role, and a status badge ("In since 9:02" / "Out" / "No PIN"). Clocked-in tiles also get a left border in `--ok` (not colour-only: the badge text is there).
- The header actions become 56px buttons: Time records, Set my PIN. Full screen moves to the Account popover.
- The `.clock-banner` confirmation becomes a large toast across the top of the pane (`--fs-xl`, 72px, `role="status"`), keeping class `clock-banner` for `timeclock.spec`.

**PIN dialog, expanded** (about 760×620):
```
+-----------------------------------------------------------+
| Clock in / out                                     [ ✕ ]  |
+---------------------------+-------------------------------+
|                           |  +-------+ +-------+ +-------+|
|      Ana Reyes            |  |   1   | |   2   | |   3   ||
|      (--fs-2xl)           |  +-------+ +-------+ +-------+|
|                           |  |   4   | |   5   | |   6   ||  keys 80×80+
|      ●  ●  ●  ○           |  +-------+ +-------+ +-------+|
|      (dots 20px)          |  |   7   | |   8   | |   9   ||
|                           |  +-------+ +-------+ +-------+|
|   Incorrect PIN (alert)   |  |   ⌫   | |   0   | |  OK   ||
|                           |  +-------+ +-------+ +-------+|
+---------------------------+-------------------------------+
```
- **Compact:** full-screen sheet with the name, dots and message stacked above a full-width keypad (64px keys).
- Keep the existing names: "Backspace" and the digits.
- Add physical keyboard digits and Enter support if they don't already exist.

**TimeRecords:**
- The preset tabs become a segmented control (56px).
- Totals become a card row on expanded.
- The table follows the table→cards rule (§3, shared conventions).

### 3.6 Catalog (M7)
- **Expanded:**
  - Segmented tabs: Items / Categories / Modifiers (56px).
  - Toolbar: search (56px), category filter, and **+ New item** (primary, 56px).
  - The list uses the row→card rule. A row shows thumbnail 56px, name, category, price range, active toggle, and Edit.
- Edit and create open a **right side sheet** (720px, full height). Its sections are Details, Variations, Modifiers, Photo, Recipe and Overrides, each a collapsible group with sticky Save / Cancel at 64px.
- Variation rows (`.var-row`) are a 2-column field grid in the sheet, with 48px remove buttons.
- Keep `.rows .row` (used by `a11y.spec`) and the dialog roles.

### 3.7 Inventory (M7)
- **Expanded:** stock list with a status chip (OK / Low / Out: colour + text) and the quantity in `--fs-xl`.
  - Each row has inline actions: Receive, Adjust, Count, Ledger (56px, icon + label at ≥720px container, icon-only with `aria-label` below).
  - The ledger opens in a side sheet.
- Keep these class names: `.stock-row`, `.stock-name strong`, `.stock-qty strong`, `.ledger-row`, `.filters`. They are used by `InventoryPage.test.tsx:41`, `inventory.spec`, `owner.spec` and `a11y.spec`.
- Stock forms (`StockForms.tsx`):
  - The quantity input gets a −/+ stepper (56px) and `inputmode="decimal"`.
  - The reason select is 56px.
  - The sheet footer is sticky.

### 3.8 Staff / Users (M8), the admin list + form example
**Expanded:**
```
+------+----------------------------------------------------------------------------------+
| rail | Staff                                [ Add existing ]  [ + New staff member ]   |
|      | [🔍 Search name or email      ] [Role ▾] [Status ▾]                              |
|      +----------------------------------------------------------------------------------+
|      | Ana Reyes          ana@brew.ph   [Manager · Main St] [Cashier · Uptown]  [Edit] [Deactivate] |
|      |----------------------------------------------------------------------------------|
|      | Ben Cruz           ben@brew.ph   [Cashier · Main St]                     [Edit] [Deactivate] |
|      |----------------------------------------------------------------------------------|
|      | ...                                                                (list scrolls) |
+------+------------------------------------------------------+---------------------------+
                                          edit → right side sheet (640px, full height):
                                          +-----------------------------------------+
                                          | Edit Ana Reyes                    [ ✕ ] |
                                          | Display name [____________________]     |
                                          | Email        [____________________]     |
                                          | [x] Active        [ ] Owner             |
                                          | ── Locations and roles ──               |
                                          | Main St   [Manager ▾]          [Remove] |
                                          | [Location ▾] [Role ▾]  [ Assign ]       |
                                          | ── Time clock PIN ──                    |
                                          | PIN [••••]  Confirm [••••]              |
                                          |-----------------------------------------|
                                          | [ Cancel ]          [ Save (64px) ]     |
                                          +-----------------------------------------+
```
- **Compact:** card per user (name, email, chips wrap, full-width [Edit] [Deactivate]). The sheet is full-screen.
- **Forms in sheets:**
  - Fields are 56px with labels above (no placeholder-as-label).
  - 2 columns when the sheet container is ≥560px.
  - Errors are inline under the field and in an `alert` summary at the top.
  - Required fields are marked with text, not just "*".
  - On mobile, `enterkeyhint="next"` / `"done"` on inputs.
- Keep `.row-item` (a11y spec), the dialog roles and the labels used by `UsersPage.test.tsx`.

### 3.9 Locations, Settings (M8)
- **Locations:** same list + side-sheet pattern as Staff. StaffList is inside the sheet.
- **Settings:** the one place a `max-width` is fine (`--form-max: 760px`, for readable forms).
  - On expanded, it becomes a 2-pane layout: section nav on the left (Business, Receipt, Branding, Tax…) as 56px items, the form on the right.
  - The sticky Save bar sits at the bottom of the pane.
  - Branding (BrandSettings, LogoMaker) colour pickers have swatches of at least 48px.

### 3.10 Menu banner, Design studio (M9)
- **Menu banner:** expanded shows the preview canvas on the left (fills the height) and a settings panel of 400px on the right. Medium and compact stack them, with settings in a bottom sheet (the current pattern).
- **Design studio** (Konva canvas, admin-only):
  - **Expanded:** top tool bar of 56px buttons, canvas in the centre, a right panel of 360px (Layers / Properties / Page tabs) that can collapse to give the canvas full width.
  - **Medium:** canvas full width, panels in the existing bottom sheet (`.studio-sheet`, `.studio-bottom`), buttons raised to 56px.
  - **Compact:** usable but not optimised. Show a dismissible "Best on a tablet or larger" note (Q10).
  - Konva handles are already `touch-action: none`. Enlarge transformer anchors to 24px with a hit area of at least 44px (Konva `anchorSize`, `hitStrokeWidth`).

---

## 4. Styling approach decision

**Recommendation: keep plain CSS. Restructure it into cascade layers, plus a design-token file and per-feature global CSS files. Do not adopt Tailwind, and do not adopt CSS Modules for this migration.**

| Option | Pros | Cons for this codebase |
|---|---|---|
| **Plain CSS + `@layer` + tokens + per-feature files (recommended)** | Zero new tooling. Class names unchanged, so the ~20 E2E class selectors (`.stock-row`, `.order-card`, `.clock-banner`, `.rows .row`, `.row-item`, `.filters`, `.ledger-row`, `.stock-qty strong`) and `InventoryPage.test` `querySelector('.stock-name strong')` keep working. Runtime branding via CSS variables just works. Layers end specificity fights between base, components and features. Vite splits CSS per lazy route chunk for free when feature CSS is imported by the feature. Layers are supported everywhere we target. | Naming discipline is on us: prefix classes per feature (`pos-`, `cart-`, `ord-`, `inv-`, `cat-`, `usr-`, `clk-`, `studio-`). There's no compile-time check for unused CSS; an optional Stylelint step can help. |
| CSS Modules | Scoped names; can't collide. | Hashed class names break every E2E class selector and the InventoryPage test. Vitest is set to `css: false`, so module imports return a proxy ([Vitest css config](https://vitest.dev/config/css)). That works, but tests can no longer target classes. It would be a full churn of tests *and* markup for little gain in a single-team app. |
| Tailwind v4 | Fast for new UIs; tokens as `@theme`; good responsive and container-query utilities. | **Every component's markup would be rewritten** (about 114 files), so the whole 2.4k lines are a rewrite, not a migration. v4 emits native cascade layers, and **unlayered legacy CSS beats layered utilities**, so a gradual migration needs the old CSS layered first anyway ([hypothesis/frontend-shared #2075](https://github.com/hypothesis/frontend-shared/issues/2075)). v4 needs Safari 16.4+ (fine for us, but it constrains old iPads). `@apply` in separate files needs `@reference` ([migration notes](https://beyond-it-blog.vercel.app/blogs/tailwind-v4-migration-guide)). Huge diffs make the review steps (`/code-review`) much harder. |

**Target structure** (frontend agent; M1)
```
src/styles/
  index.css        @layer reset, tokens, base, layout, components, features, utilities;  (FIRST CSS imported in main.tsx)
                   then @import of the files below, in this order
  reset.css        @layer reset { box-sizing, margins }
  tokens.css       @layer tokens { :root { ... }  @media size classes { :root { ... } } }
  base.css         @layer base { html/body, typography, form controls, focus-visible, touch rules (§2.3) }
  layout.css       @layer layout { .shell, .rail, .appbar, .main, .page, .pane, .split, sheets/backdrops }
  components.css   @layer components { .btn*, .field*, .tabs/.chip, .badge, .card, .alert, .modal/.sheet, .row-list/.row-item, .table, .keypad, .stepper, .empty }
  utilities.css    @layer utilities { .muted, .center, .grow, .sr-only, .no-print }
  print.css        UNLAYERED (receipt 80mm + studio export). Must stay unlayered: it uses !important, and inside
                   layers !important priority runs in reverse layer order
src/features/<feature>/<feature>.css   @layer features { ... }  imported at the top of the feature's page component
```
- **Wrap each file's content in `@layer name { }` yourself.** Don't rely on `@import url() layer(x)` being preserved through Vite's CSS pipeline; it's simpler and avoids that question.
- **Migration risk:** moving rules into layers changes precedence from specificity to layer order. For example, a high-specificity base rule now loses to any component rule.
  - Mitigation: M1 is **move-only plus tokens, with no visual change**, verified by a before/after screenshot diff at 4 viewports.
  - There are 9 `!important` in `styles.css` today. Review each one; most should go away under layers.
- `react-frontend-standards` skill says "use the project's existing approach". This decision keeps it, so no skill change is needed.

**Accessibility gate for every milestone (WCAG 2.2 AA)**
- 2.5.8 target size: our floor is 48px.
- 2.4.7 / 2.4.11 focus visible and not obscured.
- 1.4.3 / 1.4.11 contrast: text 4.5:1, UI component borders 3:1.
- 1.4.4 zoom: no zoom lock.
- 1.4.10 reflow at 320px.
- 4.1.2 names and roles unchanged.

---

## 5. Milestones (one frontend agent each; each ends with format, lint, typecheck, Vitest and the relevant Playwright projects green, plus one commit `-- frontend` / `-- e2e`)

QA work in each milestone is done by `qa-automation` after the frontend agent finishes. Playwright runs **one project at a time** on the shared DB (per CLAUDE.md lead rules).

**Playwright projects QA adds in M1** (`e2e/playwright.config.ts`)

| Project | Viewport | Options | Runs |
|---|---|---|---|
| `tablet-landscape` **(new default)** | 1180×820 | `hasTouch: true, isMobile: true` (Chromium) | **full suite** |
| `tablet-large` | 1366×1024 | `hasTouch, isMobile` | `@layout`, `@a11y`, `@visual` |
| `tablet-small` | 1133×744 | `hasTouch, isMobile` | `@layout` (rail scroll, payment fits) |
| `tablet-portrait` | 820×1180 | `hasTouch, isMobile` | `@layout`, `@a11y`, `@visual` |
| `phone` | 390×844 | `devices['iPhone 13']`-like, Chromium | `@layout`, `@a11y`, `@visual`, `@smoke` checkout |
| `desktop` | 1280×800 | today's settings | `@smoke` (regression for laptop managers) |
| `webkit-ipad` (optional, non-blocking) | 1180×820 | WebKit engine | `@smoke`. It is closer to Safari, but **not** real iPad Safari |

**Reusable QA helpers** (`e2e/support/layout.ts`)
- `expectNoHorizontalOverflow()`: already exists inline in `responsive.spec`; move it here.
- `expectFillsViewport()`: the shell's bounding box equals the viewport, with no body scroll on POS.
- `expectTouchTargets(min = 48)`: every visible `button, a, [role=tab], input, select` in `main` and `nav` is at least 48×48. Exclude inline text links.
- axe: add tags `'wcag22aa'` (enables axe's `target-size` rule) and `'best-practice'` (report-only).
- Visual: `toHaveScreenshot()` with `mask` on times, order numbers and dates, `maxDiffPixelRatio: 0.01`. **Baselines must be generated in the Linux CI image** (or the `mcr.microsoft.com/playwright` container locally), because Windows font rendering differs. Commit the baselines. **Flag:** decide who regenerates baselines when the design changes on purpose (Q11).

### M1 — Styles restructure + tokens (no visual change)
- **Frontend:**
  - Split `styles.css` into the §4 layered files and feature files, moving rules verbatim.
  - Add `tokens.css` with all §2.1 tokens, wiring existing values through them (`--radius` → `--r-md` alias, `--tap` stays 48 for now).
  - Add `--line-strong` and apply it to input borders: the only visible change, a contrast fix.
  - Fix the viewport meta (§2.3).
  - Delete `styles.css`.
- **QA:**
  - **Before** the frontend agent starts: capture baseline screenshots of login, POS, payment, orders, timeclock, inventory, catalog and staff at 390, 820, 1180 and 1366.
  - After: diff, then add the Playwright projects and `support/layout.ts` helpers.
  - Add the `wcag22aa` tag.
- **Tests:** none of the 93 Vitest tests should change. No E2E selector changes.

### M2 — App shell, primitives, PWA manifest
- **Frontend:**
  - `AppShell` with rail, bottom tabs, appbar and More sheet (§2.4).
  - Account popover (LocationSwitcher, Full screen).
  - Icons (lucide-react or inline SVG).
  - Raise the token values to the tablet sizes (`--tap` 56, type scale).
  - Primitives: `Button` sizes (sm 48, md 56, lg 64), `Field` 56px, `Keypad` (unwired), `Modal` `variant` (dialog / side / full), `useMediaQuery` (default wide), `.row-list` row→card container query, focus ring.
  - `public/manifest.webmanifest`:
    - `name`, `short_name`, `start_url: "/pos"`, `scope: "/"`.
    - `display: "standalone"` and `display_override: ["fullscreen", "standalone"]`.
    - `orientation: "any"`, `theme_color` / `background_color`.
    - 192, 512 and maskable icons, plus a 180px `apple-touch-icon`.
  - Add `<link rel="manifest">` and `meta name="mobile-web-app-capable"` to the HTML.
  - **No service worker here.** Offline remains Phase 4 of the main plan.
- **DevOps / backend check, not an API change:** confirm the Render static host and the e2e nginx serve `.webmanifest` as `application/manifest+json`. ASP.NET Core's default `FileExtensionContentTypeProvider` and recent nginx `mime.types` are believed to include it. **Unverified, so check the response header.**
- **Tests:**
  - Vitest: add `AppShell.test.tsx`:
    - role-gated links per role (Cashier / Manager / Admin / Owner);
    - "Log out" is present;
    - the More sheet renders manage links only when open, which needs a `matchMedia` mock in `src/test/setup.ts`.
  - `ProtectedRoute.roles.test.tsx` may need updates if it asserts the topbar markup. Link names are unchanged.
- **QA:**
  - `shell.spec.ts @layout`: rail visible on tablet projects, bottom tabs on phone; nav reachable; no overflow; fills viewport; touch targets; rail scrolls on `tablet-small` for the owner.
  - Branding contrast check with a light brand colour.
  - Manifest is served and parses.

### M3 — POS menu, cart, modifier sheet
- **Frontend:** §3.1 for all four size classes: tiles, chip tabs, search with clear, cart panel (expanded column, medium bottom dock, compact sheet), modifier sheet with chip options and stepper, location-pick tiles.
- **Tests:** `PosPage.test` and `cartStore.test` should pass unchanged. Names are kept: "Current order", "Open current order", "Charge …", "Increase/Decrease/Remove …", the tab roles. Add tests for modifier required-group validation and the confirm-clear step.
- **QA:**
  - Update `responsive.spec.ts`: switch to the new projects. "Wide" means cart docked (medium, expanded, large); the phone shows the bar.
  - Add `@visual` POS screenshots on landscape, portrait and phone.
  - axe on POS.
  - Check the Charge button is visible without scrolling at 1133×744 and 820×1180.

### M4 — Payment + done state + Receipt page
- **Frontend:** §3.2: two-column dialog, shared `Keypad` wired to the cash input (`inputMode="none"` and no autofocus on coarse pointers), quick tender, done view, Receipt page action bar.
- **Tests:** existing `PosPage.test` payment tests (`getByLabelText(/Cash tendered/)`) still pass. Add keypad tests (tap 5-0-0 gives 500 and the change shows; backspace; "." handling; Complete is disabled until valid).
- **QA:**
  - `checkout.spec` on `tablet-landscape` uses the keypad path (`getByRole('group', { name: 'Cash keypad' })`).
  - Keep one test that `fill`s the input (hardware-keyboard path).
  - Check the dialog fits within the viewport on all projects (already in `responsive.spec`).
  - `@visual` payment + done; axe on the payment dialog.

### M5 — Login + location picker, Time clock + PIN pad, Time records
- **Frontend:** §3.4, §3.5. Reuse `Keypad` in `PinModal`. Add the TimeRecords table→cards switch.
- **Tests:**
  - **Selector updates:** `LoginPage.test.tsx:115` (`selectOptions` becomes `click(getByRole('radio', { name: 'Uptown' }))`), but only if Q9 = tiles.
  - Add a TimeClock test for keypad entry if none exists.
- **QA:**
  - Update `e2e/support/ui.ts:13` and `auth.spec.ts:46` for radio tiles (if Q9 = tiles).
  - `timeclock.spec` keeps `.clock-banner`.
  - Add a login-keyboard check: on `tablet-landscape`, after focusing Password, the Sign in button is within the top 420px (a rough iPad keyboard proxy).
  - `@visual` login, location picker, time clock, PIN pad, time records (phone card view); axe on each.

### M6 — Orders master-detail
- **Frontend:** §3.3: list + detail pane on expanded, sheet on medium and compact, filter sheet on compact.
- **Tests:**
  - **Selector updates:**
    - `OrdersPage.test.tsx:45` ("View receipt" link) now needs a selected order. The first order is auto-selected on wide screens; set the `matchMedia` mock to wide.
    - `checkout.spec.ts:65-69` and `isolation.spec.ts:69-73` use `.order-card` + `hasText` and then a link inside the card. Keep the `order-card` class on list items. "View receipt" moves to the detail pane, so the steps become: click the card, then `getByRole('region', { name: /Order #/ }).getByRole('link', { name: 'View receipt' })`.
    - `isolation.spec.ts:69` (`toHaveCount(0)` on `.order-card` with text) still works if the list items keep the line summary text. **Keep a one-line item summary in each list item**, both for that test and for scanability.
- **QA:** `@visual` orders on landscape, portrait and phone; axe; cancel-order flow from the detail pane.

### M7 — Catalog + Inventory
- **Frontend:** §3.6, §3.7: segmented tabs, toolbars, row→card lists, side-sheet editors (ItemsTab, CategoriesTab, ModifiersTab, RecipeEditor, OverridesEditor, ItemPhotoSection, StockForms), steppers.
- **Tests:** `CatalogPage.test` and `InventoryPage.test` keep passing because class names (`.rows .row`, `.stock-*`) and dialog roles stay. Add a test for the stepper in StockForms.
- **QA:**
  - `inventory.spec` and `owner.spec` unchanged (class selectors kept). Run them on `tablet-landscape`.
  - Add a phone run of the receive-stock flow (`@layout`).
  - `@visual` and axe for catalog list, item sheet and inventory.

### M8 — Staff, Locations, Settings
- **Frontend:** §3.8, §3.9.
- **Tests:** `UsersPage.test` should pass. Check the dialog name queries if the sheet title changes; keep titles identical.
- **QA:** `@visual` and axe on staff list, staff sheet, locations and settings. The `owner.spec` Locations flow runs on `tablet-landscape`.

### M9 — Menu banner + Design studio
- **Frontend:** §3.10. Collapsible right panel on expanded, 56px controls, larger Konva anchors, phone notice.
- **Tests:** no existing component tests for these screens. Add a smoke render test for the panel collapse.
- **QA:** `@layout` smoke on `tablet-landscape` and `tablet-portrait` (open the studio, add a text element, the properties panel is reachable); axe on the page chrome (skip the canvas).

### M10 (optional) — Dark theme + polish
- **Frontend:** a `[data-theme="dark"]` token set, a toggle in the Account popover, and a contrast pass for branding on dark.
- **QA:** an axe run in both themes and dark `@visual` baselines.
- Only do this if Q3 = yes.

**Order and parallelism:** M1 → M2 → M3 → M4 must run in sequence. M5–M9 touch separate feature folders and can run in parallel worktrees **after M2**. Run E2E one project at a time.

---

## 6. Tech stack and deployment impact

| Item | Version / status | Change |
|---|---|---|
| React / React Router / TanStack Query / RHF / Zod / Zustand | 19.3 / 7.18 / 5.104 / 7.90 / 3.25 / 5.0 (per `package.json`) | none |
| Vite / Vitest / TS | 6.4 / 4.1 / 5.6 | none |
| CSS | plain CSS, cascade layers, container queries, `dvh`, `env()` | restructure only |
| Icons | `lucide-react` (latest; about 0.548 seen, **verify**) or inline SVG | new (optional) dependency |
| PWA | static `manifest.webmanifest` + icons in `frontend/public/` | new files; `vite-plugin-pwa` (about 1.x, **verify**) **not needed** until the offline phase |
| Playwright + `@axe-core/playwright` | already in `e2e/` | new projects, helpers, screenshot baselines |
| Backend / OpenAPI | — | **no change** |
| Render deploy | single service serving the SPA | none, apart from checking the `.webmanifest` MIME type (M2) |
| CI (GitHub Actions) | existing jobs | the E2E job runs projects in sequence (`--project=tablet-landscape`, then the `@layout|@a11y|@visual` grep on the other projects). Upload the Playwright HTML report and diff images as artifacts |

**On the counter tablet:**
- Add to Home Screen. This gives standalone mode on iPad and fullscreen-capable on Android.
- Then lock the device to the app with **Guided Access** (iPadOS) or **screen pinning / a kiosk browser** (Android).
- This is the reliable "kiosk" path. The web Fullscreen and Orientation APIs are unreliable on iOS (§1.5).

---

## 7. Constraints honoured
- **No API changes.** Every screen above uses existing endpoints and payloads:
  - Orders detail comes from the list payload.
  - The login location tiles come from the existing 400 `locations` extension.
  - The time clock uses the existing staff/punch endpoints.
- **API-change candidates:** none are required. Possible later asks, **not** in scope:
  - (a) a dynamic, brand-coloured manifest endpoint (Q5);
  - (b) a lightweight order-detail endpoint if the list payload ever stops including lines.
- **Accessible names, roles and labels are kept** wherever tests use them. Class selectors that tests rely on are kept (listed in §4).
- **Tests that need selector updates (complete list):**
  - `LoginPage.test.tsx:115`, `e2e/support/ui.ts:13`, `e2e/tests/auth.spec.ts:46`: only if location tiles are chosen (Q9).
  - `OrdersPage.test.tsx` (View receipt now in the detail pane, `matchMedia` mock), `e2e/tests/checkout.spec.ts:64-69`, `e2e/tests/isolation.spec.ts:67-73`.
  - `e2e/tests/responsive.spec.ts`: restructured around the new projects and size classes.
  - `ProtectedRoute.roles.test.tsx`: only if it asserts topbar structure. Check it in M2.
  - Everything else (PosPage, cartStore, Catalog, Inventory, Users, LocationSwitcher, ProtectedRoute, the format/orders hooks) should pass unchanged.

---

## 8. Open questions (short)
1. **Q1 Real device:** which tablet is at the till? A one-time `innerWidth × innerHeight` reading in its browser confirms the 1180×820 target.
2. **Q2 Browser or installed:** will staff run it from the browser or as a home-screen app with Guided Access / screen pinning? (Recommended: installed + locked.)
3. **Q3 Dark mode:** skip for now (recommended), or include M10?
4. **Q4 Categories:** about how many categories? Up to about 8 uses the top chip row; more could use a vertical category column.
5. **Q5 Manifest colours:** is a static brown `theme_color` OK, or should the installed app's colour follow branding? The latter would need a small backend endpoint.
6. **Q6 Text size check:** can someone try the M3 build at the counter and confirm 18px / 20px reads well at arm's length?
7. **Q7 iPad verification:** we need 10 minutes on a real iPad to confirm `touch-action: manipulation`, `inputmode="none"` on the cash input, and `overscroll-behavior` in standalone mode.
8. **Q8 After payment:** should "New order" be the big default, with "Print receipt" secondary? Or should it auto-print?
9. **Q9 Location picker at login:** big tiles (recommended; 3 test files change) or a styled 56px dropdown (no test churn)?
10. **Q10 Design studio on phones:** OK to mark it "best on tablet" and not optimise phone layouts?
11. **Q11 Screenshot baselines:** OK to commit Playwright visual baselines generated in CI (Linux), and regenerate them on purpose when the design changes?

## 9. Decisions (user, 2026-10-29)

- Orientation: tablet landscape first; portrait tablet and phone secondary. Device: mixed, design for 10-11" (1180x820) and scale.
- Scope: all screens, POS first, milestone order as in section 5.
- **Q3 Dark mode:** later (M10 stays optional, not scheduled).
- **Q4 Categories:** more than 8, so the POS uses a **vertical category column** beside the menu grid on expanded and medium sizes (keep
  `role="tablist"` / `role="tab"` / `aria-selected`, with vertical `aria-orientation`). On compact, keep a horizontally scrolling chip row.
- **Q8 After payment:** **auto-print.** On successful payment, open the print dialog for the receipt automatically (the browser cannot
  print silently); the done state behind it shows **New order** as the primary 64px button and **Print again** as secondary. Silent
  printing needs Chrome `--kiosk-printing` or a local print agent (QZ Tray, base plan Phase 4) and is out of scope here.
- **Q9 Location picker:** big tiles (update `LoginPage.test.tsx`, `e2e/support/ui.ts`, `auth.spec.ts` accordingly).
- Q1, Q2, Q5, Q6, Q7, Q10, Q11: not answered yet; use the plan's recommendations (static manifest colour, "best on tablet" note for
  the design studio on phones, CI-generated Linux screenshot baselines committed and regenerated on purpose). Q6/Q7 remain manual checks
  on real hardware after M3/M4.
