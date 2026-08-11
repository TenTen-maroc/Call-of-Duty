#!/usr/bin/env node
/**
 * Generates placeholder gunfeel audio as 16-bit mono WAV files.
 *
 * WHY: silence is the worst possible placeholder. A shooter with no hitmarker
 * click reads as "did that even hit?", and per references/gunfeel.md the
 * hitmarker sound does more for feel than any amount of weapon polish. These are
 * deliberately crude synthesised sounds — enough to tune timing, mixing and the
 * hit/kill distinction against, and to be replaced by real recordings later
 * without touching a line of code (they are just AudioClip refs on assets).
 *
 * Deterministic: a fixed PRNG seed, so re-running produces byte-identical files
 * and does not churn git or LFS.
 *
 * Run:  node Tools/make-placeholder-audio.mjs
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const outDir = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'Assets', '_Project', 'Audio')
const RATE = 44100

// Small deterministic PRNG — Math.random() would change the bytes every run.
function makeRandom(seed) {
  let state = seed >>> 0
  return () => {
    state ^= state << 13; state >>>= 0
    state ^= state >>> 17
    state ^= state << 5; state >>>= 0
    return state / 0xffffffff * 2 - 1
  }
}

function writeWav(name, samples) {
  const data = Buffer.alloc(samples.length * 2)
  for (let i = 0; i < samples.length; i++) {
    const clamped = Math.max(-1, Math.min(1, samples[i]))
    data.writeInt16LE(Math.round(clamped * 32767), i * 2)
  }
  const header = Buffer.alloc(44)
  header.write('RIFF', 0)
  header.writeUInt32LE(36 + data.length, 4)
  header.write('WAVE', 8)
  header.write('fmt ', 12)
  header.writeUInt32LE(16, 16)      // PCM chunk size
  header.writeUInt16LE(1, 20)       // format = PCM
  header.writeUInt16LE(1, 22)       // mono
  header.writeUInt32LE(RATE, 24)
  header.writeUInt32LE(RATE * 2, 28)
  header.writeUInt16LE(2, 32)
  header.writeUInt16LE(16, 34)
  header.write('data', 36)
  header.writeUInt32LE(data.length, 40)

  const path = join(outDir, name)
  writeFileSync(path, Buffer.concat([header, data]))
  console.log(`  ${name.padEnd(24)} ${(samples.length / RATE * 1000).toFixed(0)} ms`)
}

const build = (seconds, fn) => {
  const count = Math.floor(RATE * seconds)
  const out = new Float32Array(count)
  for (let i = 0; i < count; i++) out[i] = fn(i / RATE, i, count)
  return out
}

mkdirSync(outDir, { recursive: true })
console.log('placeholder audio ->', outDir)

// Close layer: the mechanical crack. Noise transient over a short body thump.
const rndFire = makeRandom(20260811)
writeWav('Fire_AR_Close.wav', build(0.14, (t) => {
  const crack = rndFire() * Math.exp(-t * 90)
  const body = Math.sin(2 * Math.PI * 130 * t) * Math.exp(-t * 38) * 0.7
  const click = Math.sin(2 * Math.PI * 1800 * t) * Math.exp(-t * 260) * 0.25
  return (crack + body + click) * 0.72
}))

// Tail layer: the distance/reverb answer. Quieter, longer, no transient — two
// layers is what stops a gunshot sounding cheap.
const rndTail = makeRandom(77712)
writeWav('Fire_AR_Tail.wav', build(0.55, (t) => {
  const decay = Math.exp(-t * 7)
  const rumble = Math.sin(2 * Math.PI * 85 * t) * 0.35
  return (rndTail() * 0.5 + rumble) * decay * 0.3
}))

// Hitmarker: short, bright, unmistakable.
writeWav('Hitmarker.wav', build(0.06, (t) => {
  const env = Math.exp(-t * 150)
  return (Math.sin(2 * Math.PI * 2100 * t) * 0.8 + Math.sin(2 * Math.PI * 3200 * t) * 0.2) * env * 0.5
}))

// Kill: deliberately lower, longer and fatter than the hit. Players learn the
// difference in seconds and it makes clearing a wave legible with no UI.
writeWav('Hitmarker_Kill.wav', build(0.22, (t) => {
  const env = Math.exp(-t * 22)
  const tone = Math.sin(2 * Math.PI * 420 * t) * 0.6 + Math.sin(2 * Math.PI * 280 * t) * 0.4
  const snap = Math.sin(2 * Math.PI * 1400 * t) * Math.exp(-t * 120) * 0.3
  return (tone * env + snap) * 0.55
}))

// Dry fire: the absence cue. Tiny, mechanical, no tone.
const rndDry = makeRandom(4242)
writeWav('DryFire.wav', build(0.05, (t) => {
  const env = Math.exp(-t * 200)
  return (rndDry() * 0.35 + Math.sin(2 * Math.PI * 2600 * t) * 0.4) * env * 0.45
}))

// Reload: two mechanical clacks, magazine out then in.
const rndReload = makeRandom(9001)
writeWav('Reload_AR.wav', build(1.6, (t) => {
  const clack = (at) => {
    const dt = t - at
    return dt < 0 ? 0 : (rndReload() * 0.6 + Math.sin(2 * Math.PI * 900 * dt) * 0.4) * Math.exp(-dt * 60)
  }
  return (clack(0.05) + clack(0.75) + clack(1.25)) * 0.5
}))

// ---------- drones (Rusher milestone) ----------

// Explosion: the Rusher's contact detonation. Low body, noise burst, long tail —
// it has to land harder than a gunshot or the threat reads as harmless.
const rndBoom = makeRandom(31337)
writeWav('Explosion.wav', build(0.9, (t) => {
  const punch = Math.sin(2 * Math.PI * 55 * Math.exp(-t * 2) * t) * Math.exp(-t * 6)
  const debris = rndBoom() * Math.exp(-t * 9) * 0.6
  const crack = rndBoom() * Math.exp(-t * 120) * 0.5
  return (punch + debris + crack) * 0.8
}))

// Drone alert: the fuse. A rising three-tone the player can learn to run from —
// a contact detonation is only fair if it announces itself first.
writeWav('Drone_Alert.wav', build(0.5, (t) => {
  const beep = (at, freq) => {
    const dt = t - at
    return dt < 0 || dt > 0.12 ? 0 : Math.sin(2 * Math.PI * freq * dt) * Math.exp(-dt * 18)
  }
  return (beep(0, 900) + beep(0.18, 1250) + beep(0.34, 1700)) * 0.4
}))

// Drone death (shot down, not detonated): a short electrical collapse, clearly
// NOT the explosion, so "I killed it" and "it got me" never sound alike.
const rndDeath = makeRandom(5150)
writeWav('Drone_Death.wav', build(0.35, (t) => {
  const whine = Math.sin(2 * Math.PI * (700 - 500 * t) * t) * Math.exp(-t * 10)
  const fizz = rndDeath() * Math.exp(-t * 25) * 0.4
  return (whine + fizz) * 0.5
}))

// Player hurt: a dull thud plus a short ring. Damage that makes no sound reads
// as a bug, and the ring is the "you are hurt" cue that lands before the eye
// finds the health number.
const rndHurt = makeRandom(6161)
writeWav('Player_Hurt.wav', build(0.6, (t) => {
  const thud = Math.sin(2 * Math.PI * 95 * t) * Math.exp(-t * 20)
  const ring = Math.sin(2 * Math.PI * 3100 * t) * Math.exp(-t * 4) * 0.12
  const grit = rndHurt() * Math.exp(-t * 40) * 0.25
  return (thud + ring + grit) * 0.6
}))

// ---------- shop (wave milestone) ----------

// Purchase: a short affirmative two-tone. Buying has to feel like a reward even
// before the stat does anything.
writeWav('Shop_Buy.wav', build(0.3, (t) => {
  const first = t < 0.09 ? Math.sin(2 * Math.PI * 660 * t) * Math.exp(-t * 24) : 0
  const dt = t - 0.09
  const second = dt > 0 ? Math.sin(2 * Math.PI * 990 * dt) * Math.exp(-dt * 14) : 0
  return (first + second) * 0.45
}))

// Refused: low, short, unmistakably NOT the buy sound. Silence on a refused
// purchase reads as an input that did not register.
writeWav('Shop_Refused.wav', build(0.18, (t) => {
  const tone = Math.sin(2 * Math.PI * 160 * t) * Math.exp(-t * 20)
  return tone * 0.5
}))

// ---------- shooter and tank ----------

// Drone shot: thin, electric, and clearly not the player's rifle. Incoming fire
// has to be identifiable by ear alone in a crowd.
const rndShot = makeRandom(8080)
writeWav('Drone_Shot.wav', build(0.18, (t) => {
  const zap = Math.sin(2 * Math.PI * (1400 - 900 * t) * t) * Math.exp(-t * 30)
  const hiss = rndShot() * Math.exp(-t * 60) * 0.25
  return (zap + hiss) * 0.5
}))

// Slam windup: a rising whine. This is the telegraph the Tank is built around —
// the player must be able to leave before it lands.
writeWav('Slam_Windup.wav', build(0.85, (t) => {
  const rise = Math.sin(2 * Math.PI * (180 + 420 * t) * t) * Math.min(1, t * 3)
  return rise * Math.exp(-t * 0.8) * 0.35
}))

// Slam impact: heavy, short, felt rather than heard.
const rndSlam = makeRandom(9090)
writeWav('Slam_Hit.wav', build(0.55, (t) => {
  const thud = Math.sin(2 * Math.PI * 48 * t) * Math.exp(-t * 9)
  const crack = rndSlam() * Math.exp(-t * 45) * 0.5
  return (thud + crack) * 0.75
}))

console.log('\nPlaceholders only — replace with real recordings when the feel work starts.')
