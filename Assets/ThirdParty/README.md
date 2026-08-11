# ThirdParty

Bought asset packs live here, one folder each, and are **never edited in place**.

If a pack needs changing: subclass it, or copy the specific file into
`Assets/_Project/` and edit the copy. Editing in place makes the pack
un-updatable and un-deletable, and a year from now nobody remembers which files
were touched.

Keep an eye on VRAM: packs ship 2048 or 4096 textures, and this project targets
a 4 GB card. Set Max Size 1024 on import (512 for anything not near the camera),
and check Window → Analysis → Profiler → Memory before adding the *next* pack,
not after ten of them.

This file also keeps the folder in git — an empty directory would leave
`ThirdParty.meta` pointing at nothing on a fresh clone.
