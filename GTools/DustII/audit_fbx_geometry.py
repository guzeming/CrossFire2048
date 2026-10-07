import json
from collections import Counter
from pathlib import Path
import numpy as np
from io_scene_fbx import parse_fbx

root = Path(__file__).resolve().parents[2] / 'ArtSource/CS2Imports/DustII/UnityExport'
results = []
empty_meshes = []
for path in sorted(root.glob('DustII_*.fbx')):
    tree, version = parse_fbx.parse(str(path))
    objects = next(e for e in tree.elems if e.id == b'Objects')
    models = {e.props[0]:e.props[1].decode('utf-8').split('\x00')[0] for e in objects.elems if e.id == b'Model'}
    connections = next(e for e in tree.elems if e.id == b'Connections')
    owners = {e.props[1]:models[e.props[2]] for e in connections.elems if len(e.props) >= 3 and e.props[2] in models}
    counts = Counter()
    for geometry in (e for e in objects.elems if e.id == b'Geometry'):
        entries = {e.id:e for e in geometry.elems}
        vertices = np.array(entries[b'Vertices'].props[0]).reshape(-1,3)
        indices = np.array(entries[b'PolygonVertexIndex'].props[0]).reshape(-1,3)
        indices = np.where(indices < 0, -indices-1, indices)
        points = vertices[indices]
        area = np.linalg.norm(np.cross(points[:,1]-points[:,0],points[:,2]-points[:,0]),axis=1)
        if not np.any(area > 1e-10):
            empty_meshes.append({'file':path.name,'name':owners[geometry.props[0]],'triangles':len(indices)})
        counts['triangles'] += len(indices)
        for tolerance in [0,1e-16,1e-14,1e-12,1e-10,1e-8,1e-6]:
            counts['degenerate_area_'+str(tolerance)] += int(np.count_nonzero(area <= tolerance))
    results.append({'file':path.name, **dict(counts)})
print(json.dumps(results),flush=True)
print('ZERO_AREA_MESHES ' + json.dumps(empty_meshes),flush=True)
(root/'fbx_geometry_audit.json').write_text(json.dumps(results,indent=2))
manifest_path = root/'source_manifest.json'
manifest = json.loads(manifest_path.read_text())
manifest['nonCollidableMeshes'] = [m['name'] for m in empty_meshes]
manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
