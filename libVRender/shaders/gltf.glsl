@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4



///////////////////////////////////////////
// Hierarchically compute node's model matrix on gpu.
@vs vs_hierarchical_mat
uniform hierarchical_uniforms{ // 64k max.
	int max_nodes;
	int max_instances;
	//int depth;
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
	int my_id = max_instances * nid + gl_InstanceIndex + offset;
	int write_id = my_id;
	modelView = getMat(my_id);

	int w = textureSize(parents, 0).x;
	//int zw = int(textureSize(nm, 0).x/512.0);
	for (int i=0; i<4; ++i){
		int fetch_pos = pass_n * max_nodes + nid;
		int fx = fetch_pos % w;
		int fy = fetch_pos / w;
		nid = int(texelFetch(parents, ivec2(fx, fy), 0).r);
		if (nid == -1) break;

		my_id = max_instances * nid + gl_InstanceIndex + offset;
		modelView = getMat(my_id) * modelView;
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

float random(vec2 uv) { return fract(sin(dot(uv.xy, vec2(12.9898, 78.233))) * 43758.5453); }

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

////////////////////////////////////////////////
// Actual Draw:

@vs gltf_vs
uniform gltf_mats{
	mat4 projectionMatrix, viewMatrix;
	int max_instances;
	int offset;
	int node_amount;
    int class_id;

	int obj_offset;
	
	// ui ops:
    int hover_instance_id; //only this instance is hovered.
    int hover_node_id; //-1 to select all nodes.
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity

	int display_options;
		// 0: bring to front if hovering.

	float time;
};


// model related:
uniform sampler2D NImodelViewMatrix;
uniform sampler2D NInormalMatrix;

uniform sampler2D pernode;   //trans/flag(borner,shine,front,selected?color...)/quat
uniform usampler2D perinstance; //animid/elapsed/shine/flag.

uniform sampler2D skinInvs;
//
//uniform usampler2D morphTargetWeights; //1byte morph?(1byte target(256 targets), 1byte weight)2bytes * 8.
//uniform sampler2D targets; //vec3 buffer. heading 256*(3bytes: vtx_id_st, 3bytes: offset).

uniform usampler2D animap;
uniform sampler2D animtimes;
uniform sampler2D morphdt;

// per vertex
in vec3 position;
in vec3 normal;
in vec4 color0;
in vec2 texcoord0;
in vec2 node_metas;

in vec4 joints;
in vec4 jointNodes;
in vec4 weights;

//in float vtx_id;

out vec4 color;
out vec3 vNormal;
out vec3 vTexCoord3D;
out vec3 vertPos;
out vec2 uv;

flat out vec4 vid;

flat out float vborder;
flat out vec4 vshine;

ivec2 bsearch(uint loIdx, uint hiIdx, ivec2 texSize, float elapsed) {
	while (loIdx < hiIdx - 1) {
		uint midIdx = loIdx + (hiIdx - loIdx) / 2; // Calculate mid index
		float midVal = texelFetch(animtimes, ivec2(midIdx % texSize.x, midIdx / texSize.x), 0).r;
		if (midVal < elapsed) {
			loIdx = midIdx;
		}
		else {
			hiIdx = midIdx;
		}
	}
	return ivec2(loIdx, hiIdx);
}

void main() {
	int node_id = int(node_metas.x);
	int noff = max_instances * node_id + gl_InstanceIndex + offset;

	int skin_idx = int(node_metas.y);

	int obj_id = gl_InstanceIndex + obj_offset;
	
	// 0:corner? 1:shine? 2:front?,3:selected?
	vec4 shine=vec4(0);
	vborder = 0;
	
	uvec4 objmeta = texelFetch(perinstance, ivec2(obj_id % 4096, obj_id / 4096), 0);
	int nodeflag = int(texelFetch(pernode, ivec2((noff % 2048) * 2, (noff / 2048)), 0).w);
	int myflag = int(objmeta.w);

	bool selected = (myflag & (1 << 3)) != 0 || (nodeflag & (1 << 3)) != 0;	
	bool hovering = gl_InstanceIndex == hover_instance_id && 
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

	vborder /= 16.0; //stupid webgl...
	vshine = shine;

	mat4 modelViewMatrix;
	mat3 normalMatrix;
	vid = vec4(1000 + class_id, gl_InstanceIndex / 16777216, gl_InstanceIndex % 16777216, int(node_id));

	if (skin_idx <0) {
		int get_id=max_instances*int(node_id) + gl_InstanceIndex + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}

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
		int get_id = max_instances * int(jointNodes.x) + gl_InstanceIndex + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
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

		get_id = max_instances * int(jointNodes.y) + gl_InstanceIndex + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
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

		get_id = max_instances * int(jointNodes.z) + gl_InstanceIndex + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
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

		get_id = max_instances * int(jointNodes.w) + gl_InstanceIndex + offset; //instance{node0 node0 ...(maxinstance)|node1 ...(maxinstance)|...}
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
	vertPos = mPosition.xyz/mPosition.w;

	vTexCoord3D = 0.1 * ( mypos.xyz + vec3( 0.0, 1.0, 1.0 ) );
	gl_Position = projectionMatrix * modelViewMatrix * vec4( mypos, 1.0 );

	//"move to front" displaying paramter processing.
	if ((myflag & 4) != 0 || (nodeflag & 4) != 0 || hovering && (display_options & 1) != 0) {
		gl_Position.z -= gl_Position.w;
	}

	
    color = color0;
	uv = texcoord0;
}
@end

@fs gltf_fs

uniform gltf_mats{
	mat4 projectionMatrix, viewMatrix;
	int max_instances;
	int offset;
	int node_amount;
    int class_id;

	int obj_offset;

	// ui ops:
    int hover_instance_id; //only this instance is hovered.
    int hover_node_id; //-1 to select all nodes.
    vec4 hover_shine_color_intensity; //vec3 shine rgb + shine intensity
    vec4 selected_shine_color_intensity; //vec3 shine rgb + shine intensity

	int display_options;
	
	float time;
};

uniform sampler2D t_baseColor;

in vec4 color;
in vec3 vNormal;
in vec3 vTexCoord3D;
in vec3 vertPos;
in vec2 uv;

flat in vec4 vid;

flat in float vborder;
flat in vec4 vshine;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth; // the fuck. blending cannot be closed?
layout(location=2) out vec4 out_normal;
layout(location=3) out vec4 screen_id;

layout(location=4) out float bordering;
layout(location=5) out vec4 shine;


vec4 permute( vec4 x ) {

	return mod( ( ( x * 34.0 ) + 1.0 ) * x, 289.0 );

}

vec4 taylorInvSqrt( vec4 r ) {

	return 1.79284291400159 - 0.85373472095314 * r;

}

float snoise( vec3 v ) {

	const vec2 C = vec2( 1.0 / 6.0, 1.0 / 3.0 );
	const vec4 D = vec4( 0.0, 0.5, 1.0, 2.0 );

	// First corner

	vec3 i  = floor( v + dot( v, C.yyy ) );
	vec3 x0 = v - i + dot( i, C.xxx );

	// Other corners

	vec3 g = step( x0.yzx, x0.xyz );
	vec3 l = 1.0 - g;
	vec3 i1 = min( g.xyz, l.zxy );
	vec3 i2 = max( g.xyz, l.zxy );

	//  x0 = x0 - 0. + 0.0 * C
	vec3 x1 = x0 - i1 + 1.0 * C.xxx;
	vec3 x2 = x0 - i2 + 2.0 * C.xxx;
	vec3 x3 = x0 - 1. + 3.0 * C.xxx;

	// Permutations

	i = mod( i, 289.0 );
	vec4 p = permute( permute( permute(
			 i.z + vec4( 0.0, i1.z, i2.z, 1.0 ) )
		   + i.y + vec4( 0.0, i1.y, i2.y, 1.0 ) )
		   + i.x + vec4( 0.0, i1.x, i2.x, 1.0 ) );

	// Gradients
	// ( N*N points uniformly over a square, mapped onto an octahedron.)

	float n_ = 1.0 / 7.0; // N=7

	vec3 ns = n_ * D.wyz - D.xzx;

	vec4 j = p - 49.0 * floor( p * ns.z *ns.z );  //  mod(p,N*N)

	vec4 x_ = floor( j * ns.z );
	vec4 y_ = floor( j - 7.0 * x_ );    // mod(j,N)

	vec4 x = x_ *ns.x + ns.yyyy;
	vec4 y = y_ *ns.x + ns.yyyy;
	vec4 h = 1.0 - abs( x ) - abs( y );

	vec4 b0 = vec4( x.xy, y.xy );
	vec4 b1 = vec4( x.zw, y.zw );

	vec4 s0 = floor( b0 ) * 2.0 + 1.0;
	vec4 s1 = floor( b1 ) * 2.0 + 1.0;
	vec4 sh = -step( h, vec4( 0.0 ) );

	vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
	vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

	vec3 p0 = vec3( a0.xy, h.x );
	vec3 p1 = vec3( a0.zw, h.y );
	vec3 p2 = vec3( a1.xy, h.z );
	vec3 p3 = vec3( a1.zw, h.w );

	// Normalise gradients

	vec4 norm = taylorInvSqrt( vec4( dot(p0,p0), dot(p1,p1), dot(p2, p2), dot(p3,p3) ) );
	p0 *= norm.x;
	p1 *= norm.y;
	p2 *= norm.z;
	p3 *= norm.w;

	// Mix final noise value

	vec4 m = max(0.6 - vec4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3) ), 0.0 );
	m = m * m;
	return 42.0 * dot( m*m, vec4( dot(p0,x0), dot(p1,x1),
								dot(p2,x2), dot(p3,x3) ) );

}

