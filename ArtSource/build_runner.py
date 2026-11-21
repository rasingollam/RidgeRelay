"""Build Ridge Relay's editable runner source and Unity FBX export."""
from math import acos, atan2, cos, hypot, pi, sin
from pathlib import Path
from random import Random
from bisect import bisect_left
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
    cream = material("Dune Cream", (0.96, 0.86, 0.67))
    teal = material("Courier Teal", (0.022, 0.28, 0.26))
    green = material("Vest Shade", (0.017, 0.16, 0.15))
    gold = material("Marigold Canvas", (0.93, 0.56, 0.17))
    leather = material("Boot Rubber", (0.063, 0.042, 0.042))
    light_metal = material("Buckles", (0.98, 0.96, 0.86))
    eye = material("Eyes and Nose", (0.018, 0.012, 0.012))
    tongue = material("Tongue Rose", (0.70, 0.20, 0.22))
    ear_rose = material("Inner Ear Rose", (0.92, 0.60, 0.53))
    iris = material("Hazel Iris", (0.34, 0.13, 0.035))

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
        if bone == "head" and part.name.startswith(("Eye", "Pupil", "Hazel Iris")) and not part.name.startswith("Eye Patch"):
            blink = part.vertex_groups.new(name="Blink.L" if part.location.x > 0 else "Blink.R")
            blink.add([vertex.index for vertex in part.data.vertices], 1.0, "REPLACE")
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
    ellipsoid("Chest Fur", (0, -0.003, 0.751), (0.210, 0.161, 0.199), rust, "spine")
    ellipsoid("Cream Belly", (0, -0.143, 0.746), (0.125, 0.041, 0.155), cream, "spine")
    ellipsoid("Neck Fur", (0, -0.014, 0.892), (0.164, 0.133, 0.052), rust, "spine")
    block("Courier Pack", (0, 0.194, 0.758), (0.294, 0.176, 0.322), gold, "spine", 0.038)
    block("Pack Flap", (0, 0.274, 0.829), (0.252, 0.028, 0.132), gold, "spine")
    for side in (-1, 1):
        ellipsoid("Backpack Harness", (side * 0.127, -0.131, 0.812), (0.026, 0.023, 0.108),
                  gold, "spine", (0, -0.28 * side, 0))
    block("Pack Pocket", (0, 0.300, 0.670), (0.220, 0.046, 0.105), gold, "spine", 0.02)
    for side in (-1, 1):
        block("Leather Pack Band", (side * 0.087, 0.311, 0.755), (0.021, 0.014, 0.220),
              leather, "spine", 0.004)
        block("Pack Buckle", (side * 0.087, 0.324, 0.786), (0.046, 0.014, 0.042),
              light_metal, "spine", 0.006)
        block("Buckle Opening", (side * 0.087, 0.333, 0.786), (0.022, 0.006, 0.021),
              leather, "spine", 0.002)
    for stitch in range(12):
        block("Flap Stitch", ((stitch - 5.5) * 0.019, 0.293, 0.866),
              (0.009, 0.006, 0.004), cream, "spine", 0)

    ellipsoid("Head", (0, -0.044, 1.04), (0.232, 0.206, 0.195), rust, "head")
    ellipsoid("Muzzle", (0, -0.194, 0.988), (0.151, 0.107, 0.081), cream, "head")
    ellipsoid("Nose", (0, -0.286, 1.023), (0.033, 0.027, 0.021), eye, "head")
    for side, sign in (("L", 1), ("R", -1)):
        head, tail, _ = skeleton[f"ear.{side}"]
        midpoint = (Vector(head) + Vector(tail)) / 2
        angle = 0.27 * sign
        length = (Vector(head) - Vector(tail)).length
        ellipsoid("Outer Ear", midpoint, (0.120, 0.047, length * 0.65), rust_dark,
                  f"ear.{side}", (0, angle, 0))
        ellipsoid("Inner Ear", midpoint + Vector((0, -0.035, 0.012)),
                  (0.095, 0.019, length * 0.52), ear_rose, f"ear.{side}", (0, angle, 0))
        ellipsoid("Cheek", (sign * 0.127, -0.172, 0.990), (0.074, 0.065, 0.055), cream, "head")
        ellipsoid("Eye", (sign * 0.132, -0.225, 1.077), (0.051, 0.030, 0.066), eye, "head",
                  (0, 0, sign * 0.50))
        ellipsoid("Hazel Iris", (sign * 0.132, -0.249, 1.078), (0.033, 0.015, 0.049), iris,
                  "head", (0, 0, sign * 0.5))
        ellipsoid("Pupil", (sign * 0.132, -0.263, 1.078), (0.024, 0.008, 0.039), eye,
                  "head", (0, 0, sign * 0.5))
        ellipsoid("Eye Glint", (sign * 0.120, -0.276, 1.101), (0.011, 0.006, 0.012),
                  light_metal, "head")
        ellipsoid("Hind Paw", (sign * 0.141, -0.060, 0.089), (0.078, 0.156, 0.067),
                  rust, f"foot.{side}")
        for toe in (-1, 0, 1):
            ellipsoid("Paw Toe", (sign * 0.141 + toe * 0.035, -0.180, 0.080),
                      (0.022, 0.050, 0.027), cream, f"foot.{side}")
        for name, radius in (("thigh", 0.10), ("shin", 0.062),
                             ("upperArm", 0.060), ("forearm", 0.045)):
            bone = f"{name}.{side}"
            start, end, _ = skeleton[bone]
            limb(name, start, end, radius, rust, bone)
        ellipsoid("Front Paw", (0.264 * sign, -0.063, 0.54), (0.058, 0.062, 0.054),
                  rust, f"forearm.{side}")
        for finger in (-1, 0, 1):
            ellipsoid("Paw Finger", (0.264 * sign + finger * 0.024, -0.074, 0.505),
                      (0.015, 0.023, 0.032), cream, f"forearm.{side}")
    limb("Tail Base", *skeleton["tail"][:2], 0.042, rust, "tail")
    limb("Tail Tip", *skeleton["tailTip"][:2], 0.033, rust, "tailTip")
    limb("Tail Tuft", (0.01, 0.55, 0.49), (0.029, 0.90, 0.62), 0.11, cream, "tailTip")
    # Facial details remain on the head bone so the expression follows every action.
    ellipsoid("Smile", (0, -0.305, 0.953), (0.073, 0.014, 0.024), eye, "head")
    ellipsoid("Tongue", (0, -0.319, 0.941), (0.029, 0.004, 0.009), tongue, "head")
    for sign in (-1, 1):
        block("Front Tooth", (sign * 0.012, -0.321, 0.966), (0.020, 0.009, 0.016),
              light_metal, "head", 0.004)
        limb("Whisker", (sign * 0.129, -0.255, 0.977),
             (sign * 0.246, -0.28, 0.990), 0.003, cream, "head")
        limb("Whisker", (sign * 0.133, -0.253, 0.966),
             (sign * 0.236, -0.27, 0.949), 0.003, cream, "head")

    # Fuse the fur silhouette before skinning so shoulders, hips and joints have
    # a continuous organic surface. Keep the gear and facial details crisp.
    fur = [part for part in parts if part.data.materials[0] in (rust, rust_dark)]
    details = [part for part in parts if part not in fur]
    donor = fixture("Fur weight source", fur)
    body = donor.copy()
    body.data = donor.data.copy()
    bpy.context.collection.objects.link(body)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    remesh = body.modifiers.new("Continuous Fur Surface", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = 0.009
    remesh.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    smooth = body.modifiers.new("Organic Transitions", "SMOOTH")
    smooth.factor = 0.8
    smooth.iterations = 4
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    decimate = body.modifiers.new("Mobile Surface Budget", "DECIMATE")
    decimate.ratio = min(1, 15000 / len(body.data.polygons))
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    transfer = body.modifiers.new("Rig Weight Transfer", "DATA_TRANSFER")
    transfer.object = donor
    transfer.use_vert_data = True
    transfer.data_types_verts = {"VGROUP_WEIGHTS"}
    transfer.vert_mapping = "POLYINTERP_NEAREST"
    transfer.layers_vgroup_select_src = "ALL"
    transfer.layers_vgroup_select_dst = "NAME"
    bpy.ops.object.modifier_apply(modifier=transfer.name)
    smooth_skin_weights(body)
    for face in body.data.polygons:
        face.use_smooth = True
    bpy.data.objects.remove(donor, do_unlink=True)
    fur_details = [make_fur_tufts(body, rust, 2800, 0.016)]
    for part in details:
        if part.name.startswith("Tail Tuft"):
            fur_details.append(make_fur_tufts(part, cream, 700, 0.035))
    fixture("Jerboa Skin", [body] + details + fur_details)
    skin = bpy.context.object
    # These stable slots are shared by the runtime fur and backpack palette.
    ordered = [rust, teal, cream, green, gold, leather, light_metal, eye, rust_dark,
               tongue, ear_rose, iris]
    face_materials = [skin.data.materials[face.material_index] for face in skin.data.polygons]
    skin.data.materials.clear()
    for surface in ordered:
        skin.data.materials.append(surface)
    for face, surface in zip(skin.data.polygons, face_materials):
        face.material_index = ordered.index(surface)
    skin.shape_key_add(name="Basis")
    for side in ("L", "R"):
        group = skin.vertex_groups.get("Blink." + side)
        key = skin.shape_key_add(name="Blink." + side)
        for vertex in skin.data.vertices:
            if any(weight.group == group.index for weight in vertex.groups):
                key.data[vertex.index].co.z = 1.078 + (vertex.co.z - 1.078) * 0.12
    skin.parent = rig
    modifier = skin.modifiers.new("Courier Skin", "ARMATURE")
    modifier.object = rig

    motions(rig)
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtSource/Jerboa.blend"))
    build_palette_atlas(skin)
    export_fbx(DEST / "Jerboa.fbx", [rig, skin], animated=True)


def smooth_skin_weights(body):
    neighbors = [set() for vertex in body.data.vertices]
    for edge in body.data.edges:
        a,b = edge.vertices
        neighbors[a].add(b)
        neighbors[b].add(a)
    weights = [{group.group:group.weight for group in vertex.groups} for vertex in body.data.vertices]
    for iteration in range(4):
        blended = []
        for i,adjacent in enumerate(neighbors):
            result = {group:weight*0.5 for group,weight in weights[i].items()}
            for neighbor in adjacent:
                for group,weight in weights[neighbor].items():
                    result[group] = result.get(group,0) + weight*0.5/max(1,len(adjacent))
            strongest = sorted(result.items(),key=lambda item:-item[1])[:4]
            total = sum(weight for _,weight in strongest)
            blended.append({group:weight/total for group,weight in strongest} if total else weights[i])
        weights = blended
    indices = list(range(len(body.data.vertices)))
    for group in body.vertex_groups: group.remove(indices)
    for i,blend in enumerate(weights):
        for group,weight in blend.items():
            if weight>0.001: body.vertex_groups[group].add([i],weight,"REPLACE")


def make_fur_tufts(source, surface, count, length):
    """Short weighted fiber tufts soften the silhouette without transparent shells."""
    source.data.calc_loop_triangles()
    triangles = list(source.data.loop_triangles)
    areas, total = [], 0
    for triangle in triangles:
        total += triangle.area
        areas.append(total)
    random = Random(120 + count)
    vertices, faces, weights, normals = [], [], [], []
    transform = source.matrix_world
    normal_transform = transform.to_3x3().inverted().transposed()
    for i in range(count):
        triangle = triangles[min(len(triangles)-1, bisect_left(areas, random.random()*total))]
        a, b = random.random(), random.random()
        if a+b>1: a,b=1-a,1-b
        bary = (a,b,1-a-b)
        points = [source.data.vertices[index] for index in triangle.vertices]
        point = transform @ sum((v.co*w for v,w in zip(points,bary)), Vector())
        normal = (normal_transform @ sum((v.normal*w for v,w in zip(points,bary)), Vector())).normalized()
        tangent = normal.cross(Vector((0.31,0.81,0.17))).normalized()
        tangent = (tangent*cos(i*2.4)+normal.cross(tangent)*sin(i*2.4)).normalized()
        size = length*random.uniform(0.6,1.25)
        width = size*0.075
        lean = Vector((0,0,-size*0.25))
        start = len(vertices)
        vertices.extend([point-tangent*width, point+tangent*width,
                         point+normal*size*0.58+lean-tangent*width*0.55,
                         point+normal*size*0.58+lean+tangent*width*0.55,
                         point+normal*size+lean*2])
        normals.extend([tuple(normal)]*5)
        faces.extend([(start,start+1,start+3,start+2),(start+2,start+3,start+4),
                      (start+2,start+3,start+1,start),(start+4,start+3,start+2)])
        blend = {}
        for vertex, weight in zip(points,bary):
            for group in vertex.groups:
                name = source.vertex_groups[group.group].name
                blend[name] = blend.get(name,0) + group.weight*weight
        weights.append(blend)
    data = bpy.data.meshes.new("Silhouette Fur")
    data.from_pydata(vertices,[],faces)
    data.materials.append(surface)
    result = bpy.data.objects.new("Short Fur Tufts",data)
    bpy.context.collection.objects.link(result)
    groups = {name: result.vertex_groups.new(name=name)
              for name in sorted(set(name for blend in weights for name in blend))}
    for i,blend in enumerate(weights):
        for name,weight in blend.items():
            if weight>0.001: groups[name].add(list(range(i*5,i*5+5)),weight,"REPLACE")
    for face in data.polygons: face.use_smooth=True
    data.normals_split_custom_set_from_vertices(normals)
    return result


def build_palette_atlas(skin):
    # Material cells keep color customization cheap while UVs carry surface detail.
    size, cell = 512, 128
    palette = list(skin.data.materials)
    image = bpy.data.images.new("JerboaPalette", width=size, height=size, alpha=False)
    pixels = [1.0] * (size * size * 4)
    normals = [0.5,0.5,1,1] * (size * size)
    for index, surface in enumerate(palette):
        x0, y0 = index % 4 * cell, index // 4 * cell
        color = tuple(surface.diffuse_color)
        for y in range(y0, y0 + cell):
            for x in range(x0, x0 + cell):
                start = (y * size + x) * 4
                grain = 1.0
                if surface.name not in ("Eyes and Nose", "Buckles"):
                    strand = sin(x * 1.9 + sin(y * 0.19) * 1.4)
                    weave = sin(y * 2.4) * sin(x * 2.7)
                    grain += 0.025 * strand + 0.012 * weave
                pixels[start:start + 4] = [min(1, channel * grain) for channel in color[:3]] + [1]
                dx=dy=0
                if index in (0,2,8):
                    phase=x*1.9+sin(y*0.19)*1.4
                    dx=0.16*1.9*cos(phase)
                    dy=0.16*cos(phase)*cos(y*0.19)*0.266
                elif index==4:
                    dx=0.08*2.7*cos(x*2.7)*sin(y*2.4)
                    dy=0.08*2.4*sin(x*2.7)*cos(y*2.4)
                normal=Vector((-dx,-dy,1)).normalized()
                normals[start:start+4]=[normal.x*0.5+0.5,normal.y*0.5+0.5,normal.z*0.5+0.5,1]
    image.pixels.foreach_set(pixels)
    image.update()
    image.filepath_raw = str(DEST / "JerboaPalette.png")
    image.file_format = "PNG"
    image.save()
    detail=bpy.data.images.new("JerboaFurNormal",width=size,height=size,alpha=False)
    detail.colorspace_settings.name="Non-Color"
    detail.pixels.foreach_set(normals)
    detail.filepath_raw=str(DEST/"JerboaFurNormal.png")
    detail.file_format="PNG"
    detail.save()

    # FBX uses UV channel zero. Remeshing and joining can retain a second active
    # UV set, so replace every source set with the single exported atlas channel.
    while skin.data.uv_layers:
        skin.data.uv_layers.remove(skin.data.uv_layers[0])
    uv = skin.data.uv_layers.new(name="Palette UV")
    for face in skin.data.polygons:
        index = face.material_index
        horizontal = abs(face.normal.z) > 0.7
        for loop in face.loop_indices:
            point = skin.data.vertices[skin.data.loops[loop].vertex_index].co
            across = point.x * 5 % 1
            along = (point.y if horizontal else point.z) * 5 % 1
            uv.uv[loop].vector = ((index % 4 + 0.08 + across * 0.84) / 4,
                                   (index // 4 + 0.08 + along * 0.84) / 4)

    surface = material("Jerboa Palette", (1, 1, 1))
    texture = surface.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.interpolation = "Linear"
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
    for name in ("Sprint", "Uphill", "Downhill"):
        keys = []
        for frame in range(19):
            lift, rotations = running_pose((frame % 18) / 18)
            spine = list(rotations["spine"])
            head = list(rotations["head"])
            if name == "Sprint":
                spine[0] += 0.12
                head[0] -= 0.08
                rotations["ear.L"] = (-0.42, 0.05, 0.05)
                rotations["ear.R"] = (-0.47, -0.05, -0.05)
            elif name == "Uphill":
                spine[0] += 0.15
                head[0] -= 0.08
                lift -= 0.025
            else:
                spine[0] -= 0.16
                head[0] += 0.06
                rotations["upperArm.L"] = (0.10, 0.40, -0.22)
                rotations["upperArm.R"] = (0.10, -0.40, 0.22)
                rotations["tail"] = (0.18, 0, 0.15)
            rotations["spine"] = tuple(spine)
            rotations["head"] = tuple(head)
            keys.append((frame + 1, lift, rotations))
        action(name, keys)
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
    reach = {"spine": (0.20, 0, 0), "head": -0.15,
             "upperArm.L": (-1.12, 0.30, -0.18), "upperArm.R": (-1.12, -0.30, 0.18),
             "forearm.L": -0.25, "forearm.R": -0.25}
    pull = {**reach, "spine": -0.08, "upperArm.L": (-0.72, 0.55, -0.18),
            "upperArm.R": (-0.72, -0.55, 0.18), "forearm.L": -1.15, "forearm.R": -1.15}
    action("CastMagnet", [(1, 0, {}), (5, 0, reach), (11, 0, pull), (17, 0, {})])
    action("CastRocket", [(1, 0, {}), (5, 0, {**throw, "head": -0.20}),
                          (10, 0, {**throw, "upperArm.R": -2.1, "forearm.R": -0.2}),
                          (17, 0, {})])
    burst = {**reach, "spine": -0.12, "upperArm.L": (-0.30, 0.35, -0.85),
             "upperArm.R": (-0.30, -0.35, 0.85), "forearm.L": -0.2, "forearm.R": -0.2}
    action("CastShock", [(1, 0, pull), (6, 0, pull), (10, 0, burst), (17, 0, {})])
    rig.animation_data.action = idle
    scene.frame_set(1)


if __name__ == "__main__":
    build_runner()

