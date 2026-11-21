# Ridge Relay Blender sources

[Jerboa.blend](Jerboa.blend) contains the editable material groups, 18-bone courier rig and fourteen in-place actions. The fur body is fused into a continuous mesh with smoothed skin weights. Short weighted fiber tufts soften the body and tail silhouette. [Windmill Gorge.blend](Windmill%20Gorge.blend) contains the windmill, wind gate, cliff, chevron and railing models. [Gadgets.blend](Gadgets.blend) contains the three pickup tokens. [Canyon Foliage.blend](Canyon%20Foliage.blend) contains the tree, shrub and lavender kit. [Power Effects.blend](Power%20Effects.blend) contains the homing rocket. Each model uses meters and faces Blender −Y. The FBXs are Y-up for Unity with +Z as the intended forward direction. The windmill rotor is a separate mesh with its pivot centered on the axle.

Use Blender 5.2 to edit the source files. Save your changes, then export in background mode from the project root with your installed Blender executable.

```powershell
blender -b "ArtSource/Jerboa.blend" --python-exit-code 1 --python ArtSource/export_edits.py
blender -b "ArtSource/Windmill Gorge.blend" --python-exit-code 1 --python ArtSource/export_edits.py
blender -b "ArtSource/Gadgets.blend" --python-exit-code 1 --python ArtSource/export_edits.py
```

Unity imports the files under `Assets/RidgeRelay/Art/Resources/RidgeRelay`. The runner export flattens up to sixteen material swatches into a single 512 × 512 atlas while leaving the original editable swatches in Blender. The export replaces source UV sets with one atlas channel. Runtime customization tints the fur and local blue backpack cells while preserving the atlas grain. Keep the rig, skin object and action names when updating the model.

The Unity importer creates a Generic avatar with `Idle`, `Run`, `Jump`, `Fall`, `Land`, `Stumble`, `Finish`, and `Gadget` states. The running blend tree includes `Sprint`, `Uphill`, and `Downhill`. An upper-body mask layers `CastMagnet`, `CastRocket`, and `CastShock` over running. The game selects states from the race simulation with root motion disabled. `Blink.L` and `Blink.R` blend shapes drive facial blinks.

`build_runner.py`, `build_environment.py`, and `build_pickups.py` reconstruct the original source and export files procedurally. Use `export_edits.py` to preserve manual changes in the Blender files. Run `blender -b "ArtSource/Jerboa.blend" --python-exit-code 1 --python ArtSource/render_icon.py` to refresh the app icon.

`build_foliage.py` and `build_power_models.py` rebuild their respective kits. `build_surface_maps.py` derives subtle tangent normal and smoothness maps from the painted surface albedos. The surface images, sky panorama and gadget icons are generated production assets. The images under `References` are aspirational art direction and are not Unity screenshots.
