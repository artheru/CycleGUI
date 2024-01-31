#include "me_impl.h"

// ======== Sub implementations =========
#include <imgui_internal.h>

#include "groundgrid.hpp"
#include "camera.hpp"
#include "ImGuizmo.h"
#include "init_impl.hpp"
// #include "gltf2ozz.hpp"
#include <bitset>

#include "objects.hpp"
#include "skybox.hpp"

#include "interfaces.hpp"
#include <glm/gtx/matrix_decompose.hpp>


struct obj_action_state_t{
	me_obj* obj;
	glm::mat4 intermediate;
};
std::vector<obj_action_state_t> obj_action_state;

void ClearSelection()
{
	//ui_state.selected.clear();
	for (int i = 0; i < pointclouds.ls.size(); ++i)
	{
		auto t = pointclouds.get(i);
		t->flag &= ~(1 << 6); // not selected as whole
		if (t->flag & (1 << 8)) { // only when sub selectable update sel image
			int sz = ceil(sqrt(t->capacity / 8));
			memset(t->cpuSelection, 0, sz*sz);
			sg_update_image(t->pcSelection, sg_image_data{
					.subimage = {{ { t->cpuSelection, (size_t)(sz*sz) } }} }); //neither selecting item.
		}
	}

	for (int i = 0; i < gltf_classes.ls.size(); ++i)
	{
		auto objs = gltf_classes.get(i)->objects;
		for(int j=0; j<objs.ls.size(); ++j)
		{
			auto obj = objs.get(j);
			obj->flags &= ~(1 << 3); // not selected as whole
			obj->flags &= ~(1 << 6);

			for (auto& a : obj->nodeattrs)
				a.flag = (int(a.flag) & ~(1 << 3));
		}
	}
}


void prepare_flags()
{


}

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
	.uBias = 0.3,
	.uAttenuation = {1.32f,0.84f},
};


glm::vec3 world2screen(glm::vec3 input, glm::mat4 v, glm::mat4 p, glm::vec2 screenSize)
{
	glm::vec4 a = p * v * glm::vec4(input, 1.0f);
	glm::vec3 b = glm::vec3(a) / a.w;
	glm::vec2 c = glm::vec2(b);
	return glm::vec3((c.x * 0.5f + 0.5f) * screenSize.x, (c.y * 0.5f + 0.5f) * screenSize.y, a.w);
}

void DrawWorkspace(int w, int h, ImGuiDockNode* disp_area, ImDrawList* dl, ImGuiViewport* viewport);

void DrawWorkspace(int w, int h)
{
	ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
	auto vp = ImGui::GetMainViewport();
	auto dl = ImGui::GetBackgroundDrawList(vp);
	if (node) {
		auto central = ImGui::DockNodeGetRootNode(node)->CentralNode;
		DrawWorkspace(central->Size.x, central->Size.y, central, dl, vp);
	}
}

void updateTextureW4K(sg_image simg, int objmetah, const void* data, sg_pixel_format format)
{
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, simg.id);

	_sg_gl_cache_store_texture_binding(0);
	_sg_gl_cache_bind_texture(0, img->gl.target, img->gl.tex[img->cmn.active_slot]);
	GLenum gl_img_target = img->gl.target;
	glTexSubImage2D(gl_img_target, 0,
		0, 0,
		4096, objmetah,
		_sg_gl_teximage_format(format), _sg_gl_teximage_type(format),
		data);
	_sg_gl_cache_restore_texture_binding(0);
}

