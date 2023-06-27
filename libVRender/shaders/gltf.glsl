@ctype mat4 glm::mat4
@ctype vec3 glm::vec3

@vs vs_ground
uniform gltf_ground_mats{
	mat4 pv;
};
in vec3 x_y_radius; // per instance;

// per vertex
in vec2 position;
out vec2 vpos;

void main(){
	gl_Position = pv * vec4(
		x_y_radius.x + position.x * x_y_radius.z, 
		x_y_radius.y + position.y * x_y_radius.z, 0.0, 1.0 );
	vpos=position;
}
@end

@fs fs_ground
in vec2 vpos;
out vec4 frag_color;
void main(){
	float radius=length(vpos);
	frag_color = vec4(1.0,0.95,0.9,0.2-exp((radius-0.8) * 10));
}
@end

@program gltf_ground vs_ground fs_ground


////////////////////////////////////////////////
//  SHADOW MAP


@vs vs_depth
uniform gltf_mats{
	mat4 projectionMatrix, viewMatrix;
};
uniform sampler2D NImodelViewMatrix;
in int instance_id;
in vec3 position;
in int node_id;

void main() {
    mat4 modelViewMatrix = mat4(
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+0,node_id), 0),
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+1,node_id), 0),
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+2,node_id), 0),
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+3,node_id), 0) );
	gl_Position = projectionMatrix * modelViewMatrix * vec4( position, 1.0 );
}
@end

@fs fs_depth

layout(location=0) out float g_depth;
void main() {
    g_depth = gl_FragDepth;
}
@end

@program gltf_depth_only vs_depth fs_depth

////////////////////////////////////////////////

@vs gltf_vs
uniform gltf_mats{
	mat4 projectionMatrix, viewMatrix;
};

// model related:
uniform sampler2D NImodelViewMatrix;
uniform sampler2D NInormalMatrix;

// per instance.
in int instance_id;

// per vertex
in vec3 position;
in vec3 normal;
in vec4 color0;
in int node_id;


out vec4 color;
out vec3 vNormal;
out vec3 vTexCoord3D;
out vec3 vertPos;

void main() {
    mat4 modelViewMatrix = mat4(
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+0,node_id), 0),
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+1,node_id), 0),
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+2,node_id), 0),
        texelFetch(NImodelViewMatrix, ivec2(instance_id*4+3,node_id), 0) );

    mat3 normalMatrix = mat3(
        texelFetch(NInormalMatrix, ivec2(instance_id*4+0,node_id), 0).rgb,
        texelFetch(NInormalMatrix, ivec2(instance_id*4+1,node_id), 0).rgb,
        texelFetch(NInormalMatrix, ivec2(instance_id*4+2,node_id), 0).rgb);
        
	vec4 mPosition = modelViewMatrix * vec4( position, 1.0 );
	vNormal = normalize( normalMatrix * normal );
	vertPos = mPosition.xyz/mPosition.w;

	vTexCoord3D = 0.1 * ( position.xyz + vec3( 0.0, 1.0, 1.0 ) );
	gl_Position = projectionMatrix * modelViewMatrix * vec4( position, 1.0 );

    color = vec4(0.6,0.6,0.6,1.0) + color0*0.4;
}
@end

@fs gltf_fs
uniform gltf_mats{
	mat4 projectionMatrix, viewMatrix;
};

