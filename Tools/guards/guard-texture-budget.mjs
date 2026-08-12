#!/usr/bin/env node
/**
 * GUARD: no first-party texture imports above 1024 (2048 for weapons and hands).
 *
 * THE DISASTER THIS PREVENTS
 * The hardware target is an RTX 3050 Laptop with 4 GB of VRAM, and VRAM is
 * spent on textures, not geometry. Do the arithmetic once and the rule stops
 * feeling arbitrary:
 *
 *   A 4096x4096 BC7 texture is 16 MB on the card; 21 MB once mip maps are
 *   generated. Twenty albedo/normal pairs — forty textures, which is one
 *   modest environment pack — is ~850-900 MB resident. A QUARTER OF THE CARD,
 *   spent on detail that is invisible at 1080p on a 3 m crate the player runs
 *   past. The same forty at 1024 is ~43-56 MB depending on format.
 *
 *   Texture memory scales with AREA. Halving the longest edge quarters the
 *   cost, every time. 4096 -> 1024 is 16x less memory for detail nobody can
 *   resolve at this camera distance.
 *
 * The trap is that NOTHING GOES WRONG UNTIL IT ALL GOES WRONG. Import a pack
 * at its authored 4K, and in the editor everything looks fine — the editor
 * machine has headroom and Unity streams what it can. The failure lands in the
 * built player on the target laptop, as a stutter every time a new drone type
 * first appears on screen, or as a hard allocation failure minutes into a run.
 * By then the pack is committed, its 4K source is in LFS history forever
 * (see guard-lfs-budget.mjs), and "just re-import at 1024" means re-uploading
 * every byte.
 *
 * A bought pack ships at 4K because the author cannot know your camera. Setting
 * Max Size at import is a ten-second job. Discovering it after shipping is not.
 *
 * WHAT COUNTS AS THE IMPORTED SIZE — this is the part that is easy to get wrong.
 * A texture .meta carries a legacy top-level `maxTextureSize` AND a
 * `platformSettings` list. The list's `DefaultTexturePlatform` entry is what the
 * Inspector's Default tab edits and is authoritative when present; the top-level
 * field is stale in hand-written metas (`maxTextureSizeSet: 0` marks it as never
 * authored) and is only used here as a fallback. Every OTHER entry is a
 * per-platform override and applies only when its `overridden` flag is 1 — an
 * inactive entry is dead data Unity ignores. So a Standalone entry at 4096 with
 * `overridden: 1` FAILS even though the default says 1024, because Standalone is
 * the only platform this game ships on.
 *
 * Bought packs under Assets/ThirdParty are reported but never failed — they are
 * not ours to fix, matching guard-meta-files.mjs. Their VRAM is just as real, so
 * the count is printed rather than swallowed.
 *
 * CROSS-PLATFORM NOTE: no shell redirection anywhere, same as the other guards.
 *
 * Run:  node Tools/guards/guard-texture-budget.mjs
 */
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

const ASSETS_ROOT = 'Assets'

// Project-wide ceiling, from CLAUDE.md. Not a tuning number in the Unity sense
// (no ScriptableObject can hold a build-time policy) — it is the budget itself.
const MAX_SIZE = 1024

// Viewmodel textures fill a third of the screen and are the one thing the player
// stares at for the whole run, so they earn the extra mip. Nothing else does.
const HIGH_DETAIL_MAX_SIZE = 2048

// Path SEGMENTS, not prefixes: a weapon texture is allowed to live at
// Art/Textures/Weapons/, Art/Weapons/AR_Standard/, or Prefabs/Viewmodel/ without
// this guard needing an edit. Folder layout is not the rule; intent is.
const HIGH_DETAIL_SEGMENTS = new Set(['weapons', 'weapon', 'hands', 'viewmodel', 'arms'])

// Bought packs are not ours to fix; report as skipped, not as failures.
const THIRD_PARTY = ['Assets/ThirdParty', 'Assets/Plugins', 'Assets/AssetStoreTools']

const isThirdParty = (path) => THIRD_PARTY.some((prefix) => path.startsWith(prefix))

const limitFor = (path) =>
  path.split('/').some((segment) => HIGH_DETAIL_SEGMENTS.has(segment.toLowerCase()))
    ? HIGH_DETAIL_MAX_SIZE
    : MAX_SIZE

function walk(directory, out = []) {
  let entries
  try {
    entries = readdirSync(directory)
  } catch {
    return out
  }
  for (const entry of entries) {
    if (entry.startsWith('.') || entry.endsWith('~')) continue
    const fullPath = join(directory, entry).replace(/\\/g, '/')
    let stats
    try {
      stats = statSync(fullPath)
    } catch {
      continue
    }
    if (stats.isDirectory()) walk(fullPath, out)
    else if (entry.endsWith('.meta')) out.push(fullPath)
  }
  return out
}

