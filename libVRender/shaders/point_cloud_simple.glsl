@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
/////////////////// SHADOW MAP
@vs point_cloud_vs_shadow
uniform vs_params {
    mat4 mvp;
	float dpi;
    int pc_id;
};

in vec3 position;
in float size;

void main() {
	vec4 mvpp = mvp * vec4(position, 1.0);
	gl_Position = mvpp;
	gl_PointSize = clamp(size / sqrt(mvpp.z) *3*dpi+2, 2, 32*dpi);
}
@end

@fs point_cloud_fs_onlyDepth
layout(location=0) out float g_depth;
void main() {
    g_depth = gl_FragCoord.z;
}
@end


@program pc_depth_only point_cloud_vs_shadow point_cloud_fs_onlyDepth

////////////////// RENDER
@vs point_cloud_vs
uniform vs_params {
    mat4 mvp;
	float dpi;
    int pc_id;
};

in vec3 position;
in float size;
in vec4 color; 

out vec4 v_Color;
flat out vec4 vid;

void main() {
	vec4 mvpp = mvp * vec4(position, 1.0);
	gl_Position = mvpp;
	gl_PointSize = clamp(size / sqrt(mvpp.z) *3*dpi+2, 2, 32*dpi);
	v_Color = color;
    vid = vec4(1,pc_id,gl_VertexIndex / 16777216,gl_VertexIndex % 16777216);
}
@end

@fs point_cloud_fs
in vec4 v_Color;
flat in vec4 vid;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth;
layout(location=2) out float pc_depth;
layout(location=3) out vec4 screen_id;

void main() {
	frag_color = v_Color;
    g_depth=pc_depth = gl_FragCoord.z;
    screen_id = vid;
}
@end

@program point_cloud_simple point_cloud_vs point_cloud_fs
