using CycleGUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using CycleGUI.API;
using Python.Runtime;

namespace Annotator
{
    internal partial class PanelConstructors
    {
        public static PanelBuilder.CycleGUIHandler DefineTargetObjectPanel()
        {
            var objectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Objects"));
            var objectCategories = Directory.GetDirectories(objectDir)
                    .Select(dir => Path.GetFileName(dir) ?? "No such directory")
                    .Where(folderName => folderName != "__pycache__")
                    .ToArray();

            foreach (var category in objectCategories)
            {
                var categoryDir = Path.Combine(objectDir, category);
                var modelDir = Path.Combine(categoryDir, "object_model");

                if (!Directory.Exists(modelDir))
                {
                    _targetObjects[category] = new List<string>();
                    continue;
                }

                var objFiles = Directory.GetFiles(modelDir, "*.obj", SearchOption.AllDirectories)
                    .Select(Path.GetFullPath)
                    .ToList();

                _targetObjects[category] = objFiles;
            }

            var initSelectable = false;
            int lastCategoryIndex = -1, categoryIndex = 0;
            var glbDisplayed = false;

            var searchMode = 0;
            var searchIgnoreCase = true;
            var objectIndex = 0;
            string lastObjectIdStr = "", lastSearchStr = "";

            GuizmoAction action = null;

            return pb =>
            {
                pb.SeparatorText("Target Object");

                pb.DropdownBox("Category", objectCategories, ref categoryIndex);
                objectFolderSelected = objectCategories[categoryIndex];
                if (categoryIndex != lastCategoryIndex) objectIndex = 0;

                // object id search box
                var objectDropdownItems = _targetObjects[objectFolderSelected].Select((str, index) =>
                    $"{Path.GetFileName(str)}").ToArray();
                var objectDropdownItemsFiltered = objectDropdownItems.ToArray();

                pb.Separator();

                pb.Label($"{IconFonts.ForkAwesome.Search} Object ID Search Options");
                pb.RadioButtons("search_mode", ["Substring", "Regular Expression"], ref searchMode, true);
                pb.CheckBox("Ignore Case", ref searchIgnoreCase);

                var (searchStr, _) =
                    pb.TextInput("template_search", hidePrompt: true, alwaysReturnString: true);
                if (searchStr != "")
                {
                    if (searchMode == 0)
                        objectDropdownItemsFiltered = objectDropdownItems.Where(info =>
                        {
                            var ttName = info;
                            if (searchIgnoreCase) ttName = ttName.ToLower();
                            return ttName.Contains(searchIgnoreCase ? searchStr.ToLower() : searchStr);
                        }).ToArray();
                }

                if (lastSearchStr != searchStr) objectIndex = 0;
                lastSearchStr = searchStr;

                // $"<I:{objectIndex}{index}>{Path.GetFileName(str)}").ToArray();
                pb.DropdownBox("Object ID", objectDropdownItemsFiltered, ref objectIndex);

                pb.Button($"{IconFonts.ForkAwesome.ChevronLeft} Previous");
                pb.SameLine(20);
                pb.Button($"Next {IconFonts.ForkAwesome.ChevronRight}");

                if (ViewAnnotatedResult)
                    pb.Label(AnnotationExists ? "Annotated Result Available" : "No Annotation Result");

                if (objectDropdownItemsFiltered.Length > 0)
                    ObjectIdStr = objectDropdownItemsFiltered[objectIndex];
                else ObjectIdStr = "";

                if (ObjectIdStr != "" && (categoryIndex != lastCategoryIndex || ObjectIdStr != lastObjectIdStr))
                {
                    if (objectDropdownItems.Length == 0)
                    {
                        WorkspaceProp.RemoveNamePattern("target:object");
                        glbDisplayed = false;
                    }
                    else
                    {
                        // convert .obj to .glb
                        try
                        {
                            var selectedList = _targetObjects.ContainsKey(objectFolderSelected)
                                ? _targetObjects[objectFolderSelected]
                                : new List<string>();
                            if (objectIndex >= 0 && objectIndex < selectedList.Count)
                            {
                                var objPath = selectedList.First(fn => Path.GetFileName(fn) == ObjectIdStr);
                                var glbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "target_object.glb");

                                using (Py.GIL())
                                {
                                    var py = $@"import sys
try:
    import trimesh
    import numpy as np
    from trimesh.visual import ColorVisuals
    from trimesh.visual.material import PBRMaterial
    from trimesh import repair
    import os
except Exception as e:
    raise RuntimeError('Python packages ''trimesh'' and ''numpy'' are required: ' + str(e))
path = r'{objPath}'
outp = r'{glbPath}'
obj = trimesh.load(path, file_type='obj')

# Helpers

def ensure_normals(geom):
    try:
        _ = geom.vertex_normals
    except Exception:
        pass

def repair_mesh(geom, merge_tol=1e-6):
    try:
        if hasattr(geom, 'process'):
            try:
                geom = geom.process()
            except Exception:
                pass
        try:
            geom.merge_vertices(merge_tol)
        except Exception:
            pass
        try:
            geom.remove_degenerate_faces()
        except Exception:
            pass
        try:
            geom.remove_duplicate_faces()
        except Exception:
            pass
        try:
            geom.remove_unreferenced_vertices()
        except Exception:
            pass
        try:
            repair.fix_inversion(geom)
        except Exception:
            pass
        try:
            repair.fix_winding(geom)
        except Exception:
            pass
        try:
            repair.fix_normals(geom)
        except Exception:
            pass
    except Exception:
        pass
    return geom

def sanitize_visuals_and_color(geom, rgba):
    geom = repair_mesh(geom)
    ensure_normals(geom)
    try:
        n = len(geom.vertices)
        if n > 0:
            colors = np.tile(rgba, (n, 1)).astype(np.uint8)
            geom.visual = ColorVisuals(geom, vertex_colors=colors)
            try:
                geom.visual.material = PBRMaterial(name='default', baseColorFactor=[1.0, 1.0, 1.0, 1.0])
            except Exception:
                pass
    except Exception:
        pass
    return geom

# Compute height (Z extent)

def compute_height(any_obj):
    try:
        if isinstance(any_obj, trimesh.Scene):
            mins = []
            maxs = []
            for geom in any_obj.geometry.values():
                try:
                    if geom.vertices is None or len(geom.vertices) == 0:
                        continue
                    bmin, bmax = geom.bounds
                    mins.append(bmin)
                    maxs.append(bmax)
                except Exception:
                    continue
            if len(mins) == 0:
                return 0.0
            bmin = np.min(np.vstack(mins), axis=0)
            bmax = np.max(np.vstack(maxs), axis=0)
            return float(bmax[2] - bmin[2])
        else:
            if hasattr(any_obj, 'bounds'):
                bmin, bmax = any_obj.bounds
                return float((bmax - bmin)[2])
            return 0.0
    except Exception:
        return 0.0

# Compute x,y,z extents

def compute_extents(any_obj):
    try:
        if isinstance(any_obj, trimesh.Scene):
            mins = []
            maxs = []
            for geom in any_obj.geometry.values():
                try:
                    if geom.vertices is None or len(geom.vertices) == 0:
                        continue
                    bmin, bmax = geom.bounds
                    mins.append(bmin)
                    maxs.append(bmax)
                except Exception:
                    continue
            if len(mins) == 0:
                return 0.0, 0.0, 0.0
            bmin = np.min(np.vstack(mins), axis=0)
            bmax = np.max(np.vstack(maxs), axis=0)
            ext = bmax - bmin
            return float(ext[0]), float(ext[1]), float(ext[2])
        else:
            if hasattr(any_obj, 'bounds'):
                bmin, bmax = any_obj.bounds
                ext = (bmax - bmin)
                return float(ext[0]), float(ext[1]), float(ext[2])
            return 0.0, 0.0, 0.0
    except Exception:
        return 0.0, 0.0, 0.0

# Desired uniform color: #FF6495ED -> RGBA(100,149,237,255)
rgba = np.array([100, 149, 237, 255], dtype=np.uint8)

# Process and apply to all meshes
if isinstance(obj, trimesh.Scene):
    for name, geom in list(obj.geometry.items()):
        geom = sanitize_visuals_and_color(geom, rgba)
        obj.geometry[name] = geom
    scene_or_mesh = obj
else:
    mesh = sanitize_visuals_and_color(obj, rgba)
    scene_or_mesh = mesh

# Write height to local file
try:
    dx, dy, dz = compute_extents(scene_or_mesh)
    height_path = os.path.join(os.getcwd(), 'target_object.height.txt')
    with open(height_path, 'w', encoding='utf-8') as f:
        # f.write(str(dx) + ',' + str(dy) + ',' + str(dz))
        f.write(str(dy))
except Exception:
    pass

# Export
scene_or_mesh.export(outp, file_type='glb')
";
                                    PythonEngine.RunSimpleString(py);
                                }

                                // Console.WriteLine($"Converted to GLB: {glbPath}");
                            }
                        }
                        catch (PythonException pex)
                        {
                            Console.WriteLine($"Python conversion failed: {pex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Conversion error: {ex.Message}");
                        }

                        // load and display .glb
                        if (File.Exists("target_object.glb"))
                        {
                            TargetObjectHeightBias = float.Parse(File.ReadAllText("target_object.height.txt"));
                            Workspace.AddProp(new LoadModel()
                            {
                                detail = new Workspace.ModelDetail(File.ReadAllBytes($"target_object.glb"))
                                {
                                    Center = new Vector3(0, 0, -TargetObjectHeightBias / 2f),
                                    Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2),
                                    Scale = 1f
                                },
                                name = "model_glb"
                            });

                            if (!glbDisplayed)
                            {
                                Workspace.AddProp(new PutModelObject()
                                {
                                    clsName = "model_glb",
                                    name = "target:object",
                                    // newPosition = new Vector3(0, 0, height / 2f),
                                });
                                glbDisplayed = true;
                            }
                        }
                    }
                }

