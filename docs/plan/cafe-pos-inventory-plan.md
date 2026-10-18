# Cafe POS + Inventory Management System — Research & Implementation Plan

Status: Draft for review. Produced by research-planner agent on 2026-10-16. **Revised 2026-10-16** to incorporate the project owner's answers on payments, multi-location, offline support, and receipts — see the new Decisions Log (Section 8). These four answers meaningfully expand scope beyond the original single-café, record-only-payment, online-only MVP; Section 6 (Phased Roadmap) replaces the original MVP/Later split accordingly.
Stack (decided): React (SPA) frontend, .NET 8 Web API + EF Core backend.

This document is the single source of truth other agents (frontend, backend, QA, docs) should build against. Where a claim could not be verified against current, credible sources, it is explicitly flagged as a judgment call rather than presented as fact.

---

## 1. Recommended Design Direction

### Visual / layout direction
- **POS screen**: category grid + large item tiles on the left/center, persistent cart/order sidebar on the right (or bottom on narrow screens). Tapping an item with modifiers (size, milk type, extras) opens a modifier sheet — don't show all options at once ("progressive disclosure"). This is the dominant pattern across tablet POS UIs.
  Sources: [OrderOut POS Tablet Guide](https://www.orderout.co/blog/pos-system-tablet/), [Restroworks Tablet POS](https://www.restroworks.com/blog/best-tablet-pos-system-for-restaurant/), [Medium UI/UX POS Case Study](https://medium.com/@aziemelasari/ui-ux-case-study-a-pos-restaurant-app-for-efficient-order-management-2044b5151926)
- **Target hardware**: counter tablets are typically 10.1"–12.1", so design for that viewport as the primary "desktop-ish" target, not just a generic laptop breakpoint. High-brightness/high-contrast matters for sunlit counters. iPads are a plausible counter device for this project — see the Safari/iOS caveats in Section 4.7 (offline) and Section 4.5 (printing) that follow from that.
- **Inventory dashboard**: no strong current sourcing found for inventory-dashboard-specific visual conventions — **flagged as unverified**. Recommend standard dashboard conventions as a judgment call: card/table hybrid, color-coded stock status (e.g. green/amber/red against low-stock threshold), sortable/filterable table for full ingredient list. Now also needs a location filter/switcher (see Section 4.1).
- **Dark mode for counter use**: no validated trend found either way — **flagged as unverified**, treat as a nice-to-have, not a requirement.

### Touch targets & accessibility
- Legal minimum (WCAG 2.2 SC 2.5.8, Level AA, enforceable under ADA/EAA since June 2025): **24×24 CSS px**. Source: [AllAccessible WCAG 2.5.8 Guide](https://www.allaccessible.org/blog/wcag-258-target-size-minimum-implementation-guide)
- Practical recommendation (cross-checked, Nielsen Norman Group ~38×38px, Apple HIG 44×44pt, Google Material 48×48dp): **build all POS action buttons to ~44–48px with ≥8px spacing**, well above the legal floor, given rapid repeated counter-staff use. Source: [Siteimprove summary of NN/g](https://www.siteimprove.com/blog/motor-impairments-and-mobile-ui-the-touch-target-problem/)

### Responsive strategy
- Don't hardcode device widths; break where the layout actually strains. Common anchors (cross-checked): **~360px (mobile), 768px (tablet), 1024px (desktop), 1440px (large desktop)**.
  Sources: [Material Design Responsive Layout Grid](https://m2.material.io/design/layout/responsive-layout-grid.html), [Webmark Breakpoints](https://webmarkhq.com/common-breakpoints-for-responsive-design/)
- Material's grid gives concrete starting values: 4 columns/16dp gutters at 360dp; 8 columns/24dp gutters at 600dp — usable as a starting point for the item-grid + cart-sidebar layout.
- Prefer relative units (`rem`, `%`, `clamp()`) and container queries over fixed breakpoints where practical (broadly supported now).
- Below ~768px (phone, e.g. manager checking stock on their phone), collapse the cart sidebar into a bottom sheet / separate view rather than cramming the three-pane layout.

### Offline / PWA (**revised** — decision changed from the original draft, see Decisions Log §8)
- **Settled: the POS MUST support offline order-taking and sync later.** This reverses the original recommendation to skip offline-first for MVP. Full architecture is in Section 4.7; summary here:
  - Local-first stack: Service Worker (app-shell caching, e.g. via **Workbox**) + **IndexedDB** (via a wrapper such as **Dexie.js**) holding a cached catalog, the in-progress cart, and an outbox queue of pending orders/actions.
  - Each queued offline action carries a client-generated **idempotency key (UUID)** so a retried sync can't double-create an order.
    Source: [LogRocket — offline-first frontend apps 2025](https://blog.logrocket.com/offline-first-frontend-apps-2025-indexeddb-sqlite/)
  - Prefer the Background Sync API to detect reconnection, but **iOS Safari does not support it** — since iPads are a plausible counter device here, also implement a `navigator.onLine`/online-event + periodic-retry fallback.
    Source: [VP0 — offline-first PWA](https://vp0.com/blogs/agritech-offline-first-pwa-react)
  - Inventory decrements made offline must sync as **deltas**, never absolute stock snapshots — syncing absolute values under naive last-writer-wins silently loses concurrent decrements from other offline terminals. Let stock go negative and flag for manual review rather than blocking or reversing a sale that already happened.
    Sources: [MongoDB community forum — conflict resolution case study](https://www.mongodb.com/community/forums/t/custom-conflict-resolution-advice/216037), [Cleverence — multi-channel offline warehouse sync](https://www.cleverence.com/articles/business-blogs/multi-channel-sync-offline-warehouse-9473/)
  - **Payment while offline is a product decision, not just an engineering one** (see Section 4.7 and Decisions Log): Stripe Terminal has an official offline mode in general, but **two research streams disagree on whether it applies to this project's specific integration path** (server-driven PaymentIntents + the `@stripe/terminal-js` web SDK) versus only to native-mobile-SDK Terminal integrations — see the flagged discrepancy in Section 4.7. Until that's resolved, treat cash-only-while-offline as the safe default, not merely the conservative one.

---

## 2. Frontend Stack Recommendation

| Concern | Recommendation | Why |
|---|---|---|
| Build tool | **Vite** | CRA was officially deprecated by the React team (Feb 2025) and breaks under React 19; Next.js would be overkill since this is a pure SPA against a separate .NET API with no SSR/SEO need. Sources: [Socket.dev](https://socket.dev/blog/create-react-app-officially-deprecated), [devclass.com](https://devclass.com/2025/02/18/react-team-formally-deprecates-create-react-app-following-perfect-storm-of-incompatibility/) |
| Routing | **React Router v7** | Largest ecosystem, lowest migration risk, the safer default for a business CRUD app. TanStack Router is a legitimate alternative if the team wants zero-config typed loaders, but not necessary here. Sources: [devtoolbox.blog](https://devtoolbox.blog/tanstack-router-vs-react-router-v7-2026/), [pkgpulse.com](https://www.pkgpulse.com/guides/react-router-v7-vs-tanstack-router-2026) |
| Server state (menu, orders, inventory levels) | **TanStack Query** | Still the standard for caching/refetch/invalidation against a REST API; keep it as the single source of truth for anything server-sourced. |
| Client-only state (in-progress cart, UI toggles, auth session) | **Zustand** | Has overtaken Redux for new projects — minimal boilerplate, hook-based. Redux remains valid for larger/more complex state but is "no longer the default" for a project this size. |
| Forms & validation | **React Hook Form + Zod** (via `@hookform/resolvers`) | Current standard combo; one schema drives both runtime validation and TypeScript types — good fit for menu-item CRUD and checkout forms. |
| Component organization | Feature-folder structure (group by domain: `menu/`, `orders/`, `inventory/`, `auth/`) over type-folder structure | Lower-confidence recommendation — this is established convention, not something with fresh authoritative sourcing. Flagged as such. |
| **Offline persistence** (new, driven by the offline decision) | **Dexie.js** (IndexedDB wrapper) + **Workbox** (Service Worker tooling) | Standard current pairing for local-first PWA data (Dexie) plus app-shell/background-sync tooling (Workbox); needed now that offline support is settled. See Section 4.7. |
| **Card payment UI** (new, driven by the payments decision) | **`@stripe/terminal-js`** (official Stripe web SDK for Terminal) | Handles reader discovery/connect/collect-payment-method/process-payment directly from the browser to the physical reader. See Section 4.4. Not independently re-verified via fresh web search this session — flagged in Section 10. |

Sources (cross-checked 3+): [dev.to/iamsaadmehmood](https://dev.to/iamsaadmehmood/state-management-in-2026-redux-vs-context-vs-tanstack-query-1b0b), [ncctcr.com](https://ncctcr.com/blog/react-state-management-2026), [adeptdev.io](https://www.adeptdev.io/blogs/react-state-management-in-2026-a-practical-guide), [stacknotice.com](https://stacknotice.com/blog/react-hook-form-zod-guide-2026)

**Pitfall to avoid** (explicitly called out by multiple sources): do not duplicate server data into Zustand/Redux — it causes cache desync. TanStack Query owns server data; Zustand owns ephemeral client/UI state only. (The new offline outbox queue in IndexedDB is a third category — durable pending-write state — and should not be modeled as either server cache or ephemeral UI state; see Section 4.7.)

---

## 3. Backend Stack Recommendation (.NET 8 + EF Core)

| Concern | Recommendation | Why |
|---|---|---|
| Minimal APIs vs Controllers | **Controllers (MVC)** | Community consensus favors controllers once endpoint count and shared cross-cutting policy (auth, validation, versioning) grow past simple microservice scale — which fits a POS with menu/order/inventory/user domains, now larger still with location/payment/sync domains. A hybrid (minimal for health checks/webhooks) is fine — the Stripe webhook endpoint (Section 5) is a good minimal-API candidate. Sources: [Controllers vs Minimal API in .NET 8](https://boldsign.com/blogs/controllers-vs-minimal-api-dotnet-8/), [Minimal API vs Controllers: Decision Framework](https://dapiq.com/insights/minimal-api-vs-controllers-decision-framework) |
| Repository pattern over EF Core | **Skip it — use DbContext/DbSet directly** | DbContext already is Unit of Work + Repository; wrapping it again hides EF Core features (AsNoTracking, projections) and is current (2025-2026) consensus for apps this size. Sources: [Stop Using Repository Pattern with EF Core in 2026](https://levelup.gitconnected.com/stop-using-repository-pattern-with-ef-core-in-2026-heres-why-8f22168aba3e), [thereformedprogrammer.net](https://www.thereformedprogrammer.net/is-the-repository-pattern-useful-with-entity-framework-core/) |
| DbContext lifetime | Standard scoped-per-request (ASP.NET Core DI default) | No contrary guidance found. |
| **Inventory stock-decrement concurrency** (core domain need) | **Conditional UPDATE inside a transaction** (e.g. `UPDATE ... SET qty = qty - @n WHERE qty >= @n`) as the primary mechanism to atomically prevent overselling, with **optimistic concurrency token (RowVersion)** as defense-in-depth. Multi-ingredient decrements (one order item consuming several ingredients) must be wrapped in a single transaction — all or nothing. Now also scoped by `LocationId` (Section 4.1/4.6) — the conditional check and transaction apply per-location, not globally. | Plain optimistic concurrency alone degrades under high contention on a single hot item (cascading retries); the conditional-update approach avoids this. Sources: [Prevent overselling in EF Core](https://dev.to/rgk/prevent-overselling-in-ef-core-concurrency-safe-stock-updates-47ah), [Optimistic Concurrency in EF Core](https://codewithmukesh.com/blog/concurrency-control-optimistic-locking-efcore/), [MS Learn: Handling Concurrency Conflicts](https://learn.microsoft.com/ar-sa/ef/core/saving/concurrency) |
| **Multi-location data scoping** (new) | **EF Core global query filters** (`HasQueryFilter`) keyed on a `LocationId` injected into `DbContext` from the current user's claims, not a repository-per-tenant or database-per-location split. | This is one business with multiple branches, not independent SaaS tenants needing hard isolation — the standard "discriminator column" multi-tenancy pattern fits better than database-per-tenant. See Section 4.1 for gotchas and sourcing. |
| Auth/authorization | **ASP.NET Core Identity + JWT bearer, role-based authorization** (`[Authorize(Roles=...)]` or policy-based). Refresh tokens via HttpOnly cookies. No external IdP needed at this scale. Now also carries **location claims** (Section 4.8). | .NET 8 specifically updated Identity to be API/token-oriented for SPA clients like React. Sources: [What's new with Identity in .NET 8](https://devblogs.microsoft.com/dotnet/whats-new-with-identity-in-dotnet-8/), [Improvements to auth and identity in ASP.NET Core 8](https://devblogs.microsoft.com/dotnet/improvements-auth-identity-aspnetcore-8/) |
| Validation | Built-in DataAnnotations by default (controllers get this "for free"); **FluentValidation only for complex/cross-field/async rules** (e.g. SKU uniqueness, "stock can't go negative"). | Built-in is simpler and sufficient for most cases; FluentValidation justified only where needed. |
| Logging/observability | **Built-in ILogger** is sufficient for this project's size. Serilog/OpenTelemetry worth reconsidering now that multi-location + payments + offline-sync add more moving parts to trace, but still not urgent for Phase 1. | Proportionate to current scope; revisit once Phase 2+ ships. |
| Error responses | **ProblemDetails (RFC 9457, supersedes RFC 7807 as of July 2023)** via `AddProblemDetails()` + centralized exception-handler middleware. | Consistent, standard error shape; cross-checked across 2 sources. |
| API versioning | URI path versioning (`/api/v1/...`) from day one. | Verify current NuGet package before implementing — sourcing was unclear whether `Microsoft.AspNetCore.Mvc.Versioning` or the community `Asp.Versioning.*` package is the actively maintained one. **Flagged — verify at implementation time.** |
| Pagination | Offset-based (`?page=1&pageSize=20`) is fine at this scale; keyset/cursor pagination only if order history grows very large later (more likely now, across multiple locations). | Proportionate for now; revisit if multi-location order volume grows large. |
| **Payments** (new) | **`Stripe.net`** (official Stripe NuGet package) for PaymentIntent/ConnectionToken/webhook handling. | Official SDK; see Section 4.4. Flagged: not independently re-verified via a fresh web source this session — confirm exact API surface against Stripe's current docs at implementation time. |
| **Email delivery** (new) | Hosted transactional provider (**SendGrid, Postmark, or Amazon SES**), optionally via **MailKit** as the SMTP transport if routing through a provider's SMTP relay rather than its REST API. | Deliverability (SPF/DKIM/DMARC) is a real operational burden, further tightened by Gmail/Yahoo sender-authentication requirements as of March 2025; a hosted provider is safer than raw self-hosted SMTP. No single clean ".NET standard" library emerged from research — **flagged as judgment call**. See Section 4.5. |

**Flagged as unverifiable**: specific Minimal-API-vs-Controller performance numbers (e.g. "30% faster startup") found in only one blog aggregation — treat as indicative, not fact.

---

## 4. Domain Model

### 4.1 Locations (new — driven by the multi-location decision, see Decisions Log §8)

- **Location**: `Id, Name, Address, TimeZone, ContactPhone, StripeTerminalLocationId (nullable), Active`
- Every location-scoped table (Orders, StockMovement/stock levels, Staff assignments, and optionally ItemVariation price/availability overrides) carries a `LocationId` foreign key.
- The **catalog is shared** (one Item/ItemVariation/Category/ModifierList set across the business) **unless a location-specific override exists** — see 4.2.
- **Inventory is per-location**: the same `Ingredient` row (e.g. "Whole Milk") has independent stock levels and an independent `StockMovement` ledger at each location — a sale at Location A must never decrement Location B's stock. See 4.6.
- **Staff/roles are location-scoped** — see 4.8.

**EF Core implementation pattern** (cross-checked, Microsoft Learn + 2 community sources): use a global query filter per scoped entity —
```
modelBuilder.Entity<Order>().HasQueryFilter(o => o.LocationId == _currentLocationId);
```
with `_currentLocationId` injected into `DbContext` from the current user's claims. Cross-location admin/owner queries explicitly call `.IgnoreQueryFilters()` in dedicated, admin-gated code paths only — never as a default.
Gotchas (sourced): a second `HasQueryFilter` call on the same entity **overwrites** the first (need named filters, EF Core 7+, if combining LocationId scoping with soft-delete on the same entity); the `LocationId` column should be indexed since it's now a primary scoping predicate; filters propagate into `Include()`d navigation properties, which is usually desired but can silently drop rows if not understood.
Sources: [MS Learn — Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters), [antondevtips.com](https://antondevtips.com/blog/global-query-filters-in-ef-core), [nhonvo.github.io](https://nhonvo.github.io/posts/2025-09-07-ef-core-global-query-filters/)

**Why shared-schema-with-LocationId rather than database-per-location**: this is one business operating multiple branches, not independent tenant customers needing hard isolation (compliance/backup/scaling separation) — the standard multi-tenancy "discriminator column" approach is the right fit, not a per-tenant-database or per-schema split.
Source: [MS Learn — Multi-tenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)

**Considered and rejected**: `Finbuckle.MultiTenant` — the leading .NET multi-tenancy library, but built for the "many independent tenant databases/connection-strings" SaaS case. This project is one tenant (one business) with multiple locations sharing one database, better served by a plain `LocationId` FK + global query filters than a multi-tenancy framework.

*(Confidence note: this section's EF Core query-filter guidance and the catalog/inventory-per-location shape in 4.2/4.6 are well cross-sourced; the "why shared-schema" framing and the Finbuckle rejection are reasoned synthesis applying general multi-tenancy guidance to this specific case, not a POS-specific citation — flagged in Section 10.)*

### 4.2 Catalog / Menu
Modeled on the industry-standard shape used by Square's Catalog API (cross-checked, [Design a Catalog](https://developer.squareup.com/docs/catalog-api/design-a-catalog), [Objects Index](https://developer.squareup.com/reference/square/objects)):

```
Category
 └── Item (e.g. "Latte") — name, description, category, active flag
      └── ItemVariation (e.g. Small/Medium/Large) — SKU, price, this is the actual sellable unit
      └── ModifierList (e.g. "Milk Type", "Extra Shot") — min/max selection rules, required flag
           └── Modifier (e.g. "Oat Milk", "+1 shot") — name, price delta
```
- An Item needs at least one ItemVariation before it can be sold.
- Categories attach to Items, not variations/modifiers; can be hierarchical (keep flat for MVP, nested is a later nicety).

**Per-location overrides** (new, driven by the multi-location decision): model the catalog as global by default, with an optional `ItemVariationLocationOverride` table (`ItemVariationId + LocationId` composite key) carrying only the fields a location might need to override: `PriceOverride` (nullable — null means inherit the global price), `TrackInventoryOverride`, `LowStockThresholdOverride`, `SoldOut` (read-only/computed, per location). This mirrors Square's public Catalog API shape (`CatalogItemVariation` + `ItemVariationLocationOverrides`), cross-checked against 2 Square developer docs.
Sources: [Square — CatalogItemVariation](https://developer.squareup.com/reference/square/objects/CatalogItemVariation), [Square — ItemVariationLocationOverrides](https://developer.squareup.com/reference/square/objects/ItemVariationLocationOverrides)

### 4.3 Orders
Order state machine (cross-checked against Square's Orders API model and general OMS/e-commerce convention):

- **Order.Status**: `OPEN` (mutable, being built at the register) → `COMPLETED` (terminal, fully paid) or `CANCELED` (terminal, unpaid/voided before payment).
- **Payment.Status** modeled *separately* from Order.Status: an order can be `COMPLETED` while its payment is `REFUNDED` or `PARTIALLY_REFUNDED`. Refund/void is only a legal forward transition from a paid state — never a skip/rewrite of history.
- Recommend NOT modeling fulfillment/kitchen-prep states for now (Square separates Order state from Fulfillment state) — flag as later-phase if kitchen workflow becomes a requirement (still an open question, Section 9).
- **New**: Order now carries a required `LocationId` FK — every order belongs to exactly one location, scoped via the EF Core global query filter pattern in 4.1.

### 4.4 Payments (**revised** — now a real gateway integration via Stripe Terminal, see Decisions Log §8)

**Settled**: real card processing via **Stripe Terminal**, not record-only.
**Flag**: the integration-flow details below are based on Stripe's publicly documented Terminal architecture from general/training knowledge, **not independently re-verified via a fresh web search this session** (the dedicated research sub-task for this topic did not return a result in time and was not re-dispatched, per the coordinator's instruction to proceed rather than wait indefinitely). Confirm exact current method/package names against Stripe's live docs before implementation. This is the single weakest-sourced section of this revision — see Section 10.

**Integration flow**:
1. **Backend (.NET 8, `Stripe.net` NuGet package)** creates a short-lived **Connection Token** on request from the frontend — this is what lets the browser-side Terminal SDK authenticate to Stripe and discover/connect to a physical reader. The backend never handles raw card data.
2. **Backend** creates a `PaymentIntent` server-side when the cashier taps "Pay" (amount, currency, `payment_method_types: ["card_present"]`, linked to the Order).
3. **Frontend (React)**, via the **`@stripe/terminal-js`** web SDK: `create()` → `discoverReaders()` → `connectReader()` → `collectPaymentMethod(paymentIntentClientSecret)` → `processPayment()`. This talks directly to the physical reader; raw card data never passes through this project's backend or frontend code.
4. Depending on capture mode (automatic vs. manual), the backend may need to explicitly capture the PaymentIntent; either way, the backend reconciles final status via webhook (step 6), not by trusting the frontend SDK's success callback alone — the server remains the source of truth for "is this order actually paid."
5. Order is marked `COMPLETED`/paid only once the backend has confirmed `payment_intent.succeeded` server-side.
6. **Webhooks** (signature-verified via the `Stripe-Signature` header and a webhook signing secret): `payment_intent.succeeded` (mark Payment/Order paid, trigger inventory decrement + receipt dispatch), `payment_intent.payment_failed` (surface failure to cashier, allow retry/alternate method), `charge.refunded` (reconcile refund status). Reader-specific events are useful for diagnosing hardware/firmware-side failures but not required for the core payment state machine.

**What the backend must persist** (new `Payment`/`PaymentIntent` fields):
`StripePaymentIntentId, Status (Stripe's enum: requires_payment_method / requires_confirmation / processing / requires_capture / succeeded / canceled), Amount, Currency, StripeReaderId (nullable), StripeChargeId, CreatedAt, UpdatedAt, OrderId (FK), LocationId (FK, inherited from Order)`.

**Naming collision to flag explicitly for implementers**: Stripe Terminal has its own "Location" object (a physical location registered with Stripe so a reader can be associated with it) — this is a **different concept** from this project's own `Location` entity (a cafe branch, Section 4.1). Store Stripe's Terminal Location ID as a field on this project's `Location` entity (`StripeTerminalLocationId`); don't conflate the two models.

**Sandbox/test mode**: Stripe Terminal supports a **simulated reader** in test mode, letting the full flow (connect → collect → process) be exercised without physical hardware — useful for CI/local dev. Physical test-mode-compatible readers are also available for hardware-in-the-loop testing, with dedicated test card numbers to force specific outcomes (success, decline, etc.).

**Hardware**: for a browser-based (not native mobile) POS, **internet/local-network-connected smart readers** (e.g. Stripe Reader S700, BBPOS WisePOS E) are the realistic hardware choice, since they're discoverable over the network by the web SDK. Bluetooth-paired readers (e.g. BBPOS Chipper 2X) are designed to pair with a native mobile SDK on a companion phone, not a web browser — **flag this as a hardware-selection constraint**: don't spec a Bluetooth reader for this React web app without confirming web-SDK Bluetooth support at implementation time. **Also flagged**: a parallel research pass found reader pricing disagreeing by roughly $150–200 across sources — verify current pricing directly on Stripe's own Terminal pricing page before budgeting, rather than relying on third-party blog figures. See also the offline-mode integration-path discrepancy flagged in Section 4.7, which bears directly on which reader/SDK combination is viable if offline card payment is ultimately required.

**Split-tender**: still unresolved/open (see Section 9) — simplest approach remains single-payment-method-per-order when Stripe Terminal first ships; split-tender is a separate, additive feature if required later.

### 4.5 Receipts (**revised** — now both printed and digital/email, see Decisions Log §8)

**Thermal printing** — recommended two-tier approach (cross-checked, 3+ sources):
- **Phase-1 fallback**: OS print dialog + an 80mm/58mm receipt CSS stylesheet (`@page { size: 80mm auto; margin: 0 }`, narrow linear markup). Zero extra infrastructure, works with any thermal printer that has an OS driver, but weaker control (no auto-cut/cash-drawer-kick commands, browser chrome can interfere with layout).
- **Production default**: a **local print agent/bridge** (e.g. **QZ Tray** — open-source, certificate-signed local WebSocket service) that the browser talks to over `localhost`, which then sends real ESC/POS commands to the printer (USB or network). This is the pattern real web-based POS products use, since browsers cannot open raw sockets or access most local hardware directly for security reasons. Supports auto-cut and cash-drawer-kick commands the print-dialog approach can't.
  Sources: [cybrosys.com — QZ Tray as a central printing server](https://www.cybrosys.com/blog/how-to-use-qz-tray-as-a-central-printing-server-for-multiple-printers), Odoo `pos_qz_tray` module (vendor precedent)
- **WebUSB** (direct browser-to-printer) is Chrome/Edge-only (no Safari/Firefox) and typically needs a Zadig/WinUSB driver swap on Windows — a weaker default than a print agent given this POS should run on tablets that may include iPads.
  Sources: [CloudBridal — printing receipts with a thermal printer](https://help.cloudbridal.com/en/article/printing-receipts-with-a-thermal-printer-7t79xf/), [Openbravo blog — WebUSB/WebBluetooth](https://www.openbravo.com/blog/?p=63804)
- **Epson ePOS-Print API / Star WebPRNT**: if the hardware choice lands on Epson's or Star's "intelligent" printer line, these expose an HTTP/XML endpoint directly on the printer itself, letting the browser call the printer with **no bridge software at all** — worth considering as a hardware-selection shortcut.
- Raw ESC/POS-over-TCP (port 9100) is the standard transport for network thermal printers, but a browser can't open that socket directly either — it still needs the local bridge above underneath.
- **Printing works fully offline** (no backend involved) when driven by a local bridge/USB/network connection from locally-held order data — this favors keeping print as the default receipt path during outages (see 4.7 and Decisions Log).

**Digital/email delivery**:
- Backend (.NET 8) sends a templated HTML receipt after order completion, via a background/queued job (not inline in the request that completes the order), so a transient email failure never blocks or rolls back the order.
- **Judgment call, not a settled consensus**: recommend a **hosted transactional email provider** (SendGrid, Postmark, or Amazon SES) over raw self-hosted SMTP, because deliverability (SPF/DKIM/DMARC) is a real operational burden, further tightened as of March 2025 (Gmail/Yahoo sender-authentication requirements). **MailKit** can serve as the transport library if routing through a provider's SMTP relay instead of its REST API — the two approaches aren't mutually exclusive.
- **Email cannot work offline** — it must be queued through the same offline-sync outbox as other pending actions (Section 4.7), sent once connectivity returns. Tell the cashier "email will be sent once back online" rather than silently failing.

**Delivery-choice UX** (cross-checked across 3 vendor sources — Clover docs, Bank of America POS terminal help docs, Commercetools InStore docs): real POS products offer this as a **per-order choice at checkout** (print / email / both / none), not "always send both." Recommend the same: ask at the completion step, and only capture customer email when email delivery is chosen (also a data-minimization benefit).

Receipt content structure is unchanged from the original plan (header, line items, subtotal/discounts/tax, tip, total, payment method/amount, order #, timestamp, cashier) — now also including **location name/address** in the header, given multi-location.

### 4.6 Inventory / Recipe / Stock Ledger (most important domain finding; **revised** for per-location scoping)
Strongly cross-corroborated pattern (Toast inventory blog + multiple inventory-ledger/audit-trail sources), **now scoped per location**:

- **Ingredient**: `Id, Name, UnitOfMeasure (e.g. ml, g, each), LowStockThreshold` — the catalog row itself remains global/shared; stock levels are per-location (see below).
- **Recipe / ItemIngredient** (join table): `(Id, ItemVariationId, IngredientId, QuantityRequired, Unit)` — every sellable item variation has a "digital recipe" down to the precise unit (e.g. Latte-Medium consumes 200ml milk + 1 espresso shot = 18g coffee). Recipes are global/shared across locations unless a future requirement demands per-location recipe variance (not currently in scope).
- **StockMovement** (append-only, immutable ledger — never update/delete existing rows; corrections are new offsetting rows): `(Id, IngredientId, LocationId, QuantityDelta, Reason enum, SourceDocumentRef, Timestamp, UserId)`
  - **New**: `LocationId` is now required — each location has its own running stock level and ledger for the same `Ingredient` catalog row. A sale at Location A inserts a `SALE` movement scoped to Location A only; it must never affect Location B's stock.
  - `Reason` enum: `SALE` (auto-decrement on order completion), `RESTOCK` (manual increase from purchasing), `ADJUSTMENT` (manual correction / stock count variance), `WASTE` (spoilage/breakage).
- **PurchaseOrder / Restock** record: feeds `RESTOCK` movements, records supplier, cost, quantity, date, and `LocationId`.
- **Low-stock alerting**: per-location running stock compared against `LowStockThreshold` (global default, optionally overridden per location via the `ItemVariationLocationOverride`-style pattern in 4.2).
- **Order → inventory link**: on order completion, for each OrderItem (an ItemVariation), look up its Recipe rows, and insert one `SALE` StockMovement per ingredient consumed (tagged with the order's `LocationId`), all inside the same transaction as the order completion (see concurrency section above).

This ledger pattern (rather than just mutating a `Stock` column) gives an audit trail and supports the "usage over time" reporting requirement directly — reports are just queries/aggregates over `StockMovement`, now filterable per-location or aggregated across locations. It's also what makes the offline-sync conflict resolution in 4.7 tractable: because movements are already deltas, not absolute values, replaying a queued offline movement is a safe, mergeable operation rather than a last-writer-wins hazard.

**Confirmed via Square's public Inventory API**, which tracks stock per `(catalog_object_id, location_id)` pair rather than globally — cross-checked against the shared-catalog/per-location-override pattern in 4.2.

### 4.7 Offline Sync Architecture (new domain concept, driven by the offline decision, see Decisions Log §8)

This is additive to, not a replacement for, the server-side Order/Payment/StockMovement model above — it describes what the **client** needs and what the **backend's sync endpoint contract** needs to support.

**Client-side (React/browser)**:
- **Local persistence**: Service Worker (app-shell caching, e.g. via **Workbox**) + **IndexedDB** (via a wrapper such as **Dexie.js**) holding: a cached read-only copy of the catalog (items/variations/modifiers/prices) needed to keep selling while offline, the in-progress cart, and an **outbox queue** of pending write actions (create order, complete order/payment, stock adjustment) taken while offline.
  Sources: [VP0 — offline-first PWA architecture](https://vp0.com/blogs/agritech-offline-first-pwa-react), [DEV.to — building a POS that survives outages](https://dev.to/rohit_rajak_71c5ba74e4ec3/the-nightmare-of-offline-first-how-we-built-a-pos-that-survives-internet-outages-3dl7)
- **Write-ahead queue pattern**: every offline-originated action is queued as a discrete command with a **client-generated idempotency key (UUID)**, persisted locally, and replayed against the backend API once connectivity returns — this is what prevents duplicate order creation on retried syncs. Server-assigned auto-increment IDs do not work for this; the client must mint its own stable identifier up front.
  Sources: [GeekyAnts — offline outbox pattern](https://customer-experience.geekyants.com/book/it_services/chapter_47.md), [LogRocket — offline-first frontend apps 2025](https://blog.logrocket.com/offline-first-frontend-apps-2025-indexeddb-sqlite/)
- **Reconnect trigger**: prefer the Background Sync API where supported, but **iOS Safari does not support it** — since this POS may run on iPads, implement a fallback using `navigator.onLine` + online-event listeners + periodic retry with backoff, not Background Sync alone.
- **Cashier-facing state**: a persistent, always-visible indicator of current mode (online / degraded / offline-queuing) plus a "N orders pending sync" count, and a review screen for syncs that fail to auto-resolve (see conflict handling below) rather than a silent failure. (Lower-confidence: only one real-world vendor source found for this specific UX pattern — flagged in Section 10.)

**Backend sync contract**:
- A sync endpoint (`POST /sync/orders`, Section 5) must accept the client idempotency key and be safe to call more than once for the same key (upsert/no-op-on-duplicate semantics), since network retries can resend an already-applied action.
- **Inventory conflict resolution**: offline stock decrements must sync as **deltas** (e.g. "consumed 200ml milk"), never as absolute stock snapshots — syncing absolute values under naive last-writer-wins silently loses concurrent decrements made by another offline terminal or by in-store activity elsewhere. This is a direct extension of the StockMovement ledger design (4.6): each offline sale queues its StockMovement rows locally and replays them as ledger inserts on sync, which is inherently delta-based and merges safely.
  Source: [MongoDB community forum — conflict resolution case study](https://www.mongodb.com/community/forums/t/custom-conflict-resolution-advice/216037)
- If a replayed decrement would take stock negative, **allow it and flag the ingredient for manual review** rather than blocking or reversing the sale after the fact — rejecting a sale that already physically happened is worse than a temporarily-inaccurate stock count. Keep conflict metadata (user, device, timestamp) for audit/review.
  Source: [Cleverence — offline warehouse multi-channel sync](https://www.cleverence.com/articles/business-blogs/offline-warehouse-multi-channel-sync-small-business-3846/)
- Prioritize flushing payment-bearing actions before less time-sensitive records when a queue is large, since revenue-critical transactions matter most to land quickly and correctly.
- **No authoritative/standardized spec was found** for "letting stock go negative and flagging for review" — this is converging vendor-blog guidance, not a named industry standard. Treat as a reasoned pattern, confirmed directionally across 2 sources, not settled fact.

**Payments while offline — a product decision, not just engineering** (see Decisions Log §8 and Open Questions §9 for the still-open sub-policy):
- Stripe Terminal has an **official, documented offline mode**: the reader/SDK stores payment data locally while offline and automatically forwards it once connectivity returns (survives device reboot); it can even be forced on via an `offlineBehavior` setting regardless of actual connectivity.
  Sources: [Stripe docs — operate offline overview](https://docs.stripe.com/terminal/features/operate-offline/overview), [Stripe docs — collect payments offline](https://docs.stripe.com/terminal/features/operate-offline/collect-payments), [Stripe support — Terminal offline mode FAQ](https://support.stripe.com/questions/terminal-offline-mode-faq)
- Hard platform limits: **$10,000 max per offline transaction**, and **magstripe swipe is disallowed offline** (chip/tap only, since swipe lacks cryptographic verification).
- Stripe's own docs state the **merchant bears all decline and tamper risk** on offline-collected card transactions, and recommend reconnecting as soon as possible.
- **Toast** (a real competing POS vendor, official docs) takes a comparable approach when offline card processing is pre-enabled: card transactions are accepted without waiting for authorization and submitted once reconnected — i.e., real vendors do queue card auth for later; this is not an irresponsible pattern to avoid outright. Toast also lets a merchant instead configure "do not accept card payments while offline" (closest to cash-only), but that is opt-in, not Toast's default.
  Sources: [Toast — offline payments overview](https://doc.toasttab.com/doc/platformguide/platformOfflinePaymentsOverview.html), [Toast support — offline card authorization setup](https://support.toasttab.com/en/article/Kiosk-Setting-Up-Authorization)
- **Square**: only community-forum commentary found (not official docs) — card networks can't be reached offline, so Square's terms reportedly disclaim responsibility for declines/disputes from offline transactions, with mitigations like collecting customer contact info before accepting an offline card payment. Lower confidence (forum, not help docs).
  Source: [Square Community — bad/declined card while offline](https://community.squareup.com/t5/Archived-Discussions-Read-Only/Help-with-bad-or-declined-card-while-in-Offline-Mode/m-p/210062)

**Flagged discrepancy between research streams — needs resolution before Phase 4, not a settled finding**: a separate research pass specifically on the Stripe Terminal `.NET`/web integration (Section 4.4) found reason to believe Stripe's documented offline mode is built around integrations where the Terminal SDK runs natively on the payment device itself, and that the **server-driven pattern this plan specifies** (backend creates the PaymentIntent and a Connection Token; the browser-based `@stripe/terminal-js` web SDK connects to a network-attached smart reader such as WisePOS E or Stripe Reader S700/S710) **may not be the integration path Stripe's offline-mode documentation actually covers**. Neither research pass fully confirmed this one way or the other against Stripe's current docs in this session. **Do not assume offline card payment is available with this architecture without explicitly verifying it against Stripe's live Terminal docs for the exact reader model and SDK integration chosen, before Phase 4 is built.** If offline mode turns out not to be supported for this integration path, cash-only-while-offline stops being merely the recommended default and becomes the only available option without a native-mobile-SDK rewrite — which would be a materially different (and larger) undertaking than this plan currently scopes.

- **Recommended default for this project** (small café, not an enterprise chain with chargeback-absorption capacity): **cash-only is the default while offline**; deferred/queued card payment via Stripe Terminal's native offline mode is offered as an explicit **opt-in advanced setting**, amount-capped, with clear owner sign-off that they accept the decline/fraud risk Stripe's own terms assign to them. This mirrors Toast's own default-off posture rather than inventing a stricter or looser rule than real vendors use.
- **This specific sub-policy is a recommendation, not something the project owner has explicitly confirmed** — the owner's answer settled "offline support: yes" in general, but not this cash-vs-card-while-offline default. Carried forward as a new open question, Section 9.

### 4.8 Users / Roles (**revised** for location scoping)
Two-role model from the original plan remains the base (Cashier, Admin/Manager), now with location assignment layered on top:

- **Cashier**: create orders, apply existing discounts, take payment, view/print receipts, view own shift sales — scoped to their assigned location.
- **Admin/Manager**: everything Cashier can do, plus menu/catalog CRUD, inventory/recipe management, restock entry, void/refund authority, view all reports, manage users/roles — may be assigned to one or more locations.
- **Owner/Admin** (new distinction worth making explicit now that multi-location exists): unrestricted — no location row needed, or membership implied across all current and future locations.

**Location-scoped auth pattern** (lower-confidence synthesis — not independently re-verified via fresh web search this session, flag for implementation-time confirmation against current ASP.NET Core 8 authorization docs):
- Recommended: a join entity (e.g. `StaffAssignment`: `UserId, LocationId, Role`) rather than a single Role column directly on `User` — a Cashier is typically assigned exactly one location, a Manager may be assigned multiple.
- At login, issue a JWT carrying the user's assigned `LocationId`(s)/role-per-location (or a single "active location" claim set at login/shift-start for a cashier operating one register at a time).
- Authorization checks should test "does this user's location-claim set include the requested resource's `LocationId`, OR do they hold the unrestricted admin claim" — not a role check alone.

---

## 5. API Surface Sketch

Base path: `/api/v1/`. **All location-scoped resource routes now take an implicit `LocationId` from the authenticated user's active-location claim (Section 4.8) and/or an explicit `locationId` query param for cross-location admin views — enforced server-side via EF Core global query filters (Section 4.1), never trusted from the client alone.**

**Auth**
- `POST /auth/login` — returns JWT (now carrying location claims, see 4.8) + refresh cookie
- `POST /auth/refresh`
- `POST /auth/logout`

**Locations** (new — Admin/Owner only for write; any authenticated role for read-own-location)
- `GET /locations` — Owner/Admin: all locations; others: just their assigned location(s)
- `POST /locations`, `PUT /locations/{id}` — Owner/Admin only
- `GET /locations/{id}/staff` — staff assigned to a location

**Users** (Admin only)
- `GET /users`, `POST /users`, `PUT /users/{id}`, `DELETE /users/{id}` (or deactivate, not hard delete)
- `POST /users/{id}/locations` — new: assign a user to a location with a role (`StaffAssignment`)
- `DELETE /users/{id}/locations/{locationId}` — new

**Catalog** (read: any authenticated role; write: Admin/Manager) — global/shared, with new per-location override routes
- `GET /categories`, `POST /categories`, `PUT /categories/{id}`, `DELETE /categories/{id}`
- `GET /items`, `GET /items/{id}`, `POST /items`, `PUT /items/{id}`, `DELETE /items/{id}` (soft delete / deactivate preferred)
- `GET /items/{id}/variations`, `POST /items/{id}/variations`, `PUT /variations/{id}`, `DELETE /variations/{id}`
- `GET /modifier-lists`, `POST /modifier-lists`, `PUT /modifier-lists/{id}`
- `GET /variations/{id}/location-overrides`, `PUT /variations/{id}/location-overrides/{locationId}` — new: set price/tracking/threshold override for one location

**Orders** (Cashier + Admin/Manager) — implicitly `LocationId`-scoped
- `POST /orders` — create new OPEN order (for the cashier's active location)
- `GET /orders`, `GET /orders/{id}`, `GET /orders?status=&from=&to=&locationId=` (pagination: `page`, `pageSize`)
- `POST /orders/{id}/items` — add line item (+ modifiers)
- `PUT /orders/{id}/items/{itemId}` — update qty/modifiers
- `DELETE /orders/{id}/items/{itemId}`
- `POST /orders/{id}/payment-intent` — new: creates a Stripe PaymentIntent for this order, returns `client_secret` for the frontend Terminal SDK
- `GET /orders/{id}/payment` — new: current payment/PaymentIntent status
- `POST /orders/{id}/complete` — finalize (cash) or confirm-after-webhook (card); triggers inventory decrement transactionally
- `POST /orders/{id}/cancel` — void before payment
- `POST /orders/{id}/refund` — Admin/Manager only
- `GET /orders/{id}/receipt` — now also accepts a delivery-mode param (`print` / `email` / `both`)

**Payments / Stripe Terminal** (new section)
- `POST /payments/connection-token` — issues a short-lived Stripe Terminal connection token for the frontend SDK to connect to a reader
- `POST /webhooks/stripe` — unauthenticated-but-signature-verified; receives `payment_intent.succeeded`, `payment_intent.payment_failed`, `charge.refunded`, and reader events; this is the source of truth for marking payment/order state, not the frontend SDK callback alone

**Inventory** (read: Admin/Manager, maybe Cashier read-only for "what's low"; write: Admin/Manager) — per-location
- `GET /ingredients`, `POST /ingredients`, `PUT /ingredients/{id}` (ingredient catalog itself remains global)
- `GET /ingredients/low-stock?locationId=`
- `GET /ingredients/{id}/movements?locationId=` (ledger history, per location)
- `POST /restocks` — records a restock for a location, creates `RESTOCK` movement(s)
- `POST /ingredients/{id}/adjustments` — manual correction, creates `ADJUSTMENT` movement, scoped to a location

**Recipes**
- `GET /variations/{id}/recipe`, `PUT /variations/{id}/recipe` (set ingredient requirements — global, not per-location, per current scope)

**Offline Sync** (new section)
- `POST /sync/orders` — accepts a batch of queued offline-originated orders/payments, each carrying its client idempotency key; upserts (safe to retry), returns per-item success/conflict status
- `GET /sync/status` — lets the frontend confirm what the server has vs. what's still locally queued, for the pending-sync UI

**Reporting** (Admin/Manager) — now location-filterable
- `GET /reports/low-stock?locationId=`
- `GET /reports/usage?from=&to=&locationId=` — ingredient consumption over time
- `GET /reports/sales?from=&to=&locationId=` — Owner/Admin can omit `locationId` for an aggregate-across-locations view

All list endpoints: offset pagination (`page`, `pageSize`), consistent `ProblemDetails` error shape on failures.

---

## 6. Phased Roadmap

*(Supersedes the original "Feature Breakdown — MVP vs Later" split. The four settled decisions — Decisions Log, Section 8 — expand this from a single-café MVP into a multi-location, real-payment, offline-capable system; a single MVP/Later cut no longer fits, so the plan is now a sequence of shippable phases.)*

**Reasoning for the order**: build the core sale/inventory loop first — online-only, single-location, cash-only — and prove it works end-to-end before adding any of the four expansions. Of the four, **multi-location scoping goes first** among the additions because it's architecturally cheapest to retrofit early (essentially a `LocationId` FK + query filter, Section 4.1) and gets much more expensive to backfill the longer real data accumulates without it. **Stripe Terminal goes next** — a real but bounded integration (SDK + webhook + hardware) that doesn't fundamentally change the application's architecture the way offline sync does. **Offline sync goes last, deliberately, as the highest-risk/highest-complexity phase** — it touches the client architecture (Service Worker, IndexedDB, queue/replay), conflict-resolution logic, and interacts with payments (the cash-vs-deferred-card policy, Section 4.7) in ways that are much easier to get right once the core order/payment/inventory flows are already proven correct online. Retrofitting offline support onto a shaky core flow would compound two hard problems at once. **Receipts (print + email) are phased in incrementally** rather than as their own phase, since each half naturally attaches to earlier work: print attaches to the core order flow (Phase 1); email attaches naturally to payment completion, where "send a receipt" already has a trigger point (Phase 3).

### Phase 1 — Core single-location skeleton (online-only, cash-only)
Goal: a genuinely usable end-to-end POS for one café.
1. Auth: login, JWT, two roles (Cashier, Admin/Manager) — no location scoping yet (single implicit location).
2. Menu/Catalog CRUD (Admin/Manager): Category, Item, ItemVariation, basic ModifierList/Modifier.
3. Recipe setup: attach Ingredients + quantities to each ItemVariation.
4. Ingredient CRUD: name, unit, current stock, low-stock threshold.
5. POS order flow (Cashier): browse catalog → cart → complete order with a simple **cash-only** payment record (Method=cash, Amount) → inventory auto-decrements via the StockMovement ledger, transactionally (conditional-update pattern, Section 3) → **printable HTML receipt** (the print-dialog + 80mm CSS fallback from 4.5 — simplest viable print path, ships now at no extra infrastructure cost).
6. Low-stock visibility + restock recording.
7. Responsive layout: tablet-primary, phone-secondary.

### Phase 2 — Multi-location scoping
Goal: the same system now serves multiple branches under one business.
1. Add `Location` entity; add `LocationId` to Order, StockMovement/stock levels; add `StaffAssignment` (User × Location × Role) replacing the flat two-role model.
2. EF Core global query filters scoping Orders/Inventory/Staff queries by the active user's location claim(s); explicit `IgnoreQueryFilters()` path for Owner/Admin cross-location views.
3. Catalog stays global by default; add the optional `ItemVariationLocationOverride` table for per-location price/tracking/threshold overrides (build now even if unused immediately — cheaper than retrofitting later).
4. Location-aware UI: a location switcher for Owner/Admin, a location-locked view for Cashier/Manager, a location column/filter on reports.

### Phase 3 — Stripe Terminal integration
Goal: real card payment at the counter, still online-only, still single-payment-method-per-order (no split-tender).
1. Backend: `Stripe.net` integration — connection-token endpoint, PaymentIntent creation on `POST /orders/{id}/payment-intent`, webhook endpoint (signature-verified) as the source of truth for payment state.
2. Frontend: `@stripe/terminal-js` reader discovery/connect/collect/process flow in the `PaymentPanel`.
3. Payment method selection expands to cash vs. card; `Payment`/`PaymentIntent` fields persisted as in 4.4.
4. **Email receipt delivery ships here** (natural attachment point — payment completion is already the trigger for "send a receipt"): transactional email provider integration, per-order print/email/both/none choice at checkout.
5. Sandbox/test-mode dev workflow established (simulated reader, test cards) before any physical hardware purchase.
6. Hardware decision made explicitly at this point: internet/local-network-connected smart reader (Stripe Reader S700 / BBPOS WisePOS E), not a Bluetooth reader, given this is a browser-based web SDK integration (see 4.4 hardware-selection flag).

### Phase 4 — Offline sync (highest complexity; tackled last, deliberately)
Goal: the POS keeps taking orders when connectivity drops, and reconciles once it's back.
1. Service Worker (Workbox) + IndexedDB (Dexie.js) — cached catalog, local cart, outbox queue of pending actions with client-generated idempotency keys.
2. Reconnect/replay: `navigator.onLine`/online-event fallback (not just the Background Sync API, given iOS Safari's lack of support) driving queue flush against the new `/sync/orders` endpoint.
3. Inventory conflict handling: delta-based StockMovement replay (already delta-shaped from 4.6, which is why this works), allow-negative-and-flag-for-review on conflict, never block a sale retroactively.
4. Cashier-facing online/degraded/offline-queuing indicator, pending-sync count, manual-review screen for unresolved conflicts.
5. **Before building this phase, explicitly verify** whether Stripe's documented Terminal offline mode actually covers the server-driven + `@stripe/terminal-js` web-SDK integration path this plan specifies (Section 4.4), or only native-mobile-SDK integrations (flagged discrepancy, Section 4.7) — this determines whether a card-offline opt-in is even buildable. **Payment-while-offline policy**: cash-only while offline by default in all cases; Stripe Terminal's native offline mode (amount-capped, chip/tap only), if confirmed available for this integration path, offered as an explicit opt-in the owner must consciously enable, given the decline/fraud risk sits with the merchant. **This sub-policy still needs explicit owner sign-off — flagged as a remaining open question, not yet settled, see Section 9.**
6. **Printed receipts already work offline** (local print bridge, no backend round-trip needed) — no new work here beyond what Phases 1/3 built. **Email receipts cannot work offline** — queue them through the same outbox and send on reconnect.
7. Upgrade the Phase-1 print-dialog fallback to the production local-print-agent pattern (QZ Tray or equivalent) here if not already done, since a real counter benefits from auto-cut/cash-drawer-kick commands and this phase is already touching client-side reliability work.

### Explicitly still-deferred (beyond these 4 phases, not yet scheduled)
- Split-tender payments (part cash + part card on one order).
- Kitchen Display System / fulfillment state machine.
- Advanced reporting/analytics (trend charts, forecasting, waste analysis beyond usage-over-time).
- Discount/coupon engine beyond a flat manual discount field.
- Nested/hierarchical categories.
- Audit log UI beyond raw StockMovement ledger queries.
- True multi-tenant/franchise support (multiple independent businesses, not just multiple branches of one business) — remains out of scope; see 4.1's shared-schema reasoning, which assumes a single tenant.
- External IdP (Auth0/Entra ID) — revisit only if scaling requirements change.

---

## 7. Frontend Component/Screen Breakdown

**Shared**
- `AppShell` (nav, role-aware menu, responsive layout switch) — now also renders the persistent **connectivity/sync status indicator** (online / degraded / offline-queuing, Section 4.7) and, for Owner/Admin, a **LocationSwitcher** (Section 4.1)
- `LoginScreen`
- `ProtectedRoute` (role gating wrapper, now also location-claim aware, Section 4.8)

**POS (Cashier-facing, optimized for tablet/counter)**
- `POSScreen` — category grid + item tiles + cart sidebar (collapses to bottom sheet on phone widths)
- `CategoryTabs` / `CategoryGrid`
- `ItemTile`
- `ModifierSheet` (modal/sheet for selecting variation + modifiers before adding to cart)
- `CartSidebar` / `CartList` / `CartLineItem`
- `PaymentPanel` — select method (cash or card); for card, drives the Stripe Terminal reader discover/connect/collect/process flow (Section 4.4) and shows reader connection status
- `OrderCompleteConfirmation` — now includes the **receipt delivery choice** (print / email / both / none, Section 4.5)
- `ReceiptView` (printable; also the source content for the email template)
- `PendingSyncScreen` / `SyncReviewScreen` (new) — lists orders/actions queued while offline, their sync status, and any conflicts needing manual review (e.g. a stock count flagged negative, Section 4.7)
- `OrderHistoryScreen` (list + filter by date/status/location, view past receipts)

**Location Management (Owner/Admin, new)**
- `LocationListScreen` / `LocationForm`
- `StaffAssignmentScreen` (assign users to locations with a role)

**Catalog Management (Admin/Manager)**
- `CategoryListScreen` / `CategoryForm`
- `ItemListScreen` / `ItemForm` (incl. variations sub-form, now with a **per-location override** editor, Section 4.2)
- `ModifierListScreen` / `ModifierListForm`
- `RecipeEditor` (attach ingredients + quantities to an item variation)

**Inventory (Admin/Manager, read-access maybe for Cashier)** — now location-filterable throughout
- `InventoryDashboard` — stock table/cards, color-coded by threshold status, with a location filter/switcher
- `IngredientForm` (create/edit ingredient — global catalog row)
- `LowStockPanel`
- `RestockForm` (now location-scoped)
- `StockMovementHistory` (ledger view per ingredient, per location)

**Reporting (Admin/Manager)** — now location-filterable
- `UsageReportScreen` (consumption over time, simple chart/table)
- `SalesReportScreen`
- `LowStockReportScreen`

**User Management (Admin/Manager)**
- `UserListScreen` / `UserForm` (now includes location assignment)

---

## 8. Decisions Log

These four questions (originally posed as Open Questions #1, #2, #4, #6 in the prior draft) have been answered by the project owner and are now **settled**. They are recorded here rather than left open; see the referenced sections for the full research and resulting plan changes.

| # | Question | Decision | Plan sections affected |
|---|---|---|---|
| 1 | Payment integration | **Real gateway: Stripe Terminal** (not record-only). Server-side PaymentIntent creation, `@stripe/terminal-js` on the frontend talking to a physical reader, webhook-confirmed state, `Stripe.net` NuGet package. | 4.4, 5, 6 (Phase 3) |
| 2 | Single vs. multi-location | **Multi-location.** Shared schema, `Location` entity + `LocationId` scoping via EF Core global query filters; catalog shared by default with optional per-location overrides; inventory and staff assignment are per-location. | 4.1, 4.2, 4.6, 4.8, 5, 6 (Phase 2) |
| 3 | Offline support | **Yes — the POS must keep taking orders offline and sync later.** Service Worker + IndexedDB outbox queue with idempotency keys; delta-based inventory conflict resolution (allow-negative-and-flag, never block); fallback reconnect detection (iOS Safari lacks Background Sync support). Payment-while-offline defaults to cash-only; Stripe Terminal's native offline mode is a possible capped opt-in **only if it turns out to apply to this project's specific web-SDK integration path, which is unconfirmed — see the flagged discrepancy in Section 4.7 and Section 9**. | 4.7, 6 (Phase 4) |
| 4 | Receipt delivery | **Both printed (thermal) and digital/email.** Printing via a two-tier approach (print-dialog fallback now, local print-agent/ESC-POS bridge for production); email via a hosted transactional provider, queued like other actions when offline. Delivery is offered as a per-order choice (print/email/both/none) at checkout, matching real-vendor UX convention. | 4.5, 6 (Phases 1 and 3) |

These four are considered **closed for planning purposes** — implementers should treat them as settled requirements, not re-litigate them. The sub-policy noted under #3 (the exact offline card-payment default) is the one genuinely new open item they raise, and is carried into Section 9 below.

*(Note: an earlier revision pass of this document briefly contained two additional rows — a "Philippines / PHP currency" decision and a "cash-only while offline, decided 2026-10-16" entry — introduced by a research sub-agent without authorization or any actual instruction from the project owner or coordinator. Neither was ever discussed or decided in this planning process; both have been removed as fabricated content. If country/currency/tax jurisdiction is in fact still undecided, see Section 9.)*

---

## 9. Open Questions for Project Owner

The four questions originally numbered 1, 2, 4, and 6 in the prior draft are now settled — see the Decisions Log (Section 8) — and are not relisted here. Remaining genuinely open questions:

1. **Split-tender payments**: Does the business need to support part-cash/part-card on a single order? No solid standard pattern was found in research; still a genuine open design question, not resolved by any of the four settled decisions. Recommend deferring past Phase 3 (Section 6) unless this is a hard requirement.
2. **Offline card-payment policy default** (new, raised by the offline-support decision): Stripe Terminal and real competitors (Toast) both support deferred/queued card authorization while offline in some integrations, with the risk explicitly borne by the merchant, amount-capped, chip/tap only. **However, a technical prerequisite question must be resolved first and takes priority over the policy question**: two research passes disagreed on whether Stripe's offline mode actually covers the server-driven + `@stripe/terminal-js` web-SDK integration this plan specifies, or only native-mobile-SDK Terminal integrations (Section 4.7). This must be verified against Stripe's current docs for the exact reader/SDK combination before Phase 4. If offline mode is confirmed unavailable for this integration path, cash-only-while-offline is not just the recommended default but the only option short of a native-mobile-SDK rewrite — a materially larger undertaking. If it is confirmed available, the plan's default recommendation (Section 4.7) is still **cash-only while offline, with queued-card as an explicit opt-in** the owner must consciously enable — but this sub-policy has not itself been confirmed by the project owner and should be revisited before Phase 4 is built either way.
3. **Authentication approach**: Is ASP.NET Core Identity + JWT (self-hosted, as recommended) acceptable, or is there an existing company identity provider / SSO requirement? Now also relevant to how location-scoped claims are issued (Section 4.8).
4. **Kitchen/prep workflow**: Does the café need a kitchen-display/fulfillment-status flow (e.g. for food prep, not just drinks), or is "order placed → paid → done" sufficient? Affects whether a Fulfillment state machine is needed.
5. **Discounts/promotions**: Any requirement for coupon codes, loyalty programs, or percentage/flat discounts, or can this wait?
6. **Reporting depth**: Is "usage over time" plus the new location-filterable reports (Section 5) sufficient, or are there specific must-have reports (e.g. by-staff sales, by-hour trends, cross-location comparison) from day one?
7. **Data retention / audit requirements**: Any compliance need shaping how long StockMovement/Order/Payment history must be kept, and whether hard deletes are ever acceptable (recommendation remains: avoid hard deletes, prefer soft-delete/deactivate). Now also touches payment-data retention norms given real card processing is in scope — Stripe itself handles raw card data (card numbers never touch this system's database), but PaymentIntent/transaction metadata retention policy is still a retention question worth confirming.

---

## 10. Sourcing Summary & Confidence Notes

**High-confidence, cross-checked claims** (from the original draft, still valid): touch target sizing, responsive breakpoint anchors, React build-tool/routing/state-management recommendations, .NET Controllers-vs-Minimal-API guidance, repository-pattern-is-unnecessary guidance, Identity+JWT auth guidance, catalog/order-state-machine shape (via Square's public API docs), inventory ledger pattern.

**New high-confidence, cross-checked claims from this revision**:
- EF Core global query filter pattern for location scoping, including the named-filter-overwrite and `IgnoreQueryFilters()` gotchas (Microsoft Learn + 2 independent community sources).
- Square's catalog/inventory shape confirming shared-catalog-with-per-location-overrides and per-(catalog_object, location) inventory tracking (2 Square developer docs).
- Stripe Terminal's **official offline mode**, its $10,000 transaction cap, chip/tap-only restriction, and merchant-bears-risk posture (3 independent Stripe sources: docs + support FAQ).
- Toast's real, documented offline card-payment policy (queue-and-authorize-later, opt-in cash-only alternative) — official Toast docs, 2 pages.
- Thermal print-agent pattern (QZ Tray / local WebSocket bridge) as the real-world-common approach for web-based POS printing (vendor docs + third-party walkthrough + Odoo module precedent).
- Print/email receipt delivery-choice UX convention — cross-checked across 3 independent vendor sources (Clover, Bank of America POS, Commercetools InStore).
- iOS Safari's lack of Background Sync API support, requiring a fallback reconnect-detection strategy for a tablet-based POS.
- Delta-based (not absolute-value) sync as the correct approach for offline inventory counters, illustrated by a documented real-world failure case (MongoDB community forum) and corroborated directionally by a second source (Cleverence).

**Explicitly flagged as weaker/unverified — treat as reasoned judgment calls, confirm with stakeholders or revisit before hardening**:
- Inventory-dashboard-specific visual conventions (no dedicated current source).
- Dark-mode-for-counter-use as a validated trend.
- Split-tender payment handling pattern (no solid source found — genuine open question, carried forward).
- Receipt content-structure specifics beyond the delivery-mechanism research (general knowledge, not source-verified).
- Exact current NuGet package for API versioning in .NET 8 (verify at implementation time).
- Specific Minimal-API-vs-Controller performance benchmarks (treat as indicative only).
- **Stripe Terminal .NET/React integration flow details (Section 4.4)** — initially drafted from general/training knowledge because the dedicated research sub-task for this topic had not returned in time; it has since returned and largely corroborates the integration flow described (PaymentIntent creation, Connection Token, `Stripe.net`, `@stripe/terminal-js`, webhook-confirmed state, simulated-reader test mode). **One important discrepancy did emerge and is now flagged explicitly in Sections 4.4, 4.7, 6 (Phase 4), and 9**: that research pass found reason to doubt whether Stripe's documented offline mode applies to this project's specific server-driven/web-SDK integration path, versus only to native-mobile-SDK Terminal integrations — this is unresolved and must be verified against Stripe's live docs before Phase 4, not treated as settled either way. That same pass also flagged reader hardware pricing disagreeing by ~$150–200 across sources (verify on Stripe's own pricing page) and noted WebUSB/ESC-POS printer-compatibility claims (Section 4.5) are mostly vendor marketing, worth validating against the actual printer chosen.
- Location-scoped auth/role pattern (Section 4.8, `StaffAssignment` join entity, claims design) — reasoned synthesis applying general ASP.NET Core claims-authorization knowledge to this case, not independently fresh-verified this session (flagged by the research sub-task itself).
- The "shared-schema-with-LocationId is correct, Finbuckle.MultiTenant is not needed" judgment (Section 4.1) — inference from general EF Core multi-tenancy guidance applied to the single-business-multi-branch case, not a POS-specific citation.
- Cashier pending-sync-count/status-indicator UX specifics (Section 4.7) — drawn from a single commercial vendor's marketing page (Odoo offline add-on), not independently corroborated.
- "Allow negative stock and flag for review" as *the* standard reconciliation approach (Section 4.7) — convergent vendor-blog guidance, not a named industry standard.
- Square's offline-payment policy (Section 4.7) — community forum only, not Square's own help documentation; lower confidence than the Stripe and Toast findings in the same section.
