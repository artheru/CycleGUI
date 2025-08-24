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

	int displaying; // flags: border, shine, front, selected, hover.
	vec4 shine_color_intensity;
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity
};

in vec3 start;
in vec3 end;
in vec4 meta;
in vec4 color; 
in float inid;

out vec4 v_Color;
flat out int lid;
out float dashing;
flat out int pflag;

void main() {

	vec4 startclip = mvp * vec4(start, 1.0);
	vec2 startVec2 = startclip.xy / startclip.w;
	vec4 endclip = mvp * vec4(end, 1.0);
	
	float arrow = meta.x;
	float dash = meta.y * 0.1;
	float width = meta.z;
	float flags = meta.w; // flags: border, shine, front, selected, hover
	
	// Vector mode: fixed screen-space length while maintaining perspective
	// if (arrow > 2.0) {
	// 	// Calculate desired screen-space length (you can adjust this value)
	// 	float fixedScreenLength = arrow * dpi; // pixels
	// 	
	// 	// Get direction in world space
	// 	vec3 worldDir = normalize(end - start);
	// 	
	// 	// Calculate how much world-space distance corresponds to the fixed screen length
	// 	// at the start point's depth
	// 	float startDepth = startclip.w;
	// 	float worldLengthAtStartDepth = fixedScreenLength * startDepth / (screenW * 0.5);
	// 	
	// 	// Adjust end position to maintain fixed screen length
	// 	vec3 adjustedEnd = start + worldDir * worldLengthAtStartDepth;
	// 	endclip = mvp * vec4(adjustedEnd, 1.0);
	// }
	
	vec2 dir = normalize(endclip.xy/endclip.w - startVec2);
	dir = normalize(vec2(dir.x * screenW, dir.y * screenH));
	vec2 nn = vec2(-dir.y, dir.x);

	v_Color = color / 255.0f; // Pass color to fragment shader, fuck it.

	vec3 arr[2];
	arr[0] = start;
	arr[1] = end;
	vec2 offset;

	pflag = displaying | int(flags);

	gl_Position = vec4(0);
	float facW = (width + 8) / width;
	float facDir = 1.8;
	if (gl_VertexIndex < 6) { // First 6 vertices for the line
		int idx = gl_VertexIndex % 2;       
		vec3 linePos = idx == 0 ? start : end;
		
		// For vector mode, use adjusted end position
		if (arrow > 2.0 && idx == 1) {
			vec3 worldDir = normalize(end - start);
			float startDepth = (mvp * vec4(start, 1.0)).w;
			float fixedScreenLength = arrow * dpi; // same as above
			float worldLengthAtStartDepth = fixedScreenLength * startDepth / (screenW * 0.5);
			linePos = start + worldDir * worldLengthAtStartDepth;
		}
		
		gl_Position = mvp * vec4(linePos, 1.0);
		offset = (gl_VertexIndex < 2 || gl_VertexIndex == 3) ? nn : -nn;    
		if (arrow > 1.0 && idx != 0){
			offset -= dir * facDir * facW;
		}
		if (arrow == 1.0 && idx == 0){
			offset += dir * facDir * facW;
		}

		dashing = dash == 0 ? 0 : length(gl_Position.xy / gl_Position.w - startVec2) / dash;
	}
	else if (arrow > 0) {
		dashing = 0;
		// Arrow dimensions

		float arrowLength = width * 2.0;
		vec2 arrowDir = arrow == 1.0 ? -dir: dir;
		// Arrow vertices
		vec3 arrowBase = arrow == 1.0 ? start : end;
		
		// For vector mode, adjust arrow base position
		if (arrow > 2.0 && arrow != 1.0) {
			vec3 worldDir = normalize(end - start);
			float startDepth = (mvp * vec4(start, 1.0)).w;
			float fixedScreenLength = arrow * dpi; // same as above
			float worldLengthAtStartDepth = fixedScreenLength * startDepth / (screenW * 0.5);
			arrowBase = start + worldDir * worldLengthAtStartDepth;
		}

		gl_Position = mvp * vec4(arrowBase, 1.0);

		if (gl_VertexIndex == 6) {
			offset = vec2(0);
		}
		else if (gl_VertexIndex == 7) {
			offset = nn - arrowDir * facDir;
		}
		else { // gl_VertexIndex == 8
			offset = -nn - arrowDir * facDir;
		}
		offset *= (width + 8) / width;
	}
	gl_Position.x += offset.x * width / screenW * gl_Position.w;
	gl_Position.y += offset.y * width / screenH * gl_Position.w;
	if (((displaying | int(flags)) & 4) != 0) {
		gl_Position.z -= gl_Position.w;
	}
	lid = int(inid); // gl_InstanceIndex;
}
@end

@fs line_bunch_fs

uniform line_bunch_params {
    mat4 mvp;
	float dpi;
    int bunch_id;
	float screenW;
	float screenH;

	int displaying; // not used here.
	vec4 shine_color_intensity;
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity
};

in vec4 v_Color;
flat in int lid;
in float dashing;
flat in int pflag;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth;
layout(location=2) out vec4 screen_id;

layout(location = 3) out float bordering;
layout(location = 4) out vec4 bloom;

void main() {
	//frag_color = v_Color;
    g_depth = -gl_FragCoord.z; // lines don't participate in postprocessing.
	screen_id = vec4(2, bunch_id, lid, 0);
	
	
	bool sel = (pflag & (1<<3))!=0;
	bool hovering = (pflag & (1<<4))!=0;

	// border or hovering or selected.
	if ((pflag & 1) !=0)
		bordering = 3;
	else if (sel)
		bordering = 2;
	else if (hovering)
		bordering = 1;
	else 
		bordering = 0;
	bordering /= 255;
	
	vec4 shine = vec4(0);
	if ((pflag & 2) !=0){ //self shine
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
