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
// Improved SAO (Scalable Ambient Occlusion)
    uniform SSAOUniforms {
		mat4 P, iP, iV;
		vec3 cP;

		float weight;
        float uSampleRadius; //16
        float uBias; //0.04
        vec2 uAttenuation; //(1,1)
        vec2 uDepthRange; //(NEAR,FAR)
		
		float useFlag;
    };

	// Improved hash functions from https://www.shadertoy.com/view/4djSRW
	float hash12(vec2 p)
	{
		p = fract(p * vec2(443.8975, 397.2973));
		p += dot(p.xy, p.yx + 19.19);
		return fract(p.x * p.y);
	}

	vec2 hash22(vec2 p)
	{
		vec3 p3 = fract(vec3(p.xyx) * vec3(.1031, .1030, .0973));
		p3 += dot(p3, p3.yzx+33.33);
		return fract((p3.xx+p3.yz)*p3.zy);
	}

    uniform sampler2D uDepth;  
    uniform sampler2D uNormalBuffer;
        
	in vec2 uv;
    out float occlusion;

	float perspectiveDepthToViewZ(const in float depth, const in float near, const in float far) {
		return (near * far) / ((far - near) * depth - far);
	}

	float getViewZ(const in float depth) {
		return perspectiveDepthToViewZ(depth, uDepthRange.x, uDepthRange.y);
	}

	vec4 getPosition(vec2 uv) {
		float depthValue = texture(uDepth, uv).r;
		
		if (depthValue < 0.0 || depthValue >= 0.9999) return vec4(0.0);

		float viewZ = getViewZ(depthValue);
		float clipW = P[2][3] * viewZ + P[3][3];

		vec4 clipPosition = vec4(uv * 2.0 - 1.0, 2.0 * depthValue - 1.0, 1.0);
		return vec4((iP * clipPosition).xyz * clipW, 1.0);
	}

	float getOcclusion(vec3 position, vec3 normal, vec2 sampleUV) {
		vec4 samplePos = getPosition(sampleUV);
		if (samplePos.w == 0.0) return 0.0;
		
		vec3 sampleDir = samplePos.xyz - position;
		float distSq = dot(sampleDir, sampleDir);
		
		// Early bailout for samples too far away
		if (distSq > uSampleRadius * uSampleRadius) return 0.0;
		
		// Normalize and compute AO factor
		float dist = sqrt(distSq);
		sampleDir = sampleDir / (dist + 0.0001);
		
		// Compare normals with direction to sample
		float NdotV = max(dot(normal, sampleDir) - uBias, 0.0);
		
		// Distance attenuation - use uAttenuation parameters effectively
		float attenFactor = 1.0 - smoothstep(uSampleRadius * 0.5, uSampleRadius, dist);
		
		return NdotV * attenFactor;
	}

// Precomputed spiral sampling pattern - better distribution than rotated direction
#define PI 3.14159265359
#define GOLDEN_ANGLE 2.39996323
#define ITERS 16

    void main() {
		int useFlagi = int(useFlag);
		bool useGround = bool(useFlagi & 4);
		
		float pix_depth = texture(uDepth, uv).r;
		if (pix_depth >= 0.9999 || pix_depth < 0.0) discard;
		
        float depth = getViewZ(pix_depth);	
		float radiusScale = uSampleRadius / depth;
		
		// Get screen space position and normal
		vec4 ipos = getPosition(uv);
        vec3 position = ipos.xyz;
		vec3 normal = normalize(texture(uNormalBuffer, uv).xyz);
		
		// Temporal and spatial noise to reduce banding
		// Use interleaved gradient noise for better quality
		vec2 noiseScale = vec2(textureSize(uDepth, 0)) / 4.0;
		float randomAngle = PI * hash12(uv * noiseScale + fract(normal.xy));
		
        occlusion = 0.0;
        
        // Spiral sampling pattern for better distribution
        for (int i = 0; i < ITERS; ++i) {
			float angle = randomAngle + float(i) * GOLDEN_ANGLE;
			float radius = sqrt(float(i) / float(ITERS)) * radiusScale;
			
			vec2 offset = vec2(cos(angle), sin(angle)) * radius;
			vec2 sampleUV = uv + offset / textureSize(uDepth, 0);
			
            occlusion += getOcclusion(position, normal, sampleUV);
        }
		
        occlusion = clamp(occlusion / float(ITERS), 0.0, 1.0) * weight;
    }
@end

@program ssao blending_quad ssao_fs