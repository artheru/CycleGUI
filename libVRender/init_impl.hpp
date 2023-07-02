
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
		.color_count = 3,
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = false}},
			{.pixel_format = SG_PIXELFORMAT_R32F}, // g_depth
			{.pixel_format = SG_PIXELFORMAT_R32F}, //pc_depth
		},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		.index_type = SG_INDEXTYPE_NONE,
		.sample_count = OFFSCREEN_SAMPLE_COUNT
		});

	graphics_state.pc_pip_depth = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(pc_depth_only_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = { {.stride = 16}, {.stride = 16}},
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT3,  },
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_FLOAT },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT4 },
			},
		},
		.depth = {
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},
		.colors = {{.pixel_format= SG_PIXELFORMAT_R32F, .blend = {.enabled = false}}},
		.primitive_type = SG_PRIMITIVETYPE_POINTS,
		.index_type = SG_INDEXTYPE_NONE,
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
}

void GenPasses(int w, int h)
{
	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩
	// BASIC Primitives
	sg_image_desc pc_image_hi = {
		.render_target = true,
		.width = w,
		.height = h,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
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

	// ▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩▩ Point Cloud
	// point cloud primitives and output depth for edl.
	pc_image_hi.pixel_format = SG_PIXELFORMAT_R32F; // single depth.
	pc_image_hi.label = "pc-depth-image";
	sg_image pc_depth = sg_make_image(&pc_image_hi); // solely for point cloud.

	auto hres_pass = sg_make_pass(sg_pass_desc{
		.color_attachments = { {.image = hi_color}, {.image = primitives_depth}, {.image = pc_depth}},
		.depth_stencil_attachment = {.image = depthTest},
		.label = "pc-hi-pass",
	});
	graphics_state.pc_primitive = {
		.depth = pc_depth,
		.pass = hres_pass,
		.pass_action = sg_pass_action{
			//.colors = { {.load_action = SG_LOADACTION_CLEAR, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } } },
			.colors = {
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } },
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {1.0f} },
				{.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = {1.0f} } },
			.depth = { .load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE, .clear_value = 1.0f },
			.stencil = {.load_action = SG_LOADACTION_CLEAR, .store_action = SG_STOREACTION_STORE }
		},
	};
	// --- edl lo-pass blurring depth.
	sg_image_desc pc_image = {
		.render_target = true,
		.width = w / 2,
		.height = h / 2,
		.pixel_format = SG_PIXELFORMAT_R32F,
		.min_filter = SG_FILTER_NEAREST,
		.mag_filter = SG_FILTER_NEAREST,
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
			.color_attachments = { {.image = hi_color}, {.image = primitives_depth}, {.image = primitives_normal}},
			.depth_stencil_attachment = {.image = depthTest},
		}),
		.pass_action = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_LOAD,.store_action = SG_STOREACTION_STORE, },
						{.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE,  .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f } }},
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
		.fs_images = {hi_color, pc_depth, lo_depth, primitives_depth, ssao_blur,  }
	};
}
void ResetEDLPass()
{
	// use post-processing plugin system.
	sg_destroy_image(graphics_state.primitives.color);
	sg_destroy_image(graphics_state.primitives.depthTest);
	sg_destroy_image(graphics_state.primitives.depth);
	sg_destroy_image(graphics_state.primitives.normal);

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
}


void init_gltf_render()
{
	// 2048*2048 nodes max, 4M objects max.
	graphics_state.instancing.instanceID = sg_make_buffer(sg_buffer_desc{
		.size = 16 * 1024 * 1024, // at most 4M objects. 4 int.
		.usage = SG_USAGE_STREAM,
		});
	// instance_id buffer: just refresh once.
	std::vector<int> ids(4 * 1024 * 1024);
	for (int i = 0; i < ids.size(); ++i) ids[i] = i;
	sg_update_buffer(graphics_state.instancing.instanceID, sg_range{
		.ptr = ids.data(),
		.size = ids.size() * sizeof(int)
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
				{.stride = 4, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
				{.stride = 12, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, // translation
				{.stride = 16, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, // rotation
				{.stride = 4}, // node_id
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_INT, }, // instance, 
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_FLOAT4 },
				{.buffer_index = 3, .format = SG_VERTEXFORMAT_INT }, // node, 
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
				{.stride = 4, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
				{.stride = 12}, // position
				{.stride = 12}, // normal
				{.stride = 16}, // color
				{.stride = 4}, // node_id
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_INT, },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 3, .format = SG_VERTEXFORMAT_FLOAT4 },
				{.buffer_index = 4, .format = SG_VERTEXFORMAT_INT },
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},

		.color_count = 3,
		.colors = {
			// note: blending only applies to colors[0].
			{.pixel_format = SG_PIXELFORMAT_RGBA8, .blend = {.enabled = false}},
			{.pixel_format = SG_PIXELFORMAT_R32F},
			{.pixel_format = SG_PIXELFORMAT_RGBA32F},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
		.index_type = SG_INDEXTYPE_UINT32,
		.cull_mode = SG_CULLMODE_BACK,
		.face_winding = SG_FACEWINDING_CCW,
	});

	graphics_state.gltf_pip_depth = sg_make_pipeline(sg_pipeline_desc{
		.shader = sg_make_shader(gltf_depth_only_shader_desc(sg_query_backend())),
		.layout = {
			.buffers = {
				{.stride = 4, .step_func = SG_VERTEXSTEP_PER_INSTANCE,}, //instance
				{.stride = 12}, // position
				{.stride = 4}, // node_id
			}, //position
			.attrs = {
				{.buffer_index = 0, .format = SG_VERTEXFORMAT_INT, },
				{.buffer_index = 1, .format = SG_VERTEXFORMAT_FLOAT3 },
				{.buffer_index = 2, .format = SG_VERTEXFORMAT_INT },
			},
		},
		.depth = {
			.pixel_format = SG_PIXELFORMAT_DEPTH,
			.compare = SG_COMPAREFUNC_LESS_EQUAL,
			.write_enabled = true,
		},
		.colors = {
			{.pixel_format = SG_PIXELFORMAT_R32F, .blend = {.enabled = false}},
		},
		.primitive_type = SG_PRIMITIVETYPE_TRIANGLES,
		.index_type = SG_INDEXTYPE_UINT32,
		.cull_mode = SG_CULLMODE_BACK,
		.face_winding = SG_FACEWINDING_CCW,
	});


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