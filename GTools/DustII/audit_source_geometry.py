import collections
import json
from pathlib import Path
import numpy as np

p = Path(__file__).resolve().parents[2] / 'ArtSource/CS2Imports/DustII'
d = json.loads((p/'de_dust2.gltf').read_text())
binary = (p/d['buffers'][0]['uri']).read_bytes()
def accessor(index):
    a = d['accessors'][index]; v = d['bufferViews'][a['bufferView']]
    dtype = {5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]
    n = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
    size = np.dtype(dtype).itemsize
    return np.ndarray((a['count'],n), dtype=dtype, buffer=binary,
        offset=v.get('byteOffset',0)+a.get('byteOffset',0),
        strides=(v.get('byteStride',size*n),size))
result = collections.Counter()
for mesh in d['meshes']:
    for primitive in mesh['primitives']:
        material = d['materials'][primitive['material']]
        if '/tools/' in material.get('extras',{}).get('vmat',{}).get('Name',''): continue
        vertices = accessor(primitive['attributes']['POSITION']).astype(np.float64)
        indices = accessor(primitive['indices']).reshape(-1,3)
        points = vertices[indices]
        area = np.linalg.norm(np.cross(points[:,1]-points[:,0], points[:,2]-points[:,0]),axis=1)
        result['triangles'] += len(indices)
        for tolerance in [0,1e-16,1e-14,1e-12,1e-10,1e-8,1e-6]:
            result['degenerate_area_'+str(tolerance)] += int(np.count_nonzero(area <= tolerance))
print(dict(result))
(p/'UnityExport/source_geometry_audit.json').write_text(json.dumps(result,indent=2))
