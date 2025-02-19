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
        public bool ShowInTempViewport = true;
        public bool ShowInDefaultViewport = true;
        public bool Resume;
        public bool PanelShown = false;
        public bool minZ = false;


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
                var arr = kv.Value;

                // Skip keys that are related to position or rotation
                if (key.Contains("position", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("rotation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (int i = 0; i < arr.Length; i++)
                {
                    float val = arr[i];

                    // Check if the key starts with "is_"
                    if (key.StartsWith("is_", StringComparison.OrdinalIgnoreCase))
                    {
                        // Treat the value as a boolean (0 or 1)
                        bool toggleValue = Math.Abs(val) > 0.5f; // Convert 0/1 to false/true

                        if (pb.Toggle($"{key}[{i}]", ref toggleValue))
                        {
                            Console.WriteLine($"Updating parameter {Name} {key}[{i}] from {val} to {(toggleValue ? 1.0f : 0.0f)}");

                            // Update the array with the toggle value (convert back to float)
                            arr[i] = toggleValue ? 1.0f : 0.0f;
                        }
                    }
                    else
                    {
                        // Identify keys that require integer values
                        bool requiresInteger = key.Contains("num", StringComparison.OrdinalIgnoreCase);

                        // Determine the step size and minimum value
                        float step = requiresInteger ? 1.0f : 0.01f;
                        float minValue = requiresInteger ? 0.0f : float.MinValue; // Minimum for integers is 0
                        float maxValue = float.MaxValue;
                        // Special constraints for "num_levels" and "number_of_legs"
                        if (key.Equals("num_levels", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("number_of_legs", StringComparison.OrdinalIgnoreCase))
                        {
                            minValue = 1.0f;
                            maxValue = 4.0f;
                        }
                        // Use DragFloat with the appropriate step size and value constraints
                        if (pb.DragFloat($"{key}[{i}]", ref val, step, minValue, maxValue))
                        {
                            // Ensure constraints are applied correctly
                            val = requiresInteger ? (float)Math.Round(val) : val;

                            // Apply clamping for strict value constraints
                            if (key.Equals("num_levels", StringComparison.OrdinalIgnoreCase) ||
                                key.Equals("number_of_legs", StringComparison.OrdinalIgnoreCase))
                            {
                                val = Math.Clamp(val, 1.0f, 4.0f);
                            }

                            Console.WriteLine($"Updating parameter {Name} {key}[{i}] from {arr[i]} to {val}");

                            arr[i] = val;
                        }
                        //// Use DragFloat with the appropriate step size and minimum value
                        //if (pb.DragFloat($"{key}[{i}]", ref val, step, minValue))
                        //{
                        //    Console.WriteLine($"Updating parameter {Name} {key}[{i}] from {arr[i]} to {val}");

                        //    // If integer is required, round the value
                        //    arr[i] = requiresInteger ? (float)Math.Round(val) : val;
                        //}
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
