import os
import bpy
from mathutils import Vector


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ASSET_DIR = os.path.join(ROOT, "assets", "zombiemod", "invisible_knife")
SOURCE_DIR = os.path.join(ASSET_DIR, "source")


def ensure_dir(path):
    os.makedirs(path, exist_ok=True)


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()


def create_tiny_transparent_mesh():
    mesh = bpy.data.meshes.new("zm_invisible_knife_mesh")
    vertices = [
        Vector((0.0, 0.0, 0.0)),
        Vector((0.0005, 0.0, 0.0)),
        Vector((0.0, 0.0005, 0.0)),
    ]
    mesh.from_pydata(vertices, [], [(0, 1, 2)])
    mesh.update()

    material = bpy.data.materials.new("zm_invisible_knife_transparent")
    material.use_nodes = True
    material.blend_method = "BLEND"
    material.show_transparent_back = True
    material.diffuse_color = (1.0, 1.0, 1.0, 0.0)

    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled:
        principled.inputs["Alpha"].default_value = 0.0
        principled.inputs["Base Color"].default_value = (1.0, 1.0, 1.0, 0.0)

    obj = bpy.data.objects.new("zm_invisible_knife", mesh)
    obj.data.materials.append(material)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    return obj


def export_assets():
    ensure_dir(SOURCE_DIR)
    reset_scene()
    create_tiny_transparent_mesh()

    blend_path = os.path.join(SOURCE_DIR, "zm_invisible_knife.blend")
    fbx_path = os.path.join(SOURCE_DIR, "zm_invisible_knife.fbx")
    glb_path = os.path.join(SOURCE_DIR, "zm_invisible_knife.glb")
    obj_path = os.path.join(SOURCE_DIR, "zm_invisible_knife.obj")

    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=False,
        apply_unit_scale=True,
        bake_space_transform=False,
        add_leaf_bones=False,
    )
    bpy.ops.export_scene.gltf(
        filepath=glb_path,
        export_format="GLB",
        export_materials="EXPORT",
        export_yup=True,
    )
    bpy.ops.wm.obj_export(filepath=obj_path, export_selected_objects=False)

    print(f"Created invisible knife source assets in: {SOURCE_DIR}")


if __name__ == "__main__":
    export_assets()
