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

#include "utilities.h"
#include "shaders/shaders.h"

bool TestSpriteUpdate();
bool ProcessWorkspaceFeedback();


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
		auto& objs = gltf_classes.get(i)->objects;
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

void process_argb_occurrence(const float* data, int ww, int hh)
{
	for (int i = 0; i < ww; ++i)
		for (int j = 0; j < hh; ++j)
		{
			auto nid = data[hh * i + j];
			if (!(0<=nid && nid < argb_store.rgbas.ls.size())) continue;
			argb_store.rgbas.get(nid)->occurrence += 1;
		}
}

void occurences_readout(int w, int h)
{
	struct reading
	{
		GLuint bufReadOccurence;
		GLsync sync;
		int w = -1, h = -1;
	};
	// read graphics_state.TCIN, graphics_state.sprite_render.occurences
	static GLuint readFbo = 0;
	static reading reads[2];
	if (readFbo == 0) {
		glGenFramebuffers(1, &readFbo);
	}
	auto readN = ui_state.frameCnt % 2;
	auto issueN = 1 - readN;

	//read:
	if (reads[readN].w != -1)
	{
		if (reads[readN].w == w && reads[readN].h == h)
		{
			//valid read:

			//argb occurences:
			glBindBuffer(GL_PIXEL_PACK_BUFFER, reads[readN].bufReadOccurence);
			int ww = ceil(reads[readN].w / 4.0);
			int hh = ceil(reads[readN].h / 4.0);
#ifndef __EMSCRIPTEN__
			auto buf = (const float*)glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, ww * hh * 4, GL_MAP_READ_BIT);
			// do operations.
			process_argb_occurrence(buf, ww, hh);
			glUnmapBuffer(GL_PIXEL_PACK_BUFFER);
#else
			GLenum waitReturn = glClientWaitSync(reads[readN].sync, GL_SYNC_FLUSH_COMMANDS_BIT, 0);
			glDeleteSync(reads[readN].sync);

			std::vector<float> buf(ww * hh * 4);
			glGetBufferSubData(GL_PIXEL_PACK_BUFFER, 0, ww * hh * sizeof(float), buf.data());
			// do operations
			process_argb_occurrence(buf.data(), ww, hh);
#endif
		}

		glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
		glDeleteBuffers(1, &reads[readN].bufReadOccurence);
	}

	//issue:
	_sg_image_t* o = _sg_lookup_image(&_sg.pools, graphics_state.sprite_render.occurences.id);
	auto tex_o = o->gl.tex[o->cmn.active_slot];
	// read argb occurences:
	reads[issueN].w = w;
	reads[issueN].h = h;
	glGenBuffers(1, &reads[issueN].bufReadOccurence);
	glBindBuffer(GL_PIXEL_PACK_BUFFER, reads[issueN].bufReadOccurence);
	int ww = ceil(w / 4.0);
	int hh = ceil(h / 4.0);
	glBufferData(GL_PIXEL_PACK_BUFFER, ww * hh * 4, nullptr, GL_STREAM_READ);
	glBindFramebuffer(GL_FRAMEBUFFER, readFbo);
	glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, tex_o, 0);
	glReadPixels(0, 0, ww, hh, GL_RED, GL_FLOAT, nullptr);

	//clean up
	glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
#ifdef __EMSCRIPTEN__
	reads[issueN].sync = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
	glFlush();
#endif
	glBindFramebuffer(GL_FRAMEBUFFER, 0);
}

static void me_getTexFloats(sg_image img_id, glm::vec4* pixels, int x, int y, int w, int h) {
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, img_id.id);
	SOKOL_ASSERT(img->gl.target == GL_TEXTURE_2D);
	SOKOL_ASSERT(0 != img->gl.tex[img->cmn.active_slot]);
	
	// GLuint oldFbo = 0;
	static GLuint readFbo = 0;
	if (readFbo == 0) {
		glGenFramebuffers(1, &readFbo);
	}
	//glGetIntegerv(GL_FRAMEBUFFER_BINDING, (GLint*)&oldFbo); just bind 0 is ok.
	glBindFramebuffer(GL_FRAMEBUFFER, readFbo);
	glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, img->gl.tex[img->cmn.active_slot], 0);
	glReadPixels(x, y, w, h, GL_RGBA, GL_FLOAT, pixels);
	glBindFramebuffer(GL_FRAMEBUFFER, 0);
	//sg_reset_state_cache();
	_SG_GL_CHECK_ERROR();
}

void GLAPIENTRY DebugCallback(GLenum source, GLenum type, GLuint id, GLenum severity, GLsizei length, const GLchar* message, const void* userParam) {
	std::cout << "OpenGL Debug Message: " << message << std::endl;
}

int lastW, lastH;

SSAOUniforms_t ssao_uniforms{
	.weight = 0.8f,
	.uSampleRadius = 5.0f,
	.uBias = 0,
	.uAttenuation = {1.32f,0.84f},
};


glm::vec3 world2screen(glm::vec3 input, glm::mat4 v, glm::mat4 p, glm::vec2 screenSize)
{
	glm::vec4 a = p * v * glm::vec4(input, 1.0f);
	glm::vec3 b = glm::vec3(a) / a.w;
	glm::vec2 c = glm::vec2(b);
	return glm::vec3((c.x * 0.5f + 0.5f) * screenSize.x, (c.y * 0.5f + 0.5f) * screenSize.y, a.w);
}

glm::vec2 world2pixel(glm::vec3 input, glm::mat4 v, glm::mat4 p, glm::vec2 screenSize)
{
	glm::vec4 a = p * v * glm::vec4(input, 1.0f);
	glm::vec3 b = glm::vec3(a) / a.w;
	glm::vec2 c = glm::vec2(b);
	return glm::vec2((c.x * 0.5f + 0.5f) * screenSize.x, screenSize.y-(c.y * 0.5f + 0.5f) * screenSize.y);
}


void process_remaining_touches()
{
	// touch X/Y should be pixel (for browser should be dpi scaled)
	static int touchState = 0;
	
	static float iTouchDist = -1;
	static float iX = 0, iY = 0;

	float touches[20];
	auto length = 0;

	ui_state.prevTouches.clear();
	for (int i = 0; i < ui_state.touches.size(); ++i){
		ui_state.prevTouches.insert(ui_state.touches[i].id);
		if (!ui_state.touches[i].consumed)
		{
			auto tX = ui_state.touches[i].touchX;
			auto tY = ui_state.touches[i].touchY;

			touches[length * 2] = tX;
			touches[length * 2 + 1] = tY;
			length += 1;
		}
	}

	// printf("process %d touches\n", length);
    ImGuiIO& io = ImGui::GetIO();

	static int idd = 0;
	// one finger.
	if (touchState == 0 && length==1)
	{
		// prepare
		io.AddMousePosEvent((float)touches[0], (float)touches[1]);
		cursor_position_callback(nullptr, touches[0], touches[1]);
		touchState = 1;
	}else if (touchState ==1 && length==1){
		// fire for move.
		io.AddMouseButtonEvent(0, true);
		mouse_button_callback(nullptr, 0, GLFW_PRESS, 0);
		touchState = 2;
	}else if (touchState ==1 && length ==0){
		// fire then release.
		io.AddMouseButtonEvent(0, true);
		mouse_button_callback(nullptr, 0, GLFW_PRESS, 0);
		io.AddMouseButtonEvent(0, false);
		mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);
		touchState = 0;
		// printf("clicked %d\n", idd++);
	}else if (touchState==2 && length ==1) {
		// only move now.
		io.AddMousePosEvent((float)touches[0], (float)touches[1]);
		cursor_position_callback(nullptr, touches[0], touches[1]);
	}else if (touchState ==2 && length==0)
	{
		// released.
		mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);
		io.AddMouseButtonEvent(0, false);
		// printf("released %d\n",idd++);
		touchState = 0;
	}

	// two fingers.
	else if ((touchState<=2 || touchState ==9 || touchState == 0) && length==2)
	{
		// it's right mouse.
		iTouchDist = sqrt((touches[0] - touches[2]) * (touches[0] - touches[2]) + (touches[1] - touches[3]) * (touches[1] - touches[3]));
		iX = (touches[0] + touches[2]) / 2;
		iY = (touches[1] + touches[3]) / 2;

		// release
		io.AddMouseButtonEvent(0, false);
		mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);

		// set position
		cursor_position_callback(nullptr, iX, iY);
		io.AddMousePosEvent(iX, iY);

		// press button.
		io.AddMouseButtonEvent(1, true);
		mouse_button_callback(nullptr, 1, GLFW_PRESS, 0);

		touchState = 3;
	}else if ((touchState ==3 || touchState==7) && length==2) // state 3: drag/zoom, state 7: pan-imgui.
	{
		auto wd = sqrt((touches[0] - touches[2]) * (touches[0] - touches[2]) + (touches[1] - touches[3]) * (touches[1] - touches[3]));
		auto offset = (wd - iTouchDist) / camera->dpi * 0.07;
		iTouchDist = wd;
		scroll_callback(nullptr, 0, offset);

		// right drag is not available for imgui.
		auto jX = (touches[0] + touches[2]) / 2;
		auto jY = (touches[1] + touches[3]) / 2;
		cursor_position_callback(nullptr, jX, jY);

		if (touchState == 3){
			if (abs(jX - iX) > 10 || abs(jY - iY) > 10){
				io.AddMouseButtonEvent(1, false); //release right.
				touchState = 7;
			}
		}else if (touchState==7)
		{
			io.AddMouseWheelEvent((float)(jX-iX)*0.04f, (float)(jY-iY)*0.04f);
			iX = jX;
			iY = jY;
		}

	}else if ((touchState ==3 || touchState==7) && length==0)
	{
		io.AddMouseButtonEvent(1, false);
		mouse_button_callback(nullptr, 1, GLFW_RELEASE, 0);
		touchState = 0;
	}

	// three fingers.
	else if (touchState<=3 && length==3)
	{
		// it's middle mouse.
		auto jX = (touches[0] + touches[2] + touches[4]) / 3;
		auto jY = (touches[1] + touches[3] + touches[5]) / 3;

		// release buttons.
		io.AddMouseButtonEvent(0, false);
		mouse_button_callback(nullptr, 0, GLFW_RELEASE, 0);
		io.AddMouseButtonEvent(1, false);
		mouse_button_callback(nullptr, 1, GLFW_RELEASE, 0);

		// set position
		cursor_position_callback(nullptr, jX, jY);
		io.AddMousePosEvent(jX, jY);

		// press button.
		io.AddMouseButtonEvent(2, true);
		mouse_button_callback(nullptr, 2, GLFW_PRESS, 0);

		touchState = 5;
	}else if (touchState==5 && length==3)
	{
		auto jX = (touches[0] + touches[2] + touches[4]) / 3;
		auto jY = (touches[1] + touches[3] + touches[5]) / 3;
		io.AddMousePosEvent(jX, jY);
		cursor_position_callback(nullptr, jX, jY);
	}else if (touchState ==5 && length==0)
	{
		// on low-end device, a "touch miss" could happen. we only reset state when all touches are out.
		io.AddMouseButtonEvent(2, false);
		mouse_button_callback(nullptr, 2, GLFW_RELEASE, 0);
		touchState = 0;
	}
}

