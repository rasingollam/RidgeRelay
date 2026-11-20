"""Build Ridge Relay's editable runner source and Unity FBX export."""
from math import acos, atan2, cos, hypot, pi, sin
from pathlib import Path
import sys

import bpy
from mathutils import Euler, Vector

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


def finish_keys():
    wave = {
        "spine": (-0.05, 0.04, -0.08), "head": (-0.08, 0.08, 0.10),
        "upperArm.L": (-1.85, 0.35, -0.22), "forearm.L": (-0.40, 0.15, -0.35),
        "upperArm.R": (0.12, -0.16, 0.12), "forearm.R": -0.30,
        "thigh.L": -0.10, "thigh.R": -0.10, "shin.L": 0.22, "shin.R": 0.22,
        "ear.L": -0.12, "ear.R": 0.12, "tail": (0.12, 0, 0.20),
    }
    other_wave = {**wave, "forearm.L": (-0.48, -0.20, 0.40),
                  "head": (-0.05, -0.06, -0.06), "tail": (0.16, 0, -0.20)}
    crouch = {**wave, "spine": 0.28, "thigh.L": -0.48, "thigh.R": -0.48,
              "shin.L": 0.85, "shin.R": 0.85, "ear.L": 0.18, "ear.R": 0.12}
    airborne = {**other_wave, "upperArm.R": (-1.65, -0.25, 0.20),
                "forearm.R": -0.45, "thigh.L": -0.30, "thigh.R": -0.22,
                "shin.L": 0.42, "shin.R": 0.36, "ear.L": -0.28, "ear.R": -0.22}
    return [(1, 0, wave), (9, 0, other_wave), (16, 0, wave),
            (22, -0.09, crouch), (30, 0.32, airborne), (35, 0.26, airborne),
            (43, -0.09, crouch), (50, 0, other_wave), (61, 0, wave)]

