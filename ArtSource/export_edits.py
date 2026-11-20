"""Export an already open Ridge Relay .blend without changing the source file."""
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

from build_runner import DEST, build_palette_atlas
from mesh_tools import export_fbx


def export_runner():
    rig = bpy.data.objects["Courier"]
    skin = bpy.data.objects["Jerboa Skin"]
    if len(skin.data.materials) > 16:
        raise ValueError("The Jerboa palette atlas supports up to 16 material swatches.")
    build_palette_atlas(skin)
    export_fbx(DEST / "Jerboa.fbx", [rig, skin], animated=True)


def export_environment(names):
    for name in names:
        root = bpy.data.objects[name]
        location = root.location.copy()
        root.location = (0, 0, 0)
        bpy.context.view_layer.update()
        try:
            export_fbx(DEST / "Environment" / f"{name}.fbx", [root, *root.children_recursive])
        finally:
            root.location = location
            bpy.context.view_layer.update()


if __name__ == "__main__":
    source = Path(bpy.data.filepath).name
    if source == "Jerboa.blend":
        export_runner()
    elif source == "Windmill Gorge.blend":
        export_environment(("Windmill", "WindGate", "Cliff", "Chevrons", "Rail"))
    elif source == "Gadgets.blend":
        export_environment(("PickupTailwind", "PickupBristlePad", "PickupShellFilm"))
    else:
        raise ValueError(f"Open a Ridge Relay source file before exporting, got {source!r}.")
