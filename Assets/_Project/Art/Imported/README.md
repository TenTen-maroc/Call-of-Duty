# Imported art

Free and copied-in art is grouped by source beneath this folder. Every source
must include its licence/provenance note and land in a separate commit with its
own texture-VRAM and Git LFS measurement.

`ArtImportPostprocessor` applies the committed texture/model presets to this
tree before Unity imports an asset. Weapons, hands, arms and viewmodels may use
2048 textures; everything else is capped at 1024.
