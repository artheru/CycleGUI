// shaders for rendering a debug visualization
@vs vs_dbg

in vec2 pos;
out vec2 uv;

void main() {
    gl_Position = vec4(pos*2.0-1.0, 0.5, 1.0);
    uv = pos;
}
@end

@fs fs_dbg
uniform sampler2D tex;

in vec2 uv;
out vec4 frag_color;

void main() {
    frag_color = vec4(texture(tex,uv).rgb,1.0);
}
@end

@program dbg vs_dbg fs_dbg


@fs fs_blend
uniform sampler2D tex;

in vec2 uv;
out vec4 frag_color;

void main() {
    frag_color = vec4(texture(tex, uv).rgba);
}
@end
@program blend_to_screen vs_dbg fs_blend