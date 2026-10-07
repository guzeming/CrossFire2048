"""Blender --background MODEL.blend --python this.py -- --clip SOURCE.gltf --output NEW.blend"""
import argparse,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from batch_common import *
p=argparse.ArgumentParser()
p.add_argument('--clip',required=True)
p.add_argument('--output',required=True)
p.add_argument('--rig')
args=p.parse_args(sys.argv[sys.argv.index('--')+1:])
target=bpy.data.objects.get(args.rig) if args.rig else next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
if not target or target.type!='ARMATURE': raise ValueError('Target armature missing')
if Path(args.output).exists(): raise FileExistsError('Choose a new output file: '+args.output)
bpy.context.scene.render.fps=30
before=set(bpy.data.objects)
old_actions=set(bpy.data.actions)
bpy.ops.import_scene.gltf(filepath=str(Path(args.clip).resolve()))
imported=set(bpy.data.objects)-before
sources=[o for o in imported if o.type=='ARMATURE']
source=max(sources,key=lambda o:len(set(o.data.bones.keys())&set(target.data.bones.keys())))
common=set(source.data.bones.keys())&set(target.data.bones.keys())
if not common: raise ValueError('No common bones; select the matching character or weapon clip')
original_actions=set(bpy.data.actions)-old_actions
action=bake_action(target,source,Path(args.clip).stem,*source.animation_data.action.frame_range)
for obj in imported: bpy.data.objects.remove(obj,do_unlink=True)
for old in original_actions: bpy.data.actions.remove(old)
assign(target,action)
scene=bpy.context.scene
scene.frame_start,scene.frame_end=1,int(action.frame_end)
scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
target.select_set(True)
bpy.context.view_layer.objects.active=target
bpy.ops.wm.save_as_mainfile(filepath=str(Path(args.output).resolve()),compress=True)
print('RETARGETED '+str(len(common))+' bones to '+args.output)
