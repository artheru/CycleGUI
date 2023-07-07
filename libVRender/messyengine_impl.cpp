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
	}
	lastW = w;
	lastH = h;
	
	{
		// transform to get mats.
		std::vector<std::tuple<gltf_class*, int>> renderings;
		sg_begin_pass(graphics_state.instancing.pass, graphics_state.instancing.pass_action);
		sg_apply_pipeline(graphics_state.instancing.pip);
		int offset = 0;
		for (auto& class_ : classes)
		{
			renderings.push_back(std::tuple(class_.second,offset));
			offset += class_.second->compute_mats(vm, offset);
		}
		sg_end_pass();

		// first draw point clouds, so edl only reference point's depth => pc_depth.
		sg_begin_pass(graphics_state.pc_primitive.pass, &graphics_state.pc_primitive.pass_action);
		sg_apply_pipeline(point_cloud_simple_pip);
		for (auto& entry : pointClouds)
		{
			// perform some culling.
			const auto& [n, pcBuf, colorBuf, position, quaternion] = entry.second;
			sg_apply_bindings(sg_bindings{ .vertex_buffers = {pcBuf, colorBuf} });
			vs_params_t vs_params{ .mvp = pv * translate(glm::mat4(1.0f), position) * mat4_cast(quaternion) , .dpi = camera->dpi };
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_draw(0, n, 1);
		}
		sg_end_pass();

		sg_begin_pass(graphics_state.primitives.pass, &graphics_state.primitives.pass_action);
		for (auto tup : renderings)
		{
			std::get<0>(tup)->render(vm, pm, false, std::get<1>(tup));
		}

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
		// for (auto& entry : pointClouds)
		// {
		// 	// perform some culling.
		// 	const auto& [pc, pcBuf, colorBuf] = entry.second;
		// 	sg_apply_bindings(sg_bindings{ .vertex_buffers = {pcBuf, colorBuf} });
		// 	vs_params_t vs_params{ .mvp = pv * translate(glm::mat4(1.0f), pc.position) * mat4_cast(pc.quaternion) , .dpi = camera->dpi };
		// 	sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
		// 	sg_draw(0, pc.x_y_z_Sz.size(), 1);
		// }
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
	
	sg_begin_default_pass(&graphics_state.default_passAction, w, h);
	{
		// sky quad:
		_draw_skybox(vm, pm);

		// ground:
		// draw partial ground plane for shadow casting (shadow computing for plane doesn't need ground's depth on camera view, it can be computed directly.
		std::vector<glm::vec3> ground_instances;
		for (auto& class_ : classes)
			for (auto obj : class_.second->objects)
				ground_instances.push_back(glm::vec3(obj.second.position.x, obj.second.position.y, class_.second->sceneDim.radius));

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

		// ground reflections.
		// billboards
		
		// checkGLError(__FILE__, __LINE__);
		// // write depth value.
		// _sg_pass_t* pass = _sg_lookup_pass(&_sg.pools, graphics_state.edl_hres.pass.id);
		// auto fbo = pass->gl.fb;
		// glBindFramebuffer(GL_READ_FRAMEBUFFER, fbo);
		// glBindFramebuffer(GL_DRAW_FRAMEBUFFER, 0);
		// glBlitFramebuffer(
		// 	0, 0, w, h,   // Source region (FBO dimensions)
		// 	0, 0, w, h,   // Destination region (window dimensions)
		// 	GL_DEPTH_BUFFER_BIT,   // Buffer mask (depth buffer)
		// 	GL_NEAREST            // Sampling interpolation
		// );
		// checkGLError(__FILE__, __LINE__);

		// grid:
		// todo: grid should be occluded with transparency, compare depth value.
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
	
	glm::vec4 cnids[1];
	me_getTexFloats(graphics_state.tcin_buffer, cnids, mouseX, h-mouseY, 1, 1); // note: from left bottom corner...

	ImGui::Text(std::format("pointing to {},{},{},{}", (int)cnids[0].x, (int)cnids[0].y, (int)cnids[0].z, (int)cnids[0].w).c_str());
	if (cnids[0].x == 1)
	{
		int pcid = cnids[0].y;
		int pid = int(cnids[0].z) * 16777216 + (int)cnids[0].w;
		ImGui::Text("selecting point cloud %d:%d", pcid, pid);
	}else if (cnids[0].x == 2)
	{
		int class_id = int(cnids[0].x) / 64;
		int instance_id = int(cnids[0].y) * 16777216 + (int)cnids[0].z;
		int node_id = int(cnids[0].w);
		ImGui::Text("selecting gltf object cls_%d:obj_%d:node_%d", class_id, instance_id, node_id);
	}

}

void InitGL(int w, int h)
{

	lastW = w;
	lastH = h;
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

