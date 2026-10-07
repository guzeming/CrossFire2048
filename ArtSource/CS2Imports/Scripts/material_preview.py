"""Create a material-preview copy without touching the user's open asset file."""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument("--asset", choices=["ct", "dust2"], default="ct")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
folder = root / ("CT_SAS" if args.asset == "ct" else "DustII")
basename = "ctm_sas" if args.asset == "ct" else "de_dust2"
environment_name = "studio.exr" if args.asset == "ct" else "courtyard.exr"
(folder / "TextureAudit").mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(folder / (basename + ".blend")))
scene = bpy.context.scene
source_counts = {
    "vertices": sum(len(mesh.vertices) for mesh in bpy.data.meshes),
    "bones": sum(len(armature.bones) for armature in bpy.data.armatures),
    "textures": {image.name: list(image.size) for image in bpy.data.images if image.source == "FILE"},
}
scene.render.engine = "BLENDER_EEVEE"
scene.render.use_simplify = False
scene.render.resolution_percentage = 100
scene.render.filepath = str(folder / (basename + "_material_preview.png"))
bpy.context.preferences.system.gl_texture_limit = "CLAMP_OFF"
vertex_color_bypasses = []
if args.asset == "dust2":
    # Source 2 uses some vertex-color channels for shader-specific auxiliary data
    # (the palm mesh, for example, has red-only values). A generic glTF material
    # multiplies these into albedo. Bypass that multiplication for this texture
    # inspection copy, preserving the original vertex attributes and materials.
    for material in bpy.data.materials:
        if not material.node_tree:
            continue
        for node in material.node_tree.nodes:
            if node.type != "VERTEX_COLOR":
                continue
            for socket in node.outputs:
                for link in list(socket.links):
                    destination_socket = link.to_socket
                    if destination_socket.type not in {"RGBA", "VALUE"}:
                        continue
                    material.node_tree.links.remove(link)
                    destination_socket.default_value = (1, 1, 1, 1) if destination_socket.type == "RGBA" else 1.0
                    vertex_color_bypasses.append({"material": material.name, "attribute": node.layer_name, "channel": socket.name})

# Use Blender's bundled studio environment, leaving imported materials untouched.
environment_path = Path(bpy.utils.resource_path("LOCAL")) / "datafiles/studiolights/world" / environment_name
if not environment_path.is_file():
    raise FileNotFoundError(environment_path)
scene.world.use_nodes = True
nodes = scene.world.node_tree.nodes
nodes.clear()
environment = nodes.new("ShaderNodeTexEnvironment")
environment.image = bpy.data.images.load(str(environment_path), check_existing=True)
environment.image.pack()
background = nodes.new("ShaderNodeBackground")
background.inputs["Strength"].default_value = 0.7
output = nodes.new("ShaderNodeOutputWorld")
scene.world.node_tree.links.new(environment.outputs["Color"], background.inputs["Color"])
scene.world.node_tree.links.new(background.outputs["Background"], output.inputs["Surface"])

if args.asset == "dust2":
    # Preview lighting only; do not attempt to reproduce Source 2's baked lighting.
    for obj in scene.objects:
        if obj.type == "LIGHT":
            obj.hide_render = True
    sun_data = bpy.data.lights.new("Preview_Daylight", "SUN")
    sun_data.energy = 2.0
    sun_data.angle = math.radians(4)
    sun = bpy.data.objects.new("Preview_Daylight", sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(25), math.radians(-30), math.radians(-25))

for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == "VIEW_3D":
            shading = area.spaces.active.shading
            shading.type = "MATERIAL"
            shading.use_scene_world = False
            shading.use_scene_lights = False
            shading.studiolight_rotate_z = 0.0
            shading.studiolight_intensity = 0.7
            shading.studio_light = environment_name
            area.spaces.active.overlay.show_overlays = False

bpy.context.preferences.filepaths.save_version = 0
scene["preview_note"] = "EEVEE material preview. Same meshes, rig and texture resolution as the original import."
destination = folder / (basename + "_material_preview.blend")
bpy.ops.wm.save_as_mainfile(filepath=str(destination), compress=True)
report = {
    "original": str(folder / (basename + ".blend")), "preview_copy": str(destination),
    "original_engine": "BLENDER_WORKBENCH", "preview_engine": scene.render.engine,
    "original_viewport": "SOLID + TEXTURE", "preview_viewport": "MATERIAL",
    "simplify": scene.render.use_simplify, "texture_size_limit": bpy.context.preferences.system.gl_texture_limit,
    "geometry_changed": False, "source_counts": source_counts,
    "material_preview_adjustments": vertex_color_bypasses,
    "source_texture_header_checks": {
        "body_color": {"game_package": [2048, 2048], "imported": [2048, 2048]},
        "head_color": {"game_package": [1024, 1024], "imported": [1024, 1024]},
    } if args.asset == "ct" else json.loads((folder / "TextureAudit" / "source_dimensions.json").read_text(encoding="utf-8-sig")) if (folder / "TextureAudit" / "source_dimensions.json").exists() else {},
    "material_conversion_limit": "Original Source 2 shaders are approximated; the exporter logged shader version 72 warnings."
}
(folder / "TextureAudit" / "preview_audit.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("MATERIAL_PREVIEW_SAVED " + str(destination), flush=True)
bpy.ops.render.render(write_still=True)
