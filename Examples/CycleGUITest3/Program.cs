using System.Numerics;
using System.Reflection;
using CycleGUI;
using CycleGUI.API;
using CycleGUI.Terminals;

namespace GltfViewer
{
    internal static class Program
    {
        static unsafe void Main(string[] args)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly()
                .GetManifestResourceNames().First(p => p.Contains(".ico")));

            var icoBytes = new BinaryReader(stream).ReadBytes((int)stream.Length);
            LocalTerminal.SetIcon(icoBytes, "TEST");
            LocalTerminal.AddMenuItem("Exit", LocalTerminal.Terminate);
            LocalTerminal.SetTitle("Medulla");
            LocalTerminal.Start();

            new SetAppearance(){drawGroundGrid = false, drawGuizmo = false}.IssueToDefault();

            GUI.PromptPanel(pb =>
            {
                if (pb.Button("Load Tunnel"))
                {
                    Workspace.SetCustomBackgroundShader(@"
// low tech tunnel
// 28 steps


#define T (iTime*4.+5.+sin(iTime*.3)*5.)
#define N normalize
#define P(z) vec3(cos((z)*.1)* 12., \
                  cos((z) *  .12) * 12., (z))
#define A(F, H, K) abs(dot(sin(F*p*K), H+p-p )) / K 

void mainImage(out vec4 o, in vec2 u) {
   
    float st,s,i,d,e;
    vec3  c,r = iResolution;
    
    // scaled coords
    // u = (u-r.xy/2.)/r.y;
 
    // setup ray origin, direction, and look-at
    vec2 uv = u / r.xy;
    vec4 clip = vec4(uv * 2.0 - 1.0, 1.0, 1.0);
    vec4 view = iInvPM * clip;
    view /= view.w;
    vec3 world_dir = normalize((iInvVM * view).xyz - iCameraPos); // Direction in 'Cockpit' space

    // Reconstruct Tunnel Basis
    vec3 p_center = P(T);
    vec3 Z = N(P(T+4.) - p_center);
    // Gram-Schmidt-ish process to find Up/Right relative to path
    // Original shader used: X = N(vec3(Z.z, 0, -Z.x)); which is a vector in XZ plane orthogonal to Z
    vec3 X = N(vec3(Z.z, 0., -Z.x)); 
    vec3 Y = cross(X, Z);
    
    // Basis matrix: Columns are Right, Up, Forward (or whatever the original mapping was)
    // Original: u.x * (-X) + u.y * cross(X,Z) + 1.0 * Z
    // So X_axis = -X, Y_axis = Y, Z_axis = Z
    mat3 tunnel_rot = mat3(-X, Y, Z);
    
    // Transform Camera Position and Direction from Cockpit to World
    vec3 ro = p_center + tunnel_rot * iCameraPos;
    vec3 D = tunnel_rot * world_dir;
    
    vec3 p = ro;
              
    for(;i++ < 28. && d < 3e1;
        c += 1./s + 1e1*vec3(1,2,5)/max(e, .6)
    )
        // march
        p = ro + D * d,
        
        // get path
        X = P(p.z),
        
        // store sine of iTime (not T)
        st = sin(iTime),
        
        // orb (sphere with xyz offset by st)
        e = length(p - vec3(
                    X.x + st,
                    X.y + st*2.,
                    6.+T + st*2.))-.01,
           
        // tunnel with modulating radius
        s = cos(p.z*.6)*2.+ 4. - 
            min(length(p.xy - X.x - 6.),
                length(p.xy - X.xy)),


        // noise, large scoops
        s += A(4., .25, .1),
        
        // noise, detail texture
        // remove ""T+"" if you don't like the texture moving
        s += A(T+8., .22, 2.),

        // accumulate distance
        d += s = min(e,.01+.3*abs(s));
        
    // adjust brightness and saturation,
    o.rgb = (c*c/1e6);
    o.a = 1.0;
}
");
                }

            });
        }
    }
}