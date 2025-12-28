using System.Drawing;
using System.Numerics;
using System.Reflection;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;

namespace GltfViewer
{
    internal static class Program
    {
        // Lenticular parameters
        private static float dragSpeed = -6f;
        private static Color leftC = Color.Red, rightC = Color.Blue;
        
        private static float prior_row_increment = 0.183528f;
        private static float _priorBiasLeft = 0;
        private static float _priorBiasRight = 0;
        private static float _priorPeriod = 5.32f;
        
        private static float prior_bias_left
        {
            get => _priorBiasLeft;
            set { _priorBiasLeft = value; edited_bl = true; }
        }
        
        private static float prior_bias_right
        {
            get => _priorBiasRight;
            set { _priorBiasRight = value; edited_br = true; }
        }
        
        private static float prior_period
        {
            get => _priorPeriod;
            set { _priorPeriod = value; edited_p = true; }
        }
        
        private static bool edited_bl, edited_br, edited_p;
        private static float period_fill = 1;
        
        // RGB subpixel offsets
        private static Vector2 subpx_R = new Vector2(0.0f, 0.0f);
        private static Vector2 subpx_G = new Vector2(1.0f / 3.0f, 0.0f);
        private static Vector2 subpx_B = new Vector2(2.0f / 3.0f, 0.0f);
        
        // Display mode and stripe
        private static bool stripe = false;
        private static int disp_type = 0;

        static unsafe void Main(string[] args)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));

            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("HoloTest");
            LocalTerminal.Start();

            new SetCamera() { displayMode = SetCamera.DisplayMode.EyeTrackedLenticular }.IssueToDefault();

            var init = true;
            
            GUI.PromptPanel(pb =>
            {
                pb.Panel.ShowTitle("Lenticular Tuner");
                
                // Adjust speed control
                pb.DragFloat("Adjust Speed", ref dragSpeed, 0.1f, -15.0f, 0.0f);
                pb.Label($"Current Speed: {Math.Exp(dragSpeed):F6}");

                var speed = (float)Math.Exp(dragSpeed);
                
                // Fill color controls
                var paramsChanged = pb.ColorEdit("Left Color", ref leftC);
                paramsChanged |= pb.ColorEdit("Right Color", ref rightC);

                // Lenticular parameter controls
                paramsChanged |= pb.DragFloat("Period Fill", ref period_fill, speed, 0, 100);
                paramsChanged |= pb.DragFloat("Period Total", ref _priorPeriod, speed, 0, 100, edited_p);
                paramsChanged |= pb.DragFloat("Phase Init Left", ref _priorBiasLeft, speed * 100, -100, 100, edited_bl);
                paramsChanged |= pb.DragFloat("Phase Init Right", ref _priorBiasRight, speed * 100, -100, 100, edited_br);
                paramsChanged |= pb.DragFloat("Row Increment", ref prior_row_increment, speed, -100, 100);

                edited_p = edited_bl = edited_br = false;

                // RGB Subpixel Location controls
                pb.SeparatorText("RGB Subpixel Offsets");
                paramsChanged |= pb.DragVector2("Subpixel R Offset", ref subpx_R, speed, -5, 5);
                paramsChanged |= pb.DragVector2("Subpixel G Offset", ref subpx_G, speed, -5, 5);
                paramsChanged |= pb.DragVector2("Subpixel B Offset", ref subpx_B, speed, -5, 5);

                // Display options
                pb.SeparatorText("Display Options");
                if (pb.CheckBox("Stripe", ref stripe))
                    new SetLenticularParams() { stripe = stripe }.IssueToTerminal(GUI.localTerminal);

                if (pb.RadioButtons("Display Mode", ["Calibration", "View"], ref disp_type))
                    new SetLenticularParams() { mode = (SetLenticularParams.Mode)disp_type }.IssueToTerminal(GUI.localTerminal);

                // Apply parameters when changed
                if (paramsChanged || init)
                {
                    new SetLenticularParams()
                    {
                        left_fill = leftC,
                        right_fill = rightC,
                        period_fill_left = period_fill,
                        period_fill_right = period_fill,
                        period_total_left = prior_period,
                        period_total_right = prior_period,
                        phase_init_left = prior_bias_left,
                        phase_init_right = prior_bias_right,
                        phase_init_row_increment_left = prior_row_increment,
                        phase_init_row_increment_right = prior_row_increment,
                        subpx_R = subpx_R,
                        subpx_G = subpx_G,
                        subpx_B = subpx_B,
                    }.IssueToTerminal(GUI.localTerminal);

                    init = false;
                }
            });
        }
    }
}
