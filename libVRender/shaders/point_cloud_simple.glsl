@ctype mat4 glm::mat4
@ctype vec3 glm::vec3
/////////////////// SHADOW MAP
@vs point_cloud_vs_shadow
uniform vs_params {
    mat4 mvp;
	float dpi;
    int pc_id;
};

in vec3 position;
in float size;

void main() {
	vec4 mvpp = mvp * vec4(position, 1.0);
	gl_Position = mvpp;
	gl_PointSize = clamp(size / sqrt(mvpp.z) *3*dpi+2, 2, 32*dpi);
}
@end

@fs point_cloud_fs_onlyDepth
layout(location=0) out float g_depth;
void main() {
    g_depth = gl_FragCoord.z;
}
@end


@program pc_depth_only point_cloud_vs_shadow point_cloud_fs_onlyDepth

////////////////// RENDER
@vs point_cloud_vs
uniform vs_params {
    mat4 mvp;
	float dpi;
    int pc_id;
};

in vec3 position;
in float size;
in vec4 color; 

out vec4 v_Color;
flat out vec4 vid;

void main() {
	vec4 mvpp = mvp * vec4(position, 1.0);
	gl_Position = mvpp;
	gl_PointSize = clamp(size / sqrt(mvpp.z) *3*dpi+2, 2, 32*dpi);
	v_Color = color;
    vid = vec4(1,pc_id,gl_VertexIndex / 16777216,gl_VertexIndex % 16777216);
}
@end

@fs point_cloud_fs
in vec4 v_Color;
flat in vec4 vid;

layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth;
layout(location=2) out float pc_depth;
layout(location=3) out vec4 screen_id;

void main() {
	frag_color = v_Color;
    g_depth=pc_depth = gl_FragCoord.z;
    screen_id = vid;
}
@end

@program point_cloud_simple point_cloud_vs point_cloud_fs

@vs edl_composer_vs
in vec2 position;
out vec2 f_pos;
void main() {
	gl_Position = vec4(position,0.5,1.0);
	f_pos = position+vec2(0.5,0.5);
}
@end

@fs edl_composer_fs
uniform sampler2D color_hi_res;
uniform sampler2D depth_hi_res;
uniform sampler2D depth_lo_res;
uniform sampler2D uDepth;
uniform sampler2D ssao;
//uniform sampler2D shadow-map;

uniform window {
	float w, h, pnear, pfar;
    mat4 ipmat, ivmat, pmat, pv;
    vec3 campos, lookdir;
    float debugU;
};

in vec2 f_pos;
out vec4 frag_color;

float getld(float d){
    float ndc = d * 2.0 - 1.0;
    float z = (2.0 * pnear * pfar) / (pfar + pnear - ndc * (pfar - pnear));	
    return pow(z,0.9);
}
// makes a pseudorandom number between 0 and 1
float hash(float n) {
  return fract(sin(n)*93942.234);
}

// smoothsteps a grid of random numbers at the integers
float noise(vec2 p) {
  vec2 w = floor(p);
  vec2 k = fract(p);
  k = k*k*(3.-2.*k); // smooth it
  
  float n = w.x + w.y*57.;
  
  float a = hash(n);
  float b = hash(n+1.);
  float c = hash(n+57.);
  float d = hash(n+58.);
  
  return mix(
    mix(a, b, k.x),
    mix(c, d, k.x),
    k.y);
}

// rotation matrix
mat2 m = mat2(0.6,0.8,-0.8,0.6);

// fractional brownian motion (i.e. photoshop clouds)
float fbm(vec2 p) {
  float f = 0.;
  f += 0.5000*noise(p); p *= 2.02*m;
  f += 0.2500*noise(p); p *= 2.01*m;
  f += 0.1250*noise(p); p *= 2.03*m;
  f += 0.0625*noise(p);
  f /= 0.9375;
  return f;
}

float random1(vec2 uv) { return fract(sin(dot(uv.xy, vec2(12.9876, 78.512))) * 12345.6789); }
float random2(vec2 uv) { return fract(cos(dot(uv.xy, vec2(432.123, 123.678))) * 54321.12345); }

