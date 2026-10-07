"""Build the editable Blender foreground kit and export Unity FBX + baked PBR maps.

blender --background --factory-startup --python build_hangar_kit.py -- --output <Unity asset folder>
Coordinates in this file are Blender meters: X right, Y depth, Z up.
"""
import argparse
import json
import math
import random
import sys
from pathlib import Path
import bpy
from mathutils import Vector

SOURCE = Path(__file__).resolve().parent
PROJECT = SOURCE.parents[2]
args = argparse.ArgumentParser()
args.add_argument('--output', default=str(PROJECT / 'Assets/Art/Lobby/Hangar'))
args = args.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
OUT = Path(args.output).resolve()
for sub in ('Models', 'Textures'):
    (OUT / sub).mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
random.seed(2048)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
scene.render.engine = 'CYCLES'
scene.cycles.samples = 4
scene.cycles.bake_type = 'EMIT'

MATERIALS = {}
META = {}
def material(name, color, metallic, roughness, emission=0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = roughness
    if emission:
        bsdf.inputs['Emission Color'].default_value = (*color, 1)
        bsdf.inputs['Emission Strength'].default_value = emission
    mat.diffuse_color = (*color, 1)
    MATERIALS[name] = mat
    META[name] = dict(name=name, color=list(color), metallic=metallic, smoothness=1-roughness, emission=emission)
    return mat

material('H_PaintedSteel', (.14, .17, .16), .6, .42)
material('H_EdgeSteel', (.12, .14, .15), .78, .33)
material('H_Rubber', (.022, .026, .026), .08, .7)
material('H_Brass', (.42, .25, .08), .72, .35)
material('H_AmberPaint', (.65, .28, .028), .35, .5)
material('H_Stencil', (.56, .57, .49), .1, .7)
material('H_Light', (1.0, .13, .006), .1, .2, 2)
material('H_WetConcrete', (.09, .10, .11), .35, .22)
material('H_TreadSteel', (.085, .095, .10), .8, .32)

def bake_surfaces(name, floor=False):
    """Actual Blender shader baking; no photographic assets or edit of the reference image."""
    mat = MATERIALS[name]
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf, output = nodes.get('Principled BSDF'), nodes.get('Material Output')
    coord = nodes.new('ShaderNodeTexCoord')
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 6 if floor else 11
    noise.inputs['Detail'].default_value = 6
    noise.inputs['Roughness'].default_value = .72
    links.new(coord.outputs['UV'], noise.inputs['Vector'])
    fine = nodes.new('ShaderNodeTexNoise')
    fine.inputs['Scale'].default_value = 340 if floor else 210
    fine.inputs['Detail'].default_value = 2
    links.new(coord.outputs['UV'], fine.inputs['Vector'])
    combine = nodes.new('ShaderNodeMixRGB')
    combine.blend_type = 'MULTIPLY'
    combine.inputs[0].default_value = .38
    links.new(noise.outputs['Fac'], combine.inputs[1])
    links.new(fine.outputs['Fac'], combine.inputs[2])
    ramp = nodes.new('ShaderNodeValToRGB')
    if floor:
        ramp.color_ramp.elements[0].position = .18
        ramp.color_ramp.elements[0].color = (.033, .038, .041, 1)
        ramp.color_ramp.elements[1].position = .72
        ramp.color_ramp.elements[1].color = (.16, .175, .18, 1)
    else:
        ramp.color_ramp.elements[0].position = .20
        ramp.color_ramp.elements[0].color = (.043, .049, .043, 1)
        ramp.color_ramp.elements[1].position = .73
        ramp.color_ramp.elements[1].color = (.21, .245, .22, 1)
    links.new(combine.outputs[0], ramp.inputs[0])
    rough = nodes.new('ShaderNodeValToRGB')
    rough.color_ramp.elements[0].position = .30
    rough.color_ramp.elements[0].color = (.07, .07, .07, 1) if floor else (.3, .3, .3, 1)
    rough.color_ramp.elements[1].position = .59
    rough.color_ramp.elements[1].color = (.66, .66, .66, 1)
    links.new(noise.outputs['Fac'], rough.inputs[0])
    bump = nodes.new('ShaderNodeBump')
    bump.inputs['Strength'].default_value = .3
    bump.inputs['Distance'].default_value = .016 if floor else .006
    links.new(fine.outputs['Fac'], bump.inputs['Height'])
    links.new(bump.outputs[0], bsdf.inputs['Normal'])
    links.new(rough.outputs[0], bsdf.inputs['Roughness'])
    links.new(ramp.outputs[0], bsdf.inputs['Base Color'])
    bpy.ops.mesh.primitive_plane_add(size=2)
    plane = bpy.context.object
    plane.name = 'BakeSurface'
    plane.data.materials.append(mat)
    emission = nodes.new('ShaderNodeEmission')
    image_node = nodes.new('ShaderNodeTexImage')
    nodes.active = image_node
    def bake(suffix, socket, normal=False):
        img = bpy.data.images.new(name + suffix, width=1024, height=1024, alpha=False)
        img.colorspace_settings.name = 'Non-Color' if suffix != '_Color' else 'sRGB'
        image_node.image = img
        if normal:
            links.new(bsdf.outputs[0], output.inputs[0])
            bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=8)
        else:
            links.new(socket, emission.inputs[0])
            links.new(emission.outputs[0], output.inputs[0])
            bpy.ops.object.bake(type='EMIT', margin=8)
        img.filepath_raw = str(OUT / 'Textures' / (name + suffix + '.png'))
        img.file_format = 'PNG'
        img.save()
        img.pack()
        META[name][suffix[1:].lower() + 'Texture'] = name + suffix + '.png'
    bake('_Color', ramp.outputs[0])
    # Blender bake of roughness is preserved as a source asset; Unity packs metallic/smoothness.
    bake('_Roughness', rough.outputs[0])
    bake('_Normal', None, True)
    links.new(bsdf.outputs[0], output.inputs[0])
    nodes.remove(emission)
    nodes.remove(image_node)
    bpy.data.objects.remove(plane, do_unlink=True)

