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

void init_messy_renderer()
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

	graphics_state.ssao.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(ssao_shader_desc(sg_query_backend())),
		.layout = {
			.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}}
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.write_enabled = false,
		},
		.colors={{.pixel_format = SG_PIXELFORMAT_R32F}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "ssao quad pipeline"
		});
	graphics_state.ssao.bindings= sg_bindings{
		.vertex_buffers = {quad_vbuf}		// images will be filled right before rendering
	};

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
		.vertex_buffers = {quad_vbuf}		// images will be filled right before rendering
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
		.colors = {{.pixel_format = SG_PIXELFORMAT_R32F}},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "depth-blur-pipeline"
		});

	graphics_state.edl_lres.bind = sg_bindings{
		.vertex_buffers = {quad_vbuf},
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
				.dst_factor_alpha = SG_BLENDFACTOR_ZERO}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		.label = "edl-composer-pipeline"
		});

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
				.dst_factor_alpha = SG_BLENDFACTOR_ZERO}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
		});
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

	// graphics_state.ui_composer.pip_dilateYFin = sg_make_pipeline(sg_pipeline_desc{
	// 	.shader = sg_make_shader(bloomDilateY_shader_desc(sg_query_backend())),
	// 	.layout = {
	// 		.attrs = {{.format = SG_VERTEXFORMAT_FLOAT2}},
	// 	},
	// 	.colors = {
	// 		{.blend = {.enabled = true,
	// 			.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
	// 			.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
	// 			.op_rgb = SG_BLENDOP_ADD,
	// 			.src_factor_alpha = SG_BLENDFACTOR_ONE,
	// 			.dst_factor_alpha = SG_BLENDFACTOR_ZERO}},
	// 	},
	// 	.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
	// 	});

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
				.dst_factor_alpha = SG_BLENDFACTOR_ZERO}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP,
	});
}

void GenPasses(int w, int h)
{
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
	graphics_state.shine1 = sg_make_image(&bs_desc);
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
			{.image = graphics_state.shine1 }
		},
		.depth_stencil_attachment = {.image = depthTest},
		.label = "pc-hi-pass",
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
	// --- edl lo-pass blurring depth.
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
		.color_attachments = { {.image = lo_depth} },
		.depth_stencil_attachment = {.image = ob_low_depth},
		});
	graphics_state.edl_lres.pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } } },
	};
	graphics_state.edl_lres.bind.fs_images[0] = pc_depth;


	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ MESH objects
	pc_image_hi.pixel_format = SG_PIXELFORMAT_RGBA32F; // normal.
	pc_image_hi.label = "p-normal-image";
	sg_image primitives_normal = sg_make_image(&pc_image_hi); // for ssao etc.

	graphics_state.primitives = {
		.color=hi_color, .depthTest = depthTest, .depth = primitives_depth, .normal = primitives_normal,
		.pass = sg_make_pass(sg_pass_desc{
			.color_attachments = {
				{.image = hi_color},
				{.image = primitives_depth},
				{.image = primitives_normal},
				{.image = graphics_state.TCIN},
				{.image = graphics_state.bordering },
				{.image = graphics_state.shine1 } },
			.depth_stencil_attachment = {.image = depthTest},
		}),
		.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE,  .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE }, // type(class)-obj-node.

						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE }}, 
			.depth = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE, },
			.stencil = {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE }
		},
	};
	// -------- SSAO
	pc_image_hi.pixel_format = SG_PIXELFORMAT_R32F; // single depth.
	pc_image_hi.label = "p-ssao-image";
	sg_image ssao_image = sg_make_image(&pc_image_hi);
	graphics_state.ssao.image = ssao_image;
	graphics_state.ssao.bindings.fs_images[0] = primitives_depth;
	graphics_state.ssao.bindings.fs_images[1] = primitives_normal;
	graphics_state.ssao.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = ssao_image} },
		.depth_stencil_attachment = {.image = depthTest},
		});
	graphics_state.ssao.pass_action = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } } },
	};
	// -------- SSAO Blur use kuwahara.
	sg_image ssao_blur = sg_make_image(&pc_image_hi);
	graphics_state.ssao.blur_image = ssao_blur;
	graphics_state.ssao.blur_bindings.fs_images[0] = ssao_image;
	graphics_state.ssao.blur_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = ssao_blur} },
		.depth_stencil_attachment = {.image = depthTest},
	});

	
	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ COMPOSER
	// graphics_state.composer.bind = sg_bindings{
	// 	.vertex_buffers = {graphics_state.quad_vertices},
	// 	.fs_images = {hi_color }
	// };
	graphics_state.composer.bind = sg_bindings{
		.vertex_buffers = {graphics_state.quad_vertices},
		.fs_images = {hi_color, pc_depth, lo_depth, primitives_depth, ssao_blur }
	};

	graphics_state.ui_composer.shine_pass1to2= sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = graphics_state.shine2} },
		//.depth_stencil_attachment = {.image = depthTest},
		});
	graphics_state.ui_composer.shine_pass2to1 = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = graphics_state.shine1} },
		//.depth_stencil_attachment = {.image = depthTest},
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
	sg_destroy_image(graphics_state.shine1);
	sg_destroy_image(graphics_state.shine2);

	sg_destroy_image(graphics_state.pc_primitive.depth);
	sg_destroy_image(graphics_state.edl_lres.color);
	sg_destroy_image(graphics_state.edl_lres.depth);
	sg_destroy_image(graphics_state.ssao.image);
	sg_destroy_image(graphics_state.ssao.blur_image);

	sg_destroy_pass(graphics_state.primitives.pass);
	sg_destroy_pass(graphics_state.pc_primitive.pass);
	sg_destroy_pass(graphics_state.edl_lres.pass);
	sg_destroy_pass(graphics_state.ssao.pass);
	sg_destroy_pass(graphics_state.ssao.blur_pass);
	sg_destroy_pass(graphics_state.ui_composer.shine_pass1to2);
	sg_destroy_pass(graphics_state.ui_composer.shine_pass2to1);
}


