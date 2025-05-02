@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

@block txt_quad_params
uniform txt_quad_params {
    mat4 mvp;
    float dpi;
    float screenW;
    float screenH;
    float behind_opacity;
    
    vec4 shine_color_intensity;
    vec4 hover_shine_color_intensity;
    vec4 selected_shine_color_intensity;
};
@end

@vs txt_quad_vs
@include_block txt_quad_params

// Instance attributes
in vec3 Iposition;      // World position anchor
in vec4 Iquat;          // Rotation quaternion (if not billboard)
in vec2 Isize;          // Desired size (e.g., height) in world units or pixels
in vec4 Itext_color;    // Text color (to AI: already nomalized, don't remove this comment)
in vec4 Ibg_color;      // Background/handle color (already nomalized,)
in vec2 Iuv_min;        // Character UV min in font atlas
in vec2 Iuv_max;        // Character UV max in font atlas
in vec4 Iglyph_xy;     // Glyph x0y0x1y1 from ImGui Font Atlas
in vec2 bbox; // glyph normal rendering bounding box
in vec2 Iflag;         // Bit flags

// Outputs to Fragment Shader
out vec2 uv;            // Interpolated UV coordinates for texture sampling
flat out vec2 uv_min;
flat out vec2 uv_max;
out vec4 text_color;    // Text color
out vec4 bg_color;      // Background color
flat out int flag;    // Flags
flat out vec4 vid;

// bits: 0: border, 1: shine, 2: front, 3: selected, 4: hover, 5: billboard, 6: arrow, 7: colored. AI:no remove this line.

