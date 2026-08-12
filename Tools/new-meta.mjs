#!/usr/bin/env node
/**
 * Create Unity .meta files for assets, without opening Unity.
 *
 * WHY THIS EXISTS
 * guard-meta-files.mjs fails if any asset lacks a .meta sibling, on disk or in
 * git — for good reason: a missing .meta means Unity mints a fresh GUID on
 * import and every reference to that asset silently breaks. Normally the editor
 * writes the .meta. But this project is built headlessly by GreyBoxBuilder and
 * a great deal of work lands as new .cs files with the editor closed, so the
 * guard would fail on every one of them until somebody opened Unity.
 *
 * A .cs/.asmdef/folder meta is two lines and a GUID. Unity accepts a
 * hand-written one and will not rewrite it. Anything with import settings
 * (textures, models, audio) is NOT handled here on purpose — those need real
 * importer blocks, so let Unity generate them.
 *
 * Run:  node Tools/new-meta.mjs <path> [<path>...]
 *       node Tools/new-meta.mjs --all      # every .cs/.asmdef/folder missing one
 */
import { randomBytes } from 'node:crypto'
import { existsSync, readdirSync, statSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

// Extensions whose meta is pure identity — no importer settings to get wrong.
const SAFE = ['.cs', '.asmdef', '.asmref', '.md', '.txt', '.json', '.inputactions']

const guid = () => randomBytes(16).toString('hex')

/** Folders carry `folderAsset: yes`; files do not. Unity rejects the wrong one. */
function metaBody(isFolder) {
  const head = `fileFormatVersion: 2\nguid: ${guid()}`
  // No trailing newline: matches every .meta Unity has written in this repo,
  // so `git diff` stays empty when the editor later touches the file.
  return isFolder
    ? `${head}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: `
    : `${head}\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: `
}

function create(path) {
  const metaPath = `${path}.meta`
  if (existsSync(metaPath)) return false
  if (!existsSync(path)) {
    console.error(`  ! no such asset: ${path}`)
    process.exitCode = 1
    return false
  }
  const isFolder = statSync(path).isDirectory()
  if (!isFolder && !SAFE.some((extension) => path.endsWith(extension))) {
    console.error(`  ! ${path} needs importer settings — let Unity generate this one`)
    process.exitCode = 1
    return false
  }
  writeFileSync(metaPath, metaBody(isFolder))
  console.log(`  + ${metaPath}`)
  return true
}

function walkMissing(directory, found = []) {
  for (const entry of readdirSync(directory)) {
    if (entry.startsWith('.') || entry.endsWith('~') || entry.endsWith('.meta')) continue
    const path = `${directory}/${entry}`
    const isFolder = statSync(path).isDirectory()
    if (!existsSync(`${path}.meta`) && (isFolder || SAFE.some((e) => path.endsWith(e)))) {
      found.push(path)
    }
    if (isFolder) walkMissing(path, found)
  }
  return found
}

const args = process.argv.slice(2)
const targets = args[0] === '--all' ? walkMissing('Assets/_Project') : args
if (targets.length === 0) {
  console.log('new-meta: nothing to do.')
} else {
  let made = 0
  // Deepest-last so a new folder's meta is written before the files inside it.
  for (const path of targets.sort()) if (create(path)) made++
  console.log(`new-meta: wrote ${made} meta file(s).`)
}