in vec4 color;
in vec3 vNormal;
in vec3 vTexCoord3D;
in vec3 vertPos;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth; // the fuck. blending cannot be closed?
layout(location=2) out vec4 out_normal;


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

	// height

	float n = heightMap( vTexCoord3D )*0.1;

	// color

	vec3 baseColor = color.rgb * vec3( color.r - n*0.3, color.g - n *0.4, color.b - n*0.8 );

	// normal

	const float e = 0.2;

	float nx = heightMap( vTexCoord3D + vec3( e, 0.0, 0.0 ) );
	float ny = heightMap( vTexCoord3D + vec3( 0.0, e, 0.0 ) );
	float nz = heightMap( vTexCoord3D + vec3( 0.0, 0.0, e ) );

	vec3 normal = normalize( vNormal - 0.005 * vec3( n - nx, n - ny, n - nz ) / e ); //constrast

	// diffuse light 

	vec3 vLightWeighting = vec3( 0.3 ) * 0.2; //brightness

	vec3 lDirection =  normalize(viewMatrix * vec4(0.3, 0.3, 1.0, 0.0) + vec4(-0.3,0.3,1.0,0.0)*10).rgb;
	float directionalLightWeighting = clamp(dot( normal, normalize( lDirection.xyz ) ) * 0.5 + 0.5,0.0,1.5);
	vLightWeighting += vec3( 1.0 ) * directionalLightWeighting;

	vec4 lDirectionB =  viewMatrix * vec4( normalize( vec3( 0.0, 0.0, -1.0 ) ), 0.0 );
	float bw = dot( normal, normalize( lDirectionB.xyz ) ) * clamp(exp(-vertPos.y*1.0),0.0,1.0)*0.1;
	vec3 blight = vec3( 1.0,0.0,1.0 ) * clamp(bw,0.0,1.0);

	// specular light
	vec3 dirHalfVector = normalize( normalize(lDirection.xyz) - normalize( vertPos ) ); 
 
	float dirDotNormalHalf = dot( normal, dirHalfVector );
 
	float dirSpecularWeight = 0.0; 
	if ( dirDotNormalHalf >= 0.0 ) 
		dirSpecularWeight = pow( dirDotNormalHalf, 160 );

	// rim light (fresnel)
	float rim = pow(1-abs(dot(normal, normalize(vertPos))),15);

	vLightWeighting += (dirSpecularWeight+rim) * (0.5+vec3(nx,ny,nz)) * 0.7;

	// output:
	frag_color = vec4( baseColor * vLightWeighting + blight, 1.0 );
	g_depth = gl_FragCoord.z;
	out_normal = vec4(vNormal,1.0);
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
    #define SIN45 0.707107

    uniform SSAOUniforms {
		mat4 P,iP, iV;
		vec3 cP;

        float uSampleRadius; //16
        float uBias; //0.04
        vec2 uAttenuation; //(1,1)
        vec2 uDepthRange; //(NEAR,FAR)
    };

    uniform sampler2D uDepth;
    uniform sampler2D uNormalBuffer;
        
    out float occlusion;

	float perspectiveDepthToViewZ( const in float depth, const in float near, const in float far ) {
		// maps perspective depth in [ 0, 1 ] to viewZ
		return ( near * far ) / ( ( far - near ) * depth - far );
	}

	float getViewZ( const in float depth ) {
		return perspectiveDepthToViewZ( depth, uDepthRange.x, uDepthRange.y );
	}

	vec3 getPosition(ivec2 fragCoord){
		float depthValue = texelFetch(uDepth, fragCoord, 0).r;
		ivec2 uDepthSize = textureSize(uDepth, 0);
		vec2 ndc = (2.0 * vec2(fragCoord) / uDepthSize) - 1.0;
		
		if (depthValue>0.999999) {
			vec4 clipSpacePosition = vec4(ndc, -1.0, 1.0);
			vec4 viewSpacePosition = iP * clipSpacePosition;
			viewSpacePosition /= viewSpacePosition.w;  // Homogeneous coordinates
			vec4 worldSpacePosition = iV * viewSpacePosition;
			vec3 rayOrigin = cP;
			vec3 rayDirection = normalize(worldSpacePosition.xyz - cP);

			float t = -rayOrigin.z / rayDirection.z;
			vec3 intersection = rayOrigin + t * rayDirection;
			return intersection;
		}

		float viewZ = getViewZ(depthValue);
		float clipW = P[2][3] * viewZ + P[3][3];

		vec4 clipPosition = vec4( ( vec3( ndc, depthValue ) - 0.5 ) * 2.0, 1.0 );
		clipPosition *= clipW; // unprojection.
		return ( iP * clipPosition ).xyz;
	}

    float getOcclusion(vec3 position, vec3 normal, ivec2 fragCoord) {
        vec3 occluderPosition = getPosition(fragCoord);
        vec3 positionVec = occluderPosition - position;
        float intensity = max(dot(normal, normalize(positionVec)) - uBias, 0.0);
        
        float attenuation = 1.0 / (uAttenuation.x + uAttenuation.y * length(positionVec));

        return intensity * attenuation;
    }

    void main() {
		float pix_depth = texelFetch(uDepth, ivec2(gl_FragCoord.xy), 0).r;

        ivec2 fragCoord = ivec2(gl_FragCoord.xy);
        vec3 position = getPosition(fragCoord);
        vec3 normal = texelFetch(uNormalBuffer, fragCoord, 0).xyz;
		if (pix_depth==1.0)
			normal = vec3(0.0,0.0,-1.0);

		vec2 noise = fract(sin(vec2(dot(fragCoord.xy, vec2(12.9898, 78.233)), dot(fragCoord.xy, vec2(39.789, 102.734)))) * 43758.5453);

        vec2 rand = noise*2-1; // not random enough.
        float depth = (length(position) - uDepthRange.x) / (uDepthRange.y - uDepthRange.x);

        float kernelRadius = uSampleRadius * (1.0 - depth);

        vec2 kernel[4];
        kernel[0] = vec2(0.0, 1.0);
        kernel[1] = vec2(1.0, 0.0);
        kernel[2] = vec2(0.0, -1.0);
        kernel[3] = vec2(-1.0, 0.0);

        occlusion = 0.0;
        for (int i = 0; i < 4; ++i) {
            vec2 k1 = reflect(kernel[i], rand);
            vec2 k2 = vec2(k1.x * SIN45 - k1.y * SIN45, k1.x * SIN45 + k1.y * SIN45);

            k1 *= kernelRadius;
            k2 *= kernelRadius;

            occlusion += getOcclusion(position, normal, fragCoord + ivec2(k1));
            occlusion += getOcclusion(position, normal, fragCoord + ivec2(k2 * 0.75));
            occlusion += getOcclusion(position, normal, fragCoord + ivec2(k1 * 0.5));
            occlusion += getOcclusion(position, normal, fragCoord + ivec2(k2 * 0.25));
        }
		
        occlusion = clamp(occlusion / 16.0, 0.0, 1.0);
        //occlusion = (pix_depth-0.99)*90 + 0.01*clamp(occlusion / 16.0, 0.0, 1.0);
    }
@end

@program ssao blending_quad ssao_fs