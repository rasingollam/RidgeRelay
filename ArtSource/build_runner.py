"""Build Ridge Relay's editable runner source and Unity FBX export."""
from pathlib import Path
import sys

import bpy
from mathutils import Quaternion, Vector

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "Assets/RidgeRelay/Art/Resources/RidgeRelay"
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

from mesh_tools import box, export_fbx, fixture, material, reset_scene, sphere


def build_runner():
    reset_scene()
    rust = material("Courier Coral", (0.67, 0.12, 0.078))
    rust_dark = material("Ear Shadow", (0.39, 0.062, 0.047))
    cream = material("Dune Cream", (0.91, 0.69, 0.43))
    teal = material("Courier Teal", (0.022, 0.28, 0.26))
    green = material("Vest Shade", (0.017, 0.16, 0.15))
    gold = material("Marigold Canvas", (0.93, 0.43, 0.042))
    leather = material("Boot Rubber", (0.063, 0.042, 0.042))
    light_metal = material("Buckles", (0.85, 0.87, 0.71))
    eye = material("Eyes and Nose", (0.018, 0.012, 0.012))

    skeleton = {
        "root": ((0, 0, 0), (0, 0, 0.55), None),
        "hips": ((0, 0, 0.52), (0, 0, 0.72), "root"),
        "spine": ((0, 0, 0.72), (0, -0.02, 0.92), "hips"),
        "head": ((0, -0.02, 0.91), (0, -0.075, 1.15), "spine"),
        "ear.L": ((0.129, -0.044, 1.151), (0.236, -0.038, 1.485), "head"),
        "ear.R": ((-0.129, -0.044, 1.151), (-0.226, -0.028, 1.424), "head"),
        "tail": ((0, 0.13, 0.55), (0, 0.41, 0.40), "hips"),
        "tailTip": ((0, 0.41, 0.40), (0.02, 0.73, 0.55), "tail"),
    }
    for side, sign in (("L", 1), ("R", -1)):
        skeleton.update({
            f"thigh.{side}": ((0.119 * sign, 0, 0.58), (0.128 * sign, -0.06, 0.29), "hips"),
            f"shin.{side}": ((0.128 * sign, -0.06, 0.29), (0.141 * sign, 0.024, 0.10), f"thigh.{side}"),
            f"foot.{side}": ((0.141 * sign, 0.024, 0.10), (0.141 * sign, -0.15, 0.063), f"shin.{side}"),
            f"upperArm.{side}": ((0.194 * sign, -0.01, 0.84), (0.251 * sign, 0.016, 0.665), "spine"),
            f"forearm.{side}": ((0.251 * sign, 0.016, 0.665), (0.263 * sign, -0.043, 0.535), f"upperArm.{side}"),
        })

    rig_data = bpy.data.armatures.new("Courier Skeleton")
    rig = bpy.data.objects.new("Courier", rig_data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail, parent) in skeleton.items():
        bone = rig_data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = rig_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.show_in_front = True

    parts = []

    def bind(part, bone):
        group = part.vertex_groups.new(name=bone)
        group.add([vertex.index for vertex in part.data.vertices], 1.0, "REPLACE")
        parts.append(part)
        return part

    def ellipsoid(name, at, radii, color, bone, angle=(0, 0, 0)):
        part = sphere(name, at, radii, color)
        part.rotation_euler = angle
        return bind(part, bone)

    def block(name, at, size, color, bone, bevel=0.015):
        return bind(box(name, at, size, color, bevel), bone)

    def limb(name, from_at, to_at, radius, color, bone):
        start, end = Vector(from_at), Vector(to_at)
        delta = end - start
        part = sphere(name, (start + end) * 0.5, (radius, radius, delta.length * 0.61), color)
        part.rotation_mode = "QUATERNION"
        part.rotation_quaternion = delta.to_track_quat("Z", "Y")
        return bind(part, bone)

    ellipsoid("Haunches", (0, 0.023, 0.584), (0.206, 0.153, 0.183), rust, "hips")
    ellipsoid("Vest", (0, -0.003, 0.751), (0.210, 0.161, 0.199), teal, "spine")
    ellipsoid("Vest Front", (0, -0.143, 0.746), (0.063, 0.032, 0.151), cream, "spine")
    ellipsoid("Stand Collar", (0, -0.014, 0.892), (0.164, 0.133, 0.052), green, "spine")
    block("Courier Pack", (0, 0.194, 0.758), (0.294, 0.176, 0.322), gold, "spine", 0.038)
    block("Pack Flap", (0, 0.274, 0.829), (0.252, 0.028, 0.132), gold, "spine")
    block("Pack Strap", (0, 0.297, 0.748), (0.033, 0.023, 0.133), leather, "spine")
    block("Pack Buckle", (0, 0.311, 0.754), (0.062, 0.015, 0.052), light_metal, "spine", 0.006)
    for side in (-1, 1):
        ellipsoid("Backpack Harness", (side * 0.127, -0.131, 0.812), (0.026, 0.023, 0.108),
                  gold, "spine", (0, -0.28 * side, 0))
        block("Vest Pocket", (side * 0.117, -0.143, 0.686), (0.108, 0.051, 0.085),
              green, "spine")

    ellipsoid("Head", (0, -0.044, 1.04), (0.232, 0.206, 0.195), rust, "head")
    ellipsoid("Muzzle", (0, -0.194, 0.988), (0.151, 0.107, 0.081), cream, "head")
    ellipsoid("Nose", (0, -0.286, 1.023), (0.045, 0.034, 0.026), eye, "head")
    for side, sign in (("L", 1), ("R", -1)):
        head, tail, _ = skeleton[f"ear.{side}"]
        midpoint = (Vector(head) + Vector(tail)) / 2
        angle = 0.27 * sign
        length = (Vector(head) - Vector(tail)).length
        ellipsoid("Outer Ear", midpoint, (0.099, 0.047, length * 0.61), rust_dark,
                  f"ear.{side}", (0, angle, 0))
        ellipsoid("Inner Ear", midpoint + Vector((0, -0.035, 0.012)),
                  (0.073, 0.019, length * 0.48), cream, f"ear.{side}", (0, angle, 0))
        ellipsoid("Cheek", (sign * 0.127, -0.172, 0.990), (0.074, 0.065, 0.055), cream, "head")
        ellipsoid("Eye Patch", (sign * 0.126, -0.205, 1.074), (0.053, 0.027, 0.063),
                  cream, "head", (0, 0, sign * 0.48))
        ellipsoid("Eye", (sign * 0.132, -0.225, 1.077), (0.030, 0.021, 0.049), eye, "head",
                  (0, 0, sign * 0.50))
        ellipsoid("Eye Glint", (sign * 0.126, -0.245, 1.098), (0.010, 0.006, 0.011), light_metal, "head")
        ellipsoid("Runner Shoe", (sign * 0.141, -0.060, 0.089), (0.086, 0.156, 0.067),
                  cream, f"foot.{side}")
        block("Shoe Sole", (sign * 0.141, -0.061, 0.033), (0.163, 0.292, 0.055),
              leather, f"foot.{side}", 0.024)
        block("Shoe Band", (sign * 0.141, -0.081, 0.143), (0.136, 0.047, 0.018),
              teal, f"foot.{side}", 0.008)
        for name, radius in (("thigh", 0.10), ("shin", 0.062),
                             ("upperArm", 0.060), ("forearm", 0.045)):
            bone = f"{name}.{side}"
            start, end, _ = skeleton[bone]
            limb(name, start, end, radius, rust, bone)
        ellipsoid("Driving Glove", (0.264 * sign, -0.063, 0.54), (0.058, 0.062, 0.054),
                  leather, f"forearm.{side}")
    limb("Tail Base", *skeleton["tail"][:2], 0.042, rust, "tail")
    limb("Tail Tip", *skeleton["tailTip"][:2], 0.033, rust, "tailTip")
    limb("Tail Tuft", (0.01, 0.622, 0.50), (0.029, 0.816, 0.599), 0.066, cream, "tailTip")

    fixture("Jerboa Skin", parts)
    skin = bpy.context.object
    skin.parent = rig
    modifier = skin.modifiers.new("Courier Skin", "ARMATURE")
    modifier.object = rig

    motions(rig)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtSource/Jerboa.blend"))
    build_palette_atlas(skin)
    export_fbx(DEST / "Jerboa.fbx", [rig, skin], animated=True)


