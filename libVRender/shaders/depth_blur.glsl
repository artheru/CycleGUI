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
out float frag_color;

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
    for(int i = -N/2; i <= N/2; i++){
        for(int j = -N/2; j <= N/2; j++){
            float x = float(i);
            float y = float(j);

            vec2 offset = vec2(x, y) * texelSize_hi;
            float value = texture(tex, uv + offset).r;
            if (value == 1) continue;
            
            float ldDiff = abs(getld(value) - ld);
            float bilinearFac = exp(-(ldDiff-0.02)*(ldDiff-0.02)/(0.06*0.06));

            float weight = bilinearFac * exp(-(x*x + y*y) / (2.0 * sigma * sigma));
            color += value * weight;
            totalWeight += weight;
        }
    }
    if (totalWeight==0)
        frag_color = myDval;
    else
        frag_color = color / totalWeight;
}
@end

@program depth_blur depth_blur_vs depth_blur_fs

@vs kuwahara_filter_vs
in vec2 pos;
void main() {
    gl_Position = vec4(pos*2.0-1.0, 0.5, 1.0);
}
@end

@fs kuwahara_filter_fs
#define MAX_SIZE        7
#define MAX_KERNEL_SIZE ((MAX_SIZE * 2 + 1) * (MAX_SIZE * 2 + 1))

uniform sampler2D colorTexture;

out vec4 fragColor;

vec2 texSize  = textureSize(colorTexture, 0).xy;
vec2 texCoord = gl_FragCoord.xy / texSize;

int i     = 0;
int j     = 0;
int count = 0;

vec3  valueRatios = vec3(0.3, 0.59, 0.11);

float values[MAX_KERNEL_SIZE];

vec4  color       = vec4(0.0);
vec4  meanTemp    = vec4(0.0);
vec4  mean        = vec4(0.0);
float valueMean   =  0.0;
float variance    =  0.0;
float minVariance = -1.0;

void findMean(int i0, int i1, int j0, int j1) {
  meanTemp = vec4(0);
  count    = 0;

  for (i = i0; i <= i1; ++i) {
    for (j = j0; j <= j1; ++j) {
      color  =
        texture
          ( colorTexture
          ,   (gl_FragCoord.xy + vec2(i, j))
            / texSize
          );

      meanTemp += color;

      values[count] = dot(color.rgb, valueRatios);

      count += 1;
    }
  }

  meanTemp.rgb /= count;
  valueMean     = dot(meanTemp.rgb, valueRatios);

  for (i = 0; i < count; ++i) {
    variance += pow(values[i] - valueMean, 2);
  }

  variance /= count;

  if (variance < minVariance || minVariance <= -1) {
    mean = meanTemp;
    minVariance = variance;
  }
}

void main() {
  fragColor = texture(colorTexture, texCoord);

  int size = 3;
  if (size <= 0) { return; }

  // Lower Left

  findMean(-size, 0, -size, 0);

  // Upper Right

  findMean(0, size, 0, size);

  // Upper Left

  findMean(-size, 0, 0, size);

  // Lower Right

  findMean(0, size, -size, 0);

  fragColor.rgb = mean.rgb;
}
@end

@program kuwahara_blur kuwahara_filter_vs kuwahara_filter_fs