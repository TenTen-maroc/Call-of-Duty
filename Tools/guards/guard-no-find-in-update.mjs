#!/usr/bin/env node
/**
 * GUARD: no expensive lookups inside Update / FixedUpdate / LateUpdate.
 *
 * THE DISASTER THIS PREVENTS
 * GameObject.Find and FindObjectOfType walk the entire scene graph.
 * GetComponent walks the object's component list. Any of these in a per-frame
 * method costs microseconds — which is invisible with one enemy on screen, and
 * catastrophic with forty in a wave-survival mode.
 *
 * (Camera.main has been cached by Unity since 2020.2 and is cheap now; it is
 * still flagged because a serialized/cached Camera field survives tag changes,
 * keeps the dependency explicit, and keeps this guard's rule simple. It is the
 * one entry here that is about architecture, not frame time.)
 *
 * This is the single most common Unity performance defect, and it has a nasty
 * property: it never shows up during development, because development happens
 * in a test scene with three objects. It shows up at the exact moment the game
 * is finally fun — a big wave, lots of spawns — and then the framerate collapses
 * and the cause is spread across fifteen files written months apart.
 *
 * Instantiate/Destroy per frame is included here for the same reason: it is the
 * GC-hitch factory. Pool instead (see conventions.md).
 *
 * Cache in Awake, or serialize the reference. Fixing this later means auditing
 * every script; fixing it as you write is free.
 *
 * Run:  node Tools/guards/guard-no-find-in-update.mjs
 */
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

const ROOTS = ['Assets/_Project/Scripts']

const FORBIDDEN = [
  { pattern: /\bGameObject\.Find\w*\s*\(/, name: 'GameObject.Find', fix: 'serialize the reference, or cache in Awake' },
  { pattern: /\bFindObjectsOfType\s*</, name: 'FindObjectsOfType', fix: 'cache in Awake, or use a registry/manager' },
  { pattern: /\bFindObjectOfType\s*</, name: 'FindObjectOfType', fix: 'cache in Awake, or use a registry/manager' },
  { pattern: /\bFindFirstObjectByType\s*</, name: 'FindFirstObjectByType', fix: 'cache in Awake' },
  { pattern: /\bFindAnyObjectByType\s*</, name: 'FindAnyObjectByType', fix: 'cache in Awake' },
  { pattern: /\bCamera\.main\b/, name: 'Camera.main', fix: 'cache a Camera field in Awake' },
  { pattern: /\bGetComponent(InChildren|InParent)?\s*</, name: 'GetComponent', fix: 'cache the component in Awake' },
  { pattern: /\bAddComponent\s*</, name: 'AddComponent', fix: 'add it on the prefab instead' },
  { pattern: /\bObject\.Instantiate\s*\(|(?<![.\w])Instantiate\s*\(/, name: 'Instantiate', fix: 'take from the object pool' },
  { pattern: /(?<![.\w])Destroy\s*\(/, name: 'Destroy', fix: 'return to the object pool' },
]

const PER_FRAME_METHOD =
  /\b(?:private|public|protected|internal)?\s*(?:override\s+)?void\s+(Update|FixedUpdate|LateUpdate)\s*\(\s*\)/

// Lines explicitly acknowledging the rule are allowed through.
const ALLOW = /guard-ok|GUARD-OK|one-time|cached above|editor only/i

function collectCsFiles(directory, out = []) {
  let entries
  try {
    entries = readdirSync(directory)
  } catch {
    return out
  }
  for (const entry of entries) {
    const fullPath = join(directory, entry).replace(/\\/g, '/')
    const stats = statSync(fullPath)
    if (stats.isDirectory()) collectCsFiles(fullPath, out)
    else if (entry.endsWith('.cs')) out.push(fullPath)
  }
  return out
}

/** Returns the [start, end] line ranges of per-frame method bodies. */
function perFrameRanges(lines) {
  const ranges = []
  for (let i = 0; i < lines.length; i++) {
    const match = lines[i].match(PER_FRAME_METHOD)
    if (!match) continue

    // Expression-bodied: `void Update() => Foo();` — the body is the single
    // statement up to the terminating ';'. A previous version skipped these
    // entirely, which was a false negative.
    if (lines[i].includes('=>')) {
      let end = i
      for (let j = i; j < Math.min(lines.length, i + 6); j++) {
        end = j
        if (lines[j].includes(';')) break
      }
      ranges.push({ method: match[1], start: i, end })
      i = end
      continue
    }

    // Find the opening brace (same line or the next non-empty one)
    let cursor = i
    while (cursor < lines.length && !lines[cursor].includes('{')) {
      if (lines[cursor].includes(';')) break // abstract/partial declaration; skip
      cursor++
    }
    if (cursor >= lines.length || !lines[cursor].includes('{')) continue

    let depth = 0
    let started = false
    let end = cursor
    for (let j = cursor; j < lines.length; j++) {
      const stripped = lines[j].replace(/"(?:\\.|[^"\\])*"/g, '""').replace(/\/\/.*$/, '')
      for (const character of stripped) {
        if (character === '{') {
          depth++
          started = true
        } else if (character === '}') depth--
      }
      if (started && depth <= 0) {
        end = j
        break
      }
      end = j
    }
    ranges.push({ method: match[1], start: cursor, end })
    i = end
  }
  return ranges
}

const files = ROOTS.flatMap((root) => collectCsFiles(root))

if (files.length === 0) {
  console.log('✓ guard-no-find-in-update: no first-party scripts yet — skipping')
  process.exit(0)
}

const violations = []

for (const file of files) {
  const lines = readFileSync(file, 'utf8').split('\n')
  for (const { method, start, end } of perFrameRanges(lines)) {
    for (let lineIndex = start; lineIndex <= end; lineIndex++) {
      const line = lines[lineIndex]
      if (!line) continue
      const code = line.replace(/\/\/.*$/, '').trim()
      if (!code || code.startsWith('*') || code.startsWith('/*')) continue
      if (ALLOW.test(line)) continue
      for (const { pattern, name, fix } of FORBIDDEN) {
        if (pattern.test(code)) {
          violations.push({ file, line: lineIndex + 1, method, name, fix, code: code.slice(0, 70) })
          break
        }
      }
    }
  }
}

if (violations.length > 0) {
  console.error('\n✖ guard-no-find-in-update: expensive calls inside per-frame methods.\n')
  for (const violation of violations.slice(0, 25)) {
    console.error(`   ${violation.file}:${violation.line}  in ${violation.method}()`)
    console.error(`      ${violation.name} → ${violation.fix}`)
    console.error(`      ${violation.code}`)
  }
  if (violations.length > 25) console.error(`   ...and ${violations.length - 25} more`)
  console.error('\n   This is invisible with 3 objects on screen and fatal with 40.')
  console.error('   Cache in Awake, serialize the reference, or pool the spawn.')
  console.error('   If a call is genuinely safe, add a // guard-ok comment with a reason.\n')
  process.exit(1)
}

console.log(`✓ guard-no-find-in-update: clean (${files.length} scripts checked)`)
