#!/usr/bin/env node
/**
 * GUARD: no mutable static fields or static events in first-party code.
 *
 * THE DISASTER THIS PREVENTS
 * This playbook disables Domain Reload for fast Play Mode entry (see
 * unity-setup.md). The price: static state SURVIVES between Play sessions.
 * The result is the nastiest bug class in Unity — code that works on the
 * first play and misbehaves on the second:
 *
 *   - a static event keeps last session's subscribers → handlers fire twice,
 *     then three times, then the profiler shows a mystery
 *   - a static singleton points at a destroyed object → NullReference on
 *     play #2 only, never in a fresh editor
 *   - a static score/wave counter starts from last session's value
 *
 * Nothing reproduces after an editor restart, which is exactly why it needs a
 * guard instead of debugging skill.
 *
 * Allowed without comment: `static readonly`, `const`, static methods, static
 * classes, computed static properties (`static int X => ...`).
 * If a mutable static is genuinely required, reset it in a
 * [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
 * and mark the declaration with `// guard-ok: <reason>`.
 *
 * Run:  node Tools/guards/guard-no-mutable-statics.mjs
 */
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

const ROOTS = ['Assets/_Project/Scripts']

const ALLOW = /guard-ok/i

// A line is a method/constructor signature if an identifier is immediately
// followed by '(' BEFORE any '=' — `static void Foo()` yes, but
// `static List<int> x = new();` no (its '(' comes after '=').
// The class also accepts '>' and ']': a generic method puts its type list
// right before the paren — `static T LoadOrCreate<T>(path)` — and requiring
// \w there reported every generic static helper as a mutable field. The '='
// exclusion is what still catches `static List<int> x = new();`.
const LOOKS_LIKE_METHOD = /\bstatic\b[^=;{]*[\w>\]]\s*\(/

// Computed property: `static int X => ...` — read-only, allowed. Detected as
// '=>' with no plain assignment '=' before it.
function isComputedProperty(code) {
  const arrow = code.indexOf('=>')
  if (arrow === -1) return false
  const beforeArrow = code.slice(0, arrow)
  return !/[^=!<>]=(?!=)/.test(beforeArrow)
}

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

const files = ROOTS.flatMap((root) => collectCsFiles(root))

if (files.length === 0) {
  console.log('✓ guard-no-mutable-statics: no first-party scripts yet — skipping')
  process.exit(0)
}

const violations = []

for (const file of files) {
  // Split on /\r?\n/, not '\n': a CRLF file leaves a trailing '\r' on every
  // line, and that silently breaks the comment stripping below — `.` will not
  // cross '\r', and '$' without the /m/ flag only matches end-of-string, so
  // `// ... static ...` never gets stripped. The symptom is a false positive
  // on a COMMENT that merely mentions the pattern, on Windows only.
  const lines = readFileSync(file, 'utf8').split(/\r?\n/)
  for (let index = 0; index < lines.length; index++) {
    const raw = lines[index]
    if (!raw || ALLOW.test(raw)) continue
    const code = raw.replace(/\/\/.*$/, '').replace(/"(?:\\.|[^"\\])*"/g, '""').trim()
    if (!code || code.startsWith('*') || code.startsWith('/*') || code.startsWith('#')) continue
    if (!/\bstatic\b/.test(code)) continue

    if (/\bstatic\s+(readonly|class|struct|record)\b/.test(code)) continue
    if (/\busing\s+static\b/.test(code)) continue
    if (LOOKS_LIKE_METHOD.test(code)) continue
    if (isComputedProperty(code)) continue

    // What remains: static fields, static events, static auto-properties with
    // a setter — all of them survive Play sessions with Domain Reload off.
    const kind = /\bevent\b/.test(code)
      ? 'static event (keeps last session\'s subscribers)'
      : /\{[^}]*set/.test(code)
        ? 'static settable property'
        : 'mutable static field'

    violations.push({ file, line: index + 1, kind, code: code.slice(0, 70) })
  }
}

if (violations.length > 0) {
  console.error('\n✖ guard-no-mutable-statics: static state that survives Play sessions.\n')
  for (const violation of violations.slice(0, 25)) {
    console.error(`   ${violation.file}:${violation.line}  ${violation.kind}`)
    console.error(`      ${violation.code}`)
  }
  if (violations.length > 25) console.error(`   ...and ${violations.length - 25} more`)
  console.error('\n   Domain Reload is off in this project, so these keep their values')
  console.error('   between plays — bugs that appear on the SECOND play only.')
  console.error('   Prefer instance state, `static readonly`, or `const`. If a mutable')
  console.error('   static is unavoidable, reset it in a [RuntimeInitializeOnLoadMethod]')
  console.error('   and mark the line with // guard-ok: <reason>.\n')
  process.exit(1)
}

console.log(`✓ guard-no-mutable-statics: clean (${files.length} scripts checked)`)