void DrawWorkspace(int w, int h, ImGuiDockNode* disp_area, ImDrawList* dl, ImGuiViewport* viewport);

// only on displaying.
void DrawWorkspace(int w, int h)
{
	ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
	auto vp = ImGui::GetMainViewport();
	auto dl = ImGui::GetBackgroundDrawList(vp);
	if (node) {
		auto central = ImGui::DockNodeGetRootNode(node)->CentralNode;
		DrawWorkspace(central->Size.x, central->Size.y, central, dl, vp);
		ui_state.frameCnt += 1;
	}
	ui_state.loopCnt += 1;
	process_remaining_touches();
}
void ProcessBackgroundWorkspace()
{
	// gesture could listen to keyboard/joystick. process it.
	auto& wstate = ui_state.workspace_state.top();
	if (gesture_operation* op = dynamic_cast<gesture_operation*>(wstate.operation); op != nullptr)
		op->manipulate(nullptr, nullptr);
	ui_state.loopCnt += 1;
	ProcessWorkspaceFeedback();
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

void get_viewed_sprites(int w, int h)
{
	// Operations that requires read from rendered frame, slow... do them after all render have safely done.
	// === what rgb been viewed? how much pix?
	if (ui_state.frameCnt > 60)
	{
		for (int i = 0; i < argb_store.rgbas.ls.size(); ++i)
			argb_store.rgbas.get(i)->occurrence = 0;

		occurences_readout(w, h);

		for (int i = 0; i < argb_store.rgbas.ls.size(); ++i)
		{
			auto rgba_ptr = argb_store.rgbas.get(i);
			if (rgba_ptr->streaming && rgba_ptr->atlasId!=-1)
			{
				auto ptr = GetStreamingBuffer(argb_store.rgbas.getName(i), rgba_ptr->width * rgba_ptr->height * 4);
				me_update_rgba_atlas(argb_store.atlas, rgba_ptr->atlasId,
					(int)(rgba_ptr->uvStart.x), (int)(rgba_ptr->uvEnd.y), rgba_ptr->height, rgba_ptr->width, ptr
					, SG_PIXELFORMAT_RGBA8);
				//printf("streaming first argb=(%x%x%x%x)\n", ptr[0], ptr[1], ptr[2], ptr[3]);
				rgba_ptr->loaded = true;
			}
		}
		// printf("\n");
	}
}

void camera_manip()
{
	// === camera manipulation ===
	if (ui_state.refreshStare) {
		ui_state.refreshStare = false;

		if (abs(camera->position.z - camera->stare.z) > 0.001) {
			glm::vec4 starepnt;
			me_getTexFloats(graphics_state.primitives.depth, &starepnt, lastW / 2, lastH / 2, 1, 1); // note: from left bottom corner...

			auto d = starepnt.x;
			if (d < 0) d = -d;
			if (d < 0.5) d += 0.5;
			float ndc = d * 2.0 - 1.0;
			float z = (2.0 * cam_near * cam_far) / (cam_far + cam_near - ndc * (cam_far - cam_near)); // pointing mesh's depth.
			//printf("d=%f, z=%f\n", d, z);
			//calculate ground depth.
			float gz = camera->position.z / (camera->position.z - camera->stare.z) * glm::distance(camera->position, camera->stare);
			if (gz > 0) {
				if (z < gz)
				{
					// set stare to mesh point.
					camera->stare = glm::normalize(camera->stare - camera->position) * z + camera->position;
					camera->distance = z;
				}
				else
				{
					camera->stare = glm::normalize(camera->stare - camera->position) * gz + camera->position;
					camera->distance = gz;
					camera->stare.z = 0;
				}
			}
		}
	}
}

void process_hoverNselection(int w, int h)
{
	// === hovering information === //todo: like click check 7*7 patch around the cursor.
	auto& wstate = ui_state.workspace_state.top();
	std::vector<glm::vec4> hovering(49);
	int order[] = {
		24, 25, 32, 31, 30, 23, 16, 17, 18, 19, 26, 33, 40, 39, 38, 37, 36, 29, 22, 15, 8, 9, 10, 11, 12, 13, 20, 27,
		34, 41, 48, 47, 46, 45, 44, 43, 42, 35, 28, 21, 14, 7, 0, 1, 2, 3, 4, 5, 6
	};

	me_getTexFloats(graphics_state.TCIN, hovering.data(), ui_state.mouseX - 3, h - (ui_state.mouseY + 3), 7, 7);
	// note: from left bottom corner...

	ui_state.hover_type = 0;

	std::string mousePointingType = "/", mousePointingInstance = "/";
	int mousePointingSubId = -1;
	for (int i = 0; i < 49; ++i)
	{
		auto h = hovering[order[i]];

		if (h.x == 1)
		{
			int pcid = h.y;
			int pid = int(h.z) * 16777216 + (int)h.w;
			mousePointingType = "point_cloud";
			mousePointingInstance = std::get<1>(pointclouds.ls[pcid]);
			mousePointingSubId = pid;

			if (wstate.hoverables.find(mousePointingInstance) != wstate.hoverables.end() || wstate.sub_hoverables.
				find(mousePointingInstance) != wstate.sub_hoverables.end())
			{
				ui_state.hover_type = 1;
				ui_state.hover_instance_id = pcid;
				ui_state.hover_node_id = pid;
			}
			continue;
		}
		else if (h.x > 999)
		{
			int class_id = int(h.x) - 1000;
			int instance_id = int(h.y) * 16777216 + (int)h.z;
			int node_id = int(h.w);
			mousePointingType = std::get<1>(gltf_classes.ls[class_id]);
			mousePointingInstance = *gltf_classes.get(class_id)->showing_objects_name[instance_id];// std::get<1>(gltf_classes.get(class_id)->objects.ls[instance_id]);
			mousePointingSubId = node_id;

			if (wstate.hoverables.find(mousePointingInstance) != wstate.hoverables.end())
			{
				ui_state.hover_type = class_id + 1000;
				ui_state.hover_instance_id = instance_id;
				ui_state.hover_node_id = -1;
			}
			if (wstate.sub_hoverables.find(mousePointingInstance) != wstate.sub_hoverables.end())
			{
				ui_state.hover_type = class_id + 1000;
				ui_state.hover_instance_id = instance_id;
				ui_state.hover_node_id = node_id;
			}
			continue;
		}
		else if (h.x == 2)
		{
			// bunch of lines.
			int bid = h.y;
			int lid = h.z;
			if (bid >= 0)
			{
				mousePointingType = "bunch";
				mousePointingInstance = std::get<1>(line_bunches.ls[bid]);
				mousePointingSubId = lid;
			}
			else
			{
				mousePointingType = "line_piece";
				mousePointingInstance = std::get<1>(line_pieces.ls[lid]);
				mousePointingSubId = -1;
			}

			if (wstate.hoverables.find(mousePointingInstance) != wstate.hoverables.end() || wstate.sub_hoverables.
				find(mousePointingInstance) != wstate.sub_hoverables.end())
			{
				ui_state.hover_type = 2;
				ui_state.hover_instance_id = bid;
				ui_state.hover_node_id = lid;
			}
			continue;
		}
		else if (h.x == 3)
		{
			// image sprite.
			int sid = h.y;
			mousePointingType = "sprite";
			mousePointingInstance = std::get<1>(sprites.ls[sid]);
			mousePointingSubId = -1;

			if (wstate.hoverables.find(mousePointingInstance) != wstate.hoverables.end() || wstate.sub_hoverables.
				find(mousePointingInstance) != wstate.sub_hoverables.end())
			{
				ui_state.hover_type = 3;
				ui_state.hover_instance_id = sid;
				ui_state.hover_node_id = -1;
			}
			continue;
		}
	}


	if (ui_state.displayRenderDebug)
	{
		ImGui::Text("pointing:%s>%s.%d", mousePointingType.c_str(), mousePointingInstance.c_str(), mousePointingSubId);
	}


	// ==== UI State: Selecting ==========
	
    if (select_operation* sel_op = dynamic_cast<select_operation*>(wstate.operation); sel_op != nullptr)
    {
		if (sel_op->extract_selection)
		{
			sel_op->extract_selection = false;

			auto test = [](glm::vec4 pix) -> bool
			{
				if (pix.x == 1)
				{
					int pcid = pix.y;
					int pid = int(pix.z) * 16777216 + (int)pix.w;
					auto t = pointclouds.get(pcid);
					if (t->flag & (1 << 4))
					{
						// select by point.
						if ((t->flag & (1 << 7)))
						{
							t->flag |= (1 << 6); // selected as a whole
							return true;
						}
						else if (t->flag & (1 << 8))
						{
							t->flag |= (1 << 9); // sub-selected
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
					auto obj = t->showing_objects[instance_id];// t->objects.get(instance_id);
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

			if (sel_op->selecting_mode == click)
			{
				for (int i = 0; i < 49; ++i)
				{
					if (test(hovering[order[i]])) break;
				}
			}
			else if (sel_op->selecting_mode == drag)
			{
				hovering.resize(w * h);
				auto stx = std::min(ui_state.mouseX, sel_op->select_start_x);
				auto sty = std::max(ui_state.mouseY, sel_op->select_start_y);
				auto sw = std::abs(ui_state.mouseX - sel_op->select_start_x);
				auto sh = std::abs(ui_state.mouseY - sel_op->select_start_y);
				// todo: fetch full screen TCIN.
				me_getTexFloats(graphics_state.TCIN, hovering.data(), stx, h - sty, sw, sh);
				// note: from left bottom corner...
				for (int i = 0; i < sw * sh; ++i)
					test(hovering[i]);
			}
			else if (sel_op->selecting_mode == paint)
			{
				hovering.resize(w * h);
				// todo: fetch full screen TCIN.
				me_getTexFloats(graphics_state.TCIN, hovering.data(), 0, 0, w, h);
				for (int j = 0; j < h; ++j)
					for (int i = 0; i < w; ++i)
						if (sel_op->painter_data[(j / 4) * (w / 4) + (i / 4)] > 0)
							test(hovering[(h - j - 1) * w + i]);
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
						                .subimage = {{{t->cpuSelection, (size_t)(sz * sz)}}}
					                }); //neither selecting item.
				}
			}

			wstate.feedback = feedback_continued;
		}
	}
}

// #define TOC(X) span= std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic).count(); \
// 	ImGui::Text("tic %s=%.2fms, total=%.1fms",X,span*0.001,((float)std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::high_resolution_clock::now() - tic_st).count())*0.001);\
// 	tic=std::chrono::high_resolution_clock::now();
#define TOC(X) ;

void DrawWorkspace(int w, int h, ImGuiDockNode* disp_area, ImDrawList* dl, ImGuiViewport* viewport)
{
	auto tic=std::chrono::high_resolution_clock::now();
	auto tic_st = tic;
	int span;

	auto& wstate = ui_state.workspace_state.top();

	// actually all the pixels are already ready by this point, but we move sprite occurences to the end for webgl performance.
	camera_manip();
	TOC("mani")
	process_hoverNselection(lastW, lastH);
	TOC("hvn")

	// draw
	camera->Resize(w, h);
	camera->UpdatePosition();

	auto vm = camera->GetViewMatrix();
	auto pm = camera->GetProjectionMatrix();
	auto invVm = glm::inverse(vm);
	auto invPm = glm::inverse(pm);

	auto pv = pm * vm;

	if (lastW!=w ||lastH!=h)
	{
		ResetEDLPass();
		GenPasses(w, h);

		if (select_operation* sel_op = dynamic_cast<select_operation*>(wstate.operation); sel_op != nullptr){
			sel_op->painter_data.resize(w * h);
			std::fill(sel_op->painter_data.begin(), sel_op->painter_data.end(), 0);
		}
	}
	ui_state.workspace_w=lastW = w;
	ui_state.workspace_h=lastH = h;
	
	TOC("resz")

	auto use_paint_selection = false;
	int useFlag = (wstate.useEDL ? 1 : 0) | (wstate.useSSAO ? 2 : 0) | (wstate.useGround ? 4 : 0);

	for (int i = 0; i < global_name_map.ls.size(); ++i)
		global_name_map.get(i)->obj->compute_pose();

	// process gestures.
	if (gesture_operation* op = dynamic_cast<gesture_operation*>(wstate.operation); op != nullptr)
		op->manipulate(disp_area, dl);

	// draw spot texts:
	for (int i = 0; i < spot_texts.ls.size(); ++i)
	{
		auto t = spot_texts.get(i);
		if (!t->show) continue;
		for (int j=0; j<t->texts.size(); ++j)
		{
			auto& ss = t->texts[j];
			glm::vec2 pos(0,0);
			if (ss.header & (1<<0)){
				if (ss.header & (1 << 4))
					pos = world2pixel(ss.relative->current_pos + ss.position, vm, pm, glm::vec2(w, h));
				else
					pos = world2pixel(ss.position, vm, pm, glm::vec2(w, h));
			}else if (ss.header & (1 << 4))
			{
				pos=world2pixel(ss.relative->current_pos, vm, pm, glm::vec2(w, h));
			}

			// screen coord from top-left to bottom-right.
			if (ss.header & (1<<1)){
				pos.x += ss.ndc_offset.x*disp_area->Size.x; //uv_offset.
				pos.y += ss.ndc_offset.y*disp_area->Size.y;
			}
			if (ss.header & (1<<2))
			{
				pos += ss.pixel_offset * camera->dpi;
			}
			if (ss.header & (1<<3))
			{
				auto sz = ImGui::CalcTextSize(ss.text.c_str());
				pos.x -= sz.x * ss.pivot.x;
				pos.y -= sz.y * ss.pivot.y;
			}
			dl->AddText(ImVec2(disp_area->Pos.x + pos.x, disp_area->Pos.y + pos.y), t->texts[j].color, t->texts[j].text.c_str());
		}
	}


	
	if (select_operation* sel_op = dynamic_cast<select_operation*>(wstate.operation); sel_op != nullptr){
		auto radius = sel_op->paint_selecting_radius;
		if (sel_op->selecting_mode == paint && !sel_op->selecting)
		{
			auto pos = disp_area->Pos;
			dl->AddCircle(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), radius, 0xff0000ff);
		}
		if (sel_op->selecting)
		{
			if (sel_op->selecting_mode == drag)
			{
				auto pos = disp_area->Pos;
				auto st = ImVec2(std::min(ui_state.mouseX, sel_op->select_start_x) + pos.x, std::min(ui_state.mouseY, sel_op->select_start_y) + pos.y);
				auto ed = ImVec2(std::max(ui_state.mouseX, sel_op->select_start_x) + pos.x, std::max(ui_state.mouseY, sel_op->select_start_y) + pos.y);
				dl->AddRectFilled(st, ed, 0x440000ff);
				dl->AddRect(st, ed, 0xff0000ff);
			}
			else if (sel_op->selecting_mode == paint)
			{
				auto pos = disp_area->Pos;
				dl->AddCircleFilled(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), radius, 0x440000ff);
				dl->AddCircle(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), radius, 0xff0000ff);

				// draw_image.
				for (int j = (ui_state.mouseY - radius)/4; j <= (ui_state.mouseY + radius)/4+1; ++j)
					for (int i = (ui_state.mouseX - radius)/4; i <= (ui_state.mouseX + radius)/4+1; ++i)
					{
						if (0 <= i && i < w/4 && 0 <= j && j < h/4 && 
							sqrtf((i*4 - ui_state.mouseX) * (i*4 - ui_state.mouseX) + (j*4 - ui_state.mouseY) * (j*4 - ui_state.mouseY)) < radius)
						{
							sel_op->painter_data[j * (w/4) + i] = 255;
						}
					} 

				//update texture;
				sg_update_image(graphics_state.ui_selection, sg_image_data{
						.subimage = {{ {sel_op->painter_data.data(), (size_t)((w/4) * (h / 4))} }}
					});
				use_paint_selection = true;
			}
		}
	}

	prepare_flags();
	sg_reset_state_cache(); 

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
	
	TOC("pre-draw")


	int instance_count=0, node_count = 0;
	if (draw_3d){
		// gltf transform to get mats.
		std::vector<int> renderings;

		if (!gltf_classes.ls.empty()) {

			for (int i = 0; i < gltf_classes.ls.size(); ++i)
			{
				auto t = gltf_classes.get(i);
				t->instance_offset = instance_count;
				instance_count += t->list_objects();
				renderings.push_back(node_count);
				node_count += t->count_nodes();
			}
			
			TOC("cnt")
			if (node_count != 0) {

				int objmetah1 = (int)(ceil(node_count / 2048.0f)); //4096 width, stride 2 per node.
				int size1 = 4096 * objmetah1 * 32; //RGBA32F*2, 8floats=32B sizeof(s_transrot)=32.
				std::vector<s_pernode> per_node_meta(size1);

				int objmetah2 = (int)(ceil(instance_count / 4096.0f)); // stride 1 per instance.
				int size2 = 4096 * objmetah2 * 16; //4 uint: animationid|start time.
				std::vector<s_perobj> per_object_meta(size2);
				for (int i = 0; i < gltf_classes.ls.size(); ++i)
				{
					auto t = gltf_classes.get(i);
					if (t->showing_objects.empty()) continue;
					t->prepare_data(per_node_meta, per_object_meta, renderings[i], t->instance_offset);
				}
				
				TOC("prepare")
				// update really a lot of data...
				{
					updateTextureW4K(graphics_state.instancing.node_meta, objmetah1, per_node_meta.data(), SG_PIXELFORMAT_RGBA32F);
					updateTextureW4K(graphics_state.instancing.instance_meta, objmetah2, per_object_meta.data(), SG_PIXELFORMAT_RGBA32SI);
				}
				TOC("ud")

				//███ Compute node localmat: Translation Rotation on instance, node and Animation, also perform depth 4 instancing.
				sg_begin_pass(graphics_state.instancing.animation_pass, graphics_state.instancing.pass_action);
				sg_apply_pipeline(graphics_state.instancing.animation_pip);
				for (int i = 0; i < gltf_classes.ls.size(); ++i)
				{
					auto t = gltf_classes.get(i);
					if (t->showing_objects.empty()) continue;
					t->compute_node_localmat(vm, renderings[i]); // also multiplies view matrix.
				}
				sg_end_pass();

				//███ Propagate node hierarchy, we propagate 2 times in a group, one time at most depth 4.
				// sum{n=1~l}{4^n*C_l^n} => 4, 24|, 124, 624|.
				for (int i = 0; i < int(ceil(gltf_class::max_passes / 2.0f)); ++i) {
					sg_begin_pass(graphics_state.instancing.hierarchy_pass1, graphics_state.instancing.hierarchy_pass_action);
					sg_apply_pipeline(graphics_state.instancing.hierarchy_pip);
					for (int i = 0; i < gltf_classes.ls.size(); ++i)
					{
						auto t = gltf_classes.get(i);
						if (t->showing_objects.empty()) continue;
						t->node_hierarchy(renderings[i], i * 2);
					}
					sg_end_pass();

					sg_begin_pass(graphics_state.instancing.hierarchy_pass2, graphics_state.instancing.hierarchy_pass_action);
					sg_apply_pipeline(graphics_state.instancing.hierarchy_pip);
					for (int i = 0; i < gltf_classes.ls.size(); ++i)
					{
						auto t = gltf_classes.get(i);
						if (t->showing_objects.empty()) continue;
						t->node_hierarchy(renderings[i], i * 2 + 1);
					}
					sg_end_pass();
				}
				TOC("propagate")
				
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
		TOC("hierarchy")

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
			vs_params_t vs_params{ .mvp = pv * translate(glm::mat4(1.0f), t->current_pos) * mat4_cast(t->current_rot) , .dpi = camera->dpi , .pc_id = i,
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
		
		TOC("ptc")

		// actual gltf rendering.
		// todo: just use one call to rule all rendering.
		if (node_count!=0) {
			sg_begin_pass(graphics_state.primitives.pass, &graphics_state.primitives.pass_action);

			if (wstate.useCrossSection)
				_sg_lookup_pipeline(&_sg.pools, graphics_state.gltf_pip.id)->gl.cull_mode = SG_CULLMODE_NONE;
			else
				_sg_lookup_pipeline(&_sg.pools, graphics_state.gltf_pip.id)->gl.cull_mode = SG_CULLMODE_BACK;

			sg_apply_pipeline(graphics_state.gltf_pip);

			for (int i = 0; i < gltf_classes.ls.size(); ++i) {
				auto t = gltf_classes.get(i);
				if (t->showing_objects.empty()) continue;
				t->render(vm, pm, false, renderings[i], i);
			}

			sg_end_pass();
		}
		
		TOC("gltf")
		
		// draw geometries.
		// for (int i=0; i<geometries.ls.size(); ++i)
		// {
		// 	auto t = geometries.get(i);
		// 	t->render(vm, pm, false, 0, 0);
		// }

		// draw lines
		// draw temp lines, each temp line bunch as an object.
		auto lbinited = false;
		for (int i=0; i<line_bunches.ls.size(); ++i)
		{
			auto bunch = line_bunches.get(i);
			if (bunch->n>0)
			{
				if (!lbinited)
				{
					sg_begin_pass(graphics_state.line_bunch.pass, &graphics_state.line_bunch.pass_action);
					sg_apply_pipeline(graphics_state.line_bunch.line_bunch_pip);
					lbinited = true;
				}

				sg_apply_bindings(sg_bindings{ .vertex_buffers = {bunch->line_buf}, .fs_images = {} });
				line_bunch_params_t lb{
					.mvp = pv * translate(glm::mat4(1.0f), bunch->current_pos) * mat4_cast(bunch->current_rot),
					.dpi = camera->dpi, .bunch_id = i,
					.screenW = (float)lastW,
					.screenH = (float)lastH,
					.displaying = 0,
					//.hovering_pcid = hovering_pcid,
					//.shine_color_intensity = bunch->shine_color,
					.hover_shine_color_intensity = wstate.hover_shine,
					.selected_shine_color_intensity = wstate.selected_shine,
				};
				sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_line_bunch_params, SG_RANGE(lb));
				sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_line_bunch_params, SG_RANGE(lb));

				sg_draw(0, 9, bunch->n);
			}
		}

		if (line_pieces.ls.size() > 0)
		{
			// draw like line bunch for all lines.
			if (!lbinited)
			{
				sg_begin_pass(graphics_state.line_bunch.pass, &graphics_state.line_bunch.pass_action);
				sg_apply_pipeline(graphics_state.line_bunch.line_bunch_pip);
				lbinited = true;
			}
			std::vector<gpu_line_info> info(line_pieces.ls.size());

			for (int i=0; i<line_pieces.ls.size(); ++i)
			{
				auto t = line_pieces.get(i);
				if (t->propSt != nullptr)
					t->attrs.st = t->propSt->current_pos;
				if (t->propEnd != nullptr) 
					t->attrs.end = t->propEnd->current_pos;
				info[i] = line_pieces.get(i)->attrs;
			}
			auto sz = line_pieces.ls.size() * sizeof(gpu_line_info);
			auto buf = sg_make_buffer(sg_buffer_desc{ .size = sz, .data = {info.data(), sz} });
			sg_apply_bindings(sg_bindings{ .vertex_buffers = {buf}, .fs_images = {} });

			line_bunch_params_t lb{
				.mvp = pv,
				.dpi = camera->dpi, .bunch_id = -1,
				.screenW = (float)lastW,
				.screenH = (float)lastH,
				.displaying = 0,
				//.hovering_pcid = hovering_pcid,
				//.shine_color_intensity = bunch->shine_color,
				.hover_shine_color_intensity = wstate.hover_shine,
				.selected_shine_color_intensity = wstate.selected_shine,
			};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_line_bunch_params, SG_RANGE(lb));
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_line_bunch_params, SG_RANGE(lb));
			sg_draw(0, 9, line_pieces.ls.size());
			sg_destroy_buffer(buf);
		}

		if (lbinited)
		{
			sg_end_pass();
		}
		
		TOC("pieces")
		
		// auto gp_sz = std::max(1, (int)(ceil(sqrt(global_name_map.ls.size())))); // prevent of null image.
		// std::vector<glm::vec4> global_positions(gp_sz* gp_sz);
		// for (int i = 0; i < global_name_map.ls.size();++i)
		// 	global_positions[i] = glm::vec4(global_name_map.get(i)->obj->position, 0);
		// auto pos_texture = sg_make_image(sg_image_desc{
		// 	.width = gp_sz,
		// 	.height = gp_sz,
		// 	.pixel_format = SG_PIXELFORMAT_RGBA32F,
		// 	.data = {.subimage = {{ {
		// 		.ptr = global_positions.data(),  // Your mat4 data here
		// 		.size = gp_sz*gp_sz * sizeof(glm::vec4)
		// 	}}}}
		// });
		

		// sg_destroy_image(pos_texture);

		// draw sprites: must be the last (or depth will bad).
		std::vector<gpu_sprite> sprite_params;
		sprite_params.reserve(sprites.ls.size());
		for(int i=0; i<sprites.ls.size(); ++i)
		{
			auto s = sprites.get(i);
			//auto displayType = s->flags >> 6;
			sprite_params.push_back(gpu_sprite{
				.translation= s->current_pos,
				.flag = (float)(s->flags | (s->rgba->loaded?(1<<5):0)),
				.quaternion=s->current_rot,
				.dispWH=s->dispWH,
				.uvLeftTop = s->rgba->uvStart,
				.RightBottom = s->rgba->uvEnd,
				.myshine = s->shineColor,
				.rgbid = (float)((s->rgba->instance_id<<4) |(s->rgba->atlasId & 0xf) )
			});
		}
		if (sprite_params.size()>0)
		{   //todo:....
			sg_begin_pass(graphics_state.sprite_render.pass, &graphics_state.sprite_render.pass_action);
			sg_apply_pipeline(graphics_state.sprite_render.quad_image_pip);
			u_quadim_t quadim{
				.pvm = pv,
				.screenWH = glm::vec2(w,h),
				.hover_shine_color_intensity = wstate.hover_shine,
				.selected_shine_color_intensity = wstate.selected_shine,
				.time = (float)ui_state.getMsFromStart()
			};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_u_quadim, SG_RANGE(quadim));
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_u_quadim, SG_RANGE(quadim));

			auto sz = sprite_params.size() * sizeof(gpu_sprite);
			auto buf = sg_make_buffer(sg_buffer_desc{ .size = sz, .data = {sprite_params.data(), sz} });
			sg_bindings sb = { .vertex_buffers = {buf}, .fs_images = {argb_store.atlas} };
			sg_apply_bindings(sb);
			sg_draw(0, 6, sprite_params.size());
			sg_destroy_buffer(buf);

			sg_end_pass();

			///
			sg_begin_pass(graphics_state.sprite_render.stat_pass, graphics_state.sprite_render.stat_pass_action);
			sg_apply_pipeline(graphics_state.sprite_render.stat_pip);
			sg_apply_bindings(sg_bindings{.vertex_buffers = {graphics_state.uv_vertices}, .fs_images = {graphics_state.sprite_render.viewed_rgb}});
			sg_draw(0, 4, 1);
			sg_end_pass();
		}
		
		TOC("sprites")
		// === post processing ===
		// ---ssao---
		if (wstate.useSSAO) {
			ssao_uniforms.P = pm;
			ssao_uniforms.iP = glm::inverse(pm);
			// ssao_uniforms.V = vm;
			ssao_uniforms.iV = glm::inverse(vm);
			ssao_uniforms.cP = camera->position;
			ssao_uniforms.uDepthRange[0] = cam_near;
			ssao_uniforms.uDepthRange[1] = cam_far;
			ssao_uniforms.time = (float)ui_state.getMsFromStart();
			ssao_uniforms.useFlag = useFlag;

			if (ui_state.displayRenderDebug){
				ImGui::DragFloat("uSampleRadius", &ssao_uniforms.uSampleRadius, 0.1, 0, 100);
				ImGui::DragFloat("uBias", &ssao_uniforms.uBias, 0.003, -0.5, 0.5);
				ImGui::DragFloat2("uAttenuation", ssao_uniforms.uAttenuation, 0.01, -10, 10);
				ImGui::DragFloat("weight", &ssao_uniforms.weight, 0.1, -10, 10);
				ImGui::DragFloat2("uDepthRange", ssao_uniforms.uDepthRange, 0.05, 0, 100);
			}

			sg_begin_pass(graphics_state.ssao.pass, &graphics_state.ssao.pass_action);
			sg_apply_pipeline(graphics_state.ssao.pip);
			sg_apply_bindings(graphics_state.ssao.bindings);
			// sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(ssao_uniforms));
			sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(ssao_uniforms));
			sg_draw(0, 4, 1);
			sg_end_pass();

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
				.fs_images = {graphics_state.bloom}
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
		TOC("post")
	}
		
	//

	static float facFac = 0.49, fac2Fac = 1.16, fac2WFac = 0.82, colorFac = 0.37, reverse1 = 0.581, reverse2 = 0.017, edrefl = 0.27;
	
	TOC("3d-draw")
		
		// ground reflection.
	if (wstate.useGround) {
		sg_begin_pass(graphics_state.ground_effect.pass, graphics_state.ground_effect.pass_action);

		// below to be revised
		// std::vector<glm::vec3> ground_instances;
		// for (int i = 0; i < gltf_classes.ls.size(); ++i) {
		// 	auto c = gltf_classes.get(i);
		// 	auto t = c->objects;
		// 	for (int j = 0; j < t.ls.size(); ++j){
		// 		auto& pos = t.get(j)->current_pos;
		// 		ground_instances.emplace_back(pos.x, pos.y, c->sceneDim.radius);
		// 	}
		// }
		// if (!ground_instances.empty()) {
		// 	sg_apply_pipeline(graphics_state.ground_effect.spotlight_pip);
		// 	graphics_state.ground_effect.spotlight_bind.vertex_buffers[1] = sg_make_buffer(sg_buffer_desc{
		// 		.data = {ground_instances.data(), ground_instances.size() * sizeof(glm::vec3)}
		// 		});
		// 	sg_apply_bindings(graphics_state.ground_effect.spotlight_bind);
		// 	gltf_ground_mats_t u{ pm * vm, camera->position };
		// 	sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(u));
		// 	sg_draw(0, 6, ground_instances.size());
		// 	sg_destroy_buffer(graphics_state.ground_effect.spotlight_bind.vertex_buffers[1]);
		// }
	
		sg_apply_pipeline(graphics_state.ground_effect.cs_ssr_pip);
		sg_apply_bindings(graphics_state.ground_effect.bind);
		auto ug = uground_t{
			.w = float(w), .h = float(h), .pnear = cam_near, .pfar = cam_far,
			.ipmat = glm::inverse(pm),
			.ivmat = glm::inverse(vm),
			.pmat = pm,
			.pv = pv,
			.campos = camera->position,
			.lookdir = glm::normalize(camera->stare - camera->position),
			.time = (float)ui_state.getMsFromStart()
		};
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(ug));
		sg_draw(0, 4, 1);

		sg_end_pass();
	}

	sg_begin_default_pass(&graphics_state.default_passAction, viewport->Size.x, viewport->Size.y);
	// sg_begin_default_pass(&graphics_state.default_passAction, viewport->Size.x, viewport->Size.y);
	{

		sg_apply_viewport(disp_area->Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area->Pos.y-viewport->Pos.y + h), w, disp_area->Size.y, false);
		sg_apply_scissor_rect(0, 0, viewport->Size.x, viewport->Size.y, false);

		// sky quad:
		// todo: customizable like shadertoy.
		_draw_skybox(vm, pm);

		// todo: revise this.
		std::vector<glm::vec3> ground_instances;
		for (int i = 0; i < gltf_classes.ls.size(); ++i) {
			auto c = gltf_classes.get(i);
			auto t = c->showing_objects;
			for (int j = 0; j < t.size(); ++j){
				auto& pos = t[j]->current_pos;
				ground_instances.emplace_back(pos.x, pos.y, c->sceneDim.radius);
			}
		}
		if (!ground_instances.empty()) {
			sg_apply_pipeline(graphics_state.ground_effect.spotlight_pip);
			graphics_state.ground_effect.spotlight_bind.vertex_buffers[1] = sg_make_buffer(sg_buffer_desc{
				.data = {ground_instances.data(), ground_instances.size() * sizeof(glm::vec3)}
				});
			sg_apply_bindings(graphics_state.ground_effect.spotlight_bind);
			gltf_ground_mats_t u{ pm * vm, camera->position };
			sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(u));
			sg_draw(0, 6, ground_instances.size());
			sg_destroy_buffer(graphics_state.ground_effect.spotlight_bind.vertex_buffers[1]);
		}

		sg_apply_pipeline(graphics_state.utilities.pip_blend);
		graphics_state.utilities.bind.fs_images[0] = graphics_state.ground_effect.ground_img;
		sg_apply_bindings(&graphics_state.utilities.bind);
		sg_draw(0, 4, 1);


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

		//add user shadertoy like custom shader support.
		sg_apply_pipeline(graphics_state.foreground.pip);
		sg_apply_bindings(sg_bindings{
			.vertex_buffers = { graphics_state.quad_vertices },
			.fs_images = {graphics_state.primitives.depth}
			});
		auto foreground_u = u_user_shader_t{
			.invVM = invVm,
			.invPM = invPm,
			.iResolution = glm::vec2(w,h),
			.pvm = pv,
			.camera_pos = camera->position
		};
		sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(foreground_u));
		// sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(foreground_u));
		sg_draw(0, 4, 1);
	}
	sg_end_pass();

	sg_commit();
	
	TOC("commit");
	
	get_viewed_sprites(w, h);
	TOC("gvs")
	// use async pbo to get things.

	TOC("sel")

	ImGuizmo::SetOrthographic(false);
	ImGuizmo::SetDrawlist(dl);
    ImGuizmo::SetRect(disp_area->Pos.x, disp_area->Pos.y, w, h);
	ImGuizmo::SetGizmoSizeClipSpace(120.0f * camera->dpi / w);
	if (guizmo_operation* op = dynamic_cast<guizmo_operation*>(wstate.operation); op != nullptr)
		op->manipulate(disp_area, vm, pm, h, w, viewport);
	if (wstate.drawGuizmo){
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
	    if (abs(alt - M_PI_2) < 0.2f || abs(alt + M_PI_2) < 0.2f)
	        azi = (alt > 0 ? -1 : 1) * atan2(camUp.y, camUp.x);

		camera->Azimuth = azi;
	    camera->Altitude = alt;
	    camera->UpdatePosition();
	}
	
	// ImGui::SetNextWindowPos(ImVec2(disp_area->Pos.x + 16 * camera->dpi, disp_area->Pos.y +disp_area->Size.y - 8 * camera->dpi), ImGuiCond_Always, ImVec2(0, 1));
	ImGui::SetNextWindowPos(ImVec2(disp_area->Pos.x + 8 * camera->dpi, disp_area->Pos.y + 8 * camera->dpi), ImGuiCond_Always, ImVec2(0, 0));
	ImGui::SetNextWindowViewport(viewport->ID);
	ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(1, 1));
	ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 0);
	auto color = ImGui::GetStyle().Colors[ImGuiCol_WindowBg]; color.w = 0;
	ImGui::PushStyleColor(ImGuiCol_WindowBg, color);
	ImGui::Begin("cyclegui_stat", NULL, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoDocking);

	auto io = ImGui::GetIO();
	char buf[256];
	sprintf(buf, "\u2b00 %s FPS=%.0f %s", appName, io.Framerate, appStat);
	if (ImGui::Button(buf))
	{
		ImGui::SetTooltip("GUI-Help");
	}

	// if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort))
	ImGui::End();
	ImGui::PopStyleColor();
	ImGui::PopStyleVar();
	ImGui::PopStyleVar();
	
	TOC("guizmo")

	
	// workspace manipulations:
	if (ProcessWorkspaceFeedback()) return;
	// prop interactions and workspace apis are called in turn.
	if (graphics_state.allowData && (
			TestSpriteUpdate()		// ... more...
		)) {
		graphics_state.allowData = false;
	}
	TOC("fin")
}



