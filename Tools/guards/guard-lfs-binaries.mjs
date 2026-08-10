#!/usr/bin/env node
/**
 * GUARD: binary assets go through Git LFS, not straight into git.
 *
 * THE DISASTER THIS PREVENTS
 * Git stores a full copy of every version of every binary. A 40 MB FBX edited
 * ten times is 400 MB of history that can never be reclaimed without rewriting
 * every commit. Game projects hit this fast: one weapon pack, one audio
 * library, and the repo is unusable.
 *
 * The trap is that it is completely silent. Nothing warns you. The repo just
 * gets slower, then clones start taking twenty minutes, and by the time it is
 * noticed the history is already poisoned — and rewriting history breaks every
 * clone and backup that already exists.
 *
 * Set up LFS BEFORE the first commit (assets/gitattributes.template), and let
 * this guard verify it stayed set up.
 *
 * DETECTION IS THREE-LAYERED, and the third layer exists because the first two
 * missed a real case: a 900 KB Wwise .wem went into git as a raw blob and this
 * guard reported "clean". An extension allowlist only catches binary types
 * someone already thought of, and the 5 MB catch-all misses everything smaller.
 * Ten revisions of that soundbank is 9 MB of history nobody can reclaim.
 *   1. known binary extension        (the allowlist below)
 *   2. any file over the hard limit  (whatever its extension)
 *   3. binary CONTENT over 64 KB     (catches unlisted/new asset types)
 *
 * CROSS-PLATFORM NOTE: this file deliberately contains no shell redirection
 * (`2>/dev/null`, `|| true`). execSync uses cmd.exe on Windows, where those
 * are invalid — a previous version of this guard failed on every run on
 * Windows because of exactly that. stderr is silenced via stdio instead.
 *
 * Run:  node Tools/guards/guard-lfs-binaries.mjs
 */
import { execSync } from 'node:child_process'
import { statSync, openSync, readSync, closeSync } from 'node:fs'

const LFS_EXTENSIONS = new Set([
  'fbx', 'obj', 'blend', 'dae', '3ds',
  'png', 'jpg', 'jpeg', 'tga', 'tif', 'tiff', 'psd', 'exr', 'hdr', 'bmp', 'gif', 'cubemap',
  'wav', 'mp3', 'ogg', 'aif', 'aiff',
  'mp4', 'mov', 'webm',
  'ttf', 'otf', 'zip', '7z', 'dll',
])

// Anything above this that is not in LFS is a problem regardless of extension.
const HARD_SIZE_LIMIT_BYTES = 5 * 1024 * 1024

// A file this size with binary CONTENT is an asset, whatever its extension.
// Catches new/unlisted binary types (.wem soundbanks, .pak, .bytes) that are
// under the hard limit and therefore invisible to the two checks above.
const BINARY_CONTENT_MIN_BYTES = 64 * 1024

// git's own heuristic: a NUL byte in the first 8 KB means binary.
function looksBinary(path) {
  let fd
  try {
    fd = openSync(path, 'r')
    const buf = Buffer.alloc(8192)
    const bytes = readSync(fd, buf, 0, 8192, 0)
    return buf.subarray(0, bytes).includes(0)
  } catch {
    return false
  } finally {
    if (fd !== undefined) {
      try { closeSync(fd) } catch { /* already gone */ }
    }
  }
}

function git(command) {
  return execSync(command, {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'ignore'], // silence stderr portably
  })
}

let tracked
try {
  tracked = git('git ls-files').split('\n').filter(Boolean)
} catch {
  console.error('guard-lfs-binaries: not a git repository (or git not on PATH)')
  process.exit(1)
}

let lfsInstalled = true
let lfsTracked = new Set()
try {
  const lfsOutput = git('git lfs ls-files -n')
  lfsTracked = new Set(lfsOutput.split('\n').filter(Boolean).map((p) => p.replace(/\\/g, '/')))
} catch {
  lfsInstalled = false
}

const offenders = []

for (const file of tracked) {
  const normalized = file.replace(/\\/g, '/')
  if (lfsTracked.has(normalized)) continue

  const extension = normalized.split('.').pop()?.toLowerCase() ?? ''
  let size = 0
  try {
    size = statSync(normalized).size
  } catch {
    continue // tracked but not on disk in this checkout
  }

  const isBinaryType = LFS_EXTENSIONS.has(extension)
  const isTooBig = size > HARD_SIZE_LIMIT_BYTES
  const isBinaryContent =
    !isBinaryType && size > BINARY_CONTENT_MIN_BYTES && looksBinary(normalized)

  if (isBinaryType || isTooBig || isBinaryContent) {
    const reason = isBinaryType ? 'binary type' : isTooBig ? 'oversize' : 'binary content'
    offenders.push({ file: normalized, size, reason })
  }
}

if (offenders.length > 0) {
  offenders.sort((a, b) => b.size - a.size)
  console.error('\n✖ guard-lfs-binaries: binaries tracked outside Git LFS.\n')
  for (const { file, size, reason } of offenders.slice(0, 25)) {
    const mb = (size / 1024 / 1024).toFixed(1)
    console.error(`   ${mb.padStart(7)} MB  ${file}  (${reason})`)
  }
  if (offenders.length > 25) console.error(`   ...and ${offenders.length - 25} more`)
  const totalMb = (offenders.reduce((sum, o) => sum + o.size, 0) / 1024 / 1024).toFixed(1)
  console.error(`\n   ${offenders.length} file(s), ${totalMb} MB going into git history uncompressed.`)
  if (!lfsInstalled) {
    console.error('\n   git-lfs was NOT detected on this machine. Install it first:')
    console.error('     git lfs install')
  }
  console.error('\n   Fix (do it now — this only gets more expensive):')
  console.error('     1. git lfs install')
  console.error('     2. Ensure .gitattributes matches assets/gitattributes.template')
  console.error('     3. git rm --cached <file> && git add <file>   (re-add through LFS)')
  console.error('     4. If already committed in earlier commits:')
  console.error('        git lfs migrate import --include="*.fbx,*.png,*.wav" --everything\n')
  process.exit(1)
}

if (!lfsInstalled) {
  console.log('✓ guard-lfs-binaries: clean (warning: git-lfs not detected — install it before adding any binary)')
} else {
  console.log(`✓ guard-lfs-binaries: clean (${lfsTracked.size} files in LFS)`)
}
