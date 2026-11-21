"""Shared, meter-scale Blender construction and export helpers."""
import bpy


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.preferences.filepaths.save_version = 0


def material(name, color, roughness=0.78):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*color, 1)
    result.use_nodes = True
    node = result.node_tree.nodes.get("Principled BSDF")
    node.inputs["Base Color"].default_value = result.diffuse_color
    node.inputs["Roughness"].default_value = roughness
    return result


def box(name, at, size, surface, bevel=0.025):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        modifier = obj.modifiers.new("Edge Bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 3
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.materials.append(surface)
    return obj


def sphere(name, at, size, surface, segments=20, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                        radius=1, location=at)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    obj.data.materials.append(surface)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def mesh(name, vertices, faces, surface):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    obj.data.materials.append(surface)
    bpy.context.collection.objects.link(obj)
    return obj


def fixture(name, parts, origin=(0, 0, 0)):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.scene.cursor.location = origin
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    # Joining by silhouette introduces repeated slots shared by several parts.
    used = []
    index_map = []
    for item in obj.data.materials:
        if item not in used:
            used.append(item)
        index_map.append(used.index(item))
    face_materials = [index_map[polygon.material_index] for polygon in obj.data.polygons]
    obj.data.materials.clear()
    for item in used:
        obj.data.materials.append(item)
    for polygon, index in zip(obj.data.polygons, face_materials):
        polygon.material_index = index
    return obj


def export_fbx(path, objects, animated=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        add_leaf_bones=False, use_armature_deform_only=True, path_mode="RELATIVE",
        bake_anim=animated, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=animated, bake_anim_simplify_factor=0,
        object_types={"ARMATURE", "MESH", "EMPTY"},
    )

