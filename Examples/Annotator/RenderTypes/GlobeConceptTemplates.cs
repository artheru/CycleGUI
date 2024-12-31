using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;

namespace Annotator.RenderTypes
{
    internal class GlobeConceptTemplates : BasicRender
    {
        public Dictionary<string, float[]> Parameters;
        private Dictionary<string, float[]> _lastParameters;
        public string TemplateName;


        public GlobeConceptTemplates(string templateName, Dictionary<string, float[]> paramDict, float r, float g, float b)
            : base(templateName, r, g, b)
        {
            this.TemplateName = templateName; // Use the template name directly
            this.Parameters = CloneParams(paramDict);
            _lastParameters = CloneParams(paramDict);
        }

        public override bool NeedsUpdate()
        {
            // Check if any parameter changed
            foreach (var kv in Parameters)
            {
                if (!_lastParameters.ContainsKey(kv.Key)) return true;
                if (kv.Value.Length != _lastParameters[kv.Key].Length) return true;
                for (int i = 0; i < kv.Value.Length; i++)
                    if (kv.Value[i] != _lastParameters[kv.Key][i]) return true;
            }
            return false;
        }

        public override void Update()
        {
            _lastParameters = CloneParams(Parameters);
        }

        public override void ParameterChangeAction(PanelBuilder pb)
        {
            foreach (var kv in Parameters)
            {
                var key = kv.Key;

                // Skip keys that are related to position or rotation
                if (key.Contains("position", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("rotation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var arr = kv.Value;
                for (int i = 0; i < arr.Length; i++)
                {
                    float val = arr[i];

                    // Identify keys that require integer values
                    bool requiresInteger = key.Contains("num", StringComparison.OrdinalIgnoreCase);
                                           //key.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                                           //key.Contains("subs", StringComparison.OrdinalIgnoreCase);

                    // Determine the step size and minimum value
                    float step = requiresInteger ? 1.0f : 0.01f;
                    float minValue = requiresInteger ? 0.0f : float.MinValue; // Minimum for integers is 0

                    // Use DragFloat with the appropriate step size and minimum value
                    if (pb.DragFloat($"{Name} {key}[{i}]", ref val, step, minValue))
                    {
                        Console.WriteLine($"Updating parameter {Name} {key}[{i}] from {arr[i]} to {val}");

                        // If integer is required, round the value
                        arr[i] = requiresInteger ? (float)Math.Round(val) : val;
                    }
                }
            }
        }



        private Dictionary<string, float[]> CloneParams(Dictionary<string, float[]> input)
        {
            var dict = new Dictionary<string, float[]>();
            foreach (var kv in input)
            {
                dict[kv.Key] = (float[])kv.Value.Clone(); // Deep copy
            }
            return dict;
        }

    }
}
