@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec4 glm::vec4

@vs image_vs
uniform vs_image_params {
    mat4 mvp;
    float aspect;//width / height
};

vec2 positions[6] = vec2[](
    vec2(1.0, -1.0),
    vec2(1.0, 1.0),
    vec2(-1.0, 1.0),
    vec2(1.0, -1.0),
    vec2(-1.0, -1.0),
    vec2(-1.0, 1.0)
);

in vec2 texcoord0;
out vec2 uv;

void main() {
    gl_Position = mvp * vec4(positions[gl_VertexIndex],0.0,1.0);
    uv = texcoord0;
}
@end

@fs image_fs
uniform sampler2D tex;

in vec2 uv;
out vec4 frag_color;

void main() {
    frag_color = texture(tex, uv);
}
@end

@program loadimage image_vs image_fs