float heightMap( vec3 coord ) {

	float n = abs( snoise( coord ) );

	n += 0.25   * abs( snoise( coord * 2.0 ) );
	n += 0.25   * abs( snoise( coord * 4.0 ) );
	n += 0.125  * abs( snoise( coord * 8.0 ) );
	n += 0.0625 * abs( snoise( coord * 16.0 ) );

	return n;

}

void main( void ) {

	screen_id = vid;
	// height

	vec3 noiser = vec3(time * 0.001, -time * 0.001, sin(time*0.001));
	float n = heightMap( vTexCoord3D + noiser )*0.1;

	// color

	vec3 baseColor = color.rgb * vec3( color.r - n*0.3, color.g - n *0.4, color.b - n*0.8 );

	// normal

	const float e = 0.2;

	float nx = heightMap( vTexCoord3D + noiser + vec3( e, 0.0, 0.0 ) );
	float ny = heightMap( vTexCoord3D + noiser + vec3( 0.0, e, 0.0 ) );
	float nz = heightMap( vTexCoord3D + noiser + vec3( 0.0, 0.0, e ) );

	vec3 normal = normalize( vNormal - 0.005 * vec3( n - nx, n - ny, n - nz ) / e ); //constrast
	
	vec3 vLightWeighting = vec3( 0.25 ); // ambient

	float distFactor=clamp(3/sqrt(abs(vertPos.z)),0.8,1.2);
	// diffuse light 
	vec3 lDirection1 =  normalize(viewMatrix * vec4(0.3, 0.3, 1.0, 0.0) + vec4(-0.3,0.3,1.0,0.0)*10).rgb;
	vLightWeighting += clamp(dot( normal, normalize( lDirection1.xyz ) ) * 0.5 + 0.5,0.0,1.5);
	
	vec3 lDirection2 =  normalize(vec3(0.0,0.0,1.0));
	vLightWeighting += clamp(dot( normal, normalize( lDirection2.xyz ) ) * 0.5 + 0.5,0.0,1.5)*distFactor;

	vec4 lDirectionB =  viewMatrix * vec4( normalize( vec3( 0.0, 0.0, -1.0 ) ), 0.0 );
	float bw = dot( normal, normalize( lDirectionB.xyz ) ) * clamp(exp(-vertPos.y*1.0),0.0,1.0)*0.1;
	vec3 blight = vec3( 1.0,0.0,1.0 ) * clamp(bw,0.0,1.0);

	// specular light
	float dirDotNormalHalf = dot( normal, normalize( normalize(lDirection1.xyz) - normalize( vertPos ) ) );
	float dirSpecularWeight_top = 0.0; 
	if ( dirDotNormalHalf >= 0.0 ) 
		dirSpecularWeight_top = pow( dirDotNormalHalf, 160 )*0.7;
		
	float dirDotNormalHalf2 = dot( normal, normalize( normalize(vec3(1.0,-0.5,0.0)) - normalize( vertPos ) ) );
	float dirSpecularWeight_keep = 0.0; 
	if ( dirDotNormalHalf2 >= 0.0 ) 
		dirSpecularWeight_keep = pow( dirDotNormalHalf2, 300 );

	// rim light (fresnel)
	float rim = pow(1-abs(dot(normal, normalize(vertPos))),15);

	vLightWeighting += (dirSpecularWeight_top + rim + dirSpecularWeight_keep) * (0.5+vec3(nx,ny,nz)) * 0.4 * distFactor;

	// output:
	frag_color = vec4( baseColor + (vLightWeighting - 1.5)*0.5 + blight, 1.0 );
	g_depth = gl_FragCoord.z;
	out_normal = vec4(vNormal,1.0);
	
	if (uv.x >= 0)
		frag_color = frag_color*texture(t_baseColor, uv);

	frag_color = vec4(frag_color.xyz + vshine.xyz*vshine.w * 0.2, frag_color.w);
	shine = vec4((frag_color.xyz+0.2)*(1+vshine.xyz*vshine.w) - 1.0, 1);

	bordering = vborder;

}
@end

