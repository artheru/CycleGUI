#include "shaders/shaders.h"
#include "me_impl.h"

void init_skybox_renderer()
{
	auto depth_blur_shader = sg_make_shader(skybox_shader_desc(sg_query_backend()));
	graphics_state.skybox.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = depth_blur_shader,
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "background_shader-pipeline"
		});

	graphics_state.skybox.bind = sg_bindings{
		.vertex_buffers = {graphics_state.quad_vertices},
	};

	
	// Shader program
	graphics_state.foreground.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(after_shader_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "foreground_shader-pipeline"
		});
}

void init_line_renderer()
{
	graphics_state.line_bunch = {
		.pass_action = sg_pass_action{
			.colors = {
				{
					.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE,
					.clear_value = {0.0f, 0.0f, 0.0f, 0.0f}
				},
				{.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, .clear_value = {1.0f}},
				{
					.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE,
					.clear_value = {-1.0f, 0.0, 0.0, 0.0}
				}, //tcin buffer.
				// ui:
				{.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, .clear_value = {0.0f}},
				{.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, .clear_value = {0.0f}}
			},
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE,},
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE}
		},
		.line_bunch_pip = sg_make_pipeline(sg_pipeline_desc{
			.shader = sg_make_shader(linebunch_shader_desc(sg_query_backend())),
			.layout = {
				.buffers = {
					{.stride = 32, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
				}, //position
				.attrs = {
					{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,},
					{.buffer_index = 0, .offset = 12, .format = SG_VERTEXFORMAT_FLOAT3},
					{.buffer_index = 0, .offset = 24, .format = SG_VERTEXFORMAT_UBYTE4}, //arrow|dash|width|NA
					{.buffer_index = 0, .offset = 28, .format = SG_VERTEXFORMAT_UBYTE4}, //color
				},
			},
			.depth = {
				.pixel_format = SG_PIXELFORMAT_DEPTH,
				.compare = SG_COMPAREFUNC_LESS_EQUAL,
				.write_enabled = true,
			},
			.color_count = 5,
			.colors = {
				{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = false}},
				{.pixel_format = SG_PIXELFORMAT_R32F}, // g_depth
				{.pixel_format = SG_PIXELFORMAT_RGBA32F},

				{.pixel_format = SG_PIXELFORMAT_R8},
				{.pixel_format = SG_PIXELFORMAT_RGBA8},
			},
			.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
			.index_type = SG_INDEXTYPE_NONE,
			.sample_count = OFFSCREEN_SAMPLE_COUNT,
		})
	};
}

void init_ssao_shader()
{
	graphics_state.ssao.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(ssao_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_NONE ,
		},
		.colors={{.pixel_format = SG_PIXELFORMAT_R8}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "ssao quad pipeline"
		});
	graphics_state.ssao.bindings= sg_bindings{
		.vertex_buffers = {graphics_state.uv_vertices}		// images will be filled right before rendering
	};
}
void screen_init_ssao_buffers(int w, int h)
{
	sg_image ssao_image = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = (int)(w*0.5),
		.height = (int)(h*0.5),
		.pixel_format = SG_PIXELFORMAT_R8,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
		.label = "p-ssao-image"
	});
	//sg_image ssao_blur = sg_make_image(&pc_image_hi);
	graphics_state.ssao.image = ssao_image;
	graphics_state.ssao.bindings.fs_images[0] = graphics_state.primitives.depth;
	graphics_state.ssao.bindings.fs_images[1] = graphics_state.primitives.normal;
	graphics_state.ssao.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = ssao_image} },
		//.depth_stencil_attachment = {.image = graphics_state.primitives.depthTest},
		.label = "SSAO"
		});
	graphics_state.ssao.pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } } },
		//.depth = {.load_action = SG_LOADACTION_CLEAR}
	};
}
void destroy_ssao_buffers()
{
	sg_destroy_image(graphics_state.ssao.image);
	sg_destroy_image(graphics_state.ssao.blur_image);
	sg_destroy_pass(graphics_state.ssao.pass);
	sg_destroy_pass(graphics_state.ssao.blur_pass);
}

