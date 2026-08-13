#!/usr/bin/env node
/**
 * Builds the DEVELOPMENT Windows player, RUNS IT WITH A WINDOW, and saves PNGs.
 *
 * WHY THIS EXISTS
 * Every automated thing in this repo is blind. typecheck.mjs compiles, the
 * guards read source, both test suites run in an editor process, and
 * verify-build.mjs — the one gate that leaves the editor — launches the player
 * with -nographics, which does almost no GPU work and renders nothing at all.
 * So the only way anyone found out what this game LOOKED like was to open Unity
 * and press Play. A HUD that clips, a palette that went muddy, a post-processing
 * setting that blew out the whites: none of it was reviewable by anything but a
 * human with the editor open, which on a solo project means it was reviewable
 * about once a week.
 *
 * This runs the real player, renders real frames, and writes PNGs a caller can
 * open. It is the same shape as verify-build.mjs on purpose — same Unity lookup,
 * same stale-build deletion, same -executeMethod build, same log scrape — and
 * deliberately not a second way of doing all of that.
 *
 * WHAT IT PROVES, AND WHAT IT DOES NOT
 * It proves the game RENDERS, and it shows exactly WHAT it renders: the menu,
 * the arena on load, the HUD, a wave in progress. A frame that comes back black,
 * or with the objective list off the bottom of the screen, is a real defect
 * caught by a machine.
 *
 * It proves NOTHING about frame rate. One machine's timing in a scripted run,
 * with the harness's own capture stalls in it, is not the target laptop's frame
 * time under a real wave. Item 9 on the tuning card is still a human with a 3050.
 *
 * WHY THE DEVELOPMENT BUILD
 * The capture harness is gated on UNITY_EDITOR || DEVELOPMENT_BUILD, exactly
 * like the cheat console, because a shipped game has no business carrying one.
 * The cost is honest and worth stating: development builds paint Unity's
 * "Development Build" watermark in the bottom-right of every frame. It is in
 * every PNG this writes and there is no API to turn it off.
 *
 * Unity LOCKS the project — close the editor before running this.
 *
 * Run:  node Tools/screenshot.mjs                 (build, then endless + campaign)
 *       node Tools/screenshot.mjs --reuse         (skip the build, use Build/Windows-Dev as-is)
 *       node Tools/screenshot.mjs --no-campaign   (endless pass only)
 *       node Tools/screenshot.mjs --mission mission_02_hard_contact
 */
