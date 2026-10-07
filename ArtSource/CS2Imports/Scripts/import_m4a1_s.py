"""Import the HD M4A1-S, bake its weapon clips, and create a separate equipped CT scene."""
import bpy
import json
from pathlib import Path
from mathutils import Matrix, Vector

root = Path(__file__).resolve().parent.parent
folder = root / 'M4A1_S'
clips = root / 'CT_SAS/Animations/Clips'
scene = bpy.context.scene
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
bpy.ops.import_scene.gltf(filepath=str(folder / 'm4a1_s.gltf'))
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
rig.name = 'M4A1S_Rig'
rig.data.name = 'M4A1S_Bones'
rig.show_in_front = True
rig.data.display_type = 'STICK'
meshes = [o for o in scene.objects if o.type == 'MESH' and any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers)]
hd = next(o for o in meshes if o.name.endswith('body_hd'))
legacy = next(o for o in meshes if o.name.endswith('body_legacy'))
hd.name, legacy.name = 'M4A1S_HD', 'M4A1S_Legacy'
legacy_collection = bpy.data.collections.new('Legacy_Alternative')
scene.collection.children.link(legacy_collection)
for collection in list(legacy.users_collection):
    collection.objects.unlink(legacy)
legacy_collection.objects.link(legacy)
legacy_collection.hide_viewport = True
legacy_collection.hide_render = True
weapon_collection = bpy.data.collections.new('M4A1S_Weapon')
scene.collection.children.link(weapon_collection)
for obj in (rig, hd):
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    weapon_collection.objects.link(obj)
weights = []
for obj in meshes:
    groups = {g.index for g in obj.vertex_groups if g.name in rig.data.bones}
    unweighted = sum(not any(g.group in groups and g.weight > 0 for g in v.groups) for v in obj.data.vertices)
    assert unweighted == 0, obj.name
    weights.append({'mesh':obj.name,'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'unweighted':unweighted})
rest = {b.name:b.matrix_local.copy() for b in rig.data.bones}
for b in rig.pose.bones:
    b.rotation_mode = 'QUATERNION'
rig.animation_data_create()
actions = []
character_attachment_rest = None
for clip_name in ['draw_m4a1s','draw_crouch_m4a1s','reload_m4a1s','reload_crouch_m4a1s','shoot_m4a1s']:
    before_objects, before_actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=str(clips / (clip_name + '.gltf')))
    imported = set(bpy.data.objects)-before_objects
    source = next(o for o in imported if o.type == 'ARMATURE' and 'weapon_offset' in o.data.bones)
    character_source = next(o for o in imported if o.type == 'ARMATURE' and 'wpnPivot' in o.data.bones)
    character_attachment_rest = character_source.matrix_world @ character_source.data.bones['wpn'].matrix_local
    source_actions = set(bpy.data.actions)-before_actions
    count = round(max(a.frame_range[1] for a in source_actions)) + 1
    common = [b.name for b in rig.data.bones if b.name in source.data.bones]
    assert len(common) == 6, common
    correction = {n:(source.matrix_world @ source.data.bones[n].matrix_local).inverted() @ rig.matrix_world @ rest[n] for n in common}
    samples = {n:{'location':[],'rotation_quaternion':[],'scale':[]} for n in common}
    previous = {}
    for frame in range(count):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        desired = {}
        for bone in rig.data.bones:
            n = bone.name
            if n in common:
                desired[n] = rig.matrix_world.inverted() @ source.matrix_world @ source.pose.bones[n].matrix @ correction[n]
            else:
                desired[n] = desired[bone.parent.name] @ rest[bone.parent.name].inverted() @ rest[n] if bone.parent else rest[n]
            if n not in common:
                continue
            kwargs = {'parent_matrix':desired[bone.parent.name],'parent_matrix_local':rest[bone.parent.name]} if bone.parent else {}
            basis = bone.convert_local_to_pose(desired[n], rest[n], invert=True, **kwargs)
            loc, rot, scale = basis.decompose()
            if n in previous and previous[n].dot(rot) < 0:
                rot.negate()
            previous[n] = rot.copy()
            for key, value in [('location',loc),('rotation_quaternion',rot),('scale',scale)]:
                samples[n][key].append(tuple(value))
    action = bpy.data.actions.new('M4A1S_' + clip_name)
    slot = action.slots.new('OBJECT',rig.name)
    bag = action.layers.new('Weapon animation').strips.new(type='KEYFRAME').channelbag(slot,ensure=True)
    for n, channels in samples.items():
        for path, values in channels.items():
            for component in range(len(values[0])):
                curve = bag.fcurves.new(data_path=f'pose.bones["{n}"].{path}',index=component)
                curve.keyframe_points.add(count)
                curve.keyframe_points.foreach_set('co',[v for i,value in enumerate(values) for v in (i+1,value[component])])
                for key in curve.keyframe_points:
                    key.interpolation = 'LINEAR'
                curve.update()
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1,count
    action.asset_mark()
    actions.append({'name':action.name,'frames':count,'source_clip':clip_name})
    for obj in imported:
        bpy.data.objects.remove(obj,do_unlink=True)
    for a in source_actions:
        bpy.data.actions.remove(a)
    print('WEAPON_ANIMATION '+action.name,flush=True)
rig.animation_data.action = None
for bone in rig.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
scene.frame_set(1)
bpy.context.view_layer.update()

# Neutral studio preview of the HD mesh; keep original texture pixel dimensions.
points = [hd.matrix_world @ Vector(p) for p in hd.bound_box]
low = Vector([min(p[i] for p in points) for i in range(3)])
high = Vector([max(p[i] for p in points) for i in range(3)])
center = (low+high)/2
span = max(high-low)
print('BOUNDS '+str(list(high-low)),flush=True)
preview = bpy.data.collections.new('Preview_Camera')
scene.collection.children.link(preview)
camera_data = bpy.data.cameras.new('Weapon_Overview')
camera = bpy.data.objects.new('Weapon_Overview',camera_data)
preview.objects.link(camera)
# Source weapon points along -Y in Blender; use a readable three-quarter side view.
camera.location = center + Vector((1.7,-0.6,0.65)).normalized()*span*2
camera.rotation_euler = (center-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.type = 'ORTHO'
camera_data.ortho_scale = span*1.24
camera_data.clip_start = 0.001
scene.camera = camera
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x, scene.render.resolution_y = 1200,720
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new('Weapon_Studio')
scene.world.use_nodes = True
nodes = scene.world.node_tree.nodes
nodes.clear()
env = nodes.new('ShaderNodeTexEnvironment')
env.image = bpy.data.images.load(str(Path(bpy.utils.resource_path('LOCAL'))/'datafiles/studiolights/world/studio.exr'))
background = nodes.new('ShaderNodeBackground')
background.inputs['Strength'].default_value = 0.8
output = nodes.new('ShaderNodeOutputWorld')
scene.world.node_tree.links.new(env.outputs['Color'],background.inputs['Color'])
scene.world.node_tree.links.new(background.outputs[0],output.inputs['Surface'])
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            space = area.spaces.active
            space.shading.type = 'MATERIAL'
            space.shading.studio_light = 'studio.exr'
            space.shading.studiolight_intensity = 0.8
            space.overlay.show_overlays = False
            space.region_3d.view_location = center
            space.region_3d.view_distance = span*1.25
            space.region_3d.view_rotation = camera.rotation_euler.to_quaternion()
            space.region_3d.view_perspective = 'ORTHO'
scene.render.use_simplify = False
bpy.context.preferences.system.gl_texture_limit = 'CLAMP_OFF'
images = [{'name':i.name,'size':list(i.size)} for i in bpy.data.images if i.source == 'FILE' and i != env.image]
assert all(i['size'][0] > 0 for i in images)
bpy.ops.file.pack_all()
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
hd.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.context.preferences.filepaths.save_version = 0
scene['source_model'] = 'weapons/models/m4a1_silencer/weapon_rif_m4a1_silencer.vmdl_c'
scene['weapon_note'] = 'HD variant visible; legacy variant preserved hidden. Original geometry/UV/weights/textures; approximate materials.'
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'m4a1_s.blend'),compress=True)
bpy.ops.export_scene.fbx(filepath=str(folder/'m4a1_s.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},
    add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True,bake_anim_simplify_factor=0.0,path_mode='COPY',embed_textures=True,axis_forward='-Z',axis_up='Y')
scene.render.filepath = str(folder/'m4a1_s_preview.png')
bpy.ops.render.render(write_still=True)
report = {'meshes':weights,'default_mesh':hd.name,'bones':[b.name for b in rig.data.bones],
          'images':images,'actions':actions,'size_meters':list(high-low),'source':scene['source_model']}
(folder/'m4a1_s_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

# Equip a separate CT scene through the original animation's weapon attachment.
weapon_rest = rig.data.bones['weapon'].matrix_local.copy()
bpy.ops.wm.open_mainfile(filepath=str(root/'CT_SAS/ctm_sas_animated.blend'))
scene = bpy.context.scene
character = next(o for o in scene.objects if o.type == 'ARMATURE')
with bpy.data.libraries.load(str(folder/'m4a1_s.blend'),link=False) as (available,loaded):
    loaded.collections = ['M4A1S_Weapon']
    loaded.actions = [a['name'] for a in actions]
for collection in loaded.collections:
    scene.collection.children.link(collection)
weapon = next(o for o in loaded.collections[0].all_objects if o.type == 'ARMATURE')
anchor = bpy.data.objects.new('M4A1S_Attachment',None)
scene.collection.objects.link(anchor)
constraint = anchor.constraints.new('COPY_TRANSFORMS')
constraint.target = character
constraint.subtarget = 'wpn'
weapon.parent = anchor
weapon.matrix_parent_inverse = Matrix.Identity(4)
weapon.matrix_basis = character.data.bones['wpn'].matrix_local.inverted() @ character_attachment_rest @ weapon_rest.inverted()
character.animation_data.action = bpy.data.actions['reload_m4a1s']
character.animation_data.action_slot = character.animation_data.action.slots[0]
weapon.animation_data_create()
weapon.animation_data.action = bpy.data.actions['M4A1S_reload_m4a1s']
weapon.animation_data.action_slot = weapon.animation_data.action.slots[0]
scene.frame_start, scene.frame_end = 1,93
scene.frame_set(1)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
character.select_set(True)
bpy.context.view_layer.objects.active = character
scene['equipped_note'] = 'M4A1-S and CT have synchronized reload_m4a1s actions. Other clips require switching both matching actions. Locomotion: clear weapon action to keep neutral parts.'
bpy.ops.wm.save_as_mainfile(filepath=str(root/'CT_SAS/ctm_sas_m4a1_s.blend'),compress=True)
scene.render.resolution_x, scene.render.resolution_y = 768,768
scene.render.resolution_percentage = 100
scene.render.filepath = str(folder/'ct_m4a1_s_preview.png')
bpy.ops.render.render(write_still=True)
print('M4A1S_COMPLETE',flush=True)
