import bmesh
import bpy
import json
import math
import os
import sys


STATUS_PREFIX = "SCENEBUILDER_STATUS:"
PROCEDURAL_KINDS = {"wall", "floor", "column", "road"}
EXTRUDED_KINDS = {"wall", "column"}
ASSET_KINDS = {"static-asset", "dynamic-asset"}
PLACEHOLDER_KINDS = {"static-placeholder", "dynamic-placeholder"}


def is_finite_number(value):
    return isinstance(value, (int, float)) and math.isfinite(value)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def parse_arguments():
    require("--" in sys.argv, "Missing Blender script argument separator.")
    arguments = sys.argv[sys.argv.index("--") + 1:]
    require(len(arguments) == 4, "Expected manifest and output arguments.")
    require(arguments[0] == "--manifest" and arguments[2] == "--output", "Unexpected Blender script arguments.")
    require(arguments[1] and arguments[3], "Manifest and output values are required.")
    return arguments[1], arguments[3]


def read_manifest(manifest_path):
    with open(manifest_path, "r", encoding="utf-8") as manifest_file:
        manifest = json.load(manifest_file)

    require(isinstance(manifest, dict), "Manifest root must be an object.")
    require(manifest.get("contractVersion") in {"1.0", "2.0"}, "Unsupported manifest contract version.")
    require(manifest.get("unit") == "meters", "Manifest unit must be meters.")
    require(isinstance(manifest.get("draftId"), str) and manifest["draftId"], "Manifest draft id is required.")
    require(isinstance(manifest.get("objects"), list) and manifest["objects"], "Manifest objects are required.")
    return manifest


def validate_procedural(item):
    profile = item.get("profile")
    require(isinstance(profile, list) and len(profile) >= 3, "Manifest profile must contain at least three points.")
    points = []
    for point in profile:
        require(isinstance(point, dict), "Manifest profile point must be an object.")
        require(all(is_finite_number(point.get(axis)) for axis in ("x", "y", "z")), "Manifest profile coordinates must be finite.")
        points.append((float(point["x"]), float(point["y"]), float(point["z"])))

    height = item.get("heightMeters")
    if item["kind"] in EXTRUDED_KINDS:
        require(is_finite_number(height) and height > 0, "Extruded objects require a positive height.")
    else:
        require(height is None, "Planar objects must not define a height.")
    return points, height


def validate_transform(item):
    position = item.get("position")
    scale = item.get("scale")
    require(isinstance(position, dict) and isinstance(scale, dict), "Asset instances require position and scale.")
    require(all(is_finite_number(position.get(axis)) for axis in ("x", "y", "z")), "Asset position must be finite.")
    require(all(is_finite_number(scale.get(axis)) for axis in ("x", "y", "z")), "Asset scale must be finite.")
    require(is_finite_number(item.get("rotationDegrees")), "Asset rotation must be finite.")
    return position, float(item["rotationDegrees"]), scale


def safe_asset_path(manifest_path, asset_file):
    require(isinstance(asset_file, str) and asset_file, "Asset file is required.")
    require(not os.path.isabs(asset_file), "Asset file must be relative.")
    segments = asset_file.replace("\\", "/").split("/")
    require(all(segment and segment not in {".", ".."} for segment in segments), "Asset file path is unsafe.")
    require(asset_file.lower().endswith(".glb"), "Asset file must be a GLB.")
    manifest_directory = os.path.realpath(os.path.dirname(manifest_path))
    asset_path = os.path.realpath(os.path.join(manifest_directory, *segments))
    require(os.path.commonpath([manifest_directory, asset_path]) == manifest_directory, "Asset file escaped manifest directory.")
    require(os.path.isfile(asset_path), "Staged asset file is unavailable.")
    return asset_path


def ensure_collection(name):
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(collection)
    return collection


