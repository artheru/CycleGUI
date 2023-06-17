void init_skybox_renderer()
{
	auto depth_blur_shader = sg_make_shader(skybox_shader_desc(sg_query_backend()));
	graphics_state.skybox.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = depth_blur_shader,
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "depth-blur-pipeline"
		});

	graphics_state.skybox.bind = sg_bindings{
		.vertex_buffers = {graphics_state.quad_vertices},
	};
}

void init_point_cloud_renderer()
{
	// debug shader use UV.
	float uv_vertices[] = { 0.0f, 0.0f,  1.0f, 0.0f,  0.0f, 1.0f,  1.0f, 1.0f };
	sg_buffer quad_vbuf = sg_make_buffer(sg_buffer_desc{
		.data = SG_RANGE(uv_vertices),
			.label = "quad vertices"
		});
	graphics_state.dbg.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(dbg_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "dbgvis quad pipeline"
		});
	graphics_state.dbg.bind = sg_bindings{
		.vertex_buffers = {quad_vbuf}		// images will be filled right before rendering
	};

	// Shader program
	auto point_cloud_simple = sg_make_shader(point_cloud_simple_shader_desc(sg_query_backend()));

	// Pipeline state object

	point_cloud_simple_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = point_cloud_simple,
		.layout = {
			.buffers = { {.stride = 16}, {.stride = 16}},
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,  },
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT4 },
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		.index_type = SG_INDEXTYPE_NONE,
		});



	auto edl_shader = sg_make_shader(edl_composer_shader_desc(sg_query_backend()));
	graphics_state.edl_composer.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = edl_shader,
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "edl-composer-pipeline"
		});

	auto depth_blur_shader = sg_make_shader(depth_blur_shader_desc(sg_query_backend()));
	graphics_state.edl_lres_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = depth_blur_shader,
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_DEPTH},
		.colors = {{.pixel_format = SG_PIXELFORMAT_R32F}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "depth-blur-pipeline"
		});

	graphics_state.edl_lres.bind = sg_bindings{
		.vertex_buffers = {quad_vbuf},
	};
}

void GenEDLPasses(int w, int h)
{
	sg_image_desc pc_image_hi = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		.sample_count = 1,
		.min_filter = SG_FILTER_NEAREST,
		.mag_filter = SG_FILTER_NEAREST,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
		.label = "pc-color-image-hires"
	};
	sg_image hi_color = sg_make_image(&pc_image_hi);
	pc_image_hi.pixel_format = SG_PIXELFORMAT_DEPTH;
	pc_image_hi.label = "pc-depth-image-hires";
	sg_image hi_depth = sg_make_image(&pc_image_hi);
	auto hres_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = hi_color} },
		.depth_stencil_attachment = {.image = hi_depth},
		.label = "pc-hi-pass",
		});

	graphics_state.edl_hres = {
		.color = hi_color,
		.depth = hi_depth,
		.pass = hres_pass,
		.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } } },
			.depth = {.store_action = SG_STOREACTION_STORE },
			.stencil = {.store_action = SG_STOREACTION_STORE }
		},
	};


	sg_image_desc pc_image = {
		.render_target = true,
		.width = w / 2,
		.height = h / 2,
		.pixel_format = SG_PIXELFORMAT_R32F,
		.sample_count = 1,
		.min_filter = SG_FILTER_NEAREST,
		.mag_filter = SG_FILTER_NEAREST,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
		.label = "pc-depth-image-lowres"
	};
	sg_image lo_depth = sg_make_image(&pc_image);
	// todo: remove below.
	pc_image.pixel_format = SG_PIXELFORMAT_DEPTH;
	pc_image.label = "pc-lores-gen";
	sg_image ob_low_depth = sg_make_image(&pc_image);

	graphics_state.edl_lres.depth = ob_low_depth;
	graphics_state.edl_lres.color = lo_depth;
	graphics_state.edl_lres.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = lo_depth} },
		.depth_stencil_attachment = {.image = ob_low_depth},
		.label = "pc-lo-pass",
		});
	graphics_state.edl_lres.bind.fs_images[0] = hi_depth;
	
		
	graphics_state.edl_composer.bind = sg_bindings{
		.vertex_buffers = {graphics_state.quad_vertices},
		.fs_images = {hi_depth, lo_depth, hi_color }
	};
}
void ResetEDLPass()
{
	sg_destroy_image(graphics_state.edl_hres.color);
	sg_destroy_image(graphics_state.edl_hres.depth);
	sg_destroy_image(graphics_state.edl_lres.color);
	sg_destroy_image(graphics_state.edl_lres.depth);
	sg_destroy_pass(graphics_state.edl_hres.pass);
	sg_destroy_pass(graphics_state.edl_lres.pass);
}
