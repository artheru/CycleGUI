@ctype mat4 glm::mat4
@ctype vec3 glm::vec3

@vs ground_plane_vs
uniform ground_vs_params {
    mat4 mvp;
};

in vec4 position_alpha;

out vec3 worldPosition;
out float major_alpha;

void main() {
    gl_Position = mvp * vec4(position_alpha.xyz, 1.0);
    worldPosition = (vec4(position_alpha.xyz, 1.0)).xyz;
    major_alpha = position_alpha.w;
}
@end

@fs ground_plane_fs
uniform ground_fs_params{
    vec3 starePosition;
    float scope;
};

in vec3 worldPosition;
in float major_alpha;

out vec4 frag_color;

void main() {
    float distance = length(worldPosition - starePosition);
    float alpha = 1.0;

    if (distance > scope*0.4) {
        alpha = 1.0 - smoothstep(scope*0.6, scope, distance);
    }

	frag_color = vec4(138.0 / 256.0, 43.0 / 256.0, 226.0 / 256.0, alpha * major_alpha);
}
@end

@program ground_plane ground_plane_vs ground_plane_fs

@vs sky_vs
in vec2 position;

out vec2 fpos;
void main() {
	gl_Position =  vec4( position, 1.0, 1.0 ); // set z to camera.far
	fpos = position;
}
@end

@fs sky_fs

out vec4 frag_color;

uniform sky_fs{
	float mieDirectionalG;
	mat4 invVM;
	mat4 invPM;
	
	vec3 vSunDirection;
	float vSunfade;
	vec3 vBetaR;
	vec3 vBetaM;
	float vSunE;

	float toneMappingExposure;
};

const vec3 up = vec3(0.0,0.0,1.0);
// constants for atmospheric scattering
const float pi = 3.141592653589793238462643383279502884197169;

const float n = 1.0003; // refractive index of air
const float N = 2.545E25; // number of molecules per unit volume for air at 288.15K and 1013mb (sea level -45 celsius)

// optical length at zenith for molecules
const float rayleighZenithLength = 8.4E3;
const float mieZenithLength = 1.25E3;
// 66 arc seconds -> degrees, and the cosine of that
const float sunAngularDiameterCos = 0.999956676946448443553574619906976478926848692873900859324;

// 3.0 / ( 16.0 * pi )
const float THREE_OVER_SIXTEENPI = 0.05968310365946075;
// 1.0 / ( 4.0 * pi )
const float ONE_OVER_FOURPI = 0.07957747154594767;

float rayleighPhase( float cosTheta ) {
	return THREE_OVER_SIXTEENPI * ( 1.0 + pow( cosTheta, 2.0 ) );
}

float hgPhase( float cosTheta, float g ) {
	float g2 = pow( g, 2.0 );
	float inverse = 1.0 / pow( 1.0 - 2.0 * g * cosTheta + g2, 1.5 );
	return ONE_OVER_FOURPI * ( ( 1.0 - g2 ) * inverse );
}

in vec2 fpos;

#define saturate( a ) clamp( a, 0.0, 1.0 )

// source: https://github.com/selfshadow/ltc_code/blob/master/webgl/shaders/ltc/ltc_blit.fs
vec3 RRTAndODTFit( vec3 v ) {

	vec3 a = v * ( v + 0.0245786 ) - 0.000090537;
	vec3 b = v * ( 0.983729 * v + 0.4329510 ) + 0.238081;
	return a / b;

}

vec3 ACESFilmicToneMapping( vec3 color ) {
	// sRGB => XYZ => D65_2_D60 => AP1 => RRT_SAT
	const mat3 ACESInputMat = mat3(
		vec3( 0.59719, 0.07600, 0.02840 ), // transposed from source
		vec3( 0.35458, 0.90834, 0.13383 ),
		vec3( 0.04823, 0.01566, 0.83777 )
	);

	// ODT_SAT => XYZ => D60_2_D65 => sRGB
	const mat3 ACESOutputMat = mat3(
		vec3(  1.60475, -0.10208, -0.00327 ), // transposed from source
		vec3( -0.53108,  1.10813, -0.07276 ),
		vec3( -0.07367, -0.00605,  1.07602 )
	);

	color *= toneMappingExposure / 0.6;

	color = ACESInputMat * color;

	// Apply RRT and ODT
	color = RRTAndODTFit( color );

	color = ACESOutputMat * color;

	// Clamp to [0, 1]
	return saturate( color );
}

void main() {

    // Calculate clip space position from the NDC position and depth
    // Assuming z = -1 places the position at the near plane, and w = 1 for a position (not a direction)
    vec4 clipSpacePosition = vec4(fpos.x, fpos.y, -11.0, 1.0);

    // Transform clip space position to view space
    vec4 viewSpacePosition = invPM * clipSpacePosition;

    // We only need direction, so set w to 0
    viewSpacePosition.w = 0.0;

    // Transform view space direction to world space
    vec4 worldSpaceDirection = invVM * viewSpacePosition;

    // Normalize to get the viewing direction
    vec3 direction = normalize(worldSpaceDirection.xyz);

	// optical length
	// cutoff angle at 90 to avoid singularity in next formula.
	float zenithAngle = acos( max( 0.0, dot( up, direction ) ) );
	float inverse = 1.0 / ( cos( zenithAngle ) + 0.15 * pow( 93.885 - ( ( zenithAngle * 180.0 ) / pi ), -1.253 ) );
	float sR = rayleighZenithLength * inverse;
	float sM = mieZenithLength * inverse;

	// combined extinction factor
	vec3 Fex = exp( -( vBetaR * sR + vBetaM * sM ) );

	// in scattering
	float cosTheta = dot( direction, vSunDirection );

	float rPhase = rayleighPhase( cosTheta * 0.5 + 0.5 );
	vec3 betaRTheta = vBetaR * rPhase;

	float mPhase = hgPhase( cosTheta, mieDirectionalG );
	vec3 betaMTheta = vBetaM * mPhase;

	vec3 Lin = pow( vSunE * ( ( betaRTheta + betaMTheta ) / ( vBetaR + vBetaM ) ) * ( 1.0 - Fex ), vec3( 1.5 ) );
	Lin *= mix( vec3( 1.0 ), pow( vSunE * ( ( betaRTheta + betaMTheta ) / ( vBetaR + vBetaM ) ) * Fex, vec3( 1.0 / 2.0 ) ), clamp( pow( 1.0 - dot( up, vSunDirection ), 5.0 ), 0.0, 1.0 ) );

	// nightsky
	float theta = acos( direction.z ); // elevation --> y-axis, [-pi/2, pi/2]
	float phi = atan( direction.y, direction.x ); // azimuth --> x-axis [-pi/2, pi/2]
	vec2 uv = vec2( phi, theta ) / vec2( 2.0 * pi, pi ) + vec2( 0.5, 0.0 );
	vec3 L0 = vec3( 0.1 ) * Fex;

	// composition + solar disc
	float sundisk = smoothstep( sunAngularDiameterCos, sunAngularDiameterCos + 0.00002, cosTheta );
	L0 += ( vSunE * 3000.0 * Fex ) * sundisk;

	vec3 texColor = ( Lin + L0 ) * 0.04 + vec3( 0.0, 0.0003, 0.00075 );

	vec3 retColor = pow( texColor, vec3( 1.0 / ( 1.2 + ( 1.2 * vSunfade ) ) ) );

	frag_color = vec4(ACESFilmicToneMapping(retColor), 1); // tone mapping.

}
@end


@program skybox sky_vs sky_fs