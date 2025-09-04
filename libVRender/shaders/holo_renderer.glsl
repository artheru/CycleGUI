@ctype mat4 glm::mat4
@ctype vec2 glm::vec2
@ctype vec3 glm::vec3
@ctype vec4 glm::vec4

@vs holo_screen_vs
in vec2 position;
out vec2 uv;
void main() {
    gl_Position = vec4(position, 0, 1.0); //0.5,1.0
    uv = position * 0.5 + vec2(0.5, 0.5);
}
@end

//
@fs cylindrical_curvedisplay_eye_tracking_fs
in vec2 uv;
uniform cylindrical_curvedisplay_eye_tracking_params{
    vec2 phy_screen_size_mm;             // Physical screen size in mm
    vec2 grating_dir;                // .xy = normalized direction
    float pitch_mm;                  // grating pitch. 
    float global_bias; // also add grating bias.
    float grating_to_screen_mm;      // Distance from grating plane to screen plane
    vec3 left_eye_pos_mm;
    vec3 right_eye_pos_mm;

    vec4 monitor;  // unit: pixel. xywh
    vec4 disp_area; // unit: pixel. xywh

    // z<0 for the curved points (left, right edge @ z=0).
    float curvature_degree; //0 for flat monitor, >0 for curved display, the screen curve totally for what degree.
    float curve_portion; // assue monitor w range from -1~1, only -portion~portion is curved.
};

// Textures for left/right views and optional fine index calibration
uniform sampler2D left;
uniform sampler2D right;
uniform sampler2D idx_fine_caliberation;

out vec4 frag_color;

const int sub_pixel_N = 3; // RGB subpixels

// Rotate a 2D vector by +90 degrees
vec2 rot90(vec2 v){
    return vec2(-v.y, v.x);
}

// Convert a flat position on screen (mm) to a curved surface position (mm, with z<0 when curved)
vec3 getCurvedScreenPosition(vec2 flat_pos_mm) {
    if (curvature_degree <= 0.0 || curve_portion <= 0.0) {
        return vec3(flat_pos_mm, 0.0);
    }
    float curved_angle_rad = curvature_degree * 3.1415926535 / 180.0;
    float screen_half_width = phy_screen_size_mm.x * 0.5;
    float x_centered = flat_pos_mm.x - screen_half_width;
    float arc_half_angle = curved_angle_rad * 0.5;
    if (arc_half_angle < 0.001) {
        return vec3(flat_pos_mm, 0.0);
    }
    float curved_half_width = screen_half_width * curve_portion;
    float radius = curved_half_width / sin(arc_half_angle);
    float z_out;
    float x_out = flat_pos_mm.x;
    if (x_centered < -curved_half_width) {
        float slope = -tan(arc_half_angle);
        float z_end = radius * (1.0 - cos(arc_half_angle));
        z_out = z_end + slope * (x_centered + curved_half_width);
    } else if (x_centered > curved_half_width) {
        float slope = tan(arc_half_angle);
        float z_end = radius * (1.0 - cos(arc_half_angle));
        z_out = z_end + slope * (x_centered - curved_half_width);
    } else {
        float local_x_normalized = x_centered / curved_half_width;
        float theta = local_x_normalized * arc_half_angle;
        float curved_x = radius * sin(theta);
        float curved_z = radius * (1.0 - cos(theta));
        x_out = curved_x + screen_half_width;
        z_out = curved_z;
    }
    return vec3(x_out, flat_pos_mm.y, -z_out);
}

// Surface normal on curved display
vec3 getCurvedScreenNormal(vec2 flat_pos_mm) {
    if (curvature_degree <= 0.0 || curve_portion <= 0.0) {
        return vec3(0.0, 0.0, 1.0);
    }
    float curved_angle_rad = curvature_degree * 3.1415926535 / 180.0;
    float screen_half_width = phy_screen_size_mm.x * 0.5;
    float x_centered = flat_pos_mm.x - screen_half_width;
    float arc_half_angle = curved_angle_rad * 0.5;
    if (arc_half_angle < 0.001) {
        return vec3(0.0, 0.0, 1.0);
    }
    float curved_half_width = screen_half_width * curve_portion;
    if (x_centered < -curved_half_width) {
        return vec3(sin(arc_half_angle), 0.0, cos(arc_half_angle));
    } else if (x_centered > curved_half_width) {
        return vec3(-sin(arc_half_angle), 0.0, cos(arc_half_angle));
    } else {
        float local_x_normalized = x_centered / curved_half_width;
        float theta = local_x_normalized * arc_half_angle;
        return vec3(-sin(theta), 0.0, cos(theta));
    }
}

