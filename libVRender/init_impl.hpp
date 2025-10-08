#include "shaders/shaders.h"
#include "me_impl.h"

void init_skybox_renderer()
{
	shared_graphics.skybox.pip_sky = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(skybox_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.label = "background_shader-pipeline"
		});

	shared_graphics.skybox.bind = sg_bindings{
		.vertex_buffers = {shared_graphics.quad_vertices},
	};

	// Shader program
	shared_graphics.skybox.pip_grid = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bg_grid_shader_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ZERO,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE,}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.label = "foreground_shader-pipeline"
	});


	// This is a fixed shader for equirectangular skybox images
	// It will be used across all viewports but each viewport will have its own image	
	// Create pipeline
	sg_pipeline_desc pip_desc = {};
	pip_desc.shader = sg_make_shader(skybox_equirect_shader_desc(sg_query_backend()));
	pip_desc.layout.attrs[0].format = SG_VERTEXFORMAT_FLOAT2;
	pip_desc.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP;
	pip_desc.sample_count = OFFSCREEN_SAMPLE_COUNT;
	pip_desc.label = "skybox_equirectangular_pipeline";

	shared_graphics.skybox.pip_img = sg_make_pipeline(&pip_desc);
}

void init_geometry_renderer()
{
	// implement in the future.
}

void init_line_renderer()
{
	shared_graphics.line_bunch = {
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
					{.stride = 36, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
				}, //position
				.attrs = {
					{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,},
					{.buffer_index = 0, .offset = 12, .format = SG_VERTEXFORMAT_FLOAT3},
					{.buffer_index = 0, .offset = 24, .format = SG_VERTEXFORMAT_UBYTE4}, //arrow|dash|width|NA
					{.buffer_index = 0, .offset = 28, .format = SG_VERTEXFORMAT_UBYTE4}, //color
					{.buffer_index = 0, .offset = 32, .format = SG_VERTEXFORMAT_FLOAT}, //float-ified int
				},
			},
			.depth = {
				.pixel_format = SG_PIXELFORMAT_DEPTH,
				.compare = SG_COMPAREFUNC_LESS,
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

void init_grating_display()
{
	shared_graphics.grating_display = {
		.pip = sg_make_pipeline(sg_pipeline_desc{
			.shader = sg_make_shader(grating_display_shader_desc(sg_query_backend())),
			.layout = {
				.attrs = {} // No vertex attributes needed as we generate in vertex shader
			},
			.colors = {
				{.blend = {.enabled = true,
					.src_factor_rgb = SG_BLENDFACTOR_ONE,
					.dst_factor_rgb = SG_BLENDFACTOR_ONE,
					.op_rgb = SG_BLENDOP_ADD,
					.src_factor_alpha = SG_BLENDFACTOR_ONE,
					.dst_factor_alpha = SG_BLENDFACTOR_ONE,
				}},
			},
			.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
			.label = "grating-display-pipeline"
		})
	};
}

void init_ssao_shader()
{
	shared_graphics.ssao.pip = sg_make_pipeline(sg_pipeline_desc{
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
	shared_graphics.ssao.pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } } },
		//.depth = {.load_action = SG_LOADACTION_CLEAR}
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
	working_graphics_state->ssao.image = ssao_image;

	working_graphics_state->ssao.bindings= sg_bindings{
		.vertex_buffers = {shared_graphics.uv_vertices},		// images will be filled right before rendering
		//.fs_images = {working_graphics_state->primitives.depth,  working_graphics_state->primitives.normal}
		.fs_images = {working_graphics_state->primitives.depthTest,  working_graphics_state->primitives.normal}
	};

	working_graphics_state->ssao.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = ssao_image} },
		//.depth_stencil_attachment = {.image = working_graphics_state->primitives.depthTest},
		.label = "SSAO"
		});
} 
void destroy_ssao_buffers()
{
	sg_destroy_image(working_graphics_state->ssao.image);
	sg_destroy_pass(working_graphics_state->ssao.pass);
}

void init_bloom_shaders()
{
	shared_graphics.ui_composer.pip_dilateX = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bloomDilateX_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.colors = {{.pixel_format = SG_PIXELFORMAT_RGBA8}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
	});
	shared_graphics.ui_composer.pip_dilateY = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bloomDilateY_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.colors = {{.pixel_format = SG_PIXELFORMAT_RGBA8}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		});
	shared_graphics.ui_composer.pip_blurX = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(bloomblurX_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.colors = {{.pixel_format = SG_PIXELFORMAT_RGBA8}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		});
	shared_graphics.ui_composer.pip_blurYFin = sg_make_pipeline(sg_pipeline_desc{
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
		.sample_count = OFFSCREEN_SAMPLE_COUNT
	});
}

void screen_init_bloom(int w, int h)
{
	sg_image_desc bs_desc = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
	};
	working_graphics_state->bloom1 = sg_make_image(&bs_desc);
	working_graphics_state->bloom2 = sg_make_image(&bs_desc); // to add reflection.
	working_graphics_state->shine2 = sg_make_image(&bs_desc);
}
void destroy_screen_bloom()
{
	sg_destroy_image(working_graphics_state->bloom1);
	sg_destroy_image(working_graphics_state->bloom2);
	sg_destroy_image(working_graphics_state->shine2);
}

