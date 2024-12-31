import os
import numpy as np
import pickle
import trimesh
import traceback

from concept_template import *
from geometry_template import *
from utils import apply_transformation

current_dir = os.path.dirname(os.path.abspath(__file__))

# A dictionary to map template names to classes.
TEMPLATES = {
    "Multilevel_Body": Multilevel_Body,
    "Cylindrical_Lid": Cylindrical_Lid,
}

def load_conceptualization_data(pkl_path):
    with open(pkl_path, "rb") as f:
        data_list = pickle.load(f)
    return data_list

class ConceptualAssembly:
    def __init__(self, data_index=0):
        """
        Initialize the assembly from a conceptualization pickle.
        pkl_file: path to the conceptualization pickle file.
        data_index: which entry in the data_list to load.
        """
        pkl_file="conceptualization.pkl"
        
        pkl_path = os.path.join(current_dir, pkl_file)
        data_list = load_conceptualization_data(pkl_path)
        
        self.data = data_list[data_index]
        self.template_instances = []  # List of (template_name, parameters_dict, instance)
        self._build_from_data()

    def _build_from_data(self):
        """
        Build the template instances and full mesh from the loaded data.
        """
        self.template_instances.clear()

        vertices_list = []
        faces_list = []
        total_num_vertices = 0

        for c in self.data["conceptualization"]:
            template_name = c["template"]
            parameters = c["parameters"]
            cls = TEMPLATES.get(template_name)
            if cls is None:
                raise ValueError(f"Unknown template: {template_name}")

            # Filter parameters to match the constructor of the template class
            valid_params = self._filter_parameters(cls, parameters)
            component = cls(**valid_params)
            self.template_instances.append((template_name, valid_params, component))

            vertices_list.append(component.vertices)
            faces_list.append(component.faces + total_num_vertices)
            total_num_vertices += len(component.vertices)

        self._update_mesh(vertices_list, faces_list)

    def _update_mesh(self, vertices_list, faces_list):
        """
        Update the full mesh from vertices and faces lists.
        """
        if vertices_list:
            self.vertices = np.concatenate(vertices_list)
            self.faces = np.concatenate(faces_list)
        else:
            self.vertices = np.array([])
            self.faces = np.array([])

    def get_templates_info(self):
        """
        Returns a list of (template_name, parameters_dict).
        """
        return [(tname, dict(params)) for tname, params, _ in self.template_instances]

    def update_template_parameters(self, template_name, new_params):
        """
        Update the parameters of a specific template instance by template name.
        """
        for i, (tname, old_params, _) in enumerate(self.template_instances):
            if tname == template_name:
                # Merge old and new parameters and filter them
                cls = TEMPLATES[tname]
                valid_params = cls.__init__.__code__.co_varnames[1:cls.__init__.__code__.co_argcount]
                # filtered_params = {k: v for k, v in {**old_params, **new_params}.items() if k in valid_params}
                filtered_params = self._filter_parameters(cls, {**old_params, **new_params})


                # Rebuild the single component
                try:
                    component = cls(**filtered_params)
                except TypeError as e:
                    print(f"Error while creating component for {tname} with parameters {filtered_params}")
                    print(f"Error message: {e}")
                    traceback.print_exc()
                    raise

                # Update the instance
                self.template_instances[i] = (tname, filtered_params, component)

                # Rebuild full mesh
                self._recompute_full_mesh()
                return

        raise ValueError(f"Template name {template_name} not found")

    def _recompute_full_mesh(self):
        """
        Rebuild the full mesh from current template instances.
        """
        vertices_list = []
        faces_list = []
        total_num_vertices = 0
        
        for _, _, component in self.template_instances:
            vertices_list.append(component.vertices)
            faces_list.append(component.faces + total_num_vertices)
            total_num_vertices += len(component.vertices)
        self._update_mesh(vertices_list, faces_list)

    def get_full_mesh(self):
        """
        Return the current full mesh (vertices, faces).
        """
        return self.vertices, self.faces


    def get_single_component_mesh(self, template_name):
        """
        Return vertices and faces of a single conceptual component by template name.
        """
        for tname, _, component in self.template_instances:
            if tname == template_name:
                return component.vertices, component.faces

        raise ValueError(f"Template name {template_name} not found")


    def reset_to_original(self):
        """
        Reset the assembly to its original parameters from the data file.
        """
        self._build_from_data()

    def _filter_parameters(self, cls, params):
        """
        Filter and dynamically adjust parameters to match the constructor of the template class.
        Handle type mismatches such as float-to-int conversion and list conversion explicitly.
        """
        import inspect

        valid_params = cls.__init__.__code__.co_varnames[1:cls.__init__.__code__.co_argcount]
        filtered_params = {}

        for k, v in params.items():
            if k in valid_params:
                # Dynamically convert types
                if isinstance(v, list):
                    # Check if the list contains floats that should be integers
                    if all(isinstance(item, float) for item in v):
                        filtered_params[k] = [int(item) if item.is_integer() else item for item in v]
                    else:
                        filtered_params[k] = v
                elif isinstance(v, float):
                    # Convert single float to int if it represents an integer
                    filtered_params[k] = int(v) if v.is_integer() else v
                else:
                    # Keep the original value for other cases
                    filtered_params[k] = v
            else:
                print(f"Warning: Parameter '{k}' is not recognized for class {cls.__name__} and will be ignored.")

        return filtered_params


class DemoObject():
    def __init__(self, data_index=0):
        pkl_file="conceptualization.pkl"
        pkl_path = os.path.join(current_dir, pkl_file)
        data_list = load_conceptualization_data(pkl_path)
        # source_path = "object_model/27d0566b9a4b127cda95f26923bb8547.obj"
        data = data_list[data_index]
        # print("Target object id: ", data["id"])
        object_path = "object_model/" + data["id"] + ".obj"
        source_path = os.path.join(current_dir, object_path)
        source_mesh = trimesh.load_mesh(source_path)
        source_mesh_vertices = np.array(source_mesh.vertices)
        self.faces = np.array(source_mesh.faces)
        # mesh_scale = calculate_mesh_width(source_mesh_vertices)
        self.vertices = source_mesh_vertices# + np.array([-mesh_scale * 1.5, 0, 0]) 