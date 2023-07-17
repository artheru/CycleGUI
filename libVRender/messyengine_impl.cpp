#include "me_impl.h"

// ======== Sub implementations =========
#include "groundgrid.hpp"
#include "camera.hpp"
#include "init_impl.hpp"
#include "objects.hpp"
#include "skybox.hpp"

#include "interfaces.hpp"

static void me_getTexFloats(sg_image img_id, glm::vec4* pixels, int x, int y, int w, int h) {
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, img_id.id);
	SOKOL_ASSERT(img->gl.target == GL_TEXTURE_2D);
	SOKOL_ASSERT(0 != img->gl.tex[img->cmn.active_slot]);
	
	static GLuint newFbo = 0;
	GLuint oldFbo = 0;
	if (newFbo == 0) {
		glGenFramebuffers(1, &newFbo);
	}
	glGetIntegerv(GL_FRAMEBUFFER_BINDING, (GLint*)&oldFbo);
	glBindFramebuffer(GL_FRAMEBUFFER, newFbo);
	glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, img->gl.tex[img->cmn.active_slot], 0);
	glReadPixels(x, y, w, h, GL_RGBA, GL_FLOAT, pixels);
	glBindFramebuffer(GL_FRAMEBUFFER, oldFbo);
	_SG_GL_CHECK_ERROR();
}

void GLAPIENTRY DebugCallback(GLenum source, GLenum type, GLuint id, GLenum severity, GLsizei length, const GLchar* message, const void* userParam) {
	std::cout << "OpenGL Debug Message: " << message << std::endl;
}

int lastW, lastH;

SSAOUniforms_t ssao_uniforms{
	.weight = 3.0,
	.uSampleRadius = 20.0,
	.uBias = 0.1,
	.uAttenuation = {1.37f,0.84f},
};


