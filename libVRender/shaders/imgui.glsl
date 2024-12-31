@ctype mat4 glm::mat4
@ctype vec2 glm::vec2

@vs imgui_vs
uniform imgui_vs_params {
    mat4 ProjMtx;
};

in vec2 Position;
in vec2 TexCoord0;
in vec4 Color0;

out vec2 Frag_UV;
out vec4 Frag_Color;

void main() {
    Frag_UV = TexCoord0;
    Frag_Color = Color0;
    gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
    gl_Position.z = 0;
}
@end

@fs imgui_fs
uniform sampler2D Texture;

in vec2 Frag_UV;
in vec4 Frag_Color;
out vec4 Out_Color;

void main() {
    Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
}
@end

@program imgui imgui_vs imgui_fs