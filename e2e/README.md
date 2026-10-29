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
- Tests share one database and run serially. They assert on deltas (stock before/after), and `global-setup.ts` tops
  up consumed ingredients, so the suite can be re-run without resetting. `npm run stack:reset` gives a pristine DB.
- Override targets with `E2E_BASE_URL` (default `http://localhost:5173`) and `E2E_API_URL` (default `http://localhost:5080`).
- Reports, traces and screenshots (`playwright-report/`, `test-results/`) are git-ignored.
