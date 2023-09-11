@ctype mat4 glm::mat4
@ctype vec3 glm::vec3


@vs composer_vs
in vec2 position;
void main() {
	gl_Position = vec4(position,0.5,1.0);
}
@end


@fs ground_effect_fs

uniform uground {
	float w, h, pnear, pfar;
    mat4 ipmat, ivmat, pmat, pv;
    vec3 campos, lookdir;
    float time;
};

uniform sampler2D color_hi_res;
uniform sampler2D uDepth;

out vec4 frag_color;

float getld(float d){
    float ndc = d * 2.0 - 1.0;
    float z = (2.0 * pnear * pfar) / (pfar + pnear - ndc * (pfar - pnear));	
    return z;
}

float random1(vec2 uv) { return fract(sin(dot(uv.xy, vec2(12.9876, 78.512))) * 12345.6789); }
float random2(vec2 uv) { return fract(cos(dot(uv.xy, vec2(432.123, 123.678))) * 54321.12345); }


void main() {
    vec2 uv = gl_FragCoord.xy / vec2(w, h);

    float pdepth=texture(uDepth,uv).r;
    vec4 color=texture(color_hi_res,uv);

    //frag_color=color;

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
        
		    vec2 noise = fract((sin(vec2(dot(gl_FragCoord.xy, vec2(12.9898, 78.233)), dot(gl_FragCoord.xy, vec2(39.789, 102.734))))+time/19.567) * 12.5453);
            vec2 rand = noise *2 -1;
            reflectingDir.xy +=rand*0.2 * (1-length(reflectingDir.xy));

            float vw=pow(dot(lookdir,reflectingDir)+1,2)*0.2;

            int state=0;

            float s=0.05;
            for (int i=0; i<16; i+=1, s=(s+0.1)*1.3){
                vec3 endPos = intersection + reflectingDir * s; //findist.

                vec4 ndc2 = pv * vec4(endPos,1);
                ndc2 /= ndc2.w;
                float ndepth = ndc2.z *0.5 + 0.5;
                vec2 uv2 = ndc2.xy*0.5+0.5;

                if (uv2.x>=1 ||uv2.y>=1 || uv2.x<=0 ||uv2.y<=0) break;
                float sDepth = texture(uDepth,uv2).r; 
            
                vec4 sclip = vec4(ndc2.x, ndc2.y, -1.0, 1.0);
                vec4 seye = ipmat * sclip;
                vec3 sworld_ray_dir = normalize((ivmat * vec4(seye.xyz, 0.0)).xyz);

                if (ndepth < sDepth && sDepth<1){
                    state=1;
                }else if (sDepth < ndepth){ // must get pass.
                    if (state==1) {
                        ssrcolor = texture(color_hi_res, uv2); // * rayint * (1/(1+endPos.z));
                        break;
                    }
                }
            }
            ssrcolor.w -= max(0, 0.95-length(reflectingDir.xy));
            ssrcolor.w *= 0.6;

            // also test shadow:
            vec3 shadowDir = vec3(0,0,1);
            for (float s=0.01; s<0.5; s*=1.3){
                vec3 endPos = intersection + shadowDir * s; //findist.

                vec4 ndc2 = pv * vec4(endPos,1);
                ndc2 /= ndc2.w;
                float ndepth = ndc2.z *0.5 + 0.5;
                vec2 uv2 = ndc2.xy*0.5+0.5;
                float rand1=random1(gl_FragCoord.xy), rand2=random2(gl_FragCoord.xy);
                float sDepth = texture(uDepth, vec2(uv2.x+rand1*5/w,uv2.y)).r; 
                if (sDepth < 1){
                    float zdiff=length(abs(getld(ndepth)-getld(sDepth)));
                    shadowfac += ((1 / (s - 2) + 2) * exp(-(30+30*rand2)*zdiff*zdiff))*0.05;
                }
            }
            shadowfac = clamp(shadowfac, 0, 0.5);
        }
    }
    frag_color = ssrweight * ssrcolor * (1-shadowfac) + vec4(0,0,0, shadowfac + (1-below_ground_darken)*color.w);
}
@end

@program ground_effect composer_vs ground_effect_fs