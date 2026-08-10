#!/usr/bin/env node
/**
 * Runs every guard in Tools/guards/. Exit code 1 if any guard fails.
 *
 * Each guard resolves its paths relative to the CURRENT WORKING DIRECTORY
 * (they look for `Assets/`, `Assets/_Project/Scripts`), so this runner pins
 * cwd to the repo root — derived from this file's own location, not from
 * wherever it happens to be invoked. That makes `node Tools/check.mjs` behave
 * identically from the repo root, from a subfolder, and from a git hook.
 *
 * Run:  node Tools/check.mjs
 */
import { execFileSync } from 'node:child_process'
import { readdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const toolsDir = dirname(fileURLToPath(import.meta.url))
const repoRoot = resolve(toolsDir, '..')
const guardsDir = join(toolsDir, 'guards')

const guards = readdirSync(guardsDir)
  .filter((f) => f.startsWith('guard-') && f.endsWith('.mjs'))
  .sort()

if (guards.length === 0) {
  console.error('check: no guards found in Tools/guards/ — that is itself a failure.')
  process.exit(1)
}

let failed = 0
for (const guard of guards) {
  try {
    execFileSync(process.execPath, [join(guardsDir, guard)], {
      stdio: 'inherit',
      cwd: repoRoot,
    })
  } catch {
    failed++
  }
}

if (failed > 0) {
  console.error(`\n${failed} guard(s) failed.\n`)
  process.exit(1)
}
console.log('\nAll guards passed.\n')