void DrawWorkspace(int w, int h)
{
	// draw
	camera->Resize(w, h);
	camera->UpdatePosition();

	auto vm = camera->GetViewMatrix();
	auto pm = camera->GetProjectionMatrix();

	auto pv = pm * vm;

	// Graphics part:
	// Orbital Camera(perspective/orthogonal, height focus controllable), infinite grid, ruler, guizmo
	// toggle: mossaic reflective darkgray ground, blue sky, sun. (screen space reflection)
	// PC: eye-dome, near big far not vanish; PC meshes LOD, degrade to volume distance enough,
	// MatCap Material, Gltf loader, OIT, soft shadow, SSAO
	// objects: volumetric, point cloud, lidar2d-map, line, mesh(gltf), picking via GPU(padded), object border,
	// effect: bloom.
	// maybe: plain bgfx?
	// graphics start:
	// part1: grid
	// part2: point+line pass
	// part3: eye-dome
	// part4: mesh(matcap)
	// part5: point2d map.

	if (lastW!=w ||lastH!=h)
	{
		ResetEDLPass();
		GenPasses(w, h);
		ui_state.painter_data.resize(w * h);
		std::fill(ui_state.painter_data.begin(), ui_state.painter_data.end(), 0);
	}
	lastW = w;
	lastH = h;


	auto use_paint_selection = false;

	if (ui_state.selecting)
	{
		auto& wstate = ui_state.workspace_state.top();
		if (wstate.selecting_mode == paint)
		{
			// draw_image.
			for (int j = ui_state.mouseY - wstate.paint_selecting_radius; j < ui_state.mouseY + wstate.paint_selecting_radius; ++j)
				for (int i = ui_state.mouseX - wstate.paint_selecting_radius; i < ui_state.mouseX + wstate.paint_selecting_radius; ++i)
				{
					if (0 <= i && i < w && 0 <= j && j < h && sqrtf((i - ui_state.mouseX) * (i - ui_state.mouseX) + (j - ui_state.mouseY) * (j - ui_state.mouseY)) < wstate.paint_selecting_radius)
					{
						ui_state.painter_data[j * w + i] = 255;
					}
				}
			//update texture;
			sg_update_image(graphics_state.ui_selection, sg_image_data{
					.subimage = {{ {ui_state.painter_data.data(), (size_t)(w * h)} }}
				});
		}
		use_paint_selection = true;
	}


	{
		// transform to get mats.
		std::vector<int> renderings;
		sg_begin_pass(graphics_state.instancing.pass, graphics_state.instancing.pass_action);
		sg_apply_pipeline(graphics_state.instancing.pip);
		int offset = 0;
		for (int i = 0; i < gltf_classes.ls.size(); ++i)
		{
			auto t = gltf_classes.get(i);
			renderings.push_back(offset);
			offset += t->compute_mats(vm, offset, i);
		}
		sg_end_pass();

		// first draw point clouds, so edl only reference point's depth => pc_depth.
		sg_begin_pass(graphics_state.pc_primitive.pass, &graphics_state.pc_primitive.pass_action);
		sg_apply_pipeline(point_cloud_simple_pip);
		for (int i=0; i<pointclouds.ls.size(); ++i)
		{
			// todo: perform some culling?
			const auto [n, pcBuf, colorBuf, position, quaternion, flag] = *pointclouds.get(i);
			sg_apply_bindings(sg_bindings{ .vertex_buffers = {pcBuf, colorBuf} });
			vs_params_t vs_params{ .mvp = pv * translate(glm::mat4(1.0f), position) * mat4_cast(quaternion) , .dpi = camera->dpi , .pc_id = i};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_draw(0, n, 1);
		}
		sg_end_pass();

		sg_begin_pass(graphics_state.primitives.pass, &graphics_state.primitives.pass_action);

		for (int i = 0; i < gltf_classes.ls.size(); ++i)
			gltf_classes.get(i)->render(vm, pm, false, renderings[i], i);

		sg_end_pass();
		 

		
		// draw lines

		// draw gltf. (gpu selecting/selected)

		// === post processing ===
		// ---ssao---
		ssao_uniforms.P = pm;
		ssao_uniforms.iP = glm::inverse(pm);
		ssao_uniforms.iV = glm::inverse(vm);
		ssao_uniforms.cP = camera->position;
		ssao_uniforms.uDepthRange[0] = cam_near;
		ssao_uniforms.uDepthRange[1] = cam_far;
		//ImGui::DragFloat("uSampleRadius", &ssao_uniforms.uSampleRadius, 0.1, 0, 100);
		//ImGui::DragFloat("uBias", &ssao_uniforms.uBias, 0.003, -0.5, 0.5);
		//ImGui::DragFloat2("uAttenuation", ssao_uniforms.uAttenuation, 0.04, -10, 10);
		//ImGui::DragFloat("weight", &ssao_uniforms.weight, 0.1, -10, 10);
		// ImGui::DragFloat2("uDepthRange", ssao_uniforms.uDepthRange, 0.05, 0, 100);

		sg_begin_pass(graphics_state.ssao.pass, &graphics_state.ssao.pass_action);
		sg_apply_pipeline(graphics_state.ssao.pip);
		sg_apply_bindings(graphics_state.ssao.bindings);
		sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(ssao_uniforms));
		sg_draw(0, 4, 1);
		sg_end_pass();

		sg_begin_pass(graphics_state.ssao.blur_pass, &graphics_state.ssao.pass_action);
		sg_apply_pipeline(graphics_state.kuwahara_blur.pip);
		sg_apply_bindings(graphics_state.ssao.blur_bindings);
		sg_draw(0, 4, 1);
		sg_end_pass();

		// sobel corner (hovering and selected).


		// --- shadow map pass ---
		// sg_begin_pass(graphics_state.shadow_map.pass, &graphics_state.shadow_map.pass_action);
		// _draw_gltf_shadows(vm, pm);
		// sg_apply_pipeline(graphics_state.pc_pip_depth);
		// sg_end_pass();

		// bloom pass

		// --- edl lo-res pass
		sg_begin_pass(graphics_state.edl_lres.pass, &graphics_state.edl_lres.pass_action);
		sg_apply_pipeline(graphics_state.edl_lres_pip);
		sg_apply_bindings(graphics_state.edl_lres.bind);
		depth_blur_params_t edl_params{ .kernelSize = 7, .scale = 1, .pnear = cam_near, .pfar = cam_far };
		sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(edl_params));
		sg_draw(0, 4, 1);
		sg_end_pass();
	}

	// === hovering information ===
	glm::vec4 cnids[1];
	me_getTexFloats(graphics_state.tcin_buffer, cnids, ui_state.mouseX, h - ui_state.mouseY, 1, 1); // note: from left bottom corner...

	if (cnids[0].x == 1)
	{
		int pcid = cnids[0].y;
		int pid = int(cnids[0].z) * 16777216 + (int)cnids[0].w;
		ui_state.mousePointingType = "point_cloud";
		ui_state.mousePointingInstance = std::get<1>(pointclouds.ls[pcid]);
		ui_state.mousePointingSubId = pid;
		// ui_state.mouse_subID = pid;
		// ui_state.mouse_type = 1;
		// ui_state.mouse_instance = pcid;
		// ui_state.hover_id = cnids[0];

		if (ui_state.workspace_state.top().hoverables.contains(ui_state.mousePointingInstance))
		{
			ui_state.hover_type = 1;
			ui_state.hover_instance_id = pcid;
			ui_state.hover_node_id = -1;
		}
	}
	else if (cnids[0].x > 999)
	{
		int class_id = int(cnids[0].x) - 1000;
		int instance_id = int(cnids[0].y) * 16777216 + (int)cnids[0].z;
		int node_id = int(cnids[0].w);
		ui_state.mousePointingType = std::get<1>(gltf_classes.ls[class_id]);
		ui_state.mousePointingInstance = std::get<1>(gltf_classes.get(class_id)->objects.ls[instance_id]);
		ui_state.mousePointingSubId = node_id;

		//ui_state.mouse_type = class_id + 1000;
		//ui_state.mouse_instance = instance_id;
		//ui_state.hover_id = cnids[0];

		if (ui_state.workspace_state.top().hoverables.contains(ui_state.mousePointingInstance))
		{
			ui_state.hover_type = class_id + 1000;
			ui_state.hover_instance_id = instance_id;
			ui_state.hover_node_id = -1;
		}
		if (ui_state.workspace_state.top().sub_hoverables.contains(ui_state.mousePointingInstance))
		{
			ui_state.hover_type = class_id + 1000;
			ui_state.hover_instance_id = instance_id;
			ui_state.hover_node_id = node_id;
		}
	}

	
	//

	sg_begin_default_pass(&graphics_state.default_passAction, w, h);
	{
		// sky quad:
		_draw_skybox(vm, pm);

		// ground:
		// draw partial ground plane for shadow casting (shadow computing for plane doesn't need ground's depth on camera view, it can be computed directly.
		std::vector<glm::vec3> ground_instances;
		for (int i = 0; i < gltf_classes.ls.size(); ++i) {
			auto c = gltf_classes.get(i);
			auto t = c->objects;
			for (int j = 0; j < t.ls.size(); ++j)
				ground_instances.push_back(glm::vec3(t.get(j)->position.x, t.get(j)->position.y, c->sceneDim.radius));
		}

		if (ground_instances.size() > 0) {
			sg_apply_pipeline(graphics_state.gltf_ground_pip);
			graphics_state.gltf_ground_binding.vertex_buffers[1] = sg_make_buffer(sg_buffer_desc{
				.data = {ground_instances.data(), ground_instances.size() * sizeof(glm::vec3)}
				});
			sg_apply_bindings(graphics_state.gltf_ground_binding);
			gltf_ground_mats_t u{ pm * vm, camera->position };
			sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(u));
			sg_draw(0, 6, ground_instances.size());
			sg_destroy_buffer(graphics_state.gltf_ground_binding.vertex_buffers[1]);
		}

		// composing (aware of depth)
		sg_apply_pipeline(graphics_state.composer.pip);
		sg_apply_bindings(graphics_state.composer.bind);
		auto wnd = window_t{
			.w = float(w), .h = float(h), .pnear = cam_near, .pfar = cam_far,
			.ipmat = glm::inverse(pm),
			.ivmat = glm::inverse(vm),
			.pmat = pm,
			.pv = pv,
			.campos = camera->position,
			.lookdir = glm::normalize(camera->stare - camera->position),
		};
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(wnd));
		sg_draw(0, 4, 1);

		// ui-composing.
		sg_apply_pipeline(graphics_state.ui_composer.pip);
		sg_apply_bindings(graphics_state.ui_composer.bind);
		auto composing = ui_composing_t{
			.draw_sel = use_paint_selection ? 1.0f : 0.0f,
			.border_size = 5,
			.border_color = glm::vec3(1.0,1.0,0)
		};
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_ui_composing, SG_RANGE(composing));
		sg_draw(0, 4, 1);

		// billboards
		
		// grid:
		grid->Draw(*camera);

		// debug:
		std::vector<sg_image> debugArr = {
			graphics_state.primitives.color ,
			//graphics_state.primitives.depth ,
			//graphics_state.primitives.normal,
			//graphics_state.edl_lres.color,
			//graphics_state.ssao.image,
			graphics_state.ssao.blur_image
		};
		sg_apply_pipeline(graphics_state.dbg.pip);
		for (int i=0; i<debugArr.size(); ++i)
		{
			sg_apply_viewport((i/4)*160, (i%4)*120, 160, 120, false);
			graphics_state.dbg.bind.fs_images[SLOT_tex] = debugArr[i];
			sg_apply_bindings(&graphics_state.dbg.bind);
			sg_draw(0, 4, 1);
		}
		sg_apply_viewport(0, 0, w, h, false);
	}
	sg_end_pass();

	sg_commit();

