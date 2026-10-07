"""Export the verified Dust II geometry into bounded FBX chunks for Unity.

Textures are handled separately; stripping them only from this temporary glTF
avoids decoding hundreds of images during a geometry-only conversion.
"""
import copy
import json
import sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

root = Path(__file__).resolve().parents[1]
source = root / 'DustII'
output = source / 'UnityExport'
output.mkdir(exist_ok=True)
doc = json.loads((source / 'de_dust2.gltf').read_text(encoding='utf-8'))
manifest = {'source': 'CS2 de_dust2', 'materials': [], 'chunks': []}
tool_ids = set()
for i, material in enumerate(doc['materials']):
    material['name'] = f'D2_{i:04d}_' + material['name']
    if '/tools/' in material.get('extras', {}).get('vmat', {}).get('Name', ''):
        tool_ids.add(i)
manifest['materials'] = copy.deepcopy(doc['materials'])
manifest['textures'] = copy.deepcopy(doc['textures'])
manifest['images'] = copy.deepcopy(doc['images'])
hidden_names = set()
for node in doc['nodes']:
    if 'mesh' in node:
        ids = [p.get('material', -1) for p in doc['meshes'][node['mesh']]['primitives']]
        if ids and all(i in tool_ids for i in ids): hidden_names.add(node['name'])
for material in doc['materials']:
    for key in list(material):
        if key.endswith('Texture') or key in ('extras', 'extensions'): del material[key]
    for key in list(material.get('pbrMetallicRoughness', {})):
        if key.endswith('Texture'): del material['pbrMetallicRoughness'][key]
doc.pop('images', None)
doc.pop('textures', None)
for buffer in doc['buffers']: buffer['uri'] = '../' + buffer['uri']
temporary = output / 'geometry.gltf'
temporary.write_text(json.dumps(doc), encoding='utf-8')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0
bpy.ops.import_scene.gltf(filepath=str(temporary), import_pack_images=False)
bpy.context.view_layer.update()
objects = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name not in hidden_names]
objects.sort(key=lambda o: (round(o.matrix_world.translation.x / 25), round(o.matrix_world.translation.y / 25), o.name))
assert len(objects) == 3599, len(objects)
points = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
manifest['boundsBlender'] = {'min': [min(p[i] for p in points) for i in range(3)], 'max': [max(p[i] for p in points) for i in range(3)]}
manifest['hiddenToolMeshes'] = len(hidden_names)
manifest['meshCount'] = len(objects)
manifest['triangleCount'] = sum(len(p.vertices)-2 for o in objects for p in o.data.polygons)
manifest['nonCollidableMeshes'] = []
for obj in objects:
    mesh = obj.data
    mesh.calc_loop_triangles()
    vertices = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
    mesh.vertices.foreach_get('co', vertices)
    indices = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int32)
    mesh.loop_triangles.foreach_get('vertices', indices)
    points = vertices.reshape(-1, 3)[indices.reshape(-1, 3)]
    if not np.any(np.cross(points[:, 1] - points[:, 0], points[:, 2] - points[:, 0])):
        manifest['nonCollidableMeshes'].append(obj.name)
for start in range(0, len(objects), 400):
    chunk = objects[start:start+400]
    name = f'DustII_{start // 400:02d}.fbx'
    bpy.ops.object.select_all(action='DESELECT')
    for obj in chunk: obj.select_set(True)
    bpy.context.view_layer.objects.active = chunk[0]
    bpy.ops.export_scene.fbx(filepath=str(output / name), use_selection=True,
        object_types={'MESH'}, global_scale=1, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=True,
        mesh_smooth_type='OFF', use_triangles=True, use_custom_props=False,
        bake_anim=False, path_mode='STRIP', embed_textures=False,
        axis_forward='-Z', axis_up='Y')
    manifest['chunks'].append({'file': name, 'meshes': len(chunk),
        'triangles': sum(len(p.vertices)-2 for o in chunk for p in o.data.polygons)})
    print('EXPORTED ' + name, flush=True)
(output / 'source_manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print('DUSTII_GEOMETRY_PASS ' + str(manifest['meshCount']), flush=True)