void init_ground_effects()
{
	shared_graphics.ground_effect.spotlight_pip = sg_make_pipeline(sg_pipeline_desc{
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
				.src_factor_alpha = SG_BLENDFACTOR_ZERO,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}},
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
	shared_graphics.ground_effect.spotlight_bind = sg_bindings{
		.vertex_buffers = {sg_make_buffer(sg_buffer_desc{.data = SG_RANGE(ground_vtx)}),},
		.index_buffer = {sg_make_buffer(sg_buffer_desc{.type = SG_BUFFERTYPE_INDEXBUFFER ,.data = SG_RANGE(ground_indices)})}, // slot 1 for instance per.
	};

	shared_graphics.ground_effect.cs_ssr_pip = sg_make_pipeline(sg_pipeline_desc{
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

	shared_graphics.ground_effect.pass_action = sg_pass_action{
		.colors = {
			{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE,  .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
		},
	};
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
	working_graphics_state->ground_effect.ground_img = sg_make_image(&bs_desc);
	working_graphics_state->ground_effect.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = working_graphics_state->ground_effect.ground_img} ,
		},
		.label = "ground-effects"
		});
	working_graphics_state->ground_effect.bind = sg_bindings{
		.vertex_buffers = {shared_graphics.quad_vertices},
		.fs_images = {working_graphics_state->primitives.depth, working_graphics_state->primitives.color }
	};
}
void destroy_screen_ground_effects()
{
	sg_destroy_image(working_graphics_state->ground_effect.ground_img);
	sg_destroy_pass(working_graphics_state->ground_effect.pass);
}

int rgbaTextureArrayID;

void init_sprite_images()
{
	shared_graphics.sprite_render = {
		.quad_pass_action = sg_pass_action{
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
					{.stride = 68, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
				}, //position
				.attrs = {
					{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,}, //pos
					{.buffer_index = 0, .offset = 12, .format = SG_VERTEXFORMAT_FLOAT}, //flag
					{.buffer_index = 0, .offset = 16, .format = SG_VERTEXFORMAT_FLOAT4}, //quat
					{.buffer_index = 0, .offset = 32, .format = SG_VERTEXFORMAT_FLOAT2}, //dispWH
					{.buffer_index = 0, .offset = 40, .format = SG_VERTEXFORMAT_FLOAT2}, //uvLT
					{.buffer_index = 0, .offset = 48, .format = SG_VERTEXFORMAT_FLOAT2}, //uvRB
					{.buffer_index = 0, .offset = 56, .format = SG_VERTEXFORMAT_UBYTE4}, //shinecolor
					{.buffer_index = 0, .offset = 60, .format = SG_VERTEXFORMAT_FLOAT2}, //rgb_id
				},
			},
			.depth = {
				.pixel_format = SG_PIXELFORMAT_DEPTH,
				.compare = SG_COMPAREFUNC_LESS,
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
	
	// Initialize SVG sprite pipeline
	shared_graphics.svg_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(p_svg_sprite_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 16, .step_func = SG_VERTEXSTEP_PER_VERTEX}, // interleaved pos+color (xyz + uint32 color)
				{.stride = 52, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, // instance data
			},
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3}, // pos
				{.buffer_index = 0, .offset = 12, .format = SG_VERTEXFORMAT_UBYTE4N}, // color
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3,}, // instance pos
				{.buffer_index = 1, .offset = 12, .format = SG_VERTEXFORMAT_FLOAT}, // flag
				{.buffer_index = 1, .offset = 16, .format = SG_VERTEXFORMAT_FLOAT4}, // quat
				{.buffer_index = 1, .offset = 32, .format = SG_VERTEXFORMAT_FLOAT2}, // dispWH
				{.buffer_index = 1, .offset = 40, .format = SG_VERTEXFORMAT_UBYTE4}, // myshine
				{.buffer_index = 1, .offset = 44, .format = SG_VERTEXFORMAT_FLOAT2}, // info
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS,
			.write_enabled = true,
		},
		.color_count = 5,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}},
			{.pixel_format = SG_PIXELFORMAT_R32F}, // g_depth
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, // screen_id
			{.pixel_format = SG_PIXELFORMAT_R8}, // o_bordering
			{.pixel_format = SG_PIXELFORMAT_RGBA8}, // bloom
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
		.index_type = SG_INDEXTYPE_NONE,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
	});
	
	// initial atlas.
	argb_store.atlas = sg_make_image(sg_image_desc{
		.type = SG_IMAGETYPE_ARRAY,
		.width = atlas_sz,
		.height = atlas_sz, //64MB each slice.
		.num_slices = 1, //at most 16.
		.usage = SG_USAGE_STREAM,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
	});
	argb_store.atlasNum = 1;
	
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, argb_store.atlas.id);
	SOKOL_ASSERT(img->gl.target == GL_TEXTURE_2D_ARRAY);
	SOKOL_ASSERT(0 != img->gl.tex[img->cmn.active_slot]);

    rgbaTextureArrayID = img->gl.tex[img->cmn.active_slot];
}

void screen_init_sprite_images(int w, int h)
{
	working_graphics_state->sprite_render.occurences = sg_make_image(sg_image_desc{
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

	working_graphics_state->sprite_render.viewed_rgb = sg_make_image(sg_image_desc{
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
	working_graphics_state->sprite_render.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = working_graphics_state->primitives.color},
				{.image = working_graphics_state->primitives.depth},
				{.image = working_graphics_state->TCIN},
				{.image = working_graphics_state->bordering},
				{.image = working_graphics_state->bloom1},
				{.image = working_graphics_state->sprite_render.viewed_rgb }
			},
			.depth_stencil_attachment = {.image = working_graphics_state->primitives.depthTest},
			.label = "q_images",
		});
	working_graphics_state->sprite_render.svg_pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = working_graphics_state->primitives.color},
				{.image = working_graphics_state->primitives.depth},
				{.image = working_graphics_state->TCIN},
				{.image = working_graphics_state->bordering},
				{.image = working_graphics_state->bloom1},
			},
			.depth_stencil_attachment = {.image = working_graphics_state->primitives.depthTest},
			.label = "svgs",
		});
	working_graphics_state->sprite_render.stat_pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = working_graphics_state->sprite_render.occurences }
			},
			.label = "q_images_stat",
		});
}
void destroy_sprite_images()
{
	sg_destroy_image(working_graphics_state->sprite_render.viewed_rgb);
	sg_destroy_image(working_graphics_state->sprite_render.occurences);
	sg_destroy_pass(working_graphics_state->sprite_render.pass);
	sg_destroy_pass(working_graphics_state->sprite_render.svg_pass);
	sg_destroy_pass(working_graphics_state->sprite_render.stat_pass);
}