void button_widget::keyboardjoystick_map()
{
	if (keyboard_press.size() == 1)
	{
		if (isKJHandling())
			pressed = true;
		else if (previouslyKJHandled)
			pressed = false;
	}
}
void button_widget::process(ImGuiDockNode* disp_area, ImDrawList* dl)
{
	// state.
	if (!isKJHandling())
	{
		auto cx = center_uv.x * ui_state.workspace_w + center_px.x* camera->dpi;
		auto cy = center_uv.y * ui_state.workspace_h + center_px.y* camera->dpi;
		auto rx = 0.5f * (sz_uv.x * ui_state.workspace_w + sz_px.x* camera->dpi);
		auto ry = 0.5f * (sz_uv.y * ui_state.workspace_h + sz_px.y* camera->dpi);

		// foreach pointer, any pointer would trigger.
		auto px = ui_state.mouseX;
		auto py = ui_state.mouseY;
		auto tmp_pressed = ui_state.mouseLeft && cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry;
		for (int i=0; i<ui_state.touches.size(); ++i)
		{
			auto& touch = ui_state.touches[i];
			if (touch.consumed && touch.id != pointer) continue;
			px = touch.touchX - disp_area->Pos.x;
			py = touch.touchY - disp_area->Pos.y;
			tmp_pressed |= cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry;
			if (tmp_pressed){
				touch.consumed = true;
				pointer = touch.id;
				break;
			}
		}
		pressed = tmp_pressed;
	}

	// draw.
	float w = (ui_state.workspace_w * sz_uv.x + sz_px.x * camera->dpi) * 0.5f;
	float h = (ui_state.workspace_h * sz_uv.y + sz_px.y * camera->dpi) * 0.5f;
	float rounding = std::min(w, h);
	{
		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi;
		dl->AddRectFilled(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xaa5c0751, rounding);
		auto c = !pressed ? (ImU32)ImColor::HSV(0.1f * id + 0.1f, 0.6, 1, 0.5) : (ImU32)ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.7);
		dl->AddRectFilled(ImVec2(cx - w, cy - h + (!pressed? -4:4)), ImVec2(cx + w, cy + h+(!pressed?-4:1)), c, rounding);
	}
	{
		auto sz = ImGui::CalcTextSize(display_text.c_str());

		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi - sz.x * 0.5f;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi - sz.y * 0.5f + (!pressed ? -4 : 0);

		dl->AddText(ImVec2(cx+1, cy+1), 0xff444444, display_text.c_str());
		dl->AddText(ImVec2(cx, cy), 0xffffffff, display_text.c_str());
	}
}

