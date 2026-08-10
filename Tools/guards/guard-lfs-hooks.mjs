#!/usr/bin/env node
/**
 * GUARD: when core.hooksPath is redirected, that folder must still contain the
 * four Git LFS hooks.
 *
 * THE DISASTER THIS PREVENTS
 * `git lfs install` writes pre-push / post-checkout / post-commit / post-merge
 * into .git/hooks/. This repo redirects hooks to Tools/hooks/ so the pre-commit
 * guard runner is committed and survives a clone — and the moment that redirect
 * is set, git stops reading .git/hooks/ ENTIRELY. The LFS hooks go quiet.
 *
 * Nothing appears to break. `git push` succeeds, because the clean filter still
 * turns binaries into pointer files and pointers push fine. What never happens
 * is the upload of the actual objects, which is the pre-push hook's job. The
 * failure surfaces later and somewhere else: a fresh clone on another machine
 * gets pointer text where the .fbx should be, and `smudge filter lfs failed`.
 * By then the "working" commits are already history.
 *
 * Discovered during Phase 0 setup, before any binary existed — which is the only
 * cheap time to discover it.
 *
 * Run:  node Tools/guards/guard-lfs-hooks.mjs
 */
import { execSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

const REQUIRED = ['pre-push', 'post-checkout', 'post-commit', 'post-merge']

function git(command) {
  return execSync(command, {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'ignore'],
  }).trim()
}

let hooksPath
try {
  hooksPath = git('git config --get core.hooksPath')
} catch {
  // Not set: git reads .git/hooks/, where `git lfs install` puts them. Nothing
  // to check — this guard only cares about the redirected case.
  console.log('✓ guard-lfs-hooks: core.hooksPath not set — .git/hooks/ is live, nothing to mirror')
  process.exit(0)
}

const missing = []
const notWired = []

for (const hook of REQUIRED) {
  const path = join(hooksPath, hook)
  if (!existsSync(path)) {
    missing.push(hook)
    continue
  }
  if (!readFileSync(path, 'utf8').includes(`git lfs ${hook}`)) notWired.push(hook)
}

if (missing.length === 0 && notWired.length === 0) {
  console.log(`✓ guard-lfs-hooks: all ${REQUIRED.length} LFS hooks present in ${hooksPath}/`)
  process.exit(0)
}

console.error(`\nguard-lfs-hooks: core.hooksPath is "${hooksPath}", so .git/hooks/ is IGNORED by git.`)
if (missing.length > 0) {
  console.error(`\n  Missing LFS hooks in ${hooksPath}/:`)
  for (const hook of missing) console.error(`    - ${hook}`)
}
if (notWired.length > 0) {
  console.error(`\n  Present but not calling git-lfs:`)
  for (const hook of notWired) console.error(`    - ${hook}`)
}
console.error(`
  Without pre-push, binaries are committed as pointers and the objects are
  never uploaded. The push succeeds; the next clone is broken.

  Fix:  git lfs install --force        # rewrites .git/hooks/
        cp .git/hooks/{${REQUIRED.join(',')}} ${hooksPath}/
        git add ${hooksPath} && git commit
`)
process.exit(1)
