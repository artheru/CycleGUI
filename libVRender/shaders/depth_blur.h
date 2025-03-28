#pragma once
/*
    #version:1# (machine generated, don't edit!)

    Generated by sokol-shdc (https://github.com/floooh/sokol-tools)

    Cmdline: sokol-shdc --input depth_blur.glsl --output depth_blur.h --slang glsl300es

    Overview:

        Shader program 'depth_blur':
            Get shader desc: depth_blur_shader_desc(sg_query_backend());
            Vertex shader: depth_blur_vs
                Attribute slots:
                    ATTR_depth_blur_vs_pos = 0
            Fragment shader: depth_blur_fs
                Uniform block 'depth_blur_params':
                    C struct: depth_blur_params_t
                    Bind slot: SLOT_depth_blur_params = 0
                Image 'tex':
                    Type: SG_IMAGETYPE_2D
                    Component Type: SG_SAMPLERTYPE_FLOAT
                    Bind slot: SLOT_tex = 0


    Shader descriptor structs:

        sg_shader depth_blur = sg_make_shader(depth_blur_shader_desc(sg_query_backend()));

    Vertex attribute locations for vertex shader 'depth_blur_vs':

        sg_pipeline pip = sg_make_pipeline(&(sg_pipeline_desc){
            .layout = {
                .attrs = {
                    [ATTR_depth_blur_vs_pos] = { ... },
                },
            },
            ...});

    Image bind slots, use as index in sg_bindings.vs_images[] or .fs_images[]

        SLOT_tex = 0;

    Bind slot and C-struct for uniform block 'depth_blur_params':

        depth_blur_params_t depth_blur_params = {
            .kernelSize = ...;
            .scale = ...;
            .pnear = ...;
            .pfar = ...;
        };
        sg_apply_uniforms(SG_SHADERSTAGE_[VS|FS], SLOT_depth_blur_params, &SG_RANGE(depth_blur_params));

*/
#include <stdint.h>
#include <stdbool.h>
#include <string.h>
#include <stddef.h>
#if !defined(SOKOL_SHDC_ALIGN)
  #if defined(_MSC_VER)
    #define SOKOL_SHDC_ALIGN(a) __declspec(align(a))
  #else
    #define SOKOL_SHDC_ALIGN(a) __attribute__((aligned(a)))
  #endif