void DrawWorkspace(int w, int h, ImGuiDockNode* disp_area, ImDrawList* dl, ImGuiViewport* viewport)
{
	// draw
	camera->Resize(w, h);
	camera->UpdatePosition();

	auto vm = camera->GetViewMatrix();
	auto pm = camera->GetProjectionMatrix();

	auto pv = pm * vm;

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
	auto& wstate = ui_state.workspace_state.top();

	// draw spot texts:
	for (int i = 0; i < spot_texts.ls.size(); ++i)
	{
		auto t = spot_texts.get(i);
		for (int j=0; j<t->texts.size(); ++j)
		{
			auto pos=world2screen(t->texts[j].position, vm, pm, glm::vec2(w, h));
			if (pos.z>=0)
				dl->AddText(ImVec2(disp_area->Pos.x + pos.x, disp_area->Pos.y + h - pos.y), t->texts[j].color, t->texts[j].text.c_str());
		}
	}
	

	if (wstate.selecting_mode == paint && !ui_state.selecting)
	{
		auto pos = disp_area->Pos;
		dl->AddCircle(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), wstate.paint_selecting_radius, 0xff0000ff);
	}
	if (ui_state.selecting)
	{
		if (wstate.selecting_mode == drag)
		{
			auto pos = disp_area->Pos;
			auto st = ImVec2(std::min(ui_state.mouseX, ui_state.select_start_x) + pos.x, std::min(ui_state.mouseY, ui_state.select_start_y) + pos.y);
			auto ed = ImVec2(std::max(ui_state.mouseX, ui_state.select_start_x) + pos.x, std::max(ui_state.mouseY, ui_state.select_start_y) + pos.y);
			dl->AddRectFilled(st, ed, 0x440000ff);
			dl->AddRect(st, ed, 0xff0000ff);
		}
		else if (wstate.selecting_mode == paint)
		{
			auto pos = disp_area->Pos;
			dl->AddCircleFilled(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), wstate.paint_selecting_radius, 0x440000ff);
			dl->AddCircle(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), wstate.paint_selecting_radius, 0xff0000ff);

			// draw_image.
			for (int j = (ui_state.mouseY - wstate.paint_selecting_radius)/4; j <= (ui_state.mouseY + wstate.paint_selecting_radius)/4+1; ++j)
				for (int i = (ui_state.mouseX - wstate.paint_selecting_radius)/4; i <= (ui_state.mouseX + wstate.paint_selecting_radius)/4+1; ++i)
				{
					if (0 <= i && i < w/4 && 0 <= j && j < h/4 && 
						sqrtf((i*4 - ui_state.mouseX) * (i*4 - ui_state.mouseX) + (j*4 - ui_state.mouseY) * (j*4 - ui_state.mouseY)) < wstate.paint_selecting_radius)
					{
						ui_state.painter_data[j * (w/4) + i] = 255;
					}
				}

			//update texture;
			sg_update_image(graphics_state.ui_selection, sg_image_data{
					.subimage = {{ {ui_state.painter_data.data(), (size_t)((w/4) * (h / 4))} }}
				});
			use_paint_selection = true;
		}
	}

	prepare_flags();

	static bool draw_3d = true, compose = true;
	if (ui_state.displayRenderDebug) {
		ImGui::Checkbox("draw_3d", &draw_3d);
		ImGui::Checkbox("compose", &compose);
		ImGui::Checkbox("useEDL", &wstate.useEDL);
		ImGui::Checkbox("useSSAO", &wstate.useSSAO);
		ImGui::Checkbox("useGround", &wstate.useGround);
		ImGui::Checkbox("useShineBloom", &wstate.useBloom);
		ImGui::Checkbox("useBorder", &wstate.useBorder);
	}

	int instance_count=0, node_count = 0;
	if (draw_3d){
		// gltf transform to get mats.
		std::vector<int> renderings;

		if (!gltf_classes.ls.empty()) {

			for (int i = 0; i < gltf_classes.ls.size(); ++i)
			{
				auto t = gltf_classes.get(i);
				t->instance_offset = instance_count;
				instance_count += t->objects.ls.size();
				renderings.push_back(node_count);
				node_count += t->count_nodes();
			}

			if (node_count != 0) {

				int objmetah1 = (int)(ceil(node_count / 2048.0f)); //4096 width, stride 2 per node.
				int size1 = 4096 * objmetah1 * 32; //RGBA32F*2, 8floats=32B sizeof(s_transrot)=32.
				std::vector<s_pernode> transrot_per_node(size1);

				int objmetah2 = (int)(ceil(instance_count / 4096.0f)); // stride 1 per instance.
				int size2 = 4096 * objmetah2 * 16; //4 uint: animationid|start time.
				std::vector<s_perobj> animation_meta(size2);
				for (int i = 0; i < gltf_classes.ls.size(); ++i)
				{
					auto t = gltf_classes.get(i);
					if (t->objects.ls.empty()) continue;
					t->prepare_data(transrot_per_node, animation_meta, renderings[i], t->instance_offset);
				}

				{
					updateTextureW4K(graphics_state.instancing.node_meta, objmetah1, transrot_per_node.data(), SG_PIXELFORMAT_RGBA32F);
					updateTextureW4K(graphics_state.instancing.instance_meta, objmetah2, animation_meta.data(), SG_PIXELFORMAT_RGBA32UI);
				}

				//███ Compute node localmat: Translation Rotation on instance, node and Animation, also perform depth 4 instancing.
				sg_begin_pass(graphics_state.instancing.animation_pass, graphics_state.instancing.pass_action);
				sg_apply_pipeline(graphics_state.instancing.animation_pip);
				for (int i = 0; i < gltf_classes.ls.size(); ++i)
				{
					auto t = gltf_classes.get(i);
					if (t->objects.ls.empty()) continue;
					t->compute_node_localmat(vm, renderings[i]); // also multiplies view matrix.
				}
				sg_end_pass();

				//███ Propagate node hierarchy, we propagate 2 times in a group, one time at most depth 4.
				// sum{n=1~l}{4^n*C_l^n} => 4, 24|, 124, 624|.
				for (int i = 0; i < int(ceil(gltf_class::max_passes / 2.0f)); ++i) {
					sg_begin_pass(graphics_state.instancing.hierarchy_pass1, graphics_state.instancing.pass_action);
					sg_apply_pipeline(graphics_state.instancing.hierarchy_pip);
					for (int i = 0; i < gltf_classes.ls.size(); ++i)
					{
						auto t = gltf_classes.get(i);
						if (t->objects.ls.empty()) continue;
						t->node_hierarchy(renderings[i], i * 2);
					}
					sg_end_pass();

					sg_begin_pass(graphics_state.instancing.hierarchy_pass2, graphics_state.instancing.pass_action);
					sg_apply_pipeline(graphics_state.instancing.hierarchy_pip);
					for (int i = 0; i < gltf_classes.ls.size(); ++i)
					{
						auto t = gltf_classes.get(i);
						if (t->objects.ls.empty()) continue;
						t->node_hierarchy(renderings[i], i * 2 + 1);
					}
					sg_end_pass();
				}
				
				sg_begin_pass(graphics_state.instancing.final_pass, graphics_state.instancing.pass_action);
				sg_apply_pipeline(graphics_state.instancing.finalize_pip);
				// compute inverse of node viewmatrix.
				sg_apply_bindings(sg_bindings{
					.vertex_buffers = {
						//graphics_state.instancing.Z // actually nothing required.
					},
					.vs_images = {
						graphics_state.instancing.objInstanceNodeMvMats1
					}
					});
				sg_draw(0, node_count, 1);
				sg_end_pass();

				//
			}
		}

		// first draw point clouds, so edl only reference point's depth => pc_depth.
		sg_begin_pass(graphics_state.pc_primitive.pass, &graphics_state.pc_primitive.pass_action);
		sg_apply_pipeline(point_cloud_simple_pip);
		// should consider if pointcloud should draw "sprite" handle.
		for (int i=0; i<pointclouds.ls.size(); ++i)
		{
			// todo: perform some culling?
			auto t = pointclouds.get(i);
			if (t->n == 0) continue;
			int displaying = 0;
			if (t->flag & (1 << 0)) // border
				displaying |= 1;
			if (t->flag & (1 << 1)) // shine
				displaying |= 2;
			if (t->flag & (1 << 2)) // bring to front
				displaying |= 4;

			if (t->flag & (1 << 6)) // selected.
				displaying |= (1<<3);
			if (t->flag & (1 << 8)) // sub-selectable.
				displaying |= (1 << 5);
			
			auto hovering_pcid = -1;
			if (ui_state.hover_type == 1 && ui_state.hover_instance_id == i) {
				if ((t->flag & (1 << 7)) != 0)
					displaying |= (1 << 4);
				else if ((t->flag & (1 << 8)) != 0)
					hovering_pcid = ui_state.hover_node_id;
			}

			sg_apply_bindings(sg_bindings{ .vertex_buffers = {t->pcBuf, t->colorBuf}, .fs_images = {t->pcSelection} });
			vs_params_t vs_params{ .mvp = pv * translate(glm::mat4(1.0f), t->position) * mat4_cast(t->quaternion) , .dpi = camera->dpi , .pc_id = i,
				.displaying = displaying,
				.hovering_pcid = hovering_pcid,
				.shine_color_intensity = t->shine_color,
				.hover_shine_color_intensity = wstate.hover_shine,
				.selected_shine_color_intensity = wstate.selected_shine,
			};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_draw(0, t->n, 1);
		}
		sg_end_pass();

		// --- edl lo-res pass
		if (wstate.useEDL) {
			sg_begin_pass(graphics_state.edl_lres.pass, &graphics_state.edl_lres.pass_action);
			sg_apply_pipeline(graphics_state.edl_lres_pip);
			sg_apply_bindings(graphics_state.edl_lres.bind);
			depth_blur_params_t edl_params{ .kernelSize = 5, .scale = 1, .pnear = cam_near, .pfar = cam_far };
			sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(edl_params));
			sg_draw(0, 4, 1);
			sg_end_pass();
		}


		// actual gltf rendering.
		// todo: just use one call to rule all rendering.
		if (node_count!=0) {
			sg_begin_pass(graphics_state.primitives.pass, &graphics_state.primitives.pass_action);
			sg_apply_pipeline(graphics_state.gltf_pip);

			for (int i = 0; i < gltf_classes.ls.size(); ++i) {
				auto t = gltf_classes.get(i);
				if (t->objects.ls.empty()) continue;
				t->render(vm, pm, false, renderings[i], i);
			}

			sg_end_pass();
		}

		
		// draw lines

		// draw gltf. (gpu selecting/selected)

		// === post processing ===
		// ---ssao---
		if (wstate.useSSAO) {
			ssao_uniforms.P = pm;
			ssao_uniforms.iP = glm::inverse(pm);
			ssao_uniforms.iV = glm::inverse(vm);
			ssao_uniforms.cP = camera->position;
			ssao_uniforms.uDepthRange[0] = cam_near;
			ssao_uniforms.uDepthRange[1] = cam_far;
			ssao_uniforms.time = ui_state.getMsFromStart();
			ImGui::DragFloat("uSampleRadius", &ssao_uniforms.uSampleRadius, 0.1, 0, 100);
			ImGui::DragFloat("uBias", &ssao_uniforms.uBias, 0.003, -0.5, 0.5);
			ImGui::DragFloat2("uAttenuation", ssao_uniforms.uAttenuation, 0.01, -10, 10);
			//ImGui::DragFloat("weight", &ssao_uniforms.weight, 0.1, -10, 10);
			//ImGui::DragFloat2("uDepthRange", ssao_uniforms.uDepthRange, 0.05, 0, 100);

			sg_begin_pass(graphics_state.ssao.pass, &graphics_state.ssao.pass_action);
			sg_apply_pipeline(graphics_state.ssao.pip);
			sg_apply_bindings(graphics_state.ssao.bindings);
			sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(ssao_uniforms));
			sg_draw(0, 4, 1);
			sg_end_pass();

			// sg_begin_pass(graphics_state.ssao.blur_pass, &graphics_state.ssao.pass_action);
			// sg_apply_pipeline(graphics_state.kuwahara_blur.pip);
			// sg_apply_bindings(graphics_state.ssao.blur_bindings);
			// sg_draw(0, 4, 1);
			// sg_end_pass();
		}


		// shine-bloom.
		if (wstate.useBloom)
		{
			auto clear = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f },  } } ,
			};
			auto keep = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE } },
			};

			auto binding2to1 = sg_bindings{
				.vertex_buffers = {graphics_state.quad_vertices},
				.fs_images = {graphics_state.shine2}
			};
			auto binding1to2 = sg_bindings{
				.vertex_buffers = {graphics_state.quad_vertices},
				.fs_images = {graphics_state.shine1}
			};
			sg_begin_pass(graphics_state.ui_composer.shine_pass1to2, clear);
			sg_apply_pipeline(graphics_state.ui_composer.pip_dilateX);
			sg_apply_bindings(binding1to2);
			sg_draw(0, 4, 1);
			sg_end_pass();

			sg_begin_pass(graphics_state.ui_composer.shine_pass2to1, keep);
			sg_apply_pipeline(graphics_state.ui_composer.pip_dilateY);
			sg_apply_bindings(binding2to1);
			sg_draw(0, 4, 1);
			sg_end_pass();
			
			sg_begin_pass(graphics_state.ui_composer.shine_pass1to2, keep);
			sg_apply_pipeline(graphics_state.ui_composer.pip_blurX);
			sg_apply_bindings(binding1to2);
			sg_draw(0, 4, 1);
			sg_end_pass();
		}
	}
		
	//

	static float facFac = 0.49, fac2Fac = 1.16, fac2WFac = 0.82, colorFac = 0.37, reverse1 = 0.581, reverse2 = 0.017, edrefl = 0.27;
	
	int useFlag = (wstate.useEDL ? 1 : 0) | (wstate.useSSAO ? 2 : 0) | (wstate.useGround ? 4 : 0);

	sg_begin_default_pass(&graphics_state.default_passAction, viewport->Size.x, viewport->Size.y);
	// sg_begin_default_pass(&graphics_state.default_passAction, viewport->Size.x, viewport->Size.y);
	{

		sg_apply_viewport(disp_area->Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area->Pos.y-viewport->Pos.y + h), w, disp_area->Size.y, false);
		sg_apply_scissor_rect(0, 0, viewport->Size.x, viewport->Size.y, false);
		// sky quad:
		_draw_skybox(vm, pm);

		// ground:
		// draw partial ground plane for shadow casting (shadow computing for plane doesn't need ground's depth on camera view, it can be computed directly.
		std::vector<glm::vec3> ground_instances;
		for (int i = 0; i < gltf_classes.ls.size(); ++i) {
			auto c = gltf_classes.get(i);
			auto t = c->objects;
			for (int j = 0; j < t.ls.size(); ++j)
				ground_instances.emplace_back(t.get(j)->position.x, t.get(j)->position.y, c->sceneDim.radius);
		}
		if (!ground_instances.empty()) {
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
		if (compose) {
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

				.facFac = facFac,
				.fac2Fac = fac2Fac,
				.fac2WFac = fac2WFac,
				.colorFac = colorFac,
				.reverse1 = reverse1,
				.reverse2 = reverse2,
				.edrefl = edrefl,

				.useFlag = (float)useFlag
			};
			// ImGui::DragFloat("fac2Fac", &fac2Fac, 0.01, 0, 2);
			// ImGui::DragFloat("facFac", &facFac, 0.01, 0, 1);
			// ImGui::DragFloat("fac2WFac", &fac2WFac, 0.01, 0, 1);
			// ImGui::DragFloat("colofFac", &colorFac, 0.01, 0, 1);
			// ImGui::DragFloat("reverse1", &reverse1, 0.001, 0, 1);
			// ImGui::DragFloat("reverse2", &reverse2, 0.001, 0, 1);
			// ImGui::DragFloat("refl", &edrefl, 0.0005, 0, 1);
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(wnd));
			sg_draw(0, 4, 1);
		}

		// ground reflection.
		if (wstate.useGround) {
			sg_apply_pipeline(graphics_state.ground_effect.pip);
			sg_apply_bindings(graphics_state.ground_effect.bind);
			auto ug = uground_t{
				.w = float(w), .h = float(h), .pnear = cam_near, .pfar = cam_far,
				.ipmat = glm::inverse(pm),
				.ivmat = glm::inverse(vm),
				.pmat = pm,
				.pv = pv,
				.campos = camera->position,
				.lookdir = glm::normalize(camera->stare - camera->position),
				.time = ui_state.getMsFromStart()
			};
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(ug));
			sg_draw(0, 4, 1);
		}

		// billboards

		// grid:
		if (wstate.drawGrid)
			grid->Draw(*camera, disp_area);

		// ui-composing. (border, shine, bloom)
		// shine-bloom
		if (wstate.useBloom) {
			sg_apply_pipeline(graphics_state.ui_composer.pip_blurYFin);
			sg_apply_bindings(sg_bindings{ .vertex_buffers = {graphics_state.quad_vertices},.fs_images = {graphics_state.shine2} });
			sg_draw(0, 4, 1);
		}

		// border
		if (wstate.useBorder) {
			sg_apply_pipeline(graphics_state.ui_composer.pip_border);
			sg_apply_bindings(graphics_state.ui_composer.border_bind);
			auto composing = ui_composing_t{
				.draw_sel = use_paint_selection ? 1.0f : 0.0f,
				.border_colors = {wstate.hover_border_color.x, wstate.hover_border_color.y, wstate.hover_border_color.z, wstate.hover_border_color.w,
					wstate.selected_border_color.x, wstate.selected_border_color.y, wstate.selected_border_color.z, wstate.selected_border_color.w,
					wstate.world_border_color.x, wstate.world_border_color.y, wstate.world_border_color.z, wstate.world_border_color.w},
					//.border_size = 5,
			};
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_ui_composing, SG_RANGE(composing));
			sg_draw(0, 4, 1);
		}

		// debug:
		// std::vector<sg_image> debugArr = {
		// 	graphics_state.primitives.color,
		// 	graphics_state.primitives.color,
		// 	graphics_state.primitives.color,
		// 	graphics_state.primitives.color,
		// 	graphics_state.primitives.color,
		// 	graphics_state.primitives.color,
		// 	graphics_state.primitives.color,
		// 	graphics_state.primitives.color,
		// 	//graphics_state.primitives.depth ,
		// 	//graphics_state.primitives.normal,
		// 	//graphics_state.edl_lres.color,
		// 	//graphics_state.ssao.image,
		// 	//graphics_state.ssao.blur_image
		// };
		// sg_apply_pipeline(graphics_state.dbg.pip);
		// for (int i=0; i<debugArr.size(); ++i)
		// {
		// 	sg_apply_viewport((i/4+1)*160, (i%4)*120, 160, 120, false);
		// 	graphics_state.dbg.bind.fs_images[SLOT_tex] = debugArr[i];
		// 	sg_apply_bindings(&graphics_state.dbg.bind);
		// 	sg_draw(0, 4, 1);
		// }

		//sg_apply_viewport(0, 0, w, h, false);
	}
	sg_end_pass();

	sg_commit();


	// === hovering information === //todo: like click check 7*7 patch around the cursor.
	glm::vec4 hovering[1];
	me_getTexFloats(graphics_state.TCIN, hovering, ui_state.mouseX, h - ui_state.mouseY - 1, 1, 1); // note: from left bottom corner...

	if (ui_state.refreshStare) {
		ui_state.refreshStare = false;

		if (abs(camera->position.z - camera->stare.z) > 0.001) {
			glm::vec4 starepnt;
			me_getTexFloats(graphics_state.primitives.depth, &starepnt, w / 2, h / 2, 1, 1); // note: from left bottom corner...

			auto d = starepnt.x;
			if (d < 0.5) d += 0.5;
			float ndc = d * 2.0 - 1.0;
			float z = (2.0 * cam_near * cam_far) / (cam_far + cam_near - ndc * (cam_far - cam_near)); // pointing mesh's depth.
			printf("d=%f, z=%f\n", d, z);
			//calculate ground depth.
			float gz = camera->position.z / (camera->position.z - camera->stare.z) * glm::distance(camera->position, camera->stare);
			if (gz > 0) {
				if (z < gz)
				{
					// set stare to mesh point.
					camera->stare = glm::normalize(camera->stare - camera->position) * z + camera->position;
					camera->distance = z;
				}else
				{
					camera->stare = glm::normalize(camera->stare - camera->position) * gz + camera->position;
					camera->distance = gz;
					camera->stare.z = 0;
				}
			}
		}
	}

	ui_state.hover_type = 0;

	ui_state.mousePointingType = "/";
	ui_state.mousePointingInstance = "/";
	ui_state.mousePointingSubId = -1;
	if (hovering[0].x == 1)
	{
		int pcid = hovering[0].y;
		int pid = int(hovering[0].z) * 16777216 + (int)hovering[0].w;
		ui_state.mousePointingType = "point_cloud";
		ui_state.mousePointingInstance = std::get<1>(pointclouds.ls[pcid]);
		ui_state.mousePointingSubId = pid;

		if (wstate.hoverables.find(ui_state.mousePointingInstance) != wstate.hoverables.end() || wstate.sub_hoverables.find(ui_state.mousePointingInstance) != wstate.sub_hoverables.end())
		{
			ui_state.hover_type = 1;
			ui_state.hover_instance_id = pcid;
			ui_state.hover_node_id = pid;
		}
	}
	else if (hovering[0].x > 999)
	{
		int class_id = int(hovering[0].x) - 1000;
		int instance_id = int(hovering[0].y) * 16777216 + (int)hovering[0].z;
		int node_id = int(hovering[0].w);
		ui_state.mousePointingType = std::get<1>(gltf_classes.ls[class_id]);
		ui_state.mousePointingInstance = std::get<1>(gltf_classes.get(class_id)->objects.ls[instance_id]);
		ui_state.mousePointingSubId = node_id;

		if (wstate.hoverables.find(ui_state.mousePointingInstance) != wstate.hoverables.end())
		{
			ui_state.hover_type = class_id + 1000;
			ui_state.hover_instance_id = instance_id;
			ui_state.hover_node_id = -1;
		}
		if (wstate.sub_hoverables.find(ui_state.mousePointingInstance) != wstate.sub_hoverables.end())
		{
			ui_state.hover_type = class_id + 1000;
			ui_state.hover_instance_id = instance_id;
			ui_state.hover_node_id = node_id;
		}
	}
	if (ui_state.displayRenderDebug)
	{
		ImGui::Text("pointing:%s>%s.%d", ui_state.mousePointingType.c_str(), ui_state.mousePointingInstance.c_str(), ui_state.mousePointingSubId);
	}

	if (ui_state.extract_selection)
	{
		ui_state.extract_selection = false;

		auto test = [](glm::vec4 pix) -> bool {
			if (pix.x == 1)
			{
				int pcid = pix.y;
				int pid = int(pix.z) * 16777216 + (int)pix.w;
				auto t = pointclouds.get(pcid);
				if (t->flag & (1 << 4)) {
					// select by point.
					if ((t->flag & (1 << 7)))
					{
						t->flag |= (1 << 6);// selected as a whole
						return true;
					}
					else if (t->flag & (1 << 8))
					{
						t->flag |= (1 << 9);// sub-selected
						t->cpuSelection[pid / 8] |= (1 << (pid % 8));
						return true;
					}
				}
				// todo: process select by handle.
			}
			else if (pix.x > 999)
			{
				int class_id = int(pix.x) - 1000;
				int instance_id = int(pix.y) * 16777216 + (int)pix.z;
				int node_id = int(pix.w);

				auto t = gltf_classes.get(class_id);
				auto obj = t->objects.get(instance_id);
				if (obj->flags & (1 << 4))
				{
					obj->flags |= (1 << 3);
					return true;
				}
				else if (obj->flags & (1 << 5))
				{
					obj->flags |= (1 << 6);
					obj->nodeattrs[node_id].flag = ((int)obj->nodeattrs[node_id].flag | (1 << 3));
					return true;
				}
			}
			return false;
		};

		ui_state.selpix.clear();
		if (wstate.selecting_mode == click)
		{
			ui_state.selpix.resize(7 * 7);

			me_getTexFloats(graphics_state.TCIN, ui_state.selpix.data(), ui_state.mouseX-3, h - (ui_state.mouseY+3), 7, 7); // note: from left bottom corner...

			int order[] = { 24, 25, 32, 31, 30, 23, 16, 17, 18, 19, 26, 33, 40, 39, 38, 37, 36, 29, 22, 15, 8, 9, 10, 11, 12, 13, 20, 27, 34, 41, 48, 47, 46, 45, 44, 43, 42, 35, 28, 21, 14, 7, 0, 1, 2, 3, 4, 5, 6 };
			for (int i=0; i<49; ++i)
			{
				if (test(ui_state.selpix[order[i]])) break;
			}
		}else if (wstate.selecting_mode == drag)
		{
			ui_state.selpix.resize(w * h);
			auto stx = std::min(ui_state.mouseX, ui_state.select_start_x);
			auto sty = std::max(ui_state.mouseY, ui_state.select_start_y);
			auto sw = std::abs(ui_state.mouseX - ui_state.select_start_x);
			auto sh = std::abs(ui_state.mouseY - ui_state.select_start_y);
			me_getTexFloats(graphics_state.TCIN, ui_state.selpix.data(),stx, h - sty, sw,sh); // note: from left bottom corner...
			for (int i = 0; i < sw * sh; ++i)
				test(ui_state.selpix[i]);
		}else if (wstate.selecting_mode == paint)
		{
			ui_state.selpix.resize(w * h);
			me_getTexFloats(graphics_state.TCIN, ui_state.selpix.data(), ui_state.mouseX, h - ui_state.mouseY - 1, 1, 1);
			me_getTexFloats(graphics_state.TCIN, ui_state.selpix.data(), 0, 0, w, h);
			for (int j = 0; j < h;++j)
			for (int i = 0; i < w; ++i)
				if (ui_state.painter_data[(j / 4) * (w / 4) + (i / 4)] > 0)
					test(ui_state.selpix[(h - j - 1) * w + i]);
		}
		
		// todo: display point cloud's handle, and test hovering.

		// apply changes for next draw:
		for (int i = 0; i < pointclouds.ls.size(); ++i)
		{
			auto t = pointclouds.get(i);
			if (t->flag & (1 << 8))
			{
				int sz = ceil(sqrt(t->capacity / 8));
				sg_update_image(t->pcSelection, sg_image_data{
					.subimage = {{ { t->cpuSelection, (size_t)(sz*sz) } }} }); //neither selecting item.
			}
		}

		ui_state.feedback_type = 1; // feedback selection to user.
	}

	if (ui_state.selectedGetCenter)
	{
		ui_state.selectedGetCenter = false;
		obj_action_state.clear();
		glm::vec3 pos(0.0f);
		float n = 0;
		// selecting feedback.
		for (int i = 0; i < pointclouds.ls.size(); ++i)
		{
			auto t = std::get<0>(pointclouds.ls[i]);
			auto name = std::get<1>(pointclouds.ls[i]);
			if ((t->flag & (1 << 6)) || (t->flag & (1 << 9))) {   //selected point cloud
				pos += t->position;
				obj_action_state.push_back(obj_action_state_t{.obj = t });
				n += 1;
			}
		}

		for (int i = 0; i < gltf_classes.ls.size(); ++i)
		{
			auto objs = gltf_classes.get(i)->objects;
			for (int j = 0; j < objs.ls.size(); ++j)
			{
				auto t = std::get<0>(objs.ls[i]);
				auto name = std::get<1>(objs.ls[i]);

				if ((t->flags & (1 << 3)) || (t->flags & (1 << 6))) // selected gltf
				{
					pos += t->position;
					obj_action_state.push_back(obj_action_state_t{ .obj = t });
					n += 1;
				}
			}
		}

		ui_state.gizmoCenter = pos / n;
		ui_state.gizmoQuat = glm::identity<glm::quat>();

		glm::mat4 gmat = glm::mat4_cast(ui_state.gizmoQuat);
		gmat[3] = glm::vec4(ui_state.gizmoCenter, 1.0f);
		glm::mat4 igmat = glm::inverse(gmat);

		for (auto& st : obj_action_state)
		{
			glm::mat4 mat = glm::mat4_cast(st.obj->quaternion);
			mat[3] = glm::vec4(st.obj->position, 1.0f);

			st.intermediate = igmat * mat;
		}
	}

	ImGuizmo::SetOrthographic(false);
	ImGuizmo::SetDrawlist(dl);
    ImGuizmo::SetRect(disp_area->Pos.x, disp_area->Pos.y, w, h);
	ImGuizmo::SetGizmoSizeClipSpace(120.0f * camera->dpi / w);
	if (wstate.function== gizmo_rotateXYZ || wstate.function == gizmo_moveXYZ)
	{
		glm::mat4 mat = glm::mat4_cast(ui_state.gizmoQuat);
		mat[3] = glm::vec4(ui_state.gizmoCenter, 1.0f);

		int getGType = ImGuizmo::ROTATE | ImGuizmo::TRANSLATE;
		ImGuizmo::Manipulate((float*)&vm, (float*)&pm, (ImGuizmo::OPERATION)getGType, ImGuizmo::LOCAL, (float*)&mat);

		glm::vec3 translation, scale;
		glm::quat rotation;
		glm::vec3 skew;
		glm::vec4 perspective;
		glm::decompose(mat, scale, ui_state.gizmoQuat, ui_state.gizmoCenter, skew, perspective);
		
		for (auto& st : obj_action_state)
		{
			auto nmat = mat * st.intermediate;
			glm::decompose(nmat, scale, st.obj->quaternion, st.obj->position, skew, perspective);
		}

		if (wstate.gizmo_realtime)
			ui_state.feedback_type = 2;

		// test ok is pressed.
		auto a = pm * vm * mat * glm::vec4(0, 0, 0, 1);
		glm::vec3 b = glm::vec3(a) / a.w;
		glm::vec2 c = glm::vec2(b);
		auto d = glm::vec2((c.x * 0.5f + 0.5f) * w + disp_area->Pos.x-16*camera->dpi, (-c.y * 0.5f + 0.5f) * h + disp_area->Pos.y + 50 * camera->dpi);
		ImGui::SetNextWindowPos(ImVec2(d.x, d.y), ImGuiCond_Always);
		ImGui::SetNextWindowViewport(viewport->ID);
		ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, ImGui::GetStyle().FrameRounding);
		ImGui::Begin("gizmo_checker", NULL, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoTitleBar);
		ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(0.0f, 1.0f, 0.0f, 1.0f));
		if (ImGui::Button("\uf00c"))
		{
			ui_state.feedback_type = 2;
			wstate.finished = true;
		}
		ImGui::PopStyleColor();
		ImGui::SameLine();
		ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(1.0f, 0.0f, 0.0f, 1.0f));
		if (ImGui::Button("\uf00d"))
		{
			ui_state.feedback_type = 0;
		}
		ImGui::PopStyleColor();
		ImGui::PopStyleVar();
		
		ImGui::End();
	}

    int guizmoSz = 80 * camera->dpi;
    auto viewManipulateRight = disp_area->Pos.x + w;
    auto viewManipulateTop = disp_area->Pos.y + h;
    auto viewMat = camera->GetViewMatrix();
    float* ptrView = &viewMat[0][0];
    ImGuizmo::ViewManipulate(ptrView, camera->distance, ImVec2(viewManipulateRight - guizmoSz - 25*camera->dpi, viewManipulateTop - guizmoSz - 16*camera->dpi), ImVec2(guizmoSz, guizmoSz), 0x00000000);

    glm::vec3 camDir = glm::vec3(viewMat[0][2], viewMat[1][2], viewMat[2][2]);
    glm::vec3 camUp = glm::vec3(viewMat[1][0], viewMat[1][1], viewMat[1][2]);

    auto alt = asin(camDir.z);
    auto azi = atan2(camDir.y, camDir.x);
    if (abs(alt - M_PI_2) < 0.1f || abs(alt + M_PI_2) < 0.1f)
        azi = (alt > 0 ? -1 : 1) * atan2(camUp.y, camUp.x);
    
    camera->Azimuth = azi;
    camera->Altitude = alt;
    camera->UpdatePosition();
	
	ImGui::SetNextWindowPos(ImVec2(disp_area->Pos.x + 16 * camera->dpi, disp_area->Pos.y +disp_area->Size.y - 16 * camera->dpi), ImGuiCond_Always, ImVec2(0, 1));
	ImGui::SetNextWindowViewport(viewport->ID);
	ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, ImGui::GetStyle().FrameRounding);
	auto color = ImGui::GetStyle().Colors[ImGuiCol_WindowBg]; color.w = 0.5f;
	ImGui::PushStyleColor(ImGuiCol_WindowBg, color);
	ImGui::Begin("cyclegui_stat", NULL, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoDocking);

	auto io = ImGui::GetIO();
	ImGui::Text("CycleGUI V0.1 FPS=%.0f", io.Framerate);

	if (ImGui::Button("\uf128"))
	{
		// nothing...
		// mouse left is reserved for tools, middle to rotate view, right to pan, wheel to zoom in/out, middle+right to free view, right+wheel to go up/down.
	}
	if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort))
		ImGui::SetTooltip("GUI-Help");
	ImGui::End();
	ImGui::PopStyleColor();
	ImGui::PopStyleVar();

	// workspace manipulations:
	ProcessWorkspaceFeedback();
}

