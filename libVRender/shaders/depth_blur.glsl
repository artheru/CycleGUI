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
};

in vec2 uv;
out vec4 frag_color;

#define pnear 0.0625f
#define pfar 8192.0f

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
    float denom = 1.0 / (2.0 * pi * sigma * sigma);

    // Initialize accumulation variables
    vec4 color = vec4(0.0);
    float totalWeight = 0.0;
    
    float myDval=texture(tex,uv).r;
    if (myDval==1){
        frag_color=vec4(1);
        return;
    }

    float ld=getld(myDval);

    // Perform convolution in one pass
    for(int i = -N/2; i <= N/2; i++){
        for(int j = -N/2; j <= N/2; j++){
            float x = float(i);
            float y = float(j);

            vec2 offset = vec2(x, y) * texelSize_hi;
            float value = texture(tex, uv + offset).r;
            if (value == 1) continue;
            
            if (abs(getld(value)-ld)>0.1) continue;

            float weight = denom * exp(-(x*x + y*y) / (2.0 * sigma * sigma));
            color += value * weight;
            totalWeight += weight;
        }
    }
    if (totalWeight==0)
        frag_color = vec4(myDval);
    else
        frag_color = color / totalWeight;
}
@end

@program depth_blur depth_blur_vs depth_blur_fs