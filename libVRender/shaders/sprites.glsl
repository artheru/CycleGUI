@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4
@ctype vec2 glm::vec2

@block u_quadim
// shaders for quad-image rendering.
uniform u_quadim{ // 64k max.
	mat4 pvm;
	vec2 screenWH;

	vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
	vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity

	float time;
};
@end

@vs sprite_vs
@include_block u_quadim

// per instance attributes:
in vec3 pos; 
in float f_flag; //border, shine, front, selected, hovering, loaded, billboard, unattenuated
in vec4 quat;
in vec2 dispWH;
in vec2 uvLeftTop;
in vec2 uvRightBottom;
in vec4 my_shine;
in vec2 info; //rgbid, sprite_id

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
    rgbId = int(info.x) >> 4;
	atlasId = int(info.x) & 15;
	spriteId = int(info.y);
	badrgba = ((flag & (1<<5))!=0)?0:1;

    // Calculate position and UV coordinates
    vec4 finalPos;
    vec2 finalUV;
    int xf = (vtxId % 2); 
    int yf = int(vtxId<2 || vtxId==3); //013|245 
    finalUV = mix(uvLeftTop, uvRightBottom, vec2(xf, yf));

	// todo: dispWH is -1 means auto ratio.
	
	bool billboard = (flag & (1 << 6)) != 0; //see me_sprite def.
	bool unattenuated = (flag & (1 << 7)) != 0;
	
	if (!billboard) {
		if (unattenuated) {
			// For unattenuated non-billboard, we want to counteract perspective scaling
			// First get center position of the sprite in projected space
			vec4 centerPos = pvm * mat4_cast(quat, pos) * vec4(0, 0, 0, 1);
			float depth = centerPos.w;
			
			// Create a modified model matrix that scales with distance
			mat4 scaledModel = mat4_cast(quat, pos);
			// Scale the model matrix by depth to counteract perspective division
			scaledModel[0].xyz *= depth;
			scaledModel[1].xyz *= depth;
			scaledModel[2].xyz *= depth;
			
			// Apply the scaled model matrix for the quad
			finalPos = pvm * scaledModel * vec4((vec2(xf, yf) - 0.5) / screenWH * dispWH * 2, 0, 1);
		} else {
			// Regular non-billboard with normal perspective
			finalPos = pvm * mat4_cast(quat, pos) * vec4((vec2(xf, yf) - 0.5) * dispWH * 2, 0, 1);
		}
	}
	else {
		// If billboard, the quad always faces the screen
		finalPos = pvm * vec4(pos, 1);

		if (!unattenuated) {
			// For attenuated billboards, counteract perspective scaling
			float depth = finalPos.w;
			finalPos.xyz /= finalPos.w;
			finalPos.w = 1;
			
			// Apply depth-scaled offset to maintain constant visual size
			finalPos.xy += (vec2(xf, yf) - 0.5) * dispWH / depth * 2.337f; //wtf is this magic number? whatever, it works.
		} else {
			// Regular billboard
			finalPos.xyz /= finalPos.w;
			finalPos.w = 1;
			finalPos.xy += (vec2(xf, yf) - 0.5) * dispWH / screenWH;
		}
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
	bordering /= 255;
	
    gl_Position = finalPos;
    uv = finalUV;
}
@end

@fs sprite_fs
@include_block u_quadim

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

