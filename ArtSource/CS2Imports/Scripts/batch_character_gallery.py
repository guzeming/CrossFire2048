import bpy,json,sys
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from batch_common import ROOT,BATCH
plan=json.loads((BATCH/'plan.json').read_text())
entries=[e for e in plan['models'] if e['category']=='Characters']
order=['ctm_sas','ctm_fbi','ctm_st6_variante','ctm_swat_variante','ctm_gendarmerie_varianta',
       'tm_phoenix','tm_leet_varianta','tm_balkan_variantf','tm_professional_varf','tm_jungle_raider_varianta']
labels=['CT / SAS','CT / FBI','CT / ST6','CT / SWAT','CT / GENDARMERIE',
        'T / PHOENIX','T / LEET','T / BALKAN','T / PROFESSIONAL','T / JUNGLE RAIDER']
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.render.fps=30
for index,name in enumerate(order):
    entry=next(e for e in entries if e['id']==name)
    report=json.loads((BATCH/entry['relative_dir']/'report.json').read_text())
    filepath=Path(report['blend'])
    visible_names=[m['mesh'] for m in report['meshes'] if m['mesh'] not in report['hidden'] and 'firstperson_' not in m['mesh'] and 'body_legacy' not in m['mesh']]
    rig_name=name+'_Rig'
    with bpy.data.libraries.load(str(filepath),link=False) as (src,dst):
        dst.objects=visible_names+[rig_name]
    collection=bpy.data.collections.new(labels[index])
    scene.collection.children.link(collection)
    for obj in dst.objects:
        if obj: collection.objects.link(obj)
    anchor=bpy.data.objects.new(name+'_Layout',None)
    collection.objects.link(anchor)
    group=set(o for o in dst.objects if o)
    for obj in group:
        if obj.parent not in group:
            matrix=obj.matrix_world.copy()
            obj.parent=anchor
            obj.matrix_world=matrix
    anchor.location=((index%5-2)*1.65,0,2.55 if index<5 else 0)
    text_curve=bpy.data.curves.new(labels[index],'FONT')
    text_curve.body=labels[index]
    text_curve.align_x='CENTER'
    text_curve.size=0.12
    text_obj=bpy.data.objects.new(labels[index],text_curve)
    scene.collection.objects.link(text_obj)
    text_obj.location=((index%5-2)*1.65,-0.75,(2.55 if index<5 else 0)-0.17)
    text_obj.rotation_euler=(1.570796,0,0)
    mat=bpy.data.materials.get('Labels') or bpy.data.materials.new('Labels')
    mat.use_nodes=True
    mat.node_tree.nodes.clear()
    emission=mat.node_tree.nodes.new('ShaderNodeEmission')
    emission.inputs['Color'].default_value=(0.7,0.8,1.0,1)
    mat_output=mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(emission.outputs[0],mat_output.inputs[0])
    text_curve.materials.append(mat)
scene.frame_set(6)
scene.world=bpy.data.worlds.new('Gallery_Studio')
scene.world.use_nodes=True
nodes=scene.world.node_tree.nodes
nodes.clear()
env=nodes.new('ShaderNodeTexEnvironment')
env.image=bpy.data.images.load(str(Path(bpy.utils.resource_path('LOCAL'))/'datafiles/studiolights/world/studio.exr'),check_existing=True)
bg=nodes.new('ShaderNodeBackground')
bg.inputs['Strength'].default_value=0.8
output=nodes.new('ShaderNodeOutputWorld')
scene.world.node_tree.links.new(env.outputs['Color'],bg.inputs['Color'])
dark=nodes.new('ShaderNodeBackground')
dark.inputs['Color'].default_value=(0.018,0.025,0.04,1)
light_path=nodes.new('ShaderNodeLightPath')
mix=nodes.new('ShaderNodeMixShader')
scene.world.node_tree.links.new(light_path.outputs['Is Camera Ray'],mix.inputs[0])
scene.world.node_tree.links.new(bg.outputs[0],mix.inputs[1])
scene.world.node_tree.links.new(dark.outputs[0],mix.inputs[2])
scene.world.node_tree.links.new(mix.outputs[0],output.inputs[0])
fill_data=bpy.data.lights.new('Gallery_Fill','AREA')
fill_data.energy=300
fill_data.shape='RECTANGLE'
fill_data.size=9
fill_data.size_y=5
fill=bpy.data.objects.new('Gallery_Fill',fill_data)
scene.collection.objects.link(fill)
fill.location=(0,-5,3)
fill.rotation_euler=(Vector((0,0,2.2))-fill.location).to_track_quat('-Z','Y').to_euler()
scene.view_settings.look='AgX - Medium High Contrast'
scene.view_settings.exposure=0.0
camera=bpy.data.objects.new('Character_Selection',bpy.data.cameras.new('Character_Selection'))
scene.collection.objects.link(camera)
center=Vector((0,0,2.15))
camera.location=center+Vector((0,-15,0.6))
camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO'
camera.data.ortho_scale=9.2
scene.camera=camera
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x,scene.render.resolution_y=1800,1100
scene.render.resolution_percentage=100
scene.render.filepath=str(BATCH/'characters_overview.png')
scene.frame_start,scene.frame_end=1,23
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active
            space.shading.type='MATERIAL'
            space.shading.studio_light='studio.exr'
            space.overlay.show_overlays=False
            space.region_3d.view_perspective='CAMERA'
            space.region_3d.view_camera_zoom=0
scene['note']='Selected 5 CT and 5 T. Individual editable files and FBX exports are in Characters/. Geometry and rigs remain separate.'
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(BATCH/'characters_overview.blend'),compress=True)
bpy.ops.render.render(write_still=True)
print('GALLERY_COMPLETE',flush=True)
