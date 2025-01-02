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

    vec2 best_viewing_angle; // good angle region, feathering to what angle.
    vec2 viewing_compensator; 
};

out vec2 uv;
// out vec2 pixelPos;

flat out float gid;
flat out int left_or_right;

// NEW: Carry the perpendicular distance from line center to the fragment shader
flat out vec2 lineDir_px;  // Line direction in pixel space
flat out vec2 lineCenter_px;  // Line center in pixel space
flat out float lineWidth_px;  // Line width in pixels

flat out float angle;
flat out float other_angle;

void main()
{
    // todo: to get better visual: 1> use a view-angle varied barrier distance. 2> simply tuncate bad vision
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
    vec3 other_eye_pos = (is_left ? right_eye_pos_mm : left_eye_pos_mm);

    // Calculate grating position
    vec2 grating_dir = normalize(grating_dir_width_mm.xy);
    vec2 perp = vec2(-grating_dir.y, grating_dir.x);
    
    // Position of this grating's center
    vec2 slot_center = gid * perp * grating_dir_width_mm.z;
    
    // viewing angle: plane of ln(eye_pos->slot_center)-ln(slot_center->grating_dir) vs screen(z=0).
    // first project eye_pos on grating line.
    vec3 eye_project = vec3(slot_center + dot(eye_pos.xy - slot_center, grating_dir) * grating_dir, 0);
    // then use eye_project to calculate angle.
    vec3 eye_to_project = normalize(eye_pos - eye_project);
    vec3 screen_normal = vec3(0.0, 0.0, 1.0);
    angle = acos(dot(eye_to_project, screen_normal));

    // Calculate the same angle for the other eye
    vec3 other_eye_project = vec3(slot_center + dot(other_eye_pos.xy - slot_center, grating_dir) * grating_dir, 0);
    vec3 other_eye_to_project = normalize(other_eye_pos - other_eye_project);
    other_angle = acos(dot(other_eye_to_project, screen_normal));

    // fix distortion on large angle: by moving slot_center and grating_to_screen_mm.
    float sign = sign(dot(eye_pos.xy - slot_center, perp));
    float angle_compensator = smoothstep(best_viewing_angle.x, best_viewing_angle.y, angle);
    float cgrating_to_screen_mm = grating_to_screen_mm * (1 + angle_compensator * viewing_compensator.x); //trial 1: absolutely no effect.
    slot_center += sign * angle_compensator * viewing_compensator.y * perp * grating_dir_width_mm.z;
    
    // Ray intersection with screen (z=0)
    vec3 ray = vec3(slot_center, cgrating_to_screen_mm) - eye_pos;
    vec2 hit_mm = slot_center.xy + ray.xy * -cgrating_to_screen_mm/ ray.z;

    // Local offset within the slot
    vec2 half_ext = vec2(slot_width_mm * 4, screen_size_mm.x + screen_size_mm.y) * 0.5; // it's a line.
    vec2 local_offset = localQuad[vertex_in_eye] * half_ext;
    vec2 final_offset = local_offset.x * perp + local_offset.y * grating_dir;
    
    // Final point in grating plane
    vec2 proj_grating = hit_mm + final_offset;

    // === final ===
    
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

    // Convert line parameters to pixel space
    lineDir_px = normalize(grating_dir / disp_area_mm * disp_area.zw);
    lineDir_px.y = -lineDir_px.y;
    vec2 center_mm = (hit_mm - disp_area_LT_mm);
    lineCenter_px = center_mm / screen_size_mm * monitor.zw;  // Center in pixels from window origin
    lineCenter_px.y = disp_area.w - lineCenter_px.y;
}
@end

@fs grating_display_fs

uniform grating_display_fs_params {
    vec2 screen_size_mm;             // Physical screen size in mm
    vec3 left_eye_pos_mm;
    vec3 right_eye_pos_mm;
    float pupil_factor; 
    float slot_width_mm;
    //float feather_width_mm;

    int debug, show_left, show_right;

    vec2 offset; // gl_fragcoord offset.
    
    vec2 best_viewing_angle; // good angle region, feathering to what angle.
    vec2 leakings;
    vec2 dims; // leaking_dim, impacted_dim, 
};

uniform sampler2D left;
uniform sampler2D right;

in vec2 uv;
//in vec2 pixelPos;

flat in float gid;
flat in int left_or_right;
flat in vec2 lineDir_px;
flat in vec2 lineCenter_px;
flat in float lineWidth_px;

