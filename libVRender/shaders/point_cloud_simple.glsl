@ctype mat4 glm::mat4

@vs point_cloud_vs
uniform vs_params {
    mat4 mvp;
};

in vec3 position;
in float size;
in vec4 color;

out vec4 v_Color;
void main() {
	gl_Position = mvp * vec4(position, 1.0);
	gl_PointSize = size;
	v_Color = color;
}
@end

@fs point_cloud_fs
in vec4 v_Color;
out vec4 frag_color;
void main() {
	frag_color = v_Color;
}
@end

@program point_cloud_simple point_cloud_vs point_cloud_fs