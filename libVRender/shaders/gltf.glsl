@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4



///////////////////////////////////////////
// Hierarchically compute node's model matrix on gpu.
@vs vs_hierarchical_mat
uniform hierarchical_uniforms{ // 64k max.
	int max_nodes;
	int max_instances;
	int depth;
	int pass_n;
	int offset;
};

//in float vid;

uniform isampler2D parents;
uniform sampler2D nmat;


flat out mat4 modelView;

mat4 getMat(int nodeId) {
	int x = (nodeId % 1024)*2;
	int y = (nodeId / 1024)*2;
	return mat4(
		texelFetch(nmat, ivec2(x, y), 0),
		texelFetch(nmat, ivec2(x, y + 1), 0),
		texelFetch(nmat, ivec2(x + 1, y), 0),
		texelFetch(nmat, ivec2(x + 1, y + 1), 0));
}

void main(){
	int nid=int(gl_VertexIndex);

	//layout: instance_id*4+i,node_id
	// whole texture is 2048*2048, (2x2px per node/object) wise. 1M node/instance. (gl_point)
	int write_id = max_instances * nid + gl_InstanceIndex + offset;
	int tmp_id = write_id;
	modelView = getMat(tmp_id);

	int w = textureSize(parents, 0).x;
	//int zw = int(textureSize(nm, 0).x/512.0);
	if (pass_n<depth)
		for (int i=0; i<4; ++i){
			int fetch_pos = pass_n * max_nodes + nid;
			int fx = fetch_pos % w;
			int fy = fetch_pos / w;
			nid = int(texelFetch(parents, ivec2(fx, fy), 0).r);
			if (nid == -1) break;

			tmp_id = max_instances * nid + gl_InstanceIndex + offset;
			modelView = getMat(tmp_id) * modelView;
		}
	
	gl_PointSize = 2;

	int x=write_id%1024;
	int y=write_id/1024;
	
	gl_Position = vec4((x+0.5)/512-1.0, (y+0.5)/512-1.0, 0, 1);
}

@end

@fs fs_write_mat4

flat in mat4 modelView;

out vec4 NImodelViewMatrix;

void main() {
	ivec2 uv = ivec2(gl_FragCoord.xy - ivec2(gl_FragCoord / 2) * 2);
	int n = uv.x * 2 + uv.y;
	NImodelViewMatrix = modelView[n];
}

@end

@program gltf_hierarchical vs_hierarchical_mat fs_write_mat4


///
// Finally compute inverse normal matrix.
@vs vs_node_final_mat
uniform sampler2D nmat;

//in float vid;

flat out mat4 iModelView;

mat4 getMat(int nodeId) {
	int x = (nodeId % 1024)*2;
	int y = (nodeId / 1024)*2;
	return mat4(
		texelFetch(nmat, ivec2(x, y), 0),
		texelFetch(nmat, ivec2(x, y + 1), 0),
		texelFetch(nmat, ivec2(x + 1, y), 0),
		texelFetch(nmat, ivec2(x + 1, y + 1), 0));
}

const int depth = 4;

void main() {
	int my_id = int(gl_VertexIndex);

	mat4 modelView = getMat(my_id);
	iModelView = transpose(inverse(modelView));

	gl_PointSize = 2;

	int x = my_id % 1024;
	int y = my_id / 1024;

	gl_Position = vec4((x + 0.5) / 512 - 1.0, (y + 0.5) / 512 - 1.0, 0, 1);
}

@end


@fs fs_write_mat3

flat in mat4 iModelView;
out vec4 NInormalMatrix;

void main() {
	ivec2 uv = ivec2(gl_FragCoord.xy - ivec2(gl_FragCoord / 2) * 2);
	int n = uv.x * 2 + uv.y;
	NInormalMatrix = vec4(vec3(iModelView[n]), 0);
}

@end

@program gltf_node_final vs_node_final_mat fs_write_mat3


//////////////////////////////////////////////////////
// GROUND per gltf object.
@vs vs_ground
uniform gltf_ground_mats{
	mat4 pv;
	vec3 campos;
};
in vec3 x_y_radius; // per instance;

