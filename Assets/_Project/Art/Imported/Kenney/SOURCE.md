# Kenney audio — retained CC0 subset

Official sources downloaded 2026-08-13:

- [Impact Sounds](https://kenney.nl/assets/impact-sounds), version 1.0, 130 files
- [Sci-fi Sounds](https://kenney.nl/assets/sci-fi-sounds), version 1.0, 70 files
- [Interface Sounds](https://kenney.nl/assets/interface-sounds), version 1.0, 100 files
- Licence: [Creative Commons CC0](https://kenney.nl/support)

The complete archives remain outside the repository. This folder retains only
18 clips selected for concrete footsteps, differentiated bullet impacts,
facility ambience, enemy/explosion cues, and interface confirmation. The source
archives measured 7,510,490 bytes; the retained OGG files measure 578,632 bytes.

Archive SHA-256 values:

- Sci-fi Sounds: `119340F351A5098AD814F78719438C0DA355A9CE8A4C8A3AF6A8D48AA3D49E04`
- Impact Sounds: `029D734AF1582474EDF3A694D1B0CEBC97C1C152F2F39FA34D4C2BAFC5DE77F8`
- Interface Sounds: `F2193D072726D6758A5F7871B2DCC54DCCE0D5C35C6F0A62F92549B327C81232`

Short cues import mono, PCM, and decompressed-on-load for predictable latency.
The three five-second ambience loops import mono Vorbis and compressed-in-memory
so the always-resident room bed does not pay the PCM footprint of source audio.
