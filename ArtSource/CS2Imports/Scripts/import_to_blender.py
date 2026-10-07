"""Import the extracted CS2 assets, preserve their data, and save a verified blend."""

import argparse
import json
import math
import sys
from pathlib import Path
from urllib.parse import unquote

import bpy
from mathutils import Vector


def arguments():
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset", choices=["ct", "dust2"], required=True)
    parser.add_argument("--preview", action="store_true")
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1:])


def bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return lo, hi, points


def move_to_collection(obj, collection):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)


def setup_preview(objects, mode, output):
    scene = bpy.context.scene
    lo, hi, points = bounds(objects)
    center = (lo + hi) / 2
    span = max(hi - lo)
    preview_collection = bpy.data.collections.new("Preview_Camera")
    scene.collection.children.link(preview_collection)
    camera_data = bpy.data.cameras.new("Asset_Overview")
    camera = bpy.data.objects.new("Asset_Overview", camera_data)
    preview_collection.objects.link(camera)
    direction = Vector((3.5, -6, 2.2) if mode == "ct" else (0.6, -0.85, 1.7)).normalized()
    camera.location = center + direction * span * 2
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.clip_start = 0.01
    camera_data.clip_end = max(1000, span * 10)
    rotation = camera.rotation_euler.to_matrix()
    right = rotation @ Vector((1, 0, 0))
    up = rotation @ Vector((0, 1, 0))
    xs = [(p - center).dot(right) for p in points]
    ys = [(p - center).dot(up) for p in points]
    width, height = (900, 1000) if mode == "ct" else (1400, 1000)
    aspect = width / height
    projected_width, projected_height = max(xs) - min(xs), max(ys) - min(ys)
    camera_data.ortho_scale = (max(projected_width, projected_height * aspect) if aspect >= 1 else max(projected_height, projected_width / aspect)) * 1.12
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(output)
    scene.render.film_transparent = False
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "TEXTURE"
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    shading.show_specular_highlight = False
    shading.background_type = "WORLD"
    scene.world.color = (0.055, 0.065, 0.08)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                space = area.spaces.active
                space.clip_end = camera_data.clip_end
                space.shading.type = "SOLID"
                space.shading.color_type = "TEXTURE"
                space.overlay.show_floor = False
                space.region_3d.view_distance = span * 1.3
                space.region_3d.view_location = center
                space.region_3d.view_rotation = camera.rotation_euler.to_quaternion()
                space.region_3d.view_perspective = "ORTHO"


args = arguments()
root = Path(__file__).resolve().parent.parent
folder, name = ("CT_SAS", "ctm_sas") if args.asset == "ct" else ("DustII", "de_dust2")
asset_dir = root / folder
source = asset_dir / (name + ".gltf")
destination = asset_dir / (name + ".blend")
doc = json.loads(source.read_text(encoding="utf-8"))
external_files = [entry["uri"] for kind in ("images", "buffers") for entry in doc.get(kind, []) if "uri" in entry and not entry["uri"].startswith("data:")]
missing = [uri for uri in external_files if not (asset_dir / unquote(uri)).is_file()]
if missing:
    raise RuntimeError(f"Missing glTF dependencies: {missing}")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.world = bpy.data.worlds.new("Preview_World")
bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.scene.unit_settings.scale_length = 1.0
print(f"IMPORT_START {source}", flush=True)
bpy.ops.import_scene.gltf(filepath=str(source), import_pack_images=False)
print("IMPORT_FINISHED", flush=True)
bpy.context.view_layer.update()
armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
bone_shapes = {bone.custom_shape for armature in armatures for bone in armature.pose.bones if bone.custom_shape}
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj not in bone_shapes]
expected_mesh_objects = sum("mesh" in node for node in doc.get("nodes", []))
if len(meshes) != expected_mesh_objects:
    raise RuntimeError(f"Mesh count changed: glTF={expected_mesh_objects}, Blender={[(obj.name, len(obj.data.vertices)) for obj in meshes]}")

hidden = bpy.data.collections.new("FirstPerson_Alternatives" if args.asset == "ct" else "Tools_BlockLight")
bpy.context.scene.collection.children.link(hidden)
hidden_objects = []
tool_materials = {mat["name"] for mat in doc.get("materials", []) if "/tools/" in mat.get("extras", {}).get("vmat", {}).get("Name", "")}
for obj in meshes:
    firstperson = args.asset == "ct" and "firstperson_" in obj.name
    tools_only = args.asset == "dust2" and obj.data.materials and all(mat and mat.name in tool_materials for mat in obj.data.materials)
    if firstperson or tools_only:
        move_to_collection(obj, hidden)
        hidden_objects.append(obj.name)
