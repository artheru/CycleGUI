@ctype mat4 glm::mat4

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
out vec4 frag_color;
void main() {
	frag_color = v_Color;
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
uniform sampler2D depth_hi_res;
uniform sampler2D depth_lo_res;
uniform sampler2D color_hi_res;

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

    vec2 texelSize_hi = vec2(1.0) / vec2(textureSize(depth_hi_res, 0));
    vec2 texelSize_lo = vec2(1.0) / vec2(textureSize(depth_lo_res, 0));
    
    vec2 uv = gl_FragCoord.xy / vec2(w, h);
    
    vec4 color=texture(color_hi_res,uv);
    
    vec2[] offsets = vec2[](
        vec2(-1.0, -1.0), vec2(0.0, -1.0), vec2(1.0, -1.0),
        vec2(-1.0, 0.0),                    vec2(1.0, 0.0),
        vec2(-1.0, 1.0), vec2(0.0, 1.0), vec2(1.0, 1.0)
    );
    
    // plat
    float me=texture(depth_lo_res,uv).r;
    float dmax =me;
    float dmin =me;
    float myld=getld(me);
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
    
    float zfac=0.4;
    float fac=1;

    fac=max(0,zfac/(pow(dmax-dmin,0.8) +zfac));

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

    // float fac = max(0, 1-(dmax-dmin)*5);
    if (dhmin<centerDepth){
        fac = fac * (1-min((centerDepth-dhmin)*2.2,1)); // border. factor=2.0
    }
    
    frag_color = vec4(color.xyz*fac,color.w);//+vec4(vec3(fac),1);
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