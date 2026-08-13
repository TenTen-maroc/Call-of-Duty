# Sonniss GDC firearm audio — trimmed royalty-free subset

Official source used 2026-08-13:

- [Sonniss GDC Game Audio community archive](https://sonniss.com/gameaudiogdc/)
- 2015 `sonniss.zip` official mirror: 9,037,755,976 bytes
- [GDC bundle licence](https://sonniss.com/gdc-bundle-license/): worldwide,
  non-exclusive and royalty-free for media production; no attribution required;
  raw redistribution and AI/ML training are prohibited

The archive remains outside the repository. Its central directory was fetched by
HTTP range, then only the exact compressed members below were downloaded. This
folder retains three mono 48 kHz OGG derivatives totalling **67,181 bytes**:

| Retained file | Source member | Source SHA-256 | Edit |
| --- | --- | --- | --- |
| `rifle_close.ogg` | `SoundMorph - Future Weapons/Alliance-AssaultRifle_05-Single_Shot-04.wav` | `94E9C311DD1296A7D6143D3A8A854AA6759CEE486AEB84F94310CF5E6131B5C0` | mono, 48 kHz, trimmed/faded to 1.20 s |
| `rifle_tail.ogg` | `Coll Anderson - Guns/EFX EXT M4 Shots 02 B.wav` | `8D582026FE611A45B96A3920DA15E15CE274571BF5FBEFBBB451FC6965B94829` | first of three shots, mono, 48 kHz, +8 dB, 1.77 s |
| `rifle_reload.ogg` | `Coll Anderson - Guns/EFX INT AR-15 M4 Charging Handle With Mag 01_A.wav` | `7012AB06C71F1844FC5AE64FDAFB3EA64F21F97C50CF5CB539125245CD75C5F5` | first charging action, mono, 48 kHz, +5 dB, 1.24 s |

Derived-file SHA-256 values:

- `rifle_close.ogg`: `BC0BD9E7DFE06FE66B6E32DCA4AE4DB97C72CEB42424433D07C4BB835F99BFC3`
- `rifle_tail.ogg`: `9158154D3395518224AC660B053A32F865F6FFD6BE61FFBED08371013EAADBBB`
- `rifle_reload.ogg`: `6D9F530318BEB14194CC72E9E06828D954384DC3B68DFE50619443F80D0661CE`

These replace the generated close/tail/reload placeholders through the optional
audio kit. Nulling the whole kit and rebuilding restores the deterministic WAVs.