float perspectiveDepthToViewZ( const in float depth, const in float near, const in float far ) {
	// maps perspective depth in [ 0, 1 ] to viewZ
	return ( near * far ) / ( ( far - near ) * depth - far );
}

float getViewZ( const in float depth ) {
	return perspectiveDepthToViewZ( depth, pnear, pfar );
}

vec3 getPosition(float depthValue, vec2 uv){
	vec2 ndc = (2.0 * uv) - 1.0;
	
	float viewZ = getViewZ(depthValue);
	float clipW = pmat[2][3] * viewZ + pmat[3][3];

	vec4 clipPosition = vec4( ( vec3( ndc, depthValue ) - 0.5 ) * 2.0, 1.0 );
	clipPosition *= clipW; // unprojection.
	return ( ipmat * clipPosition ).xyz;
}

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(w, h);
    vec4 color=texture(color_hi_res,uv);
    frag_color=color;

    // ▩▩▩▩▩ Eye dome lighting ▩▩▩▩▩
    vec2 texelSize_hi = vec2(1.0) / vec2(textureSize(depth_hi_res, 0));
    vec2 texelSize_lo = vec2(1.0) / vec2(textureSize(depth_lo_res, 0));
    vec2[] offsets = vec2[](
        vec2(-1.0, -1.0), vec2(0.0, -1.0), vec2(1.0, -1.0),
        vec2(-1.0, 0.0),                    vec2(1.0, 0.0),
        vec2(-1.0, 1.0), vec2(0.0, 1.0), vec2(1.0, 1.0)
    );

    // lo-res blending...
    float low_me = texture(depth_lo_res,uv).r;
    float dmax = low_me;
    float dmin = low_me;
    float myld = getld(low_me);
    for (int i = 0; i < 8; ++i)
    {
        vec2 offset = offsets[i];
        float depth = texture(depth_lo_res, uv + offset * texelSize_lo).r;
    
        if (depth<1 && abs(getld(depth)-myld) <0.3){
            dmax = max(depth, dmax);
            dmin = min(depth, dmin);
        }
    }
    dmax=getld(dmax);
    dmin=getld(dmin);
    
    float zfac=0.4f;
    float fac=max(0,zfac/(pow(abs(dmax-dmin),0.8) +zfac));

    // border:
    float dhmin = 1;
    for (int i = 0; i < 8; ++i)
    {
        vec2 offset = offsets[i];
        float depth = texture(depth_hi_res, uv + offset * texelSize_hi).r;
        if (depth>0 && depth<1)
        dhmin = min(depth, dhmin);
    }
    float pcpxdepth = texture(depth_hi_res, uv).r;
    float centerDepth=getld(pcpxdepth);
    dhmin=getld(dhmin);
    if (dhmin<centerDepth){
        fac = fac * (1-min((centerDepth-dhmin)*2.2,1)); // border. factor=2.0
    }
    
    // if point is behind the actual scene, discard
    float pdepth=texture(uDepth,uv).r;
    if (pdepth < pcpxdepth)
        fac=1.0;
    
    float b = max(color.x, max(color.y, color.z));

    fac = (pow(fac, 0.8) - 1) * (b * 0.66);
    frag_color = vec4(color.xyz + fac,color.w) ;//+vec4(vec3(fac),1);
    
    //frag_color = vec4(vec3(fac),1) + color*0.1;//+;
    // ▩▩▩▩▩ SSAO ▩▩▩▩▩
    float darken=texture(ssao,uv).r * b*0.9;
    frag_color = vec4(frag_color.xyz - darken, color.w);


    // ▩▩▩▩▩ Ground SSR ▩▩▩▩▩
    vec2 ndc = (2.0 * gl_FragCoord.xy / vec2(w,h) - vec2(1.0));
    vec4 clip = vec4(ndc.x, ndc.y, -1.0, 1.0);
    vec4 eye = ipmat * clip;
    vec3 world_ray_dir = normalize((ivmat * vec4(eye.xyz, 0.0)).xyz);
    
    // Compute intersection with ground plane
    float t;
    bool doesIntersect;
    if (world_ray_dir.z != 0) {
        t = -campos.z / world_ray_dir.z;
        doesIntersect = (t >= 0);
    } else {
        doesIntersect = false;
    }

    float ssrweight = 0;
    
    vec4 ssrcolor= vec4(0);
    float below_ground_darken = 1.0;
    
    float shadowfac = 0;
    if (doesIntersect && campos.z > 0) {
        vec3 intersection = campos + t * world_ray_dir;
        vec3 lookdir = normalize(intersection-campos);
        
        vec4 ndci = pv * vec4(intersection,1);
        ndci /= ndci.w;
        float idepth = ndci.z *0.5 + 0.5;
        
        if (idepth < pdepth){ // should able to see reflection.
            below_ground_darken = 0.8-exp(-60*(lookdir.z)*(lookdir.z))*0.2;

            ssrweight = exp(-120*(lookdir.z)*(lookdir.z))*0.4+0.6;

            // ray marching:
            vec3 reflectingDir = vec3(lookdir.x,lookdir.y,-lookdir.z);
        
            float vw=pow(dot(lookdir,reflectingDir)+1,2)*0.2;

            int state=0;
            float rayint=1; 

            vec2 p = uv *5/length(intersection-campos); float t=0;
            vec2 a = vec2(fbm(p+t*3.), fbm(p-t*3.+8.1));
            vec2 b = vec2(fbm(p+t*4. + a*7. + 3.1), fbm(p-t*4. + a*7. + 91.1));
            float c = fbm(b*9. + t*20.);

            for (float s=0.05; s<8; s*=1.4, rayint*=0.99){
                vec3 endPos = intersection + reflectingDir * s; //findist.

                vec4 ndc2 = pv * vec4(endPos,1);
                ndc2 /= ndc2.w;
                float ndepth = ndc2.z *0.5 + 0.5;
                vec2 uv2 = ndc2.xy*0.5+0.5;
                uv2+=(c-0.5)*0.03;
                if (uv2.x>=1 ||uv2.y>=1 || uv2.x<=0 ||uv2.y<=0) break;
                float sDepth = texture(uDepth,uv2).r; 
            
                vec4 sclip = vec4(ndc2.x, ndc2.y, -1.0, 1.0);
                vec4 seye = ipmat * sclip;
                vec3 sworld_ray_dir = normalize((ivmat * vec4(seye.xyz, 0.0)).xyz);

                if (sDepth > ndepth){ // && getViewZ(sDepth)<getViewZ(ndepth)+1){
                    ssrcolor = texture(color_hi_res, uv2) * rayint * (3/(3+endPos.z));
                    state=1;
                }else{ // must get pass.
                    if (state==1) state=2; 
                    break;
                }
            }
            if (state!=2)
                ssrcolor=vec4(0);
            ssrweight += random1(gl_FragCoord.xy)*0.4-0.2;
            ssrweight *= 0.66;

            // also test shadow:
            vec3 shadowDir = vec3(0,0,1);
            for (float s=0.01; s<0.5; s*=1.3){
                vec3 endPos = intersection + shadowDir * s; //findist.

                vec4 ndc2 = pv * vec4(endPos,1);
                ndc2 /= ndc2.w;
                float ndepth = ndc2.z *0.5 + 0.5;
                vec2 uv2 = ndc2.xy*0.5+0.5;
                uv2+=(c-0.5)*0.01;
                float rand1=random1(gl_FragCoord.xy), rand2=random2(gl_FragCoord.xy);
                float sDepth = texture(uDepth, vec2(uv2.x+rand1*5/w,uv2.y)).r; 
                if (sDepth < 1){
                    float zdiff=length(abs(getld(ndepth)-getld(sDepth)));
                    shadowfac += ((1 / (s - 2) + 2) * exp(-(30+30*rand2)*zdiff*zdiff))*0.05;
                }
            }
            shadowfac = clamp(shadowfac, 0, 0.3);
        }
    }

    // todo: preserve hue?
    frag_color = (frag_color * below_ground_darken * (1 - ssrweight) + ssrweight * ssrcolor) * (1-shadowfac) + vec4(0,0,0,1)*shadowfac;
}
@end

// @fs edl_composer_fs_simple
// uniform sampler2D color;
// 
// in vec2 f_pos;
// 
// out vec4 frag_color;
// void main() {
// 	vec4 c = texture(color, f_pos);
// 	frag_color = c;
// }
// @end

@program edl_composer edl_composer_vs edl_composer_fs