@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

////////////////// RENDER
@vs point_cloud_vs
uniform vs_params {
    mat4 mvp;
	float dpi;
    int pc_id;

	int displaying; // per cloud.
	int hovering_pcid; // if sub selectable, which pcid is hovering?
	vec4 shine_color_intensity;
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity
};

in vec3 position;
in float size;
in vec4 color; 

out vec4 v_Color;
flat out vec4 vid;

flat out int vertex;

flat out int selc;

void main() {
	vec4 mvpp = mvp * vec4(position, 1.0);
	gl_Position = mvpp;
	gl_PointSize = clamp(size / sqrt(mvpp.z) *3*dpi+2, 2, 32*dpi);
	v_Color = color;
    vid = vec4(1,pc_id,gl_VertexIndex / 16777216,gl_VertexIndex % 16777216);
	
	//"move to front" displaying paramter processing.
	if ((displaying & 4) != 0){
        gl_Position.z -= gl_Position.w;
	}
	vertex = gl_VertexIndex;

}
@end

@fs point_cloud_fs

uniform vs_params {
    mat4 mvp;
	float dpi;
    int pc_id;

	int displaying; // per cloud. 0:borner? 1:shine? 2:front? 3: selected as whole? 4: hovering as whole? 5: sub selectable?
	int hovering_pcid; // if sub selectable, which pcid is hovering?
	vec4 shine_color_intensity;
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity
};

uniform usampler2D selected;

in vec4 v_Color;
flat in vec4 vid;
flat in int vertex;

flat in int selc;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth;
layout(location=2) out float pc_depth;
layout(location=3) out vec4 screen_id;

layout(location=4) out float bordering;
layout(location=5) out vec4 bloom;

void main() {
	//frag_color = v_Color;
    g_depth=pc_depth = gl_FragCoord.z;
    screen_id = vid;
	

	int sz= textureSize(selected,0).r;
	int cd = vertex / 8;
	int cc = vertex % 8;
	int selc=int(texelFetch(selected, ivec2(cd%sz.x,cd/sz.x),0).r);
	
	bool sel = ((displaying & (1<<3))!=0) || ((displaying & (1<<5))!=0) && (selc & (1<<cc))!=0;
	bool hovering = (displaying & (1<<4))!=0 || ((displaying & (1<<5))!=0) && hovering_pcid == vertex;

	// border or hovering or selected.
	if ((displaying & 1) !=0)
		bordering = 3;
	else if (sel)
		bordering = 2;
	else if (hovering)
		bordering = 1;
	else 
		bordering = 0;
	bordering /= 255;
	
	vec4 shine = vec4(0);
	if ((displaying & 2) !=0){ //self shine
		shine = shine_color_intensity;
	}
	if (sel){ //selected shine
		shine += selected_shine_color_intensity;
	}
	if (hovering){ //hover shine
		shine += hover_shine_color_intensity;
	}
	shine = min(shine, vec4(1));

	frag_color = vec4(v_Color.xyz * (1-shine.w*0.3) + shine.xyz*shine.w * 0.5, v_Color.w);
	bloom = vec4((frag_color.xyz+0.2)*(1+shine.xyz*shine.w) - 0.9, 1);
}
@end

@program point_cloud_simple point_cloud_vs point_cloud_fs
