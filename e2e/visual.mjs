// Cross-platform launcher for the M1 visual baseline:  node visual.mjs capture | compare
import { spawnSync } from 'node:child_process'

const mode = process.argv[2]
if (!['capture', 'compare'].includes(mode)) {
  console.error('usage: node visual.mjs capture|compare   (E2E_BASE_URL / E2E_API_URL select the stack)')
  process.exit(2)
}
const r = spawnSync('npx', ['playwright', 'test', 'visual-baseline', '--grep', '@visual-m1', ...process.argv.slice(3)], {
  stdio: 'inherit',
  shell: true,
  env: { ...process.env, VISUAL_MODE: mode },
})
process.exit(r.status ?? 1)
