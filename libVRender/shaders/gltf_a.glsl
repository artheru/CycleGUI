@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

///////////////////////////////////////////
// compute node's local matrix (animation)
@vs vs_node_local_mat
uniform animator{ // 64k max.
	int max_instances;
	int instance_offset;
	int node_amount;
	int offset;
	mat4 viewMatrix;
	int flags; //bit 0: use animation.
};

uniform sampler2D transrot;
uniform isampler2D parents;

uniform usampler2D animmeta; //what animation this instance is animating.
uniform usampler2D animap;
uniform sampler2D animtimes;
uniform sampler2D animdt;

layout(location = 0) in vec3 itranslation;
layout(location = 1) in vec4 irotation;
layout(location = 2) in vec3 iscale;

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

mat4 generateMat4(vec3 translation, vec4 rotation, vec3 scale) {
	// Convert quaternion to a rotation matrix
	float qx = rotation.x;
	float qy = rotation.y;
	float qz = rotation.z;
	float qw = rotation.w;

	mat4 rotMatrix = mat4(
		1.0 - 2.0 * qy * qy - 2.0 * qz * qz, 2.0 * qx * qy + 2.0 * qz * qw, 2.0 * qx * qz - 2.0 * qy * qw, 0.0,
		2.0 * qx * qy - 2.0 * qz * qw, 1.0 - 2.0 * qx * qx - 2.0 * qz * qz, 2.0 * qy * qz + 2.0 * qx * qw, 0.0,
		2.0 * qx * qz + 2.0 * qy * qw, 2.0 * qy * qz - 2.0 * qx * qw, 1.0 - 2.0 * qx * qx - 2.0 * qy * qy, 0.0,
		0,0,0, 1.0
	);

	// Apply scale to the rotation matrix
	rotMatrix[0] *= scale.x;
	rotMatrix[1] *= scale.y;
	rotMatrix[2] *= scale.z;

	// Set the translation
	rotMatrix[3] = vec4(translation, 1.0);

	return rotMatrix;
}

flat out mat4 modelView;

ivec2 bsearch(uint loIdx, uint hiIdx, ivec2 texSize, float elapsed) {
	while (loIdx <= hiIdx) {
		uint midIdx = loIdx + (hiIdx - loIdx) / 2; // Calculate mid index
		float midVal = texelFetch(animtimes, ivec2(midIdx % texSize.x, midIdx / texSize.x), 0).r;
		if (midVal < elapsed) {
			loIdx = midIdx + 1;
		}
		else {
			hiIdx = midIdx - 1;
		}
	}
	return ivec2(loIdx, hiIdx);
}

vec4 slerp(vec4 prev, vec4 next, float keyframe) {
	float dotProduct = dot(prev, next);

	// Make sure we take the shortest path in case dot product is negative
	if (dotProduct < 0.0) {
		next = -next;
		dotProduct = -dotProduct;
	}

	// If the two quaternions are too close, perform linear interpolation
	if (dotProduct > 0.9995)
		return normalize((1.0 - keyframe) * prev + keyframe * next);

	// Perform the spherical linear interpolation
	float theta0 = acos(dotProduct);
	float theta = keyframe * theta0;
	float sinTheta = sin(theta);
	float sinTheta0Inv = 1.0 / (sin(theta0) + 1e-6);

	float scalePrevQuat = cos(theta) - dotProduct * sinTheta * sinTheta0Inv;
	float scaleNextQuat = sinTheta * sinTheta0Inv;
	return scalePrevQuat * prev + scaleNextQuat * next;
}
//vec4 slerp(vec4 q1, vec4 q2, float t) {
//	// Calculate the cosine of the angle between the two quaternions
//	float cosHalfTheta = dot(q1, q2);
//
//	// If q1=q2 or q1=-q2 then theta = 0 and we can return q1
//	if (abs(cosHalfTheta) >= 1.0) {
//		return q1;
//	}
//
//	// Calculate temporary values
//	float halfTheta = acos(cosHalfTheta);
//	float sinHalfTheta = sqrt(1.0 - cosHalfTheta * cosHalfTheta);
//
//	// If theta = 180 degrees then result is not fully defined
//	// We could rotate around any axis normal to q1 or q2
//	if (abs(sinHalfTheta) < 0.001) {
//		return (q1 * 0.5 + q2 * 0.5);
//	}
//
//	float ratioA = sin((1 - t) * halfTheta) / sinHalfTheta;
//	float ratioB = sin(t * halfTheta) / sinHalfTheta;
//
//	// Calculate the interpolated quaternion
//	return (q1 * ratioA + q2 * ratioB);
//}

vec4 normalizeQuat(vec4 q) {
	float length = sqrt(dot(q, q));
	return q / length;
}

