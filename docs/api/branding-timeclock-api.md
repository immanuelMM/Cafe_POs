# Branding and staff time clock API

Applies on top of [`phase1-api.md`](phase1-api.md) and [`phase2-api.md`](phase2-api.md). Same conventions: base `/api/v1`, JSON camelCase, errors are RFC 9457 ProblemDetails, times are UTC ISO-8601.

> Frontend note: the client retries every `401` once after a token refresh, so the time clock never uses `401` for a wrong PIN (it uses `403`).

## 1. Branding (one brand for every location)

Stored as a single row (`BrandSettings`, Id = 1). Uploaded images are stored **in the database** (`bytea`), so they survive container rebuilds.

### `BrandingDto`

```json
{
  "brandName": "M brew & Bean",
  "tagline": "Freshly brewed every day",
  "primaryColor": "#3b2417",
  "logoUrl": "/api/v1/branding/logo?v=638650000000000000",
  "tarpaulinUrl": "/api/v1/branding/tarpaulin?v=638650000000000000",
  "tarpaulinContentType": "image/png"
}
```

- `tagline`: optional (`null`), max 200 chars. Printed on the generated menu banner.
- `primaryColor`: `#RRGGBB`, default `#3b2417` (the current `--brand`).
- `logoUrl` / `tarpaulinUrl`: `null` when nothing is uploaded. Otherwise a path **relative to the API host** (prefix it with the API origin, i.e. `API_BASE` without `/api/v1`), with a `v` query value that changes on every upload (cache busting).
- `tarpaulinContentType`: `image/png`, `image/jpeg`, `image/webp` or `application/pdf`; `null` when none.

### Endpoints

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/branding` | Anonymous | `BrandingDto` (the login screen needs it) |
| PUT | `/branding` | Owner, Admin | Body `{ brandName (required, max 100), tagline? (max 200, empty/null clears), primaryColor (required, `^#[0-9a-fA-F]{6}$`) }` -> `BrandingDto`. `400` on validation errors |
| GET | `/branding/logo` | Anonymous | The image bytes with its content type, `Cache-Control: public, max-age=31536000, immutable` (URL is versioned). `404` when none |
| PUT | `/branding/logo` | Owner, Admin | `multipart/form-data`, field `file`. PNG, JPEG or WebP only (checked by content type **and** file signature), max **1 MB**. -> `BrandingDto`. `400` wrong type / too big / missing |
| DELETE | `/branding/logo` | Owner, Admin | -> `BrandingDto` (logoUrl null) |
| GET | `/branding/tarpaulin` | Anonymous | Bytes + content type, same caching. `404` when none |
| PUT | `/branding/tarpaulin` | Owner, Admin | `multipart/form-data`, field `file`. PNG, JPEG, WebP or PDF (type + signature checked), max **15 MB**. -> `BrandingDto` |
| DELETE | `/branding/tarpaulin` | Owner, Admin | -> `BrandingDto` |

SVG is rejected on purpose: it can carry script.

The **generated** menu banner (tarpaulin) is built by the frontend from the existing catalog endpoints plus `BrandingDto`; it needs no new endpoint.

### Brand colors and gradient

`BrandingDto` also carries `secondaryColor` and `tertiaryColor` (`#rrggbb` lowercase or `null`), `gradient` (bool, default `false`) and `gradientAngle` (0 to 360, default `135`). `PUT /branding` accepts the same fields: `null` or `""` clears a color; `400` when a color is not `#RRGGBB`, a tertiary color is set without a secondary, `gradient` is `true` with fewer than two colors, or the angle is outside 0 to 360.

### Design studio documents

Editable logo and banner designs, stored as opaque JSON owned by the frontend editor (layers, positions, fonts, embedded images). Saving a design does **not** change the live logo or tarpaulin: the editor exports a PNG/PDF and uploads it through `PUT /branding/logo` or `PUT /branding/tarpaulin`.

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/branding/designs/{kind}` | Owner, Admin | `kind` is `logo` or `banner`. -> `{ kind, document, updatedAt }`. `404` when nothing saved yet or unknown kind |
| PUT | `/branding/designs/{kind}` | Owner, Admin | Body `{ document: { ... } }` (must be a JSON object, max 8 MB including embedded images). -> `{ kind, document, updatedAt }`. `400` not an object / too large |

## 2. Staff time clock (shared PIN kiosk)

The kiosk is a screen in the app on the cafe tablet. It needs a **signed-in session with an active location** (any role, e.g. the cashier logged in at the till), so the PIN endpoints are never reachable anonymously from the internet. Staff then pick their name and enter their personal PIN; they do not log in themselves.

### Data

- `ApplicationUser` gains `ClockPinHash` (string, nullable, hashed with the Identity password hasher, never returned), `ClockPinFailedCount` (int), `ClockPinLockedUntil` (UTC, nullable).
- `TimeEntry` (location-scoped, `ILocationScoped`, global query filter like orders): `Id`, `LocationId`, `UserId`, `ClockInAt`, `ClockOutAt?` (UTC). At most one open entry (no `ClockOutAt`) per user and location.

### DTOs

```jsonc
// TimeClockStaffDto: one per staff member assigned to the active location (active users only, no Owners)
{ "userId": "guid", "displayName": "Casey Cashier", "role": "Cashier", "hasPin": true, "clockedIn": true, "openSince": "2026-10-18T01:00:00Z" }

// TimeEntryDto
{ "id": "guid", "userId": "guid", "displayName": "Casey Cashier", "locationId": "guid", "clockInAt": "2026-10-18T01:00:00Z", "clockOutAt": null, "hours": null }
// hours: worked hours rounded to 2 decimals; null while the shift is open

// PunchResultDto
{ "action": "In", "entry": { /* TimeEntryDto */ } }   // action: "In" | "Out"
```

### Endpoints

All need an authenticated session **with an active location** (`400 Location required` otherwise, e.g. Owner All-locations session), except `PUT /timeclock/my-pin`.

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/timeclock/staff` | Any role | `TimeClockStaffDto[]` for the active location, sorted by display name |
| POST | `/timeclock/punch` | Any role (kiosk) | Body `{ userId, pin }`. Toggles: no open entry -> clock **in**; open entry -> clock **out**. -> `PunchResultDto`. Errors: `404` user not assigned to this location (or inactive / Owner); `422 PIN not set`; `403 Incorrect PIN`; `423 PIN locked` after **5** wrong PINs in a row (locked for **5 minutes**, the counter resets on success) |
| PUT | `/timeclock/my-pin` | Any signed-in user | Body `{ pin }` (4 to 6 digits). Sets the caller's own PIN. -> `204` |
| PUT | `/users/{id}/pin` | Owner, Admin | Body `{ pin }` (4 to 6 digits). Same visibility rules as `PUT /users/{id}` (Admin: users at their location only, `404` otherwise; Admin cannot set an Owner's PIN, `403`). -> `204` |
| DELETE | `/users/{id}/pin` | Owner, Admin | Clears the PIN. Same rules. -> `204` |
| GET | `/timeclock/entries` | Any role | Query `from`, `to` (UTC ISO instants; entries whose `clockInAt` is in `[from, to)`; default: last 7 days), `userId` (optional). **Cashier**: always only their own entries (`userId` ignored). **Manager/Admin/Owner**: everyone at the active location, or one user with `userId`. Newest first. -> `TimeEntryDto[]` |

Totals (hours per person for the range) are computed by the client from the entries.