@program gltf gltf_vs gltf_fs


@vs blending_quad
in vec2 pos;

void main() {
    gl_Position = vec4(pos*2.0-1.0, 0.5, 1.0);
}
@end

@fs ssao_fs

// this is actually SAO(scalable ambient occulusion)
    uniform SSAOUniforms {
		mat4 P,iP, iV;
		vec3 cP;

		float weight;
        float uSampleRadius; //16
        float uBias; //0.04
        vec2 uAttenuation; //(1,1)
        vec2 uDepthRange; //(NEAR,FAR)
		
		float time;
		float useFlag;
    };

    uniform sampler2D uDepth;
    uniform sampler2D uNormalBuffer;
        
    out float occlusion;

	float perspectiveDepthToViewZ( const in float depth, const in float near, const in float far ) {
		// maps perspective depth in [ 0, 1 ] to viewZ
		float dd = depth > 0.5 ? depth : depth + 0.5;
		return ( near * far ) / ( ( far - near ) * dd - far );
	}

	float getViewZ( const in float depth ) {
		return perspectiveDepthToViewZ( depth, uDepthRange.x, uDepthRange.y );
	}

	vec4 getPosition(ivec2 fragCoord){
		float depthValue = texelFetch(uDepth, fragCoord, 0).r;
		ivec2 uDepthSize = textureSize(uDepth, 0);
		vec2 ndc = (2.0 * vec2(fragCoord) / uDepthSize) - 1.0;
		
		if (depthValue == 1.0) { // test ground plane.
			vec4 clipSpacePosition = vec4(ndc, -1.0, 1.0);
			vec4 eye = iP * clipSpacePosition;
			vec3 world_ray_dir = normalize((iV * vec4(eye.xyz, 0.0)).xyz);

			if (world_ray_dir.z != 0) {
				float t = -cP.z / world_ray_dir.z;
				if (t >= 0) {
					return vec4(cP + t * world_ray_dir, 1);
				}
			}
			return vec4(0);
		}

		float viewZ = getViewZ(depthValue);
		float clipW = P[2][3] * viewZ + P[3][3];

		vec4 clipPosition = vec4(vec3((2.0 * vec2(fragCoord) / uDepthSize) - 1 , 2 * depthValue - 1), 1.0);
		//clipPosition *= clipW; // unprojection.
		return vec4((iP * clipPosition).xyz * clipW, 1);
	}

	float getOcclusion(vec3 position, vec3 normal, ivec2 fragCoord) {
		vec4 ipos = getPosition(fragCoord);
		if (ipos.w == 0) return 0;
		vec3 occluderPosition = ipos.xyz;
		vec3 positionVec = occluderPosition - position;
		float len = length(positionVec);
		if (len < 0.001 || len > 0.5) return 0;
		float intensity = max(dot(normal, normalize(positionVec)) - uBias, 0.0);
		float attenuation = 1.0 / (uAttenuation.x + uAttenuation.y * length(positionVec));
		//return dot(normalize(normal), normalize(positionVec));
		return intensity * attenuation;

		// vec3 occluderPosition = getPosition(fragCoord).xyz;
		// vec3 positionVec = occluderPosition - position;
		// float intensity = max(dot(normal, normalize(positionVec)) - uBias, 0.0);
		// float attenuation = 1.0 / (uAttenuation.x + uAttenuation.y * length(positionVec));
		// return intensity * attenuation;
	}

