"""Build the rounded canyon vegetation kit for RidgeRelay."""
from pathlib import Path
from math import pi, sin, cos
from random import Random
import sys
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from mesh_tools import material, reset_scene, sphere, fixture, export_fbx


def make_kit():
    reset_scene()
    bark = material("Warm tree bark", (0.31, 0.15, 0.065))
    leaves = material("Canyon canopy", (0.19, 0.36, 0.105))
    leaf_light = material("Sunlit canopy", (0.36, 0.48, 0.12))
    leaf_shadow = material("Canopy shade", (0.10, 0.23, 0.08))
    sage = material("River sage", (0.24, 0.42, 0.25))
    flower = material("Lavender flowers", (0.43, 0.25, 0.54))
    grass = material("Bank grass", (0.29, 0.39, 0.11))
    kits = []

    def branch(start, end, radius):
        delta = Vector(end) - Vector(start)
        bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=radius, radius2=radius * 0.66,
                                       depth=delta.length, location=(Vector(start) + Vector(end)) * 0.5)
        part = bpy.context.object
        part.rotation_mode = "QUATERNION"
        part.rotation_quaternion = delta.to_track_quat("Z", "Y")
        part.data.materials.append(bark)
        for face in part.data.polygons:
            face.use_smooth = True
        return part

    random = Random(781)
    leaf_vertices,leaf_faces,leaf_colors=[],[],[]
    parts = [branch((0, 0, -0.12), (0.18, 0, 1.7), 0.23),
             branch((0.18, 0, 1.7), (0.56, 0.1, 3.15), 0.16)]
    for i in range(7):
        angle = i * pi * 2 / 7
        end = Vector((cos(angle) * 1.8, sin(angle) * 1.5, 3.0 + (i % 3) * 0.35))
        start = Vector((0.18, 0, 1.7 + (i % 2) * 0.45))
        parts.append(branch(start, end, 0.11))
        for j in range(6):
            at = end + Vector((random.uniform(-0.65, 0.65), random.uniform(-0.55, 0.55),
                               random.uniform(-0.1, 0.6)))
            radius = random.uniform(0.42, 0.72)
            parts.append(branch(end,at,0.022))
            for leaf in range(70):
                point=at+Vector((random.uniform(-radius,radius),random.uniform(-radius*0.85,radius*0.85),
                                 random.uniform(-radius*0.4,radius*0.4)))
                angle=random.uniform(0,pi*2)
                direction=Vector((cos(angle),sin(angle),random.uniform(-0.35,0.35))).normalized()
                normal=Vector((random.uniform(-0.4,0.4),random.uniform(-0.4,0.4),1)).normalized()
                across=normal.cross(direction).normalized()
                length=random.uniform(0.12,0.24)
                width=length*random.uniform(0.42,0.60)
                base=len(leaf_vertices)
                leaf_vertices.extend([point-direction*length*0.45,point+across*width*0.5,
                                      point+normal*0.018,point+direction*length*0.6,
                                      point-across*width*0.5])
                leaf_faces.extend([(base,base+1,base+2),(base+1,base+3,base+2),
                                   (base+2,base+3,base+4),(base,base+2,base+4)])
                leaf_colors.extend([random.choices((0,1,2),(0.50,0.35,0.15))[0]]*4)
    for i in range(5):
        angle = i * pi * 2 / 5
        parts.append(branch((0, 0, 0.12), (cos(angle) * 0.65, sin(angle) * 0.65, 0.03), 0.12))
    leaf_mesh=bpy.data.meshes.new("Canyon Leaves")
    leaf_mesh.from_pydata(leaf_vertices,[],leaf_faces)
    for surface in (leaves,leaf_light,leaf_shadow):leaf_mesh.materials.append(surface)
    for face,color in zip(leaf_mesh.polygons,leaf_colors):face.material_index=color
    leaf_object=bpy.data.objects.new("Canyon Leaves",leaf_mesh)
    bpy.context.collection.objects.link(leaf_object)
    parts.append(leaf_object)
    kits.append(fixture("CanyonTree", parts))
    parts = []
    for x, y, z, scale in [(-0.34, 0, 0.30, 0.45), (0.24, 0.1, 0.35, 0.55),
                           (0, -0.18, 0.28, 0.40)]:
        parts.append(sphere("Sage Cluster", (x, y, z), (scale, scale * 0.8, scale * 0.65),
                            sage, segments=14, rings=8))
    kits.append(fixture("RiverShrub", parts))
    parts = []
    for i in range(7):
        x, y = (i % 3 - 1) * 0.13, (i // 3 - 1) * 0.10
        height = 0.38 + (i % 3) * 0.07
        stalk = sphere("Lavender Stalk", (x, y, height * 0.5),
                       (0.015, 0.015, height * 0.5), grass, segments=8, rings=6)
        parts.append(stalk)
        parts.append(sphere("Flower Spike", (x, y, height), (0.047, 0.047, 0.14),
                            flower, segments=10, rings=8))
    kits.append(fixture("LavenderPatch", parts))
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtSource/Canyon Foliage.blend"))
    destination = ROOT / "Assets/RidgeRelay/Art/Resources/RidgeRelay/Environment"
    for kit in kits:
        export_fbx(destination / (kit.name + ".fbx"), [kit])


if __name__ == "__main__":
    make_kit()
