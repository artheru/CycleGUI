@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

@block handle_icon_params
uniform handle_icon_params_t {
    mat4 mvp;
    float dpi;
    float screenW;
    float screenH;
    float behind_opacity;
};
@end

@vs handle_icon_vs
@include_block handle_icon_params

in vec3 position;
in vec2 texcoord0;
// Instance attributes
in vec3 Iposition;
in vec4 Itext_color;
in vec4 Ihandle_color;

out vec2 uv;
out vec4 world_pos;
out vec4 text_color;
out vec4 handle_color;

void main() {
    // Apply instance position
    vec3 pos = position + Iposition;
    world_pos = vec4(pos, 1.0);
    
    // Transform vertex position to clip space
    gl_Position = mvp * world_pos;
    
    // Pass texture coordinates and instance attributes to fragment shader
    uv = texcoord0;
    text_color = Itext_color;
    handle_color = Ihandle_color;
}
@end

@fs handle_icon_fs
@include_block handle_icon_params

in vec2 uv;
in vec4 world_pos;
in vec4 text_color;
in vec4 handle_color;
out vec4 frag_color;

uniform sampler2D font_texture;
uniform sampler2D depth_texture;

void main() {
    // Sample the font texture for the character
    vec4 tex_color = texture(font_texture, uv);
    
    // Calculate screen position for depth test
    vec4 clip_pos = mvp * world_pos;
    vec3 ndc = clip_pos.xyz / clip_pos.w;
    vec2 screen_pos = (ndc.xy * 0.5 + 0.5) * vec2(screenW, screenH);
    
    // Compare depth with the depth buffer
    float depth_sample = texture(depth_texture, screen_pos / vec2(screenW, screenH)).r;
    float current_depth = gl_FragCoord.z;
    
    // Set alpha based on depth test (semi-transparent if behind objects)
    float alpha = (current_depth > depth_sample) ? behind_opacity : 1.0;
    
    // Apply color and transparency:
    // If the texture has a letter pixel (tex_color.a > 0), use text_color
    // Otherwise use handle_color for the background
    vec4 color = mix(handle_color, text_color, step(0.5, tex_color.a));
    float final_alpha = mix(handle_color.a * 0.7, text_color.a, step(0.5, tex_color.a)) * alpha;
    
    frag_color = vec4(color.rgb, final_alpha);
    
    // Discard fully transparent pixels
    if (frag_color.a < 0.01) {
        discard;
    }
}
@end

@program handle_icon handle_icon_vs handle_icon_fs 



@block text_along_line_params
uniform text_along_line_params_t {
    mat4 mvp;
    float dpi;
    int text_id;
    float screenW;
    float screenH;
    float behind_opacity;
    vec4 color;
    vec3 direction;
    int vertical_alignment; // 0=up, 1=middle, 2=down
};
@end

@vs text_along_line_vs
@include_block text_along_line_params

in vec3 position;
in vec2 texcoord0;
in float char_index;

out vec2 uv;
out vec4 world_pos;

void main() {
    // Calculate offset based on character index and direction
    vec3 offset = normalize(direction) * char_index * 0.1; // 0.1 is character spacing
    
    // Apply vertical alignment
    vec3 up_vector = vec3(0.0, 0.0, 1.0); // Default up vector
    vec3 right = normalize(cross(direction, up_vector));
    vec3 up = normalize(cross(right, direction));
    
    float vert_offset = 0.0;
    if (vertical_alignment == 0) { // Up
        vert_offset = 0.05;
    } else if (vertical_alignment == 1) { // Middle
        vert_offset = 0.0;
    } else if (vertical_alignment == 2) { // Down
        vert_offset = -0.05;
    }
    
    // Apply offset
    vec3 adjusted_pos = position + offset + (up * vert_offset);
    world_pos = vec4(adjusted_pos, 1.0);
    
    // Transform to clip space
    gl_Position = mvp * world_pos;
    
    // Pass texture coordinates to fragment shader
    uv = texcoord0;
}
@end

@fs text_along_line_fs
@include_block text_along_line_params

in vec2 uv;
in vec4 world_pos;
out vec4 frag_color;

uniform sampler2D font_texture;
uniform sampler2D depth_texture;

void main() {
    // Sample the font texture
    vec4 tex_color = texture(font_texture, uv);
    
    // Calculate screen position for depth test
    vec4 clip_pos = mvp * world_pos;
    vec3 ndc = clip_pos.xyz / clip_pos.w;
    vec2 screen_pos = (ndc.xy * 0.5 + 0.5) * vec2(screenW, screenH);
    
    // Compare depth with the depth buffer
    float depth_sample = texture(depth_texture, screen_pos / vec2(screenW, screenH)).r;
    float current_depth = gl_FragCoord.z;
    
    // Set alpha based on depth test (semi-transparent if behind objects)
    float alpha = (current_depth > depth_sample) ? behind_opacity : 1.0;
    
    // Apply color and transparency
    frag_color = vec4(color.rgb, color.a * tex_color.a * alpha);
    
    // Discard fully transparent pixels
    if (frag_color.a < 0.01) {
        discard;
    }
}
@end

@program text_along_line text_along_line_vs text_along_line_fs 