void init_bloom_shaders()
{
	graphics_state.ui_composer.pip_dilateX = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bloomDilateX_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.colors = {{.pixel_format = SG_PIXELFORMAT_RGBA8}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
	});
	graphics_state.ui_composer.pip_dilateY = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bloomDilateY_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.colors = {{.pixel_format = SG_PIXELFORMAT_RGBA8}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		});
	graphics_state.ui_composer.pip_blurX = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bloomblurX_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.colors = {{.pixel_format = SG_PIXELFORMAT_RGBA8}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		});
	graphics_state.ui_composer.pip_blurYFin = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bloomblurYFin_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE,
				.op_rgb = SG_BLENDOP_ADD,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
	});
}

void screen_init_bloom(int w, int h)
{
	sg_image_desc bs_desc = {
		.render_target = true,
		.width = w/2,
		.height = h/2,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
	};
	graphics_state.bloom = sg_make_image(&bs_desc);
	graphics_state.shine2 = sg_make_image(&bs_desc);
}
void destroy_screen_bloom()
{
	
}

void init_ground_effects()
{
	graphics_state.ground_effect.spotlight_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_ground_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 12}, // position
				{.stride = 12, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
			}, //position
			.attrs = {
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3, },
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3 },
			},
		},
		// todo: don't need high resolution.
		//.depth = {
		//	.pixel_format = SG_PIXELFORMAT_NONE,
		//},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ZERO}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
		.index_type = SG_INDEXTYPE_UINT16,
		.cull_mode = SG_CULLMODE_BACK,
		.face_winding = SG_FACEWINDING_CW,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		});

	float ground_vtx[] = {
		-1,1,0,
		1,1,0,
		1,-1,0,
		-1,-1,0
	};
	short ground_indices[] = { 0,1,2,2,3,0 };
	graphics_state.ground_effect.spotlight_bind = sg_bindings{
		.vertex_buffers = {sg_make_buffer(sg_buffer_desc{.data = SG_RANGE(ground_vtx)}),},
		.index_buffer = {sg_make_buffer(sg_buffer_desc{.type = SG_BUFFERTYPE_INDEXBUFFER ,.data = SG_RANGE(ground_indices)})}, // slot 1 for instance per.
	};

	graphics_state.ground_effect.cs_ssr_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(ground_effect_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_NONE,
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ZERO}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		});
}
void screen_init_ground_effects(int w, int h)
{
	sg_image_desc bs_desc = {
		.render_target = true,
		.width = w/2,
		.height = h/2,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
	};
	graphics_state.ground_effect.ground_img = sg_make_image(&bs_desc);
	graphics_state.ground_effect.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = graphics_state.ground_effect.ground_img} ,
		},
		.label = "ground-effects"
		});
	graphics_state.ground_effect.pass_action = sg_pass_action{
		.colors = {
			{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE,  .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
		},
	};
	graphics_state.ground_effect.bind = sg_bindings{
		.vertex_buffers = {graphics_state.quad_vertices},
		.fs_images = {graphics_state.primitives.depth, graphics_state.primitives.color }
	};
}
void destroy_screen_ground_effects()
{
	sg_destroy_image(graphics_state.ground_effect.ground_img);
	sg_destroy_pass(graphics_state.ground_effect.pass);
}

