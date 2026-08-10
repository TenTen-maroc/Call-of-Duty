#!/usr/bin/env node
/**
 * GUARD: every asset has a .meta, on disk AND in git.
 *
 * THE DISASTER THIS PREVENTS
 * Unity stores an asset's GUID in its .meta file. Every prefab, scene, and
 * material references other assets *by GUID*, not by path. A missing .meta
 * means Unity generates a fresh GUID on import — and every reference to that
 * asset silently breaks. Materials go magenta, prefabs lose their scripts,
 * and nothing in the console explains why.
 *
 * Crucially, the damage is a GIT problem, not just a disk problem: the classic
 * root cause is a .gitignore line excluding `*.meta`, which leaves the asset
 * tracked and its meta untracked. On the machine that committed it, everything
 * works — the meta exists locally. It breaks only for the NEXT clone, i.e.
 * exactly when it hurts most. So this guard checks two layers:
 *
 *   1. git layer  — every tracked asset/folder has a TRACKED .meta sibling
 *   2. disk layer — local files and metas are 1:1 (catches deletions made
 *                   outside Unity before they ever reach a commit)
 *
 * Unity's own ignore rules are respected: paths starting with '.', ending in
 * '~', named 'cvs', or with a '.tmp' extension get no .meta by design.
 *
 * Run:  node Tools/guards/guard-meta-files.mjs
 */
import { execSync } from 'node:child_process'
import { existsSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'

const ASSETS_ROOT = 'Assets'

// Bought packs are not ours to fix; report as skipped, not as failures.
const THIRD_PARTY = ['Assets/ThirdParty', 'Assets/Plugins', 'Assets/AssetStoreTools']

function unityIgnoresName(name) {
  if (name.startsWith('.')) return true
  if (name.endsWith('~')) return true
  if (name.toLowerCase() === 'cvs') return true
  if (name.toLowerCase().endsWith('.tmp')) return true
  return false
}

const unityIgnoresPath = (path) => path.split('/').some(unityIgnoresName)
const isThirdParty = (path) => THIRD_PARTY.some((prefix) => path.startsWith(prefix))

// ---------- disk layer ----------

function walk(directory, files = [], directories = []) {
  let entries
  try {
    entries = readdirSync(directory)
  } catch {
    return { files, directories }
  }
  for (const entry of entries) {
    if (unityIgnoresName(entry)) continue
    const fullPath = join(directory, entry).replace(/\\/g, '/')
    let stats
    try {
      stats = statSync(fullPath)
    } catch {
      continue
    }
    if (stats.isDirectory()) {
      directories.push(fullPath)
      walk(fullPath, files, directories)
    } else {
      files.push(fullPath)
    }
  }
  return { files, directories }
}

const { files, directories } = walk(ASSETS_ROOT)

if (files.length === 0 && directories.length === 0) {
  console.error(`guard-meta-files: no ${ASSETS_ROOT}/ folder found — run from the project root`)
  process.exit(1)
}

const allPaths = new Set([...files, ...directories])
const diskMetas = new Set(files.filter((file) => file.endsWith('.meta')))

const diskMissingMeta = []
const diskOrphanMeta = []

for (const path of allPaths) {
  if (path.endsWith('.meta')) continue
  if (isThirdParty(path)) continue
  if (!diskMetas.has(`${path}.meta`)) diskMissingMeta.push(path)
}
for (const meta of diskMetas) {
  if (isThirdParty(meta)) continue
  const asset = meta.slice(0, -'.meta'.length)
  if (!allPaths.has(asset)) diskOrphanMeta.push(meta)
}

// ---------- git layer ----------

let gitTracked = null
try {
  const output = execSync('git ls-files -- Assets', {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'ignore'],
  })
  gitTracked = new Set(output.split('\n').filter(Boolean).map((p) => p.replace(/\\/g, '/')))
} catch {
  gitTracked = null // not a repo — disk layer alone still runs
}

const gitMissingMeta = []
const gitOrphanMeta = []

if (gitTracked && gitTracked.size > 0) {
  // Folders are implicit in git; derive every ancestor directory of tracked files.
  const trackedDirs = new Set()
  for (const file of gitTracked) {
    const parts = file.split('/')
    for (let depth = 2; depth < parts.length; depth++) {
      trackedDirs.add(parts.slice(0, depth).join('/'))
    }
  }

  for (const file of gitTracked) {
    if (file.endsWith('.meta')) continue
    if (isThirdParty(file) || unityIgnoresPath(file)) continue
    if (!gitTracked.has(`${file}.meta`)) gitMissingMeta.push(file)
  }
  for (const dir of trackedDirs) {
    if (isThirdParty(dir) || unityIgnoresPath(dir)) continue
    if (!gitTracked.has(`${dir}.meta`)) gitMissingMeta.push(`${dir}/`)
  }
  for (const meta of gitTracked) {
    if (!meta.endsWith('.meta')) continue
    if (isThirdParty(meta) || unityIgnoresPath(meta)) continue
    const asset = meta.slice(0, -'.meta'.length)
    const assetKnownToGit = gitTracked.has(asset) || trackedDirs.has(asset)
    if (!assetKnownToGit && !existsSync(asset)) gitOrphanMeta.push(meta)
  }
}

// ---------- report ----------

let failed = false

function report(title, items, fix) {
  if (items.length === 0) return
  failed = true
  console.error(`\n✖ guard-meta-files: ${title}\n`)
  for (const item of items.slice(0, 20)) console.error(`   ${item}`)
  if (items.length > 20) console.error(`   ...and ${items.length - 20} more`)
  console.error(`\n   ${fix}\n`)
}

report(
  'tracked in git WITHOUT a tracked .meta — breaks every other clone.',
  gitMissingMeta,
  'Root cause is almost always a .gitignore line excluding *.meta — check it,\n' +
    '   then: git add <path>.meta  (open the project in Unity first if the meta\n' +
    '   is missing on disk too).'
)
report(
  '.meta tracked in git with no matching tracked asset.',
  gitOrphanMeta,
  'Fix: git rm <file>.meta — the asset was removed without its meta.'
)
report(
  'assets on disk without a .meta file.',
  diskMissingMeta,
  'Fix: open the project in Unity (it regenerates missing metas), then commit\n' +
    '   the new .meta files.'
)
report(
  '.meta files on disk with no matching asset.',
  diskOrphanMeta,
  'Fix: delete these .meta files. An asset was removed outside Unity (file\n' +
    '   explorer, git checkout, or an editor that does not understand Unity).'
)

if (failed) process.exit(1)

const checkedCount = allPaths.size - diskMetas.size
const gitNote = gitTracked ? `, git cross-check on ${gitTracked.size} tracked paths` : ', no git repo — disk only'
console.log(`✓ guard-meta-files: clean (${checkedCount} assets on disk${gitNote})`)
