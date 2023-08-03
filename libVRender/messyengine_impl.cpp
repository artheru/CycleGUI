#include "me_impl.h"

// ======== Sub implementations =========
#include "groundgrid.hpp"
#include "camera.hpp"
#include "init_impl.hpp"
#include "objects.hpp"
#include "skybox.hpp"

#include "interfaces.hpp"

void ClearSelection()
{
	ui_state.selected.clear();
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
			objs.get(j)->flags[0] &= ~(1 << 3);
			objs.get(j)->flags[0] |= (-1) << 8;
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


	if (ui_state.selecting)
	{
		if (wstate.selecting_mode == paint)
		{
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

	{
		// gltf transform to get mats.
		std::vector<int> renderings;
		int offset = 0;
		if (!gltf_classes.ls.empty()) {
			sg_begin_pass(graphics_state.instancing.pass, graphics_state.instancing.pass_action);
			sg_apply_pipeline(graphics_state.instancing.pip);

			gltf_displaying.flags.clear();
			gltf_displaying.shine_colors.clear();

			for (int i = 0; i < gltf_classes.ls.size(); ++i)
			{
				auto t = gltf_classes.get(i);
				renderings.push_back(offset);
				t->metainfo_offset = gltf_displaying.shine_colors.size();
				if (t->objects.ls.size() == 0) continue;
				offset += t->prepare(vm, offset, i);
			}
			if (offset > 0) {
				int objmetah = (int)(ceil(offset / 512));
				int size = 4096 * objmetah * 4;
				gltf_displaying.shine_colors.reserve(size);
				gltf_displaying.flags.reserve(size);

				{
					_sg_image_t* img = _sg_lookup_image(&_sg.pools, graphics_state.instancing.objShineIntensities.id);

					_sg_gl_cache_store_texture_binding(0);
					_sg_gl_cache_bind_texture(0, img->gl.target, img->gl.tex[img->cmn.active_slot]);
					GLenum gl_img_target = img->gl.target;
					glTexSubImage2D(gl_img_target, 0,
						0, 0,
						4096, objmetah,
						_sg_gl_teximage_format(SG_PIXELFORMAT_RGBA8), _sg_gl_teximage_type(SG_PIXELFORMAT_RGBA8),
						gltf_displaying.shine_colors.data());
					_sg_gl_cache_restore_texture_binding(0);
				}
				{
					_sg_image_t* img = _sg_lookup_image(&_sg.pools, graphics_state.instancing.objFlags.id);

					_sg_gl_cache_store_texture_binding(0);
					_sg_gl_cache_bind_texture(0, img->gl.target, img->gl.tex[img->cmn.active_slot]);
					GLenum gl_img_target = img->gl.target;
					glTexSubImage2D(gl_img_target, 0,
						0, 0,
						4096, objmetah,
						_sg_gl_teximage_format(SG_PIXELFORMAT_R32UI), _sg_gl_teximage_type(SG_PIXELFORMAT_R32UI),
						gltf_displaying.flags.data());
					_sg_gl_cache_restore_texture_binding(0);
				}
			}
			sg_end_pass();
		}

		// first draw point clouds, so edl only reference point's depth => pc_depth.
		sg_begin_pass(graphics_state.pc_primitive.pass, &graphics_state.pc_primitive.pass_action);
		sg_apply_pipeline(point_cloud_simple_pip);
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
				.hover_shine_color_intensity = ui_state.hover_shine,
				.selected_shine_color_intensity = ui_state.selected_shine,
			};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_draw(0, t->n, 1);
		}
		sg_end_pass();

		// --- edl lo-res pass
		sg_begin_pass(graphics_state.edl_lres.pass, &graphics_state.edl_lres.pass_action);
		sg_apply_pipeline(graphics_state.edl_lres_pip);
		sg_apply_bindings(graphics_state.edl_lres.bind);
		depth_blur_params_t edl_params{ .kernelSize = 7, .scale = 1, .pnear = cam_near, .pfar = cam_far };
		sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(edl_params));
		sg_draw(0, 4, 1);
		sg_end_pass();


		// actual gltf rendering.
		if (!gltf_classes.ls.empty()) {
			sg_begin_pass(graphics_state.primitives.pass, &graphics_state.primitives.pass_action);

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

		// shine-bloom.
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
		sg_apply_pipeline(graphics_state.composer.pip);
		sg_apply_bindings(graphics_state.composer.bind);

		static float facFac = 0.49, fac2Fac = 1.16, fac2WFac = 0.82, colorFac = 0.37, reverse1=0.581, reverse2=0.017, edrefl=0.27;
		ImGui::Checkbox("useEDL", &wstate.useEDL);
		ImGui::Checkbox("useSSAO", &wstate.useSSAO);
		ImGui::Checkbox("useGround", &wstate.useGround);
		int useFlag = (wstate.useEDL ? 1 : 0) | (wstate.useSSAO ? 2 : 0) | (wstate.useGround ? 4 : 0);
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
			.edrefl=edrefl,

			.useFlag = (float)useFlag
		};
		ImGui::DragFloat("fac2Fac", &fac2Fac, 0.01, 0, 2);
		ImGui::DragFloat("facFac", &facFac, 0.01, 0, 1);
		ImGui::DragFloat("fac2WFac", &fac2WFac, 0.01, 0, 1);
		ImGui::DragFloat("colofFac", &colorFac, 0.01, 0, 1);
		ImGui::DragFloat("reverse1", &reverse1, 0.001, 0, 1);
		ImGui::DragFloat("reverse2", &reverse2, 0.001, 0, 1);
		ImGui::DragFloat("refl", &edrefl, 0.0005, 0, 1);
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(wnd));
		sg_draw(0, 4, 1);

		// ui-composing. (border, shine, bloom)
		// shine-bloom
		sg_apply_pipeline(graphics_state.ui_composer.pip_blurYFin);
		sg_apply_bindings(sg_bindings{.vertex_buffers = {graphics_state.quad_vertices},.fs_images = {graphics_state.shine2}});
		sg_draw(0, 4, 1);


		// billboards
		
		// grid:
		grid->Draw(*camera);

		// border
		sg_apply_pipeline(graphics_state.ui_composer.pip_border);
		sg_apply_bindings(graphics_state.ui_composer.border_bind);
		auto composing = ui_composing_t{
			.draw_sel = use_paint_selection ? 1.0f : 0.0f,
			.border_colors = {ui_state.hover_border_color.x, ui_state.hover_border_color.y, ui_state.hover_border_color.z, ui_state.hover_border_color.w,
				ui_state.selected_border_color.x, ui_state.selected_border_color.y, ui_state.selected_border_color.z, ui_state.selected_border_color.w,
				ui_state.world_border_color.x, ui_state.world_border_color.y, ui_state.world_border_color.z, ui_state.world_border_color.w},
			//.border_size = 5,
		};
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_ui_composing, SG_RANGE(composing));
		sg_draw(0, 4, 1);

		// debug:
		// std::vector<sg_image> debugArr = {
		// 	graphics_state.primitives.color ,
		// 	//graphics_state.primitives.depth ,
		// 	//graphics_state.primitives.normal,
		// 	//graphics_state.edl_lres.color,
		// 	//graphics_state.ssao.image,
		// 	graphics_state.ssao.blur_image
		// };
		// sg_apply_pipeline(graphics_state.dbg.pip);
		// for (int i=0; i<debugArr.size(); ++i)
		// {
		// 	sg_apply_viewport((i/4)*160, (i%4)*120, 160, 120, false);
		// 	graphics_state.dbg.bind.fs_images[SLOT_tex] = debugArr[i];
		// 	sg_apply_bindings(&graphics_state.dbg.bind);
		// 	sg_draw(0, 4, 1);
		// }
		// sg_apply_viewport(0, 0, w, h, false);
	}
	sg_end_pass();

	sg_commit();

	// === hovering information === //todo: like click check 7*7 patch around the cursor.
	glm::vec4 hovering[1];
	me_getTexFloats(graphics_state.TCIN, hovering, ui_state.mouseX, h - ui_state.mouseY-1, 1, 1); // note: from left bottom corner...

	ui_state.hover_type = 0;

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
						t->flag |= (1 << 6);// select as a whole
						return true;
					}
					else if (t->flag & (1 << 8))
					{
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
				if (obj->flags[0] & (1 << 4))
				{
					obj->flags[0] |= (1 << 3);
					return true;
				}
				else if (obj->flags[0] & (1 << 5))
				{
					obj->flags[0] = (obj->flags[0] & 255) | (node_id << 8);
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

		// apply changes:

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
	}


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

	glGetError(); //clear errors.

	camera = new Camera(glm::vec3(0.0f, 0.0f, 0.0f), 10, w, h, 0.2);
	grid = new GroundGrid();

	init_sokol();

	GenPasses(w, h);
}

