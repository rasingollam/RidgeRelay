"""Make three stylized world-space gadget tokens for Ridge Relay."""
from math import cos, pi, sin
from pathlib import Path
import sys

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from mesh_tools import box, export_fbx, fixture, material, mesh, reset_scene

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "Assets/RidgeRelay/Art/Resources/RidgeRelay/Environment"


def extrude_shape(name, outline, front, back, surface):
    count = len(outline)
    vertices = [(x, depth, z) for depth in (front, back) for x, z in outline]
    faces = [tuple(range(count)), tuple(range(count * 2 - 1, count - 1, -1))]
    for index in range(count):
        following = (index + 1) % count
        faces.append((following, index, index + count, following + count))
    return mesh(name, vertices, faces, surface)


def build():
    reset_scene()
    aqua = material("Tailwind Aqua", (0.026, 0.74, 0.55))
    turquoise = material("Tailwind Deep Teal", (0.014, 0.265, 0.24))
    foam = material("Glider Cream", (0.92, 0.92, 0.74))
    orange = material("Bristle Coral", (0.99, 0.25, 0.053))
    clay = material("Bristle Clay", (0.43, 0.078, 0.026))
    lavender = material("Shell Lavender", (0.55, 0.40, 0.82))
    violet = material("Shell Indigo", (0.205, 0.102, 0.38))

    def disc(radius, count=12):
        return [(cos(i * 2 * pi / count) * radius, sin(i * 2 * pi / count) * radius)
                for i in range(count)]

    def tailwind():
        parts = [extrude_shape("Tailwind Outer Ring", disc(0.39), -0.065, 0.065, turquoise),
                 extrude_shape("Tailwind Aqua Faces", disc(0.335), -0.083, 0.083, aqua)]
        shape = [(-0.20, -0.05), (0.0, 0.075), (0.20, -0.05),
                 (0.20, 0.027), (0.0, 0.152), (-0.20, 0.027)]
        for z in (-0.13, 0.045):
            arrow = [(x, height + z) for x, height in shape]
            parts.append(extrude_shape("Front Wind Chevron", arrow, -0.104, -0.080, foam))
            parts.append(extrude_shape("Rear Wind Chevron", arrow, 0.080, 0.104, foam))
        return fixture("PickupTailwind", parts)

    def bristle():
        parts = [box("Bristle Pad Frame", (0, 0, 0), (0.75, 0.15, 0.65), clay, 0.085),
                 box("Bristle Pad Core", (0, 0, 0), (0.64, 0.188, 0.54), orange, 0.065)]
        for side in (-1, 1):
            for x in (-0.16, 0.16):
                for z in (-0.135, 0.135):
                    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.10, radius2=0.019,
                                                   depth=0.13, location=(x, 0.15 * side, z))
                    spike = bpy.context.object
                    spike.name = "Bristle Tuft"
                    spike.rotation_euler.x = -side * pi * 0.5
                    spike.data.materials.append(foam)
                    parts.append(spike)
        return fixture("PickupBristlePad", parts)

    def shell():
        shape = [(0, -0.41), (0.24, -0.244), (0.35, -0.022), (0.35, 0.236),
                 (0, 0.351), (-0.35, 0.236), (-0.35, -0.022), (-0.24, -0.244)]
        inner = [(x * 0.84, z * 0.84 + 0.014) for x, z in shape]
        mark = [(0, -0.20), (0.154, -0.055), (0.106, 0.06),
                (0.02, 0.145), (-0.14, 0.037), (-0.175, -0.076)]
        parts = [extrude_shape("Shell Film Edge", shape, -0.075, 0.075, violet),
                 extrude_shape("Shell Film Body", inner, -0.096, 0.096, lavender),
                 extrude_shape("Front Shell Mark", mark, -0.12, -0.093, foam),
                 extrude_shape("Rear Shell Mark", mark, 0.093, 0.12, foam)]
        return fixture("PickupShellFilm", parts)

    tokens = [tailwind(), bristle(), shell()]
    for index, token in enumerate(tokens):
        export_fbx(DEST / f"{token.name}.fbx", [token])
        token.location.x = index * 1.25
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtSource/Gadgets.blend"))


if __name__ == "__main__":
    build()