void toggle_widget::keyboardjoystick_map()
{
	if (keyboard_press.size() == 1 && keyboard_press[0]){
		if (lastPressCnt + 1 != ui_state.loopCnt)
			on = !on;
		lastPressCnt = ui_state.loopCnt;
	}
}
void toggle_widget::process(ImGuiDockNode* disp_area, ImDrawList* dl)
{
	// state.
	if (!isKJHandling())
	{
		auto cx = center_uv.x * ui_state.workspace_w + center_px.x* camera->dpi;
		auto cy = center_uv.y * ui_state.workspace_h + center_px.y* camera->dpi;
		auto rx = 0.5f * (sz_uv.x * ui_state.workspace_w + sz_px.x * camera->dpi);
		auto ry = 0.5f * (sz_uv.y * ui_state.workspace_h + sz_px.y * camera->dpi);

		// foreach pointer:
		auto px = ui_state.mouseX;
		auto py = ui_state.mouseY;
		if (ui_state.mouseLeft && ui_state.frameCnt==ui_state.mouseLeftDownFrameCnt) {
			if (cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry) {
				on = !on;
				// todo: consume the input.
			}
			pointer = -1; //not touched.
		}else{		
			for (int i=0; i<ui_state.touches.size(); ++i)
			{
				auto& touch = ui_state.touches[i];
				if (touch.id == pointer) touch.consumed = true;
				if (touch.consumed || !touch.starting) continue;
				px = touch.touchX - disp_area->Pos.x;
				py = touch.touchY - disp_area->Pos.y;
				if (cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry)
				{
					on = !on;
					touch.consumed = true;
					pointer = touch.id;
					break;
				}
			}

			auto resetPointer = true;
			for (int i = 0; i < ui_state.touches.size(); ++i)
				if (ui_state.touches[i].id == pointer) resetPointer = false;
			if (resetPointer) pointer = -1;
		}
	}

	// draw.
	float w = (ui_state.workspace_w * sz_uv.x + sz_px.x * camera->dpi) * 0.5f;
	float h = (ui_state.workspace_h * sz_uv.y + sz_px.y * camera->dpi) * 0.5f;
	float w2 = (ui_state.workspace_w * sz_uv.x + (sz_px.x) * camera->dpi) * 0.5f * 0.6f - 4 * camera->dpi;
	float h2 = (ui_state.workspace_h * sz_uv.y + (sz_px.y) * camera->dpi) * 0.5f - 4 * camera->dpi;
	float rounding = std::min(std::min(w, h), std::min(w2, h2));
	{
		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi;
		dl->AddRectFilled(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xa0333333, rounding);
		dl->AddRect(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xaa5c0751, rounding);
	}
	{
		float cx = disp_area->Pos.x + (disp_area->Size.x * center_uv.x + center_px.x * camera->dpi) + (on?1:-1) * w * 0.4;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi;
		auto c = on?(ImU32)ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.5):0x90a0a0a0;
		dl->AddRectFilled(ImVec2(cx - w2, cy - h2), ImVec2(cx + w2, cy + h2), c, rounding);
	}
	{
		auto sz = ImGui::CalcTextSize(display_text.c_str());

		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi - sz.x * 0.5f;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi - sz.y * 0.5f;

		dl->AddText(ImVec2(cx+1, cy+1), 0xff444444, display_text.c_str());
		dl->AddText(ImVec2(cx, cy), 0xffffffff, display_text.c_str());
	}
}