// per vertex
in vec2 position;
out vec2 vpos;
out float dist;
out float fa;

void main(){
	gl_Position = pv * vec4(
		x_y_radius.x + position.x * x_y_radius.z, 
		x_y_radius.y + position.y * x_y_radius.z, 0.0, 1.0 );
	vpos=position;
	dist = length(campos-vec3(x_y_radius.xy,0));
	fa = 1-abs(campos.z)/(length(campos.xy-x_y_radius.xy)+abs(campos.z));
}
@end

@fs fs_ground
in vec2 vpos;
in float dist;
in float fa;

out vec4 frag_color;

//float random(vec2 uv) { return fract(sin(dot(uv.xy, vec2(12.9898, 78.233))) * 43758.5453); }
// very good quality prng: https://www.shadertoy.com/view/4djSRW
float random(vec2 p)
{
	vec3 p3  = fract(vec3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}


void main(){
	float radius=length(vpos);
	float dfactor=clamp(2/dist, 0.1,0.4);
	//vec3 baseColor=vec3(1.0,0.95,0.9);
	vec3 baseColor=vec3(1.0,0.95,0.9)*0.8+
		vec3(random(gl_FragCoord.xy),random(gl_FragCoord.yx),random(gl_FragCoord.xx))*dfactor*fa*0.7;

	frag_color = vec4(baseColor ,dfactor-exp((radius-0.8) * 10));
}
@end

@program gltf_ground vs_ground fs_ground

// header:

@include fake_pbr.glslh

@block u_gltf_mats
uniform gltf_mats
@include_block udef
@end


////////////////////////////////////////////////
// Actual Draw:

@vs gltf_vs

@include_block u_gltf_mats
@include_block vs_header
@include_block bsearch

// only for mica effect.
out vec2 vid_hash2;

void main() {
	int node_id = int(fnode_id);
	int skin_idx = int(fnode_info.x);

	int instid = gl_InstanceIndex + instance_index_offset;
	int noff = max_instances * node_id + instid + offset;

	int obj_id = instid + obj_offset;
	
	// 0:borner? 1:shine? 2:front?,3:selected?
	vec4 shine=vec4(0);
	vborder = 0;
	
	ivec4 objmeta = texelFetch(perinstance, ivec2(obj_id % 4096, obj_id / 4096), 0);
	int nodeflag = int(texelFetch(pernode, ivec2((noff % 2048) * 2, (noff / 2048)), 0).w);
	myflag = int(objmeta.w);

	bool selected = (myflag & (1 << 3)) != 0 || (nodeflag & (1 << 3)) != 0;	
	bool hovering = instid == hover_instance_id &&
			(((myflag & (1<<4)) != 0) || hover_node_id == node_id && ((myflag & (1<<5)) != 0));

	// todo: optimize the below code.
	if (hovering && selected){
		vborder = 2;
		shine = hover_shine_color_intensity*0.8+selected_shine_color_intensity*0.5;
	}
	else if (hovering){
		vborder = 1;
		shine = hover_shine_color_intensity;
	}else if (selected){
		vborder = 2;
		shine = selected_shine_color_intensity;
	}else{
		if ((myflag & 1) != 0 || (nodeflag & 1) != 0)
			vborder = 3;

		if ((myflag & 2) != 0)
			shine = vec4(float(objmeta.z & 0xFF),
				float((objmeta.z >> 8) & 0xFF),
				float((objmeta.z >> 16) & 0xFF),
				float((objmeta.z >> 24) & 0xFF)) / 256.0;
		// node color is low precision color. 4K color(0xfff=12bit). only using 20bit.
		if ((nodeflag & 2) != 0) {
			int packedColor = nodeflag >> 8; //total 12bit.
			shine += vec4(float(packedColor & 0xF),
				float((packedColor >> 4) & 0xF),
				float((packedColor >> 8) & 0xF),
				16.0) / 16.0;
		}
	}

	// further squeeze 3bit of transparency...
	//float alpha = (8 - ((nodeflag >> 20) & 7)) / 8.0;

	vborder /= 255.0; //stupid webgl...
	vshine = shine;

	mat4 modelViewMatrix;
	mat3 normalMatrix;
	vid = vec4(1000 + class_id, instid / 16777216, instid % 16777216, int(node_id));

	if (skin_idx == 255) {
		int get_id=max_instances*int(node_id) + instid + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}

		int x=(get_id%1024)*2;
		int y=(get_id/1024)*2;

		modelViewMatrix = mat4(
			texelFetch(NImodelViewMatrix, ivec2(x, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x, y + 1), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y + 1), 0));

		normalMatrix = mat3(
			texelFetch(NInormalMatrix, ivec2(x, y), 0).rgb,
			texelFetch(NInormalMatrix, ivec2(x, y + 1), 0).rgb,
			texelFetch(NInormalMatrix, ivec2(x + 1, y), 0).rgb);
	}
	else {
		int get_id = max_instances * int(jointNodes.x) + instid + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
		int x = (get_id % 1024) * 2;
		int y = (get_id / 1024) * 2;
		int invId = skin_idx + int(joints.x);
		int xx = (invId % 512) * 4;
		int yy = invId / 512;
		mat4 wj0 = weights.x * mat4(
				texelFetch(NImodelViewMatrix, ivec2(x, y), 0),
				texelFetch(NImodelViewMatrix, ivec2(x, y + 1), 0),
				texelFetch(NImodelViewMatrix, ivec2(x + 1, y), 0),
				texelFetch(NImodelViewMatrix, ivec2(x + 1, y + 1), 0)) *
			mat4(
				texelFetch(skinInvs, ivec2(xx, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 1, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 2, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 3, yy), 0));

		get_id = max_instances * int(jointNodes.y) + instid + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
		x = (get_id % 1024) * 2;
		y = (get_id / 1024) * 2;
		invId = skin_idx + int(joints.y);
		xx = (invId % 512) * 4;
		yy = invId / 512;
		mat4 wj1 = weights.y * mat4(
			texelFetch(NImodelViewMatrix, ivec2(x, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x, y + 1), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y + 1), 0)) *
			mat4(
				texelFetch(skinInvs, ivec2(xx, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 1, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 2, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 3, yy), 0));

		get_id = max_instances * int(jointNodes.z) + instid + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
		x = (get_id % 1024) * 2;
		y = (get_id / 1024) * 2;
		invId = skin_idx + int(joints.z);
		xx = (invId % 512) * 4;
		yy = invId / 512;
		mat4 wj2 = weights.z * mat4(
			texelFetch(NImodelViewMatrix, ivec2(x, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x, y + 1), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y + 1), 0)) *
			mat4(
				texelFetch(skinInvs, ivec2(xx, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 1, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 2, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 3, yy), 0));

		get_id = max_instances * int(jointNodes.w) + instid + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
		x = (get_id % 1024) * 2;
		y = (get_id / 1024) * 2;
		invId = skin_idx + int(joints.w);
		xx = (invId % 512) * 4;
		yy = invId / 512;
		mat4 wj3 = weights.w * mat4(
			texelFetch(NImodelViewMatrix, ivec2(x, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x, y + 1), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y + 1), 0)) *
			mat4(
				texelFetch(skinInvs, ivec2(xx, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 1, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 2, yy), 0),
				texelFetch(skinInvs, ivec2(xx + 3, yy), 0));

		modelViewMatrix = wj0 + wj1 + wj2 + wj3;
		normalMatrix = transpose(inverse(mat3(modelViewMatrix)));
	}
        
	// morph targets:
	int animationId = int(objmeta.x);
	vec3 mypos = position;
	if (animationId >= 0) {
		float elapsed = objmeta.y / 1000.0f;
		uint wid = animationId * node_amount + node_id;
		uvec4 w_anim = texelFetch(animap, ivec2((wid % 512) * 4 + 3, wid / 512), 0);

		if (w_anim.w > 0) {
			// has morph targets.
			ivec2 texSize = textureSize(animtimes, 0); // Get texture size to determine array length
			ivec2 texSizeDt = textureSize(morphdt, 0); // Get texture size to determine array length
			int meta_place = int(w_anim.w - 1);
			int targetsN = int(texelFetch(morphdt, ivec2(meta_place % texSizeDt.x, meta_place / texSizeDt.x), 0).r);
			int vcount = int(texelFetch(morphdt, ivec2((meta_place + 1) % texSizeDt.x, (meta_place + 1) / texSizeDt.x), 0).r);
			int vstart = int(texelFetch(morphdt, ivec2((meta_place + 2) % texSizeDt.x, (meta_place + 2) / texSizeDt.x), 0).r);
			// todo: node_vstart actually has two start: one opaque, one transparent. morphing node must not have both type primitives. should fix with vstart=>vstart/tvstart+uniform_tvtx_start test.

			ivec2 bs = bsearch(w_anim.z, w_anim.z + w_anim.y - 1, texSize, elapsed);
			float loTime = texelFetch(animtimes, ivec2(bs.x % texSize.x, bs.x / texSize.x), 0).r;
			float hiTime = texelFetch(animtimes, ivec2(bs.y % texSize.x, bs.y / texSize.x), 0).r;

			uint data_idx1 = w_anim.x + (bs.x - w_anim.z) * targetsN;
			uint data_idx2 = w_anim.x + (bs.y - w_anim.z) * targetsN;

			float tD = hiTime - loTime;
			int vert = int(gl_VertexIndex) - vstart;
			for (int i = 0; i < targetsN; ++i) { //for each target compute weights
				uint di1 = data_idx1 + i;
				uint di2 = data_idx2 + i;
				float datalo = texelFetch(morphdt, ivec2(di1 % texSizeDt.x, di1 / texSizeDt.x), 0).r;
				float datahi = texelFetch(morphdt, ivec2(di2 % texSizeDt.x, di2 / texSizeDt.x), 0).r;
				float t;
				if (elapsed > hiTime)
					t = datahi;
				else if (elapsed < loTime)
					t = datalo;
				else t = ((elapsed - loTime) * datahi + (hiTime - elapsed) * datalo) / tD;
				if (t == 0) continue;
				uint mtid = meta_place + 3 + i * vcount * 3 + vert * 3;
				mypos += t * vec3(texelFetch(morphdt, ivec2(mtid % texSizeDt.x, mtid / texSizeDt.x), 0).r,
					texelFetch(morphdt, ivec2((mtid + 1) % texSizeDt.x, (mtid + 1) / texSizeDt.x), 0).r,
					texelFetch(morphdt, ivec2((mtid + 2) % texSizeDt.x, (mtid + 2) / texSizeDt.x), 0).r);
			}
		}
	}


	vec4 mPosition = modelViewMatrix * vec4( mypos, 1.0 );
	vNormal = normalize( normalMatrix * normal );
	vertPos = mPosition.xyz/mPosition.w; // camera coordination.

	if (cs_active_planes > 0 && ((myflag & (1 << 7)) == 0)) {
		vec4 tmp = iv * mPosition;
		vWorld3D = vec3(tmp); 
	}

	gl_Position = projectionMatrix * mPosition;

	//"move to front" displaying paramter processing.
	if ((myflag & 4) != 0 || (nodeflag & 4) != 0 || hovering && (display_options & 1) != 0) {
		gl_Position.z -= gl_Position.w;
	}

    color = color0;
	//color.w = min(color.w, alpha);

	uv = texcoord0;
	uv_atlas = tex_atlas;
	em_atlas_out = em_atlas;
	tex_weight_out = tex_weight;
	env_intensity = fnode_info.y / 127.0; // Convert from 0-127 range to 0-1 range


	vid_hash2 = vec2(gl_VertexIndex % 2, gl_VertexIndex / 2) * 0.5; //mica powder hashing like varying.
}
@end

