#!/usr/bin/env node
/**
 * Compiles every first-party assembly with Unity's own Roslyn, WITHOUT opening
 * the editor and without needing a licence.
 *
 * WHY THIS EXISTS
 * Unity refuses to start without an activated licence, so on a fresh machine
 * there is no way to know whether the C# even compiles until someone signs in.
 * The first run of this script caught three real defects that were otherwise
 * invisible: CoD.Weapons was missing its CoD.Player asmdef reference, and two
 * lighting APIs used by the scene builder are obsolete in Unity 6.
 *
 * It is also just faster than the editor for a syntax/type pass, and it is the
 * piece that could run in CI later.
 *
 * WHAT IT DOES NOT DO
 * It does not run the game, execute editor code, or validate that a serialized
 * reference is actually wired in a scene. Compiling is necessary, not sufficient.
 *
 * Run:  node Tools/typecheck.mjs
 */
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync, statSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const cacheDir = join(repoRoot, 'Library', 'TypecheckCache') // under Library/, so gitignored

// ---------- locate the editor that matches ProjectVersion.txt ----------

function projectEditorVersion() {
  const file = join(repoRoot, 'ProjectSettings', 'ProjectVersion.txt')
  if (!existsSync(file)) return null
  const match = readFileSync(file, 'utf8').match(/m_EditorVersion:\s*(\S+)/)
  return match ? match[1] : null
}

function findEditorData() {
  const wanted = projectEditorVersion()
  const roots = [
    process.env.UNITY_EDITOR_ROOT,
    'C:/Program Files/Unity/Hub/Editor',
    'C:/Program Files/Unity/Editor',
    '/Applications/Unity/Hub/Editor',
  ].filter(Boolean)

  for (const root of roots) {
    if (!existsSync(root)) continue
    const candidates = wanted && existsSync(join(root, wanted)) ? [wanted] : readdirSync(root).sort().reverse()
    for (const version of candidates) {
      for (const suffix of ['Editor/Data', 'Unity.app/Contents']) {
        const data = join(root, version, suffix)
        if (existsSync(join(data, 'Managed', 'UnityEngine'))) return { data, version }
      }
    }
  }
  return null
}

const editor = findEditorData()
if (editor === null) {
  console.error('typecheck: no Unity editor found. Set UNITY_EDITOR_ROOT to the folder holding versioned installs.')
  process.exit(1)
}

const dotnet = join(editor.data, 'NetCoreRuntime', 'dotnet.exe')
const csc = join(editor.data, 'DotNetSdkRoslyn', 'csc.dll')
if (!existsSync(dotnet) || !existsSync(csc)) {
  console.error(`typecheck: Unity ${editor.version} has no bundled Roslyn at ${csc}`)
  process.exit(1)
}

// ---------- reference sets ----------

const unityRefs = [
  ...listDlls(join(editor.data, 'Managed', 'UnityEngine')),
  ...listDlls(join(editor.data, 'NetStandard', 'ref', '2.1.0')),
]

function listDlls(directory) {
  if (!existsSync(directory)) return []
  return readdirSync(directory).filter((f) => f.endsWith('.dll')).map((f) => join(directory, f))
}

function collectCs(directory, out = []) {
  if (!existsSync(directory)) return out
  for (const entry of readdirSync(directory)) {
    const full = join(directory, entry)
    if (statSync(full).isDirectory()) collectCs(full, out)
    else if (entry.endsWith('.cs')) out.push(full)
  }
  return out
}

function run(responseLines, label) {
  mkdirSync(cacheDir, { recursive: true })
  const rsp = join(cacheDir, `${label}.rsp`)
  writeFileSync(rsp, responseLines.join('\n'), 'utf8')
  try {
    const output = execFileSync(dotnet, [csc, `@${rsp}`], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] })
    return { ok: true, output }
  } catch (error) {
    return { ok: false, output: `${error.stdout ?? ''}${error.stderr ?? ''}` }
  }
}

// ---------- package assemblies, built from source once and cached ----------

