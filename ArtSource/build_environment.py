"""Build the modular sandstone and wind farm kit."""
from math import cos, pi, sin
from pathlib import Path
from random import Random
import sys

import bpy

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "Assets/RidgeRelay/Art/Resources/RidgeRelay/Environment"
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

from mesh_tools import box, export_fbx, fixture, material, mesh, reset_scene


def make_kit():
    reset_scene()
    sand = material("Chalk Sandstone", (0.74, 0.53, 0.34))
    light = material("Cut Stone", (0.90, 0.71, 0.50))
    dark = material("Terracotta Stone", (0.52, 0.29, 0.185))
    teal = material("Painted Teal", (0.046, 0.37, 0.34))
    deep_teal = material("Oxidized Teal", (0.025, 0.23, 0.24))
    gold = material("Ochre Paint", (0.88, 0.43, 0.04))
    cream = material("Sailcloth", (0.96, 0.79, 0.56))
    wood = material("Windmill Timber", (0.26, 0.11, 0.065))

    def cuboid(name, position, size, color, bevel=0.025):
        return box(name, position, size, color, bevel)

    def tower(name, levels, surface, sides=8):
        vertices = []
        for radius, height in levels:
            for section in range(sides):
                theta = pi * 2 * section / sides + pi / sides
                vertices.append((radius * cos(theta), radius * sin(theta), height))
        faces = [tuple(range(sides - 1, -1, -1))]
        for ring in range(len(levels) - 1):
            for section in range(sides):
                index = ring * sides + section
                next_index = ring * sides + (section + 1) % sides
                faces.append((index, next_index, next_index + sides, index + sides))
        faces.append(tuple(range(len(vertices) - sides, len(vertices))))
        return mesh(name, vertices, faces, surface)

    def windmill():
        parts = [
            tower("Mill Tower", [(1.04, 0), (1.01, 0.24), (0.91, 0.37),
                                (0.61, 3.28), (0.63, 3.40)], sand),
            tower("Mill Cap", [(0.70, 3.36), (0.72, 3.46), (0.48, 3.78),
                              (0.20, 3.98), (0.04, 4.03)], dark),
            tower("Entry Ring", [(1.07, 0.07), (1.07, 0.20)], light),
            tower("Balcony Ring", [(0.79, 2.31), (0.83, 2.41), (0.80, 2.57)], light),
            tower("Teal Ring", [(0.804, 2.47), (0.795, 2.55)], teal),
            cuboid("Door", (0, -0.91, 0.57), (0.43, 0.20, 1.04), wood, 0.09),
            cuboid("Sill", (0, -1.04, 0.095), (0.55, 0.33, 0.19), light),
            cuboid("Door Cap", (0, -0.92, 1.085), (0.54, 0.18, 0.155), light),
        ]
        for side in (-1, 1):
            parts.append(cuboid("Window", (0.17 * side, side * 0.61, 2.94),
                                (0.21, 0.14, 0.29), wood, 0.025))
        fixed = fixture("WindmillTower", parts)
        pivot = (0, -0.75, 2.97)
        blades = [cuboid("Axle", (0, -0.80, 2.97), (0.26, 0.26, 0.26), gold, 0.06)]
        for quadrant in range(4):
            theta = pi * quadrant / 2
            blade = cuboid("Spar", (sin(theta) * 0.69, -0.80, 2.97 + cos(theta) * 0.69),
                           (0.063, 0.081, 1.42), wood, 0.012)
            blade.rotation_euler.y = theta
            blades.append(blade)
            sail_points = [(-0.02, 0.24), (0.48, 1.08), (0.10, 1.39), (-0.05, 1.39)]
            vertices = []
            for depth in (-0.855, -0.815):
                vertices.extend((x * cos(theta) + z * sin(theta), depth,
                                 2.97 + z * cos(theta) - x * sin(theta))
                                for x, z in sail_points)
            faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1),
                     (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
            blades.append(mesh("Windmill Sail", vertices, faces, cream))
        rotor = fixture("WindmillRotor", blades, pivot)
        origin = bpy.data.objects.new("Windmill", None)
        bpy.context.collection.objects.link(origin)
        fixed.parent = origin
        rotor.parent = origin
        return [origin, fixed, rotor]

    def wind_gate():
        parts = []
        for side in (-1, 1):
            x = 3.15 * side
            parts.extend([
                cuboid("Pier", (x, 0, 0.53), (0.51, 0.63, 1.06), sand, 0.055),
                cuboid("Pier Plinth", (x, 0, 0.105), (0.72, 0.83, 0.21), light, 0.035),
                cuboid("Gate Upright", (x, 0, 1.68), (0.24, 0.33, 1.56), deep_teal),
                cuboid("Teal Inset", (x, -0.187, 1.62), (0.10, 0.075, 1.39), teal),
            ])
        # The shoulder of the arch is outside the drivable lane lines.
        for segment in range(13):
            angle_a = pi * segment / 13
            angle_b = pi * (segment + 1) / 13
            vertices = []
            for y in (-0.18, 0.18):
                for x_r, z_r, theta in [(3.24, 1.05, angle_a), (3.24, 1.05, angle_b),
                                        (3.06, 0.83, angle_b), (3.06, 0.83, angle_a)]:
                    vertices.append((x_r * cos(theta), y, 2.43 + z_r * sin(theta)))
            faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1),
                     (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
            parts.append(mesh("Arch Segment", vertices, faces, light if segment % 3 == 0 else teal))
        return [fixture("WindGate", parts)]

    def cliff():
        rng = Random(367)
        levels = [(2.12, 0), (2.16, 0.84), (1.94, 1.27), (1.97, 1.65),
                  (1.68, 2.88), (1.88, 3.06), (1.79, 3.44), (1.46, 4.74), (1.18, 4.97)]
        rock = tower("Stratified Cliff", levels, sand)
        for i, vertex in enumerate(rock.data.vertices):
            z_jitter = rng.uniform(-0.100, 0.10) if i >= 8 else 0
            vertex.co.x *= rng.uniform(0.87, 1.14)
            vertex.co.y *= rng.uniform(0.87, 1.14)
            vertex.co.z += z_jitter
        rock.data.materials.append(dark)
        rock.data.materials.append(light)
        for polygon in rock.data.polygons:
            if polygon.index in range(17, 25) or polygon.index in range(41, 49):
                polygon.material_index = 1
            elif polygon.index % 5 == 0 or polygon.index == len(rock.data.polygons) - 1:
                polygon.material_index = 2
        return [fixture("Cliff", [rock])]

    def chevrons():
        parts = [
            cuboid("Board", (0, 0, 0.66), (2.40, 0.14, 0.55), wood, 0.053),
            cuboid("Side Post", (-0.89, 0.03, 0.32), (0.13, 0.13, 0.64), sand),
            cuboid("Side Post", (0.89, 0.03, 0.32), (0.13, 0.13, 0.64), sand),
        ]
        points = [(-0.16, -0.17), (0.00, -0.17), (0.18, 0.00),
                  (0.00, 0.17), (-0.16, 0.17), (0.02, 0.00)]
        for offset in (-0.835, -0.283, 0.283, 0.835):
            vertices = [(offset + x, -0.073, 0.659 + z) for x, z in points]
            parts.append(mesh("Painted Direction Arrow", vertices, [(0, 1, 2, 3, 4, 5)], gold))
        return [fixture("Chevrons", parts)]

    def rail():
        parts = [
            cuboid("Rail Beam", (0, 0, 0.48), (2, 0.22, 0.17), sand),
            cuboid("Rail Teal Inset", (0, -0.113, 0.47), (1.83, 0.03, 0.073), deep_teal),
        ]
        for x in (-0.85, 0.85):
            parts.append(cuboid("Rail Pier", (x, 0, 0.31), (0.27, 0.31, 0.62), light))
        return [fixture("Rail", parts)]

    modules = {"Windmill": windmill(), "WindGate": wind_gate(), "Cliff": cliff(),
               "Chevrons": chevrons(), "Rail": rail()}
    for name, objects in modules.items():
        export_fbx(DEST / f"{name}.fbx", objects)

    for index, objects in enumerate(modules.values()):
        for obj in objects:
            if obj.parent is None:
                obj.location.x += index * 8
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtSource/Windmill Gorge.blend"))


if __name__ == "__main__":
    make_kit()