def create_procedural_mesh(item, collection):
    points, height = validate_procedural(item)
    mesh = bpy.data.meshes.new("SceneBuilderMesh")
    mesh.from_pydata(points, [], [list(range(len(points)))])
    mesh.update()
    scene_object = bpy.data.objects.new("SceneBuilderObject", mesh)
    collection.objects.link(scene_object)
    if item["kind"] in EXTRUDED_KINDS:
        mesh_builder = bmesh.new()
        mesh_builder.from_mesh(mesh)
        faces = list(mesh_builder.faces)
        require(len(faces) == 1, "Extruded profile must create one face.")
        extrusion = bmesh.ops.extrude_face_region(mesh_builder, geom=faces)
        vertices = [element for element in extrusion["geom"] if isinstance(element, bmesh.types.BMVert)]
        bmesh.ops.translate(mesh_builder, vec=(0.0, 0.0, float(height)), verts=vertices)
        bmesh.ops.recalc_face_normals(mesh_builder, faces=list(mesh_builder.faces))
        mesh_builder.to_mesh(mesh)
        mesh_builder.free()


def apply_transform(parent, position, rotation_degrees, scale):
    parent.location = (float(position["x"]), float(position["y"]), float(position["z"]))
    parent.rotation_euler = (0.0, 0.0, math.radians(rotation_degrees))
    parent.scale = (float(scale["x"]), float(scale["y"]), float(scale["z"]))


def import_asset(item, manifest_path, collection):
    position, rotation_degrees, scale = validate_transform(item)
    asset_path = safe_asset_path(manifest_path, item.get("assetFile"))
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=asset_path)
    imported = [scene_object for scene_object in bpy.data.objects if scene_object not in before]
    require(imported, "Asset import did not create objects.")
    parent = bpy.data.objects.new("SceneBuilderAssetInstance", None)
    collection.objects.link(parent)
    for scene_object in imported:
        if scene_object.parent is None or scene_object.parent not in imported:
            scene_object.parent = parent
    apply_transform(parent, position, rotation_degrees, scale)


def create_placeholder(item, collection):
    position, rotation_degrees, scale = validate_transform(item)
    size = item.get("placeholderSize")
    require(isinstance(size, dict) and all(is_finite_number(size.get(axis)) and size[axis] > 0 for axis in ("x", "y", "z")), "Placeholder size must be positive and explicit.")
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    placeholder = bpy.context.active_object
    placeholder.name = "SceneBuilderPlaceholder"
    placeholder.dimensions = (float(size["x"]), float(size["y"]), float(size["z"]))
    parent = bpy.data.objects.new("SceneBuilderPlaceholderInstance", None)
    collection.objects.link(parent)
    placeholder.parent = parent
    apply_transform(parent, position, rotation_degrees, scale)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def export_scene(output_path):
    output_parent = os.path.dirname(output_path)
    require(output_parent and os.path.isdir(output_parent), "Output directory is unavailable.")
    require(not os.path.exists(output_path), "Staging output already exists.")
    bpy.ops.export_scene.gltf(filepath=output_path, export_format="GLB", export_apply=True)


def main():
    manifest_path, output_path = parse_arguments()
    manifest = read_manifest(manifest_path)
    clear_scene()
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    procedural_collection = ensure_collection("SB_PROCEDURAL")
    static_collection = ensure_collection("SB_STATIC_ASSETS")
    dynamic_collection = ensure_collection("SB_DYNAMIC_ASSETS")
    for item in manifest["objects"]:
        require(isinstance(item, dict) and isinstance(item.get("id"), str) and item["id"], "Manifest object id is required.")
        kind = item.get("kind")
        if kind in PROCEDURAL_KINDS:
            create_procedural_mesh(item, procedural_collection)
        elif manifest["contractVersion"] == "2.0" and kind in ASSET_KINDS:
            import_asset(item, manifest_path, static_collection if kind == "static-asset" else dynamic_collection)
        elif manifest["contractVersion"] == "2.0" and kind in PLACEHOLDER_KINDS:
            create_placeholder(item, static_collection if kind == "static-placeholder" else dynamic_collection)
        else:
            raise ValueError("Manifest object kind is unsupported.")
    export_scene(output_path)
    print(STATUS_PREFIX + "SUCCEEDED")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print(STATUS_PREFIX + "FAILED")
        raise