                lastCategoryIndex = categoryIndex;
                lastObjectIdStr = ObjectIdStr;

                if (!initSelectable && Program.DefaultAction != null)
                {
                    // Program.DefaultAction.SetObjectSelectable(targetObject.Name);
                    // initSelectable = true;
                }

                pb.SeparatorText("Template Instances");

                var tObjects = TemplateObjects.ToList();
                pb.Table("template_objects_table", ["Template Name", "GUID", "Actions"], tObjects.Count, (row, i) =>
                {
                    var selectChange = row.Label(tObjects[i].TemplateName);
                    if (row.Label(tObjects[i].Guid.ToString())) selectChange = true;

                    var btId = row.ButtonGroup([IconFonts.ForkAwesome.Arrows, IconFonts.ForkAwesome.Trash], ["Move", "Delete"]);
                    if (btId == 0)
                    {
                        action?.End();
                        Program.DefaultAction.SetSelection([]);

                        selectChange = true;
                        Program.DefaultAction.SetSelection(new[] { tObjects[i].Name });
                        action = new GuizmoAction()
                        {
                            realtimeResult = true,//realtime,
                            finished = () =>
                            {
                                Console.WriteLine("OKOK...");
                                Program.DefaultAction.SetSelection([]);
                                // IssueCrossSection();
                            },
                            terminated = () =>
                            {
                                Console.WriteLine("Forget it...");
                                Program.DefaultAction.SetSelection([]);
                                // IssueCrossSection();
                            },
                        };
                        action.feedback = (valueTuples, _) =>
                        {
                            var name = valueTuples[0].name;
                            var render = TemplateObjects.First(geo => geo.Name == name);
                            render.Pos = valueTuples[0].pos with
                            {
                                Z = valueTuples[0].pos.Z - TargetObjectHeightBias / 2f
                            };
                            render.Rot = valueTuples[0].rot;
                        };
                        action.Start();
                    }
                    else if (btId == 1)
                    {
                        WorkspaceProp.RemoveNamePattern(tObjects[i].Name);
                        TemplateObjects.Remove(tObjects[i]);
                        SelectedTemplate = null;
                    }

                    if (selectChange)
                    {
                        SelectedTemplate = tObjects[i];
                        ToggleTransparency();
                    }
                });

                pb.Panel.Repaint();
            };
        }

        private static Dictionary<string, List<string>> _targetObjects = new();
    }
}
