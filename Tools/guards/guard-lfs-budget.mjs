#!/usr/bin/env node
/**
 * GUARD: the Git LFS working set stays under 400 MB, and no single object over 25 MB.
 *
 * THE DISASTER THIS PREVENTS
 * GitHub's free tier gives 1 GB of LFS storage and 1 GB of bandwidth per month.
 * One environment pack from the Asset Store exceeds both on its own. Blow the
 * bandwidth quota and every `git clone` and `git lfs pull` in the account starts
 * failing — not just this repo — until the month rolls over or someone pays.
 *
 * The part that actually bites is worse than the number:
 *
 *   LFS STORAGE IS CUMULATIVE AND IS NEVER RECLAIMED.
 *
 * Deleting a texture does not free its bytes. Re-exporting a 200 MB FBX at
 * better settings does not replace the old one — it ADDS a second 200 MB
 * object, and both are billed forever. Nothing short of `git lfs migrate` plus a
 * force-push plus a support request to purge the server side gets it back, and
 * that breaks every clone and backup that already exists. Meanwhile the working
 * tree looks tidy, `du` on the checkout looks fine, and the quota page is the
 * only place the truth lives.
 *
 * The rule that follows from that is not "keep the repo small". It is:
 *
 *   GET RESOLUTION, COMPRESSION AND IMPORT SETTINGS RIGHT BEFORE THE FIRST
 *   COMMIT OF ANY BINARY. Never commit a texture, model or sound you already
 *   intend to re-export. Iterate on it OUTSIDE the repo, commit once.
 *
 * WHAT THIS MEASURES, AND WHAT IT CANNOT. `git lfs ls-files` lists the objects
 * in the CURRENT checkout. The bill is every version ever pushed, so this total
 * is a LOWER BOUND that only ever understates the real figure. That asymmetry is
 * deliberate: a working set that has already reached 400 MB implies a history
 * well past it, and the caps below are set to trip long before the 1 GB wall.
 * Calibration when this guard was written: 16 objects, 1.2 MB total, .git at
 * 6.9 MB. The headroom is enormous. This is a discipline problem, not a capacity
 * problem, and the guard exists so it stays one.
 *
 * THE ESCAPE HATCH, STATED HONESTLY. The cap is a budget, not a law of physics.
 * A GitHub Data Pack is $5/month for 50 GB of storage and 50 GB of bandwidth,
 * and stacks. If the project genuinely needs 3 GB of art, buy the pack and raise
 * TOTAL_CAP_BYTES here in the same commit, with a note. What must not happen is
 * drifting past the free tier by accident and finding out when a clone fails.
 *
 * A guard that crashes is a guard that gets deleted, so a missing git-lfs, a
 * missing repo, and an empty LFS set are all reported and passed rather than
 * failed — guard-lfs-binaries.mjs already fails hard on those.
 *
 * CROSS-PLATFORM NOTE: no shell redirection anywhere. stderr is silenced through
 * stdio, because execSync uses cmd.exe on Windows where `2>/dev/null` is invalid.
 *
 * Run:  node Tools/guards/guard-lfs-budget.mjs
 */
import { execSync } from 'node:child_process'

// Free-tier budget, deliberately well under the 1 GB wall so there is room to
// notice and react. Raising these is a decision to be made and written down,
// not a nuisance to be silenced — see the escape hatch above.
const TOTAL_CAP_BYTES = 400 * 1000 * 1000
const SINGLE_OBJECT_CAP_BYTES = 25 * 1000 * 1000

// A cumulative resource needs to shout before it fails, because by the time it
// fails the bytes are already in history and cannot be taken back.
const WARN_FRACTION = 0.75

// git-lfs formats sizes with SI units, not binary ones: a 634,867-byte file
// prints as "635 KB". Verified against this repo's own detail normal.
const UNITS = { B: 1, KB: 1e3, MB: 1e6, GB: 1e9, TB: 1e12 }

function git(command) {
  return execSync(command, {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'ignore'],
  })
}

try {
  git('git rev-parse --git-dir')
} catch {
  console.log('  guard-lfs-budget: skipped (not a git repository, or git not on PATH)')
  process.exit(0)
}

