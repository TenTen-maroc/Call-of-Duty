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

## Tazir Pass environment kits

Official sources downloaded 2026-08-13:

- [Nature Kit](https://kenney.nl/assets/nature-kit), CC0
- [Survival Kit](https://kenney.nl/assets/survival-kit), CC0

Archive SHA-256 values:

- Nature Kit: `FA7974A0D342BFE63C38664BA9F8EC1A4AAB8EA25F099BDC56870E33588C4D9D`
- Survival Kit: `C3586341B5932C87EB43D75D915434F47DAED168B17ED36A03E8CA9977C7443E`

The complete 10,537,521-byte and 1,948,174-byte archives remain outside the
repository. `Nature/` retains eleven pine, rock, shrub, grass, and log FBXs
(322,654 bytes). `Survival/` retains ten outpost/prop FBXs plus the shared
colormap (314,384 bytes). Imported meshes are collider-free presentation
children; generated primitives remain the sole owners of collision and
navigation. Attribution is not required by CC0 and is retained here only for
provenance.
