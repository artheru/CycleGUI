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
@fs lenticular_interlace
in vec2 uv;
uniform lenticular_interlace_params{
    // all units in pixel.
    vec4 disp_area;                          // unit: pixel. xywh
    vec4 screen_params;                      // xy: screen_wh, zw: unused
    vec4 fill_color_left, fill_color_right;  // use fill_color to debug
    vec4 lenticular_left;                    // x: phase_init, y: period_total, z: period_fill, w: row_increment
    vec4 lenticular_right;                   // x: phase_init, y: period_total, z: period_fill, w: row_increment

    // sub_pixel_offset.
    vec2 subpx_R, subpx_G, subpx_B; 
};

// Textures for left/right views and optional fine index calibration
uniform sampler2D left_im;
uniform sampler2D right_im;
uniform sampler2D idx_fine_caliberation;

out vec4 frag_color;

const float sub_px_len = 1.0 / 3.0; // RGB subpixels

void main() {
    // get screen pixel xy:
    vec2 screen_wh = screen_params.xy;
    vec2 xy = disp_area.xy + uv * disp_area.zw - screen_wh / 2;

    float phase_init_left = lenticular_left.x;
    float period_total_left = lenticular_left.y;
    float period_fill_left = lenticular_left.z;
    float phase_init_row_increment_left = lenticular_left.w;

    float phase_init_right = lenticular_right.x;
    float period_total_right = lenticular_right.y;
    float period_fill_right = lenticular_right.z;
    float phase_init_row_increment_right = lenticular_right.w;
    
    // Array of subpixel offsets for R, G, B channels
    vec2 subpx_offsets[3];
    subpx_offsets[0] = subpx_R;
    subpx_offsets[1] = subpx_G;
    subpx_offsets[2] = subpx_B;
    
    vec3 final_color = vec3(0, 0, 0);
    for (int i = 0; i < 3; ++i) // RGB subpixels.
    {
        vec2 my_place = xy + subpx_offsets[i];

        float my_phase_left = my_place.x - (my_place.y * phase_init_row_increment_left + phase_init_left);
        my_phase_left -= floor(my_phase_left / period_total_left) * period_total_left;
        float left = 0;
        float max_left = min(1, period_fill_left / sub_px_len);
        float period_fill_left_border = max(0, period_fill_left - sub_px_len);
        float period_fill_left_border2 = min(period_total_left, period_total_left - sub_px_len + period_fill_left);
        if (0.0 <= my_phase_left && my_phase_left < period_fill_left_border) left = max_left;
        else if (period_fill_left_border <= my_phase_left && my_phase_left < period_fill_left)
            left = max_left * (1.0 - (my_phase_left - period_fill_left_border) / sub_px_len);
        else if (period_fill_left < my_phase_left && my_phase_left <= period_total_left - sub_px_len) left = 0.0;
        else if (period_total_left - sub_px_len < my_phase_left && my_phase_left <= period_fill_left_border2)
            left = max_left * (my_phase_left - period_fill_left_border2) / (period_fill_left_border2 - (period_total_left - sub_px_len));
        else left = max_left;

        float my_phase_right = my_place.x - (my_place.y * phase_init_row_increment_right + phase_init_right);
        my_phase_right -= floor(my_phase_right / period_total_right) * period_total_right;
        float right = 0;
        float max_right = min(1.0, period_fill_right / sub_px_len);
        float period_fill_right_border = max(0.0, period_fill_right - sub_px_len);
        float period_fill_right_border2 = min(period_total_right, period_total_right - sub_px_len + period_fill_right);
        if (0.0 <= my_phase_right && my_phase_right < period_fill_right_border) right = max_right;
        else if (period_fill_right_border <= my_phase_right && my_phase_right < period_fill_right)
            right = max_right * (1.0 - (my_phase_right - period_fill_right_border) / sub_px_len);
        else if (period_fill_right < my_phase_right && my_phase_right <= period_total_right - sub_px_len) right = 0.0;
        else if (period_total_right - sub_px_len < my_phase_right && my_phase_right <= period_fill_right_border2)
            right = max_right * (my_phase_right - period_fill_right_border2) / (period_fill_right_border2 - (period_total_right - sub_px_len));
        else right = max_right;

        // todo: for curved display should use eye position to un-distort uv.
        vec2 uv_left = uv;
        vec2 uv_right = uv;

        vec3 left_color = texture(left_im, uv_left).rgb * (1 - fill_color_left.a) + fill_color_left.rgb * fill_color_left.a;
        vec3 right_color = texture(right_im, uv_right).rgb * (1 - fill_color_right.a) + fill_color_right.rgb * fill_color_right.a;

        vec3 chosen = left_color * left + right_color * right;

        //final_color += chosen;
        // accumulate subpixel contribution
        if (i == 0)      final_color.r += chosen.r;
        else if (i == 1) final_color.g += chosen.g;
        else             final_color.b += chosen.b;
    }

    // normalize accumulated result
    frag_color = vec4(final_color, 1.0);
}
@end

@program holo_renderer holo_screen_vs lenticular_interlace