let listing
try {
  listing = git('git lfs ls-files --size')
} catch {
  console.log('  guard-lfs-budget: skipped (git-lfs not installed here — install it before adding any binary)')
  process.exit(0)
}

// "<oid> <* or -> <path> (<size> <unit>)". Read the size from the END of the
// line so a path containing brackets cannot confuse the match.
const SIZED_LINE = /^(\S+)\s+[-*]\s+(.*?)\s+\(([\d.]+)\s*([A-Z]+)\)\s*$/

const objects = []
const unparsed = []

for (const line of listing.split('\n')) {
  const trimmed = line.trim()
  if (trimmed === '') continue
  const match = trimmed.match(SIZED_LINE)
  const unit = match ? UNITS[match[4].toUpperCase()] : undefined
  if (!match || unit === undefined) {
    unparsed.push(trimmed)
    continue
  }
  objects.push({ path: match[2].replace(/\\/g, '/'), bytes: Number(match[3]) * unit })
}

if (objects.length === 0 && unparsed.length === 0) {
  console.log('✓ guard-lfs-budget: clean (no LFS objects yet)')
  process.exit(0)
}

const totalBytes = objects.reduce((sum, o) => sum + o.bytes, 0)
const mb = (bytes) => (bytes / 1e6).toFixed(1)

const oversized = objects.filter((o) => o.bytes > SINGLE_OBJECT_CAP_BYTES)
oversized.sort((a, b) => b.bytes - a.bytes)

let failed = false

if (totalBytes > TOTAL_CAP_BYTES) {
  failed = true
  console.error(
    `\n✖ guard-lfs-budget: LFS working set is ${mb(totalBytes)} MB, over the ${mb(TOTAL_CAP_BYTES)} MB cap.\n`
  )
  const biggest = [...objects].sort((a, b) => b.bytes - a.bytes).slice(0, 10)
  for (const { path, bytes } of biggest) {
    console.error(`   ${mb(bytes).padStart(8)} MB  ${path}`)
  }
  console.error('\n   Remember this total UNDERSTATES the bill: it counts the current checkout,')
  console.error('   and GitHub charges for every version ever pushed.')
}

if (oversized.length > 0) {
  failed = true
  console.error(
    `\n✖ guard-lfs-budget: ${oversized.length} object(s) over the ${mb(SINGLE_OBJECT_CAP_BYTES)} MB single-object cap.\n`
  )
  for (const { path, bytes } of oversized.slice(0, 15)) {
    console.error(`   ${mb(bytes).padStart(8)} MB  ${path}`)
  }
  if (oversized.length > 15) console.error(`   ...and ${oversized.length - 15} more`)
}

if (failed) {
  console.error('\n   Fix, in order of preference:')
  console.error('     1. If NOT yet committed: fix it now. Re-export the source at a sane')
  console.error('        resolution or compression and commit the file exactly once.')
  console.error('     2. If it is a bought pack: import only the folders actually used.')
  console.error('        Delete the demo scenes, 4K variants and source PSDs BEFORE `git add`.')
  console.error('     3. If already pushed: the bytes are billed forever. `git lfs migrate`')
  console.error('        plus a force-push rewrites history and breaks every existing clone —')
  console.error('        decide deliberately, do not do it reflexively.')
  console.error('     4. If the project genuinely needs the space: a GitHub Data Pack is')
  console.error('        $5/month per 50 GB. Buy it and raise the cap in this file, with a note.\n')
  process.exit(1)
}

if (unparsed.length > 0) {
  console.log(`  guard-lfs-budget: ${unparsed.length} line(s) from git-lfs had no readable size — total is understated.`)
}

const warnAt = TOTAL_CAP_BYTES * WARN_FRACTION
if (totalBytes > warnAt) {
  console.log(
    `  guard-lfs-budget: WARNING — ${mb(totalBytes)} MB of ${mb(TOTAL_CAP_BYTES)} MB used. ` +
      'Every byte added from here is permanent.'
  )
}

const percent = ((totalBytes / TOTAL_CAP_BYTES) * 100).toFixed(1)
console.log(
  `✓ guard-lfs-budget: clean (${objects.length} object(s), ${mb(totalBytes)} MB of ${mb(TOTAL_CAP_BYTES)} MB — ${percent}%)`
)