#endif
#define ATTR_depth_blur_vs_pos (0)
#define SLOT_tex (0)
#define SLOT_depth_blur_params (0)
#pragma pack(push,1)
SOKOL_SHDC_ALIGN(16) typedef struct depth_blur_params_t {
    int kernelSize;
    float scale;
    float pnear;
    float pfar;
} depth_blur_params_t;
#pragma pack(pop)
/*
    #version 300 es
    
    layout(location = 0) in vec2 pos;
    out vec2 uv;
    
    void main()
    {
        gl_Position = vec4((pos * 2.0) - vec2(1.0), 0.5, 1.0);
        uv = pos;
    }
    
*/
static const char depth_blur_vs_source_glsl300es[156] = {
    0x23,0x76,0x65,0x72,0x73,0x69,0x6f,0x6e,0x20,0x33,0x30,0x30,0x20,0x65,0x73,0x0a,
    0x0a,0x6c,0x61,0x79,0x6f,0x75,0x74,0x28,0x6c,0x6f,0x63,0x61,0x74,0x69,0x6f,0x6e,
    0x20,0x3d,0x20,0x30,0x29,0x20,0x69,0x6e,0x20,0x76,0x65,0x63,0x32,0x20,0x70,0x6f,
    0x73,0x3b,0x0a,0x6f,0x75,0x74,0x20,0x76,0x65,0x63,0x32,0x20,0x75,0x76,0x3b,0x0a,
    0x0a,0x76,0x6f,0x69,0x64,0x20,0x6d,0x61,0x69,0x6e,0x28,0x29,0x0a,0x7b,0x0a,0x20,
    0x20,0x20,0x20,0x67,0x6c,0x5f,0x50,0x6f,0x73,0x69,0x74,0x69,0x6f,0x6e,0x20,0x3d,
    0x20,0x76,0x65,0x63,0x34,0x28,0x28,0x70,0x6f,0x73,0x20,0x2a,0x20,0x32,0x2e,0x30,
    0x29,0x20,0x2d,0x20,0x76,0x65,0x63,0x32,0x28,0x31,0x2e,0x30,0x29,0x2c,0x20,0x30,
    0x2e,0x35,0x2c,0x20,0x31,0x2e,0x30,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,0x75,0x76,
    0x20,0x3d,0x20,0x70,0x6f,0x73,0x3b,0x0a,0x7d,0x0a,0x0a,0x00,
};
/*
    #version 300 es
    precision mediump float;
    precision highp int;
    
    struct depth_blur_params
    {
        int kernelSize;
        highp float scale;
        highp float pnear;
        highp float pfar;
    };
    
    uniform depth_blur_params _23;
    
    uniform highp sampler2D tex;
    
    in highp vec2 uv;
    layout(location = 0) out highp float frag_color;
    layout(location = 1) out highp vec4 out_normal;
    
    highp float getld(highp float d)
    {
        return ((2.0 * _23.pnear) * _23.pfar) / ((-(d * 2.0 + (-1.0))) * (_23.pfar - _23.pnear) + (_23.pfar + _23.pnear));
    }
    
    void main()
    {
        highp vec2 _66 = vec2(_23.scale) / vec2(textureSize(tex, 0));
        highp float _74 = float(_23.kernelSize);
        highp float _76 = _74 * 0.16666667163372039794921875;
        highp float color = 0.0;
        highp float totalWeight = 0.0;
        highp vec4 _88 = texture(tex, uv);
        highp float _91 = _88.x;
        if (_91 == 1.0)
        {
            frag_color = 1.0;
            return;
        }
        highp float param = _91;
        highp float _103 = getld(param);
        highp float dx = 0.0;
        highp float dy = 0.0;
        highp float xsum = 0.0;
        highp float ysum = 1.0;
        int _111 = (-_23.kernelSize) / 2;
        int i = _111;
        for (;;)
        {
            int _119 = _23.kernelSize / 2;
            if (i <= _119)
            {
                for (int j = _111; j <= _119; j++)
                {
                    highp float _136 = float(i);
                    highp float _139 = float(j);
                    highp vec4 _151 = texture(tex, vec2(_136, _139) * _66 + uv);
                    highp float _152 = _151.x;
                    if (_152 == 1.0)
                    {
                        continue;
                    }
                    highp float param_1 = _152;
                    highp float _161 = getld(param_1);
                    highp float _166 = abs(_161 - _103);
                    if (i != 0)
                    {
                        dx += (_161 / float(i));
                        xsum += 1.0;
                    }
                    if (j != 0)
                    {
                        dy += (_161 / float(j));
                        ysum += 1.0;
                    }
                    highp float _201 = exp(((0.0199999995529651641845703125 - _166) * (_166 - 0.0199999995529651641845703125)) * 277.77777099609375);
                    highp float _217 = exp((-(_136 * _136 + (_139 * _139))) / ((_74 * 0.3333333432674407958984375) * _76));
                    color = _152 * (_201 * _217) + color;
                    totalWeight = _201 * _217 + totalWeight;
                }
                i++;
                continue;
            }
            else
            {
                break;
            }
        }
        out_normal = vec4(normalize(cross(vec3(1.0, 0.0, dx / xsum), vec3(0.0, 1.0, dy / ysum))), 1.0);
        if (totalWeight == 0.0)
        {
            frag_color = _91;
        }
        else
        {
            frag_color = color / totalWeight;
        }
    }
    
*/
static const char depth_blur_fs_source_glsl300es[2760] = {
    0x23,0x76,0x65,0x72,0x73,0x69,0x6f,0x6e,0x20,0x33,0x30,0x30,0x20,0x65,0x73,0x0a,
    0x70,0x72,0x65,0x63,0x69,0x73,0x69,0x6f,0x6e,0x20,0x6d,0x65,0x64,0x69,0x75,0x6d,
    0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x3b,0x0a,0x70,0x72,0x65,0x63,0x69,0x73,0x69,
    0x6f,0x6e,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x69,0x6e,0x74,0x3b,0x0a,0x0a,0x73,
    0x74,0x72,0x75,0x63,0x74,0x20,0x64,0x65,0x70,0x74,0x68,0x5f,0x62,0x6c,0x75,0x72,
    0x5f,0x70,0x61,0x72,0x61,0x6d,0x73,0x0a,0x7b,0x0a,0x20,0x20,0x20,0x20,0x69,0x6e,
    0x74,0x20,0x6b,0x65,0x72,0x6e,0x65,0x6c,0x53,0x69,0x7a,0x65,0x3b,0x0a,0x20,0x20,
    0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x73,0x63,
    0x61,0x6c,0x65,0x3b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,
    0x6c,0x6f,0x61,0x74,0x20,0x70,0x6e,0x65,0x61,0x72,0x3b,0x0a,0x20,0x20,0x20,0x20,
    0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x70,0x66,0x61,0x72,
    0x3b,0x0a,0x7d,0x3b,0x0a,0x0a,0x75,0x6e,0x69,0x66,0x6f,0x72,0x6d,0x20,0x64,0x65,
    0x70,0x74,0x68,0x5f,0x62,0x6c,0x75,0x72,0x5f,0x70,0x61,0x72,0x61,0x6d,0x73,0x20,
    0x5f,0x32,0x33,0x3b,0x0a,0x0a,0x75,0x6e,0x69,0x66,0x6f,0x72,0x6d,0x20,0x68,0x69,
    0x67,0x68,0x70,0x20,0x73,0x61,0x6d,0x70,0x6c,0x65,0x72,0x32,0x44,0x20,0x74,0x65,
    0x78,0x3b,0x0a,0x0a,0x69,0x6e,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x76,0x65,0x63,
    0x32,0x20,0x75,0x76,0x3b,0x0a,0x6c,0x61,0x79,0x6f,0x75,0x74,0x28,0x6c,0x6f,0x63,
    0x61,0x74,0x69,0x6f,0x6e,0x20,0x3d,0x20,0x30,0x29,0x20,0x6f,0x75,0x74,0x20,0x68,
    0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x66,0x72,0x61,0x67,0x5f,
    0x63,0x6f,0x6c,0x6f,0x72,0x3b,0x0a,0x6c,0x61,0x79,0x6f,0x75,0x74,0x28,0x6c,0x6f,
    0x63,0x61,0x74,0x69,0x6f,0x6e,0x20,0x3d,0x20,0x31,0x29,0x20,0x6f,0x75,0x74,0x20,
    0x68,0x69,0x67,0x68,0x70,0x20,0x76,0x65,0x63,0x34,0x20,0x6f,0x75,0x74,0x5f,0x6e,
    0x6f,0x72,0x6d,0x61,0x6c,0x3b,0x0a,0x0a,0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,
    0x6f,0x61,0x74,0x20,0x67,0x65,0x74,0x6c,0x64,0x28,0x68,0x69,0x67,0x68,0x70,0x20,
    0x66,0x6c,0x6f,0x61,0x74,0x20,0x64,0x29,0x0a,0x7b,0x0a,0x20,0x20,0x20,0x20,0x72,
    0x65,0x74,0x75,0x72,0x6e,0x20,0x28,0x28,0x32,0x2e,0x30,0x20,0x2a,0x20,0x5f,0x32,
    0x33,0x2e,0x70,0x6e,0x65,0x61,0x72,0x29,0x20,0x2a,0x20,0x5f,0x32,0x33,0x2e,0x70,
    0x66,0x61,0x72,0x29,0x20,0x2f,0x20,0x28,0x28,0x2d,0x28,0x64,0x20,0x2a,0x20,0x32,
    0x2e,0x30,0x20,0x2b,0x20,0x28,0x2d,0x31,0x2e,0x30,0x29,0x29,0x29,0x20,0x2a,0x20,
    0x28,0x5f,0x32,0x33,0x2e,0x70,0x66,0x61,0x72,0x20,0x2d,0x20,0x5f,0x32,0x33,0x2e,
    0x70,0x6e,0x65,0x61,0x72,0x29,0x20,0x2b,0x20,0x28,0x5f,0x32,0x33,0x2e,0x70,0x66,
    0x61,0x72,0x20,0x2b,0x20,0x5f,0x32,0x33,0x2e,0x70,0x6e,0x65,0x61,0x72,0x29,0x29,
    0x3b,0x0a,0x7d,0x0a,0x0a,0x76,0x6f,0x69,0x64,0x20,0x6d,0x61,0x69,0x6e,0x28,0x29,
    0x0a,0x7b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x76,0x65,0x63,
    0x32,0x20,0x5f,0x36,0x36,0x20,0x3d,0x20,0x76,0x65,0x63,0x32,0x28,0x5f,0x32,0x33,
    0x2e,0x73,0x63,0x61,0x6c,0x65,0x29,0x20,0x2f,0x20,0x76,0x65,0x63,0x32,0x28,0x74,
    0x65,0x78,0x74,0x75,0x72,0x65,0x53,0x69,0x7a,0x65,0x28,0x74,0x65,0x78,0x2c,0x20,
    0x30,0x29,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,
    0x6c,0x6f,0x61,0x74,0x20,0x5f,0x37,0x34,0x20,0x3d,0x20,0x66,0x6c,0x6f,0x61,0x74,
    0x28,0x5f,0x32,0x33,0x2e,0x6b,0x65,0x72,0x6e,0x65,0x6c,0x53,0x69,0x7a,0x65,0x29,
    0x3b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,
    0x74,0x20,0x5f,0x37,0x36,0x20,0x3d,0x20,0x5f,0x37,0x34,0x20,0x2a,0x20,0x30,0x2e,
    0x31,0x36,0x36,0x36,0x36,0x36,0x36,0x37,0x31,0x36,0x33,0x33,0x37,0x32,0x30,0x33,
    0x39,0x37,0x39,0x34,0x39,0x32,0x31,0x38,0x37,0x35,0x3b,0x0a,0x20,0x20,0x20,0x20,
    0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x63,0x6f,0x6c,0x6f,
    0x72,0x20,0x3d,0x20,0x30,0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,
    0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x74,0x6f,0x74,0x61,0x6c,0x57,0x65,
    0x69,0x67,0x68,0x74,0x20,0x3d,0x20,0x30,0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,0x20,
    0x68,0x69,0x67,0x68,0x70,0x20,0x76,0x65,0x63,0x34,0x20,0x5f,0x38,0x38,0x20,0x3d,
    0x20,0x74,0x65,0x78,0x74,0x75,0x72,0x65,0x28,0x74,0x65,0x78,0x2c,0x20,0x75,0x76,
    0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,
    0x61,0x74,0x20,0x5f,0x39,0x31,0x20,0x3d,0x20,0x5f,0x38,0x38,0x2e,0x78,0x3b,0x0a,
    0x20,0x20,0x20,0x20,0x69,0x66,0x20,0x28,0x5f,0x39,0x31,0x20,0x3d,0x3d,0x20,0x31,
    0x2e,0x30,0x29,0x0a,0x20,0x20,0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x66,0x72,0x61,0x67,0x5f,0x63,0x6f,0x6c,0x6f,0x72,0x20,0x3d,0x20,0x31,
    0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x72,0x65,0x74,0x75,
    0x72,0x6e,0x3b,0x0a,0x20,0x20,0x20,0x20,0x7d,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,
    0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x70,0x61,0x72,0x61,0x6d,0x20,
    0x3d,0x20,0x5f,0x39,0x31,0x3b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,
    0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x5f,0x31,0x30,0x33,0x20,0x3d,0x20,0x67,0x65,
    0x74,0x6c,0x64,0x28,0x70,0x61,0x72,0x61,0x6d,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,
    0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x64,0x78,0x20,0x3d,
    0x20,0x30,0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,
    0x66,0x6c,0x6f,0x61,0x74,0x20,0x64,0x79,0x20,0x3d,0x20,0x30,0x2e,0x30,0x3b,0x0a,
    0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,
    0x78,0x73,0x75,0x6d,0x20,0x3d,0x20,0x30,0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,0x20,
    0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x79,0x73,0x75,0x6d,
    0x20,0x3d,0x20,0x31,0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,0x20,0x69,0x6e,0x74,0x20,
    0x5f,0x31,0x31,0x31,0x20,0x3d,0x20,0x28,0x2d,0x5f,0x32,0x33,0x2e,0x6b,0x65,0x72,
    0x6e,0x65,0x6c,0x53,0x69,0x7a,0x65,0x29,0x20,0x2f,0x20,0x32,0x3b,0x0a,0x20,0x20,
    0x20,0x20,0x69,0x6e,0x74,0x20,0x69,0x20,0x3d,0x20,0x5f,0x31,0x31,0x31,0x3b,0x0a,
    0x20,0x20,0x20,0x20,0x66,0x6f,0x72,0x20,0x28,0x3b,0x3b,0x29,0x0a,0x20,0x20,0x20,
    0x20,0x7b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x69,0x6e,0x74,0x20,0x5f,
    0x31,0x31,0x39,0x20,0x3d,0x20,0x5f,0x32,0x33,0x2e,0x6b,0x65,0x72,0x6e,0x65,0x6c,
    0x53,0x69,0x7a,0x65,0x20,0x2f,0x20,0x32,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x69,0x66,0x20,0x28,0x69,0x20,0x3c,0x3d,0x20,0x5f,0x31,0x31,0x39,0x29,
    0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x66,0x6f,0x72,0x20,0x28,0x69,0x6e,0x74,0x20,
    0x6a,0x20,0x3d,0x20,0x5f,0x31,0x31,0x31,0x3b,0x20,0x6a,0x20,0x3c,0x3d,0x20,0x5f,
    0x31,0x31,0x39,0x3b,0x20,0x6a,0x2b,0x2b,0x29,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,
    0x6f,0x61,0x74,0x20,0x5f,0x31,0x33,0x36,0x20,0x3d,0x20,0x66,0x6c,0x6f,0x61,0x74,
    0x28,0x69,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,
    0x20,0x5f,0x31,0x33,0x39,0x20,0x3d,0x20,0x66,0x6c,0x6f,0x61,0x74,0x28,0x6a,0x29,
    0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x76,0x65,0x63,0x34,0x20,0x5f,0x31,0x35,
    0x31,0x20,0x3d,0x20,0x74,0x65,0x78,0x74,0x75,0x72,0x65,0x28,0x74,0x65,0x78,0x2c,
    0x20,0x76,0x65,0x63,0x32,0x28,0x5f,0x31,0x33,0x36,0x2c,0x20,0x5f,0x31,0x33,0x39,
    0x29,0x20,0x2a,0x20,0x5f,0x36,0x36,0x20,0x2b,0x20,0x75,0x76,0x29,0x3b,0x0a,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x68,
    0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x5f,0x31,0x35,0x32,0x20,
    0x3d,0x20,0x5f,0x31,0x35,0x31,0x2e,0x78,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x69,0x66,0x20,0x28,0x5f,0x31,
    0x35,0x32,0x20,0x3d,0x3d,0x20,0x31,0x2e,0x30,0x29,0x0a,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x63,0x6f,0x6e,0x74,0x69,0x6e,0x75,0x65,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7d,0x0a,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x68,0x69,0x67,
    0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x70,0x61,0x72,0x61,0x6d,0x5f,0x31,
    0x20,0x3d,0x20,0x5f,0x31,0x35,0x32,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,0x70,0x20,0x66,
    0x6c,0x6f,0x61,0x74,0x20,0x5f,0x31,0x36,0x31,0x20,0x3d,0x20,0x67,0x65,0x74,0x6c,
    0x64,0x28,0x70,0x61,0x72,0x61,0x6d,0x5f,0x31,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x68,0x69,0x67,0x68,
    0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x5f,0x31,0x36,0x36,0x20,0x3d,0x20,0x61,
    0x62,0x73,0x28,0x5f,0x31,0x36,0x31,0x20,0x2d,0x20,0x5f,0x31,0x30,0x33,0x29,0x3b,
    0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x69,0x66,0x20,0x28,0x69,0x20,0x21,0x3d,0x20,0x30,0x29,0x0a,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7b,0x0a,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x64,0x78,0x20,0x2b,0x3d,0x20,0x28,0x5f,0x31,0x36,0x31,0x20,0x2f,
    0x20,0x66,0x6c,0x6f,0x61,0x74,0x28,0x69,0x29,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x78,0x73,0x75,0x6d,0x20,0x2b,0x3d,0x20,0x31,0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7d,0x0a,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x69,
    0x66,0x20,0x28,0x6a,0x20,0x21,0x3d,0x20,0x30,0x29,0x0a,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x64,0x79,0x20,0x2b,0x3d,0x20,0x28,0x5f,0x31,0x36,0x31,0x20,0x2f,0x20,0x66,
    0x6c,0x6f,0x61,0x74,0x28,0x6a,0x29,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x79,0x73,
    0x75,0x6d,0x20,0x2b,0x3d,0x20,0x31,0x2e,0x30,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7d,0x0a,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x68,0x69,0x67,
    0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x5f,0x32,0x30,0x31,0x20,0x3d,0x20,
    0x65,0x78,0x70,0x28,0x28,0x28,0x30,0x2e,0x30,0x31,0x39,0x39,0x39,0x39,0x39,0x39,
    0x39,0x35,0x35,0x32,0x39,0x36,0x35,0x31,0x36,0x34,0x31,0x38,0x34,0x35,0x37,0x30,
    0x33,0x31,0x32,0x35,0x20,0x2d,0x20,0x5f,0x31,0x36,0x36,0x29,0x20,0x2a,0x20,0x28,
    0x5f,0x31,0x36,0x36,0x20,0x2d,0x20,0x30,0x2e,0x30,0x31,0x39,0x39,0x39,0x39,0x39,
    0x39,0x39,0x35,0x35,0x32,0x39,0x36,0x35,0x31,0x36,0x34,0x31,0x38,0x34,0x35,0x37,
    0x30,0x33,0x31,0x32,0x35,0x29,0x29,0x20,0x2a,0x20,0x32,0x37,0x37,0x2e,0x37,0x37,
    0x37,0x37,0x37,0x30,0x39,0x39,0x36,0x30,0x39,0x33,0x37,0x35,0x29,0x3b,0x0a,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x68,
    0x69,0x67,0x68,0x70,0x20,0x66,0x6c,0x6f,0x61,0x74,0x20,0x5f,0x32,0x31,0x37,0x20,
    0x3d,0x20,0x65,0x78,0x70,0x28,0x28,0x2d,0x28,0x5f,0x31,0x33,0x36,0x20,0x2a,0x20,
    0x5f,0x31,0x33,0x36,0x20,0x2b,0x20,0x28,0x5f,0x31,0x33,0x39,0x20,0x2a,0x20,0x5f,
    0x31,0x33,0x39,0x29,0x29,0x29,0x20,0x2f,0x20,0x28,0x28,0x5f,0x37,0x34,0x20,0x2a,
    0x20,0x30,0x2e,0x33,0x33,0x33,0x33,0x33,0x33,0x33,0x34,0x33,0x32,0x36,0x37,0x34,
    0x34,0x30,0x37,0x39,0x35,0x38,0x39,0x38,0x34,0x33,0x37,0x35,0x29,0x20,0x2a,0x20,
    0x5f,0x37,0x36,0x29,0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x63,0x6f,0x6c,0x6f,0x72,0x20,0x3d,0x20,0x5f,
    0x31,0x35,0x32,0x20,0x2a,0x20,0x28,0x5f,0x32,0x30,0x31,0x20,0x2a,0x20,0x5f,0x32,
    0x31,0x37,0x29,0x20,0x2b,0x20,0x63,0x6f,0x6c,0x6f,0x72,0x3b,0x0a,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x74,0x6f,0x74,
    0x61,0x6c,0x57,0x65,0x69,0x67,0x68,0x74,0x20,0x3d,0x20,0x5f,0x32,0x30,0x31,0x20,
    0x2a,0x20,0x5f,0x32,0x31,0x37,0x20,0x2b,0x20,0x74,0x6f,0x74,0x61,0x6c,0x57,0x65,
    0x69,0x67,0x68,0x74,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x7d,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x69,0x2b,0x2b,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,
    0x20,0x63,0x6f,0x6e,0x74,0x69,0x6e,0x75,0x65,0x3b,0x0a,0x20,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x7d,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x65,0x6c,0x73,
    0x65,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x62,0x72,0x65,0x61,0x6b,0x3b,0x0a,0x20,
    0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x7d,0x0a,0x20,0x20,0x20,0x20,0x7d,0x0a,0x20,
    0x20,0x20,0x20,0x6f,0x75,0x74,0x5f,0x6e,0x6f,0x72,0x6d,0x61,0x6c,0x20,0x3d,0x20,
    0x76,0x65,0x63,0x34,0x28,0x6e,0x6f,0x72,0x6d,0x61,0x6c,0x69,0x7a,0x65,0x28,0x63,
    0x72,0x6f,0x73,0x73,0x28,0x76,0x65,0x63,0x33,0x28,0x31,0x2e,0x30,0x2c,0x20,0x30,
    0x2e,0x30,0x2c,0x20,0x64,0x78,0x20,0x2f,0x20,0x78,0x73,0x75,0x6d,0x29,0x2c,0x20,
    0x76,0x65,0x63,0x33,0x28,0x30,0x2e,0x30,0x2c,0x20,0x31,0x2e,0x30,0x2c,0x20,0x64,
    0x79,0x20,0x2f,0x20,0x79,0x73,0x75,0x6d,0x29,0x29,0x29,0x2c,0x20,0x31,0x2e,0x30,
    0x29,0x3b,0x0a,0x20,0x20,0x20,0x20,0x69,0x66,0x20,0x28,0x74,0x6f,0x74,0x61,0x6c,
    0x57,0x65,0x69,0x67,0x68,0x74,0x20,0x3d,0x3d,0x20,0x30,0x2e,0x30,0x29,0x0a,0x20,
    0x20,0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x66,0x72,0x61,
    0x67,0x5f,0x63,0x6f,0x6c,0x6f,0x72,0x20,0x3d,0x20,0x5f,0x39,0x31,0x3b,0x0a,0x20,
    0x20,0x20,0x20,0x7d,0x0a,0x20,0x20,0x20,0x20,0x65,0x6c,0x73,0x65,0x0a,0x20,0x20,
    0x20,0x20,0x7b,0x0a,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x20,0x66,0x72,0x61,0x67,
    0x5f,0x63,0x6f,0x6c,0x6f,0x72,0x20,0x3d,0x20,0x63,0x6f,0x6c,0x6f,0x72,0x20,0x2f,
    0x20,0x74,0x6f,0x74,0x61,0x6c,0x57,0x65,0x69,0x67,0x68,0x74,0x3b,0x0a,0x20,0x20,
    0x20,0x20,0x7d,0x0a,0x7d,0x0a,0x0a,0x00,
};
#if !defined(SOKOL_GFX_INCLUDED)
  #error "Please include sokol_gfx.h before depth_blur.h"