void init_imgui_renderer()
{
	
	// grid line.
	shared_graphics.grid_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(ground_plane_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = { {.stride = 16}},
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT4,  },
			},
		},
		.depth = {
			.compare = SG_COMPAREFUNC_LESS,
			//.write_enabled = true,
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}}
		},
		.primitive_type = SG_PRIMITIVETYPE_LINES,
		.index_type = SG_INDEXTYPE_NONE,
		.sample_count = MSAA
		});

    shared_graphics.utilities.pip_imgui = sg_make_pipeline(sg_pipeline_desc{
        .shader = sg_make_shader(imgui_shader_desc(sg_query_backend())),
        .layout = {
            .attrs = {
                { .format = SG_VERTEXFORMAT_FLOAT2 },  // Position
                { .format = SG_VERTEXFORMAT_FLOAT2 },  // TexCoord0
                { .format = SG_VERTEXFORMAT_UBYTE4N }  // Color0
            }
        },
        .colors = {
            {.blend = {
                .enabled = true,
                .src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
                .dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                .src_factor_alpha = SG_BLENDFACTOR_ONE,
                .dst_factor_alpha = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
            }}
        },
        .primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
        .index_type = sizeof(ImDrawIdx) == 2 ? SG_INDEXTYPE_UINT16 : SG_INDEXTYPE_UINT32,
		.sample_count = MSAA,
        .label = "imgui-pipeline"
    });
}

static void init_world_ui_renderer() {

	shared_graphics.world_ui.pass_action = sg_pass_action{
		.colors = {{.load_action = SG_LOADACTION_LOAD ,.store_action = SG_STOREACTION_STORE },
		           {.load_action = SG_LOADACTION_LOAD ,.store_action = SG_STOREACTION_STORE},
				   {.load_action = SG_LOADACTION_LOAD ,.store_action = SG_STOREACTION_STORE},
				   {.load_action = SG_LOADACTION_LOAD ,.store_action = SG_STOREACTION_STORE},
				   {.load_action = SG_LOADACTION_LOAD ,.store_action = SG_STOREACTION_STORE}},
		.depth = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = 1.0f }
	};

	// Create the shader from the world_ui.glsl shader description
	sg_shader shd = sg_make_shader(txt_quad_shader_desc(sg_query_backend()));
	
	// Set up pipeline for instanced rendering of text quads
	sg_pipeline_desc pip_desc = {
		.shader = shd,
		.layout = {
			.buffers = {
				{.stride = sizeof(gpu_text_quad), .step_func = SG_VERTEXSTEP_PER_INSTANCE }  // Instance attributes
			},
			.attrs = {
				{.offset = 0  ,.format = SG_VERTEXFORMAT_FLOAT3 },                // position (xyz)
				{.offset = 12 ,.format = SG_VERTEXFORMAT_FLOAT }, // rotation (xyzw)
				{.offset = 16 ,.format = SG_VERTEXFORMAT_FLOAT2 }, // size (xy)
				{.offset = 24 ,.format = SG_VERTEXFORMAT_UBYTE4N }, // text_color (rgba)
				{.offset = 28 ,.format = SG_VERTEXFORMAT_UBYTE4N }, // bg_color (rgba)
				{.offset = 32 ,.format = SG_VERTEXFORMAT_FLOAT2 }, // uv_min (xy)
				{.offset = 40 ,.format = SG_VERTEXFORMAT_FLOAT2 }, // uv_max (xy)
				{.offset = 48 ,.format = SG_VERTEXFORMAT_BYTE4 }, // glyph_offset (x0y0x1y1)
				{.offset = 52 ,.format = SG_VERTEXFORMAT_UBYTE4}, // glyph_bb, empty*2
				{.offset = 56 ,.format = SG_VERTEXFORMAT_FLOAT2 },  // flags
				{.offset = 64 ,.format = SG_VERTEXFORMAT_FLOAT2 },  // quad screen-offset
			}
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},
		.color_count = 5,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = false}},
			{.pixel_format = SG_PIXELFORMAT_R32F },
			{ .pixel_format = SG_PIXELFORMAT_RGBA32F },
			{.pixel_format = SG_PIXELFORMAT_R8 },
			{.pixel_format = SG_PIXELFORMAT_RGBA8 },},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.index_type = SG_INDEXTYPE_NONE,
		.cull_mode = SG_CULLMODE_NONE,
		.face_winding = SG_FACEWINDING_CCW,
		.sample_count = MSAA,
		.label = "world_ui_pipeline"
	};
	
	shared_graphics.world_ui.pip_txt = sg_make_pipeline(&pip_desc);
}