#ifdef _DEBUG
	// if (ImGui::BeginMainMenuBar()) {
	// 	if (ImGui::BeginMenu("sokol-gfx")) {
	// 		ImGui::MenuItem("Capabilities", 0, &sg_imgui.caps.open);
	// 		ImGui::MenuItem("Buffers", 0, &sg_imgui.buffers.open);
	// 		ImGui::MenuItem("Images", 0, &sg_imgui.images.open);
	// 		ImGui::MenuItem("Shaders", 0, &sg_imgui.shaders.open);
	// 		ImGui::MenuItem("Pipelines", 0, &sg_imgui.pipelines.open);
	// 		ImGui::MenuItem("Passes", 0, &sg_imgui.passes.open);
	// 		ImGui::MenuItem("Calls", 0, &sg_imgui.capture.open);
	// 		ImGui::EndMenu();
	// 	}
	// 	ImGui::EndMainMenuBar();
	// }
	// sg_imgui_draw(&sg_imgui);
#endif
	

}

void InitGL(int w, int h)
{

	lastW = w;
	lastH = h;

	ui_state.painter_data.resize(w * h, 0);

	glewInit();

	// Set OpenGL states
	glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
	
	sg_desc desc = {
		.buffer_pool_size = 65535,
		.image_pool_size = 65535,
		.logger = {.func = slog_func, },
	};
	sg_setup(&desc);

	glEnable(GL_VERTEX_PROGRAM_POINT_SIZE);
	glHint(GL_POINT_SMOOTH_HINT, GL_FASTEST);
	glEnable(GL_POINT_SPRITE);

	camera = new Camera(glm::vec3(0.0f, 0.0f, 0.0f), 10, w, h, 0.2);
	grid = new GroundGrid();

	init_sokol();

	GenPasses(w, h);
}