void init_sprite_images()
{
	graphics_state.sprite_render = {
		.pass_action = sg_pass_action{
			.colors = {
				{ .load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, .clear_value = {0.0f, 0.0f, 0.0f, 0.0f} 	},
				{.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, .clear_value = {1.0f}},
				{	.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE,.clear_value = {-1.0f, 0.0, 0.0, 0.0}}, //tcin buffer.
				{.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, .clear_value = {0.0f}},
				{.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, .clear_value = {0.0f}},
				{ .load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {-1.0f} } //occupies (RGB idx)
			},
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE,},
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE}
		},
		.stat_pass_action =  sg_pass_action{
			.colors = {
				{ .load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE,  .clear_value = {-1.0f}	},
			},
		},
		.quad_image_pip = sg_make_pipeline(sg_pipeline_desc{
			.shader = sg_make_shader(p_sprite_shader_desc(sg_query_backend())),
			.layout = {
				.buffers = {
					{.stride = 64, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
				}, //position
				.attrs = {
					{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,}, //pos
					{.buffer_index = 0, .offset = 12, .format = SG_VERTEXFORMAT_FLOAT}, //flag
					{.buffer_index = 0, .offset = 16, .format = SG_VERTEXFORMAT_FLOAT4}, //quat
					{.buffer_index = 0, .offset = 32, .format = SG_VERTEXFORMAT_FLOAT2}, //dispWH
					{.buffer_index = 0, .offset = 40, .format = SG_VERTEXFORMAT_FLOAT2}, //uvLT
					{.buffer_index = 0, .offset = 48, .format = SG_VERTEXFORMAT_FLOAT2}, //uvRB
					{.buffer_index = 0, .offset = 56, .format = SG_VERTEXFORMAT_UBYTE4}, //shinecolor
					{.buffer_index = 0, .offset = 60, .format = SG_VERTEXFORMAT_FLOAT}, //rgb_id
				},
			},
			.depth = {
				.pixel_format = SG_PIXELFORMAT_DEPTH,
				.compare = SG_COMPAREFUNC_LESS_EQUAL,
				.write_enabled = true,
			},
			.color_count = 6,
			.colors = {
				{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = false}},
				{.pixel_format = SG_PIXELFORMAT_R32F}, // g_depth
				{.pixel_format = SG_PIXELFORMAT_RGBA32F},

				{.pixel_format = SG_PIXELFORMAT_R8},
				{.pixel_format = SG_PIXELFORMAT_RGBA8},
				{.pixel_format = SG_PIXELFORMAT_R32F}, //rgb_viewed
			},
			.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
			.index_type = SG_INDEXTYPE_NONE,
			.sample_count = OFFSCREEN_SAMPLE_COUNT,
		}),
		.stat_pip = sg_make_pipeline(sg_pipeline_desc{
			.shader = sg_make_shader(sprite_stats_shader_desc(sg_query_backend())),
			.layout = {
				.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
			},
			.depth = {
				.pixel_format = SG_PIXELFORMAT_NONE,
			},
			.color_count = 1,
			.colors = {
				{.pixel_format = SG_PIXELFORMAT_R32F}, // occurences.
			},
			.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
			.index_type = SG_INDEXTYPE_NONE,
		}),
	};
	// initial atlas.
	argb_store.atlas = sg_make_image(sg_image_desc{
		.type = SG_IMAGETYPE_ARRAY,
		.width = 4096,
		.height = 4096,
		.num_slices = 1, //at most 16.
		.usage = SG_USAGE_STREAM,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
	});
	argb_store.atlasNum = 1;
}

void screen_init_sprite_images(int w, int h)
{
	graphics_state.sprite_render.occurences = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = (int)ceil(w/4.0),
		.height = (int)(ceil(h/4.0)),
		.pixel_format = SG_PIXELFORMAT_R32F,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.min_filter = SG_FILTER_NEAREST,
		.mag_filter = SG_FILTER_NEAREST,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
		.label = "argb-occurences"
	});

	graphics_state.sprite_render.viewed_rgb = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_R32F, //RGBA32F for hdr and bloom?
		.sample_count = 1,
		.min_filter = SG_FILTER_NEAREST,
		.mag_filter = SG_FILTER_NEAREST,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
		.label = "viewed-rgb"
	});

	// quad images
	graphics_state.sprite_render.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = graphics_state.primitives.color},
				{.image = graphics_state.primitives.depth},
				{.image = graphics_state.TCIN},
				{.image = graphics_state.bordering},
				{.image = graphics_state.bloom},
				{.image = graphics_state.sprite_render.viewed_rgb }
			},
			.depth_stencil_attachment = {.image = graphics_state.primitives.depthTest},
			.label = "q_images",
		});
	graphics_state.sprite_render.stat_pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = graphics_state.sprite_render.occurences }
			},
			.label = "q_images_stat",
		});
}
void destroy_sprite_images()
{
	sg_destroy_image(graphics_state.sprite_render.viewed_rgb);
	sg_destroy_image(graphics_state.sprite_render.occurences);
	sg_destroy_pass(graphics_state.sprite_render.pass);
	sg_destroy_pass(graphics_state.sprite_render.stat_pass);
}

