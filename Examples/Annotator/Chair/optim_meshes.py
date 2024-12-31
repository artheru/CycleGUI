import numpy as np
import os
import pickle
import trimesh
import open3d as o3d
from scipy.spatial import cKDTree

def compute_neighbors(vertices, k=None, max_distance=None):
    """Find neighbors for each vertex using either max_distance or k nearest."""
    tree = cKDTree(vertices)
    neighbors = []
    for i, vertex in enumerate(vertices):
        if k:
            distances, indices = tree.query(vertex, k=k)
            neighbors.append(indices)
        elif max_distance:
            indices = tree.query_ball_point(vertex, max_distance)
            neighbors.append(indices)
        else:
            raise ValueError("Either k or max_distance must be specified.")
    return neighbors



def chamfer_distance(A, B):
    """Compute Chamfer Distance between two point clouds."""
    tree_A = cKDTree(A)
    tree_B = cKDTree(B)
    distances_A_to_B, _ = tree_A.query(B)
    distances_B_to_A, _ = tree_B.query(A)
    return np.mean(distances_A_to_B ** 2) + np.mean(distances_B_to_A ** 2)

def energy_similarity(D, A, B):
    """Improved similarity energy term using Chamfer Distance."""
    warped_A = A + D
    return chamfer_distance(warped_A, B)



def energy_structure(D, A, neighbors):
    """Compute the structure energy term."""
    warped_A = A + D
    energy = 0
    for i, neighbors_i in enumerate(neighbors):
        for j in neighbors_i:
            spring_stretch = warped_A[i] - warped_A[j]
            original_length = A[i] - A[j]
            energy += (np.linalg.norm(spring_stretch) - np.linalg.norm(original_length)) ** 2
    return energy


def compute_similarity_gradient(D, A, B):
    """Compute the gradient of the similarity term."""
    warped_A = A + D
    tree_B = cKDTree(B)
    distances, indices = tree_B.query(warped_A)
    closest_points = B[indices]
    return 2 * (warped_A - closest_points)


def compute_structure_gradient(D, A, neighbors):
    """Compute the gradient of the structure term using fewer neighbors."""
    warped_A = A + D
    gradient = np.zeros_like(A)

    for i, neighbors_i in enumerate(neighbors):
        # Get the neighboring vertices and compute stretch
        neighbor_vertices = warped_A[neighbors_i]
        spring_stretch = warped_A[i] - neighbor_vertices
        original_length = A[i] - A[neighbors_i]

        # Calculate spring deformation and accumulate gradient
        spring_deformation = (np.linalg.norm(spring_stretch, axis=1) - 
                              np.linalg.norm(original_length, axis=1))
        gradient[i] += np.sum((spring_deformation[:, None] * spring_stretch /
                               (np.linalg.norm(spring_stretch, axis=1, keepdims=True) + 1e-6)), axis=0)
    return gradient



def gradient_descent(A, B, neighbors, alpha, iterations, learning_rate):
    """Perform gradient descent to minimize energy."""
    print("In gradient descent....")
    D = np.zeros_like(A)  # Initialize deformation
    for it in range(iterations):
        grad_sim = compute_similarity_gradient(D, A, B)
        grad_str = compute_structure_gradient(D, A, neighbors)
        grad = grad_sim + alpha * grad_str
        D -= learning_rate * grad

        # Monitor energy
        energy = energy_similarity(D, A, B) + alpha * energy_structure(D, A, neighbors)
        print(f"Iteration {it + 1}/{iterations}, Energy: {energy}")
    return D