@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4
@ctype vec2 glm::vec2

@vs sprite_vs

// shaders for quad-image rendering.
uniform u_quadim{ // 64k max.
	mat4 pvm;
	vec2 screenWH; 
    
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity

	float time;
};

// per instance attributes:
in vec3 pos; 
in float f_flag; //border, shine, front, selected, hovering, billboard, loaded
in vec4 quat;
in vec2 dispWH;
in vec2 uvLeftTop;
in vec2 uvRightBottom;
in vec4 my_shine;
in float info; //rgbid.

out vec2 uv;

flat out int badrgba;
flat out int rgbId;
flat out int atlasId;
flat out int spriteId;
flat out vec4 shine;
flat out float bordering;

mat4 mat4_cast(vec4 q, vec3 position) {
	float qx = q.x;
	float qy = q.y;
	float qz = q.z;
	float qw = q.w;

	return mat4(
		1.0 - 2.0 * qy * qy - 2.0 * qz * qz, 2.0 * qx * qy + 2.0 * qz * qw, 2.0 * qx * qz - 2.0 * qy * qw, 0.0,
		2.0 * qx * qy - 2.0 * qz * qw, 1.0 - 2.0 * qx * qx - 2.0 * qz * qz, 2.0 * qy * qz + 2.0 * qx * qw, 0.0,
		2.0 * qx * qz + 2.0 * qy * qw, 2.0 * qy * qz - 2.0 * qx * qw, 1.0 - 2.0 * qx * qx - 2.0 * qy * qy, 0.0,
		position.x, position.y, position.z, 1.0
	);
}

void main() {
	int vtxId=int(gl_VertexIndex);
	int flag = int(f_flag); //
	bool billboard=(flag & (1<<5))!=0; // if billboard, the dispWH is pixel unit and the quad always facing the screen (like sprite).
    rgbId = int(info) >> 4;
	atlasId = int(info) & 15;
	spriteId = gl_InstanceIndex;
	badrgba = ((flag & (1<<6))!=0)?0:1;

    // Calculate position and UV coordinates
    vec4 finalPos;
    vec2 finalUV;
    int xf = (vtxId % 2);
    int yf = int(vtxId<2 || vtxId==3); //013|245
    finalUV = mix(uvLeftTop, uvRightBottom, vec2(xf, yf));

	// todo: dispWH is -1 means auto ratio.

    if (billboard) {
        // If billboard, the quad always faces the screen
        finalPos = pvm * vec4(pos, 1);
        finalPos.xyz /= finalPos.w;
        finalPos.w=1;
        finalPos.xy+=(vec2(xf, yf)-0.5) * dispWH / screenWH;
    } else {
        // If not billboard, apply rotation and position
        finalPos = pvm * mat4_cast(quat, pos) * vec4((vec2(xf, yf)-0.5) * dispWH, 0 , 1);
    }

    
	bool sel = ((flag & (1<<3))!=0);
	bool hovering = (flag & (1<<4))!=0;

	shine = vec4(0);
	if ((flag & 2) !=0){ //self shine
		shine = my_shine;
	}
	if (sel){ //selected shine
		shine += selected_shine_color_intensity;
	}
	if (hovering){ //hover shine
		shine += hover_shine_color_intensity;
	}
	shine = min(shine, vec4(1));


	if ((flag & 1) !=0)
		bordering = 3;
	else if (sel)
		bordering = 2;
	else if (hovering)
		bordering = 1;
	else 
		bordering = 0;
	bordering /= 16;
	
    gl_Position = finalPos;
    uv = finalUV;
}
@end

@fs sprite_fs
uniform u_quadim{ // 64k max.
	mat4 pvm;
	vec2 screenWH;
    
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity

	float time;
};

uniform sampler2DArray tex; //array of all 4096s.

in vec2 uv;
in flat int badrgba;
in flat int rgbId;
in flat int atlasId;
in flat int spriteId;

flat in vec4 shine;
flat in float bordering;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth;
layout(location=2) out vec4 screen_id;
layout(location=3) out float o_bordering;
layout(location=4) out vec4 bloom;
// actually inputed texture is float type, but we use as int type... could have compatibility issue.
layout(location=5) out float rgb_viewed; 

// from shadertoy https://www.shadertoy.com/view/4djSRW
float rand(vec2 p){
	vec3 p3  = fract(vec3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

void main() {
	vec4 v_Color;
	if (badrgba==1)
		v_Color = vec4(rand(gl_FragCoord.xy+fract(sin(time))), rand(gl_FragCoord.xy+fract(cos(time))), rand(gl_FragCoord.xy+fract(sin(time)*cos(time))), 1.0);
	else
		//v_Color = texture(tex, vec3(uv, atlasId)); //texture(atlas[atlasId], uv);
		v_Color = texelFetch(tex, ivec3(uv, atlasId),0)*0.5
			+texelFetch(tex, ivec3(uv.x+1, uv.y, atlasId),0)*0.2
			+texelFetch(tex, ivec3(uv.x,uv.y+1, atlasId),0)*0.2
			+texelFetch(tex, ivec3(uv.x+1,uv.y+1, atlasId),0)*0.1; // might have bleeding effect... be noticed.
			
	if (v_Color.w<0.1) discard;

	v_Color.xyz = v_Color.zyx;// bgr->rgb
	frag_color = vec4(v_Color.xyz * (1-shine.w*0.3) + shine.xyz*shine.w * 0.5, v_Color.w);
	bloom = vec4((frag_color.xyz+0.2)*(1+shine.xyz*shine.w) - 0.9, 1);;
	o_bordering = bordering;
    rgb_viewed = rgbId;
	g_depth=-gl_FragCoord.z; //other wise don't write g_depth.
    screen_id = vec4(3, spriteId, 0, 0);
}
@end

@program p_sprite sprite_vs sprite_fs

@vs sprite_stats_vs
// 4x shrink to max, 16px->1.
in vec2 pos;
out vec2 uv;

void main() {
    gl_Position = vec4(pos*2.0-1.0, 0.5, 1.0);
    uv = pos;
}
@end

@fs sprite_stats_fs

uniform sampler2D viewingID;
in vec2 uv;
out float most_occured_ID;

void main() {
    // Calculate the size of each 4x4 patch
	ivec2 patchCoord = ivec2(gl_FragCoord.xy * 4);

    // Read 4x4 patch of IDs
	int idx = -1;
    for (int i = 0; i < 4; ++i) {
        for (int j = 0; j < 4; ++j) {
			int id = int(texelFetch(viewingID, patchCoord + ivec2(i, j), 0).r);
			if (id == -1) continue;
			if (idx == id) {
				most_occured_ID = id;
				return;
			}
			idx = id;
        }
    }
	most_occured_ID = idx;
}
@end

@program sprite_stats sprite_stats_vs sprite_stats_fs
