# Audio

**These are placeholders.** Synthesised by `node Tools/make-placeholder-audio.mjs`,
deterministically, so re-running does not churn git or LFS.

They exist because silence is the worst possible placeholder: with no hitmarker
click every shot reads as "did that even hit?", and there is no way to tune
timing or the hit/kill distinction against nothing. Per the gunfeel reference,
the hitmarker sound does more for feel than any amount of weapon polish, and a
one-layer gunshot is the number-one reason a shooter sounds cheap — hence the
separate close and tail layers.

| File | Role |
| --- | --- |
| `Fire_AR_Close.wav` | Mechanical crack, played dry at the listener |
| `Fire_AR_Tail.wav` | Distance/reverb answer. The second layer is the point |
| `Hitmarker.wav` | Short, bright confirmation |
| `Hitmarker_Kill.wav` | Lower, longer, fatter — learned in seconds, no UI needed |
| `DryFire.wav` | Empty magazine. Absence feedback matters too |
| `Reload_AR.wav` | Three mechanical clacks across the reload |

Replacing them touches no code: they are `AudioClip` references on
`AR_Standard.asset` and the scene's `Hitmarker`. Drop real recordings in with the
same names and re-run **CoD → Build Grey Box**, or just re-assign in the
Inspector.

Import settings are forced by the builder: mono, PCM, decompress-on-load — a
gunshot decoded on the audio thread is a hitch where latency is most audible.