void init_gltf_render()
{
	// Z(mathematically) buffer: just refresh once, only used in node id.
	std::vector<float> ids(65535);
	for (int i = 0; i < ids.size(); ++i) ids[i] = i;
	graphics_state.instancing.Z = sg_make_buffer(sg_buffer_desc{
		.size = 65535 * 4, // at most 4M node per object? wtf so many. 4 int.
		.data = { ids.data(), ids.size() * 4}
	});

	graphics_state.instancing.obj_translate = sg_make_buffer(sg_buffer_desc{
		.size = 16 * 1024 * 1024 * 3,
		.usage = SG_USAGE_STREAM,
		});
	graphics_state.instancing.obj_quat = sg_make_buffer(sg_buffer_desc{
		.size = 16 * 1024 * 1024 * 4, 
		.usage = SG_USAGE_STREAM,
		});

	graphics_state.instancing.pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_compute_mat_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 12, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, // translation
				{.stride = 16, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, // rotation
				{.stride = 4}, // node_id
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT4 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_FLOAT }, // node, 
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_NONE,
			.write_enabled = false,
		},
		.color_count = 2,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, // model view mat.
			{.pixel_format = SG_PIXELFORMAT_RGBA32F}, // normal mat.
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		});
	graphics_state.instancing.objInstanceNodeMvMats = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 4096, // 2048*2048 nodes for all classes/instances.=>4096px
		.height = 4096, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});
	graphics_state.instancing.objInstanceNodeNormalMats = sg_make_image(sg_image_desc{
		.render_target = true,
		.width = 4096, // 2048*2048 nodes for all classes/instances.
		.height = 4096, //
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		});

	/// ==== ui related ====
	// if shine, what color?

	gltf_displaying.shine_colors.reserve(512 * 1024);
	gltf_displaying.flags.reserve(512 * 1024);

	graphics_state.instancing.objShineIntensities = sg_make_image(sg_image_desc{
		.width = 4096, //. 512*8;
		.height = 1024, //
		.usage = SG_USAGE_DYNAMIC,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		});
	
	// displaying params like corner? shine? move to front? 0:corner? 1:shine? 2:front? (shader 3: hovering?)
	graphics_state.instancing.objFlags = sg_make_image(sg_image_desc{
		.width = 4096, // 2048*2048 nodes for all classes/instances.
		.height = 1024, //
		.usage = SG_USAGE_DYNAMIC,
		.pixel_format = SG_PIXELFORMAT_R32UI,
	});

	// per instance: what animation?progress? -> (8bit animation + 8bit progress) * 8 combo.
	graphics_state.instancing.animation = sg_make_image(sg_image_desc{ 
		.width = 2048, // 2048*2048 nodes for all classes/instances.
		.height = 2048, //
		.usage = SG_USAGE_DYNAMIC,
		.pixel_format = SG_PIXELFORMAT_R32UI,
		});

	graphics_state.instancing.pass = sg_make_pass(sg_pass_desc{
		.color_attachments = {
			{.image = graphics_state.instancing.objInstanceNodeMvMats},
			{.image = graphics_state.instancing.objInstanceNodeNormalMats},},
	});

	graphics_state.instancing.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE,.clear_value = {0.0f,0.0f,0.0f,0.0f} },
						{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, .clear_value = {0.0f,0.0f,0.0f,0.0f}} } };


	graphics_state.gltf_pip = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 12}, // position
				{.stride = 12}, // normal
				{.stride = 16}, // color
				{.stride = 4}, // node_id
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_FLOAT4 },
				{.buffer_index = 3, .format = SG_VERTEXFORMAT_FLOAT }, //node_id.
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


	graphics_state.gltf_ground_pip = sg_make_pipeline(sg_pipeline_desc{
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
		.depth = {
			.compare = SG_COMPAREFUNC_ALWAYS,
			.write_enabled = false,
		},
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
	graphics_state.gltf_ground_binding = sg_bindings{
		.vertex_buffers = {sg_make_buffer(sg_buffer_desc{.data = SG_RANGE(ground_vtx)}),},
		.index_buffer = {sg_make_buffer(sg_buffer_desc{.type = SG_BUFFERTYPE_INDEXBUFFER ,.data = SG_RANGE(ground_indices)})}, // slot 1 for instance per.
	};
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

	init_skybox_renderer();
	init_messy_renderer();
	init_gltf_render();

	// Pass action
	graphics_state.default_passAction = sg_pass_action{
		.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 1.0f } } }
	};
}