void init_messy_renderer()
{
	// rgb draw shader use UV.
	shared_graphics.utilities.pip_rgbdraw = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(dbg_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		// .sample_count = 1,
		.label = "dbgvis quad pipeline"
		});

	shared_graphics.utilities.pip_blend = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(blend_to_screen_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ZERO,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.label = "screen blending"
		});
	shared_graphics.utilities.bind = sg_bindings{
		.vertex_buffers = {shared_graphics.uv_vertices}
	};

	init_ssao_shader();

	// Pipeline state object
	shared_graphics.point_cloud_simple_pip = sg_make_pipeline(sg_pipeline_desc{
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
			.compare = SG_COMPAREFUNC_LESS,
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
	

	shared_graphics.edl_lres.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(depth_blur_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_DEPTH},
		.color_count = 2,
		.colors = {{.pixel_format = SG_PIXELFORMAT_R32F}, {.pixel_format = SG_PIXELFORMAT_RGBA32F}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "depth-blur-pipeline"
		});
	
	shared_graphics.edl_lres.pass_action = sg_pass_action{
		.colors = {
			{.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
			{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE,  .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
		},
	};


	shared_graphics.composer.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(edl_composer_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.colors = {
			{.blend = {.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ZERO,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.label = "edl-composer-pipeline"
		});
	init_ground_effects();
	
	shared_graphics.ui_composer.pip_border = sg_make_pipeline(sg_pipeline_desc{
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
		.sample_count = OFFSCREEN_SAMPLE_COUNT
		});

	init_bloom_shaders();

	//pipelines for geometries.
	init_geometry_renderer();
	
	// Initialize world ui renderer.
	init_world_ui_renderer();

	// walkable pipeline
	shared_graphics.walkable_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(walkable_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.color_count = 1,
		.colors = {{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled=true, .src_factor_rgb=SG_BLENDFACTOR_SRC_ALPHA, .dst_factor_rgb=SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA}}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "walkable-pipeline"
	});

	// region3d pipeline
	shared_graphics.region3d_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(region3d_shader_desc(sg_query_backend())),
		.layout = { .attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}} },
		.depth = {.pixel_format = SG_PIXELFORMAT_NONE},
		.color_count = 1,
		.colors = { {.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = true,
			.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
			.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
			.src_factor_alpha = SG_BLENDFACTOR_ONE,
			.dst_factor_alpha = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA}} },
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "region3d-pipeline"
	});

	// region caches
	shared_graphics.region_cache = sg_make_image(sg_image_desc{
		.width = 2048,
		.height = 2048, // 4 tiers * 8 rows
		.usage = SG_USAGE_STREAM,
		.pixel_format = SG_PIXELFORMAT_RGBA16SI,
		.label = "region-cache"
	});

	// per-viewport region3d pass (draws onto hi_color with transparent clear)
	shared_graphics.walkable_passAction = sg_pass_action{ .colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = {0,0,0,0} } } };
}