void throttle_widget::init()
{
	//if bounceback record pivot.
}
void throttle_widget::keyboardjoystick_map()
{
	if (keyboard_press.size() == 2){
		if (isKJHandling()){
		if (keyboard_press[1]) current_pos = glm::clamp(current_pos + 0.1f, -1.0f, 1.0f);
		if (keyboard_press[0]) current_pos = glm::clamp(current_pos - 0.1f, -1.0f, 1.0f);
		}else if (previouslyKJHandled)
		{
			if (bounceBack)
			{
				current_pos = init_pos;
			}
		}
	}
}
void throttle_widget::process(ImGuiDockNode* disp_area, ImDrawList* dl)
{
	// state.
	if (!isKJHandling())
	{
		auto cx = center_uv.x * ui_state.workspace_w + center_px.x* camera->dpi;
		auto cy = center_uv.y * ui_state.workspace_h + center_px.y* camera->dpi;
		auto rx = 0.5f * (sz_uv.x * ui_state.workspace_w + sz_px.x * camera->dpi);
		auto ry = 0.5f * (sz_uv.y * ui_state.workspace_h + sz_px.y * camera->dpi);

		// foreach pointer:
		auto px = ui_state.mouseX;
		auto py = ui_state.mouseY;
		if (ui_state.mouseLeft && ui_state.frameCnt==ui_state.mouseLeftDownFrameCnt && pointer == -1) {
			if ((onlyHandle && cx + (current_pos - 0.15) * rx <= px && px <= cx + (current_pos + 0.15) * rx ||
				cx - rx <= px && px <= cx + rx) && cy - ry <= py && py <= cy + ry) {
				pointer = -2; // indicate it's mouse input.
				// todo: consume the input.
			}
		}else
		{
			for (int i=0; i<ui_state.touches.size(); ++i)
			{
				auto& touch = ui_state.touches[i];
				px = touch.touchX - disp_area->Pos.x;
				py = touch.touchY - disp_area->Pos.y;
				if (touch.id == pointer) {
					touch.consumed = true;
					break;
				}
				if (touch.consumed) continue; // put it here because touch could haven't updated yet.
				if (touch.starting && cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry){
					pointer = touch.id;
					touch.consumed = true;
					break;
				}
			}
		}


		if (pointer == -2 && !ui_state.mouseLeft ||
			pointer >=0 && std::all_of(ui_state.touches.begin(), ui_state.touches.end(), [this](const touch_state& p) { return p.id != this->pointer; }))
		{
			pointer = -1;
		}


		if (pointer != -1)
		{
			current_pos = glm::clamp((px - cx) / (rx * 0.7f), -1.0f, 1.0f);
			// printf("thjrottle:%f\n", (px - cx) / (rx * 0.7f));
			// if ((px - cx) / (rx * 0.7f) < -1)
			// 	printf("???");
		}
		else
		{
			if (bounceBack)
			{
				current_pos = init_pos;
			}
		}
	}

	// draw.
	float w = (ui_state.workspace_w * sz_uv.x + sz_px.x * camera->dpi) * 0.5f;
	float h = (ui_state.workspace_h * sz_uv.y + sz_px.y * camera->dpi) * 0.5f;
	float rounding = std::min(w, h)*0.8;
	{
		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi;
		dl->AddRectFilled(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xaa5c0751, rounding);
		dl->AddRectFilled(ImVec2(cx - w+4, cy - h+4), ImVec2(cx + w-4, cy + h), 0xa0335333,rounding);
		// dl->AddQuadFilled(ImVec2(cx + w, cy + h), ImVec2(cx + w, cy -h), ImVec2(cx -w, cy -h), ImVec2(cx -w, cy + h), ImColor::HSV(0.1f * id, 1, 1, 1));
	}
	{
		float w2 = (ui_state.workspace_w * sz_uv.x + (sz_px.x) * camera->dpi) * 0.5f * 0.4f - 4 * camera->dpi;
		float h2 = (ui_state.workspace_h * sz_uv.y + (sz_px.y) * camera->dpi) * 0.5f;
		float cx = disp_area->Pos.x + (disp_area->Size.x * center_uv.x + center_px.x * camera->dpi) + current_pos * w * 0.6;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi;
		ImColor c = ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.5);
		dl->AddRectFilled(ImVec2(cx - w2, cy - h2+2), ImVec2(cx + w2, cy + h2), 0xee222222, rounding);
		dl->AddRectFilled(ImVec2(cx - w2, cy - h2+2), ImVec2(cx + w2, cy + h2 - 4), c, rounding);
	}
	{
		char value_s[40];
		sprintf(value_s, "%s:%0.2f", display_text.c_str(), value());
		auto sz = ImGui::CalcTextSize(value_s);

		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi - sz.x * 0.5f;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi - sz.y * 0.5f;

		// dl->AddText(ImVec2(cx+1, cy+1), 0xff444444, display_text.c_str());
		// dl->AddText(ImVec2(cx, cy), 0xffffffff, display_text.c_str());

		dl->AddText(ImVec2(cx+1, cy+1-2), 0xff444444, value_s);
		dl->AddText(ImVec2(cx, cy-2), 0xffffffff, value_s);
	}
}

