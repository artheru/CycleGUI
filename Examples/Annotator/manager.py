import importlib
import os
import sys

def load_object(object_name):
    """
    Dynamically load and return a module based on the object name.
    """
    base_path = os.path.dirname(__file__)
    object_path = os.path.join(base_path, object_name)
    # print(f"What is the object path: {object_path}")
    if not os.path.exists(object_path):
        raise ValueError(f"Object folder '{object_name}' does not exist")

    if object_path not in sys.path:
        sys.path.insert(0, object_path)

    try:
        # Clear previous module and its dependencies from sys.modules
        for module_name in ["main", "concept_template"]:
            if module_name in sys.modules:
                # print(f"Removing cached module: {module_name}")
                del sys.modules[module_name]
        
        # print(f"Object path {object_path}")
        module = importlib.import_module("main")
        module = importlib.reload(module)  # Ensure fresh reload
        # print(f"Imported module {module}")
        return module
    except ImportError as e:
        print(f"Object name is {object_name} || path is {object_path}")
        raise ImportError(f"Failed to import module '{object_name}': {e}")
    finally:
        # Clean up sys.path
        if object_path in sys.path:
            sys.path.remove(object_path)