bake_surfaces('H_PaintedSteel')
bake_surfaces('H_WetConcrete', True)

parts = []
assets = []
def finish_obj(obj, name, mat):
    obj.name = name
    obj.data.materials.append(MATERIALS[mat])
    parts.append(obj)
    return obj

def box(name, loc, size, mat='H_PaintedSteel', bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    finish_obj(obj, name, mat)
    if bevel:
        mod = obj.modifiers.new('Machined edge chamfer', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
        bpy.ops.object.modifier_apply(modifier=mod.name)
        mod = obj.modifiers.new('Weighted surface normals', 'WEIGHTED_NORMAL')
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj

def cylinder(name, loc, radius, depth, mat='H_EdgeSteel', direction=(0,0,1), vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc)
    obj = bpy.context.object
    obj.rotation_euler = Vector(direction).to_track_quat('Z', 'Y').to_euler()
    finish_obj(obj, name, mat)
    mod = obj.modifiers.new('Rim bevel', 'BEVEL'); mod.width=min(.008, radius*.2); mod.segments=2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in obj.data.polygons: p.use_smooth = True
    return obj

def tube(name, points, radius=.016, mat='H_Rubber'):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'; curve.resolution_u=8
    curve.bevel_depth=radius; curve.bevel_resolution=2
    spline = curve.splines.new('BEZIER'); spline.bezier_points.add(len(points)-1)
    for point, co in zip(spline.bezier_points, points):
        point.co=co; point.handle_left_type=point.handle_right_type='AUTO'
    obj=bpy.data.objects.new(name,curve);scene.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.object.convert(target='MESH')
    finish_obj(bpy.context.object,name,mat)
    return bpy.context.object

def stencil(text, loc, size=.10, mat='H_Stencil'):
    curve=bpy.data.curves.new('Stencil_'+text, 'FONT');curve.body=text;curve.size=size
    curve.align_x='CENTER';curve.extrude=.00025
    obj=bpy.data.objects.new('Stencil_'+text,curve);scene.collection.objects.link(obj)
    obj.location=loc;obj.rotation_euler=(math.pi/2,0,0)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.object.convert(target='MESH');finish_obj(bpy.context.object,'Stencil_'+text,mat)

def asset(name):
    global parts
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts: obj.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.join()
    obj=bpy.context.object;obj.name=name
    scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    # Export evaluated bevel geometry with Blender's meter-to-FBX conversion.
    bpy.ops.export_scene.fbx(filepath=str(OUT/'Models'/(name+'.fbx')), use_selection=True,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, object_types={'MESH'}, use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
    obj.data.calc_loop_triangles()
    assets.append(dict(name=name, triangles=len(obj.data.loop_triangles), vertices=len(obj.data.vertices), materials=[m.name for m in obj.data.materials]))
    parts=[]
    return obj

# Reusable hard-surface cargo case, approximately 1m wide, detailed on every side.
box('Case shell', (0,0,.36), (1.08,.69,.70), bevel=.04)
box('Lid gasket', (0,0,.655), (1.1,.715,.028), 'H_Rubber', .012)
box('Lid cap', (0,0,.71), (1.1,.715,.09), bevel=.025)
box('Foot base', (0,0,.032), (1.08,.68,.064), 'H_Rubber', .016)
for side in (-1,1):
    box('Recessed face', (0,side*.348,.37), (.82,.022,.39), 'H_Rubber', .02)
    box('Inset panel', (0,side*.363,.37), (.76,.019,.33), bevel=.014)
    for x in (-.475,.475):
        box('Corner armor', (x,side*.319,.365), (.102,.10,.64), 'H_EdgeSteel', .018)
        for z in (.09,.61):
            cylinder('Torx fastener', (x,side*.376,z), .014,.008, direction=(0,1,0),vertices=6)
    for x in (-.32,.32):
        box('Latch seat', (x,side*.374,.63), (.09,.042,.155), 'H_Rubber', .012)
        box('Latch lever', (x,side*.402,.62), (.052,.026,.11), 'H_Brass', .008)
        cylinder('Latch pivot',(x,side*.418,.675),.015,.06,direction=(1,0,0))
for x in (-.53,.53):
    box('Handle recess',(x,0,.39),(.032,.37,.18),'H_Rubber',.018)
    tube('Carry handle',[(x,-.14,.41),(x*1.10,-.13,.36),(x*1.10,.13,.36),(x,.14,.41)],.018,'H_EdgeSteel')
for x in (-.36,-.18,0,.18,.36):
    box('Top strengthening rib',(x,0,.766),(.065,.55,.03),'H_EdgeSteel',.01)
stencil('SAS',(0,-.379,.35),.14)
stencil('FIELD EQUIPMENT  /  04',(0,-.380,.26),.032)
box('Orange ID tab',(.26,-.38,.49),(.14,.008,.055),'H_AmberPaint',.002)
case=asset('EquipmentCase')

# Long weapons case with hinges, ribs, and double locks.
box('Weapon shell',(0,0,.20),(1.42,.44,.38),bevel=.032)
box('Gasket',(0,0,.33),(1.44,.45,.025),'H_Rubber',.008)
box('Weapon lid',(0,0,.385),(1.44,.45,.09),bevel=.02)
for x in (-.60,-.35,0,.35,.60):
    box('Structural strap',(x,0,.224),(.055,.48,.44),'H_Rubber',.01)
    box('Lid stripe',(x,0,.452),(.06,.42,.013),'H_EdgeSteel',.006)
for x in (-.48,.48):
    box('Lock base',(x,-.242,.30),(.09,.04,.16),'H_EdgeSteel',.009)
    box('Lock',(x,-.267,.28),(.055,.025,.09),'H_Brass',.007)
tube('Front handle',[(-.15,-.24,.26),(-.14,-.31,.20),(.14,-.31,.20),(.15,-.24,.26)],.016)
stencil('ARMORY',(0,-.226,.14),.072)
weapon=asset('WeaponCase')

# Structural column: I-beam, base bolts, utility conduit, caged amber worklight.
box('Column web',(0,0,2.5),(.18,.32,5),'H_PaintedSteel',.015)
for x in (-.19,.19): box('I-beam flange',(x,0,2.5),(.12,.52,5),'H_EdgeSteel',.012)
box('Base plate',(0,0,.07),(.80,.83,.14),'H_PaintedSteel',.026)
for x in (-.29,.29):
    for y in (-.3,.3): cylinder('Anchor bolt',(x,y,.17),.037,.08,'H_EdgeSteel',vertices=6)
for z in (.5,1.55,2.6,3.65,4.7):
    box('Cross brace',(0,-.29,z),(.62,.095,.09),'H_EdgeSteel',.012)
    for x in (-.22,.22): cylinder('Flange bolt',(x,-.35,z),.025,.025,direction=(0,1,0),vertices=6)
box('Luminaire housing',(0,-.32,2.6),(.16,.16,2.25),'H_Rubber',.02)
box('Amber tube',(0,-.414,2.6),(.045,.045,2.13),'H_Light',.018)
for z in (1.55,2.1,2.8,3.65):
    box('Light guard',(0,-.452,z),(.15,.025,.027),'H_EdgeSteel',.004)
tube('Electrical conduit',[(.26,.22,.15),(.29,.22,2.2),(.27,.22,4.8)],.035)
for z in (.8,4.15): box('Conduit collar',(.28,.22,z),(.12,.10,.07),'H_EdgeSteel',.008)
column=asset('HangarColumn')

# Low floor: beveled slabs, expansion seams and a side drain. Surface repeats every 3m.
for x in range(-3,4):
    for y in range(-2,5):
        slab=box('Concrete slab',(x*2.5,y*2.5,-.065),(2.485,2.485,.12),'H_WetConcrete',.009)
        # Planar UVs preserve one consistent texture scale over the whole floor.
        for poly in slab.data.polygons:
            for li in poly.loop_indices:
                v=slab.data.vertices[slab.data.loops[li].vertex_index].co + slab.location
                slab.data.uv_layers.active.data[li].uv=(v.x/3,v.y/3)
floor=asset('HangarFloor')

# Flush ring inlaid into the concrete, not a raised display pedestal.
def ring_arc(name,start,end,radius,width,z,mat):
    count=max(2,int((end-start)*1.5));vertices=[];faces=[]
    for i in range(count+1):
        a=math.radians(start+(end-start)*i/count)
        for r in (radius-width*.5,radius+width*.5): vertices.append((math.sin(a)*r,math.cos(a)*r,z))
    for i in range(count):faces.append((i*2,i*2+2,i*2+3,i*2+1))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj);finish_obj(obj,name,mat)
ring_arc('Inset steel channel',0,360,1.12,.045,.003,'H_Rubber')
for i in range(8):
    ring_arc('Segmented amber inlay',i*45+2,i*45+41,1.12,.016,.006,'H_Light')
    ring_arc('Painted perimeter marks',i*45+3,i*45+36,1.19,.025,.004,'H_AmberPaint')
ring=asset('DeploymentRing')

# Thick coiled utility cable with brass connectors; foreground silhouette detail.
pts=[]
for i in range(45):
    t=i/44*math.pi*4.2;r=.40+.07*i/44
    pts.append((math.sin(t)*r,math.cos(t)*r,.035+i*.0005))
tube('Coiled power lead',pts,.025)
tube('Loose lead',[(0,.43,.04),(-.45,.70,.038),(-.72,1.05,.035),(-1.10,1.28,.04)],.025)
for end in (pts[-1],(-1.10,1.28,.04)):
    cylinder('Connector',end,.045,.14,'H_Brass',direction=(1,0,0))
cable=asset('PowerCable')

# Recessed drain with modeled slots and bolts.
box('Drain surround',(0,0,-.014),(.42,2.5,.055),'H_Rubber',.01)
for side in (-1,1): box('Steel rail',(side*.18,0,.008),(.05,2.5,.024),'H_EdgeSteel',.005)
for i in range(44):box('Drain slat',(0,-1.21+i*.056,.012),(.31,.024,.018),'H_TreadSteel',.005)
grate=asset('DrainGrate')

# A portable worklamp, fixture and stand together.
box('Lamp base',(0,0,.045),(.48,.30,.08),'H_Rubber',.015)
for side in (-1,1):box('Lamp bracket',(side*.18,0,.14),(.04,.09,.24),'H_EdgeSteel',.006)
box('Lamp body',(0,0,.21),(.37,.18,.16),'H_PaintedSteel',.02)
box('Lamp lens',(0,-.098,.21),(.29,.018,.08),'H_Light',.012)
lamp=asset('WorkLight')

# Gallery in the native source file keeps every exported asset separately editable.
for i,obj in enumerate((case,weapon,column,floor,ring,cable,grate,lamp)):
    if obj == floor: obj.location=(0,0,-.2)
    elif obj == column: obj.location=(4,2,0)
    elif obj == ring: obj.location=(0,0,0)
    else:obj.location=(-4+(i%3)*2,3+(i//3)*2,0)
scene.world.color=(.08,.08,.08)
scene.render.engine='BLENDER_EEVEE'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'HangarKit.blend'))
(OUT/'materials.json').write_text(json.dumps({'materials':list(META.values())},indent=2),encoding='utf-8')
(SOURCE/'asset-report.json').write_text(json.dumps({'assets':assets,'blender':bpy.app.version_string},indent=2),encoding='utf-8')
print('HANGAR_KIT_PASS',json.dumps(assets))