#endif
static inline const sg_shader_desc* depth_blur_shader_desc(sg_backend backend) {
  if (backend == SG_BACKEND_GLES3) {
    static sg_shader_desc desc;
    static bool valid;
    if (!valid) {
      valid = true;
      desc.attrs[0].name = "pos";
      desc.vs.source = depth_blur_vs_source_glsl300es;
      desc.vs.entry = "main";
      desc.fs.source = depth_blur_fs_source_glsl300es;
      desc.fs.entry = "main";
      desc.fs.uniform_blocks[0].size = 16;
      desc.fs.uniform_blocks[0].layout = SG_UNIFORMLAYOUT_STD140;
      desc.fs.uniform_blocks[0].uniforms[0].name = "_23.kernelSize";
      desc.fs.uniform_blocks[0].uniforms[0].type = SG_UNIFORMTYPE_INT;
      desc.fs.uniform_blocks[0].uniforms[0].array_count = 1;
      desc.fs.uniform_blocks[0].uniforms[1].name = "_23.scale";
      desc.fs.uniform_blocks[0].uniforms[1].type = SG_UNIFORMTYPE_FLOAT;
      desc.fs.uniform_blocks[0].uniforms[1].array_count = 1;
      desc.fs.uniform_blocks[0].uniforms[2].name = "_23.pnear";
      desc.fs.uniform_blocks[0].uniforms[2].type = SG_UNIFORMTYPE_FLOAT;
      desc.fs.uniform_blocks[0].uniforms[2].array_count = 1;
      desc.fs.uniform_blocks[0].uniforms[3].name = "_23.pfar";
      desc.fs.uniform_blocks[0].uniforms[3].type = SG_UNIFORMTYPE_FLOAT;
      desc.fs.uniform_blocks[0].uniforms[3].array_count = 1;
      desc.fs.images[0].name = "tex";
      desc.fs.images[0].image_type = SG_IMAGETYPE_2D;
      desc.fs.images[0].sampler_type = SG_SAMPLERTYPE_FLOAT;
      desc.label = "depth_blur_shader";
    }
    return &desc;
  }
  return 0;
}