void stick_widget::keyboardjoystick_map()
{
	if (keyboard_press.size() == 4){
		if (isKJHandling()){
			constexpr float step = 0.075f;
			// udlr
			if (keyboard_press[0]) current_pos.y = glm::clamp(current_pos.y - step, -1.0f, 1.0f);
			else if (keyboard_press[1]) current_pos.y = glm::clamp(current_pos.y + step, -1.0f, 1.0f);
			else current_pos.y = glm::sign(current_pos.y) * glm::clamp(glm::abs(current_pos.y) - step, 0.0f, 1.0f);
			
			if (keyboard_press[2]) current_pos.x = glm::clamp(current_pos.x - step, -1.0f, 1.0f);
			else if (keyboard_press[3]) current_pos.x = glm::clamp(current_pos.x + step, -1.0f, 1.0f);
			else current_pos.x = glm::sign(current_pos.x) * glm::clamp(glm::abs(current_pos.x) - step, 0.0f, 1.0f);
		}else if (previouslyKJHandled)
		{
			if (bounceBack)
			{
				current_pos += (init_pos - current_pos) * 0.1f;
			}
		}
	}
}
void stick_widget::process(ImGuiDockNode* disp_area, ImDrawList* dl)
{
	// state.
	auto sz = std::min(sz_uv.x * ui_state.workspace_w + sz_px.x * camera->dpi, sz_uv.y * ui_state.workspace_h + sz_px.y * camera->dpi) * 0.5f;

	if (!isKJHandling())
	{
		auto cx = center_uv.x * ui_state.workspace_w + center_px.x* camera->dpi;
		auto cy = center_uv.y * ui_state.workspace_h + center_px.y* camera->dpi;

		// foreach pointer:
		auto px = ui_state.mouseX;
		auto py = ui_state.mouseY;
		if (ui_state.mouseLeft && ui_state.frameCnt==ui_state.mouseLeftDownFrameCnt && pointer == -1) {
			if ((onlyHandle && cx + (current_pos.x - 0.15) * sz <= px && px <= cx + (current_pos.x + 0.15) * sz || cx - sz <= px && px <= cx + sz) && 
				(onlyHandle && cy + (current_pos.y - 0.15) * sz <= py && py <= cy + (current_pos.y + 0.15) * sz || cy - sz <= py && py <= cy + sz)) {
				pointer = -2;
				// todo: consume the input.
			}
		}else
		{
			for (int i=0; i<ui_state.touches.size(); ++i)
			{
				auto& touch = ui_state.touches[i];
				px = touch.touchX - disp_area->Pos.x;
				py = touch.touchY - disp_area->Pos.y;
				if (touch.id == pointer) {
					touch.consumed = true;
					break;
				}
				if (touch.consumed) continue;
				if (touch.starting && cx - sz <= px && px <= cx + sz && cy - sz <= py && py <= cy + sz) {
					pointer = touch.id;
					touch.consumed = true;
					break;
				}
			}
		}

		if (pointer == -2 && !ui_state.mouseLeft ||
			pointer >= 0 && std::all_of(ui_state.touches.begin(), ui_state.touches.end(), [this](const touch_state& p) { return p.id != this->pointer; }))
		{
			// todo: replace with "pointer released"
			pointer = -1;
		}


		if (pointer != -1)
		{
			current_pos = glm::clamp(glm::vec2((px - cx) / (sz * 0.7f),(py - cy) / (sz * 0.7f)), -1.0f, 1.0f);
		}
		else
		{
			if (bounceBack)
			{
				current_pos += (init_pos - current_pos) * 0.1f;
			}
		}
	}

	// draw.
	float rounding = sz * 0.666;
	{
		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi;
		dl->AddRectFilled(ImVec2(cx - sz, cy - sz), ImVec2(cx + sz, cy + sz), 0xaa5c0751, rounding);
		dl->AddRectFilled(ImVec2(cx - sz+2, cy -sz+2), ImVec2(cx + sz, cy + sz), 0xa0335333, rounding);
		dl->AddTriangleFilled(ImVec2(cx - sz * 0.8, cy), ImVec2(cx - sz * 0.7, cy + sz * 0.1), ImVec2(cx - sz * 0.7, cy - sz * 0.1), 0xa0221122);
		dl->AddTriangleFilled(ImVec2(cx + sz * 0.8, cy), ImVec2(cx + sz * 0.7, cy + sz * 0.1), ImVec2(cx + sz * 0.7, cy - sz * 0.1), 0xa0221122);
		dl->AddTriangleFilled(ImVec2(cx, cy - sz * 0.8), ImVec2(cx + sz * 0.1, cy - sz * 0.7), ImVec2(cx - sz * 0.1, cy - sz * 0.7), 0xa0221122);
		dl->AddTriangleFilled(ImVec2(cx, cy + sz * 0.8), ImVec2(cx + sz * 0.1, cy + sz * 0.7), ImVec2(cx - sz * 0.1, cy + sz * 0.7), 0xa0221122);
		// dl->AddQuadFilled(ImVec2(cx + w, cy + h), ImVec2(cx + w, cy -h), ImVec2(cx -w, cy -h), ImVec2(cx -w, cy + h), ImColor::HSV(0.1f * id, 1, 1, 1));
	}
	{
		float w2 = (ui_state.workspace_w * sz_uv.x + (sz_px.x) * camera->dpi) * 0.5f * 0.6f - 4 * camera->dpi;
		float h2 = (ui_state.workspace_h * sz_uv.y + (sz_px.y) * camera->dpi) * 0.5f * 0.6f - 4 * camera->dpi;
		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi + current_pos.x * sz * 0.4;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi + current_pos.y * sz * 0.4;
		ImColor c = ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.5);
		dl->AddCircleFilled(ImVec2(cx, cy), sz * 0.6f, c);
		dl->AddCircleFilled(ImVec2(cx - 1, cy - 1), sz * 0.6f - 2, 0x99000000);
		dl->AddCircleFilled(ImVec2(cx - 2, cy - 2), sz * 0.6f - 3, c);
		dl->AddCircleFilled(ImVec2(cx-sz*0.35f, cy-sz*0.35f), 4, 0xffffffff);
	}
	{
		char value_s[40];
		sprintf(value_s, "%s\n%0.2f,%0.2f", display_text.c_str(), current_pos.x, current_pos.y);
		auto sz = ImGui::CalcTextSize(value_s);

		float cx = disp_area->Pos.x + disp_area->Size.x * center_uv.x + center_px.x * camera->dpi - sz.x * 0.5f;
		float cy = disp_area->Pos.y + disp_area->Size.y * center_uv.y + center_px.y * camera->dpi - sz.y * 0.5f;

		dl->AddText(ImVec2(cx+1, cy+1), 0xff444444, value_s);
		dl->AddText(ImVec2(cx, cy), 0xffffffff, value_s);
	}
}

