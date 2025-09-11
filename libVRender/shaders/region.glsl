@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

@block region3d_params
uniform region3d_params {
	mat4 vm;
	mat4 pm;
	vec2 viewport_size;
	vec3 cam_pos;
	float quantize;
	float face_opacity;
};
@end

@vs region3d_vs
in vec2 position;
void main(){
	gl_Position = vec4(position,0.0,1.0);
}
@end

@fs region3d_fs
@include_block region3d_params

uniform usampler2D region_cache;  // RGBA32UI, width=2048, height=32 (4 tiers * 8 rows per tier)
uniform sampler2D depth_texture; // scene depth buffer (R32F into [0,1])

// 2048-wide hash
int hash_index(ivec3 q){
	return (q.x * 73856093 ^ q.y * 19349663 ^ q.z * 83492791) & 0x7ff; // 0..2047
}

// Each tier uses 8 rows, each slot contains up to 8 entries packed in those 8 rows
bool voxel_exists(int tier, ivec3 cell, out uvec4 voxel){
	int hx = hash_index(cell);
	int base_y = tier * 8;
	for (int k=0; k<8; ++k){
		uvec4 v = texelFetch(region_cache, ivec2(hx, base_y + k), 0);
		// 0x80000000 denotes empty entry
		if (v.r == 0x80000000u) continue;
		if (ivec3(int(v.r), int(v.g), int(v.b)) == cell){ voxel = v; return true; }
	}
	return false;
}

// compute dt to the next face of the current cell for grid size s; also output face normal
float next_boundary_dt(vec3 p, vec3 dir, float s, out vec3 n){
	const float BIG = 1e30;
	float ix = floor(p.x / s);
	float iy = floor(p.y / s);
	float iz = floor(p.z / s);
	float bx = (dir.x > 0.0 ? (ix + 1.0) * s : ix * s);
	float by = (dir.y > 0.0 ? (iy + 1.0) * s : iy * s);
	float bz = (dir.z > 0.0 ? (iz + 1.0) * s : iz * s);
	float dx = dir.x == 0.0 ? BIG : (bx - p.x) / dir.x;
	float dy = dir.y == 0.0 ? BIG : (by - p.y) / dir.y;
	float dz = dir.z == 0.0 ? BIG : (bz - p.z) / dir.z;
	float dt = dx; n = vec3(sign(dir.x),0,0);
	if (dy < dt) { dt = dy; n = vec3(0,sign(dir.y),0); }
	if (dz < dt) { dt = dz; n = vec3(0,0,sign(dir.z)); }
	return max(dt + s * 1e-4, 0.0);
}

layout(location=0) out vec4 frag_color;

void main(){
	// NDC coords for current pixel
	vec2 ndc_xy = (gl_FragCoord.xy / viewport_size) * 2.0 - 1.0;

	// Prepare inverses
	mat4 invPM = inverse(pm);
	mat4 invVM = inverse(vm);

	// Ray origin and direction (independent of scene depth)
	vec3 cam = cam_pos;
	vec4 clip_far = vec4(ndc_xy, 1.0, 1.0);
	vec4 view_far = invPM * clip_far; view_far /= view_far.w;
	vec3 world_far = (invVM * vec4(view_far.xyz, 1.0)).xyz;
	vec3 rayDir = normalize(world_far - cam);

	// Use the scene depth only to cap marching distance
	float scene_d = texelFetch(depth_texture, ivec2(gl_FragCoord.xy), 0).r;
	float t1;
	if (scene_d > 0.0 && scene_d < 1.0){
		vec4 clip = vec4(ndc_xy, scene_d * 2.0 - 1.0, 1.0);
		vec4 viewP = invPM * clip; viewP /= viewP.w;
		vec3 hitPos = (invVM * vec4(viewP.xyz, 1.0)).xyz;
		t1 = max(0.0, dot(hitPos - cam, rayDir));
	}else{
		// No valid depth: march until a generous far distance
		t1 = 1e6;
	}
	if (t1 <= 0.0){ frag_color = vec4(0.0); return; }

	// base voxel size (tier 0) and tier 1 size (factor 3)
	float s0 = max(quantize, 1e-6);
	float s1 = s0 * 3.0;

	vec3 accumPremul = vec3(0.0);
	float trans = 1.0;
	float t = 0.0;
	const int MAX_STEPS = 256;

	int curTier = 1; // start from tier-1
	ivec3 parentCell = ivec3(0);

	for (int step=0; step<MAX_STEPS && t < t1 && trans > 0.02; ++step){
		vec3 p = cam + rayDir * t;
		if (curTier == 1){
			// test tier-1
			parentCell = ivec3(floor(p / s1 + 1e-6));
			uvec4 temp;
			if (voxel_exists(1, parentCell, temp)){
				// descend without advancing
				curTier = 0;
				continue;
			}
			// empty: advance to next tier-1 boundary
			vec3 n1; float dt1 = next_boundary_dt(p, rayDir, s1, n1);
			float seg1 = min(dt1, t1 - t);
			if (seg1 <= 0.0) { t += s1 * 1e-4; continue; }
			t += seg1;
			continue;
		}

		// curTier == 0: operate inside current parentCell until crossing its boundary
		ivec3 cell0 = ivec3(floor(p / s0 + 1e-6));
		uvec4 vox = uvec4(0);
		vec3 n0; float dt0 = next_boundary_dt(p, rayDir, s0, n0);
		vec3 n1b; float dtToParent = next_boundary_dt(p, rayDir, s1, n1b);
		float seg = min(min(dt0, dtToParent), t1 - t);
		if (seg <= 0.0) { t += s0 * 1e-4; continue; }
		if (voxel_exists(0, cell0, vox)){
			// decode color and source alpha
			float r = float(vox.a & 255u) / 255.0;
			float g = float((vox.a >> 8) & 255u) / 255.0;
			float b = float((vox.a >> 16) & 255u) / 255.0;
			float a_src = float((vox.a >> 24) & 255u) / 255.0;
			vec3 c = vec3(r,g,b);
			// face glass once per crossed face (using n0)
			vec3 L = normalize(vec3(0.4, 0.7, 0.5));
			float shade = 0.9 + 0.1 * max(0.0, dot(normalize(n0), L));
			vec3 c_face = c * shade;
			float a_face = clamp(face_opacity + a_src, 0.0, 1.0);
			accumPremul += trans * a_face * c_face;
			trans *= (1.0 - a_face);
			// cloud volume within voxel
			float a_eff = clamp(a_src * (seg / s0), 0.0, 1.0);
			accumPremul += trans * a_eff * c;
			trans *= (1.0 - a_eff);
		}
		t += seg;
		// if we hit parent boundary, ascend
		if (seg + 1e-6 >= dtToParent){
			curTier = 1;
		}
	}

	float finalA = clamp(1.0 - trans, 0.0, 1.0);
	vec3 outColor = finalA > 0.0 ? (accumPremul / finalA) : vec3(0.0);
	frag_color = vec4(outColor, finalA);
}
@end

@program region3d region3d_vs region3d_fs 