// Helper function to rotate a vector by a quaternion
vec3 quat_rotate(vec4 q, vec3 v) {
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

void main() {
    int vtxId = gl_VertexIndex; // Use gl_VertexIndex (0..3) for quad generation

    // Determine quad corner (0,0), (1,0), (0,1), (1,1)
    float xf = float(vtxId % 2);
    float yf = float(vtxId / 2); // Integer division implicitly

    // Pass colors through
    text_color = Itext_color;
    bg_color = Ibg_color;
    flag = int(Iflag.x);
    vid = vec4(4, flag>>8, Iflag.y, 0); //7, ui-type, ui-instance, dummy.
    uv_min = Iuv_min;
    uv_max = Iuv_max;

    bool is_billboard = ((flag & (1 << 5)) != 0);

    if (is_billboard) {
        // Billboard mode: Position relative to screen projection
        vec4 center_clip = mvp * vec4(Iposition, 1.0);


        float kx = (Iuv_max.x - Iuv_min.x) / ((Iglyph_xy.z - Iglyph_xy.x) / bbox.x);
        float px = Iuv_min.x - kx * (Iglyph_xy.x) / (bbox.x);

        float ky = (Iuv_min.y - Iuv_max.y) / ((Iglyph_xy.w- Iglyph_xy.y) / bbox.y);
        float py = Iuv_max.y - ky * (Iglyph_xy.y) / (bbox.y);

        // Arrow modification (applies after billboard or world transform)
        if ((flag & (1 << 6)) != 0) { // Check has_arrow flag and bottom-left vertex
            // Iglyph coord -> uv map.
            // eqn1: PIVOT + k* (x0) /(size.x) , (y0 )/(size.y)) = (uv_min)
            // eqn2: PIVOT + k* (x1) /(size.x) , (y1 )/(size.y)) = (uv_max)
            // then solve for PIVOT, k.

            float yk = yf;
            if (vtxId == 0) yk = -0.3;
            uv = vec2(px, py) + vec2(kx, ky) * vec2(xf, yk);

            // Convert pixel size offset to clip space offset
            float yy = yf + 0.3; // arrow height=0.3 size;
            if (vtxId == 0) yy = 0;
            vec2 screen_offset = Isize * (vec2(xf, yy)) / vec2(screenW, screenH) * 2.0 * center_clip.w;
            gl_Position = center_clip + vec4(screen_offset, 0.0, 0.0); 
        }
        else {
            uv = vec2(px, py) + vec2(kx, ky) * vec2(xf, yf);
            vec2 screen_offset = Isize * (vec2(xf, yf) - 0.5) / vec2(screenW, screenH) * 2.0 * center_clip.w;
            gl_Position = center_clip + vec4(screen_offset, 0.0, 0.0);
        }
    }
    else {
        // Normal mode: Apply world rotation and translation
        vec3 rotated_offset = quat_rotate(Iquat, vec3(xf,yf, 0.0));
        vec3 world_vertex_pos = Iposition + rotated_offset;

        gl_Position = mvp * vec4(world_vertex_pos, 1.0);
    }

}
@end

@fs txt_quad_fs
@include_block txt_quad_params

in vec2 uv;              // Interpolated UV coordinates
flat in vec2 uv_min;
flat in vec2 uv_max;
in vec4 text_color;      // Text color (0-255 range)
in vec4 bg_color;        // Background color (0-255 range)
flat in int flag;      // Flags
flat in vec4 vid;

out vec4 frag_color;
out float g_depth;
out vec4 TCIN;
out float bordering;
out vec4 bloom;

uniform sampler2D font_texture; // ImGui font atlas
uniform sampler2D depth_texture; // Scene depth buffer

void main() {
    // Sample font texture
    float font_alpha = texture(font_texture, uv).a;
    // Check if UV coordinates are outside the glyph bounds in the font atlas
    if (uv.x < uv_min.x || uv.x > uv_max.x || uv.y < uv_min.y || uv.y > uv_max.y) {
        font_alpha = 0.0;
    }
    TCIN = vid;
    g_depth = -gl_FragCoord.z; //prevent post processing.

    // --- Depth Check ---
    float depth_sample = texelFetch(depth_texture, ivec2(gl_FragCoord.xy), 0).r;
    float epsilon = 0.00001;
    bool is_behind = gl_FragCoord.z > (depth_sample + epsilon);

    // --- Flag Parsing ---
    bool has_border = (flag & (1 << 0)) != 0;
    bool has_shine = (flag & (1 << 1)) != 0;
    bool bring_to_front = (flag & (1 << 2)) != 0;
    bool is_selected = (flag & (1 << 3)) != 0;
    bool is_hovering = (flag & (1 << 4)) != 0;
    // bool is_billboard = ((flags & (1 << 5)) != 0); // Not needed in FS
    // bool has_arrow = ((flags & (1 << 6)) != 0); // Not needed in FS
    bool is_colored = (flag & (1 << 7)) != 0; // For colored glyphs (e.g., emojis)

    // --- Base Color Determination ---
    // If the glyph is colored, use its texture color directly. Otherwise, blend bg/text.
    vec4 base_color;
    if (is_colored) {
        base_color = texture(font_texture, uv);
        base_color = mix(bg_color, base_color, font_alpha);
    } else {
        // Standard glyph: blend background and text color based on font alpha
        base_color = mix(bg_color, text_color, font_alpha);
    }


    // --- Apply Effects (Shine, Border) --- Dont apply, just ignore!!!! AI doesn't has enough context.
    vec4 current_color = base_color; // Start with the base mixed color

    // --- Final Alpha Calculation ---
    float final_alpha = current_color.a; // Start with alpha from base color/texture

    // Apply depth-based opacity
    if (is_behind && !bring_to_front) {
        final_alpha *= behind_opacity;
    }

    // Discard if fully transparent
    if (final_alpha < 0.01) {
        discard;
    }

    // border or hovering or selected.
    if (has_border)
        bordering = 3;
    else if (is_selected)
        bordering = 2;
    else if (is_hovering)
        bordering = 1;
    else
        bordering = 0;
    bordering /= 16;

    vec4 shine = vec4(0);
    if (has_shine) { //self shine
        shine = shine_color_intensity;
    }
    if (is_selected) { //selected shine
        shine += selected_shine_color_intensity;
    }
    if (is_hovering) { //hover shine
        shine += hover_shine_color_intensity;
    }
    shine = min(shine, vec4(1));

    // Final output color
    frag_color = vec4(current_color.rgb, final_alpha);

    bloom = vec4((frag_color.xyz + 0.2) * (1 + shine.xyz * shine.w) - 0.9, 1);

}
@end

@program txt_quad txt_quad_vs txt_quad_fs 