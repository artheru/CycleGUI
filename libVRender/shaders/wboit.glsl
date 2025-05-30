@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4


// header:

@include fake_pbr.glslh

@block u_gltf_reveal_mats
uniform gltf_mats_reveal_unused
@include_block udef
@end


// Actual draw:
@vs gltf_vs_reveal

@include_block u_gltf_reveal_mats
//@include_block vs_header

// model related:
uniform sampler2D NImodelViewMatrix;
//uniform sampler2D NInormalMatrix;

uniform sampler2D pernode;   //trans/flag(borner,shine,front,selected?color...)/quat
uniform isampler2D perinstance; //animid/elapsed/shine/flag.

uniform sampler2D skinInvs;
//
//uniform usampler2D morphTargetWeights; //1byte morph?(1byte target(256 targets), 1byte weight)2bytes * 8.
//uniform sampler2D targets; //vec3 buffer. heading 256*(3bytes: vtx_id_st, 3bytes: offset).

uniform usampler2D animap;
uniform sampler2D animtimes;
uniform sampler2D morphdt;

// per vertex
in vec3 position;
//in vec3 normal;
//in vec4 color0;
//in vec4 texcoord0; // uv.xy for base color, uv.zw for emissive
//in vec4 tex_atlas; // base color atlas
//in vec4 em_atlas; // emissive atlas
//in vec2 tex_weight; // .x for basecolor, .y for emissive

in float fnode_id;// vec2 node_metas;
in vec4 fnode_info; //<255.

in vec4 joints;
in vec4 jointNodes;
in vec4 weights;


// outputs:
//out vec4 color;
//out vec3 vNormal;
out vec3 vWorld3D;
//out vec3 vertPos;
//out vec4 uv; // uv.xy for base color, uv.zw for emissive
//flat out vec4 uv_atlas;
//flat out vec4 em_atlas_out;
//flat out vec2 tex_weight_out;
flat out vec4 vid;
flat out int vborder;
//flat out vec4 vshine;
//flat out float env_intensity; // Environment intensity from material
flat out int myflag;

@include_block bsearch

void main() {
	int node_id = int(fnode_id);
	int skin_idx = int(fnode_info.x);

	int instid = gl_InstanceIndex + instance_index_offset;
	int noff = max_instances * node_id + instid + offset;

	int obj_id = instid + obj_offset;

	// 0:borner? 1:shine? 2:front?,3:selected?
	vec4 shine = vec4(0);
	vborder = 0;

	ivec4 objmeta = texelFetch(perinstance, ivec2(obj_id % 4096, obj_id / 4096), 0);
	int nodeflag = int(texelFetch(pernode, ivec2((noff % 2048) * 2, (noff / 2048)), 0).w);
	myflag = int(objmeta.w);

	bool selected = (myflag & (1 << 3)) != 0 || (nodeflag & (1 << 3)) != 0;
	bool hovering = instid == hover_instance_id &&
		(((myflag & (1 << 4)) != 0) || hover_node_id == node_id && ((myflag & (1 << 5)) != 0));

	// todo: optimize the below code.
	if (hovering && selected) {
		vborder = 2;
		shine = hover_shine_color_intensity * 0.8 + selected_shine_color_intensity * 0.5;
	}
	else if (hovering) {
		vborder = 1;
		shine = hover_shine_color_intensity;
	}
	else if (selected) {
		vborder = 2;
		shine = selected_shine_color_intensity;
	}
	else {
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

	//vshine = shine;
	// we put vborder scaling in the end.

	mat4 modelViewMatrix;
	mat3 normalMatrix;
	vid = vec4(1000 + class_id, instid / 16777216, instid % 16777216, int(node_id));

	if (skin_idx == 255) {
		int get_id = max_instances * int(node_id) + instid + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}

		int x = (get_id % 1024) * 2;
		int y = (get_id / 1024) * 2;

		modelViewMatrix = mat4(
			texelFetch(NImodelViewMatrix, ivec2(x, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x, y + 1), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y), 0),
			texelFetch(NImodelViewMatrix, ivec2(x + 1, y + 1), 0));

		//normalMatrix = mat3(
		//	texelFetch(NInormalMatrix, ivec2(x, y), 0).rgb,
		//	texelFetch(NInormalMatrix, ivec2(x, y + 1), 0).rgb,
		//	texelFetch(NInormalMatrix, ivec2(x + 1, y), 0).rgb);
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
		//normalMatrix = transpose(inverse(mat3(modelViewMatrix)));
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


	vec4 mPosition = modelViewMatrix * vec4(mypos, 1.0);
	//vNormal = normalize(normalMatrix * normal);
	//vertPos = mPosition.xyz / mPosition.w; // camera coordination.

	if (cs_active_planes > 0 && ((myflag & (1 << 7)) == 0)) {
		vec4 tmp = iv * mPosition;
		vWorld3D = vec3(tmp);
	}

	gl_Position = projectionMatrix * mPosition;// modelViewMatrix* vec4(mypos, 1.0);

	//"move to front" displaying paramter processing.
	if ((myflag & 4) != 0 || (nodeflag & 4) != 0 || hovering && (display_options & 1) != 0) {
		gl_Position.z -= gl_Position.w;
	}
	//color = color0;
	//uv = texcoord0;
	//uv_atlas = tex_atlas;
	//em_atlas_out = em_atlas;
	//tex_weight_out = tex_weight;
	//env_intensity = fnode_info.y / 127.0; // Convert from 0-127 range to 0-1 range
}
@end




@fs wboit_reveal_fs

@include_block u_gltf_reveal_mats
@include_block computeNormalColor
//@include_block fs_header

uniform sampler2D t_atlas;

//in vec4 color;
//in vec3 vNormal;
in vec3 vWorld3D; // only using if use cross section.
//in vec3 vertPos;
//in vec4 uv; // uv.xy for base color, uv.zw for emissive
//flat in vec4 uv_atlas;
//flat in vec4 em_atlas_out;
//flat in vec2 tex_weight_out;
flat in vec4 vid;
flat in int vborder;
//flat in vec4 vshine;
//flat in float env_intensity; // Environment intensity from material
flat in int myflag;

layout(location = 0) out float g_depth;
layout(location = 1) out vec4 screen_id;
layout(location = 2) out float bordering;

void main(void) {
	// Add clipping test at start of fragment shader
	if (cs_active_planes > 0 && ((myflag & (1 << 7)) == 0)) {
		for (int i = 0; i < cs_active_planes; i++) {
			float dist = dot(vWorld3D.xyz - cs_planes[i].xyz, cs_directions[i].xyz);
			if (dist > 0.0) {
				discard;
			}
		}
	}

	screen_id = vid;
	bordering = vborder | (1 << 4);
	bordering /= 255;
}
@end

@program wboit_reveal gltf_vs_reveal wboit_reveal_fs
