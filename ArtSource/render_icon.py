"""Render the Ridge Relay app icon from the courier Blender source."""
from math import cos, pi, sin
from pathlib import Path
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from mesh_tools import mesh

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "Assets/RidgeRelay/Art/Brand/RidgeRelayIcon.png"


def ink(name, color):
    ink_material = bpy.data.materials.new(name)
    ink_material.use_nodes = True
    nodes = ink_material.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (*color, 1)
    ink_material.node_tree.links.new(emission.outputs[0], output.inputs[0])
    return ink_material


def render():
    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("Icon Camera")
    camera = bpy.data.objects.new("Icon Camera", camera_data)
    scene.collection.objects.link(camera)
    target = Vector((0, -0.055, 1.08))
    camera.location = (0.36, -3.3, 1.275)
    direction = (target - camera.location).normalized()
    rotation = direction.to_track_quat("-Z", "Y")
    camera.rotation_euler = rotation.to_euler()
    scene.camera = camera
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 1.13
    right, up = rotation @ Vector((1, 0, 0)), rotation @ Vector((0, 1, 0))
    backdrop = target + direction * 1.05

    def plane(name, points, color, depth):
        vertices = [backdrop + right * x + up * y + direction * depth for x, y in points]
        mesh(name, vertices, [tuple(range(len(vertices)))], ink(name, color))

    def disc(name, radius, color, depth, offset_y=0):
        vertices = [(radius * cos(i * pi / 32), radius * sin(i * pi / 32) + offset_y)
                    for i in range(64)]
        plane(name, vertices, color, depth)

    plane("Deep Teal Field", [(-1, -1), (1, -1), (1, 1), (-1, 1)],
          (0.013, 0.115, 0.128), 0.2)
    plane("Diagonal Wind Band", [(-1, -0.35), (1, 0.31), (1, 0.45), (-1, -0.21)],
          (0.02, 0.172, 0.181), 0.16)
    plane("Thin Wind Band", [(-1, -0.44), (1, 0.22), (1, 0.235), (-1, -0.425)],
          (0.034, 0.24, 0.235), 0.15)
    disc("Ochre Badge Rim", 0.453, (0.86, 0.41, 0.082), 0.10, -0.024)
    disc("Sandstone Badge Keyline", 0.435, (0.91, 0.71, 0.40), 0.09, -0.024)
    disc("Lagoon Badge", 0.422, (0.03, 0.34, 0.33), 0.08, -0.024)
    disc("Badge Spotlight", 0.336, (0.065, 0.46, 0.42), 0.07, 0.043)

    def light(name, position, power, size, color):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = power
        data.shape = "DISK"
        data.size = size
        data.color = color
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()

    light("Warm Key", (-2.3, -3, 4), 490, 2.6, (1.0, 0.87, 0.75))
    light("Lagoon Fill", (2.5, -1.9, 1.6), 180, 2.2, (0.60, 0.83, 1))
    light("Sun Rim", (0.5, 1.5, 3.5), 400, 1.0, (1, 0.69, 0.40))
    scene.world = bpy.data.worlds.new("Icon Ambient")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.24

    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "AgX"
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(OUTPUT)
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    render()
