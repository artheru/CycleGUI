import os
import numpy as np
import open3d as o3d
import pickle
import trimesh
from concept_template import *
from geometry_template import *

def calculate_mesh_width(vertices):
    x_coords = [vertex[0] for vertex in vertices]
    return max(x_coords) - min(x_coords)

def draw_arrow(scale):
    vertices_list = []
    faces_list = []
    total_num_vertices = 0

    cylinder = Cylinder(scale, scale*0.15, position=[-scale*0.4/2,0,0], rotation=[0,0,np.pi/2])
    vertices_list.append(cylinder.vertices)
    faces_list.append(cylinder.faces + total_num_vertices)
    total_num_vertices += len(cylinder.vertices)

    cone = Cone(scale*0.3, scale*0.4, position=[scale/2 - scale*0.4/2,0,0], rotation=[0,0,-np.pi/2])
    vertices_list.append(cone.vertices)
    faces_list.append(cone.faces + total_num_vertices)
    total_num_vertices+= len(cone.vertices)

    vertices = np.concatenate(vertices_list)
    faces = np.concatenate(faces_list)
    arrow_mesh = o3d.geometry.TriangleMesh(o3d.utility.Vector3dVector(vertices),
                                           o3d.utility.Vector3iVector(faces))
    arrow_mesh.compute_vertex_normals()
    colors = np.array([[94/255,133/255,212/255] for _ in range(len(arrow_mesh.vertices))])
    arrow_mesh.vertex_colors = o3d.utility.Vector3dVector(colors)
    return arrow_mesh

def render_conceptualization_to_mesh(data):
    vertices_list = []
    faces_list = []
    total_num_vertices = 0

    for c in data["conceptualization"]:
        module = eval(c["template"])
        component = module(**c["parameters"])
        vertices_list.append(component.vertices)
        faces_list.append(component.faces + total_num_vertices)
        total_num_vertices += len(component.vertices)

    final_vertices = np.concatenate(vertices_list)
    final_faces = np.concatenate(faces_list)
    return final_vertices, final_faces

def get_conceptual_templates(data):
    """
    Returns a list of (template_name, parameters_dict) for each conceptual component.
    For example: [("Standard_Sphere", {"radius":[0.59], ...}), ("Semi_Ring_Bracket", {...}), ...]
    """
    templates_info = []
    for c in data["conceptualization"]:
        template_name = c["template"]
        parameters = c["parameters"]
        templates_info.append((template_name, parameters))
    return templates_info

def load_conceptualization_data(pkl_path):
    # Load the full conceptualization data from a pickle
    with open(pkl_path, "rb") as f:
        data_list = pickle.load(f)
    return data_list


if __name__ == "__main__":

    data_list = load_conceptualization_data("conceptualization.pkl")

    for data in data_list:
        if data["type"] == "scanned":
            print("Model id: %s  (Scanned model)" % (data["id"]))
        else:
            print("Model id: %s" % (data["id"]))

        has_object_model = True
        if not os.path.exists("object_model"):
            has_object_model = False
        elif not os.path.exists("object_model/%s.obj"%data["id"]):
            has_object_model = False

        # Get object model
        if has_object_model:
            source_path = "object_model/" + data["id"] + ".obj"
            source_mesh = trimesh.load_mesh(source_path)
            source_mesh_vertices = np.array(source_mesh.vertices)
            source_mesh_faces = np.array(source_mesh.faces)
            mesh_scale = calculate_mesh_width(source_mesh_vertices)
            source_mesh_vertices = source_mesh_vertices + np.array([-mesh_scale * 1.5, 0, 0])
            source_mesh_vertices = o3d.utility.Vector3dVector(source_mesh_vertices)
            source_mesh_faces = o3d.utility.Vector3iVector(source_mesh_faces)
            source_mesh = o3d.geometry.TriangleMesh(source_mesh_vertices, source_mesh_faces)
            source_mesh.compute_vertex_normals()

        # Get conceptualization
        vertices, faces = render_conceptualization_to_mesh(data)
        if has_object_model:
            vertices = vertices + np.array([mesh_scale * 1.5, 0, 0])
        vertices = o3d.utility.Vector3dVector(vertices)
        faces = o3d.utility.Vector3iVector(faces)
        conceptualization_rendering = o3d.geometry.TriangleMesh(vertices, faces)
        conceptualization_rendering.compute_vertex_normals()

        if has_object_model:
            arrow_mesh = draw_arrow(mesh_scale * 0.5)
            o3d.visualization.draw_geometries([conceptualization_rendering, arrow_mesh, source_mesh])
        else:
            o3d.visualization.draw_geometries([conceptualization_rendering])