void init_messy_renderer()
{
	// debug shader use UV.
	graphics_state.utilities.pip_dbg = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(dbg_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "dbgvis quad pipeline"
		});
	graphics_state.utilities.pip_blend = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(blend_to_screen_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ZERO}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "screen blending"
		});
	graphics_state.utilities.bind = sg_bindings{
		.vertex_buffers = {graphics_state.uv_vertices}
	};

	init_ssao_shader();

	graphics_state.kuwahara_blur.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(kuwahara_blur_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.write_enabled = false,
		},
		.colors = {{.pixel_format = SG_PIXELFORMAT_R32F}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "kuwahara-blur quad"
	});
	graphics_state.ssao.blur_bindings = sg_bindings{
		.vertex_buffers = {graphics_state.uv_vertices}		// images will be filled right before rendering
	};

	// Pipeline state object
	point_cloud_simple_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(point_cloud_simple_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = { {.stride = 16}, {.stride = 4}},
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,  },
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_UBYTE4N },
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},
		.color_count = 6,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = false}},
			{.pixel_format = SG_PIXELFORMAT_R32F}, // g_depth
			{.pixel_format = SG_PIXELFORMAT_R32F}, //pc_depth
			{.pixel_format = SG_PIXELFORMAT_RGBA32F},

			{.pixel_format = SG_PIXELFORMAT_R8},
			{.pixel_format = SG_PIXELFORMAT_RGBA8},
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		.index_type = SG_INDEXTYPE_NONE,
		.sample_count = OFFSCREEN_SAMPLE_COUNT
		});
	

	auto depth_blur_shader = sg_make_shader(depth_blur_shader_desc(sg_query_backend()));
	graphics_state.edl_lres_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = depth_blur_shader,
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_DEPTH},
		.color_count = 2,
		.colors = {{.pixel_format = SG_PIXELFORMAT_R32F}, {.pixel_format = SG_PIXELFORMAT_RGBA32F}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "depth-blur-pipeline"
		});

	graphics_state.edl_lres.bind = sg_bindings{
		.vertex_buffers = {graphics_state.uv_vertices},
	};


	graphics_state.composer.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(edl_composer_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "edl-composer-pipeline"
		});
	init_ground_effects();
	
	graphics_state.ui_composer.pip_border = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(border_composer_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.op_rgb = SG_BLENDOP_ADD,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		});

	init_bloom_shaders();
}

