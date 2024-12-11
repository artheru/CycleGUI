@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

@vs blending_quad
in vec2 pos;
out vec2 uv;
void main() {
    gl_Position = vec4(pos*2.0-1.0, 0.5, 1.0);
	uv = pos;
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
		
		float useFlag;
    };

	// from shadertoy https://www.shadertoy.com/view/4djSRW
	vec2 hash23(vec3 p3)
	{
		p3 = fract(p3 * vec3(.1031, .1030, .0973));
		p3 += dot(p3, p3.yzx+33.33);
		return fract((p3.xx+p3.yz)*p3.zy);
	}
	vec2 hash22(vec2 p)
	{
		vec3 p3 = fract(vec3(p.xyx) * vec3(.1031, .1030, .0973));
		p3 += dot(p3, p3.yzx+33.33);
		return fract((p3.xx+p3.yz)*p3.zy);
	}
	float hash12(vec2 p)
	{
		vec3 p3  = fract(vec3(p.xyx) * .1031);
		p3 += dot(p3, p3.yzx + 33.33);
		return fract((p3.x + p3.y) * p3.z);
	}

    uniform sampler2D uDepth;  
    uniform sampler2D uNormalBuffer;
        
	in vec2 uv;
    out float occlusion;

	float perspectiveDepthToViewZ( const in float depth, const in float near, const in float far ) {
		// maps perspective depth in [ 0, 1 ] to viewZ
		float dd = depth > 0.5 ? depth : depth + 0.5;
		return ( near * far ) / ( ( far - near ) * dd - far );
	}

	float getViewZ( const in float depth ) {
		return perspectiveDepthToViewZ( depth, uDepthRange.x, uDepthRange.y );
	}

	vec4 getPosition(vec2 uv){
		float depthValue = texture(uDepth, uv).r;
		vec2 ndc = (2.0 * uv) - 1.0;
		
		if (depthValue < 0 || depthValue==1.0) return vec4(0);

		float viewZ = getViewZ(depthValue);
		float clipW = P[2][3] * viewZ + P[3][3];

		vec4 clipPosition = vec4(vec3(uv , 2 * depthValue - 1), 1.0);
		return vec4((iP * clipPosition).xyz * clipW, 1);
	}

	float getOcclusion(vec3 position, vec3 normal, vec2 uv) {
		vec4 ipos = getPosition(uv);
		if (ipos.w == 0) return 0;
		vec3 occluderPosition = ipos.xyz;
		vec3 positionVec = occluderPosition - position;
		float len = max(length(positionVec), 0.0001);
		float intensity = max(dot(normal, positionVec / len) - uBias, 0.0);
		float attenuation = clamp(len < 0.01 ? len / 0.01 : len>0.5 ? (0.6 - len) / 0.1 : 1, 0, 1);
		return intensity * attenuation;
	}

#define SIN40 0.64278760968653932632264340990726
#define COS40 0.76604444311897803520239265055542
#define ITERS 16

    void main() {
		int useFlagi = int(useFlag);
		bool useGround = bool(useFlagi & 4);
		
		float pix_depth = texture(uDepth, uv).r;
		if (pix_depth == 1.0 || pix_depth < 0) discard;
        float depth = getViewZ(pix_depth);	
		float uvscale = uSampleRadius / depth;

		vec2 suv = uv;// +hash22(uv + vec2(pix_depth, -pix_depth)) * uvscale / textureSize(uDepth, 0);
		vec4 ipos = getPosition(suv);
        vec3 position = ipos.xyz; // alrady gets ground plane.
		vec3 normal = normalize(texture(uNormalBuffer, suv).xyz);
		//if (normal.z < 0) normal = -normal;

		float oFac=weight;

		float ang = hash12(vec2(uv.x*uv.y+uv.x+pix_depth,uv.x*uv.x-uv.y-pix_depth)+normal.xy);
		vec2 k1 = vec2(cos(ang), sin(ang)) * uvscale;

        occlusion = 0.0;
        for (int i = 0; i < ITERS; ++i) {
			k1 = vec2(k1.x * COS40 - k1.y * SIN40, k1.x * COS40 + k1.y * SIN40);
			vec2 kk = k1 * i * (0.9 + 0.2 * hash12(uv * pix_depth+normal.xy));
            occlusion += getOcclusion(position, normal, suv + (kk) / textureSize(uDepth, 0));
        }
		
        occlusion = clamp(occlusion / ITERS, 0.0, 1.0) * oFac;
		//occlusion = occlusion*0.00001+length(k1) * 0.1;
    }
@end

@program ssao blending_quad ssao_fs