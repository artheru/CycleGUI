using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CycleGUI;
using CycleGUI.API;

namespace Annotator.RenderTypes
{
    public class GlobeConceptTemplates : BasicRender
    {
        public Dictionary<string, float[]> Parameters;
        private Dictionary<string, float[]> _lastParameters;
        public string TemplateName;
        public bool minZ = false;
        public bool isHighlighted = false;
        public bool NeedsUpdateFlag = false;
        public bool IsTransparent = false;

        public GlobeConceptTemplates(string name, string templateName, Dictionary<string, float[]> paramDict, float r, float g, float b)
            : base(name, r, g, b)
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
            if (isHighlighted)
            {
                isHighlighted = false;

                // Restore normal appearance
                //new SetAppearance()
                //{
                //   useGround = false,
                //}.IssueToAllTerminals();
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