void GenPasses(int w, int h)
{
	printf("Regenerate console for resolution %dx%d\n", w, h);
	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩
	// UI Helper
	sg_image_desc sel_desc = {
		.width = w/4,
		.height = h/4,
		.usage = SG_USAGE_STREAM,
		.pixel_format = SG_PIXELFORMAT_R8,
	};
	graphics_state.ui_selection = sg_make_image(&sel_desc);
	sg_image_desc bs_desc = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_R8,
	};
	graphics_state.bordering = sg_make_image(&bs_desc);
	bs_desc.pixel_format = SG_PIXELFORMAT_RGBA8;
	graphics_state.bloom = sg_make_image(&bs_desc);
	graphics_state.shine2 = sg_make_image(&bs_desc);

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩
	// BASIC Primitives

	sg_image_desc pc_image_hi = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_RGBA8, //RGBA32F for hdr and bloom?
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.min_filter = SG_FILTER_NEAREST,
		.mag_filter = SG_FILTER_NEAREST,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
		.label = "primitives-color"
	};
	sg_image hi_color = sg_make_image(&pc_image_hi);

	pc_image_hi.pixel_format = SG_PIXELFORMAT_DEPTH; // for depth test.
	pc_image_hi.label = "depthTest-image";
	sg_image depthTest = sg_make_image(&pc_image_hi);

	pc_image_hi.pixel_format = SG_PIXELFORMAT_R32F; // single depth.
	pc_image_hi.label = "p-depth-image";
	sg_image primitives_depth = sg_make_image(&pc_image_hi);

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Class/Instance/Node
	pc_image_hi.pixel_format = SG_PIXELFORMAT_RGBA32F; // single depth.
	pc_image_hi.label = "class-instance-node";
	graphics_state.TCIN = sg_make_image(&pc_image_hi);

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Point Cloud
	// point cloud primitives and output depth for edl.
	pc_image_hi.pixel_format = SG_PIXELFORMAT_R32F; // single depth.
	pc_image_hi.label = "pc-depth-image";
	sg_image pc_depth = sg_make_image(&pc_image_hi); // solely for point cloud.

	auto hres_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = hi_color},
			{.image = primitives_depth},
			{.image = pc_depth},
			{.image = graphics_state.TCIN },
			{.image = graphics_state.bordering },
			{.image = graphics_state.bloom }
		},
		.depth_stencil_attachment = {.image = depthTest},
		.label = "pointcloud",
	});
	graphics_state.pc_primitive = {
		.depth = pc_depth,
		.pass = hres_pass,
		.pass_action = sg_pass_action{
			.colors = {
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {1.0f} },
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {1.0f} },
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {-1.0f,0.0,0.0,0.0} }, //tcin buffer.
				// ui:
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {0.0f} },
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {0.0f} } },
			.depth = { .load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = 1.0f },
			.stencil = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE }
		},
	};
	 
	sg_image primitives_normal = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_RGBA32F, //RGBA32F for hdr and bloom?
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_REPEAT,
		.label = "p-normal-image"
	}); // for ssao etc.

	// --- edl lo-pass blurring depth, also estimate normal.
	sg_image_desc pc_image = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_R32F,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
	};
	sg_image lo_depth = sg_make_image(&pc_image);
	pc_image.pixel_format = SG_PIXELFORMAT_DEPTH;
	sg_image ob_low_depth = sg_make_image(&pc_image);
	graphics_state.edl_lres.depth = ob_low_depth;
	graphics_state.edl_lres.color = lo_depth;
	graphics_state.edl_lres.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = lo_depth} ,
			{.image = primitives_normal},
		},
		.depth_stencil_attachment = {.image = ob_low_depth},
		.label = "edl-low"
		});
	graphics_state.edl_lres.pass_action = sg_pass_action{
		.colors = {
			{.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
			{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE,  .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
		},
	};
	graphics_state.edl_lres.bind.fs_images[0] = pc_depth;

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Line Bunch

	graphics_state.line_bunch.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = hi_color},
				{.image = primitives_depth},
				{.image = graphics_state.TCIN},
				{.image = graphics_state.bordering},
				{.image = graphics_state.bloom}
			},
			.depth_stencil_attachment = {.image = depthTest},
			.label = "linebunch",
		});

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ MESH objects

	graphics_state.primitives = {
		.color=hi_color, .depthTest = depthTest, .depth = primitives_depth, .normal = primitives_normal,
		.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = hi_color},
				{.image = primitives_depth},
				{.image = primitives_normal},
				{.image = graphics_state.TCIN},
				{.image = graphics_state.bordering },
				{.image = graphics_state.bloom } },
			.depth_stencil_attachment = {.image = depthTest},
			.label = "GLTF"
		}),
		.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE }, // type(class)-obj-node.

						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE }}, 
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, },
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE }
		},
	};
	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Images
	screen_init_sprite_images(w, h);
	// -------- SSAO
	screen_init_ssao_buffers(w, h);
	// -------- SSAO Blur use kuwahara.
	//sg_image ssao_blur = sg_make_image(&pc_image_hi);
	//graphics_state.ssao.blur_image = ssao_blur;
	//graphics_state.ssao.blur_bindings.fs_images[0] = ssao_image;
	//graphics_state.ssao.blur_pass = sg_make_pass(sg_pass_desc{
	//	.color_attachments = { {.image = ssao_blur} },
	//	.depth_stencil_attachment = {.image = depthTest},
	//});

	screen_init_ground_effects(w, h);
	
	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ COMPOSER
	// graphics_state.composer.bind = sg_bindings{
	// 	.vertex_buffers = {graphics_state.quad_vertices},
	// 	.fs_images = {hi_color }
	// };
	// composer should
	graphics_state.composer.bind = sg_bindings{
		.vertex_buffers = {graphics_state.quad_vertices},
		.fs_images = {hi_color, pc_depth, lo_depth, primitives_depth, graphics_state.ssao.image } //ssao_blur }
	};

	graphics_state.ui_composer.shine_pass1to2= sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = graphics_state.shine2} },
		//.depth_stencil_attachment = {.image = depthTest},
		.label = "shine"
		});
	graphics_state.ui_composer.shine_pass2to1 = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = graphics_state.bloom} },
		//.depth_stencil_attachment = {.image = depthTest},
		.label = "shine"
		});
	// dilatex 12, dilatey 21, blurx 12, blury 2D.

	graphics_state.ui_composer.border_bind = sg_bindings{
		.vertex_buffers = {graphics_state.quad_vertices},
		.fs_images = {graphics_state.bordering, graphics_state.ui_selection }
	};
}
void ResetEDLPass()
{
	// use post-processing plugin system.
	// all sg_make_image in genpass should be destroyed.

	sg_destroy_image(graphics_state.primitives.color);
	sg_destroy_image(graphics_state.primitives.depthTest);
	sg_destroy_image(graphics_state.primitives.depth);
	sg_destroy_image(graphics_state.primitives.normal);
	sg_destroy_image(graphics_state.TCIN);

	sg_destroy_image(graphics_state.ui_selection);
	sg_destroy_image(graphics_state.bordering);
	sg_destroy_image(graphics_state.bloom);
	sg_destroy_image(graphics_state.shine2);

	sg_destroy_image(graphics_state.pc_primitive.depth);
	sg_destroy_image(graphics_state.edl_lres.color);
	sg_destroy_image(graphics_state.edl_lres.depth);
	

	sg_destroy_pass(graphics_state.primitives.pass);
	sg_destroy_pass(graphics_state.pc_primitive.pass);
	sg_destroy_pass(graphics_state.edl_lres.pass);
	sg_destroy_pass(graphics_state.ui_composer.shine_pass1to2);
	sg_destroy_pass(graphics_state.ui_composer.shine_pass2to1);
	sg_destroy_pass(graphics_state.line_bunch.pass);

	destroy_ssao_buffers();
	destroy_screen_ground_effects();
	destroy_sprite_images();
}