def motions(rig):
    # Pose in model-space axes, then convert into each bone's rest axes.
    def pose(rotations, lift):
        for bone in rig.pose.bones:
            bone.rotation_mode = "QUATERNION"
            angles = rotations.get(bone.name, 0)
            if isinstance(angles, (int, float)):
                angles = (angles, 0, 0)
            rest = bone.bone.matrix_local.to_quaternion()
            bone.rotation_quaternion = rest.inverted() @ Euler(angles).to_quaternion() @ rest
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
    scene.frame_start, scene.frame_end = 1, 61
    idle = action("Idle", [
        (1, 0, {}), (16, 0.013, {"spine": -0.035, "ear.L": 0.07, "tail": (0, 0.10, 0.02)}),
        (31, 0, {"head": (0.015, -0.055, 0.12), "ear.R": (-0.06, 0.16, 0.07), "tailTip": 0.14}),
        (38, 0.010, {"head": (-0.025, -0.025, 0.08), "ear.R": (0.07, -0.04, 0.02)}),
        (48, 0.016, {"spine": -0.024, "tailTip": (-0.045, -0.09, 0.04)}), (61, 0, {}),
    ])

    def running_pose(phase):
        wave = sin(phase * 2 * pi)
        bounce = cos((phase - 0.14) * 4 * pi)
        lift = -0.072 - 0.022 * bounce
        rotations = {
            "spine": (0.31 + 0.06 * bounce, 0.055 * wave, 0.085 * wave),
            "head": (-0.16 - 0.027 * bounce, -0.04 * wave, -0.063 * wave),
            "ear.L": (-0.14 + 0.19 * cos(4 * pi * phase - 1.2), 0.13 * wave, 0.04 * wave),
            "ear.R": (-0.17 + 0.17 * cos(4 * pi * phase - 1.8), 0.10 * wave, 0.05 * wave),
            "tail": (-0.04 + 0.14 * cos(4 * pi * phase - 0.65), 0.06 * wave, -0.24 * wave),
            "tailTip": (-0.04 + 0.21 * cos(4 * pi * phase - 1.15), 0, -0.14 * sin(2 * pi * phase - 0.9)),
        }
        for side, offset, sign in (("L", 0, 1), ("R", 0.5, -1)):
            foot_phase = (phase + offset) % 1.0
            # Solve the hip, knee and boot angles for a driven ankle arc.
            # Ground contact moves straight backward, with toe-off leading the recovery.
            stance = 0.39
            if foot_phase < stance:
                contact = foot_phase / stance
                y = -0.254 + 0.512 * contact
                z = 0.098 + 0.059 * contact ** 4
                pitch = -0.035 + 0.30 * contact ** 3
            else:
                recovery = (foot_phase - stance) / (1 - stance)
                easing = recovery * recovery * (3 - 2 * recovery)
                y = 0.258 - 0.512 * easing + 0.05 * sin(2 * pi * recovery)
                z = 0.098 + 0.059 * (1 - recovery) + 0.245 * sin(pi * recovery) ** 1.25
                pitch = 0.265 - 0.30 * easing + 0.43 * sin(pi * recovery)
            thigh, shin = hypot(0.060, 0.29), hypot(0.084, 0.19)
            drop = 0.58 + lift - z
            reach = min(thigh + shin - 0.0001, hypot(y, drop))
            lean = atan2(y, drop)
            upper = lean - acos((thigh * thigh + reach * reach - shin * shin) / (2 * thigh * reach))
            lower = lean + acos((shin * shin + reach * reach - thigh * thigh) / (2 * shin * reach))
            thigh_delta = upper - atan2(-0.060, 0.29)
            shin_delta = lower - atan2(0.084, 0.19)
            rotations[f"thigh.{side}"] = thigh_delta
            rotations[f"shin.{side}"] = shin_delta - thigh_delta
            rotations[f"foot.{side}"] = pitch - shin_delta
            arm = cos(2 * pi * foot_phase + 0.18)
            rotations[f"upperArm.{side}"] = (0.26 + 0.66 * arm, 0.09 * sign, 0.03 * sign)
            rotations[f"forearm.{side}"] = -0.82 - 0.25 * sin(2 * pi * foot_phase + 0.5)
        return lift, rotations

    action("Run", [(frame + 1, *running_pose((frame % 18) / 18)) for frame in range(19)])
    launch = {"thigh.L": -0.74, "thigh.R": 0.56, "shin.L": 0.95, "shin.R": 0.2,
              "foot.R": -0.38, "upperArm.L": 0.66, "upperArm.R": -1.15, "forearm.R": -0.74,
              "spine": 0.25, "head": -0.28, "ear.L": -0.37, "ear.R": -0.30, "tail": 0.29}
    tuck = {"thigh.L": -0.98, "thigh.R": -0.75, "shin.L": 1.04, "shin.R": 1.23,
            "foot.L": -0.21, "foot.R": -0.43, "upperArm.L": (-0.63, 0.20, -0.15),
            "upperArm.R": (-0.78, -0.26, 0.18), "forearm.L": -0.42, "forearm.R": -0.57,
            "spine": 0.30, "head": -0.21, "ear.L": 0.24, "ear.R": 0.12, "tail": -0.21, "tailTip": 0.32}
    brace = {"thigh.L": -0.19, "thigh.R": 0.02, "shin.L": 0.16, "shin.R": 0.22,
             "foot.L": -0.17, "foot.R": -0.2, "spine": 0.13, "head": -0.10,
             "upperArm.L": (0.22, 0.47, 0.09), "upperArm.R": (0.28, -0.38, -0.11),
             "forearm.L": -0.42, "forearm.R": -0.35, "ear.L": 0.28, "ear.R": 0.17, "tail": -0.24}
    action("Jump", [(1, -0.075, launch), (4, -0.015, launch), (8, 0.035, tuck), (16, 0.027, tuck)])
    action("Fall", [(1, 0.027, tuck), (7, 0.012, brace), (14, 0, brace)])
    compress = {"spine": 0.52, "head": -0.31, "thigh.L": -0.50, "thigh.R": -0.37,
                "shin.L": 0.85, "shin.R": 0.81, "foot.L": -0.36, "foot.R": -0.44,
                "upperArm.L": (0.36, 0.28, -0.12), "upperArm.R": (0.68, -0.35, 0.06),
                "forearm.L": -0.64, "forearm.R": -0.73, "ear.L": 0.40, "ear.R": 0.23,
                "tail": -0.55, "tailTip": 0.3}
    action("Land", [(1, -0.010, brace), (4, -0.105, compress), (9, *running_pose(0.46)),
                    (13, *running_pose(0.69))])
    recoil = {"spine": (-0.22, 0.29, -0.21), "head": (0.30, -0.13, 0.16),
              "upperArm.L": (-0.25, 0.98, -0.41), "upperArm.R": (0.56, -0.60, 0.42),
              "forearm.L": -0.63, "forearm.R": -0.45, "thigh.L": -0.48, "thigh.R": 0.25,
              "shin.L": 0.66, "shin.R": 0.79, "ear.L": -0.47, "ear.R": -0.31,
              "tail": (0.4, 0.14, -0.45), "tailTip": (0.20, -0.03, 0.26)}
    catch = {**compress, "spine": (0.58, -0.24, 0.20), "head": (-0.36, 0.19, -0.09),
             "thigh.L": 0.02, "thigh.R": -0.65, "shin.L": 0.54, "shin.R": 0.42,
             "upperArm.L": (-0.7, 0.47, 0.1), "upperArm.R": (0.64, -0.29, 0.10),
             "tail": (-0.30, 0, 0.72)}
    action("Stumble", [(1, *running_pose(0.25)), (4, -0.058, recoil), (9, -0.116, catch),
                        (14, *running_pose(0.61)), (21, *running_pose(0.9))])
    action("Finish", finish_keys())
    lift, throw = running_pose(0.35)
    throw.update({"spine": (0.28, -0.23, 0.31), "head": (-0.15, 0.09, -0.16),
                  "upperArm.R": (-1.42, -0.40, 0.07), "forearm.R": -0.21,
                  "upperArm.L": (0.57, 0.23, -0.18), "tail": (0.11, 0, -0.33)})
    action("Gadget", [(1, *running_pose(0.11)), (6, lift, throw), (10, lift, throw),
                      (17, *running_pose(0.85))])
    rig.animation_data.action = idle
    scene.frame_set(1)


if __name__ == "__main__":
    build_runner()