void gesture_operation::manipulate(ImGuiDockNode* disp_area, ImDrawList* dl)
{
	for(int i=0; i<widgets.ls.size(); ++i)
	{
		auto w = widgets.get(i);
		w->id = i;
		w->process_keyboardjoystick();

		if (disp_area != nullptr)
			w->process(disp_area, dl);
	}
	ui_state.workspace_state.top().feedback = realtime_event;
}
gesture_operation::~gesture_operation()
{
	for (int i=0; i< widgets.ls.size(); ++i){
		auto gesture = widgets.get(i);
		delete gesture;
	}
}

void InitGL(int w, int h)
{
	auto io = ImGui::GetIO();
	io.ConfigInputTrickleEventQueue = false;
	io.ConfigDragClickToInputText = true;


	ui_state.workspace_w=lastW = w;
	ui_state.workspace_h=lastH = h;

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


//WORKSPACE FEEDBACK

#define WSFeedInt32(x) { *(int*)pr=x; pr+=4;}
#define WSFeedFloat(x) { *(float*)pr=x; pr+=4;}
#define WSFeedDouble(x) { *(double*)pr=x; pr+=8;}
#define WSFeedBytes(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WSFeedString(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WSFeedBool(x) {*(bool*)pr=x; pr+=1;}

// should be called inside before draw.
unsigned char ws_feedback_buf[1024 * 1024];

bool TestSpriteUpdate()
{
	auto pr = ws_feedback_buf;

	//other operations:
	// dynamic images processing.
	// 1. for all rgbn, sort by (viewing area-(unviewed?unviewed time:0)) descending.
	// 2. keep rgbas at atlas until atlas is full or current rgba is unviewed. get a rgba schedule list (!loaded || invalidate)
	// 3. if any rgba is on schedule, issue, don't exceessively require data.(WSFeedInt32(0)....)
	// 4. issue a render task to reorder, if any w/h changed.
	std::vector<me_rgba*> shown_rgba;
	shown_rgba.reserve(argb_store.rgbas.ls.size());
	std::vector<int> pixels(argb_store.atlasNum);
	std::vector<std::vector<me_rgba*>> reverseIdx(argb_store.atlasNum);
	auto ttlPix = 0;
	for (int i = 0; i < argb_store.rgbas.ls.size(); ++i) {
		auto rgbptr = argb_store.rgbas.get(i);
		auto pix = rgbptr->width * rgbptr->height;
		if (rgbptr->occurrence > 0) {
			shown_rgba.push_back(rgbptr);
			ttlPix += pix;
		}
		if (rgbptr->atlasId != -1)
		{
			pixels[rgbptr->atlasId] += pix;
			reverseIdx[rgbptr->atlasId].push_back(rgbptr);
		}
	}
	std::sort(shown_rgba.begin(), shown_rgba.end(), [](const me_rgba* a, const me_rgba* b) {
		return a->occurrence > b->occurrence;
		});
	std::vector<me_rgba*> allocateList, candidateList;
	auto updateAtlas = -1;
	using rect_ptr = rect_type*;
	for (int i = 0; i < shown_rgba.size(); ++i)
	{
		if (shown_rgba[i]->width <= 0) continue;
		if (!shown_rgba[i]->invalidate && shown_rgba[i]->loaded) continue;
		if (shown_rgba[i]->atlasId == -1)
		{
			// if not selected atlas to register, select an atlas.
			if (updateAtlas == -1)
			{
				//try to fit in at least one atlas.
				std::vector<int> atlasSeq(argb_store.atlasNum);
				for (int j = 0; j < argb_store.atlasNum; ++j) atlasSeq[j] = j;
				std::sort(atlasSeq.begin(), atlasSeq.end(), [&pixels](const int& a, const int& b) {
					return pixels[a] > pixels[b];
					});
				for (int j = 0; j < argb_store.atlasNum; ++j)
				{
					if (shown_rgba[i]->width * shown_rgba[i]->height + pixels[atlasSeq[j]] > 4096 * 4096 * 0.99) break;
					auto& rgbas = reverseIdx[atlasSeq[j]];
					std::vector<rect_type> rects;
					rects.push_back(rectpack2D::rect_xywh(0, 0, shown_rgba[i]->width, shown_rgba[i]->height));
					for (int k = 0; k < rgbas.size(); ++k)
						rects.push_back(rectpack2D::rect_xywh(0, 0, rgbas[k]->width, rgbas[k]->height));
					std::sort(rects.begin(), rects.end(), [](const rectpack2D::rect_xywh& a, const rectpack2D::rect_xywh& b) {
						return a.get_wh().area() > b.get_wh().area();
						});

					auto packed = true;
					auto report_successful = [](rect_type&) {
						return rectpack2D::callback_result::CONTINUE_PACKING;
						};
					auto report_unsuccessful = [&](rect_type&) {
						packed = false;
						return rectpack2D::callback_result::ABORT_PACKING;
						};
					const auto result_size = rectpack2D::find_best_packing_dont_sort<spaces_type>(
						rects,
						rectpack2D::make_finder_input(
							4096,
							1,
							report_successful,
							report_unsuccessful,
							rectpack2D::flipping_option::DISABLED
						)
					);

					if (!packed) continue;
					updateAtlas = atlasSeq[j];
					allocateList = reverseIdx[atlasSeq[j]];
					goto atlasFound;
				}
				// atlas is insufficient. expand atlas array by factor 2.
				// todo............
			}
		atlasFound:
			allocateList.push_back(shown_rgba[i]);
		}
		else
		{
			candidateList.push_back(shown_rgba[i]); // already allocated.
		}
	}
	if (updateAtlas >= 0)
	{
		// backup previous atlas rgba's uv.
		std::vector<glm::vec4> uvuv(reverseIdx[updateAtlas].size());
		for (int i = 0; i < uvuv.size(); ++i)
			uvuv[i] = glm::vec4(reverseIdx[updateAtlas][i]->uvStart, reverseIdx[updateAtlas][i]->uvEnd);
		// try pack.
		std::vector<rect_type> rects(allocateList.size());
		for (int k = 0; k < allocateList.size(); ++k)
		{
			rects[k].w = allocateList[k]->width;
			rects[k].h = allocateList[k]->height;
		}
		auto report_successful = [&](rect_type& r) {
			// allocated.
			auto id = &r - rects.data();
			allocateList[id]->atlasId = updateAtlas;
			allocateList[id]->uvStart = glm::vec2(r.x, r.y+r.h);
			allocateList[id]->uvEnd = glm::vec2((r.x + r.w), (r.y));
			candidateList.push_back(allocateList[id]);
			return rectpack2D::callback_result::CONTINUE_PACKING;
			};
		auto report_unsuccessful = [&](rect_type& ri) {
			return rectpack2D::callback_result::CONTINUE_PACKING;
			};
		const auto result_size = rectpack2D::find_best_packing<spaces_type>(
			rects,
			rectpack2D::make_finder_input(
				4096,
				1,
				report_successful,
				report_unsuccessful,
				rectpack2D::flipping_option::DISABLED
			)
		);

		// atlas is changed, perform an OpenGL shuffle.
	}

	if (candidateList.size() > 0) {
		WSFeedInt32(-1);
		// sprite processing id.
		WSFeedInt32(0);
		auto updatePix = 0;
		auto lastId = 0;
		for (int i = 0; i < candidateList.size(); ++i)
		{
			lastId = i;
			auto ptr = argb_store.rgbas.get(candidateList[i]->instance_id);
			updatePix += ptr->width * ptr->height;
			if (updatePix * 4 > 1024 * 1024 && lastId>4)
			{
				break;
			}
		}
		WSFeedInt32(lastId + 1);
		for (int i = 0; i <= lastId; ++i)
		{
			auto& str = std::get<1>(argb_store.rgbas.ls[candidateList[i]->instance_id]);
			WSFeedString(str.c_str(), str.length());
		}

		// finalize:
		workspaceCallback(ws_feedback_buf, pr - ws_feedback_buf);

		return true;
	}
	return false;
}

void throttle_widget::feedback(unsigned char*& pr)
{
	WSFeedFloat(value());
	WSFeedBool(pointer != -1);
}
void toggle_widget::feedback(unsigned char*& pr)
{
	WSFeedBool(on);
}
void button_widget::feedback(unsigned char*& pr)
{
	WSFeedBool(pressed);
}
void stick_widget::feedback(unsigned char*& pr)
{
	WSFeedFloat(current_pos.x);
	WSFeedFloat(-current_pos.y); // cartesian rhc
	WSFeedBool(pointer != -1);
}

void gesture_operation::feedback(unsigned char*& pr)
{
	WSFeedInt32(widgets.ls.size());
	for(int i=0; i<widgets.ls.size(); ++i)
	{
		auto g = widgets.get(i);
		WSFeedString(g->widget_name.c_str(), g->widget_name.length());
		g->feedback(pr);
	}
}

void select_operation::feedback(unsigned char*& pr)
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
		auto& objs = cls->objects;
		for (int j = 0; j < objs.ls.size(); ++j)
		{
			auto t = std::get<0>(objs.ls[j]);
			auto name = std::get<1>(objs.ls[j]);

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

				// should notify how many is selected.
				for (int z = 0; z < cls->model.nodes.size(); ++z)
				{
					if (((int(t->nodeattrs[z].flag) & (1 << 3)) != 0))
					{
						auto &str = cls->model.nodes[z].name;
						WSFeedString(str.c_str(), str.length());
					}
				}

				// auto sz = int(ceil(cls->model.nodes.size() / 8.0f));
				// std::vector<unsigned char> bits(sz);
				// for (int z = 0; z < cls->model.nodes.size(); ++z)
				// 	bits[z / 8] |= (((int(t->nodeattrs[z].flag) & (1 << 3)) != 0) << (z % 8));
				// WSFeedBytes(bits.data(), sz);
				// // todo: problematic: could selected multiple sub, use "cpuSelection"
				// auto id = 0;
				// auto subname = cls->nodeId_name_map[id];
				// WSFeedString(subname.c_str(), subname.length());
			}
		}
	}
	WSFeedInt32(-1);
}
void guizmo_operation::feedback(unsigned char*& pr)
{
	// transform feedback.
	WSFeedInt32(obj_action_state.size());

	for (auto& oas : obj_action_state)
	{
		WSFeedString(oas.obj->name.c_str(), oas.obj->name.length());
		WSFeedFloat(oas.obj->target_position[0]);
		WSFeedFloat(oas.obj->target_position[1]);
		WSFeedFloat(oas.obj->target_position[2]);
		WSFeedFloat(oas.obj->target_rotation[0]);
		WSFeedFloat(oas.obj->target_rotation[1]);
		WSFeedFloat(oas.obj->target_rotation[2]);
		WSFeedFloat(oas.obj->target_rotation[3]);
	}
}

