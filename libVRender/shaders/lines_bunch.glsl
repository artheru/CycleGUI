@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

////////////////// RENDER
@vs line_bunch_vs
uniform line_bunch_params{
    mat4 mvp;
	float dpi;
    int bunch_id;
	float screenW;
	float screenH;

	int displaying; // per cloud.
	// int hovering_pcid; // if sub selectable, which pcid is hovering?
	vec4 shine_color_intensity;
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity
};

in vec3 start;
in vec3 end;
in vec4 meta;
in vec4 color; 

out vec4 v_Color;
flat out int bid;
out float dashing;

void main() {

	vec4 startclip = mvp * vec4(start, 1.0);
	vec2 startVec2 = startclip.xy / startclip.w;
	vec4 endclip = mvp * vec4(end, 1.0);
	vec2 dir = normalize(endclip.xy/endclip.w - startVec2);
	dir = normalize(vec2(dir.x * screenW, dir.y * screenH));
	vec2 nn = vec2(-dir.y, dir.x);

	v_Color = color / 255.0f; // Pass color to fragment shader, fuck it.

	vec3 arr[2];
	arr[0] = start;
	arr[1] = end;
	vec2 offset;

	float arrow = meta.x * 255;
	float dash = meta.y * 0.1;
	float width = meta.z;
	float flags = meta.w; // flags: border, front, selected, selectable, 

	gl_Position = vec4(0);
	float facW = (width + 8) / width;
	float facDir = 1.8;
	if (gl_VertexIndex < 6) { // First 6 vertices for the line
		int idx = gl_VertexIndex % 2;       
		vec3 linePos = idx == 0 ? start : end;    
		gl_Position = mvp * vec4(linePos, 1.0);
		offset = (gl_VertexIndex < 2 || gl_VertexIndex == 3) ? nn : -nn;    
		if (arrow > 0) {
			if (idx != 0)
				offset -= dir * facDir * facW;
		}

		dashing = dash == 0 ? 0 : length(gl_Position.xy / gl_Position.w - startVec2) / dash;
	}
	else if (arrow > 0) {
		dashing = 0;
		// Arrow dimensions

		float arrowLength = width * 2.0;
		vec3 arrowDir = arrow == 1.0 ? normalize(start - end) : normalize(end - start);
		// Arrow vertices
		vec3 arrowBase = arrow == 1.0 ? start : end;

		gl_Position = mvp * vec4(arrowBase, 1.0);

		if (gl_VertexIndex == 6) {
			offset = vec2(0);
		}
		else if (gl_VertexIndex == 7) {
			offset = nn - dir * facDir;
		}
		else { // gl_VertexIndex == 8
			offset = -nn - dir * facDir;
		}
		offset *= (width + 8) / width;
	}
	gl_Position.x += offset.x * width / screenW * gl_Position.w;
	gl_Position.y += offset.y * width / screenH * gl_Position.w;
	bid = gl_InstanceIndex;
}
@end

@fs line_bunch_fs

uniform line_bunch_params {
    mat4 mvp;
	float dpi;
    int bunch_id;
	float screenW;
	float screenH;

	int displaying; // per bunch. 0:border? 1:shine? 2:front? 3: selected as whole?
	// int hovering_pcid; // if sub selectable, which pcid is hovering?
	vec4 shine_color_intensity;
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity
};

in vec4 v_Color;
flat in int bid;
in float dashing;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth;
layout(location=2) out vec4 screen_id;

layout(location = 3) out float bordering;
layout(location = 4) out vec4 bloom;

void main() {
	//frag_color = v_Color;
    g_depth = -gl_FragCoord.z; // lines don't participate in postprocessing.
	screen_id = vec4(2, bunch_id, bid, 0);
	
	
	bool sel = (displaying & (1<<3))!=0;
	bool hovering = (displaying & (1<<4))!=0;

	// border or hovering or selected.
	if ((displaying & 1) !=0)
		bordering = 3;
	else if (sel)
		bordering = 2;
	else if (hovering)
		bordering = 1;
	else 
		bordering = 0;
	bordering /= 16;
	
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

	if (fract(dashing) > 0.5) {
		frag_color = vec4(0);
	}
}
@end

@program linebunch line_bunch_vs line_bunch_fs