function buildPackage(name, sourceDirs, extraFlags, refs) {
  const target = join(cacheDir, `${name}.dll`)
  const sources = sourceDirs.flatMap((d) => collectCs(d))
  if (sources.length === 0) return null
  if (existsSync(target)) return target

  const lines = [
    '-nologo', '-target:library', '-nostdlib', '-langversion:9.0', '-warnaserror-',
    // Unity's own package code is not our quality gate; only our assemblies are.
    '-nowarn:0169,0649,0414,0067,1591,0618,0162,0429,8632',
    ...extraFlags,
    `-out:"${target}"`,
    ...[...unityRefs, ...refs].map((r) => `-r:"${r}"`),
    ...sources.map((s) => `"${s}"`),
  ]
  const result = run(lines, name)
  if (!result.ok) {
    console.error(`typecheck: could not build ${name} from source\n${result.output.split('\n').slice(0, 5).join('\n')}`)
    return null
  }
  return target
}

const builtInPackages = join(editor.data, 'Resources', 'PackageManager', 'BuiltInPackages')
const ugui = buildPackage('UnityEngine.UI', [join(builtInPackages, 'com.unity.ugui', 'Runtime', 'UGUI')],
  ['-define:PACKAGE_PHYSICS', '-define:PACKAGE_PHYSICS2D', '-define:PACKAGE_ANIMATION'], [])

// The Input System is a registry package, so its source only exists after Unity
// has resolved packages once. On a fresh clone that has never been opened, fetch
// the exact version pinned in the manifest straight from the registry — that is
// what lets this run in CI, or here, before anyone has signed in to Unity.
function inputSystemSource() {
  const packageCache = join(repoRoot, 'Library', 'PackageCache')
  if (existsSync(packageCache)) {
    const resolved = readdirSync(packageCache).find((d) => d.startsWith('com.unity.inputsystem'))
    if (resolved) return join(packageCache, resolved, 'InputSystem')
  }

  const manifestPath = join(repoRoot, 'Packages', 'manifest.json')
  if (!existsSync(manifestPath)) return null
  const version = JSON.parse(readFileSync(manifestPath, 'utf8')).dependencies?.['com.unity.inputsystem']
  if (!version) return null

  const extracted = join(cacheDir, `inputsystem-${version}`)
  const sourceDir = join(extracted, 'package', 'InputSystem')
  if (existsSync(sourceDir)) return sourceDir

  const url = `https://packages.unity.com/com.unity.inputsystem/-/com.unity.inputsystem-${version}.tgz`
  try {
    mkdirSync(extracted, { recursive: true })
    // curl and tar both ship with Windows 10+, macOS and Linux. Both commands run
    // with cwd inside the target and RELATIVE filenames on purpose: GNU tar reads
    // a leading `C:\` as a remote host spec and fails with "Cannot connect to C:",
    // which cost a debugging round the first time. Relative paths have no colon,
    // so GNU tar and BSD tar behave the same.
    execFileSync('curl', ['-sL', '--fail', '--max-time', '120', '-o', 'package.tgz', url],
      { cwd: extracted, stdio: 'ignore' })
    execFileSync('tar', ['xzf', 'package.tgz'], { cwd: extracted, stdio: 'ignore' })
  } catch {
    return null // offline, or the registry is unreachable: skip rather than fail
  }
  return existsSync(sourceDir) ? sourceDir : null
}

const inputSystemDir = inputSystemSource()
const inputSystem = inputSystemDir
  ? buildPackage('Unity.InputSystem', [inputSystemDir],
      ['-unsafe', '-define:UNITY_INPUT_SYSTEM_ENABLE_UI'], ugui ? [ugui] : [])
  : null

/**
 * Anything else a first-party asmdef references by package assembly name.
 *
 * Order matters: the DLL Unity already compiled is both cheapest and exactly
 * what the editor will link against, so it wins. Building from the resolved
 * package source is the fallback for a clone that has never been opened, and
 * `null` — an honest "skipped" — is the last resort.
 *
 * WHY THIS EXISTS: the lookup below tests `packageRefs[name] === null` to decide
 * whether to skip an assembly. An unlisted name is `undefined`, not `null`, so
 * before this existed a reference to an unknown package was neither skipped NOR
 * resolved: the assembly compiled without it and failed with errors that looked
 * like our bug. A gate that lies is worse than one that stops.
 */
function prebuiltAssembly(name) {
  const dll = join(repoRoot, 'Library', 'ScriptAssemblies', `${name}.dll`)
  return existsSync(dll) ? dll : null
}

function packageSource(idPrefix, ...subPath) {
  const packageCache = join(repoRoot, 'Library', 'PackageCache')
  if (!existsSync(packageCache)) return null
  const resolved = readdirSync(packageCache).find((d) => d.startsWith(idPrefix))
  if (!resolved) return null
  const dir = join(packageCache, resolved, ...subPath)
  return existsSync(dir) ? dir : null
}

