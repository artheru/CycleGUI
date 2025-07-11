@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec3 glm::vec3

@vs ground_plane_vs
uniform ground_vs_params {
    mat4 mvp;
};

in vec4 position_alpha;

out vec3 worldPosition;
out float major_alpha;

//out vec2 uv;

void main() {
    gl_Position = mvp * vec4(position_alpha.xyz, 1.0);
	//uv = gl_Position.xy / gl_Position.w * 0.5 + 0.5;
    worldPosition = (vec4(position_alpha.xyz, 1.0)).xyz;
    major_alpha = position_alpha.w;
}
@end

@fs ground_plane_fs
uniform ground_fs_params{
    vec3 starePosition;
	vec2 viewportOffset;
    float scope;
};
uniform sampler2D uDepth;

in vec3 worldPosition;
in float major_alpha;

//in vec2 uv;

out vec4 frag_color;

void main() {
    float distance = length(worldPosition - starePosition);
    float alpha = 1.0;

    if (distance > scope*0.4) {
        alpha = 1.0 - smoothstep(scope*0.6, scope, distance);
    }

	float myd=gl_FragCoord.z;
	//float vd = texture(uDepth, uv).r;
	float vd=texelFetch(uDepth, ivec2(gl_FragCoord.xy-viewportOffset), 0).r;
	if (vd < 0) discard; // vd = -vd; minus depth mean direct to screen.
	if (myd > vd)
		alpha *= 0;// clamp(0.0001 / (myd - vd), 0.0, 0.2);
	if (major_alpha>=0)
		frag_color = vec4(138.0 / 256.0, 43.0 / 256.0, 226.0 / 256.0, alpha * clamp(major_alpha,0,1));
	else //rgb(145, 200, 228)
		frag_color = vec4(145 / 256.0, 200 / 256.0, 228 / 256.0, alpha * clamp(-major_alpha, 0, 1));
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

// actually could be a bit difficult to implement a good ground grid with pure shader....
@vs user_shader_vs
uniform u_user_shader{
	mat4 invVM;
	mat4 invPM;
	vec2 iResolution;
	mat4 pvm;
	vec3 camera_pos;
};

in vec2 position;

out vec2 fpos;
out vec3 lookat;
out float upper, lower, mid, ufac, lfac, mfac;
void main() {
	gl_Position =  vec4( position, 1.0, 1.0 ); // set z to camera.far
	fpos = position;
	
    vec4 clipSpacePosition = vec4(fpos.x, fpos.y, 0, 1.0);
    vec4 viewSpacePosition = invPM * clipSpacePosition;
    // We only need direction, so set w to 0
    viewSpacePosition.w = 0.0;
    vec4 worldSpaceDirection = invVM * viewSpacePosition;
    lookat = normalize(worldSpaceDirection.xyz);

	vec3 ll = normalize((invVM * vec4((invPM * vec4(0, 0, 0, 1)).xyz, 0)).xyz);
	float cdist;
	if (ll.z * camera_pos.z > 0) cdist = camera_pos.z;
	else{
		float s1 = -camera_pos.z / ll.z;
		cdist = min(s1, abs(camera_pos.z * 2));
	}
	
	float v1d = max(-1.5, log(cdist) / log(5) - 1);

	mid = exp(log(5) * round(v1d));
	mfac = max(0, 1 - 0.7*abs(v1d - round(v1d)));

	lower = exp(log(5) * (round(v1d)-1));
	lfac = max(0, 1 - 0.7*abs(v1d - (round(v1d) - 1)));

	upper = exp(log(5) * (round(v1d)+1));
	ufac = max(0, 1 - 0.7*abs(v1d - (round(v1d) + 1)));
	
	lfac = pow(lfac, 1.4);
	ufac = pow(ufac, 0.7);
}
@end

@fs grid_fs

uniform u_user_shader{
	mat4 invVM;
	mat4 invPM;
	vec2 iResolution;
	mat4 pvm;
	vec3 camera_pos;
};
uniform sampler2D uDepth;	

in vec2 fpos;
in vec3 lookat;
out vec4 frag_color;
in float upper, lower, mid, ufac, lfac, mfac;

float pointLineDistance(vec2 sxp, vec2 sxyp, vec2 fpos) {
    // Direction vector of the line
    vec2 direction = sxyp - sxp;

    // Normal vector to the line
    vec2 normal = vec2(-direction.y, direction.x);

    // Vector from point sxp to the point fpos
    vec2 pointVec = fpos - sxp;

    // Project pointVec onto normal and calculate the distance
    float distance = abs(dot(pointVec, normal)) / length(normal);

    return distance;
}

float linecolor(vec2 gnd, float unit) {
	vec2 mmf = round(gnd / unit) * unit;
	//vec2 ad = 1-abs(gnd - mmf) / unit;
	//return min(ad.x, ad.y);

	vec2 xl = vec2(mmf.x, gnd.y);
	vec2 yl = vec2(gnd.x, mmf.y);
	vec4 xp = pvm * vec4(xl, 0, 1);
	vec4 yp = pvm * vec4(yl, 0, 1);
	vec4 xyp = pvm * vec4(mmf, 0, 1);
	vec2 sxp = xp.xy / xp.w  * iResolution;
	vec2 syp = yp.xy / yp.w  * iResolution;
	vec2 sxyp = xyp.xy / xyp.w  * iResolution;
	vec2 fipos = fpos * iResolution;
	float dx = pointLineDistance(sxp, sxyp, fipos);
	float dy = pointLineDistance(syp, sxyp, fipos);
	float wd = 2;
	return max(0, wd - min(dx, dy)) / wd;
}

void main() {

	if (lookat.z * camera_pos.z > 0) discard;

    float t = camera_pos.z / lookat.z;
    vec3 worldSpace = camera_pos - t * lookat;
	float upcolor = linecolor(worldSpace.xy, upper);
	float locolor = linecolor(worldSpace.xy, lower);
	float mcolor = linecolor(worldSpace.xy, mid);
	float mixed = upcolor * ufac + locolor * lfac + mcolor * mfac;
	if (mixed == 0) discard;

	// alpha part:
	vec4 gndproj = pvm*vec4(worldSpace, 1.0);
	float myd = gndproj.z / gndproj.w * 0.5 + 0.5;
	float vd=texture(uDepth, fpos*0.5+0.5).r;
	if (vd<0) vd=-vd;
    float alpha = 1.0;
	float tglk = abs(lookat.z / length(lookat.xy));
	if (tglk < 0.1)
		alpha *= tglk * 10;
	//if (myd > 0.985)
	//	alpha *= 1 - (myd - 0.985) / 0.015;
	if (myd < 0.8)
		alpha *= max(0, (myd - 0.5) / 0.3);
	if (myd > vd) // should hide.
		alpha *= clamp(0.0001 / (myd-vd), 0.0, 1.0);
	float finalpha = alpha * 0.3 * mixed;
	if (finalpha < 0.01)
		discard;

	frag_color = vec4(vec3(138.0 / 256.0, 43.0 / 256.0, 226.0 / 256.0)*max(vec3(1), vec3(mixed * 1.3, mixed * 1.0, mixed * 1.5)), finalpha);
	//gl_FragDepth = myd;
}
@end

@program after_shader user_shader_vs grid_fs

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


float random(vec2 uv) { return fract(sin(dot(uv.xy, vec2(12.9898, 78.233))) * 43758.5453); }

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

	vec3 noise= vec3(random(fpos/50)*2, random(fpos/30)*1.5, random(fpos/20))*0.1;
	//retColor = retColor + 
	//	exp(-150*(direction.z+0.06)*(direction.z+0.06))*clamp(exp(direction.z*10),0,1) * 
	//	(vec3(138.0 / 256.0, 43.0 / 256.0, 226.0 / 256.0) + noise)*0.5;

	frag_color = vec4(ACESFilmicToneMapping(retColor), 1); // tone mapping.

}
@end


@program skybox sky_vs sky_fs