@fs gltf_fs
@include_block u_gltf_mats
@include_block computeNormalColor
@include_block fs_header

in vec2 vid_hash2;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth; // the fuck. blending cannot be closed?
layout(location=2) out vec4 out_normal;
layout(location=3) out vec4 screen_id;

layout(location=4) out float bordering;
layout(location=5) out vec4 shine;

vec3 hash32(vec2 p)
{
	vec3 p3 = fract(vec3(p.xyx) * vec3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz+33.33);
    return fract((p3.xxy+p3.yzz)*p3.zyx);
}

float hash12(vec2 p)
{
	vec3 p3 = fract(vec3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float hash13(vec3 p3)
{
	p3 = fract(p3 * .1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

void main( void ) {
	// Add clipping test at start of fragment shader
	if (cs_active_planes > 0 && ((myflag & (1 << 7)) == 0)) {
		bool isBorder = false;
		vec3 ddx = dFdx(vWorld3D);
		vec3 ddy = dFdy(vWorld3D);
		for (int i = 0; i < cs_active_planes; i++) {
			float dist = dot(vWorld3D.xyz - cs_planes[i].xyz, cs_directions[i].xyz);
			if (dist > 0.0) {
				discard;
			}

			float gradientMag = length(vec2(
				dot(ddx, cs_directions[i].xyz),
				dot(ddy, cs_directions[i].xyz)
			));

			if (abs(dist) < gradientMag) {
				isBorder = true;
			}
		}
		if (isBorder) {
			frag_color = cs_color;
			return;
		}
	}

	screen_id = vid;
	
	vec4 baseColor = color;
	// Sample base color texture
	if (uv_atlas.x > 0 && tex_weight_out.x > 0) {
		baseColor = baseColor * texture(t_atlas, fract(uv.xy) * uv_atlas.xy + uv_atlas.zw);
	}

	// alpha dithering.
	if (baseColor.w < 0.95) discard;

	//vec3 magic = vec3(0.06711056f, 0.00583715f, 52.9829189f);
	//if (baseColor.a < 0.1 || baseColor.a < fract(magic.z * fract(dot(gl_FragCoord.xy+vec2(sin(time*0.001),cos(time*0.001))*0.1, magic.xy))))
	//	discard;
	
	// Sample emissive texture
	vec3 emissiveColor = vec3(0.0);
	if (em_atlas_out.x > 0 && tex_weight_out.y > 0) {
		emissiveColor = texture(t_atlas, fract(uv.zw) * em_atlas_out.xy + em_atlas_out.zw).rgb;
	}



	// normal
	const float e = 0.2;


	vec3 normal = vNormal; 
	
	vec3 vLightWeighting = vec3( 0.25 ); // ambient

	// Flip normal if it's facing away from viewer to avoid black rendering
	if (dot(normal, normalize(-vertPos)) < 0.0) {
		normal = -normal;
		vLightWeighting = vec3(0);
	}
	

	float distFactor=clamp(3/sqrt(abs(vertPos.z)),0.9,1.1);
	// diffuse light
	// todo: put light computation into cpu to kill excessive computes on shader.
	vec3 lDirection1 =  normalize(viewMatrix * vec4(0.3, 0.3, 1.0, 0.0) + vec4(-0.3,0.3,1.0,0.0)*10).rgb;
	vLightWeighting += clamp(dot( normal, normalize( lDirection1.xyz ) ) * 0.5 + 0.5,0.0,1.5);
	
	vec3 lDirection2 =  normalize(vec3(0.0,0.0,1.0));
	vLightWeighting += clamp(dot( normal, normalize( lDirection2.xyz ) ) * 0.5 + 0.5,0.0,1.5)*distFactor;

	vec4 lDirectionB =  viewMatrix * vec4( normalize( vec3( 0.0, 0.0, -1.0 ) ), 0.0 );
	float bw = dot( normal, normalize( lDirectionB.xyz ) ) * clamp(exp(-vertPos.y*1.0),0.5,1.0)*0.1;
	vec3 blight = vec3( 1.0,0.0,1.0 ) * clamp(bw,0.0,1.0);

	// specular light
	float dirDotNormalHalf = dot( normal, normalize( normalize(lDirection1.xyz) - normalize( vertPos ) ) );
	float dirSpecularWeight_top = 0.0; 
	if ( dirDotNormalHalf >= 0.0 ) 
		dirSpecularWeight_top = pow( dirDotNormalHalf, 180 )*0.8;
		
	float dirDotNormalHalf2 = dot( normal, normalize( normalize(vec3(1.0,-0.5,0.0)) - normalize( vertPos ) ) );
	float dirSpecularWeight_keep = 0.0; 
	if ( dirDotNormalHalf2 >= 0.0 ) 
		dirSpecularWeight_keep = pow( dirDotNormalHalf2, 300 )*1.2;
	 
	// rim light (fresnel)
	float rim = pow(1-abs(dot(normal, normalize(vertPos))),15);  
	vLightWeighting += (dirSpecularWeight_top + rim + dirSpecularWeight_keep) * 0.2 * distFactor;

	// Environment mapping based on env_intensity
	vec3 envColor = vec3(0.0);
	if (env_intensity > 0.0) {
		envColor = computeNormalColor(normal);
		float envLum = dot(baseColor.rgb, vec3(0.299, 0.587, 0.114)) * (0.7 + 0.3 * (1 - env_intensity))
			+ env_intensity * 0.3 * env_intensity;
		baseColor.xyz = envColor * envLum * env_intensity + baseColor.xyz * (1 - env_intensity);
	}

	// output:
	frag_color = vec4( baseColor.xyz + vLightWeighting*0.2-0.4 + blight + emissiveColor, baseColor.w );
	
	// Apply color bias/contrast
	frag_color.rgb = frag_color.rgb * color_bias.w + color_bias.rgb;
	
	// Apply brightness adjustment
	frag_color.rgb = pow(max(frag_color.rgb, vec3(0.001)), vec3(1.0 / brightness));
	
	frag_color.rgb += normal_shading * vNormal;
	
	float luminance = dot(frag_color.rgb, vec3(0.299, 0.587, 0.114));
	float bc_shineFac = pow(luminance/illumrng, illumfac);
	shine = vec4(clamp((frag_color.rgb + 0.2) * (1 + vshine.xyz * vshine.w) - 0.9, 0, 1) + frag_color.rgb * bc_shineFac, 1);

	bordering = vborder;
	g_depth = gl_FragCoord.z;
	out_normal = vec4(vNormal,1.0);

	// add some sparkling glittering effect, make the surface like brushed mica powder 
	float glitter = pow(hash12(gl_FragCoord.yx + vid_hash2.yx), pow(8, clamp((2.3 - vLightWeighting.x) * 10, 0.0, 3.0))) * 0.06 * vLightWeighting.x;
	vec3 glitter_color = hash32(gl_FragCoord.xy + vid_hash2.yx);

	frag_color = vec4(frag_color.xyz + vshine.xyz * vshine.w * 0.2, frag_color.w)
		+ vec4(glitter * glitter_color, 0);
}
@end

@program gltf gltf_vs gltf_fs





@fs wboit_accum_fss
@include_block u_gltf_mats
@include_block computeNormalColor
@include_block fs_header

in vec2 vid_hash2;

out vec4 frag_color;
out float w_accum;
out vec4 emissive;

vec3 hash32(vec2 p) 
{
	vec3 p3 = fract(vec3(p.xyx) * vec3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return fract((p3.xxy + p3.yzz) * p3.zyx);
}

float hash12(vec2 p)
{
	vec3 p3 = fract(vec3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float hash13(vec3 p3)
{
	p3 = fract(p3 * .1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

void main(void) {
	// Add clipping test at start of fragment shader
	if (cs_active_planes > 0 && ((myflag & (1 << 7)) == 0)) {
		bool isBorder = false;
		vec3 ddx = dFdx(vWorld3D);
		vec3 ddy = dFdy(vWorld3D);
		for (int i = 0; i < cs_active_planes; i++) {
			float dist = dot(vWorld3D.xyz - cs_planes[i].xyz, cs_directions[i].xyz);
			if (dist > 0.0) {
				discard;
			}

			float gradientMag = length(vec2(
				dot(ddx, cs_directions[i].xyz),
				dot(ddy, cs_directions[i].xyz)
			));

			if (abs(dist) < gradientMag) {
				isBorder = true;
			}
		}
		if (isBorder) {
			frag_color = cs_color;
			return;
		}
	}

	// Sample base color texture
	vec4 baseColor = color;
	if (uv_atlas.x > 0 && tex_weight_out.x > 0) {
		baseColor = baseColor * texture(t_atlas, fract(uv.xy) * uv_atlas.xy + uv_atlas.zw);
	}
	
	// Sample emissive texture
	vec3 emissiveColor = vec3(0.0);
	if (em_atlas_out.x > 0 && tex_weight_out.y > 0) {
		emissiveColor = texture(t_atlas, fract(uv.zw) * em_atlas_out.xy + em_atlas_out.zw).rgb;
	}

	// normal
	const float e = 0.2;


	vec3 normal = vNormal;

	vec3 vLightWeighting = vec3(0.25); // ambient

	// Flip normal if it's facing away from viewer to avoid black rendering
	if (dot(normal, normalize(-vertPos)) < 0.0) {
		normal = -normal;
		vLightWeighting = vec3(0);
	}


	float distFactor = clamp(3 / sqrt(abs(vertPos.z)), 0.9, 1.1);
	// diffuse light 
	vec3 lDirection1 = normalize(viewMatrix * vec4(0.3, 0.3, 1.0, 0.0) + vec4(-0.3, 0.3, 1.0, 0.0) * 10).rgb;
	vLightWeighting += clamp(dot(normal, normalize(lDirection1.xyz)) * 0.5 + 0.5, 0.0, 1.5);

	vec3 lDirection2 = normalize(vec3(0.0, 0.0, 1.0));
	vLightWeighting += clamp(dot(normal, normalize(lDirection2.xyz)) * 0.5 + 0.5, 0.0, 1.5) * distFactor;

	vec4 lDirectionB = viewMatrix * vec4(normalize(vec3(0.0, 0.0, -1.0)), 0.0);
	float bw = dot(normal, normalize(lDirectionB.xyz)) * clamp(exp(-vertPos.y * 1.0), 0.5, 1.0) * 0.1;
	vec3 blight = vec3(1.0, 0.0, 1.0) * clamp(bw, 0.0, 1.0);

	// specular light
	float dirDotNormalHalf = dot(normal, normalize(normalize(lDirection1.xyz) - normalize(vertPos)));
	float dirSpecularWeight_top = 0.0;
	if (dirDotNormalHalf >= 0.0)
		dirSpecularWeight_top = pow(dirDotNormalHalf, 180) * 0.8;

	float dirDotNormalHalf2 = dot(normal, normalize(normalize(vec3(1.0, -0.5, 0.0)) - normalize(vertPos)));
	float dirSpecularWeight_keep = 0.0;
	if (dirDotNormalHalf2 >= 0.0)
		dirSpecularWeight_keep = pow(dirDotNormalHalf2, 300) * 1.2;

	// rim light (fresnel)
	float rim = pow(1 - abs(dot(normal, normalize(vertPos))), 15);
	vLightWeighting += (dirSpecularWeight_top + rim + dirSpecularWeight_keep) * 0.2 * distFactor;

	// Environment mapping based on env_intensity
	vec3 envColor = vec3(0.0);
	if (env_intensity > 0.0) {
		envColor = computeNormalColor(normal);
		float envLum = dot(baseColor.rgb, vec3(0.299, 0.587, 0.114)) * (0.7 + 0.3 * (1 - env_intensity))
			+ env_intensity * 0.3 * env_intensity;
		emissiveColor.xyz += envColor * envLum * env_intensity + baseColor.xyz * (1 - env_intensity);
	}

	// output:
	frag_color = vec4(baseColor.xyz + vLightWeighting * 0.2 - 0.4 + blight + emissiveColor, baseColor.w);

	// Apply color bias/contrast
	frag_color.rgb = frag_color.rgb * color_bias.w + color_bias.rgb;

	// Apply brightness adjustment
	frag_color.rgb = pow(max(frag_color.rgb, vec3(0.001)), vec3(1.0 / brightness));

	float luminance = dot(frag_color.rgb, vec3(0.299, 0.587, 0.114));

	float bc_shineFac = pow(luminance / illumrng, illumfac);
	// add some sparkling glittering effect, make the surface like brushed mica powder 
	float glitter = pow(hash12(gl_FragCoord.yx + vid_hash2.yx), pow(8, clamp((2.3 - vLightWeighting.x) * 10, 0.0, 3.0))) * 0.06 * vLightWeighting.x;
	vec3 glitter_color = hash32(gl_FragCoord.xy + vid_hash2.yx);

	frag_color = vec4(frag_color.xyz + vshine.xyz * vshine.w * 0.2, frag_color.w)
		+ vec4(glitter * glitter_color, 0);


	// Extract opacity from myflag bits 8-15
	int itransparency = (myflag >> 8) & 0xFF;
	float transparency = float(itransparency) / 255.0;

	float z = gl_FragCoord.z;
	if (frag_color.w > 0.95 && itransparency == 0)
		discard;

	if (frag_color.w < 0.01)
		discard;

	frag_color.a *= (1 - transparency);
	frag_color.w = pow(frag_color.w, 0.5);

	vec4 shine = vec4(clamp(pow(vLightWeighting * 0.2 + blight + emissiveColor + glitter * glitter_color, vec3(2.0)), 0, 1.2) * pow((1 - frag_color.w)*frag_color.w, 0.5), 1.0);

	w_accum = clamp(pow((frag_color.a * 8.0 + 0.001) * (-z + 1.0), 3.0) * 1000, 0.001, 300.0);
	emissive = shine * w_accum;
	frag_color = frag_color * w_accum;
}
@end

@program wboit_accum gltf_vs wboit_accum_fss


@vs wboit_composing_vs
in vec2 position;
out vec2 uv;
void main() {
    gl_Position = vec4(position, 0, 1.0); //0.5,1.0
    uv = position * 0.5 + vec2(0.5, 0.5);
}
@end

@fs wboit_composing_fs
// also compose to screen, use additive blending.
uniform sampler2D wboit_accum;
uniform sampler2D wboit_w_accum;
uniform sampler2D bordering; // 1>>4 is hit.
uniform sampler2D wboit_emissive;
//uniform sampler2D wboit_reveal;
//uniform sampler2D primitive_depth;
//uniform sampler2D color_hi_res1;

in vec2 uv;
out vec4 frag_color;
out vec4 bloom2;

vec2 hash22(vec2 p)
{
	vec3 p3 = fract(vec3(p.xy, fract(p.y)+sin(fract(p.x))) * vec3(.1031, .1030, .0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xy + p3.yz) * p3.zy);
}

void main() {
    vec4 accum = texture(wboit_accum, uv);
	vec4 e = texture(wboit_emissive, uv);
    float w = texture(wboit_w_accum, uv).x;
	int r = int(texture(bordering, uv).x * 255) >> 4;
	
	if (w < 0.0001 || r == 0) discard;

	// === frost glass effect 
	//    alpha=0 no displacement, alpha=0.5 max displacement.
	// vec2 vid_hash2 = uv + vec2(
	// 	sin(hash22(uv * 15.0).x * 6.28),
	// 	cos(hash22(uv * 15.0).y * 6.28)
	// ) * 0.005;

    //float depth = texture(primitive_depth, uv).r;
	//vec4 color = texture(color_hi_res1,uv); // todo: maybe add frost glass effect? +hash22(uv) * 5 / textureSize(color_hi_res1, 0));

    //if (depth < 1.0) {
    //    float opaqueAlpha = color.a = 0.7;
    //    // WBOIT formula. 
    //    float opaqueWeight =
    //        clamp(pow((opaqueAlpha * 8.0 + 0.001) * (-depth + 1.0), 3.0) * 1000, 0.001, 300.0);
	//
    //    accum += color * opaqueWeight;
    //    w += opaqueWeight;
    //}
	w = clamp(w, 0.0001, 50000);
	frag_color = accum / w;
	bloom2 = pow(e / w, vec4(2));

	// diffuse "overlight"
	// float luminance = dot(frag_color.rgb, vec3(0.299, 0.587, 0.114));
	// if (luminance > 0.3) {
	// 	float fac = (1 - (luminance - 0.3) / 0.8);
	// 	fac *= fac;
	// 	frag_color.a = frag_color.a * fac + (1 - fac) * (frag_color.a + 0.3);
	// }
}
@end

@program wboit_compose wboit_composing_vs wboit_composing_fs