# Quaternius — Meridian character and animation source

Official sources downloaded 2026-08-13 through each pack's free itch.io
download, without an account or payment:

- [Universal Base Characters](https://quaternius.com/packs/universalbasecharacters.html)
  ([download page](https://quaternius.itch.io/universal-base-characters))
- [Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html)
  ([download page](https://quaternius.itch.io/universal-animation-library))
- Licence: [Creative Commons CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)
- Attribution: not required; retained here for provenance

Archive SHA-256 values:

- `Universal Base Characters[Standard].zip`:
  `FDBF1804C90DFC1EA03E992BFF7DA2DFD1A79318E13270A660180F9308455F40`
- `Universal Animation Library[Standard].zip`:
  `CC73FC4E495B82958207316596317A3F40B9FA38065BDE1027937452DA537724`

The complete 128,968,391-byte and 15,904,933-byte archives remain outside the
repository. The retained 29,653,106-byte subset is the male Humanoid FBX, its
base-color and normal textures, and the library's no-root-motion Unity FBX.
`MeridianHumanBuilder` selects only the clips used by the shared rifleman
Animator Controller. Root motion stays disabled because `NavMeshAgent` owns
translation. Import changes are limited to the project's Humanoid, compressed
mesh, 1024-texture, no-camera, no-light policy.
