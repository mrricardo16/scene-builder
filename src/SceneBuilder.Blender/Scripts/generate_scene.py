import bmesh
import bpy
import json
import math
import os
import sys


CONTRACT_VERSION = "1.0"
STATUS_PREFIX = "SCENEBUILDER_STATUS:"
SUPPORTED_KINDS = {"wall", "floor", "column", "road"}
EXTRUDED_KINDS = {"wall", "column"}


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
    require(manifest.get("contractVersion") == CONTRACT_VERSION, "Unsupported manifest contract version.")
    require(manifest.get("unit") == "meters", "Manifest unit must be meters.")
    require(isinstance(manifest.get("draftId"), str) and manifest["draftId"], "Manifest draft id is required.")
    require(isinstance(manifest.get("objects"), list) and manifest["objects"], "Manifest objects are required.")
    return manifest


def validate_object(item):
    require(isinstance(item, dict), "Manifest object must be an object.")
    require(isinstance(item.get("id"), str) and item["id"], "Manifest object id is required.")
    require(item.get("kind") in SUPPORTED_KINDS, "Manifest object kind is unsupported.")
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


def create_mesh(item):
    points, height = validate_object(item)
    mesh = bpy.data.meshes.new("SceneBuilderMesh")
    mesh.from_pydata(points, [], [list(range(len(points)))])
    mesh.update()

    scene_object = bpy.data.objects.new("SceneBuilderObject", mesh)
    bpy.context.collection.objects.link(scene_object)

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
    for item in manifest["objects"]:
        create_mesh(item)
    export_scene(output_path)
    print(STATUS_PREFIX + "SUCCEEDED")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print(STATUS_PREFIX + "FAILED")
        raise