void GenPasses(int w, int h)
{
	// printf("Regenerate console for resolution %dx%d\n", w, h);
	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩
	// UI Helper
	sg_image_desc sel_desc = {
		.width = w/4,
		.height = h/4,
		.usage = SG_USAGE_STREAM,
		.pixel_format = SG_PIXELFORMAT_R8,
	};
	working_graphics_state->ui_selection = sg_make_image(&sel_desc);
	sg_image_desc bs_desc = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_R8,
	};
	working_graphics_state->bordering = sg_make_image(&bs_desc);
	bs_desc.pixel_format = SG_PIXELFORMAT_RGBA8;

	screen_init_bloom(w, h);

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
	sg_image wboit_composed = sg_make_image(&pc_image_hi);

	pc_image_hi.pixel_format = SG_PIXELFORMAT_RGBA32F;
	sg_image wboit_accum = sg_make_image(&pc_image_hi);
	sg_image wboit_emissive = sg_make_image(&pc_image_hi);

	pc_image_hi.pixel_format = SG_PIXELFORMAT_R32F;
	sg_image w_accum= sg_make_image(&pc_image_hi);

	pc_image_hi.pixel_format = SG_PIXELFORMAT_R8;
	sg_image wboit_reveal = sg_make_image(&pc_image_hi);

	pc_image_hi.pixel_format = SG_PIXELFORMAT_DEPTH; // for depth test.
	pc_image_hi.label = "depthTest-image";
	sg_image depthTest = sg_make_image(&pc_image_hi);
	sg_image depthTest2 = sg_make_image(&pc_image_hi);


	pc_image_hi.pixel_format = SG_PIXELFORMAT_R32F; // single depth.
	pc_image_hi.label = "p-depth-image";
	sg_image primitives_depth = sg_make_image(&pc_image_hi);

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Class/Instance/Node
	pc_image_hi.pixel_format = SG_PIXELFORMAT_RGBA32F; // single depth.
	pc_image_hi.label = "class-instance-node";
	// pc_image_hi.sample_count = 1;
	working_graphics_state->TCIN = sg_make_image(&pc_image_hi);

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
			{.image = working_graphics_state->TCIN },
			{.image = working_graphics_state->bordering },
			{.image = working_graphics_state->bloom1 }
		},
		.depth_stencil_attachment = {.image = depthTest},
		.label = "pointcloud",
	});
	working_graphics_state->pc_primitive = {
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
	// walkable low-res RT
	working_graphics_state->walkable_overlay.low = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = std::max(1, w/4),
		.height = std::max(1, h/4),
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
		.label = "walkable-low"
	});
	working_graphics_state->walkable_overlay.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = working_graphics_state->walkable_overlay.low} },
		.label = "walkable-low-pass"
	});
	// transparent clear for overlay pass
	shared_graphics.walkable_passAction = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = {0.0f,0.0f,0.0f,0.0f} } }
	};

	sg_image lo_depth = sg_make_image(&pc_image);
	pc_image.pixel_format = SG_PIXELFORMAT_DEPTH;
	sg_image ob_low_depth = sg_make_image(&pc_image);
	working_graphics_state->edl_lres.depth = ob_low_depth;
	working_graphics_state->edl_lres.color = lo_depth;
	working_graphics_state->edl_lres.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = lo_depth} ,
			{.image = primitives_normal},
		},
		.depth_stencil_attachment = {.image = ob_low_depth},
		.label = "edl-low"
		});
	
	working_graphics_state->edl_lres.bind = sg_bindings{
		.vertex_buffers = {shared_graphics.uv_vertices},
		.fs_images =  {{pc_depth}}
	};

	// region3d pass targets color (hi_color)
	working_graphics_state->region3d.pass_action = sg_pass_action{ .colors = { {.load_action = SG_LOADACTION_LOAD} } };
	working_graphics_state->region3d.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = hi_color} },
		.label = "region3d"
	});

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Line Bunch

	working_graphics_state->line_bunch.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = hi_color},
				{.image = primitives_depth},
				{.image = working_graphics_state->TCIN},
				{.image = working_graphics_state->bordering},
				{.image = working_graphics_state->bloom1}
			},
			.depth_stencil_attachment = {.image = depthTest},
			.label = "linebunch",
		});

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ MESH objects

	working_graphics_state->primitives = {
		.color=hi_color, .depthTest = depthTest, .depth = primitives_depth, .normal = primitives_normal,
		.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = hi_color},
				{.image = primitives_depth},
				{.image = primitives_normal},
				{.image = working_graphics_state->TCIN},
				{.image = working_graphics_state->bordering },
				{.image = working_graphics_state->bloom1 } },
			.depth_stencil_attachment = {.image = depthTest},
			.label = "GLTF"
		}),
		.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, }, // fix normal problem.
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE }, // type(class)-obj-node.

						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE }}, 
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, },
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE }
		},
	};

	working_graphics_state->wboit = {
		.accum = wboit_accum, .revealage = wboit_reveal, .wboit_composed= wboit_composed, 
		.w_accum = w_accum, .wboit_emissive = wboit_emissive,
		// accum pass: just generate image.
		.accum_pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = wboit_accum},
				{.image = w_accum},
				{.image = wboit_emissive }
			},
			.depth_stencil_attachment = {.image = depthTest},
			.label = "wboit-accum-pass"
		}),
		// treat transparent objects as opaque and get other info.
		.reveal_pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = primitives_depth},
				{.image = working_graphics_state->TCIN},
				{.image = working_graphics_state->bordering },
			},
			.depth_stencil_attachment = {.image = depthTest},
			.label = "wboit-reveal-pass"
		}),
		.compose_pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = wboit_composed},
				{.image = working_graphics_state->bloom2},
			},
			.depth_stencil_attachment = {.image = depthTest},
			.label = "wboit-compose-pass"
		}),
		.compose_bind = sg_bindings{
			.vertex_buffers = {shared_graphics.quad_vertices},
			//.fs_images = {wboit_accum, w_accum, primitives_depth, hi_color},
			.fs_images = {wboit_accum, wboit_emissive, w_accum, working_graphics_state->bordering }
		},
	};

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Images
	screen_init_sprite_images(w, h);
	// -------- SSAO
	screen_init_ssao_buffers(w, h);
	// -------- SSAO Blur use kuwahara.
	//sg_image ssao_blur = sg_make_image(&pc_image_hi);
	//working_graphics_state->ssao.blur_image = ssao_blur;
	//working_graphics_state->ssao.blur_bindings.fs_images[0] = ssao_image;
	//working_graphics_state->ssao.blur_pass = sg_make_pass(sg_pass_desc{
	//	.color_attachments = { {.image = ssao_blur} },
	//	.depth_stencil_attachment = {.image = depthTest},
	//});

	screen_init_ground_effects(w, h);
	
	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ COMPOSER
	working_graphics_state->composer.bind = sg_bindings{
		.vertex_buffers = {shared_graphics.quad_vertices},
		.fs_images = {hi_color,
			pc_depth, lo_depth,
			primitives_depth,
			working_graphics_state->ssao.image,
			wboit_composed}
	};

	working_graphics_state->ui_composer.shine_pass1to2= sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = working_graphics_state->shine2} },
		//.depth_stencil_attachment = {.image = depthTest},
		.label = "shine"
		});
	working_graphics_state->ui_composer.shine_pass2to1 = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = working_graphics_state->bloom1} },
		//.depth_stencil_attachment = {.image = depthTest},
		.label = "shine"
		});
	// dilatex 12, dilatey 21, blurx 12, blury 2D.

	working_graphics_state->ui_composer.border_bind = sg_bindings{
		.vertex_buffers = {shared_graphics.quad_vertices},
		.fs_images = {working_graphics_state->bordering, working_graphics_state->ui_selection }
	};

	sg_image_desc temp_render_desc = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		.sample_count = MSAA,// OFFSCREEN_SAMPLE_COUNT,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
	};
	working_graphics_state->temp_render = sg_make_image(&temp_render_desc);
	// temp_render_desc.sample_count = 1;

	// working_graphics_state->final_image = sg_make_image(&temp_render_desc);

	working_graphics_state->world_ui.depthTest = depthTest2;
	working_graphics_state->world_ui.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = working_graphics_state->primitives.color},
				{.image = working_graphics_state->primitives.depth},
				{.image = working_graphics_state->TCIN},
				{.image = working_graphics_state->bordering},
				{.image = working_graphics_state->bloom1},
			},
			.depth_stencil_attachment = {.image = depthTest2},
			.label = "world-ui",
		});


	// Create a separate depth texture with matching format
	sg_image_desc temp_depth_desc = {
	    .render_target = true,
	    .width = w,
	    .height = h,
	    .pixel_format = SG_PIXELFORMAT_DEPTH_STENCIL,  // Match the pipeline's depth format
	    .sample_count = MSAA, //OFFSCREEN_SAMPLE_COUNT,
	    .min_filter = SG_FILTER_LINEAR,
	    .mag_filter = SG_FILTER_LINEAR,
	};
	working_graphics_state->temp_render_depth = sg_make_image(&temp_depth_desc);

	// todo: how about use taa.
	// resolve takes up too much memory and time. just ignore. 
	// working_graphics_state->msaa_render_pass = sg_make_pass(sg_pass_desc{
	//     .color_attachments = {{.image = working_graphics_state->temp_render}},
	// 	.resolve_attachments = {{.image = working_graphics_state->final_image}},
	//     .depth_stencil_attachment = {.image = working_graphics_state->temp_render_depth},  // Use our new depth texture
	//     .label = "temp-render-pass"
	// });

	working_graphics_state->temp_render_pass = sg_make_pass(sg_pass_desc{
	    .color_attachments = {{.image = working_graphics_state->temp_render}},
	    .depth_stencil_attachment = {.image = working_graphics_state->temp_render_depth},  // Use our new depth texture
	    .label = "temp-render-pass"
	});


	working_graphics_state->inited = true;
}