void init_gltf_render()
{
	// at most 1M nodes(instance)
	// Z(mathematically) buffer: just refresh once, only used in node id.
	std::vector<float> ids(1024*1024);
	for (int i = 0; i < ids.size(); ++i) ids[i] = i;
	graphics_state.instancing.Z = sg_make_buffer(sg_buffer_desc{
		.size = 1024 * 1024 * 4, // at most 4M node per object? wtf so many. 4 int.
		.data = { ids.data(), ids.size() * 4}
	});

	// node transform texture: 32MB
	// this is input trans/flag/rot.(3float trans)(1float:24bit flag)|4float rot.
	graphics_state.instancing.node_meta = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 4096, // 1M nodes for all classes/instances, 2 width per node.
		.height = 512, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
	});

	// per instance data. using s_perobj.
	graphics_state.instancing.instance_meta = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 4096, // 1M nodes for all classes/instances.
		.height = 256, //
		.pixel_format = SG_PIXELFORMAT_RGBA32SI,
		});

	// 192MB node cache. 1M nodes max(64byte node*2+64byte normal)
	graphics_state.instancing.objInstanceNodeMvMats1 = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 2048, // 1M nodes for all classes/instances.=>2048px.
		.height = 2048, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});
	graphics_state.instancing.objInstanceNodeMvMats2 = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 2048, // 1M nodes for all classes/instances.=>2048px.
		.height = 2048, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});
	graphics_state.instancing.objInstanceNodeNormalMats = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 2048, //
		.height = 2048, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});


	graphics_state.instancing.animation_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(compute_node_local_mat_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 3*4, },
				{.stride = 4*4, },
				{.stride = 3*4, },
			}, //position
			.attrs = {
				{.buffer_index = 0, .offset=0, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 1, .offset=0, .format = SG_VERTEXFORMAT_FLOAT4 },
				{.buffer_index = 2, .offset=0, .format = SG_VERTEXFORMAT_FLOAT3 },
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_NONE,
			.write_enabled = false,
		},
		.color_count = 1,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, // local view / viewMatrix.
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		});
	_sg_lookup_pipeline(&_sg.pools, graphics_state.instancing.animation_pip.id)->cmn.use_instanced_draw = true;

	graphics_state.instancing.pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f } } },
		.depth = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE },
		.stencil = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE }
	};
	graphics_state.instancing.animation_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = graphics_state.instancing.objInstanceNodeMvMats1},
		},
		.label = "gltf_node_animation_pass"
	});

	graphics_state.instancing.hierarchy_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_hierarchical_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
			},
			.attrs = {
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_NONE,
			.write_enabled = false,
		},
		.color_count = 1,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, // model view mat.
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
	});
	_sg_lookup_pipeline(&_sg.pools, graphics_state.instancing.hierarchy_pip.id)->cmn.use_instanced_draw = true;

	graphics_state.instancing.hierarchy_pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_LOAD, } },
	};

	graphics_state.instancing.hierarchy_pass1 = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = graphics_state.instancing.objInstanceNodeMvMats2},
		},
		.label = "gltf_node_hierarchy_pass1"
		});
	graphics_state.instancing.hierarchy_pass2 = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = graphics_state.instancing.objInstanceNodeMvMats1},
		},
		.label = "gltf_node_hierarchy_pass2"
		});

	
	graphics_state.instancing.finalize_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_node_final_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
			},
			.attrs = {
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_NONE,
			.write_enabled = false,
		},
		.color_count = 1,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, // model view mat.
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		});
	_sg_lookup_pipeline(&_sg.pools, graphics_state.instancing.finalize_pip.id)->cmn.use_instanced_draw = true;
	graphics_state.instancing.final_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = graphics_state.instancing.objInstanceNodeNormalMats},
		},
		.label = "gltf_node_final_pass"
		});
	
	graphics_state.gltf_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 12}, // position
				{.stride = 12}, // normal
				{.stride = 16}, // color
				{.stride = 8}, // texcoord
				{.stride = 8}, // node_meta

				{.stride = 16}, // joints
				{.stride = 16}, // jointNodes
				{.stride = 16}, // weights
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_FLOAT4 },
				{.buffer_index = 3, .format = SG_VERTEXFORMAT_FLOAT2 }, //texture.
				{.buffer_index = 4, .format = SG_VERTEXFORMAT_FLOAT2 }, //node_meta.

				{.buffer_index = 5, .format = SG_VERTEXFORMAT_FLOAT4 }, //joints.
				{.buffer_index = 6, .format = SG_VERTEXFORMAT_FLOAT4 }, //jointNodes.
				{.buffer_index = 7, .format = SG_VERTEXFORMAT_FLOAT4 }, //weights.
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},

		.color_count = 6,
		.colors = {
			// note: blending only applies to colors[0].
			{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = false}},
			{.pixel_format = SG_PIXELFORMAT_R32F},
			{.pixel_format = SG_PIXELFORMAT_RGBA32F},
			{.pixel_format = SG_PIXELFORMAT_RGBA32F},

			{.pixel_format = SG_PIXELFORMAT_R8},
			{.pixel_format = SG_PIXELFORMAT_RGBA8},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
		.index_type = SG_INDEXTYPE_UINT32,
		.cull_mode = SG_CULLMODE_BACK,
		.face_winding = SG_FACEWINDING_CCW,
	});
	_sg_lookup_pipeline(&_sg.pools, graphics_state.gltf_pip.id)->cmn.use_instanced_draw = true;


	unsigned char dummytexdata[] = {1,2,4,8};
	graphics_state.dummy_tex = sg_make_image(sg_image_desc{
			.width = 1 ,
			.height = 1 ,
			.pixel_format = SG_PIXELFORMAT_RGBA8,
			.data = {.subimage = {{ {
				.ptr = dummytexdata,  // Your mat4 data here
				.size = 4
			}}}},
			.label = "dummy"
			});
}