bool ProcessWorkspaceFeedback()
{
	auto pr = ws_feedback_buf;
	auto& wstate = ui_state.workspace_state.top();
	auto pid = wstate.id; // wstate pointer.
	if (wstate.feedback == pending)
		return false;

	WSFeedInt32(pid);
	if (wstate.feedback == operation_canceled ) // canceled.
	{
		WSFeedBool(false);

		// terminal.
		ui_state.pop_workspace_state();
		
		workspaceCallback(ws_feedback_buf, pr - ws_feedback_buf);
	}
	else {
		WSFeedBool(true); // have feedback value now.

		if (wstate.feedback == feedback_finished) // operation has finished.
		{ 
			WSFeedBool(true);
			wstate.operation->feedback(pr);
			ui_state.pop_workspace_state();
			workspaceCallback(ws_feedback_buf, pr - ws_feedback_buf);
		}
		else if (wstate.feedback == feedback_continued)
		{
			WSFeedBool(false);
			wstate.operation->feedback(pr);
			workspaceCallback(ws_feedback_buf, pr - ws_feedback_buf);
			wstate.feedback = pending; // invalidate feedback.
		}
		else if (wstate.feedback == realtime_event) // live streaming event
		{
			WSFeedBool(false);
			wstate.operation->feedback(pr);
			realtimeUICallback(ws_feedback_buf, pr - ws_feedback_buf);
			wstate.feedback = pending; // invalidate feedback.
		}
	}

	// finalize:
	return true;
}

void guizmo_operation::manipulate(ImGuiDockNode* disp_area, glm::mat4 vm, glm::mat4 pm, int h, int w, ImGuiViewport* viewport)
{
	glm::mat4 mat = glm::mat4_cast(gizmoQuat);
	mat[3] = glm::vec4(gizmoCenter, 1.0f);

	int getGType = ImGuizmo::ROTATE | ImGuizmo::TRANSLATE;
	ImGuizmo::Manipulate((float*)&vm, (float*)&pm, (ImGuizmo::OPERATION)getGType, ImGuizmo::LOCAL, (float*)&mat);

	glm::vec3 translation, scale;
	glm::quat rotation;
	glm::vec3 skew;
	glm::vec4 perspective;
	glm::decompose(mat, scale, gizmoQuat, gizmoCenter, skew, perspective);
	
	for (auto& st : obj_action_state)
	{
		auto nmat = mat * st.intermediate;
		glm::decompose(nmat, scale, st.obj->target_rotation, st.obj->target_position, skew, perspective);
	}

	if (realtime)
		ui_state.workspace_state.top().feedback = realtime_event;

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
		ui_state.workspace_state.top().feedback = feedback_finished;
	}
	ImGui::PopStyleColor();
	ImGui::SameLine();
	ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(1.0f, 0.0f, 0.0f, 1.0f));
	if (ImGui::Button("\uf00d"))
	{
		ui_state.workspace_state.top().feedback = operation_canceled;
		// revoke operation.
		gizmoCenter = originalCenter;
		gizmoQuat = glm::identity<glm::quat>();
		glm::mat4 mat = glm::mat4_cast(gizmoQuat);
		mat[3] = glm::vec4(gizmoCenter, 1.0f);
		glm::decompose(mat, scale, gizmoQuat, gizmoCenter, skew, perspective);
		for (auto& st : obj_action_state)
		{
			auto nmat = mat * st.intermediate;
			glm::decompose(nmat, scale, st.obj->target_rotation, st.obj->target_position, skew, perspective);
		}
	}
	ImGui::PopStyleColor();
	ImGui::PopStyleVar();
	
	ImGui::End();
}

void guizmo_operation::selected_get_center()
{
	auto op = static_cast<guizmo_operation*>(ui_state.workspace_state.top().operation);

	obj_action_state.clear();
	glm::vec3 pos(0.0f);
	float n = 0;
	// selecting feedback.
	for (int i = 0; i < pointclouds.ls.size(); ++i)
	{
		auto t = std::get<0>(pointclouds.ls[i]);
		auto name = std::get<1>(pointclouds.ls[i]);
		if ((t->flag & (1 << 6)) || (t->flag & (1 << 9))) {   //selected point cloud
			pos += t->target_position;
			obj_action_state.push_back(obj_action_state_t{ .obj = t });
			n += 1;
		}
	}

	for (int i = 0; i < gltf_classes.ls.size(); ++i)
	{
		auto& objs = gltf_classes.get(i)->objects;
		for (int j = 0; j < objs.ls.size(); ++j)
		{
			auto t = std::get<0>(objs.ls[j]);
			auto name = std::get<1>(objs.ls[j]);

			if ((t->flags & (1 << 3)) || (t->flags & (1 << 6))) // selected gltf
			{
				pos += t->target_position;
				obj_action_state.push_back(obj_action_state_t{ .obj = t });
				n += 1;
			}
		}
	}

	op->gizmoCenter = op->originalCenter = pos / n;
	op->gizmoQuat = glm::identity<glm::quat>();

	glm::mat4 gmat = glm::mat4_cast(op->gizmoQuat);
	gmat[3] = glm::vec4(op->gizmoCenter, 1.0f);
	glm::mat4 igmat = glm::inverse(gmat);

	for (auto& st : obj_action_state)
	{
		glm::mat4 mat = glm::mat4_cast(st.obj->target_rotation);
		mat[3] = glm::vec4(st.obj->target_position, 1.0f);

		st.intermediate = igmat * mat;
	}
}