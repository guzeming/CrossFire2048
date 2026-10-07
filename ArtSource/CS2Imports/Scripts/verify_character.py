"""Reopen the character and test actual armature deformation without saving a pose."""
import json
import math
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

root = Path(__file__).resolve().parent.parent
path = root / "CT_SAS" / "ctm_sas.blend"
bpy.ops.wm.open_mainfile(filepath=str(path))
armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
body = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.name.endswith("thirdperson_body"))
bone = armature.pose.bones["arm_upper_L"]
bpy.context.view_layer.update()
depsgraph = bpy.context.evaluated_depsgraph_get()
evaluated = body.evaluated_get(depsgraph)
mesh = evaluated.to_mesh()
before = [vertex.co.copy() for vertex in mesh.vertices]
evaluated.to_mesh_clear()
old_matrix = bone.matrix_basis.copy()
bone.rotation_mode = "QUATERNION"
bone.rotation_quaternion = Quaternion(Vector((0, 1, 0)), math.radians(20))
bpy.context.view_layer.update()
depsgraph.update()
evaluated = body.evaluated_get(depsgraph)
mesh = evaluated.to_mesh()
movement = [(vertex.co - before[index]).length for index, vertex in enumerate(mesh.vertices)]
evaluated.to_mesh_clear()
bone.matrix_basis = old_matrix
changed = sum(distance > 0.0001 for distance in movement)
assert changed > 50, f"Armature did not deform body: {changed} changed vertices"
file_images = [image for image in bpy.data.images if image.source == "FILE"]
assert all(image.packed_file for image in file_images), "Texture is not packed"
result = {"reopen_passed": True, "bone_tested": bone.name, "test_rotation_degrees": 20,
          "deformed_body_vertices": changed, "max_vertex_displacement_meters": max(movement),
          "bones": len(armature.data.bones), "packed_images": len(file_images),
          "test_pose_saved": False}
(root / "CT_SAS" / "rig_validation.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result), flush=True)