// not used.
void init_shadow()
{
	int shadow_resolution = 1024;
	sg_image_desc sm_desc = {
		.render_target = true,
		.width = shadow_resolution,
		.height = shadow_resolution,
		.pixel_format = SG_PIXELFORMAT_R32F,
	};
	sg_image shadow_map = sg_make_image(&sm_desc);
	sm_desc.pixel_format = SG_PIXELFORMAT_DEPTH_STENCIL; // for depth test.
	sg_image depthTest = sg_make_image(&sm_desc);
	graphics_state.shadow_map = {
		.shadow_map = shadow_map, .depthTest = depthTest,
		.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = { {.image = shadow_map}, },
			.depth_stencil_attachment = {.image = depthTest},
			.label = "shadow-map"
		}),
		.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f } } },
			.depth = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE },
			.stencil = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE }
		},
	};
}

void init_sokol()
{
	float quadVertice[] = {
		// positions            colors
		-1.0f, -1.0f,
		 1.0f, -1.0f,
		-1.0f,  1.0f,
		 1.0f,  1.0f,
	};
	graphics_state.quad_vertices = sg_make_buffer(sg_buffer_desc{
		.data = SG_RANGE(quadVertice),
		.label = "composer-quad-vertices"
		});
	float uv_vertices[] = { 0.0f, 0.0f,  1.0f, 0.0f,  0.0f, 1.0f,  1.0f, 1.0f };
	graphics_state.uv_vertices = sg_make_buffer(sg_buffer_desc{
		.data = SG_RANGE(uv_vertices),
			.label = "quad vertices"
		});

	init_skybox_renderer();
	init_messy_renderer();
	init_gltf_render();
	init_line_renderer();
	init_sprite_images();

	// Pass action
	graphics_state.default_passAction = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 1.0f } } }
	};
}