hidden.hide_render = True
hidden.hide_viewport = True

# Make the base-color texture active for Blender's texture-preview viewport.
for material_data in doc.get("materials", []):
    material = bpy.data.materials.get(material_data.get("name", ""))
    color = material_data.get("pbrMetallicRoughness", {}).get("baseColorTexture")
    if not material or not material.node_tree or not color:
        continue
    tex = doc["textures"][color["index"]]
    uri = doc["images"][tex["source"]].get("uri", "")
    expected = Path(unquote(uri)).name
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image and Path(bpy.path.abspath(node.image.filepath)).name == expected:
            material.node_tree.nodes.active = node
            break

image_report = []
for image in bpy.data.images:
    if image.source == "FILE":
        if image.size[0] == 0 or image.size[1] == 0:
            raise RuntimeError(f"Unloaded image: {image.name}")
        image_report.append({"name": image.name, "width": image.size[0], "height": image.size[1]})

rig_report = []
for obj in armatures:
    obj.show_in_front = True
    obj.data.display_type = "STICK"
    rig_report.append({"name": obj.name, "bones": len(obj.data.bones), "bone_names": [bone.name for bone in obj.data.bones]})
weights_report = []
if args.asset == "ct":
    if not armatures:
        raise RuntimeError("Character armature is missing")
    for obj in meshes:
        modifiers = [mod for mod in obj.modifiers if mod.type == "ARMATURE" and mod.object]
        if not modifiers:
            raise RuntimeError(f"No armature binding on {obj.name}")
        bones = {bone.name for mod in modifiers for bone in mod.object.data.bones}
        deform_groups = {group.index for group in obj.vertex_groups if group.name in bones}
        unweighted = sum(not any(weight.group in deform_groups and weight.weight > 0 for weight in vertex.groups) for vertex in obj.data.vertices)
        weights_report.append({"mesh": obj.name, "vertices": len(obj.data.vertices), "vertex_groups": len(obj.vertex_groups), "unweighted_vertices": unweighted})
        if unweighted:
            raise RuntimeError(f"Unweighted vertices in {obj.name}: {unweighted}")

visible_meshes = [obj for obj in meshes if obj.name not in hidden_objects]
lo, hi, _ = bounds(visible_meshes)
setup_preview(visible_meshes, args.asset, asset_dir / (name + "_preview.png"))
bpy.ops.object.select_all(action="DESELECT")
if armatures:
    armatures[0].select_set(True)
    bpy.context.view_layer.objects.active = armatures[0]

report = {
    "source": str(source), "blend": str(destination), "blender_version": bpy.app.version_string,
    "mesh_objects": len(meshes), "vertices": sum(len(obj.data.vertices) for obj in meshes),
    "triangles": sum(sum(len(poly.vertices) - 2 for poly in obj.data.polygons) for obj in meshes),
    "materials": len(bpy.data.materials), "images": image_report,
    "armatures": rig_report, "weights": weights_report, "actions": len(bpy.data.actions),
    "hidden_alternatives_or_helpers": hidden_objects,
    "visible_bounds_meters": {"min": list(lo), "max": list(hi), "size": list(hi - lo)},
    "missing_external_files": missing,
    "notes": ["Textures are packed into the blend file; glTF/PNG originals are also retained.",
              "Source 2 materials are approximate; original shader behavior is not reproduced.",
              "This import preserves geometry and rigging; gameplay logic is not converted.",
              "No game animation clips are included in this import."]
}
print("PACKING_TEXTURES", flush=True)
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene["source_game"] = "Counter-Strike 2"
bpy.context.scene["source_asset"] = str(source)
bpy.context.scene["import_note"] = "Geometry, UVs, normals, textures and rigging import. Materials are approximate."
bpy.ops.wm.save_as_mainfile(filepath=str(destination), compress=True)
report["packed_images"] = sum(bool(image.packed_file) for image in bpy.data.images if image.source == "FILE")
(asset_dir / (name + "_report.json")).write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
print("SAVED " + str(destination), flush=True)
print(json.dumps({key: report[key] for key in ("mesh_objects", "vertices", "triangles", "packed_images", "actions")}), flush=True)
if args.preview:
    bpy.ops.render.render(write_still=True)
    print("PREVIEW_SAVED", flush=True)
