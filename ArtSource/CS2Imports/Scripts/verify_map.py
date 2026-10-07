"""Verify the saved map and fit its overview camera to landscape aspect ratio."""
import json
from pathlib import Path

import bpy
from mathutils import Vector

root = Path(__file__).resolve().parent.parent
folder = root / "DustII"
path = folder / "de_dust2.blend"
bpy.ops.wm.open_mainfile(filepath=str(path))
report = json.loads((folder / "de_dust2_report.json").read_text(encoding="utf-8"))
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
images = [image for image in bpy.data.images if image.source == "FILE"]
assert len(meshes) == report["mesh_objects"]
assert len(images) == report["packed_images"]
assert all(image.packed_file for image in images)
assert all(image.size[0] > 0 and image.size[1] > 0 for image in images)
camera = bpy.context.scene.camera
hidden = set(report["hidden_alternatives_or_helpers"])
points = [obj.matrix_world @ Vector(corner) for obj in meshes if obj.name not in hidden for corner in obj.bound_box]
rotation = camera.rotation_euler.to_matrix()
right, up = rotation @ Vector((1, 0, 0)), rotation @ Vector((0, 1, 0))
xs, ys = [p.dot(right) for p in points], [p.dot(up) for p in points]
scene = bpy.context.scene
aspect = scene.render.resolution_x / scene.render.resolution_y
camera.data.ortho_scale = max(max(xs) - min(xs), (max(ys) - min(ys)) * aspect) * 1.12
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)
result = {"reopen_passed": True, "mesh_objects": len(meshes), "packed_images": len(images), "missing_images": 0}
(folder / "map_validation.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result), flush=True)
bpy.ops.render.render(write_still=True)
