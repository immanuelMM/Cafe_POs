# End-to-end tests (Playwright)

Runs a real browser against the real stack: PostgreSQL + the .NET API + the nginx-served React build, started with
docker compose and seeded by `DevDataSeeder`.

```bash
cd e2e
npm install --legacy-peer-deps
npm run install:browsers      # one-time: Playwright Chromium
docker compose -f ../docker-compose.yml down   # stop the dev stack: the E2E stack uses the same ports
npm run stack:up              # build + start db/api/web with the E2E override and a separate DB volume
npm run test:e2e              # full suite (about 90 s)
npm run test:e2e:smoke        # fast @smoke subset (for every PR)
npm run test:e2e:a11y         # axe accessibility checks only
npm run stack:down            # stop; `npm run stack:reset` also deletes the E2E database volume
```

- `docker-compose.e2e.yml` disables rate limiting and points the database at its own volume (`cafepos-e2e-pgdata`),
  so your hand-edited dev data is never used or modified.
- The override reuses the dev stack's container names, so `stack:up` recreates `cafepos-db` on the E2E volume. Afterwards a
  plain `docker compose up -d` (dev stack) brings the dev database back on `backend_cafepos-pgdata`; until then anything
  running against `cafepos-db` is using the E2E data. Always `docker compose down` the dev stack before `stack:up`.
- CI runs the `@smoke` subset on pull requests and the full suite on `main` and nightly (`.github/workflows/ci.yml`, job `e2e`).
- Tests share one database and run serially. They assert on deltas (stock before/after), and `global-setup.ts` tops
  up consumed ingredients, so the suite can be re-run without resetting. `npm run stack:reset` gives a pristine DB.
- Override targets with `E2E_BASE_URL` (default `http://localhost:5173`) and `E2E_API_URL` (default `http://localhost:5080`).
- Reports, traces and screenshots (`playwright-report/`, `test-results/`) are git-ignored.

## M1 visual baseline (local only)

`tests/visual-baseline.spec.ts` (`@visual-m1`) captures 57 full-page screenshots (15 screens x 4 viewports) and pixel-diffs them.
It is skipped unless `VISUAL_MODE` is set, so it never runs in the normal suite or CI. Run it against an isolated stack
(own compose project, ports and volume; it can run next to the dev stack):

```bash
WEB_PORT=15173 API_PORT=15080 DB_PORT=15433 docker compose -p cafepos-e2e \
  -f ../docker-compose.yml -f docker-compose.e2e.yml -f docker-compose.e2e-iso.yml up -d --build --wait
export E2E_BASE_URL=http://localhost:15173 E2E_API_URL=http://localhost:15080
node visual.mjs capture   # writes e2e/.visual-baseline-m1/*.png (git-ignored, Windows-rendered, same machine only)
node visual.mjs compare   # re-captures to _current/, writes _diff/*.png and prints % differing pixels per screenshot
```
