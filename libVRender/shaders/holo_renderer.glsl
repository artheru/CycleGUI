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
    vec4 disp_area;                          // unit: pixel. xywh, relative to screen.
    vec4 screen_params;                      // xy: screen xywh
    vec4 fill_color_left, fill_color_right;  // use fill_color to debug
    vec4 lenticular_left;                    // x: phase_init, y: period_total, z: period_fill, w: row_increment
    vec4 lenticular_right;                   // x: phase_init, y: period_total, z: period_fill, w: row_increment

    // sub_pixel_offset.
    vec2 subpx_R, subpx_G, subpx_B;

    // block-rect tuning mode (grid + mask)
    // block_grid_mode: x=cols, y=rows, z=mode(0=off,1=rect_mask), w=reserved
    // block_rect: x0,y0,x1,y1 inclusive block indices, origin at top-left (y=0 top)
    vec4 block_grid_mode;
    vec4 block_rect;

    int mode;                                // 0: Caliberation(default, original blend), 1: View(select closest phase)
    int stripe;                              // 0: no stripe, 1: show diagonal stripe
};

// Textures for left/right views and optional fine index calibration
uniform sampler2D left_im;
uniform sampler2D right_im;
uniform sampler2D idx_fine_caliberation;

out vec4 frag_color;

const float sub_px_len = 1.0 / 3.0; // RGB subpixels

void main() {
    // Optional: only render inside selected block-rect (everything else black)
    if (block_grid_mode.z != 0.0) {
        float cols_f = max(block_grid_mode.x, 0.0);
        float rows_f = max(block_grid_mode.y, 0.0);
        if (cols_f >= 1.0 && rows_f >= 1.0) {
            int cols = int(cols_f);
            int rows = int(rows_f);

            int bx = int(floor(uv.x * float(cols)));
            // uv.y=1 is top edge in our fullscreen quad; convert to top-origin block coords
            int by = int(floor((1.0 - uv.y) * float(rows)));
            bx = clamp(bx, 0, cols - 1);
            by = clamp(by, 0, rows - 1);

            int x0 = int(block_rect.x);
            int y0 = int(block_rect.y);
            int x1 = int(block_rect.z);
            int y1 = int(block_rect.w);
            int xmin = min(x0, x1);
            int xmax = max(x0, x1);
            int ymin = min(y0, y1);
            int ymax = max(y0, y1);

            if (bx < xmin || bx > xmax || by < ymin || by > ymax) {
                frag_color = vec4(0.0, 0.0, 0.0, 1.0);
                return;
            }
        }
    }

    // get screen pixel xy:
    vec2 screen_wh = screen_params.zw;
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

        vec2 biasLR = texture(idx_fine_caliberation, (disp_area.xy + uv * disp_area.zw) / screen_wh).rg;
        float biasL = biasLR.r;
        float biasR = biasLR.g;

        float my_phase_left = my_place.x - (my_place.y * phase_init_row_increment_left + phase_init_left) + period_fill_left/2 + biasL;
        my_phase_left -= round(my_phase_left / period_total_left) * period_total_left;
        float left = 0;

        float my_phase_right = my_place.x - (my_place.y * phase_init_row_increment_right + phase_init_right) + period_fill_right / 2 + biasR;
        my_phase_right -= round(my_phase_right / period_total_right) * period_total_right;
        float right = 0;
        if (mode == 0) {
            // Calibration/original: soft blend within a fill window
            float d_left = abs(my_phase_left);
            float max_left = min(1.0, period_fill_left / sub_px_len);
            if (d_left < period_fill_left / 2) {
                left = max_left;
            }
            else if (d_left < period_fill_left / 2 + sub_px_len / 2) {
                left = max_left * (1.0 - (d_left - period_fill_left / 2) / (sub_px_len / 2.0));
            }
            else {
                left = 0.0;
            }

            float d_right = abs(my_phase_right);
            float max_right = min(1.0, period_fill_right / sub_px_len);
            if (d_right < period_fill_right / 2) {
                right = max_right;
            }
            else if (d_right < period_fill_right / 2 + sub_px_len / 2) {
                right = max_right * (1.0 - (d_right - period_fill_right / 2.0) / (sub_px_len / 2.0));
            }
            else {
                right = 0.0;
            }
        } else {
            // View/new: hard select the view whose phase is closest to 0 (wrap-aware)
            float dL = abs(my_phase_left);
            float dR = abs(my_phase_right);
            if (dL <= dR) { left = 1.0; right = 0.0; }
            else          { left = 0.0; right = 1.0; }
        }

        // todo: for curved display should use eye position to un-distort uv.
        vec2 uv_left = uv;
        vec2 uv_right = uv;

        // Calculate stripe multiplier for fill colors
        float stripe_alpha_left = fill_color_left.a;
        float stripe_alpha_right = fill_color_right.a;
        
        if (stripe != 0) {  // stripe enabled
            float screen_width = screen_wh.x;
            float stripe_period = 0.1 * screen_width;  // stripe width (0.05) + padding (0.05)
            float stripe_width = 0.05 * screen_width;
            
            // For left eye: diagonal from upper-left to lower-right (x + y pattern)
            float diagonal_left = my_place.x + my_place.y;
            float phase_left = mod(diagonal_left, stripe_period);
            float stripe_mult_left = (phase_left < stripe_width) ? 1.0 : 0.0;
            stripe_alpha_left *= stripe_mult_left;
            
            // For right eye: diagonal from upper-right to lower-left (x - y pattern)
            float diagonal_right = my_place.x - my_place.y;
            float phase_right = mod(diagonal_right, stripe_period);
            float stripe_mult_right = (phase_right < stripe_width) ? 1.0 : 0.0;
            stripe_alpha_right *= stripe_mult_right;
        }

        vec3 left_color = texture(left_im, uv_left).rgb * (1 - stripe_alpha_left) + fill_color_left.rgb * stripe_alpha_left;
        vec3 right_color = texture(right_im, uv_right).rgb * (1 - stripe_alpha_right) + fill_color_right.rgb * stripe_alpha_right;

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