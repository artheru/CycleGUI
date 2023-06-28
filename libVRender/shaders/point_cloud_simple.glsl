@ctype mat4 glm::mat4

/////////////////// SHADOW MAP
@vs point_cloud_vs_shadow
uniform vs_params {
    mat4 mvp;
	float dpi;
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
};

in vec3 position;
in float size;
in vec4 color; 

out vec4 v_Color;

void main() {
	vec4 mvpp = mvp * vec4(position, 1.0);
	gl_Position = mvpp;
	gl_PointSize = clamp(size / sqrt(mvpp.z) *3*dpi+2, 2, 32*dpi);
	v_Color = color;
}
@end

@fs point_cloud_fs
in vec4 v_Color;
layout(location=0) out vec4 frag_color;
layout(location=1) out float g_depth;
layout(location=2) out float pc_depth;
void main() {
	frag_color = v_Color;
    g_depth=pc_depth=gl_FragCoord.z;
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
uniform sampler2D ssao;
uniform sampler2D uDepth;
//uniform sampler2D shadow-map;

uniform window {
	float w, h, pnear, pfar;
};

in vec2 f_pos;
out vec4 frag_color;

float getld(float d){
    float ndc = d * 2.0 - 1.0;
    return (2.0 * pnear * pfar) / (pfar + pnear - ndc * (pfar - pnear));	
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
    float me = texture(depth_lo_res,uv).r;
    float dmax = me;
    float dmin = me;
    float myld = getld(me);
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
    float centerDepth = texture(depth_hi_res, uv).r;
    centerDepth=getld(centerDepth);
    dhmin=getld(dhmin);
    if (dhmin<centerDepth){
        fac = fac * (1-min((centerDepth-dhmin)*2.2,1)); // border. factor=2.0
    }
    
    // if point is behind the actual scene, discard.
    float pdepth=texture(uDepth,uv).r;
    if (pdepth<me) fac=1.0;
    
    frag_color = vec4(color.xyz*fac,color.w) ;//+vec4(vec3(fac),1);
    
    
    //frag_color = vec4(vec3(fac),1) + color*0.1;//+;
    // ▩▩▩▩▩ SSAO ▩▩▩▩▩
    float darken=texture(ssao,uv).r * fac;
    frag_color = frag_color * (1-darken) + vec4(vec3(0.0),darken);
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