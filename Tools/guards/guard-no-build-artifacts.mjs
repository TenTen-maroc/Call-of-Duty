#!/usr/bin/env node
/**
 * GUARD: no Unity build artifacts tracked in git.
 *
 * THE DISASTER THIS PREVENTS
 * Library/ in a working FPS project is 5-20 GB and changes on every asset
 * import. Committed once, it is in git history forever — every clone pays for
 * it, and removing it later requires a full history rewrite. On a machine with
 * ~170 GB free, a repo whose .git folder is larger than the project itself is
 * a real, immediate problem, not a theoretical one.
 *
 * Temp/ and obj/ are worse in a subtler way: Unity writes them while the editor
 * is open, so committing them produces conflicts on files no human wrote.
 *
 * Run:  node Tools/guards/guard-no-build-artifacts.mjs
 */
import { execSync } from 'node:child_process'

const FORBIDDEN_PREFIXES = [
  'Library/',
  'Temp/',
  'Obj/',
  'obj/',
  'Build/',
  'Builds/',
  'Logs/',
  'MemoryCaptures/',
  'UserSettings/',
]

const FORBIDDEN_EXTENSIONS = ['.csproj', '.sln', '.unityproj', '.suo', '.user']

let tracked
try {
  tracked = execSync('git ls-files', { encoding: 'utf8' }).split('\n').filter(Boolean)
} catch {
  console.error('guard-no-build-artifacts: not a git repository (or git not on PATH)')
  process.exit(1)
}

const offenders = tracked.filter((file) => {
  const normalized = file.replace(/\\/g, '/')
  if (FORBIDDEN_PREFIXES.some((prefix) => normalized.startsWith(prefix))) return true
  return FORBIDDEN_EXTENSIONS.some((extension) => normalized.endsWith(extension))
})

if (offenders.length > 0) {
  console.error('\n✖ guard-no-build-artifacts: generated files are tracked in git.\n')
  for (const file of offenders.slice(0, 25)) console.error(`   ${file}`)
  if (offenders.length > 25) console.error(`   ...and ${offenders.length - 25} more`)
  console.error(`\n   ${offenders.length} file(s) total.`)
  console.error('\n   Fix:')
  console.error('     1. Ensure .gitignore matches assets/gitignore.template')
  console.error('     2. git rm -r --cached Library Temp obj Build Builds Logs')
  console.error('     3. Commit the removal, then verify: git ls-files | grep Library')
  console.error('\n   If these are already deep in history, rewrite with git-filter-repo')
  console.error('   NOW, while the repo is still young. It only gets harder.\n')
  process.exit(1)
}

console.log('✓ guard-no-build-artifacts: clean')
