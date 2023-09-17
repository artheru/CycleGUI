@ctype mat4 glm::mat4
@ctype vec3 glm::vec3


@vs edl_composer_vs
in vec2 position;
//out vec2 f_pos;
void main() {
	gl_Position = vec4(position,0.5,1.0);
	//f_pos = position+vec2(0.5,0.5);
}
@end

@fs bloom_dilateX
// offscreen pass.
uniform sampler2D shine;
out vec4 frag_color;
void main(){
    vec4 bloom=vec4(0);
    for (int i=-9; i<=9; ++i){
        vec4 zf = texelFetch(shine, ivec2(gl_FragCoord)+ivec2(i,0), 0);
        bloom = max(bloom, zf-0.125*abs(i));
    }
    frag_color=bloom;
}
@end
@program bloomDilateX edl_composer_vs bloom_dilateX

@fs bloom_dilateY
// offscreen pass.
uniform sampler2D shine;
out vec4 frag_color;
void main(){
    vec4 bloom=vec4(0);
    for (int i=-9; i<=9; ++i){
        vec4 zf = texelFetch(shine, ivec2(gl_FragCoord)+ivec2(0,i), 0);
        bloom = max(bloom, zf-0.125*abs(i));
    }
    frag_color=bloom;
    frag_color=vec4(vec3(bloom),0.5);
}
@end
@program bloomDilateY edl_composer_vs bloom_dilateY

@fs bloom_blurX
// offscreen pass.
uniform sampler2D shine;
out vec4 frag_color;
void main(){
    vec4 bloom=vec4(0);
    for (int i=-11; i<=11; ++i){
        vec4 zf = texelFetch(shine, ivec2(gl_FragCoord)+ivec2(i,0), 0);
        bloom += zf;
    }
    frag_color=bloom/23;
}
@end
@program bloomblurX edl_composer_vs bloom_blurX

@fs bloom_blurY
// also compose to screen, use additive blending.
uniform sampler2D shine;
out vec4 frag_color;
void main(){
    vec4 bloom=vec4(0);
    for (int i=-11; i<=11; ++i){
        vec4 zf = texelFetch(shine, ivec2(gl_FragCoord)+ivec2(0,i), 0);
        bloom += zf;
    }
    frag_color=bloom/23;
}
@end
@program bloomblurYFin edl_composer_vs bloom_blurY


@fs ui_composer_fs
// border and selection, use normal blending.
uniform sampler2D bordering;
uniform sampler2D ui_selection;

uniform ui_composing{
    float draw_sel;
    //vec3 border_color_hover;    //bordering=1
    //vec3 border_color_selected; //2
    //vec3 border_color_world;    //3
    vec4 border_colors[3]; // hover, selected, world.
    //float border_size;
};

out vec4 frag_color;

void main(){
    // border:
    vec2[] offsets = vec2[](
        vec2(-1.0, -1.0), vec2(0.0, -1.0), vec2(1.0, -1.0),
        vec2(-1.0, 0.0),                    vec2(1.0, 0.0),
        vec2(-1.0, 1.0), vec2(0.0, 1.0), vec2(1.0, 1.0)
    );

    float border = 0;
    float center = texelFetch(bordering, ivec2(gl_FragCoord.xy), 0).r;
    vec3 border_color = vec3(0);
    
    for (int i = 0; i < 8; ++i)
    {
        vec2 offset = offsets[i];
        float test = texelFetch(bordering, ivec2(gl_FragCoord.xy + offset), 0).r;
        if (test > center){ 
            border = 1; 
            center = test;
            border_color = border_colors[int(test*16)-1].xyz;
        }
    }

    frag_color = vec4(border_color, 0.66) * border;
    
    vec2 uv = gl_FragCoord.xy / textureSize(bordering, 0); // has the same size of screen.
    uv.y = 1-uv.y;
    // selection:
    if (draw_sel>0){
        frag_color += vec4(1,0.3,0,0.5*texture(ui_selection, uv).r);
    }
}
@end

@program border_composer edl_composer_vs ui_composer_fs

@fs edl_composer_fs
uniform sampler2D color_hi_res;
uniform sampler2D depth_hi_res;
uniform sampler2D depth_lo_res;
uniform sampler2D uDepth;
uniform sampler2D ssao;

uniform window {
	float w, h, pnear, pfar;
    mat4 ipmat, ivmat, pmat, pv;
    vec3 campos, lookdir;
    float debugU;

    float facFac, fac2Fac, fac2WFac, colorFac, reverse1, reverse2, edrefl, edlthres, useFlag;
};

out vec4 frag_color;

float getld(float d){
    float ndc = d * 2.0 - 1.0;
    float z = (2.0 * pnear * pfar) / (pfar + pnear - ndc * (pfar - pnear));	
    return z;
}

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(w, h);

    vec4 color=texture(color_hi_res,uv);
    frag_color=color;
    
    int useFlagi=int(useFlag);
    bool useEDL=bool(useFlagi&1), useSSAO=bool(useFlagi&2), useGround=bool(useFlagi&4);

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
    
        if (depth<1 && abs(getld(depth)-myld) < 0.1){
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
    float facb = 1;
    if (dhmin<centerDepth){
        facb =  (1-min((centerDepth-dhmin)*2.2,1)); // border. factor=2.0
        facb = facb * 0.6 + 0.4;
    }

    float kk=min(fac,facb);
    float kv=kk-reverse1;
    fac = kk + edrefl*exp(-(kv*kv)/(reverse2+0.00001));

    
    // if point is behind the actual scene, discard
    float pdepth=texture(uDepth,uv).r;
    if (pdepth < pcpxdepth || !useEDL)
        fac=1.0;

    fac = 0.2 + 0.8 * fac;
    
    float b = max(color.x, max(color.y, color.z));

    float fac2 = (pow(fac, fac2Fac) - 1) * b;
    frag_color = vec4(color.xyz * fac * facFac + fac2*fac2WFac + color.xyz*colorFac,color.w) ;//+;
    //frag_color = vec4(vec3(fac),1) + color*0.1;//+;


    // ▩▩▩▩▩ SSAO ▩▩▩▩▩
    float darken=(texelFetch(ssao,ivec2(gl_FragCoord.xy),0).r) * b*0.9;
    if (pdepth == pcpxdepth){ // point cloud ssao
        darken *=0.5;
    }
    if (!useSSAO) darken=0;
    frag_color = vec4(frag_color.xyz - darken, color.w);
}
@end

@program edl_composer edl_composer_vs edl_composer_fs