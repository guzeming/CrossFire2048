import bpy
import math
from mathutils import Matrix, Vector
from pathlib import Path

ROOT=Path(__file__).resolve().parent.parent
BATCH=ROOT/'Batch'

def bake_action(target,source,name,frame_start,frame_end):
    scene=bpy.context.scene
    scene.render.fps=30
    common=[b.name for b in target.data.bones if b.name in source.data.bones]
    rest={b.name:b.matrix_local.copy() for b in target.data.bones}
    world=target.matrix_world.copy()
    inverse=world.inverted()
    correction={n:(source.matrix_world@source.data.bones[n].matrix_local).inverted()@world@rest[n] for n in common}
    data={n:{'location':[],'rotation_quaternion':[],'scale':[]} for n in common}
    previous={}
    start,end=round(frame_start),round(frame_end)
    for frame in range(start,end+1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        desired={}
        for bone in target.data.bones:
            n=bone.name
            if n in correction:
                desired[n]=inverse@source.matrix_world@source.pose.bones[n].matrix@correction[n]
            else:
                desired[n]=desired[bone.parent.name]@rest[bone.parent.name].inverted()@rest[n] if bone.parent else rest[n]
            if n not in correction: continue
            kwargs={'parent_matrix':desired[bone.parent.name],'parent_matrix_local':rest[bone.parent.name]} if bone.parent else {}
            loc,rot,scale=bone.convert_local_to_pose(desired[n],rest[n],invert=True,**kwargs).decompose()
            if n in previous and previous[n].dot(rot)<0: rot.negate()
            previous[n]=rot.copy()
            for channel,values in [('location',loc),('rotation_quaternion',rot),('scale',scale)]:
                if not all(math.isfinite(v) for v in values): raise ValueError('Nonfinite pose '+name)
                data[n][channel].append(tuple(values))
    action=bpy.data.actions.new(name)
    slot=action.slots.new('OBJECT',target.name)
    bag=action.layers.new('Baked Source 2').strips.new(type='KEYFRAME').channelbag(slot,ensure=True)
    count=end-start+1
    for n,channels in data.items():
        for channel,values in channels.items():
            for component in range(len(values[0])):
                curve=bag.fcurves.new(data_path=f'pose.bones["{n}"].{channel}',index=component)
                curve.keyframe_points.add(count)
                curve.keyframe_points.foreach_set('co',[v for i,row in enumerate(values) for v in (i+1,row[component])])
                for key in curve.keyframe_points: key.interpolation='LINEAR'
                curve.update()
    action.use_fake_user=True
    action.use_frame_range=True
    action.frame_start=1
    action.frame_end=max(2,count)
    action['source_rig']=source.name
    action['mapped_bones']=len(common)
    action.asset_mark()
    return action

def assign(rig,action):
    rig.animation_data_create()
    rig.animation_data.action=action
    if action: rig.animation_data.action_slot=action.slots[0]
    else:
        for bone in rig.pose.bones: bone.matrix_basis=Matrix.Identity(4)

def skeletal_actions():
    result=[]
    for action in bpy.data.actions:
        found=False
        for layer in action.layers:
            for strip in layer.strips:
                for slot in action.slots:
                    bag=strip.channelbag(slot)
                    if bag and any(fc.data_path.startswith('pose.bones[') for fc in bag.fcurves): found=True
        if found: result.append(action)
    return result

def positions(obj):
    bpy.context.view_layer.update()
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh=evaluated.to_mesh()
    values=[evaluated.matrix_world@v.co for v in mesh.vertices]
    evaluated.to_mesh_clear()
    return values

def check_motion(rig,mesh,action):
    assign(rig,action)
    bpy.context.scene.frame_set(1)
    first=positions(mesh)
    maximum=0
    changed=0
    for fraction in [0.25,0.5,0.75]:
        bpy.context.scene.frame_set(round(1+(action.frame_range[1]-1)*fraction))
        current=positions(mesh)
        distances=[(a-b).length for a,b in zip(first,current)]
        maximum=max(maximum,max(distances,default=0))
        changed=max(changed,sum(d>0.00001 for d in distances))
        if not all(math.isfinite(v) for p in current for v in p): raise ValueError('Nonfinite mesh')
        extent=max(max(p[i] for p in current)-min(p[i] for p in current) for i in range(3))
        if extent>15: raise ValueError('Unexpected animated mesh extent '+str(extent))
    return {'action':action.name,'changed_vertices':changed,'max_motion_m':maximum}

def studio(meshes,character=False):
    scene=bpy.context.scene
    bpy.context.view_layer.update()
    points=[o.matrix_world@Vector(p) for o in meshes for p in o.bound_box]
    lo=Vector([min(p[i] for p in points) for i in range(3)])
    hi=Vector([max(p[i] for p in points) for i in range(3)])
    center=(lo+hi)/2
    span=max(hi-lo)
    camera=bpy.data.objects.new('Asset_Overview',bpy.data.cameras.new('Asset_Overview'))
    collection=bpy.data.collections.new('Preview_Camera')
    scene.collection.children.link(collection)
    collection.objects.link(camera)
    camera.location=center+Vector((3.5,-6,2.2) if character else (1.7,-0.6,0.65)).normalized()*span*2
    camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO'
    camera.data.ortho_scale=span*1.24
    camera.data.clip_start=0.001
    scene.camera=camera
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x,scene.render.resolution_y=(640,720) if character else (1000,650)
    scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Studio')
    scene.world.use_nodes=True
    nodes=scene.world.node_tree.nodes
    nodes.clear()
    env=nodes.new('ShaderNodeTexEnvironment')
    env.image=bpy.data.images.load(str(Path(bpy.utils.resource_path('LOCAL'))/'datafiles/studiolights/world/studio.exr'),check_existing=True)
    bg=nodes.new('ShaderNodeBackground')
    bg.inputs['Strength'].default_value=0.8
    output=nodes.new('ShaderNodeOutputWorld')
    scene.world.node_tree.links.new(env.outputs['Color'],bg.inputs['Color'])
    scene.world.node_tree.links.new(bg.outputs[0],output.inputs['Surface'])
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.shading.type='MATERIAL'
                space.shading.studio_light='studio.exr'
                space.shading.studiolight_intensity=0.8
                space.overlay.show_overlays=False
                space.region_3d.view_location=center
                space.region_3d.view_distance=span*1.5
                space.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
                space.region_3d.view_perspective='ORTHO'
            elif area.type=='DOPESHEET_EDITOR': area.spaces.active.mode='ACTION'
    scene.render.use_simplify=False
    bpy.context.preferences.system.gl_texture_limit='CLAMP_OFF'
    return list(hi-lo)

def export_fbx(path,objects,animations=True):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'ARMATURE','MESH'},
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=animations,bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,bake_anim_simplify_factor=0.0,path_mode='COPY',embed_textures=True,
        axis_forward='-Z',axis_up='Y')
