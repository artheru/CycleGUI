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

uniform isampler2D region_cache;  // RGBA16SI, width=2048, height=2048 (2 tiers * 1024 rows per tier), 256K buckets, 8 slots per bucket
uniform sampler2D depth_texture; // scene depth buffer (R32F into [0,1])

// 256K-wide hash (bucket count)
int hash_index(ivec3 q){
	return (q.x * 73856093 ^ q.y * 19349663 ^ q.z * 83492791) & 0x3ffff; // 0..262143
}

// Each bucket has 8 horizontal slots; layout:
// width=2048 = 256 buckets/row * 8 slots; height=2048 = 1024 rows * 2 tiers
bool voxel_exists(int tier, ivec3 cell, out ivec4 voxel){
	int hx = hash_index(cell);
	int by = hx / 256;         // 0..1023 buckets per tier vertically
	int bx = hx - by * 256;    // 0..255 buckets per row
	int base_x = bx * 8;
	int y = tier * 1024 + by;
	for (int k=0; k<8; ++k){
		ivec4 v = texelFetch(region_cache, ivec2(base_x + k, y), 0);
		// -32768 denotes empty entry in R (signed 16-bit min)
		if (v.r == -32768) return false;
		if (ivec3(v.r, v.g, v.b) == cell){ voxel = v; return true; }
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

// unpack RGBA4444 from 16-bit signed channel
vec4 unpack_rgba4444(int a16){
	uint u = uint(a16) & 0xffffu;
	float r = float((u >> 12) & 0xfu) / 15.0;
	float g = float((u >> 8)  & 0xfu) / 15.0;
	float b = float((u >> 4)  & 0xfu) / 15.0;
	float a = float(u & 0xfu) / 15.0;
	return vec4(r,g,b,a);
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
		t1 = 1e6;
	}
	if (t1 <= 0.0){ frag_color = vec4(0.0); return; }

	// two tiers: s0 = q, s1 = 7*q
	float s0 = max(quantize, 1e-6);
	float s1 = s0 * 6.0;

	vec3 accumPremul = vec3(0.0);
	float trans = 1.0;
	float t = 0.0;
	const int MAX_STEPS = 100;
	int curTier = 1;

	bool glass = false;
	for (int step=0; step<MAX_STEPS && t < t1 && trans > 0.1; ++step){
		vec3 p = cam + rayDir * t;
		if (curTier == 1){
			ivec3 cell1 = ivec3(floor(p / s1 + 1e-6));
			ivec4 tmp;
			if (voxel_exists(1, cell1, tmp)) { curTier = 0; continue; }
			vec3 n1; float dt1 = next_boundary_dt(p, rayDir, s1, n1);
			float seg1 = min(dt1, t1 - t);
			if (seg1 <= 0.0) { t += s1 * 1e-4; continue; }
			t += seg1;
			continue;
		}

		// tier 0 within parent
		ivec3 cell0 = ivec3(floor(p / s0 + 1e-6));
		ivec4 vox = ivec4(0);
		vec3 n0; float dt0 = next_boundary_dt(p, rayDir, s0, n0);
		vec3 n1b; float dtToParent = next_boundary_dt(p, rayDir, s1, n1b);
		float seg = min(min(dt0, dtToParent), t1 - t);
		if (seg <= 0.0) { t += s0 * 1e-4; continue; }
		if (voxel_exists(0, cell0, vox)){
			vec4 cA = unpack_rgba4444(vox.a);
			vec3 c = cA.rgb;
			float a_src = cA.a;
			if (!glass) {
				vec3 rseed = ivec3(floor(p / (s0 / 10)));
				float r1 = fract(sin(dot(rseed, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
				float r2 = fract(sin(dot(rseed + 17.0, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
				float r3 = fract(sin(dot(rseed + 39.0, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
				vec3 c_face = 0.8 + vec3(r1, r2, r3) * 0.2;
				accumPremul += c_face * face_opacity;
				trans = (1.0 - face_opacity);
				glass = true;
			}
			// todo: this should use some exponential decay. but whatever.
			float a_eff = clamp(a_src * (seg / s0), 0.0, 1.0);
			accumPremul += trans * a_eff * c;
			trans *= (1.0 - a_eff);
		}
		t += seg;
		if (seg + 1e-6 >= dtToParent){ curTier = 1; }
	}

	float finalA = clamp(1.0 - trans, 0.0, 1.0);
	vec3 outColor = finalA > 0.0 ? (accumPremul / finalA) : vec3(0.0);
	frag_color = vec4(outColor, finalA);
}
@end

@program region3d region3d_vs region3d_fs 