/**
 * Enough of a .meta to answer one question. Deliberately not a YAML parser:
 * these guards take no npm dependencies, and the shape below is fixed by
 * Unity's own serializer.
 *
 * Indentation is the whole grammar. TextureImporter's own keys sit at two
 * spaces; a platformSettings entry opens with "  - " and its keys sit at four.
 * Any other two-space key therefore ends the list.
 */
function parseTextureImporter(text) {
  if (!text.includes('TextureImporter:')) return null

  let topLevelMaxSize = null
  let inPlatformSettings = false
  let current = null
  const platforms = []

  for (const line of text.split('\n')) {
    if (/^ {2}platformSettings:/.test(line)) {
      inPlatformSettings = true
      continue
    }
    // "  - " is a list item, not a key, so \w correctly leaves the list open.
    if (/^ {2}\w/.test(line)) inPlatformSettings = false

    if (!inPlatformSettings) {
      const match = line.match(/^ {2}maxTextureSize:\s*(\d+)/)
      if (match) topLevelMaxSize = Number(match[1])
      continue
    }

    if (/^ {2}- /.test(line)) {
      current = { buildTarget: '(unnamed)', maxSize: null, overridden: 0 }
      platforms.push(current)
    }
    if (current === null) continue

    const buildTarget = line.match(/^\s+buildTarget:\s*(\S+)/)
    if (buildTarget) current.buildTarget = buildTarget[1]
    const maxSize = line.match(/^\s+maxTextureSize:\s*(\d+)/)
    if (maxSize) current.maxSize = Number(maxSize[1])
    const overridden = line.match(/^\s+overridden:\s*(\d+)/)
    if (overridden) current.overridden = Number(overridden[1])
  }

  const fallback = platforms.find((p) => p.buildTarget === 'DefaultTexturePlatform')
  const defaultMaxSize = fallback?.maxSize ?? topLevelMaxSize
  const active = platforms.filter(
    (p) => p.buildTarget !== 'DefaultTexturePlatform' && p.overridden === 1 && p.maxSize !== null
  )
  return { defaultMaxSize, active }
}

// ---------- scan ----------

const metas = walk(ASSETS_ROOT)
if (metas.length === 0) {
  console.error(`guard-texture-budget: no ${ASSETS_ROOT}/ folder found — run from the project root`)
  process.exit(1)
}

const offenders = []
const thirdPartyOverBudget = []
let checked = 0
let unreadable = 0
let largestSeen = 0

for (const meta of metas) {
  let text
  try {
    text = readFileSync(meta, 'utf8')
  } catch {
    continue
  }

  const importer = parseTextureImporter(text)
  if (importer === null) continue

  const asset = meta.slice(0, -'.meta'.length)
  if (importer.defaultMaxSize === null && importer.active.length === 0) {
    unreadable++
    continue
  }
  checked++

  const limit = limitFor(asset)
  const failures = []
  if (importer.defaultMaxSize !== null) {
    largestSeen = Math.max(largestSeen, importer.defaultMaxSize)
    if (importer.defaultMaxSize > limit) failures.push(`default ${importer.defaultMaxSize}`)
  }
  for (const platform of importer.active) {
    largestSeen = Math.max(largestSeen, platform.maxSize)
    if (platform.maxSize > limit) failures.push(`${platform.buildTarget} override ${platform.maxSize}`)
  }
  if (failures.length === 0) continue

  const record = { asset, limit, detail: failures.join(', ') }
  if (isThirdParty(asset)) thirdPartyOverBudget.push(record)
  else offenders.push(record)
}

// ---------- report ----------

if (thirdPartyOverBudget.length > 0) {
  console.log(
    `  guard-texture-budget: ${thirdPartyOverBudget.length} texture(s) over budget in bought packs — not failed, but they`
  )
  console.log('  spend the same VRAM. Override Max Size on import rather than editing the pack in place.')
}

if (offenders.length > 0) {
  console.error('\n✖ guard-texture-budget: textures imported above the VRAM budget.\n')
  for (const { asset, limit, detail } of offenders.slice(0, 25)) {
    console.error(`   ${asset}\n      limit ${limit}, found ${detail}`)
  }
  if (offenders.length > 25) console.error(`   ...and ${offenders.length - 25} more`)
  console.error('\n   Every doubling of Max Size costs 4x the VRAM on a 4 GB card.')
  console.error('\n   Fix:')
  console.error('     1. Select the texture. In the Inspector, set Max Size to 1024 on the')
  console.error('        DEFAULT tab — and clear any per-platform override you do not need.')
  console.error('     2. A weapon or hands texture may sit at 2048: put it under a folder')
  console.error('        named Weapons/, Hands/ or Viewmodel/ and this guard allows it.')
  console.error('     3. Do this BEFORE committing the file. The .png is a Git LFS object;')
  console.error('        re-exporting later leaves the 4K original in history forever.\n')
  process.exit(1)
}

const skipped = unreadable > 0 ? `, ${unreadable} with no readable size` : ''
console.log(`✓ guard-texture-budget: clean (${checked} texture(s), largest import size ${largestSeen}${skipped})`)
