# Ridge Relay Blender sources

[Jerboa.blend](Jerboa.blend) contains the editable material groups, 17-bone courier rig and eight in-place actions. [Windmill Gorge.blend](Windmill%20Gorge.blend) contains the windmill, wind gate, cliff, chevron and railing models. [Gadgets.blend](Gadgets.blend) contains the three pickup tokens. Each model uses meters and faces Blender −Y. The FBXs are Y-up for Unity with +Z as the intended forward direction. The windmill rotor is a separate mesh with its pivot centered on the axle.

Use Blender 5.2 to edit the source files. Save your changes, then export in background mode from the project root with your installed Blender executable.

```powershell
blender -b "ArtSource/Jerboa.blend" --python-exit-code 1 --python ArtSource/export_edits.py
blender -b "ArtSource/Windmill Gorge.blend" --python-exit-code 1 --python ArtSource/export_edits.py
blender -b "ArtSource/Gadgets.blend" --python-exit-code 1 --python ArtSource/export_edits.py
```

Unity imports the files under `Assets/RidgeRelay/Art/Resources/RidgeRelay`. The runner export flattens up to sixteen material swatches into a single 64 × 64 texture for the mesh while leaving the original editable swatches in Blender. Keep the rig and skin object names and the eight action names when updating the model.

The Unity importer creates a Generic avatar and writes a controller with `Idle`, `Run`, `Jump`, `Fall`, `Land`, `Stumble`, `Finish`, and `Gadget` states. The game selects states from the race simulation and uses the clips for presentation with root motion disabled.

`build_runner.py`, `build_environment.py`, and `build_pickups.py` reconstruct the original source and export files procedurally. Use `export_edits.py` to preserve manual changes in the Blender files. Run `blender -b "ArtSource/Jerboa.blend" --python-exit-code 1 --python ArtSource/render_icon.py` to refresh the app icon.