import { execFileSync, spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')

const reuse = process.argv.includes('--reuse')
const skipCampaign = process.argv.includes('--no-campaign')
const missionFlag = process.argv.indexOf('--mission')
const requestedMission = missionFlag !== -1 ? process.argv[missionFlag + 1] : null

// Duplicated from BuildSmokeTest.cs on purpose, exactly as verify-build.mjs
// duplicates its markers: a Node script cannot reference a C# const. If you
// change them there, change them here — a mismatch shows up as a failed run,
// never as a silent pass, because a missing marker is a failure.
const PASS_MARKER = 'COD_SHOTS_OK'
const FAIL_MARKER = 'COD_SHOTS_FAIL'
const SHOT_MARKER = 'COD_SHOT'
const EXECUTABLE = 'CallOfDuty.exe'
const BUILD_METHOD = 'CoD.EditorTools.GameBuilder.BuildWindowsDevelopmentHeadless'

// 16:9, and deliberately smaller than the 1920x1080 the player defaults to.
// Windows clamps a window to the desktop, so asking for the full panel width on
// a 1080p laptop yields a window that is silently a few pixels short and no
// longer 16:9 — and every FOV number in this project is tuned for 16:9. 1600x900
// fits inside any modern panel with its title bar, so what comes back is the
// aspect that was asked for.
const WIDTH = 1600
const HEIGHT = 900

// Logs/ is already gitignored and already covered by guard-no-build-artifacts,
// so PNGs written here cannot reach a commit even by accident. That is the whole
// reason this is not a new top-level folder: a new folder needs a new ignore
// rule, and the first person to forget it commits a megabyte of screenshots
// through LFS.
const shotRoot = join(repoRoot, 'Logs', 'Screenshots')
const outputDirectory = join(repoRoot, 'Build', 'Windows-Dev')
const executable = join(outputDirectory, EXECUTABLE)
const buildLog = join(repoRoot, 'Logs', 'player-build-dev.log')

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

/**
 * The stableId of the FIRST mission in the catalog, or null.
 *
 * Read out of the asset rather than hard-coded, because a hard-coded id that
 * stops matching produces the worst possible outcome: MissionDirector logs
 * "no matching mission", falls back to the endless loop, and the campaign pass
 * quietly photographs the endless HUD while claiming to be the campaign.
 *
 * The catalog is read for the ORDER — mission 1 is the one every player sees
 * first, and it is the only one unlocked on a fresh save. Every step degrades to
 * null rather than throwing: the campaign pass is a bonus, and losing it must
 * never cost the endless frames too.
 */
function firstMissionId() {
  const missionsDirectory = join(repoRoot, 'Assets', '_Project', 'Data', 'Missions')
  const catalog = join(missionsDirectory, 'Missions.asset')
  if (!existsSync(catalog)) return null

  const text = readFileSync(catalog, 'utf8')
  // Scoped by SEARCH, not indexOf: the key is indented inside the MonoBehaviour
  // block, so hunting for a line that literally starts with it finds nothing,
  // and slicing from -1 hands the regex below the last character of the file.
  const start = text.search(/^\s*missions:/m)
  if (start === -1) return null
  const list = text.slice(start)
  const reference = list.match(/^\s*-\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]{32})/m)
  if (!reference) return null
  const guid = reference[1].toLowerCase()

  for (const entry of readdirSync(missionsDirectory)) {
    if (!entry.endsWith('.asset.meta')) continue
    const meta = readFileSync(join(missionsDirectory, entry), 'utf8')
    if (!new RegExp(`guid:\\s*${guid}\\b`, 'i').test(meta)) continue

    const asset = join(missionsDirectory, entry.slice(0, -'.meta'.length))
    if (!existsSync(asset)) return null
    const id = readFileSync(asset, 'utf8').match(/^\s*stableId:\s*(\S+)\s*$/m)
    return id ? id[1] : null
  }
  return null
}

function buildPlayer(unity) {
  // A stale executable that failed to rebuild would otherwise be photographed
  // happily, and the caller would review last week's game believing it was this
  // one. Same reasoning as verify-build.mjs, and it matters more here: a stale
  // GATE fails loudly, a stale SCREENSHOT looks perfect.
  if (existsSync(outputDirectory)) rmSync(outputDirectory, { recursive: true, force: true })

  console.log('screenshot: building the development player...')
  try {
    execFileSync(unity, [
      '-batchmode', '-quit', '-projectPath', repoRoot,
      '-executeMethod', BUILD_METHOD,
      '-logFile', buildLog,
    ], { stdio: 'inherit' })
  } catch {
    console.error(`screenshot: the player build FAILED. See ${buildLog}`)
    process.exit(1)
  }
}

/**
 * One capture pass. Returns { ok, shots } — shots is what actually landed on
 * disk, reported whether the pass passed or not, because a caller debugging a
 * failure wants to look at the frames that DID come back.
 */