#define SIN45 0.707107
#define ITERS 24

    void main() {
		int useFlagi = int(useFlag);
		bool useGround = bool(useFlagi & 4);

		float pix_depth = texelFetch(uDepth, ivec2(gl_FragCoord.xy), 0).r;

        ivec2 fragCoord = ivec2(gl_FragCoord.xy);
		vec4 ipos = getPosition(fragCoord);
		if (ipos.w == 0) {
			discard;
		}
        vec3 position = ipos.xyz; // alrady gets ground plane.
        vec3 normal = texelFetch(uNormalBuffer, fragCoord, 0).xyz;

		float oFac=weight;

		if (pix_depth == 1.0) {
			normal = vec3(0.0, 0.0, -1.0);
			oFac *= 2;
		}

		vec2 noise = fract((sin(6.28 * vec2(
			fract(dot(fragCoord.xy, vec2(12.9898, 78.233))), 
			fract(dot(fragCoord.xy, vec2(39.789, 102.734))) 
		))) * 135.12852);
        vec2 rand = vec2(1.0, 0) + noise*0.3;
		//vec2 rand = vec2(1.0, 0);
        float depth = getViewZ(pix_depth);
	
		vec2 k1 = rand * uSampleRadius / (depth + 0.001);

        occlusion = 0.0;
        for (int i = 0; i < 24; ++i) {
			k1 = vec2(k1.x * SIN45 - k1.y * SIN45, k1.x * SIN45 + k1.y * SIN45) * 1.06;
            occlusion += getOcclusion(position, normal, fragCoord + ivec2(k1));
        }
		
        occlusion = clamp(occlusion / ITERS, 0.0, 1.0) * oFac;
    }
@end

@program ssao blending_quad ssao_fs