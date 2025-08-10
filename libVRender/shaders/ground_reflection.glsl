@ctype mat4 glm::mat4
@ctype vec3 glm::vec3


@vs composer_vs
in vec2 position;
out vec2 uv;
void main() {
	gl_Position = vec4(position,0.5,1.0);
    uv = position * 0.5 + vec2(0.5, 0.5);
}
@end


@fs ground_effect_fs

uniform uground {
	float w, h, pnear, pfar;
    mat4 ipmat, ivmat, pmat, pv;
    vec3 campos;
    float time;
};

uniform sampler2D color_hi_res;
uniform sampler2D uDepth;

out vec4 frag_color;
in vec2 uv;

float getld(float d){
    float ndc = d * 2.0 - 1.0;
    float z = (2.0 * pnear * pfar) / (pfar + pnear - ndc * (pfar - pnear));	
    return z;
}

float random1(vec2 uv) { return fract(sin(dot(uv.xy, vec2(12.9876, 78.512))) * 12345.6789); }
float random2(vec2 uv) { return fract(cos(dot(uv.xy, vec2(432.123, 123.678))) * 54321.12345); }

float sampleDepth(vec2 uv) {
    float depth = texture(uDepth, uv).r;
    return depth < 0 ? -1 : depth > 0.5 ? depth : depth + 0.5;
}

void main() {
    //vec2 uv = gl_FragCoord.xy / vec2(w, h);

    float pdepth = sampleDepth(uv);// texture(uDepth, uv).r;
    vec4 color=texture(color_hi_res,uv);

    //frag_color=color;

    // ▩▩▩▩▩ Ground SSR ▩▩▩▩▩
    vec2 ndc = (2.0 * uv - vec2(1.0));
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
        vec3 lookdir = world_ray_dir;
        
        vec4 ndci = pv * vec4(intersection,1);
        ndci /= ndci.w;
        float idepth = ndci.z *0.5 + 0.5;
        
        if (idepth < pdepth){ // should able to see reflection.
            below_ground_darken = 0.8-exp(-60*(lookdir.z)*(lookdir.z))*0.2;

            ssrweight = exp(-120*(lookdir.z)*(lookdir.z))*0.4+0.6;

            // ray marching:
            vec3 reflectingDir = vec3(lookdir.x,lookdir.y,-lookdir.z);
        
		    vec2 noise = fract((sin(vec2(dot(gl_FragCoord.xy, vec2(12.9898, 78.233)), dot(gl_FragCoord.xy, vec2(39.789, 102.734))))) * 12.5453);
            vec2 rand = noise *2 -1;
            reflectingDir.xy +=rand*0.2 * (1-length(reflectingDir.xy));

            float vw=pow(dot(lookdir,reflectingDir)+1,2)*0.2;

            int state=0;

            float s=0.1; //todo: change to "1 pixel".
            int fq = 0;
            vec2 prevUv = ndci.xy * 0.5 + 0.5;

            float prevS = 0;
            for (int i=0; i<32; i+=1, s=(s+0.1)*1.1){
                vec3 endPos = intersection + reflectingDir * s; //findist.

                vec4 ndc2 = pv * vec4(endPos,1);
                ndc2 /= ndc2.w;
                float ndepth = ndc2.z *0.5 + 0.5;
                vec2 uv2 = ndc2.xy * 0.5 + 0.5;
                vec2 puv2 = uv2;
                float nld = getld(ndepth);

                if (uv2.x>=1 ||uv2.y>=1 || uv2.x<=0 ||uv2.y<=0) break;
                float sDepth = sampleDepth(uv2);

                if (sDepth < ndepth) {
                    float minD = abs(nld - getld(sDepth));
                    // from prevUv find the nearest uc that sDepth closest to ndepth.
                    for (int j = 0; j < 2; ++j) {
                        vec2 nuv = (uv2 + prevUv) * 0.5;
                        sDepth = sampleDepth(nuv);
                        if (sDepth < ndepth) uv2 = nuv;
                        else prevUv = nuv;
                        minD = min(minD, abs(nld-getld(sDepth)));
                    }
                    if (minD < s-prevS) {
                        ssrcolor = texture(color_hi_res, uv2) + (min(10.0 / (s + 10), 1) - 1); // * rayint * (1/(1+endPos.z));
                        break;
                    }
                }
                prevUv = puv2;
                prevS = s;
            }
            ssrcolor.w -= max(0, 0.95-length(reflectingDir.xy));
            //ssrcolor.w *= 0.6;

            // also test shadow:
            vec3 shadowDir = vec3(0,0,1);
            float rand1=random1(gl_FragCoord.xy), rand2=random2(gl_FragCoord.xy);
            for (float s=0.02; s<0.5; s=(s+0.05)*1.2){
                vec3 endPos = intersection + shadowDir * s; //findist.
            
                vec4 ndc2 = pv * vec4(endPos,1);
                ndc2 /= ndc2.w;
                float ndepth = ndc2.z *0.5 + 0.5;
                vec2 uv2 = ndc2.xy*0.5+0.5;
                float sDepth = sampleDepth(vec2(uv2.x+rand1*5/w,uv2.y));
                //float sDepth = texture(uDepth, uv2).r;
            
                if (sDepth < 1){
                    // get projected to ground position:
                    vec3 pxpos = campos + normalize(endPos - campos) * getld(sDepth);
                    float l = length(pxpos.xy - intersection.xy);
                    shadowfac += (1 / (s + 1)) * exp(-20 * l);
                    //float zdiff=length(abs(getld(ndepth)-getld(sDepth)));
                    //shadowfac += ((1 / (s - 2) + 2) * exp(-(30+30*rand2)*zdiff*zdiff))*0.05;
                }
            }
            shadowfac = clamp(shadowfac * 0.3, 0, 0.5);
        }
    }
    //frag_color = vec4(shadowfac,ssrcolor.r,0,1);
    float ff1 = ssrweight * (1 - shadowfac);
    //frag_color = ssrcolor * ssrweight * (1-shadowfac) + vec4(0,0,0, shadowfac + (1-below_ground_darken)*color.w);
    frag_color = ssrcolor*(0.5+0.5*ff1) - vec4(vec3(0.1 * (1 - ff1)), 0) + vec4(0, 0, 0, shadowfac + (1 - below_ground_darken) * color.w);
}
@end

@program ground_effect composer_vs ground_effect_fs