flat in float angle;
flat in float other_angle;

out vec4 frag_color;

// Sub-pixel offsets (relative to pixel center, in pixel units)

float getSubpixelCoverage(vec2 pixelPos, vec2 subPixelOffset) {
    // Calculate distance from point to line
    vec2 toPixel = pixelPos + subPixelOffset - lineCenter_px;
    
    float dist = abs(dot(toPixel, vec2(lineDir_px.y, -lineDir_px.x)));

    // Compute coverage using smoothstep 
    return 1.0 - smoothstep(lineWidth_px * 0.5,
        lineWidth_px * 0.5 + 0.5,
        dist);
}

void main()
{
    // Get base color from texture
    vec4 targetColor, otherColor, baseColor;
    vec2 midpnt = 0.5 * (left_eye_pos_mm.xy + right_eye_pos_mm.xy);
    vec4 leftColor = texture(left, uv - pupil_factor * (left_eye_pos_mm.xy - midpnt) / screen_size_mm);
    vec4 rightColor = texture(right, uv - pupil_factor * (right_eye_pos_mm.xy - midpnt) / screen_size_mm);
    
    
    float base_e = leakings.x; // base leaking.
    float angfac = smoothstep(best_viewing_angle.x, best_viewing_angle.y, max(other_angle, angle));
    float ex = base_e + (1 - base_e) * angfac * leakings.y;

    if (left_or_right == 1) {
        if (show_left == 1){
            targetColor = leftColor;
            otherColor = rightColor;
        }
    } else {
        if (show_right == 1){
            targetColor = rightColor;
            otherColor = leftColor;
        }
    } 
    vec2 dim = dims + (1 - dims) * (1 - angfac);

    float other_fac = ex, my_fac = base_e, tf = dim.y, of = dim.x, tb = dim.x * ex, ob = (1 - angfac) * dim.x * ex; // angle>other_angle, aka other leaks a lot affecting visual.
    if (other_angle > angle) {
        my_fac = ex;
        other_fac = base_e;
        of = dim.y;
        tf = dim.x;
        ob = dim.y * ex;
        tb = (1 - angfac) * dims.y * ex;
    }

    // 1> actual_otherColor * other_fac + actual_targetColor = targetColor * tf + tb;
    // 2> actual_targetColor * my_fac + actual_otherColor = otherColor * of + ob;
    // Solve actual colors.
    float det = 1 - my_fac * other_fac;
    vec4 actual_targetColor = ((targetColor * tf + tb) - other_fac * (otherColor * of + ob)) / det;
    //vec4 actual_otherColor = ((otherColor * of + ob) - my_fac * (targetColor * tf + tb)) / det;

    baseColor = actual_targetColor;
    baseColor.w = 1;
    

    // Calculate fragment position in pixel coordinates relative to display area
    vec2 pixelPos = gl_FragCoord.xy - offset;

    // Calculate sub-pixel coverage
    vec3 subPixelCoverage;
    subPixelCoverage.x = getSubpixelCoverage(pixelPos, vec2(-0.33, 0.0));
    subPixelCoverage.y = getSubpixelCoverage(pixelPos, vec2(0.0, 0.0));
    subPixelCoverage.z = getSubpixelCoverage(pixelPos, vec2(0.33, 0.0));
    
    if (debug == 1)
        baseColor = vec4(left_or_right == 1 ? 1 : 0, 0, left_or_right == 0 ? 1 : 0, 1);

    // Apply sub-pixel coverage
    frag_color = vec4(
        baseColor.r * subPixelCoverage.r,
        baseColor.g * subPixelCoverage.g,
        baseColor.b * subPixelCoverage.b,
        1 // feathering ignored...
    );

    //if (left_or_right == 1){
    //    if (show_left == 1)
    //        frag_color = vec4(angle, 0, 0, 1);
    //    else frag_color = frag_color * 0.01;
    //}
    //else {
    //    if (show_right == 1)
    //        frag_color = vec4(0, 0, angle, 1);
    //    else frag_color = frag_color * 0.01;
    //}

    //vec2 diff = pixelPos - lineCenter_px;
    //vec2 perp = vec2(lineDir_px.y, -lineDir_px.x);
    //frag_color = frag_color * 0.001 + vec4(abs(dot(diff, perp))*0.03, abs(length(perp)-1)*100,0, 1);
}
@end

@program grating_display grating_display_vs grating_display_fs