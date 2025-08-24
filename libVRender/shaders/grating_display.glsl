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

    // Sign convention: outward normal toward +Z; for curved display (>0), screen points have z < 0
    float curved_angle_deg; // 0 for flat monitor, >0 for curved display, e.g., 60 deg
    float curved_portion; // -1~1 is screen range, but only -curved_portion~curved_portion actually has curve.
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

// Pass actual 3D screen position (mm) to fragment shader for UV correction
out vec3 screen_pos3d_mm;
// Pass display area conversion (mm) to fragment shader
flat out vec2 disp_area_LT_mm_out;
flat out vec2 disp_area_mm_out;
flat out float curved_flag;

// Function to calculate curved screen position (piecewise: tangent planes + circular arc)
vec3 getCurvedScreenPosition(vec2 flat_pos_mm) {
    if (curved_angle_deg <= 0.0 || curved_portion <= 0.0) {
        // Flat screen - return unchanged position
        return vec3(flat_pos_mm, 0.0);
    }
    
    // Convert angle to radians
    float curved_angle_rad = curved_angle_deg * 3.14159265 / 180.0;
    
    // Screen coordinates centered at origin: -screen_size_mm.x/2 to +screen_size_mm.x/2
    float screen_half_width = screen_size_mm.x * 0.5;
    
    // Convert to screen-centered coordinates
    float x_centered = flat_pos_mm.x - screen_half_width;
    
    // Arc parameters
    float arc_half_angle = curved_angle_rad * 0.5;
    
    // Avoid division by zero
    if (arc_half_angle < 0.001) {
        return vec3(flat_pos_mm, 0.0);
    }
    
    float curved_half_width = screen_half_width * curved_portion;
    float radius = curved_half_width / sin(arc_half_angle);
    
    // End-of-arc z and tangent slope
    float z_end = radius * (1.0 - cos(arc_half_angle));
    
    float x_out = flat_pos_mm.x;
    float z_out;
    
    if (x_centered < -curved_half_width) {
        // Left tangent plane with angle -arc_half_angle
        float slope = -tan(arc_half_angle);
        z_out = z_end + slope * (x_centered + curved_half_width);
    } else if (x_centered > curved_half_width) {
        // Right tangent plane with angle +arc_half_angle
        float slope = tan(arc_half_angle);
        z_out = z_end + slope * (x_centered - curved_half_width);
    } else {
        // Inside arc: map to circular arc
        float local_x_normalized = x_centered / curved_half_width;  // [-1, 1] within curved portion
        float theta = local_x_normalized * arc_half_angle;
        float curved_x = radius * sin(theta);
        float curved_z = radius * (1.0 - cos(theta));
        x_out = curved_x + screen_half_width;
        z_out = curved_z;
    }
    // Sign convention: outward normal points toward +Z, curved surface is located at z < 0
    z_out = -z_out;
    return vec3(x_out, flat_pos_mm.y, z_out);
}

// Function to get curved screen normal at a position (piecewise: tangent planes + circular arc)
vec3 getCurvedScreenNormal(vec2 flat_pos_mm) {
    if (curved_angle_deg <= 0.0 || curved_portion <= 0.0) {
        // Flat screen normal
        return vec3(0.0, 0.0, 1.0);
    }
    
    float curved_angle_rad = curved_angle_deg * 3.14159265 / 180.0;
    float screen_half_width = screen_size_mm.x * 0.5;
    
    // Convert to screen-centered coordinates
    float x_centered = flat_pos_mm.x - screen_half_width;
    
    float arc_half_angle = curved_angle_rad * 0.5;
    
    // Avoid division by zero
    if (arc_half_angle < 0.001) {
        return vec3(0.0, 0.0, 1.0);
    }
    
    float curved_half_width = screen_half_width * curved_portion;
    
    if (x_centered < -curved_half_width) {
        // Left tangent plane: normal tilted by -arc_half_angle around Y
        return vec3(sin(arc_half_angle), 0.0, cos(arc_half_angle));
    } else if (x_centered > curved_half_width) {
        // Right tangent plane: normal tilted by +arc_half_angle around Y
        return vec3(-sin(arc_half_angle), 0.0, cos(arc_half_angle));
    } else {
        // Inside arc
        float local_x_normalized = x_centered / curved_half_width;  // [-1, 1]
        float theta = local_x_normalized * arc_half_angle;
        return vec3(-sin(theta), 0.0, cos(theta));
    }
}

