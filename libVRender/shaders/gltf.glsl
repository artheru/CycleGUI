@ctype mat4 glm::mat4

@vs vs
// per instance.
in mat4 mvp;

in vec3 position;
in vec4 color0;

out vec4 color;

void main() {
    gl_Position = mvp * vec4(position, 1.0);
    color = color0;
}
@end

@fs fs
in vec4 color;
out vec4 frag_color;

void main() {
    frag_color = color*0.1+vec4(1.0);
}
@end

@program gltf vs fs
