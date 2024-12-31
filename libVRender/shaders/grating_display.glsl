@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

@vs grating_display_vs
uniform grating_display_vs_params {
    vec2 screen_size_mm;             // Physical screen size in mm
    vec3 grating_dir_width_mm;       // .xy = normalized direction, .z = grating width
    float grating_to_screen_mm;      // Distance from grating plane to screen plane
    float slot_width_mm;             // On-Screen width of the projected grating 
    vec3 left_eye_pos_mm;
    vec3 right_eye_pos_mm;

    vec4 monitor;  // unit: pixel. xywh
    vec4 disp_area; // unit: pixel. xywh
    float start_grating; // also add grating bias.
};

out vec2 uv;
flat out float gid;
flat out int left_or_right;

// NEW: Carry the perpendicular distance from line center to the fragment shader
out float lineDist;

void main()
{
    int grating_id        = gl_VertexIndex / 12;
    int vertex_in_grating = gl_VertexIndex % 12;
    bool is_left         = (vertex_in_grating < 6);
    int vertex_in_eye    = vertex_in_grating % 6;

    gid = start_grating + grating_id;

    vec2 localQuad[6] = vec2[](
        vec2(-1.0, -1.0),
        vec2( 1.0, -1.0),
        vec2(-1.0,  1.0),
        vec2(-1.0,  1.0),
        vec2( 1.0, -1.0),
        vec2( 1.0,  1.0)
    );

    // === monitor physical coordinates (y down, x left, z out) ===
    // Get eye position
    vec3 eye_pos = (is_left ? left_eye_pos_mm : right_eye_pos_mm);

    // Calculate grating position
    vec2 grating_dir = normalize(grating_dir_width_mm.xy);
    vec2 perp = vec2(-grating_dir.y, grating_dir.x);
    
    // Position of this grating's center
    vec2 slot_center = gid * perp * grating_dir_width_mm.z;
    
    // Ray intersection with screen (z=0)
    // todo: fix distortion on large angle. first make h_eye that project eye_pos on grating line,
    //       then, test the angle. process non-linearity.
    //       finally add back ray.xy.
    vec3 ray = vec3(slot_center, grating_to_screen_mm) - eye_pos;
    vec2 hit_mm = slot_center.xy + ray.xy * -grating_to_screen_mm/ ray.z;

    // Local offset within the slot
    vec2 half_ext = vec2(slot_width_mm * 4, screen_size_mm.x + screen_size_mm.y) * 0.5; // it's a line.
    vec2 local_offset = localQuad[vertex_in_eye] * half_ext;
    vec2 final_offset = local_offset.x * perp + local_offset.y * grating_dir;
    
    // Final point in grating plane
    vec2 proj_grating = hit_mm + final_offset;
    // === fin ===
    
    // Convert to UV space (0..1)
    vec2 disp_area_mm = disp_area.zw / monitor.zw * screen_size_mm;
    vec2 disp_area_LT_mm = (disp_area.xy - monitor.xy) / monitor.zw * screen_size_mm;
    vec2 rel = (proj_grating - disp_area_LT_mm) / disp_area_mm;
    
    // Convert to NDC space (-1..1)
    vec2 ndc;
    ndc.x = rel.x * 2.0 - 1.0;
    ndc.y = -(rel.y * 2.0 - 1.0);  // Flip Y coordinate
    
    gl_Position = vec4(ndc, 0.0, 1.0);
    
    // UV coordinates
    uv = rel;
    uv.y = 1.0 - rel.y;  // Flip V coordinate

    left_or_right = (is_left ? 1 : 0);

    // NEW: Compute perpendicular distance from the center of the line
    // local_offset.x is along 'perp' direction (the line thickness axis).
    lineDist = local_offset.x;
}
@end

@fs grating_display_fs

uniform grating_display_fs_params {
    vec2 screen_size_mm;             // Physical screen size in mm
    vec3 left_eye_pos_mm;
    vec3 right_eye_pos_mm;
    float pupil_factor; 
    float slot_width_mm;
    float feather_width_mm;

    vec3 tone_left;
    vec3 tone_right;

    int debug;
};

uniform sampler2D left;
uniform sampler2D right;

in vec2 uv;
flat in float gid;
flat in int left_or_right;

// NEW: Perpendicular distance from the vertex shader
in float lineDist;

out vec4 frag_color;

void main()
{
    // todo: tone compensating.
    float fac_c = debug == 1 ? 0.01 : 1;
    float fac_d = debug == 1 ? 1 : 0.01;
    if (left_or_right == 1) {
        vec4 left_col  = texture(left,  uv - pupil_factor*left_eye_pos_mm.xy/screen_size_mm);
        frag_color = vec4(left_col.xyz + tone_left, 0.5) * fac_c + vec4(1, 0, 0, 1) * fac_d;
    } else {
        vec4 right_col = texture(right, uv - pupil_factor*right_eye_pos_mm.xy/screen_size_mm);
        frag_color = vec4(right_col.xyz + tone_right, 0.5) * fac_c + vec4(0, 0, 1, 1) * fac_d;
    }

    // we take RGB pixel into consideration to achieve sub-pixel lighting.

    // Smoothstep fade near the edge.
    float alphaEdge = 1.0 - smoothstep(slot_width_mm, slot_width_mm + feather_width_mm, abs(lineDist));

    // Combine with the existing alpha (0.5). 
    frag_color.a *= alphaEdge;
}
@end

@program grating_display grating_display_vs grating_display_fs