// shaders for rendering a debug visualization
@vs depth_blur_vs

in vec2 pos;
out vec2 uv;

void main() {
    gl_Position = vec4(pos*2.0-1.0, 0.5, 1.0);
    uv = pos;
}
@end

@fs depth_blur_fs
uniform sampler2D tex;
uniform depth_blur_params {
    uniform int kernelSize;
    uniform float scale;
    uniform float pnear, pfar;
};

in vec2 uv;
layout(location=0) out float frag_color;
layout(location=1) out vec4 out_normal;

float getld(float d){
    float ndc = d * 2.0 - 1.0;
    return (2.0 * pnear * pfar) / (pfar + pnear - ndc * (pfar - pnear));	
}

void main() {
    vec2 texelSize_hi = vec2(scale) / vec2(textureSize(tex, 0));

    int N = kernelSize;
    // Calculate depth_blur parameters
    float sigma = float(N) / 6.0;
    float pi = 3.14159265358979323846;

    // Initialize accumulation variables
    float color = 0;
    float totalWeight = 0.0;
    
    float myDval=texture(tex,uv).r;
    if (myDval==1){
        frag_color=1;
        return;
    }

    float ld=getld(myDval);

    // Perform convolution in one pass, bilinear filter.
    // todo: use kuwahara-filter
    float dx=0, dy=0, xsum=0, ysum=1;
    for(int i = -N/2; i <= N/2; i++){
        for(int j = -N/2; j <= N/2; j++){
            float x = float(i);
            float y = float(j);

            vec2 offset = vec2(x, y) * texelSize_hi;
            float value = texture(tex, uv + offset).r;
            if (value == 1) continue;
            
            float myld=getld(value);
            float ldDiff = abs(myld - ld);

            if (i!=0){
                dx += myld/i; xsum+=1;}
            if (j!=0){
                dy += myld/j; ysum+=1;}

            float bilinearFac = exp(-(ldDiff-0.02)*(ldDiff-0.02)/(0.06*0.06));
            float weight = bilinearFac * exp(-(x*x + y*y) / (2.0 * sigma * sigma));
            color += value * weight;
            totalWeight += weight;
        }
    }

    out_normal = vec4(normalize(cross(vec3(1,0,dx/xsum),vec3(0,1,dy/ysum))),1);

    if (totalWeight==0)
        frag_color = myDval;
    else
        frag_color = color / totalWeight;
}
@end

@program depth_blur depth_blur_vs depth_blur_fs