function resolvePackage(name, idPrefix, subPath, extraFlags = []) {
  const prebuilt = prebuiltAssembly(name)
  if (prebuilt) return prebuilt
  const source = packageSource(idPrefix, ...subPath)
  return source ? buildPackage(name, [source], extraFlags, []) : null
}

// AI Navigation: NavMeshSurface and friends, referenced by CoD.Enemies.
// NMC_CAN_ACCESS_TERRAIN mirrors the package's own versionDefine — the terrain
// module is in our manifest, so the editor compiles it with that symbol too.
const navigation = resolvePackage('Unity.AI.Navigation', 'com.unity.ai.navigation',
  ['Runtime'], ['-define:NMC_CAN_ACCESS_TERRAIN'])

const packageRefs = {
  'Unity.InputSystem': inputSystem,
  'UnityEngine.UI': ugui,
  'Unity.AI.Navigation': navigation,
}

// ---------- first-party assemblies, in dependency order ----------

const scriptsRoot = join(repoRoot, 'Assets', '_Project', 'Scripts')
const asmdefs = readdirSync(scriptsRoot)
  .map((folder) => {
    const dir = join(scriptsRoot, folder)
    if (!statSync(dir).isDirectory()) return null
    const file = readdirSync(dir).find((f) => f.endsWith('.asmdef'))
    if (!file) return null
    const json = JSON.parse(readFileSync(join(dir, file), 'utf8'))
    return { name: json.name, dir, references: json.references ?? [], isEditor: (json.includePlatforms ?? []).includes('Editor') }
  })
  .filter(Boolean)

const byName = new Map(asmdefs.map((a) => [a.name, a]))
const ordered = []
const visiting = new Set()

function visit(assembly) {
  if (ordered.includes(assembly) || visiting.has(assembly.name)) return
  visiting.add(assembly.name)
  for (const reference of assembly.references) {
    const dependency = byName.get(reference)
    if (dependency) visit(dependency)
  }
  visiting.delete(assembly.name)
  ordered.push(assembly)
}
asmdefs.forEach(visit)

let failed = 0
let skipped = 0

for (const assembly of ordered) {
  // Falsy, not `=== null`: an unlisted package name is `undefined`, and letting
  // that through compiles the assembly without a reference it declared.
  const missing = assembly.references.filter((r) => !byName.has(r) && !packageRefs[r])
  if (missing.length > 0) {
    console.log(`•  ${assembly.name.padEnd(14)} skipped — ${missing.join(', ')} not resolved yet (open Unity once)`)
    skipped++
    continue
  }

  const refs = [
    ...Object.values(packageRefs).filter(Boolean),
    ...assembly.references.filter((r) => byName.has(r)).map((r) => join(cacheDir, `${r}.dll`)),
  ]

  const lines = [
    '-nologo', '-target:library', '-nostdlib', '-langversion:9.0', '-nullable:enable', '-warnaserror-',
    '-define:UNITY_EDITOR',
    `-out:"${join(cacheDir, `${assembly.name}.dll`)}"`,
    ...[...unityRefs, ...refs].map((r) => `-r:"${r}"`),
    ...collectCs(assembly.dir).map((s) => `"${s}"`),
  ]

  const result = run(lines, assembly.name)
  const diagnostics = result.output.split('\n').filter((l) => /: (error|warning) /.test(l))
  const errors = diagnostics.filter((l) => l.includes(': error '))
  const warnings = diagnostics.filter((l) => l.includes(': warning '))

  if (errors.length > 0 || warnings.length > 0) {
    console.log(`✖  ${assembly.name.padEnd(14)} ${errors.length} error(s), ${warnings.length} warning(s)`)
    for (const line of diagnostics.slice(0, 12)) console.log(`     ${line.trim()}`)
    failed++
  } else {
    console.log(`✓  ${assembly.name.padEnd(14)} clean`)
  }
}

console.log('')
if (failed > 0) {
  // Warnings count as failure on purpose: first-party code stays at zero
  // warnings, and the Unity console is too noisy to be that gate by hand.
  console.error(`typecheck: ${failed} assembly/assemblies with errors or warnings.\n`)
  process.exit(1)
}
console.log(`typecheck: all clean with Unity ${editor.version}${skipped > 0 ? ` (${skipped} skipped)` : ''}.\n`)
