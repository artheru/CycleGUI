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
    vec2 screen_wh;
    vec4 fill_color_left, fill_color_right;  // use fill_color to debug
    float phase_init_left, phase_init_right, period_total, period_fill; // a period: first empty, then fill
    float phase_init_row_increment;
};

// Textures for left/right views and optional fine index calibration
uniform sampler2D left_im;
uniform sampler2D right_im;
uniform sampler2D idx_fine_caliberation;

out vec4 frag_color;

const float sub_px_len = 1.0 / 3.0; // RGB subpixels

void main() {
    // get screen pixel xy:
    vec2 xy = disp_area.xy + uv * disp_area.zw - screen_wh / 2;
    vec3 final_color = vec3(0, 0, 0);
    for (int i = 0; i < 3; ++i) // RGB subpixels.
    {
        float my_place_st = xy.x + i * sub_px_len;

        float my_phase_left = my_place_st - (xy.y * phase_init_row_increment + phase_init_left);
        my_phase_left -= floor(my_phase_left / period_total) * period_total;
        float left = 0;
        if (my_phase_left < period_fill - sub_px_len) left = 0;
        else if (my_phase_left < period_fill && period_fill <= my_phase_left + sub_px_len) left = 1 - (period_fill - my_phase_left) / sub_px_len;
        else if (period_fill < my_phase_left && my_phase_left + sub_px_len < period_total) left = 1;
        else left = (period_total - my_phase_left) / sub_px_len;

        float my_phase_right = my_place_st - (xy.y * phase_init_row_increment + phase_init_right);
        my_phase_right -= floor(my_phase_right / period_total) * period_total;
        float right = 0;
        if (my_phase_right < period_fill - sub_px_len) right = 0;
        else if (my_phase_right < period_fill && period_fill <= my_phase_right + sub_px_len) right = 1 - (period_fill - my_phase_right) / sub_px_len;
        else if (period_fill < my_phase_right && my_phase_right + sub_px_len < period_total) right = 1;
        else right = (period_total - my_phase_right) / sub_px_len;

        // todo: for curved display should use eye position to un-distort uv.
        vec2 uv_left = uv;
        vec2 uv_right = uv;

        vec3 left_color = texture(left_im, uv_left).rgb * (1 - fill_color_left.a) + fill_color_left.rgb * fill_color_left.a;
        vec3 right_color = texture(right_im, uv_right).rgb * (1 - fill_color_right.a) + fill_color_right.rgb * fill_color_right.a;

        vec3 chosen = left_color * (1 - left) + right_color * (1 - right);

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