def build_palette_atlas(skin):
    # Every face of this flat-color design samples the center of its color block.
    # Export one draw call per runner while leaving separate materials in the .blend.
    size, cell = 64, 16
    palette = list(skin.data.materials)
    image = bpy.data.images.new("JerboaPalette", width=size, height=size, alpha=False)
    pixels = [1.0] * (size * size * 4)
    for index, surface in enumerate(palette):
        x0, y0 = index % 4 * cell, index // 4 * cell
        color = tuple(surface.diffuse_color)
        for y in range(y0, y0 + cell):
            for x in range(x0, x0 + cell):
                start = (y * size + x) * 4
                pixels[start:start + 4] = color
    image.pixels.foreach_set(pixels)
    image.update()
    image.filepath_raw = str(DEST / "JerboaPalette.png")
    image.file_format = "PNG"
    image.save()

    uv = skin.data.uv_layers.active or skin.data.uv_layers.new(name="Palette UV")
    for face in skin.data.polygons:
        index = face.material_index
        center = ((index % 4 + 0.5) / 4, (index // 4 + 0.5) / 4)
        for loop in face.loop_indices:
            uv.uv[loop].vector = center

    surface = material("Jerboa Palette", (1, 1, 1))
    texture = surface.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.interpolation = "Closest"
    surface.node_tree.links.new(texture.outputs["Color"],
                                surface.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    skin.data.materials.clear()
    skin.data.materials.append(surface)


def motions(rig):
    # Rotation around the model's world X axis, expressed in each bone rest frame.
    def pose(rotations, lift):
        for bone in rig.pose.bones:
            bone.rotation_mode = "QUATERNION"
            angle = rotations.get(bone.name, 0)
            rest = bone.bone.matrix_local.to_quaternion()
            bone.rotation_quaternion = rest.inverted() @ Quaternion((1, 0, 0), angle) @ rest
            bone.location = rest.inverted() @ Vector((0, 0, lift)) if bone.name == "hips" else (0, 0, 0)

    def action(name, keys):
        action_data = bpy.data.actions.new(name)
        action_data.use_fake_user = True
        rig.animation_data_create()
        rig.animation_data.action = action_data
        for frame, lift, rotations in keys:
            pose(rotations, lift)
            for bone in rig.pose.bones:
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
            rig.pose.bones["hips"].keyframe_insert("location", frame=frame, group="hips")
        return action_data

    scene = bpy.context.scene
    scene.render.fps = 30
    scene.frame_start, scene.frame_end = 1, 30
    idle = action("Idle", [
        (1, 0, {}), (23, 0.014, {"ear.L": 0.12, "tail": -0.07}),
        (46, 0, {"ear.R": -0.11, "tailTip": 0.12}), (61, 0, {}),
    ])

    def stride(direction, passing):
        return {
            "thigh.L": direction * (0.16 if passing else 0.68),
            "thigh.R": direction * (-0.10 if passing else -0.72),
            "shin.L": (0.63 + 0.45 * direction) if passing else (0.43 - 0.24 * direction),
            "shin.R": (0.63 - 0.45 * direction) if passing else (0.43 + 0.24 * direction),
            "foot.L": -0.29, "foot.R": -0.29,
            "upperArm.L": -direction * 0.75, "upperArm.R": direction * 0.75,
            "forearm.L": -0.82, "forearm.R": -0.78, "spine": 0.24,
            "head": -0.12, "ear.L": 0.14 if passing else -0.14,
            "ear.R": 0.16 if passing else -0.13, "tail": direction * 0.11,
        }

    action("Run", [
        (1, 0.014, stride(1, False)), (6, 0.085, stride(1, True)),
        (11, 0.014, stride(-1, False)), (16, 0.085, stride(-1, True)),
        (21, 0.014, stride(1, False)),
    ])
    tuck = {"thigh.L": -0.95, "thigh.R": -0.59, "shin.L": 1.12, "shin.R": 1.04,
            "upperArm.L": -0.55, "upperArm.R": 0.27, "spine": 0.34, "ear.L": -0.29, "tail": 0.36}
    action("Jump", [(1, -0.025, stride(1, True)), (8, 0.07, tuck), (20, 0.045, tuck)])
    action("Fall", [(1, 0.045, tuck), (14, 0, {"upperArm.L": 0.32, "upperArm.R": 0.35,
                                                "shin.L": 0.19, "shin.R": 0.18, "tail": 0.51})])
    action("Land", [(1, 0, tuck), (5, -0.036, {"spine": 0.46, "thigh.L": -0.28,
                                              "thigh.R": -0.29, "shin.L": 0.52, "shin.R": 0.53}),
                    (13, 0.02, stride(1, True))])
    action("Stumble", [(1, 0.02, stride(1, True)), (7, -0.035, {"spine": 0.66, "head": -0.34,
                                                               "upperArm.L": -1.55, "upperArm.R": 1.2,
                                                               "ear.L": 0.46, "tail": -0.52}),
                        (21, 0.01, stride(-1, True))])
    action("Finish", [(1, 0.01, stride(1, True)), (10, 0.25, {"upperArm.L": -2.35, "upperArm.R": -2.08,
                                                              "thigh.L": -0.44, "thigh.R": 0.23, "tail": 0.36}),
                      (25, 0, {"upperArm.L": -1.58, "upperArm.R": -1.46}), (48, 0, {})])
    action("Gadget", [(1, 0.01, stride(1, True)), (7, 0.05, {"upperArm.L": -1.12, "forearm.L": -1.2,
                                                              "spine": 0.32, "thigh.R": 0.65}),
                      (17, 0.02, stride(-1, True))])
    rig.animation_data.action = idle
    scene.frame_set(1)


if __name__ == "__main__":
    build_runner()

