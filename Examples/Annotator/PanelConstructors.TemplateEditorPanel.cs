using CycleGUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Annotator.RenderTypes;
using Python.Runtime;
using CycleGUI.API;

namespace Annotator
{
    internal partial class PanelConstructors
    {
        public static PanelBuilder.CycleGUIHandler DefineTemplateEditorPanel()
        {
            int dragStepId = 0;

            return pb =>
            {
                // pb.CheckBox("Kiosk Mode", ref KioskMode);

                ConceptTemplate templateObject = null;

                lock (TemplateObjects)
                {
                    if (SelectedTemplate == null)
                    {
                        pb.Label("No template object selected yet.");
                        pb.Panel.Repaint();
                        return;
                    }
                    templateObject = SelectedTemplate;
                }

                pb.Label(templateObject.TemplateName);
                pb.Label(templateObject.Guid.ToString());

                var needsUpdateMesh = false;
                var updatedParams = new List<(string ParamName, PanelConstructors.ParameterInfo ParamInfo, List<double> ParamVal)>();
                foreach (var paramInfo in templateObject.Parameters)
                {
                    var key = paramInfo.ParamName;
                    var arr = paramInfo.ParamVal.ToList();
                    var isFloat = paramInfo.ParamInfo.IsFloat;
                    var min = paramInfo.ParamInfo.Min;
                    var max = paramInfo.ParamInfo.Max;
                    var valType = isFloat ? "Floating" : "Integer";

                    pb.Separator();
                    pb.Label(isFloat ? $"{key} {valType}" : $"{key} {valType} range: {min} ~ {max}");

                    for (int i = 0; i < arr.Count; i++)
                    {
                        var fVal = (float)arr[i];
                        if (pb.DragFloat($"{key}[{i}]", ref fVal, isFloat ? 0.001f : 1, isFloat ? float.MinValue : min,
                                isFloat ? float.MaxValue : max)) needsUpdateMesh = true;
                        arr[i] = fVal;
                    }

                    updatedParams.Add((key, paramInfo.ParamInfo, arr));
                }

                pb.Separator();
                pb.Label("position");
                float px = templateObject.Pos.X, py = templateObject.Pos.Y, pz = templateObject.Pos.Z;
                var posChanged = pb.DragFloat("position[0]", ref py, 0.001f);
                posChanged |= pb.DragFloat("position[1]", ref pz, 0.001f);
                posChanged |= pb.DragFloat("position[2]", ref px, 0.001f);
                templateObject.Pos = new Vector3(px, py, pz);
                if (posChanged)
                {
                    Workspace.AddProp(new PutModelObject()
                    {
                        clsName = $"{templateObject.Name}-cls",
                        name = templateObject.Name,
                        newPosition = templateObject.Pos with
                        {
                            Z = templateObject.Pos.Z + TargetObjectHeightBias / 2
                        },
                        newQuaternion = templateObject.Rot
                    });
                }

                pb.Separator();
                pb.Label("rotation (radians)");
                // rx, ry, rz are intrinsic rotations about X, Y, Z respectively (XYZ order, q = Rz * Ry * Rx)
                float rx, ry, rz;
                (rx, ry, rz) = QuaternionToEulerXYZ(templateObject.Rot);
                rx = (float)(rx / Math.PI * 180f);
                ry = (float)(ry / Math.PI * 180f);
                rz = (float)(rz / Math.PI * 180f);
                var rotChanged = pb.DragFloat("rotation[0]", ref ry, 0.01f);
                rotChanged |= pb.DragFloat("rotation[1]", ref rz, 0.01f);
                rotChanged |= pb.DragFloat("rotation[2]", ref rx, 0.01f);
                // Compose quaternion with XYZ order: q = Rz * Ry * Rx
                rx = (float)(rx / 180f * Math.PI);
                ry = (float)(ry / 180f * Math.PI);
                rz = (float)(rz / 180f * Math.PI);
                var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, rx);
                var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ry);
                var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rz);
                templateObject.Rot = qz * qy * qx;
                if (rotChanged)
                {
                    Workspace.AddProp(new PutModelObject()
                    {
                        clsName = $"{templateObject.Name}-cls",
                        name = templateObject.Name,
                        newPosition = templateObject.Pos with
                        {
                            Z = templateObject.Pos.Z + TargetObjectHeightBias / 2
                        },
                        newQuaternion = templateObject.Rot
                    });
                }

                if (needsUpdateMesh)
                {
                    templateObject.UpdateParamsAndMesh(updatedParams);
                    MeshUpdater.UpdateMesh(templateObject, false);
                }

                pb.Panel.Repaint();
            };
        }
    }
}
