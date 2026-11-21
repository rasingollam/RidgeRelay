"""Build the homing rocket used by the in-game power effect."""
from pathlib import Path
from math import pi, cos, sin
import sys
import bpy

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from mesh_tools import material, reset_scene, sphere, fixture, mesh, export_fbx


def build_rocket():
    reset_scene()
    orange = material("Rocket Orange", (0.91, 0.28, 0.055))
    cream = material("Rocket Cream", (0.92, 0.84, 0.62))
    teal = material("Rocket Window", (0.03, 0.62, 0.65), 0.25)
    flame = material("Rocket Flame", (1, 0.56, 0.10))
    parts = [sphere("Rocket Hull", (0, 0, 0), (0.18, 0.48, 0.18), orange, segments=24, rings=16)]
    bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=0.18, radius2=0.015,
                                   depth=0.32, location=(0, -0.49, 0))
    nose = bpy.context.object
    nose.rotation_euler.x = pi / 2
    nose.data.materials.append(cream)
    for face in nose.data.polygons:
        face.use_smooth = True
    parts.append(nose)
    for index in range(3):
        angle = index * pi * 2 / 3
        points = []
        for z in (-0.014, 0.014):
            for x, y in [(0.14, 0.12), (0.37, 0.42), (0.14, 0.36)]:
                points.append((x * cos(angle) - z * sin(angle), y,
                               x * sin(angle) + z * cos(angle)))
        parts.append(mesh("Rocket Fin", points,
                          [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)], cream))
    parts.append(sphere("Window Rim", (0.166, -0.12, 0.04), (0.026, 0.080, 0.080),
                        cream, segments=16, rings=10))
    parts.append(sphere("Window Glass", (0.186, -0.12, 0.04), (0.015, 0.060, 0.060),
                        teal, segments=16, rings=10))
    parts.append(sphere("Rocket Exhaust", (0, 0.51, 0), (0.10, 0.17, 0.10),
                        flame, segments=16, rings=10))
    rocket = fixture("HomingRocket", parts)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtSource/Power Effects.blend"))
    export_fbx(ROOT / "Assets/RidgeRelay/Art/Resources/RidgeRelay/Environment/HomingRocket.fbx", [rocket])


if __name__ == "__main__":
    build_rocket()
