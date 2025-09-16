using CycleGUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Annotator.RenderTypes;
using Python.Runtime;

namespace Annotator
{
    internal partial class PanelConstructors
    {
        public static PanelBuilder.CycleGUIHandler DefineTemplateEditorPanel()
        {
            int dragStepId = 0;

            return pb =>
            {
                pb.CheckBox("Kiosk Mode", ref KioskMode);

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

                pb.Label($"{templateObject.Name}");
                // pb.Label("Drag Step");
                // pb.SameLine(25);
                // pb.RadioButtons("Drag step", ["0.01", "0.001"], ref dragStepId, true);
                // float step = dragStepId == 0 ? 0.01f : 0.001f;
                if (pb.CheckBox("All transparent but editing", ref allTransparentButEditing))
                {
                    ToggleTransparency();
                }

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
