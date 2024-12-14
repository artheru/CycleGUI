import os
import numpy as np
import pickle
from concept_template import *
from geometry_template import *


def calculate_mesh_width(vertices):
    x_coords = [vertex[0] for vertex in vertices]
    return max(x_coords) - min(x_coords)


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


def prepare_mesh(vertices, faces):
    """Flattens the mesh data into a single float array."""
    expanded_vertices = vertices[faces.flatten()]
    return expanded_vertices.flatten()

def rotate_x_axis(mesh):
    R = np.array(
        [[1, 0, 0],
         [0, 0, -1],
         [0, 1, 0]])
    
    rotated_mesh = np.dot(np.asarray(mesh), R.T)
    return rotated_mesh

def generate_mesh_from_pkl(pkl_path):
    with open(pkl_path, "rb") as f:
        data_list = pickle.load(f)

    # for testing purpose use only a single data
    data_single = data_list[0]
    model_id = data_single["id"]
    source_path = "object_model/" + model_id + ".obj"
    source_mesh = trimesh.load_mesh(source_path)
    source_mesh_vertices = np.array(source_mesh.vertices)
    source_mesh_faces = np.array(source_mesh.faces)
    mesh_scale = calculate_mesh_width(source_mesh_vertices)
    # source_mesh_vertices = source_mesh_vertices + np.array([-mesh_scale * 1.5, 0, 0])
    real_mesh = prepare_mesh(source_mesh_vertices, source_mesh_faces)
    # conceptualized mesh
    vertices, faces = render_conceptualization_to_mesh(data_single)
    concept_mesh = prepare_mesh(vertices, faces)

    return real_mesh, concept_mesh



    # all_meshes = {}

    for data in data_list:
        model_id = data["id"]
        vertices, faces = render_conceptualization_to_mesh(data)
        mesh = prepare_mesh(vertices, faces)
        all_meshes[model_id] = mesh

    return all_meshes


if __name__ == "__main__":
    # Example usage
    pkl_path = "conceptualization.pkl"
    meshes, _ = generate_mesh_from_pkl(pkl_path)

    # Output meshes as needed
    for model_id, mesh in meshes.items():
        print(f"Model {model_id}: {len(mesh)} values")