void main()
{
    // todo: to get better visual: 1> use a view-angle varied barrier distance. 2> simply tuncate bad vision
    int grating_id        = gl_VertexIndex / 12;
    int vertex_in_grating = gl_VertexIndex % 12;
    bool is_left         = (vertex_in_grating < 6);
    int vertex_in_eye    = vertex_in_grating % 6;

    gid = start_grating + grating_id;
     
    vec2 localQuad[6];
    localQuad[0] = vec2(-1.0, -1.0);
    localQuad[1] = vec2(1.0, -1.0);
    localQuad[2] = vec2(-1.0, 1.0);
    localQuad[3] = vec2(-1.0, 1.0);
    localQuad[4] = vec2(1.0, -1.0);
    localQuad[5] = vec2(1.0, 1.0);

    // === monitor physical coordinates (y down, x left, z out) ===
    // Get eye position
    vec3 eye_pos = (is_left ? left_eye_pos_mm : right_eye_pos_mm);
    vec3 other_eye_pos = (is_left ? right_eye_pos_mm : left_eye_pos_mm);

    // Calculate grating position
    vec2 grating_dir = normalize(grating_dir_width_mm.xy);
    vec2 perp = vec2(-grating_dir.y, grating_dir.x);
    
    // Position of this grating's center
    vec2 slot_center = gid * perp * grating_dir_width_mm.z;
    
    // For curved displays, we need to project eye position onto the grating line considering curvature
    vec3 eye_project_3d;
    vec3 other_eye_project_3d;
    
    if (curved_angle_deg > 0.0) {
        // For curved screen, find the projection point that accounts for curvature
        // This is an approximation - project onto flat grating line first, then get curved position
        vec2 eye_project_2d = slot_center + dot(eye_pos.xy - slot_center, grating_dir) * grating_dir;
        eye_project_3d = getCurvedScreenPosition(eye_project_2d);
        
        vec2 other_eye_project_2d = slot_center + dot(other_eye_pos.xy - slot_center, grating_dir) * grating_dir;
        other_eye_project_3d = getCurvedScreenPosition(other_eye_project_2d);
    } else {
        // Flat screen calculation
        vec2 eye_project_2d = slot_center + dot(eye_pos.xy - slot_center, grating_dir) * grating_dir;
        eye_project_3d = vec3(eye_project_2d, 0.0);
        
        vec2 other_eye_project_2d = slot_center + dot(other_eye_pos.xy - slot_center, grating_dir) * grating_dir;
        other_eye_project_3d = vec3(other_eye_project_2d, 0.0);
    }
    
    // Calculate viewing angles using curved screen normals
    vec3 eye_to_project = normalize(eye_pos - eye_project_3d);
    vec3 screen_normal = getCurvedScreenNormal(eye_project_3d.xy);
    angle = acos(dot(eye_to_project, screen_normal));

    // Calculate the same angle for the other eye
    vec3 other_eye_to_project = normalize(other_eye_pos - other_eye_project_3d);
    vec3 other_screen_normal = getCurvedScreenNormal(other_eye_project_3d.xy);
    other_angle = acos(dot(other_eye_to_project, other_screen_normal));

    // fix distortion on large angle: by moving slot_center and grating_to_screen_mm.
    float sign = sign(dot(eye_pos.xy - slot_center, perp));
    float angle_compensator = smoothstep(best_viewing_angle.x, best_viewing_angle.y, angle);
    float cgrating_to_screen_mm = grating_to_screen_mm * (1 + angle_compensator * viewing_compensator.x); //trial 1: absolutely no effect.
    slot_center += sign * angle_compensator * viewing_compensator.y * perp * grating_dir_width_mm.z;
    
    // Ray intersection with curved screen
    vec3 ray = vec3(slot_center, cgrating_to_screen_mm) - eye_pos;
    
    vec2 hit_mm;
    if (curved_angle_deg > 0.0) {
        // For curved screen, we need to solve ray-cylinder intersection
        // Simplified approach: project ray onto XY plane first, then apply curvature
        vec2 hit_2d = slot_center.xy + ray.xy * -cgrating_to_screen_mm / ray.z;
        vec3 hit_3d = getCurvedScreenPosition(hit_2d);
        hit_mm = hit_3d.xy;
    } else {
        // Flat screen intersection
        hit_mm = slot_center.xy + ray.xy * -cgrating_to_screen_mm / ray.z;
    }

    // Local offset within the slot
    vec2 half_ext = vec2(slot_width_mm * 3, 2 * screen_size_mm.x + screen_size_mm.y) * 0.5; // it's a line.
    vec2 local_offset = localQuad[vertex_in_eye] * half_ext;
    vec2 final_offset = local_offset.x * perp + local_offset.y * grating_dir;
    
    // Final point in grating plane
    vec2 proj_grating = hit_mm + final_offset;

    // === final ===
    
    // Convert to UV space (0..1)
    vec2 disp_area_mm = disp_area.zw / monitor.zw * screen_size_mm;
    vec2 disp_area_LT_mm = (disp_area.xy - monitor.xy) / monitor.zw * screen_size_mm;
    vec2 rel = (proj_grating - disp_area_LT_mm) / disp_area_mm;
    // outputs for FS
    disp_area_LT_mm_out = disp_area_LT_mm;
    disp_area_mm_out = disp_area_mm;
    curved_flag = curved_angle_deg > 0.0 ? 1.0 : 0.0;
    
    // Convert to NDC space (-1..1)
    vec2 ndc;
    ndc.x = rel.x * 2.0 - 1.0;
    ndc.y = -(rel.y * 2.0 - 1.0);  // Flip Y coordinate
    
    gl_Position = vec4(ndc, 0.0, 1.0);
    
    // UV coordinates
    uv = rel;
    uv.y = 1.0 - rel.y;  // Flip V coordinate

    // Provide 3D screen position for UV correction in FS
    if (curved_angle_deg > 0.0) {
        screen_pos3d_mm = getCurvedScreenPosition(proj_grating);
    } else {
        screen_pos3d_mm = vec3(proj_grating, 0.0);
    }

    left_or_right = (is_left ? 1 : 0);

    // Convert line parameters to pixel space
    lineDir_px = normalize(grating_dir / disp_area_mm * disp_area.zw);
    lineDir_px.y = -lineDir_px.y;
    vec2 center_mm = (hit_mm - disp_area_LT_mm);
    lineCenter_px = center_mm / screen_size_mm * monitor.zw;  // Center in pixels from window origin
    lineCenter_px.y = disp_area.w - lineCenter_px.y;
    lineWidth_px = slot_width_mm / screen_size_mm.x * monitor.zw.x;
}
@end