// Ray-plane intersection, plane defined by point P0 and normal N
vec3 intersectRayPlane(vec3 rayOrigin, vec3 rayDir, vec3 planePoint, vec3 planeNormal) {
    float denom = dot(rayDir, planeNormal);
    float t = (abs(denom) > 1e-6) ? dot(planePoint - rayOrigin, planeNormal) / denom : 0.0;
    return rayOrigin + max(t, 0.0) * rayDir;
}

void main() {
    // 1> gl_FragCoord -> physical screen position xy. x:left2right, y:top2bottom
    vec2 monitor_wh_px = monitor.zw;
    vec2 phy_screen_xy = (gl_FragCoord.xy - monitor.xy) / monitor_wh_px * phy_screen_size_mm; //

    vec2 equi_screen_size_mm = phy_screen_size_mm; // the curve monitor's left right edge, form a new "equivalent" viewscreen.

    vec3 final_color = vec3(0.0);
    float xpitch = pitch_mm * grating_dir.x / max(length(grating_dir.xy), 1e-6); 

    // for left right eye.
    for (int i = 0; i < sub_pixel_N; ++i) // RGB subpixels.
    {
        // Subpixel offsets in pixel units (-1/3, 0, +1/3) along X
        float sub_off = (i == 0 ? -0.33 : (i == 1 ? 0.0 : 0.33));
        vec2 px_size_mm = phy_screen_size_mm / monitor_wh_px;
        vec2 subpixel_screen_xy = phy_screen_xy + vec2(sub_off * px_size_mm.x, 0.0); //apply uniform.
        vec3 subpixel_phy_screen_xyz = getCurvedScreenPosition(subpixel_screen_xy); // apply curvature. z:in2out, with >0 curvature it should be z<0.
        
        // eye-subpixel line, intersects grating surface(move subpixel tangent plane along normal grating_to_screen_mm)
        vec3 subpixel_normal = normalize(getCurvedScreenNormal(subpixel_screen_xy));
        vec3 plane_point = subpixel_phy_screen_xyz + subpixel_normal * grating_to_screen_mm;
        
        // Left eye path
        vec3 le_dir = subpixel_phy_screen_xyz - left_eye_pos_mm;
        vec3 le_gp = intersectRayPlane(left_eye_pos_mm, le_dir, plane_point, subpixel_normal);
        vec2 le_intersect1_xy = le_gp.xy; // project eye_sp_on_grating_plane to subpixel tangent plane, get diff xy, then +subpixel_screen_xy

        // intersect1 to the line pass monitor top-left, dir grating_dir_width_mm.xy, 
        vec2 gdir = normalize(grating_dir.xy);
        vec2 disp_area_mm = disp_area.zw / monitor_wh_px * phy_screen_size_mm;
        vec2 disp_area_LT_mm = (disp_area.xy - monitor.xy) / monitor_wh_px * phy_screen_size_mm;
        float le_intersect1_g_dist = dot(le_intersect1_xy - disp_area_LT_mm, gdir); //
        float le_grating_fidx = le_intersect1_g_dist / pitch_mm + global_bias;
        le_grating_fidx += texture(idx_fine_caliberation, uv).r;;
        int le_idx = int(round(le_grating_fidx));
        vec2 le_nearest_grating_pivot_xy = vec2(le_intersect1_xy.x - xpitch * (le_grating_fidx - float(le_idx)), le_intersect1_xy.y); 

        vec3 le_nearest_grating_pivot_phy_screen_xyz = getCurvedScreenPosition(le_nearest_grating_pivot_xy) + subpixel_normal * grating_to_screen_mm;// apply height (grating_dir.z) and consider curvature.

        // todo: or, we can draw a "ray" from subpixel to eye, and use the distance to grating to decide pixel's display?
        vec3 le_eye_grating_dir = left_eye_pos_mm - le_nearest_grating_pivot_phy_screen_xyz;
        float le_zlen = dot(le_eye_grating_dir, subpixel_normal);
        vec2 le_eye_gc_xy = ((le_eye_grating_dir - le_zlen * subpixel_normal) / max(le_zlen, 1e-6) *  grating_to_screen_mm).xy; //grating_pivot center.
        float d_eye_grating_left = dot(le_eye_gc_xy, rot90(gdir)) ;// eye_xy-> line pass 0 and angle 
    
        vec2 subpixel_gc_xy = subpixel_screen_xy - le_nearest_grating_pivot_xy;
        float d_subpixel_grating_left = dot(subpixel_gc_xy, rot90(gdir)); //

        float g_diff_left = abs(d_eye_grating_left - d_subpixel_grating_left); //vice versa for right.

        // Right eye path
        vec3 re_dir = subpixel_phy_screen_xyz - right_eye_pos_mm;
        vec3 re_gp = intersectRayPlane(right_eye_pos_mm, re_dir, plane_point, subpixel_normal);
        vec2 re_intersect1_xy = re_gp.xy;
        float re_intersect1_g_dist = dot(re_intersect1_xy - disp_area_LT_mm, gdir);
        float re_grating_fidx = re_intersect1_g_dist / pitch_mm + global_bias;
        re_grating_fidx += texture(idx_fine_caliberation, uv).r;;
        int re_idx = int(round(re_grating_fidx));
        vec2 re_nearest_grating_pivot_xy = vec2(re_intersect1_xy.x - xpitch * (re_grating_fidx - float(re_idx)), re_intersect1_xy.y);
        vec3 re_nearest_grating_pivot_phy_screen_xyz = getCurvedScreenPosition(re_nearest_grating_pivot_xy) + subpixel_normal * grating_to_screen_mm;
        vec3 re_eye_grating_dir = right_eye_pos_mm - re_nearest_grating_pivot_phy_screen_xyz;
        float re_zlen = dot(re_eye_grating_dir, subpixel_normal);
        vec2 re_eye_gc_xy = ((re_eye_grating_dir - re_zlen * subpixel_normal) / max(re_zlen, 1e-6) *  grating_to_screen_mm).xy;
        float d_eye_grating_right = dot(re_eye_gc_xy, rot90(gdir));
        float d_subpixel_grating_right = dot(subpixel_screen_xy - re_nearest_grating_pivot_xy, rot90(gdir));
        float g_diff_right = abs(d_eye_grating_right - d_subpixel_grating_right);

        // eye position->subpixelpoint line intersects z=0 plane(equivalent viewscreen)
        vec2 uv_left;
        {
            vec3 dir = subpixel_phy_screen_xyz - left_eye_pos_mm;
            float denom = dir.z;
            float t = (abs(denom) > 1e-6) ? (-left_eye_pos_mm.z) / denom : 0.0;
            vec3 hit = left_eye_pos_mm + t * dir;
            uv_left = (hit.xy - disp_area_LT_mm) / disp_area_mm;
            uv_left.y = 1.0 - uv_left.y;
        }
        vec2 uv_right;
        {
            vec3 dir = subpixel_phy_screen_xyz - right_eye_pos_mm;
            float denom = dir.z;
            float t = (abs(denom) > 1e-6) ? (-right_eye_pos_mm.z) / denom : 0.0;
            vec3 hit = right_eye_pos_mm + t * dir;
            uv_right = (hit.xy - disp_area_LT_mm) / disp_area_mm;
            uv_right.y = 1.0 - uv_right.y;
        }

        vec3 left_color = texture(left, uv_left).rgb;
        vec3 right_color = texture(right, uv_right).rgb;

        vec3 chosen = (g_diff_left <= g_diff_right) ? left_color : right_color;

        // accumulate subpixel contribution
        if (i == 0)      final_color.r += chosen.r;
        else if (i == 1) final_color.g += chosen.g;
        else             final_color.b += chosen.b;
    }

    // normalize accumulated result
    final_color /= float(sub_pixel_N);
    frag_color = vec4(final_color, 1.0);
}
@end

@program cylindrical_curvedisplay_eye_tracking_display holo_screen_vs cylindrical_curvedisplay_eye_tracking_fs