function capturePass(name, missionId) {
  const directory = join(shotRoot, name)
  // Wipe first. A pass that dies half way through otherwise leaves the previous
  // run's frames sitting beside the new ones with nothing to tell them apart —
  // and reviewing a stale frame as if it were fresh is worse than reviewing none.
  if (existsSync(directory)) rmSync(directory, { recursive: true, force: true })
  mkdirSync(directory, { recursive: true })

  const log = join(repoRoot, 'Logs', `player-screenshots-${name}.log`)
  if (existsSync(log)) rmSync(log, { force: true })

  const args = [
    '-codScreenshots',
    '-codShotDirectory', directory,
    // NOT -nographics and NOT -batchmode. The whole point is a real window with
    // a real swap chain: -nographics renders nothing, and the capture would come
    // back black if it came back at all.
    '-screen-fullscreen', '0',
    '-screen-width', String(WIDTH),
    '-screen-height', String(HEIGHT),
    '-logFile', log,
  ]
  if (missionId) args.push('-codMission', missionId)

  console.log(`screenshot: running the ${name} pass at ${WIDTH}x${HEIGHT}...`)
  // The player quits itself at the end of the route, which takes about fifteen
  // seconds. The timeout is the backstop for the two hangs this cannot rule out
  // from inside: a binary that predates the harness ignores the flag and just
  // plays the game forever, and WaitForEndOfFrame never fires on a machine with
  // no usable display, leaving the coroutine waiting for a frame nobody draws.
  const run = spawnSync(executable, args, { timeout: 180_000, stdio: 'ignore' })

  const text = existsSync(log) ? readFileSync(log, 'utf8') : ''
  const shots = []
  for (const line of text.split('\n')) {
    const match = line.trim().match(new RegExp(`^${SHOT_MARKER} (\\d+) (.+)$`))
    if (match) shots.push({ bytes: Number(match[1]), path: match[2] })
  }

  const ok = run.status === 0 && text.includes(PASS_MARKER) && !text.includes(FAIL_MARKER)
  if (!ok) {
    console.error(`\n✖ screenshot: the ${name} pass FAILED (exit ${run.status}).`)
    // The errors themselves, not just the verdict — a gate that only says "no"
    // costs a debugging round every time it fires.
    for (const line of text.split('\n')) {
      if (/error|exception|COD_SHOTS/i.test(line)) console.error('    ' + line.trim())
    }
    // The one failure with no error in the log at all, named explicitly because
    // it is silent and it is the failure --reuse makes likely: a player built
    // before the capture harness existed does not recognise the flag, so it just
    // starts the game and plays it until the timeout kills it. Nothing is logged
    // because nothing went wrong — the binary is simply the wrong one.
    if (shots.length === 0 && !text.includes('Screenshot run:')) {
      console.error('    The player never acknowledged -codScreenshots. This binary probably')
      console.error('    predates the capture harness — rebuild it (drop --reuse).')
    }
    console.error(`    full log: ${log}`)
  }
  return { ok, shots, log }
}

// ---------- run ----------

const unity = findUnity()
mkdirSync(join(repoRoot, 'Logs'), { recursive: true })

if (reuse) {
  if (!existsSync(executable)) {
    console.error(`screenshot: --reuse was given but ${executable} does not exist. Run without --reuse.`)
    process.exit(1)
  }
  // Loud on purpose. --reuse is the one way this tool can show you a game that
  // is not the game in your working tree, so the age of the binary is stated
  // rather than assumed.
  console.log(`screenshot: REUSING the player built ${statSync(executable).mtime.toISOString()}`)
} else {
  if (unity === null) {
    console.error(`screenshot: no Unity ${projectEditorVersion()} found. Set UNITY_EDITOR_ROOT, or pass --reuse.`)
    process.exit(1)
  }
  buildPlayer(unity)
  if (!existsSync(executable)) {
    console.error(`screenshot: the build reported success but ${executable} does not exist.`)
    process.exit(1)
  }
}

const missionId = requestedMission ?? firstMissionId()
if (!skipCampaign && !missionId) {
  console.warn('screenshot: no mission found in Assets/_Project/Data/Missions — skipping the campaign pass.')
}

const passes = [capturePass('endless', null)]
if (!skipCampaign && missionId) passes.push(capturePass('campaign', missionId))

console.log('')
let total = 0
for (const pass of passes) {
  for (const shot of pass.shots) {
    console.log(`  ${shot.path}   (${Math.round(shot.bytes / 1024)} KB)`)
    total++
  }
}

const failed = passes.filter((pass) => !pass.ok).length
if (failed > 0) {
  console.error(`\n✖ screenshot: ${failed} of ${passes.length} pass(es) failed. ${total} frame(s) survived, listed above.\n`)
  process.exit(1)
}

console.log(`\n✓ screenshot: ${total} frame(s) rendered by the built player at ${WIDTH}x${HEIGHT}.`)
console.log(`  ${shotRoot}\n`)
