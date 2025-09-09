@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

@block walkable_params
uniform walkable_params {
	mat4 vm;
	mat4 pm;
	vec2 viewport_size;
	vec4 cam_pos_res;   // xyz: cam pos, w: res_scale
	vec4 grid_origin;   // xyz: origin, w: unused
};
@end

@vs walkable_vs
in vec2 position;
out vec2 uv;
void main(){
	uv = (position + 1.0) * 0.5;
	gl_Position = vec4(position,0,1);
}
@end

@fs walkable_fs
@include_block walkable_params
uniform sampler2D u_cache; // RGBA32F, 2048x268
//uniform sampler2D uDepth;  // scene depth
in vec2 uv;
layout(location=0) out vec4 frag_color;

// hash helpers
int hash_index(ivec2 q){
	int xq=q.x, yq=q.y;
	return (xq * 997 + yq) & 0x7ff;
	//int ah = (yq * 4761041 + 4848659) % 5524067;
	//int bh = (xq * 6595867 + 3307781) % 7237409;
	//return abs(ah + bh) % 2048;
}

vec4 test_walkable(vec2 world_xy){
	// process tiers in order same to cpu.
	float qs[4] = float[4](3.0, 10.0, 30.0, 90.0);
	int traversed = 0;
	float accum = 0;
	for (int tier = 0; tier < 4; ++tier){
		float q = qs[tier];
		// 4-neighborhood hashing: floor/ceil for x,y
		vec2 wq = world_xy / q;
		ivec2 f = ivec2(floor(wq));
		ivec2 c = ivec2(ceil(wq));
		ivec2 cells[4] = ivec2[4](ivec2(f.x, f.y), ivec2(c.x, f.y), ivec2(f.x, c.y), ivec2(c.x, c.y));
		
		int ids[16]; int n = 0;
		// gather up to 16 candidates from up to 4 hash slots in this tier
		for (int s=0; s<4 && n<16; ++s){
			ivec2 cell = cells[s];
			int h = hash_index(cell);
			vec4 v = texelFetch(u_cache, ivec2(h, tier), 0);
			int cand0 = int(v.x), cand1 = int(v.y), cand2 = int(v.z), cand3 = int(v.w);
			if (cand0>=0) ids[n++] = cand0;
			if (n<16 && cand1>=0) ids[n++] = cand1;
			if (n<16 && cand2>=0) ids[n++] = cand2;
			if (n<16 && cand3>=0) ids[n++] = cand3;
		}
		
		// if this tier has any candidate, perform walkable test and quit tier loop afterward
		if (n>0){
			for(int i=0;i<n;i++){
				int lid = ids[i];
				if (lid<0) continue;
				// read local map xytheta (rows 4..11 after 4 hash rows), 2048-wide
				int row = 4 + (lid/2048);
				int col = lid % 2048;
				vec4 xyta = texelFetch(u_cache, ivec2(col,row), 0);
				vec2 lxy = xyta.xy; float theta = xyta.z;
				// transform world pos into local map polar
				vec2 d = world_xy - lxy;
				float wang = atan(d.y, d.x);
				float ang = wang - theta; 
				ang = mod(ang, 6.28318530718);
				if (ang<0.0) ang += 6.28318530718;
				wang = mod(wang, 6.28318530718);
				if (wang < 0.0) wang += 6.28318530718;
				
				float abin_f = ang * (128.0 / 6.28318530718);
				int abin_low = int(floor(abin_f));
				int abin_high = int(ceil(abin_f));
				//float abin_frac = fract(abin_f);
				if (abin_low < 0) abin_low += 128;
				if (abin_high >= 128) abin_high -= 128;
				
				int gx = (lid % 64);
				int gy = (lid / 64);
				
				// 128 bins => 32 texels (RGBA), bins start at row 12
				int binTex_low = abin_low/4;
				int binChan_low = abin_low%4;
				int acx_low = gx*32 + binTex_low;
				int acy = 12 + gy;
				vec4 ad_low = texelFetch(u_cache, ivec2(acx_low,acy), 0);
				float dist_low = (binChan_low==0?ad_low.x:binChan_low==1?ad_low.y:binChan_low==2?ad_low.z:ad_low.w);
				
				int binTex_high = abin_high/4;
				int binChan_high = abin_high%4; 
				int acx_high = gx*32 + binTex_high;
				vec4 ad_high = texelFetch(u_cache, ivec2(acx_high,acy), 0);
				float dist_high = (binChan_high==0?ad_high.x:binChan_high==1?ad_high.y:binChan_high==2?ad_high.z:ad_high.w);
				
				float dist_allow = min(dist_low, dist_high);// mix(dist_low, dist_high, abin_frac);
				float dist = length(d);
				traversed += 1;
				if (dist < dist_allow) {
					if (dist_allow < 9999999) {
						if (dist < 20)
							return vec4(0, 1.0, 0.0, 0.4);
						else
							accum = max(accum, 20 / dist);
					}
					else {
						accum = max(accum, clamp(2 / dist, 0, 1));
					}
				}
			}
			// had candidates in this tier but none passed – quit as requested
			if (traversed > 10)
				return vec4(0, 1, 1, 0.3*accum);
		}
	}
	return vec4(0, 1, 1, 0.3*accum);
}

void main(){
	// reconstruct world ray and intersect with ground plane z=0
	vec2 ndc = (2.0 * uv - vec2(1.0));
	vec4 clip = vec4(ndc.x, ndc.y, -1.0, 1.0);
	mat4 inv_pm = inverse(pm);
	mat4 inv_vm = inverse(vm);
	vec4 eye = inv_pm * clip;
	vec3 world_ray_dir = normalize((inv_vm * vec4(eye.xyz, 0.0)).xyz);
	vec3 cam = cam_pos_res.xyz;
	if (world_ray_dir.z == 0.0) { discard; }
	float t = -cam.z / world_ray_dir.z;
	if (t < 0.0) { discard; }
	vec3 hit = cam + t * world_ray_dir;
	// project hit back and compare against scene depth; discard if occluded or out of range
	mat4 pv = pm * vm;
	vec4 ndci = pv * vec4(hit, 1.0);
	ndci /= ndci.w;
	float idepth = ndci.z * 0.5 + 0.5;
	if (idepth <= 0.0 || idepth >= 1.0) { discard; }
	frag_color = test_walkable(hit.xy);
}
@end

@program walkable walkable_vs walkable_fs 