void ResetEDLPass()
{
	// use post-processing plugin system.
	// all sg_make_image in genpass should be destroyed.

	sg_destroy_image(working_graphics_state->primitives.color);
	sg_destroy_image(working_graphics_state->primitives.depthTest);
	sg_destroy_image(working_graphics_state->primitives.depth);
	sg_destroy_image(working_graphics_state->primitives.normal);
	sg_destroy_image(working_graphics_state->TCIN);

	sg_destroy_image(working_graphics_state->wboit.accum);
	sg_destroy_image(working_graphics_state->wboit.revealage);
	sg_destroy_image(working_graphics_state->wboit.w_accum);
	sg_destroy_image(working_graphics_state->wboit.wboit_composed);
	sg_destroy_image(working_graphics_state->wboit.wboit_emissive);

	sg_destroy_image(working_graphics_state->ui_selection);
	sg_destroy_image(working_graphics_state->bordering);

	sg_destroy_image(working_graphics_state->pc_primitive.depth);
	sg_destroy_image(working_graphics_state->edl_lres.color);
	sg_destroy_image(working_graphics_state->edl_lres.depth);
	

	sg_destroy_pass(working_graphics_state->primitives.pass);
	sg_destroy_pass(working_graphics_state->wboit.accum_pass);
	sg_destroy_pass(working_graphics_state->wboit.reveal_pass);
	sg_destroy_pass(working_graphics_state->wboit.compose_pass);
	sg_destroy_pass(working_graphics_state->pc_primitive.pass);
	sg_destroy_pass(working_graphics_state->edl_lres.pass);
	sg_destroy_pass(working_graphics_state->ui_composer.shine_pass1to2);
	sg_destroy_pass(working_graphics_state->ui_composer.shine_pass2to1);
	sg_destroy_pass(working_graphics_state->line_bunch.pass);
	sg_destroy_pass(working_graphics_state->world_ui.pass);

    sg_destroy_image(working_graphics_state->temp_render);
	sg_destroy_image(working_graphics_state->world_ui.depthTest);
    // sg_destroy_image(working_graphics_state->final_image);
    sg_destroy_image(working_graphics_state->temp_render_depth);
    sg_destroy_pass(working_graphics_state->temp_render_pass);
	
	destroy_ssao_buffers();
	destroy_screen_ground_effects();
	destroy_sprite_images();
	destroy_screen_bloom();
}