void main() {
	int nid = int(gl_VertexIndex);
	int noff = max_instances * int(gl_VertexIndex) + gl_InstanceIndex + offset;

	// xy samples "per_node_transrot"!
	int x = (noff % 2048) * 2; //width=4096
	int y = (noff / 2048); //2048 elements per row..
	vec3 translation = texelFetch(transrot, ivec2(x, y), 0).xyz;
	vec4 quat = texelFetch(transrot, ivec2(x + 1, y), 0);

	vec3 t = itranslation;
	vec4 r = irotation;
	vec3 s = iscale;

	// animation part:
	if ((flags & 1) != 0) {
		int iid = gl_InstanceIndex + instance_offset;
		uvec2 tmp = texelFetch(animmeta, ivec2(iid % 4096, iid / 4096), 0).xy;
		uint animationId = tmp.x;
		if (animationId >= 0) {
			float elapsed = tmp.y / 1000.0f;
			uint wid = animationId * node_amount + nid;
			uvec4 t_anim = texelFetch(animap, ivec2((wid % 512) * 4, wid / 512), 0);
			uvec4 s_anim = texelFetch(animap, ivec2((wid % 512) * 4 + 1, wid / 512), 0);
			uvec4 r_anim = texelFetch(animap, ivec2((wid % 512) * 4 + 2, wid / 512), 0);

			ivec2 texSize = textureSize(animtimes, 0); // Get texture size to determine array length
			ivec2 texSizeDt = textureSize(animdt, 0); // Get texture size to determine array length

			if (t_anim.w > 0) {
				// has translation.
				ivec2 bs = bsearch(t_anim.z, t_anim.z + t_anim.y, texSize, elapsed);
				float loTime = texelFetch(animtimes, ivec2(bs.x % texSize.x, bs.x / texSize.x), 0).r;
				float hiTime = texelFetch(animtimes, ivec2(bs.y % texSize.x, bs.y / texSize.x), 0).r;
				uint data_idx1 = t_anim.x + bs.x - t_anim.z;
				vec3 datalo = texelFetch(animdt, ivec2(data_idx1 % texSizeDt.x, data_idx1 / texSizeDt.x), 0).rgb;
				uint data_idx2 = t_anim.x + bs.y - t_anim.z;
				vec3 datahi = texelFetch(animdt, ivec2(data_idx2 % texSizeDt.x, data_idx2 / texSizeDt.x), 0).rgb;
				// linear interpolation:
				float tD = hiTime - loTime;
				if (tD == 0) {}
				else t = ((elapsed - loTime) * datahi + (hiTime - elapsed) * datalo) / tD;
			}
			if (s_anim.w > 0) {
				// has scale.
				ivec2 bs = bsearch(s_anim.z, s_anim.z + s_anim.y, texSize, elapsed);
				float loTime = texelFetch(animtimes, ivec2(bs.x % texSize.x, bs.x / texSize.x), 0).r;
				float hiTime = texelFetch(animtimes, ivec2(bs.y % texSize.x, bs.y / texSize.x), 0).r;
				uint data_idx1 = s_anim.x + bs.x - s_anim.z;
				vec3 datalo = texelFetch(animdt, ivec2(data_idx1 % texSizeDt.x, data_idx1 / texSizeDt.x), 0).xyz;
				uint data_idx2 = s_anim.x + bs.y - s_anim.z;
				vec3 datahi = texelFetch(animdt, ivec2(data_idx2 % texSizeDt.x, data_idx2 / texSizeDt.x), 0).xyz;
				// linear interpolation:
				float tD = hiTime - loTime;
				if (tD == 0) {}
				else s = ((elapsed - loTime) * datalo + (hiTime - elapsed) * datahi) / tD;
			}
			if (r_anim.w > 0) {
				// has rotation.
				ivec2 bs = bsearch(r_anim.z, r_anim.z + r_anim.y, texSize, elapsed);
				float loTime = texelFetch(animtimes, ivec2(bs.x % texSize.x, bs.x / texSize.x), 0).r;
				float hiTime = texelFetch(animtimes, ivec2(bs.y % texSize.x, bs.y / texSize.x), 0).r;
				uint data_idx1 = r_anim.x + bs.x - r_anim.z;
				vec4 datalo = texelFetch(animdt, ivec2(data_idx1 % texSizeDt.x, data_idx1 / texSizeDt.x), 0);
				uint data_idx2 = r_anim.x + bs.y - r_anim.z;
				vec4 datahi = texelFetch(animdt, ivec2(data_idx2 % texSizeDt.x, data_idx2 / texSizeDt.x), 0);
				// linear interpolation:
				float tD = hiTime - loTime;
				if (tD == 0){}
				else {
					//r = slerp(datalo, datahi, (elapsed - loTime) / tD);
					//r = normalizeQuat(datalo);
					vec4 normQuatLo = normalizeQuat(datalo);
					vec4 normQuatHi = normalizeQuat(datahi);
					// 
					// // Perform Spherical Linear Interpolation (SLERP)
					r = normalizeQuat(slerp(normQuatLo, normQuatHi, (elapsed - loTime) / tD));
				}
			}
		}
	}

	mat4 omat = generateMat4(t, r, s);
	mat4 local = mat4_cast(quat, translation) * omat;


	int w = textureSize(parents, 0).x;
	int fx = nid % w;
	int fy = nid / w;
	int parent = int(texelFetch(parents, ivec2(fx, fy), 0).r);

	if (parent == -1)
		local = viewMatrix * local;

	modelView = local;
	// if (final == 1)
	// 	iModelView = transpose(inverse(modelView));

	// if no animation? use getLocal result.
	gl_PointSize = 2;
	x = noff % 1024;
	y = noff / 1024;
	gl_Position = vec4((x + 0.5) / 512 - 1.0, (y + 0.5) / 512 - 1.0, 0, 1);
}
@end

@fs fs_write_mat4a

flat in mat4 modelView;

out vec4 NImodelViewMatrix;

void main() {
	ivec2 uv = ivec2(gl_FragCoord.xy - ivec2(gl_FragCoord / 2) * 2);
	int n = uv.x * 2 + uv.y;
	NImodelViewMatrix = modelView[n];
}

@end


@program compute_node_local_mat vs_node_local_mat fs_write_mat4a