@fs grating_display_fs

uniform grating_display_fs_params {
    vec2 screen_size_mm;             // Physical screen size in mm
    vec3 left_eye_pos_mm;
    vec3 right_eye_pos_mm;
    //float pupil_factor; 
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

in vec3 screen_pos3d_mm;
flat in vec2 disp_area_LT_mm_out;
flat in vec2 disp_area_mm_out;
flat in float curved_flag;

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
    // Compute corrected UV for curved display by intersecting eye->screen ray with z=0 plane
    vec2 uv_samp = uv;
    if (curved_flag > 0.5) {
        vec3 eye_pos_mm = (left_or_right == 1) ? left_eye_pos_mm : right_eye_pos_mm;
        vec3 dir = screen_pos3d_mm - eye_pos_mm;
        float denom = dir.z;
        if (abs(denom) > 1e-6) {
            float t0 = (-eye_pos_mm.z) / denom;
            vec3 intersect_z = eye_pos_mm + t0 * dir;
            vec2 center_mm = disp_area_LT_mm_out + 0.5 * disp_area_mm_out;
            vec2 uv_fix = (intersect_z.xy - center_mm) / (disp_area_mm_out * 0.5) + vec2(0.5);
            uv_fix.y = 1.0 - uv_fix.y;
            uv_samp = uv_fix;
        }
    }

    // Get base color from texture
    vec4 targetColor, otherColor, baseColor;
    vec4 leftColor = texture(left, uv_samp);
    vec4 rightColor = texture(right, uv_samp);
    
    
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
        baseColor = vec4(left_or_right == 1 && show_left == 1 ? 1 : 0, 0, 
            left_or_right == 0 && show_right == 1 ? 1 : 0, 1);
    
    // Apply sub-pixel coverage
    frag_color = vec4(
        baseColor.r * subPixelCoverage.r,
        baseColor.g * subPixelCoverage.g,
        baseColor.b * subPixelCoverage.b,
        1 // feathering ignored...
    );
}
@end

@program grating_display grating_display_vs grating_display_fs