float hash13(vec3 p3)
{
	p3 = fract(p3 * .1031);
	p3 += dot(p3, p3.zyx + 31.32);
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
	// hashing based transparency:
	if (hash13(vec3(gl_FragCoord.xy, time)) < 1 - v_Color.w)
		discard;

	v_Color.xyz = v_Color.xyz;
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
	// todo: maybe use a dynamic way to determine?
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


///////////////////////////
@vs svg_sprite_vs
@include_block u_quadim

// vertex attributes from interleaved buffer
in vec3 v_position; // SVG per vertex position.
in vec4 color;

// per instance attributes:
in vec3 pos; // SVG position(model position).
in float f_flag; //border, shine, front, selected, hovering, loaded, billboard, unattenuated
in vec4 quat;
in vec2 dispWH;
in vec4 my_shine;
in vec2 info; //flags, sprite_id

flat out int spriteId;
flat out vec4 shine;
flat out float bordering;
out vec4 v_color;

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
	int flag = int(f_flag);
	spriteId = int(info.y);

	// Pass the vertex color to fragment shader
	v_color = color;

	// Calculate final position based on instance and vertex data
	vec4 finalPos;

	bool billboard = (flag & (1 << 6)) != 0;
	bool unattenuated = (flag & (1 << 7)) != 0;

	vec3 position = vec3(v_position.xy / 1000, 0);
	if (!billboard) {
		// Regular model transformation - apply model matrix to vertex position
		mat4 modelMatrix = mat4_cast(quat, pos);

		// Apply scale to the model matrix
		modelMatrix[0].xyz *= dispWH.x;
		modelMatrix[1].xyz *= dispWH.y;

		if (unattenuated) {
			// For unattenuated, apply depth-based scaling
			vec4 centerPos = pvm * vec4(pos, 1.0);
			float depth = centerPos.w;

			// Scale the model matrix by depth to counteract perspective division
			modelMatrix[0].xyz *= depth;
			modelMatrix[1].xyz *= depth;
			modelMatrix[2].xyz *= depth;
		}

		// Transform the vertex by model and projection matrices
		finalPos = pvm * modelMatrix * vec4(position, 1.0);
	}
	else {
		// Billboard mode - the base transform is just the position
		vec4 centerPos = pvm * vec4(pos, 1.0);

		if (!unattenuated) {
			// Standard billboard with perspective
			float depth = centerPos.w;
			vec3 normalizedPos = centerPos.xyz / depth;

			// Get screen position
			vec2 screenPos = normalizedPos.xy;

			// Apply the vertex offset scaled by depth
			vec2 scaledOffset = position.xy * dispWH / depth;

			// Construct the final position
			finalPos = vec4(screenPos + scaledOffset, normalizedPos.z, 1.0);
		}
		else {
			// Unattenuated billboard (constant size regardless of distance)
			vec3 normalizedPos = centerPos.xyz / centerPos.w;

			// Apply vertex offset in screen space
			vec2 scaledOffset = position.xy * dispWH / screenWH;

			// Construct the final position
			finalPos = vec4(normalizedPos.xy + scaledOffset, normalizedPos.z, 1.0);
		}
	}

	bool sel = (flag & (1 << 3)) != 0;
	bool hovering = (flag & (1 << 4)) != 0;

	shine = vec4(0);
	if ((flag & 2) != 0) { //self shine
		shine = my_shine;
	}
	if (sel) { //selected shine
		shine += selected_shine_color_intensity;
	}
	if (hovering) { //hover shine
		shine += hover_shine_color_intensity;
	}
	shine = min(shine, vec4(1));


	if ((flag & 1) != 0)
		bordering = 3;
	else if (sel)
		bordering = 2;
	else if (hovering)
		bordering = 1;
	else
		bordering = 0;
	bordering /= 255;

	gl_Position = finalPos;
}
@end


@fs svg_sprite_fs
@include_block u_quadim

flat in int spriteId;
flat in vec4 shine;
flat in float bordering;
in vec4 v_color;

layout(location = 0) out vec4 frag_color;
layout(location = 1) out float g_depth;
layout(location = 2) out vec4 screen_id;
layout(location = 3) out float o_bordering;
layout(location = 4) out vec4 bloom;

void main() {
	// Use the vertex color from the interleaved buffer
	vec4 baseColor = v_color;

	frag_color = vec4(baseColor.xyz * (1 - shine.w * 0.3) + shine.xyz * shine.w * 0.5, baseColor.w);
	bloom = vec4((frag_color.xyz + 0.2) * (1 + shine.xyz * shine.w) - 0.9, 1);
	o_bordering = bordering;
	g_depth = -gl_FragCoord.z; // Write depth
	screen_id = vec4(3, spriteId, 0, 0); // Use same type ID as regular sprites
}
@end

@program p_svg_sprite svg_sprite_vs svg_sprite_fs