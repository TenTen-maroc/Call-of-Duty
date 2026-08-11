#!/usr/bin/env node
/**
 * Builds the Windows player and PROVES IT RUNS, outside the editor.
 *
 * WHY THIS EXISTS
 * Every other gate in this repo runs inside the editor. typecheck.mjs compiles,
 * the guards read source, and both test suites run in an editor process with
 * every editor-only code path live. A build can pass all of that and still fail
 * on its own terms:
 *
 *   - a scene missing from Build Settings (the editor plays it anyway)
 *   - a `#if UNITY_EDITOR` block that was silently holding something up
 *   - an asset only reachable through AssetDatabase, which does not ship
 *   - managed stripping removing something reflection reached
 *
 * The first run of this script found a real one that no editor gate could: the
 * run record and the settings each loaded their OWN SaveData, so ending a run
 * wrote the whole file and reverted every setting to un-chosen. It was visible
 * only by reading the save a built player produced.
 *
 * WHAT IT DOES
 *   1. Builds the player headlessly (release by default, --dev for the other).
 *   2. Launches the .exe with -codSmokeTest -batchmode -nographics.
 *      BuildSmokeTest boots, waits for the menu, loads the arena, counts every
 *      error and exception, and quits 0 or 1.
 *   3. Greps the player log for the pass marker and reports.
 *
 * Unity LOCKS the project — close the editor before running this.
 *
 * Run:  node Tools/verify-build.mjs          (release)
 *       node Tools/verify-build.mjs --dev    (development: exercises the
 *                                             DEVELOPMENT_BUILD cheat gate)
 */
import { execFileSync, spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const development = process.argv.includes('--dev')

// These three strings are duplicated from BuildSmokeTest.cs and GameBuilder.cs
// on purpose: a Node script cannot reference a C# const. If you change them
// there, change them here — the mismatch shows up as a failed gate, not as a
// silent pass, because a missing marker is a failure.
const PASS_MARKER = 'COD_SMOKE_OK'
const FAIL_MARKER = 'COD_SMOKE_FAIL'
const EXECUTABLE = 'CallOfDuty.exe'

const outputDirectory = join(repoRoot, 'Build', development ? 'Windows-Dev' : 'Windows')
const executable = join(outputDirectory, EXECUTABLE)
const buildLog = join(repoRoot, 'Logs', development ? 'player-build-dev.log' : 'player-build-release.log')
const smokeLog = join(repoRoot, 'Logs', development ? 'player-smoke-dev.log' : 'player-smoke-release.log')
const method = development
  ? 'CoD.EditorTools.GameBuilder.BuildWindowsDevelopmentHeadless'
  : 'CoD.EditorTools.GameBuilder.BuildWindowsHeadless'

function projectEditorVersion() {
  const file = join(repoRoot, 'ProjectSettings', 'ProjectVersion.txt')
  if (!existsSync(file)) return null
  const match = readFileSync(file, 'utf8').match(/m_EditorVersion:\s*(\S+)/)
  return match ? match[1] : null
}

function findUnity() {
  const version = projectEditorVersion()
  const roots = [
    process.env.UNITY_EDITOR_ROOT,
    'C:/Program Files/Unity/Hub/Editor',
    'C:/Program Files/Unity/Editor',
  ].filter(Boolean)

  for (const root of roots) {
    if (!version) continue
    const candidate = join(root, version, 'Editor', 'Unity.exe')
    if (existsSync(candidate)) return candidate
  }
  return null
}

const unity = findUnity()
if (unity === null) {
  console.error(`verify-build: no Unity ${projectEditorVersion()} found. Set UNITY_EDITOR_ROOT.`)
  process.exit(1)
}

mkdirSync(join(repoRoot, 'Logs'), { recursive: true })

// A stale executable that fails to rebuild would otherwise be smoke-tested
// happily, and the gate would pass on last week's binary.
if (existsSync(outputDirectory)) rmSync(outputDirectory, { recursive: true, force: true })

console.log(`verify-build: building the ${development ? 'development' : 'release'} player...`)
try {
  execFileSync(unity, [
    '-batchmode', '-quit', '-projectPath', repoRoot,
    '-executeMethod', method,
    '-logFile', buildLog,
  ], { stdio: 'inherit' })
} catch {
  console.error(`verify-build: the player build FAILED. See ${buildLog}`)
  process.exit(1)
}

if (!existsSync(executable)) {
  console.error(`verify-build: the build reported success but ${executable} does not exist.`)
  process.exit(1)
}

console.log('verify-build: launching the built player with the smoke test...')
// -nographics because this runs on the dev machine; a real window would grab
// the screen. The smoke test quits the player itself, so no timeout wrapper is
// needed — but spawnSync's timeout is the backstop if it ever hangs.
const run = spawnSync(executable, [
  '-codSmokeTest', '-batchmode', '-nographics', '-logFile', smokeLog,
], { timeout: 300_000, stdio: 'ignore' })

const log = existsSync(smokeLog) ? readFileSync(smokeLog, 'utf8') : ''
const passed = log.includes(PASS_MARKER)
const failed = log.includes(FAIL_MARKER)

if (run.status !== 0 || failed || !passed) {
  console.error(`verify-build: FAILED (exit ${run.status}, pass marker ${passed}, fail marker ${failed})`)
  // The errors themselves, not just the verdict — a gate that only says "no"
  // costs a debugging round every time it fires.
  for (const line of log.split('\n')) {
    if (/error|exception|COD_SMOKE/i.test(line)) console.error('    ' + line.trim())
  }
  console.error(`    full log: ${smokeLog}`)
  process.exit(1)
}

console.log(`\n✓ verify-build: the ${development ? 'development' : 'release'} player booted, reached the menu,`)
console.log(`  loaded the arena and logged no errors. ${executable}\n`)