void init_gltf_render()
{
	// at most 1M nodes(instance)
	// Z(mathematically) buffer: just refresh once, only used in node id.
	std::vector<float> ids(1024*1024);
	for (int i = 0; i < ids.size(); ++i) ids[i] = i;
	shared_graphics.instancing.Z = sg_make_buffer(sg_buffer_desc{
		.size = 1024 * 1024 * 4, // at most 4M node per object? wtf so many. 4 int.
		.data = { ids.data(), ids.size() * 4}
	});

	// node transform texture: 32MB
	// this is input trans/flag/rot.(3float trans)(1float:24bit flag)|4float rot.
	shared_graphics.instancing.node_meta = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 4096, // 1M nodes for all classes/instances, 2 width per node.
		.height = 512, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
	});

	// per instance data. using s_perobj.
	shared_graphics.instancing.instance_meta = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 4096, // 1M nodes for all classes/instances.
		.height = 256, //
		.pixel_format = SG_PIXELFORMAT_RGBA32SI,
		});

	// 192MB node cache. 1M nodes max(64byte node*2+64byte normal)
	shared_graphics.instancing.objInstanceNodeMvMats1 = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 2048, // 1M nodes for all classes/instances.=>2048px.
		.height = 2048, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});
	shared_graphics.instancing.objInstanceNodeMvMats2 = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 2048, // 1M nodes for all classes/instances.=>2048px.
		.height = 2048, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});
	shared_graphics.instancing.objInstanceNodeNormalMats = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 2048, //
		.height = 2048, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});


	shared_graphics.instancing.animation_pip = sg_make_pipeline(sg_pipeline_desc{
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
	_sg_lookup_pipeline(&_sg.pools, shared_graphics.instancing.animation_pip.id)->cmn.use_instanced_draw = true;

	shared_graphics.instancing.pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f } } },
		.depth = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE },
		.stencil = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE }
	};
	shared_graphics.instancing.animation_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = shared_graphics.instancing.objInstanceNodeMvMats1},
		},
		.label = "gltf_node_animation_pass"
	});

	shared_graphics.instancing.hierarchy_pip = sg_make_pipeline(sg_pipeline_desc{
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
	_sg_lookup_pipeline(&_sg.pools, shared_graphics.instancing.hierarchy_pip.id)->cmn.use_instanced_draw = true;

	shared_graphics.instancing.hierarchy_pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_LOAD, } },
	};

	shared_graphics.instancing.hierarchy_pass1 = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = shared_graphics.instancing.objInstanceNodeMvMats2},
		},
		.label = "gltf_node_hierarchy_pass1"
		});
	shared_graphics.instancing.hierarchy_pass2 = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = shared_graphics.instancing.objInstanceNodeMvMats1},
		},
		.label = "gltf_node_hierarchy_pass2"
		});

	
	shared_graphics.instancing.finalize_pip = sg_make_pipeline(sg_pipeline_desc{
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
	_sg_lookup_pipeline(&_sg.pools, shared_graphics.instancing.finalize_pip.id)->cmn.use_instanced_draw = true;
	shared_graphics.instancing.final_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = shared_graphics.instancing.objInstanceNodeNormalMats},
		},
		.label = "gltf_node_final_pass"
		});
	
	shared_graphics.gltf_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 12}, // position
				{.stride = 12}, // normal
				{.stride = 4}, // color - reduced from 16 to 4 bytes
				{.stride = 56}, // texcoord - expanded from 24 to 40 bytes (vec4 texcoord + vec4 atlasinfo + vec4 em_atlas + vec2 tex_weight)
				{.stride = 8}, // node_meta
				{.stride = 16}, // joints
				{.stride = 16}, // jointNodes
				{.stride = 16}, // weights
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_UBYTE4N }, // Changed from FLOAT4 to UBYTE4N
				{.buffer_index = 3, .format = SG_VERTEXFORMAT_FLOAT4 }, // texcoord - uv.xy for base color, uv.zw for emissive
				{.buffer_index = 3, .offset = 16, .format = SG_VERTEXFORMAT_FLOAT4,  }, // base color atlas info
				{.buffer_index = 3, .offset = 32, .format = SG_VERTEXFORMAT_FLOAT4,  }, // emissive atlas info
				{.buffer_index = 3, .offset = 48, .format = SG_VERTEXFORMAT_FLOAT2,  }, // texture weights

				{.buffer_index = 4, .format = SG_VERTEXFORMAT_FLOAT }, //fnode_id.
				{.buffer_index = 4, .offset = 4, .format = SG_VERTEXFORMAT_UBYTE4 }, //fskin_id, env_intensity, /,/

				{.buffer_index = 5, .format = SG_VERTEXFORMAT_FLOAT4 }, //joints.
				{.buffer_index = 6, .format = SG_VERTEXFORMAT_FLOAT4 }, //jointNodes.
				{.buffer_index = 7, .format = SG_VERTEXFORMAT_FLOAT4 }, //weights.
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS,
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
	_sg_lookup_pipeline(&_sg.pools, shared_graphics.gltf_pip.id)->cmn.use_instanced_draw = true;


	shared_graphics.wboit.accum_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(wboit_accum_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 12}, // position
				{.stride = 12}, // normal
				{.stride = 4}, // color - reduced from 16 to 4 bytes
				{.stride = 56}, // texcoord - expanded from 24 to 40 bytes (vec4 texcoord + vec4 atlasinfo + vec4 em_atlas + vec2 tex_weight)
				{.stride = 8}, // node_meta
				{.stride = 16}, // joints
				{.stride = 16}, // jointNodes
				{.stride = 16}, // weights
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_UBYTE4N }, // Changed from FLOAT4 to UBYTE4N
				{.buffer_index = 3, .format = SG_VERTEXFORMAT_FLOAT4 }, // texcoord - uv.xy for base color, uv.zw for emissive
				{.buffer_index = 3, .offset = 16, .format = SG_VERTEXFORMAT_FLOAT4,  }, // base color atlas info
				{.buffer_index = 3, .offset = 32, .format = SG_VERTEXFORMAT_FLOAT4,  }, // emissive atlas info
				{.buffer_index = 3, .offset = 48, .format = SG_VERTEXFORMAT_FLOAT2,  }, // texture weights

				{.buffer_index = 4, .format = SG_VERTEXFORMAT_FLOAT }, //fnode_id.
				{.buffer_index = 4, .offset = 4, .format = SG_VERTEXFORMAT_UBYTE4 }, //fskin_id, env_intensity, /,/

				{.buffer_index = 5, .format = SG_VERTEXFORMAT_FLOAT4 }, //joints.
				{.buffer_index = 6, .format = SG_VERTEXFORMAT_FLOAT4 }, //jointNodes.
				{.buffer_index = 7, .format = SG_VERTEXFORMAT_FLOAT4 }, //weights.
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = false,
		},

		.color_count = 3,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA32F, .blend = {
				.enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_ONE,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE,
				.src_factor_alpha = SG_BLENDFACTOR_ONE,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE
			}},
			{.pixel_format = SG_PIXELFORMAT_R32F},
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, // emissive
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
		.index_type = SG_INDEXTYPE_UINT32,
		.cull_mode = SG_CULLMODE_BACK,
		.face_winding = SG_FACEWINDING_CCW,
		});

	shared_graphics.wboit.accum_pass_action = sg_pass_action{
			.colors = {
				{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, },
				{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, },
				{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE }, // emissive
			},
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, },
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE }
	};

	shared_graphics.wboit.reveal_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(wboit_reveal_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 12}, // position
				//{.stride = 12}, // normal
				//{.stride = 4}, // color - reduced from 16 to 4 bytes
				//{.stride = 56}, // texcoord - expanded from 24 to 40 bytes (vec4 texcoord + vec4 atlasinfo + vec4 em_atlas + vec2 tex_weight)
				{.stride = 8}, // node_meta
				{.stride = 16}, // joints
				{.stride = 16}, // jointNodes
				{.stride = 16}, // weights
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3 },
				//{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				//{.buffer_index = 2, .format = SG_VERTEXFORMAT_UBYTE4N }, // Changed from FLOAT4 to UBYTE4N
				//{.buffer_index = 3, .format = SG_VERTEXFORMAT_FLOAT4 }, // texcoord - uv.xy for base color, uv.zw for emissive
				//{.buffer_index = 3, .offset = 16, .format = SG_VERTEXFORMAT_FLOAT4,  }, // base color atlas info
				//{.buffer_index = 3, .offset = 32, .format = SG_VERTEXFORMAT_FLOAT4,  }, // emissive atlas info
				//{.buffer_index = 3, .offset = 48, .format = SG_VERTEXFORMAT_FLOAT2,  }, // texture weights

				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT }, //fnode_id.
				{.buffer_index = 1, .offset = 4, .format = SG_VERTEXFORMAT_UBYTE4 }, //fskin_id, env_intensity, /,/

				{.buffer_index = 2, .format = SG_VERTEXFORMAT_FLOAT4 }, //joints.
				{.buffer_index = 3, .format = SG_VERTEXFORMAT_FLOAT4 }, //jointNodes.
				{.buffer_index = 4, .format = SG_VERTEXFORMAT_FLOAT4 }, //weights.
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS,
			.write_enabled = true,
		},

		.color_count = 3,
		.colors = {
			// note: blending only applies to colors[0].
			{.pixel_format = SG_PIXELFORMAT_R32F}, //g_depth
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, //TCID

			{.pixel_format = SG_PIXELFORMAT_R8}, //bordering.
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
		.index_type = SG_INDEXTYPE_UINT32,
		.cull_mode = SG_CULLMODE_BACK,
		.face_winding = SG_FACEWINDING_CCW,
		});

	shared_graphics.wboit.reveal_pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, }, //depth.
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, }, //s-id
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE },  //bordering
					},
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, },
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE }
	};

	shared_graphics.wboit.compose_pass_action = sg_pass_action{
			.colors = {
				{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, } ,
				{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, } },
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, },
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE }
	};


	shared_graphics.wboit.compose_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(wboit_compose_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
		},
		.color_count = 2,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA8,},
			{.pixel_format = SG_PIXELFORMAT_RGBA8,},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.sample_count = OFFSCREEN_SAMPLE_COUNT,
		.label = "wboit-compose-pipeline"
		});

	unsigned char dummytexdata[] = {1,2,4,8};
	shared_graphics.dummy_tex = sg_make_image(sg_image_desc{
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

void init_graphics()
{
	special_objects.add("me::mouse", mouse_object = new me_special());
	// per viewport
	special_objects.add("me::camera", (me_special*)(ui.viewports[0].camera_obj = new me_special())); // could be replaced.
	// add alias for main viewport
	special_objects.add("me::camera(main)", (me_special*)ui.viewports[0].camera_obj);

    // initialize aliases for auxiliary viewports with numbered defaults
    for (int i = 1; i < MAX_VIEWPORTS; ++i)
    {
        if (ui.viewports[i].camera_obj == nullptr)
            ui.viewports[i].camera_obj = new me_special();
        char alias[64];
        sprintf(alias, "me::camera(vp%d)", i);
        special_objects.add(alias, (me_special*)ui.viewports[i].camera_obj);
		ui.viewports[i].cameraAliasKey = ui.viewports[i].panelName = alias;
    }

	float quadVertice[] = {
		// positions            colors
		-1.0f, -1.0f,
		 1.0f, -1.0f,
		-1.0f,  1.0f,
		 1.0f,  1.0f,
	};
	shared_graphics.quad_vertices = sg_make_buffer(sg_buffer_desc{
		.data = SG_RANGE(quadVertice),
		.label = "composer-quad-vertices"
		});
	float uv_vertices[] = { 0.0f, 0.0f,  1.0f, 0.0f,  0.0f, 1.0f,  1.0f, 1.0f };
	shared_graphics.uv_vertices = sg_make_buffer(sg_buffer_desc{
		.data = SG_RANGE(uv_vertices),
			.label = "quad vertices"
		});

	init_skybox_renderer();
	init_messy_renderer();
	init_imgui_renderer();
	init_grating_display();
	init_gltf_render();
	init_line_renderer();
	init_sprite_images();
	

	// Pass action
	shared_graphics.default_passAction = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 1.0f } } }
	};

	// Create walkable cache texture (RGBA32F)
	shared_graphics.walkable_cache = sg_make_image(sg_image_desc{
		.width = 2048,
		.height = 268,
		.usage = SG_USAGE_STREAM,
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		.min_filter = SG_FILTER_NEAREST,
		.mag_filter = SG_FILTER_NEAREST,
		.wrap_u = SG_WRAP_CLAMP_TO_EDGE,
		.wrap_v = SG_WRAP_CLAMP_TO_EDGE,
		.label = "walkable-cache"
	});
}