void InitGL(int w, int h)
{
	auto io = ImGui::GetIO();
	io.ConfigInputTrickleEventQueue = false;
	io.ConfigDragClickToInputText = true;


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

	glGetError(); //clear errors.

	camera = new Camera(glm::vec3(0.0f, 0.0f, 0.0f), 10, w, h, 0.2);
	grid = new GroundGrid();

	init_sokol();

	GenPasses(w, h);
}


#define WSFeedInt32(x) { *(int*)pr=x; pr+=4;}
#define WSFeedFloat(x) { *(float*)pr=x; pr+=4;}
#define WSFeedDouble(x) { *(double*)pr=x; pr+=8;}
#define WSFeedBytes(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WSFeedString(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WSFeedBool(x) {*(bool*)pr=x; pr+=1;}

// should be called inside before draw.
unsigned char ws_feedback_buf[1024 * 1024];

void ProcessWorkspaceFeedback()
{
	if (ui_state.feedback_type == -1) // pending.
		return;

	auto pr = ws_feedback_buf;
	auto& wstate = ui_state.workspace_state.top();
	auto pid = wstate.id; // wstate pointer.

	WSFeedInt32(pid);
	if (ui_state.feedback_type == 0 ) // canceled.
	{
		WSFeedBool(false);

		// terminal.
		if (ui_state.workspace_state.size() > 1)
			ui_state.workspace_state.pop(); //after selecting, the 
	}
	else {
		WSFeedBool(true); // have feedback value now.

		if (ui_state.feedback_type == 1)
		{
			// selecting feedback.
			for (int i = 0; i < pointclouds.ls.size(); ++i)
			{
				auto t = std::get<0>(pointclouds.ls[i]);
				auto name = std::get<1>(pointclouds.ls[i]);
				if (t->flag & (1 << 6))
				{
					// selected as whole.
					WSFeedInt32(0);
					WSFeedString(name.c_str(), name.length());
				}
				if (t->flag & (1 << 9)) { // sub selected
					int sz = ceil(sqrt(t->capacity / 8));
					WSFeedInt32(1);
					WSFeedString(name.c_str(), name.length());
					WSFeedBytes(t->cpuSelection, sz * sz);
				}
			}

			for (int i = 0; i < gltf_classes.ls.size(); ++i)
			{
				auto cls = gltf_classes.get(i);
				auto objs = cls->objects;
				for (int j = 0; j < objs.ls.size(); ++j)
				{
					auto t = std::get<0>(objs.ls[i]);
					auto name = std::get<1>(objs.ls[i]);

					if (t->flags & (1 << 3))
					{
						// selected as whole.
						WSFeedInt32(0);
						WSFeedString(name.c_str(), name.length());
					}
					if (t->flags & (1 << 6))
					{
						WSFeedInt32(2);
						WSFeedString(name.c_str(), name.length());

						auto sz = int(ceil(cls->model.nodes.size() / 8.0f));
						std::vector<unsigned char> bits(sz);
						for (int z = 0; z < cls->model.nodes.size(); ++z)
							bits[z / 8] |= (((int(t->nodeattrs[z].flag) & (1 << 3)) != 0) << (z % 8));
						WSFeedBytes(bits.data(), sz);
						// // todo: problematic: could selected multiple sub, use "cpuSelection"
						// auto id = 0;
						// auto subname = cls->nodeId_name_map[id];
						// WSFeedString(subname.c_str(), subname.length());
					}
				}
			}
			WSFeedInt32(-1);

			// if (ui_state.workspace_state.size() > 1)
			// 	ui_state.workspace_state.pop(); //after selecting, the 
		}else if (ui_state.feedback_type == 2)
		{
			// transform feedback.
			WSFeedInt32(obj_action_state.size());
			WSFeedBool(wstate.finished);

			for (auto& oas : obj_action_state)
			{
				WSFeedString(oas.obj->name.c_str(), oas.obj->name.length());
				WSFeedFloat(oas.obj->position[0]);
				WSFeedFloat(oas.obj->position[1]);
				WSFeedFloat(oas.obj->position[2]);
				WSFeedFloat(oas.obj->quaternion[0]);
				WSFeedFloat(oas.obj->quaternion[1]);
				WSFeedFloat(oas.obj->quaternion[2]);
				WSFeedFloat(oas.obj->quaternion[3]);
			}

			if (wstate.finished)
			{
				// terminal.
				if (ui_state.workspace_state.size() > 1)
					ui_state.workspace_state.pop(); //after selecting, the 
			}
		}
	}

	// finalize:
	workspaceCallback(ws_feedback_buf, pr - ws_feedback_buf);

	ui_state.feedback_type = -1; // invalidate feedback.
}