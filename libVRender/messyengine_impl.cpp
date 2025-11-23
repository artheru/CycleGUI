#include "me_impl.h"

// ========  Library Imports  =========
#include <imgui_internal.h>
#include <glm/gtc/type_ptr.hpp>
#include <glm/gtx/matrix_decompose.hpp>
#include <glm/gtx/intersect.hpp>
#include <bitset>
#include <algorithm>
#include <cstring>
#include <cstdint>
#include <cmath>
#include <limits>
#include <functional>

// ======== API Set addon for sokol =======
#include "platform.hpp"

// ======== Sub implementations =========
#include "groundgrid.hpp"
#include "camera.hpp"
#include "ImGuizmo.h"
#include "init_impl.hpp"
#include "objects.hpp"
#include "skybox.hpp"
#include "interfaces.hpp"
#include "stat_viewer.hpp"
#include "utilities.h"
#include "shaders/shaders.h"
#include "lib/imgui/misc/cpp/imgui_stdlib.h"

void DrawViewportMenuBar();
bool ProcessOperationFeedback();
bool ProcessInteractiveFeedback();

extern std::vector<std::function<bool(unsigned char*&)>> interactive_processing_list;

void ClearSelection()
{
	//working_viewport->selected.clear();
	for (int gi = 0; gi < global_name_map.ls.size(); ++gi){
		auto nt = global_name_map.get(gi);
		auto name = global_name_map.getName(gi);
		RouteTypes(nt, 
			[&]	{
				// point cloud.
				auto t = (me_pcRecord*)nt->obj;
				t->flag &= ~(1 << 6); // not selected as whole
				if (t->flag & (1 << 8)) { // only when sub selectable update sel image
					int sz = ceil(sqrt(t->capacity / 8));
					memset(t->cpuSelection, 0, sz*sz);
					sg_update_image(t->pcSelection, sg_image_data{
							.subimage = {{ { t->cpuSelection, (size_t)(sz*sz) } }} }); //neither selecting item.
				}
			}, [&](int class_id)
			{
				// gltf
				auto t = (gltf_object*)nt->obj;
				auto cls = gltf_classes.get(class_id);

				t->flags[working_viewport_id] &= ~(1 << 3); // not selected as whole
				t->flags[working_viewport_id] &= ~(1 << 6);

				for (auto& a : t->nodeattrs)
					a.flag = (int(a.flag) & ~(1 << 3));
			}, [&]
			{
				// line piece.
				auto piece = (me_line_piece*)nt->obj;
				piece->flags[working_viewport_id] &= ~(1 << 3);
			}, [&]
			{
				// sprites;
				auto t = (me_sprite*)nt->obj;
				t->per_vp_stat[working_viewport_id] &= ~(1 << 1);
			},[&]
			{
				// ui:
				auto t = (me_world_ui*)nt->obj;
				t->selected[working_viewport_id] = false;
			},[&]
			{
				// geometry.
			});
	}

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

template <typename RegionPredicate>
static bool fine_select_pointclouds(select_operation* sel_op, bool deselect, const RegionPredicate& containsScreenPoint,
	const glm::mat4& viewMatrix, const glm::mat4& projMatrix, const glm::vec2& screenSize)
{
	bool anyChange = false;
	for (int i = 0; i < pointclouds.ls.size(); ++i)
	{
		auto t = pointclouds.get(i);
		if (t == nullptr) continue;
		if (t->n <= 0) continue;
		if (!t->show[working_viewport_id]) continue;
		if ((t->flag & (1 << 4)) == 0) continue;

		bool selectableWhole = (t->flag & (1 << 7)) != 0;
		bool selectableSub = (t->flag & (1 << 8)) != 0;
		if (!selectableWhole && !selectableSub) continue;

		std::vector<glm::vec4> cpuPoints(t->n);
		readBuffer(t->pcBuf, 0, t->n * sizeof(glm::vec4), cpuPoints.data());

		bool cloudChanged = false;
		bool touchedSub = false;

		for (int pid = 0; pid < t->n; ++pid)
		{
			const auto& pt = cpuPoints[pid];
			glm::vec3 world = t->current_pos + t->current_rot * glm::vec3(pt);
			glm::vec2 screen = world2pixel(world, viewMatrix, projMatrix, screenSize);

			if (!std::isfinite(screen.x) || !std::isfinite(screen.y))
				continue;

			if (!containsScreenPoint(screen.x, screen.y))
				continue;

			if (!deselect)
			{
				if (selectableWhole && (t->flag & (1 << 6)) == 0)
				{
					t->flag |= (1 << 6);
					cloudChanged = true;
					break;
				}
				if (selectableSub)
				{
					size_t byte_idx = size_t(pid) / 8;
					uint8_t bit_mask = uint8_t(1 << (pid % 8));
					if ((t->cpuSelection[byte_idx] & bit_mask) == 0)
					{
						t->cpuSelection[byte_idx] |= bit_mask;
						cloudChanged = true;
						touchedSub = true;
					}
				}
			}
			else
			{
				if (selectableWhole && (t->flag & (1 << 6)) != 0)
				{
					t->flag &= ~(1 << 6);
					cloudChanged = true;
					break;
				}
				if (selectableSub && (t->flag & (1 << 9)))
				{
					size_t byte_idx = size_t(pid) / 8;
					uint8_t bit_mask = uint8_t(1 << (pid % 8));
					if ((t->cpuSelection[byte_idx] & bit_mask) != 0)
					{
						t->cpuSelection[byte_idx] &= ~bit_mask;
						cloudChanged = true;
						touchedSub = true;
					}
				}
			}
		}

		if (selectableSub && cloudChanged)
		{
			if (!deselect)
			{
				if (touchedSub)
					t->flag |= (1 << 9);
			}
			else if ((t->flag & (1 << 9)) != 0)
			{
				int dim = int(std::ceil(std::sqrt(t->capacity / 8.0f)));
				int total_bytes = dim * dim;
				bool any_selected = false;
				for (int bi = 0; bi < total_bytes; ++bi)
				{
					if (t->cpuSelection[bi] != 0)
					{
						any_selected = true;
						break;
					}
				}
				if (!any_selected)
					t->flag &= ~(1 << 9);
			}
		}

		if (cloudChanged)
			anyChange = true;
	}
	return anyChange;
}

void process_remaining_touches()
{
	// touch X/Y should be pixel (for browser should be dpi scaled)
	static int touchState = 0;
	
	static float iTouchDist = -1;
	static float iX = 0, iY = 0;

	float touches[20];
	auto length = 0;

	ui.prevTouches.clear();
	for (int i = 0; i < ui.touches.size(); ++i){
		ui.prevTouches.insert(ui.touches[i].id);
		if (!ui.touches[i].consumed)
		{
			auto tX = ui.touches[i].touchX;
			auto tY = ui.touches[i].touchY;

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
		auto offset = (wd - iTouchDist) / working_viewport->camera.dpi * 0.07;
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

void DefaultRenderWorkspace(disp_area_t disp_area, ImDrawList* dl);
void ProcessWorkspace(disp_area_t disp_area, ImDrawList* dl, ImGuiViewport* viewport);
void GenMonitorInfo();
static void LoadGratingParams(grating_param_t* params);

float grating_disp_fac = 4;
// only on displaying.
void DrawMainWorkspace()
{
	// Render Debug UI
	if (ui.displayRenderDebug()) {
		ImGui::DragFloat("GLTF_illumfac", &GLTF_illumfac, 0.1f, 0, 300);
		ImGui::DragFloat("GLTF_illumrng", &GLTF_illumrng, 0.001f, 1.0, 1.5f);
	}


	ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
	auto vp = ImGui::GetMainViewport();
	auto dl = ImGui::GetBackgroundDrawList(vp);
	if (node) {
		auto central = ImGui::DockNodeGetRootNode(node)->CentralNode;
		GenMonitorInfo();
		ui.viewports[0].active = true;
		switch_context(0);
		if (ui.viewports[0].displayMode == viewport_state_t::Normal){
			vp->useAuxScale = false;
			vp->auxScale = 1.0;
			DefaultRenderWorkspace(disp_area_t{ .Size = {(int)central->Size.x, (int)central->Size.y}, .Pos = {(int)central->Pos.x, (int)central->Pos.y} }, dl);
		}

		else if(ui.viewports[0].displayMode == viewport_state_t::EyeTrackedHolography)
		{
			vp->useAuxScale = true;
			vp->auxScale = 2.0;
			// compute eye position:
			if (!working_viewport->holography_loaded_params) {
				working_viewport->holography_loaded_params = true;
				LoadGratingParams(&grating_params);
			}

			auto midpnt = (grating_params.left_eye_pos_mm + grating_params.right_eye_pos_mm) / 2.0f;

			auto eye_pos_to_screen_center_physical_left = grating_params.left_eye_pos_mm * grating_params.pupil_factor + midpnt * (1 - grating_params.pupil_factor) - glm::vec3(grating_params.screen_size_physical_mm / 2.0f, 0);
			shared_graphics.ETH_display.left_eye_world = glm::vec3(eye_pos_to_screen_center_physical_left.x, -eye_pos_to_screen_center_physical_left.y, eye_pos_to_screen_center_physical_left.z) / grating_params.world2phy;

			auto eye_pos_to_screen_center_physical_right = grating_params.right_eye_pos_mm * grating_params.pupil_factor + midpnt * (1 - grating_params.pupil_factor) - glm::vec3(grating_params.screen_size_physical_mm / 2.0f, 0);
			shared_graphics.ETH_display.right_eye_world = glm::vec3(eye_pos_to_screen_center_physical_right.x, -eye_pos_to_screen_center_physical_right.y, eye_pos_to_screen_center_physical_right.z) / grating_params.world2phy;


			// we only use /4 resolution for holography.
			grating_disp_fac = 4;

			working_graphics_state->ETH_display = { .eye_id = 0 };
			DefaultRenderWorkspace(disp_area_t{ .Size = {(int)(central->Size.x/grating_disp_fac), (int)(central->Size.y/grating_disp_fac)}, .Pos = {(int)central->Pos.x, (int)central->Pos.y} }, dl);

			working_graphics_state = &graphics_states[MAX_VIEWPORTS];
			working_graphics_state->ETH_display = { .eye_id = 1 };
			DefaultRenderWorkspace(disp_area_t{ .Size = {(int)(central->Size.x/grating_disp_fac), (int)(central->Size.y/grating_disp_fac)}, .Pos = {(int)central->Pos.x, (int)central->Pos.y} }, dl);
		}
		else if (ui.viewports[0].displayMode == viewport_state_t::EyeTrackedHolography2)
		{
			vp->useAuxScale = true;
			vp->auxScale = 2.0;

			// compute eye position:
			auto midpnt = (grating_params.left_eye_pos_mm + grating_params.right_eye_pos_mm) / 2.0f;

			auto eye_pos_to_screen_center_physical_left = grating_params.left_eye_pos_mm * grating_params.pupil_factor + midpnt * (1 - grating_params.pupil_factor) - glm::vec3(grating_params.screen_size_physical_mm / 2.0f, 0);
			shared_graphics.ETH_display.left_eye_world = glm::vec3(eye_pos_to_screen_center_physical_left.x, -eye_pos_to_screen_center_physical_left.y, eye_pos_to_screen_center_physical_left.z) / grating_params.world2phy;

			auto eye_pos_to_screen_center_physical_right = grating_params.right_eye_pos_mm * grating_params.pupil_factor + midpnt * (1 - grating_params.pupil_factor) - glm::vec3(grating_params.screen_size_physical_mm / 2.0f, 0);
			shared_graphics.ETH_display.right_eye_world = glm::vec3(eye_pos_to_screen_center_physical_right.x, -eye_pos_to_screen_center_physical_right.y, eye_pos_to_screen_center_physical_right.z) / grating_params.world2phy;


			// we only use /2 resolution for holography.
			grating_disp_fac = 2;

			working_graphics_state->ETH_display = { .eye_id = 0 };
			DefaultRenderWorkspace(disp_area_t{ .Size = {(int)(central->Size.x / grating_disp_fac), (int)(central->Size.y / grating_disp_fac)}, .Pos = {(int)central->Pos.x, (int)central->Pos.y} }, dl);

			working_graphics_state = &graphics_states[MAX_VIEWPORTS];
			working_graphics_state->ETH_display = { .eye_id = 1 };
			DefaultRenderWorkspace(disp_area_t{ .Size = {(int)(central->Size.x / grating_disp_fac), (int)(central->Size.y / grating_disp_fac)}, .Pos = {(int)central->Pos.x, (int)central->Pos.y} }, dl);
		}

		ProcessWorkspace(disp_area_t{ .Size = {(int)central->Size.x, (int)central->Size.y}, .Pos = {(int)central->Pos.x, (int)central->Pos.y} }, dl, vp);
		working_viewport->frameCnt += 1;
	}
}

void FinalizeFrame()
{
	ui.loopCnt += 1;
	process_remaining_touches();
}

void ProcessBackgroundWorkspace()
{
	// gesture could listen to keyboard/joystick. process it.
	auto& wstate = ui.viewports[working_viewport_id].workspace_state.back();
	// patch for gesture_operation.
	if (gesture_operation* op = dynamic_cast<gesture_operation*>(wstate.operation); op != nullptr)
		op->draw(disp_area_t{}, nullptr, glm::mat4{}, glm::mat4{});
	ProcessOperationFeedback();
}


void get_viewed_sprites(int w, int h)
{
	// todo: to simplify, we only read out mainviewport.

	// if (working_viewport != ui.viewports) return;
	// Operations that requires read from rendered frame, slow... do them after all render have safely done.
	// === what rgb been viewed? how much pix?
	if (working_viewport->frameCnt > 60 && argb_store.rgbas.ls.size()>0)
	{
		occurences_readout(w, h);
		// printf("\n");
	}
}

void camera_manip()
{
	if (working_viewport->camera.test_apply_external()) {
		return;
	}

	if (working_viewport->camera.anchor_type == 0 || working_viewport->camera.anchor_type == 1)
		camera_object->current_pos = camera_object->target_position = working_viewport->camera.stare;
	else
		camera_object->current_pos = camera_object->target_position = working_viewport->camera.position;


	// === camera manipulation ===
	// related to camera-obj coordination.
	if (working_viewport->refreshStare) {
		working_viewport->refreshStare = false;

		if (abs(working_viewport->camera.position.z - working_viewport->camera.stare.z) > 0.001) {
			glm::vec4 starepnts[400];
			me_getTexFloats(working_graphics_state->primitives.depth, starepnts, working_viewport->disp_area.Size.x / 2-10, working_viewport->disp_area.Size.y / 2-10, 20, 20); // note: from left bottom corner...

			//calculate ground depth.
			float gz = working_viewport->camera.position.z / (working_viewport->camera.position.z - working_viewport->camera.stare.z) * 
				glm::distance(working_viewport->camera.position, working_viewport->camera.stare);
			//gz = std::min(abs(std::max(working_viewport->camera.position.z,2.0f)) * 3, gz);
			auto d = std::min_element(starepnts, starepnts + 40, [](const glm::vec4& a, const glm::vec4& b) { return a.x < b.x; })->x;

			if (d < 0) {
				working_viewport->camera.stare = glm::normalize(working_viewport->camera.stare - working_viewport->camera.position) * gz + working_viewport->camera.position;
				working_viewport->camera.distance = gz;
				working_viewport->camera.stare.z = 0;
				return;
			}

			if (d < 0.5) d += 0.5;
			float ndc = d * 2.0 - 1.0;
			float z = (2.0 * cam_near * cam_far) / (cam_far + cam_near - ndc * (cam_far - cam_near)); // pointing mesh's depth.
			if (working_viewport->camera.ProjectionMode == 1)
			{
				z = cam_near + (cam_far - cam_near) * d;
			}

			DBG("update stare. d=%f, z=%f, gz=%f\n", d, z, gz);
			 
			if (gz > 0) {
				if (z < gz)
				{
					// set stare to mesh point.
					working_viewport->camera.stare = glm::normalize(working_viewport->camera.stare - working_viewport->camera.position) * z + working_viewport->camera.position;
					working_viewport->camera.distance = z;
				}
				else
				{
					working_viewport->camera.stare = glm::normalize(working_viewport->camera.stare - working_viewport->camera.position) * gz + working_viewport->camera.position;
					working_viewport->camera.distance = gz;
					working_viewport->camera.stare.z = 0;
				}
			}
		}
	}

}
void process_hoverNselection(int w, int h)
{
	// === hovering information === //todo: like click check 7*7 patch around the cursor.
	auto& wstate = working_viewport->workspace_state.back();
	std::vector<glm::vec4> hovering(49);
	int order[] = {
		24, 25, 32, 31, 30, 23, 16, 17, 18, 19, 26, 33, 40, 39, 38, 37, 36, 29, 22, 15, 8, 9, 10, 11, 12, 13, 20, 27,
		34, 41, 48, 47, 46, 45, 44, 43, 42, 35, 28, 21, 14, 7, 0, 1, 2, 3, 4, 5, 6
	};

	me_getTexFloats(working_graphics_state->TCIN, hovering.data(), working_viewport->mouseX() - 3, h - (working_viewport->mouseY() + 3), 7, 7);
	// note: from left bottom corner...

	working_viewport->hover_type = 0;
	working_viewport->hover_obj = nullptr;

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

			working_viewport->hover_obj = pointclouds.get(pcid);

			if ((pointclouds.get(pcid)->flag & (1<<8))!=0 || (pointclouds.get(pcid)->flag & (1<<7))!=0)
			{
				working_viewport->hover_type = 1;
				working_viewport->hover_instance_id = pcid;
				working_viewport->hover_node_id = pid;
			}
			continue;
		}
		else if (h.x > 999)
		{
			int class_id = int(h.x) - 1000;
			int instance_id = int(h.y) * 16777216 + (int)h.z;
			int node_id = int(h.w);
			mousePointingType = std::get<1>(gltf_classes.ls[class_id]);
			auto obj = gltf_classes.get(class_id)->showing_objects[working_viewport_id][instance_id];
			mousePointingInstance = obj->name;
			mousePointingSubId = node_id;

			working_viewport->hover_obj = obj;

			if ((obj->flags[working_viewport_id] & (1<<4))!=0){
				working_viewport->hover_type = class_id + 1000;
				working_viewport->hover_instance_id = instance_id;
				working_viewport->hover_node_id = -1;
			}
			if ((obj->flags[working_viewport_id] & (1<<5))!=0)
			{
				working_viewport->hover_type = class_id + 1000;
				working_viewport->hover_instance_id = instance_id;
				working_viewport->hover_node_id = node_id;
			}

			continue;
		}
		else if (h.x == 2)
		{
			// bunch of lines.
			int bid = h.y;
			int lid = h.z;

			working_viewport->hover_type = 2;
			working_viewport->hover_instance_id = bid;
			working_viewport->hover_node_id = lid;

			if (bid >= 0)
			{
				mousePointingType = "bunch";
				mousePointingInstance = std::get<1>(line_bunches.ls[bid]);
				mousePointingSubId = lid;
				
				working_viewport->hover_obj = std::get<0>(line_bunches.ls[bid]);
			}
			else
			{
				mousePointingType = "line_piece";
				mousePointingInstance = std::get<1>(line_pieces.ls[lid]);
				mousePointingSubId = -1;

				working_viewport->hover_obj = std::get<0>(line_pieces.ls[lid]);
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
			
			working_viewport->hover_type = 3;
			working_viewport->hover_instance_id = sid;
			working_viewport->hover_node_id = -1;
			
			working_viewport->hover_obj = std::get<0>(sprites.ls[sid]);

			continue;
		}else if (h.x == 4)
		{
			// world_ui
			int type = h.y;
			int sid = h.z;

			if (type == 1)
			{
				// handle
				mousePointingType = "ui-handle";
				mousePointingInstance = std::get<1>(handle_icons.ls[sid]);
				mousePointingSubId = -1;

				working_viewport->hover_type = 4; //handle
				working_viewport->hover_instance_id = sid;
				working_viewport->hover_node_id = -1;

				working_viewport->hover_obj = std::get<0>(handle_icons.ls[sid]);
			}else if (type==2)
			{
				// handle
				mousePointingType = "ui-text-line";
				mousePointingInstance = std::get<1>(text_along_lines.ls[sid]);
				mousePointingSubId = -1;

				working_viewport->hover_type = 5; //textline
				working_viewport->hover_instance_id = sid;
				working_viewport->hover_node_id = -1;

				working_viewport->hover_obj = std::get<0>(text_along_lines.ls[sid]);
			}
		}
	}


	if (ui.displayRenderDebug())
	{
		ImGui::Text("pointing:%s>%s.%d", mousePointingType.c_str(), mousePointingInstance.c_str(), mousePointingSubId);
	}

	// ==== UI State: Selecting ==========
	
    if (select_operation* sel_op = dynamic_cast<select_operation*>(wstate.operation); sel_op != nullptr)
    {
		if (sel_op->extract_selection)
		{
			sel_op->extract_selection = false;

			std::function<bool(glm::vec4)> process_pixel;
			if (ui.alt)
				process_pixel = [sel_op](glm::vec4 pix) -> bool
				{
					if (pix.x == 1)
					{
						if (sel_op->fine_select_pointclouds)
							return false;
						int pcid = pix.y;
						int pid = int(pix.z) * 16777216 + (int)pix.w;
						auto t = pointclouds.get(pcid);
						if (t->flag & (1 << 4))
						{
							if ((t->flag & (1 << 7)) && (t->flag & (1 << 6)))
							{
								t->flag &= ~(1 << 6);
								return true;
							}
							else if ((t->flag & (1 << 8)) && (t->flag & (1 << 9)))
							{
								if (t->capacity <= 0)
									return false;

								int dim = int(std::ceil(std::sqrt(t->capacity / 8.0f)));
								int total_bytes = dim * dim;
								if (total_bytes <= 0)
									return false;

								size_t byte_idx = size_t(pid) / 8;
								if (byte_idx >= size_t(total_bytes))
									return false;

								uint8_t bit_mask = uint8_t(1 << (pid % 8));
								if ((t->cpuSelection[byte_idx] & bit_mask) == 0)
									return false;

								t->cpuSelection[byte_idx] &= ~bit_mask;

								bool any_selected = false;
								for (int i = 0; i < total_bytes; ++i)
								{
									if (t->cpuSelection[i] != 0)
									{
										any_selected = true;
										break;
									}
								}
								if (!any_selected)
									t->flag &= ~(1 << 9);
								return true;
							}
						}
					}
					else if (pix.x == 2)
					{
						int bid = pix.y;
						if (bid < 0)
						{
							int lid = pix.z;
							auto t = line_pieces.get(lid);
							if ((t->flags[working_viewport_id] & (1 << 3)) != 0)
							{
								t->flags[working_viewport_id] &= ~(1 << 3);
								return true;
							}
						}
					}
					else if (pix.x == 3)
					{
						int sid = pix.y;
						auto sprite = sprites.get(sid);
						if ((sprite->per_vp_stat[working_viewport_id] & (1 << 1)) != 0)
						{
							sprite->per_vp_stat[working_viewport_id] &= ~(1 << 1);
							return true;
						}
					}
					else if (pix.x == 4)
					{
						int type = pix.y;
						int sid = pix.z;

						if (type == 1)
						{
							auto t = handle_icons.get(sid);
							if (t->selected[working_viewport_id])
							{
								t->selected[working_viewport_id] = false;
								return true;
							}
						}
					}
					else if (pix.x > 999)
					{
						int class_id = int(pix.x) - 1000;
						int instance_id = int(pix.y) * 16777216 + (int)pix.z;
						int node_id = int(pix.w);

						auto t = gltf_classes.get(class_id);
						auto obj = t->showing_objects[working_viewport_id][instance_id];
						if ((obj->flags[working_viewport_id] & (1 << 3)) != 0)
						{
							obj->flags[working_viewport_id] &= ~(1 << 3);
							return true;
						}
						else if ((obj->flags[working_viewport_id] & (1 << 5)) != 0 &&
							(obj->flags[working_viewport_id] & (1 << 6)) != 0 &&
							node_id >= 0 && node_id < obj->nodeattrs.size() &&
							((int(obj->nodeattrs[node_id].flag) & (1 << 3)) != 0))
						{
							obj->nodeattrs[node_id].flag = (int(obj->nodeattrs[node_id].flag) & ~(1 << 3));

							bool any_selected = false;
							for (auto& attr : obj->nodeattrs)
							{
								if ((int(attr.flag) & (1 << 3)) != 0)
								{
									any_selected = true;
									break;
								}
							}
							if (!any_selected)
								obj->flags[working_viewport_id] &= ~(1 << 6);
							return true;
						}
					}
					return false;
				};
			else
				process_pixel = [sel_op](glm::vec4 pix) -> bool
				{
					if (pix.x == 1)
					{
						if (sel_op->fine_select_pointclouds)
							return false;
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
					else if (pix.x == 2)
					{
						// select line piece.
						int bid = pix.y;
						if (bid<0)
						{
							int lid = pix.z;
							auto t = line_pieces.get(lid);
							if (t->flags[working_viewport_id] & (1 << 5))
								line_pieces.get(lid)->flags[working_viewport_id] |= 1 << 3;
							return true;
						}
					}
					else if (pix.x == 3)
					{
						// select sprite.
						int sid = pix.y;
						auto sprite = sprites.get(sid);
						if (sprite->per_vp_stat[working_viewport_id] & (1<<0))
						{
							sprite->per_vp_stat[working_viewport_id] |= (1 << 1);
							return true;
						}
					}else if (pix.x == 4)
					{
						// select world ui.
						int type = pix.y;
						int sid = pix.z;

						if (type == 1)
						{
							auto t = handle_icons.get(sid);
							if (t->selectable[working_viewport_id])
							{
								t->selected[working_viewport_id] = true;
								return true;
							}
						}
					}
					else if (pix.x > 999)
					{
						int class_id = int(pix.x) - 1000;
						int instance_id = int(pix.y) * 16777216 + (int)pix.z;
						int node_id = int(pix.w);

						auto t = gltf_classes.get(class_id);
						auto obj = t->showing_objects[working_viewport_id][instance_id];// t->objects.get(instance_id);
						if (obj->flags[working_viewport_id] & (1 << 4))
						{
							obj->flags[working_viewport_id] |= (1 << 3);
							return true;
						}
						else if (obj->flags[working_viewport_id] & (1 << 5))
						{
							obj->flags[working_viewport_id] |= (1 << 6);
							obj->nodeattrs[node_id].flag = ((int)obj->nodeattrs[node_id].flag | (1 << 3));
							return true;
						}
					}
					return false;
				};

			// if the drag mode is just clicking?
			if (sel_op->selecting_mode == click)
			{
				for (int i = 0; i < 49; ++i)
				{
					if (process_pixel(hovering[order[i]])) break;
				}
				if (sel_op->fine_select_pointclouds)
				{
					glm::mat4 viewMatrix = working_viewport->camera.GetViewMatrix();
					glm::mat4 projMatrix = working_viewport->camera.GetProjectionMatrix();
					glm::vec2 screenSize((float)w, (float)h);
					float mouseX = working_viewport->mouseX();
					float mouseY = working_viewport->mouseY();
					auto pointTest = [mouseX, mouseY](float sx, float sy)
					{
						return sx >= mouseX - 3.0f && sx <= mouseX + 3.0f &&
							   sy >= mouseY - 3.0f && sy <= mouseY + 3.0f;
					};
					fine_select_pointclouds(sel_op, ui.alt, pointTest, viewMatrix, projMatrix, screenSize);
				}
			}
			else if (sel_op->selecting_mode == drag)
			{
				hovering.resize(w * h);
				auto stx = std::min(working_viewport->mouseX(), sel_op->select_start_x);
				auto sty = std::max(working_viewport->mouseY(), sel_op->select_start_y);
				auto sw = std::abs(working_viewport->mouseX() - sel_op->select_start_x);
				auto sh = std::abs(working_viewport->mouseY() - sel_op->select_start_y);
				bool treatAsClick = sw < 3 && sh < 3;
				if (treatAsClick)
				{
					for (int i = 0; i < 49; ++i)
					{
						if (process_pixel(hovering[order[i]])) break;
					}
				}
				else {
					// todo: fetch full screen TCIN.
					me_getTexFloats(working_graphics_state->TCIN, hovering.data(), stx, h - sty, sw, sh);
					// note: from left bottom corner...
					for (int i = 0; i < sw * sh; ++i)
						process_pixel(hovering[i]);
				}
				if (sel_op->fine_select_pointclouds)
				{
					glm::mat4 viewMatrix = working_viewport->camera.GetViewMatrix();
					glm::mat4 projMatrix = working_viewport->camera.GetProjectionMatrix();
					glm::vec2 screenSize((float)w, (float)h);
					if (treatAsClick)
					{
						float mouseX = working_viewport->mouseX();
						float mouseY = working_viewport->mouseY();
						auto pointTest = [mouseX, mouseY](float sx, float sy)
						{
							return sx >= mouseX - 3.0f && sx <= mouseX + 3.0f &&
							       sy >= mouseY - 3.0f && sy <= mouseY + 3.0f;
						};
						fine_select_pointclouds(sel_op, ui.alt, pointTest, viewMatrix, projMatrix, screenSize);
					}
					else
					{
						float rectMinX = stx;
						float rectMaxX = stx + sw;
						float rectMaxY = sty;
						float rectMinY = rectMaxY - sh;
						auto rectTest = [rectMinX, rectMaxX, rectMinY, rectMaxY](float sx, float sy)
						{
							return sx >= rectMinX && sx <= rectMaxX &&
							       sy >= rectMinY && sy <= rectMaxY;
						};
						fine_select_pointclouds(sel_op, ui.alt, rectTest, viewMatrix, projMatrix, screenSize);
					}
				}
			}
			else if (sel_op->selecting_mode == paint)
			{
				hovering.resize(w * h);
				// todo: fetch full screen TCIN.
				me_getTexFloats(working_graphics_state->TCIN, hovering.data(), 0, 0, w, h);
				for (int j = 0; j < h; ++j)
					for (int i = 0; i < w; ++i)
						if (sel_op->painter_data[(j / 4) * (w / 4) + (i / 4)] > 0)
							process_pixel(hovering[(h - j - 1) * w + i]);
				if (sel_op->fine_select_pointclouds && !sel_op->painter_data.empty())
				{
					glm::mat4 viewMatrix = working_viewport->camera.GetViewMatrix();
					glm::mat4 projMatrix = working_viewport->camera.GetProjectionMatrix();
					glm::vec2 screenSize((float)w, (float)h);
					int painterW = std::max(1, (w + 3) / 4);
					int painterH = std::max(1, (h + 3) / 4);
					auto* painterData = sel_op->painter_data.data();
					auto paintTest = [painterData, painterW, painterH, w, h](float sx, float sy)
					{
						if (!(sx >= 0.0f && sy >= 0.0f && sx < static_cast<float>(w) && sy < static_cast<float>(h)))
							return false;
						int ix = std::clamp(static_cast<int>(sx), 0, w - 1);
						int iy = std::clamp(static_cast<int>(sy), 0, h - 1);
						int cellX = std::clamp(ix / 4, 0, painterW - 1);
						int cellY = std::clamp(iy / 4, 0, painterH - 1);
						int idx = cellY * painterW + cellX;
						return painterData[idx] > 0;
					};
					fine_select_pointclouds(sel_op, ui.alt, paintTest, viewMatrix, projMatrix, screenSize);
				}
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

void BeforeDrawAny()
{
	// also do any expensive precomputations here.

	// Reset chord trigger tracking for new frame
	ui.lastChordTriggered = ui.thisChordTriggered;
	ui.thisChordTriggered.clear();

	for (int i = 0; i < argb_store.rgbas.ls.size(); ++i)
		argb_store.rgbas.get(i)->occurrence = 0;

	// perform reading here, so all the draw is already completed.
	for (int i=0; i<MAX_VIEWPORTS; ++i){
		if (!ui.viewports[i].active) continue;
		switch_context(i);
		if (i == 0)
		{
			int w = ui.viewports[0].disp_area.Size.x;
			int h = ui.viewports[0].disp_area.Size.y;
			// todo: to simplify, we only read out mainviewport.

			// if (working_viewport != ui.viewports) return;
			// Operations that requires read from rendered frame, slow... do them after all render have safely done.
			// === what rgb been viewed? how much pix?
			if (working_viewport->frameCnt > 60 && argb_store.rgbas.ls.size()>0)
			{
				occurences_readout(w, h);
				// printf("\n");
			}
		}
		process_hoverNselection(working_viewport->disp_area.Size.x, working_viewport->disp_area.Size.y);
	}

	for (int i = 0; i < argb_store.rgbas.ls.size(); ++i)
	{
		auto rgba_ptr = argb_store.rgbas.get(i);
		if (rgba_ptr->streaming && rgba_ptr->atlasId != -1)
		{
			auto ptr = GetStreamingBuffer(argb_store.rgbas.getName(i), rgba_ptr->width, rgba_ptr->height);
			me_update_rgba_atlas(argb_store.atlas, rgba_ptr->atlasId,
				(int)(rgba_ptr->uvStart.x), (int)(rgba_ptr->uvEnd.y), rgba_ptr->height, rgba_ptr->width, ptr
				, SG_PIXELFORMAT_RGBA8);
			//printf("streaming first argb=(%x%x%x%x)\n", ptr[0], ptr[1], ptr[2], ptr[3]);
			rgba_ptr->loaded = true;
			rgba_ptr->loadLoopCnt = ui.loopCnt;
		}
	}
}

void skip_imgui_render(const ImDrawList* im_draws, const ImDrawCmd* im_draw_cmd)
{
	// nothing.
}
int monitorX, monitorY, monitorWidth, monitorHeight;
char* monitorName;

void GenMonitorInfo()
{
    auto window = (GLFWwindow*)ImGui::GetMainViewport()->PlatformHandle;
	int windowX, windowY, windowWidth, windowHeight;
	
    GLFWmonitor* monitor = glfwGetWindowMonitor(window);

    // If the window is not already fullscreen, find the monitor
    if (!monitor)
    {
        glfwGetWindowPos(window, &windowX, &windowY);
        glfwGetWindowSize(window, &windowWidth, &windowHeight);

        int monitorCount;
        GLFWmonitor** monitors = glfwGetMonitors(&monitorCount);

        for (int i = 0; i < monitorCount; i++)
        {
	        glfwGetMonitorWorkarea(monitors[i], &monitorX, &monitorY, &monitorWidth, &monitorHeight);

	        if (windowX < monitorX + monitorWidth && windowX + windowWidth > monitorX &&
		        windowY < monitorY + monitorHeight && windowY + windowHeight > monitorY)
	        {
				monitorName = (char*)glfwGetMonitorName(monitors[i]);
		        return;
	        }
        }
    }

	if (monitor){
		monitorName = (char*)glfwGetMonitorName(monitor);
		glfwGetMonitorWorkarea(monitor, &monitorX, &monitorY, &monitorWidth, &monitorHeight);
	}
}



glm::mat4 last_iv; //turd on shit mountain.

glm::vec3 get_ETH_viewing_eye()
{
	if (working_graphics_state->ETH_display.eye_id % 2 == 0)
	{
		//left eye.
		return shared_graphics.ETH_display.left_eye_world;
	}

	return shared_graphics.ETH_display.right_eye_world;
	//right eye.
}
void DefaultRenderWorkspace(disp_area_t disp_area, ImDrawList* dl)
{
	TOC("render_start")
	auto cmd_st = dl->CmdBuffer.Size;
	dl->AddDrawCmd();

	auto w = disp_area.Size.x;
	auto h = disp_area.Size.y;

	for (int i = 0; i < global_name_map.ls.size(); ++i)
		global_name_map.get(i)->obj->compute_pose();

	TOC("compute_pose")

	camera_manip();

	auto& wstate = working_viewport->workspace_state.back();

	// we don't reapply workspace for each viewport because we use viewport-specific flags.
	
	// actually all the pixels are already ready by this point, but we move sprite occurences to the end for webgl performance.

	// draw
	working_viewport->camera.Resize(w, h);
	working_viewport->camera.UpdatePosition();

	auto vm = working_viewport->camera.GetViewMatrix();
	auto pm = working_viewport->camera.GetProjectionMatrix();
	auto campos = working_viewport->camera.getPos();
	auto camstare = working_viewport->camera.getStare();

	if (working_viewport->displayMode == viewport_state_t::EyeTrackedHolography ||
		working_viewport->displayMode == viewport_state_t::EyeTrackedHolography2)
	{
		auto eye_pos_screen_world = get_ETH_viewing_eye();

		auto screen_matrix = vm;
		auto monitor_world_sz = grating_params.screen_size_physical_mm / grating_params.world2phy;
		glm::mat4 eyeTransform = glm::translate(glm::mat4(1.0f), -eye_pos_screen_world);

		

		// Calculate physical size of display area in mm
		auto disp_area_world_sz = monitor_world_sz * glm::vec2(disp_area.Size.x * grating_disp_fac / (float)monitorWidth, disp_area.Size.y * grating_disp_fac / (float)monitorHeight);

		auto disp_lt_screen_world = glm::vec2((disp_area.Pos.x - monitorX), -(disp_area.Pos.y - monitorY)) / glm::vec2(monitorWidth, monitorHeight) * monitor_world_sz + glm::vec2(-monitor_world_sz.x / 2, monitor_world_sz.y / 2);

		vm = eyeTransform * screen_matrix;

		auto cnear = cam_near;
		auto fac = cnear / eye_pos_screen_world.z;;

		//eye_pos_screen_world = glm::vec3(0); //debug...
		pm = glm::frustum(
			fac * (disp_lt_screen_world.x - eye_pos_screen_world.x),
			fac * (disp_lt_screen_world.x + disp_area_world_sz.x - eye_pos_screen_world.x),
			fac * (disp_lt_screen_world.y - disp_area_world_sz.y - eye_pos_screen_world.y),
			fac * (disp_lt_screen_world.y - eye_pos_screen_world.y),
			cnear, cam_far);
		glm::mat3 rotation = glm::mat3(vm);
		glm::vec3 translation = glm::vec3(vm[3]);
		campos = -glm::transpose(rotation) * translation;
	}

	auto invVm = last_iv = glm::inverse(vm);
	auto invPm = glm::inverse(pm);

	auto pv = pm * vm;

	if (working_graphics_state->disp_area.Size.x!=w ||working_graphics_state->disp_area.Size.y!=h)
	{
		if (working_graphics_state->inited)
			ResetEDLPass();
		GenPasses(w, h);

		// patch for select_operation.
		if (select_operation* sel_op = dynamic_cast<select_operation*>(wstate.operation); sel_op != nullptr){
			sel_op->painter_data.resize(int(std::ceil(w / 4.0f) * std::ceil(h / 4.0f)));
			std::fill(sel_op->painter_data.begin(), sel_op->painter_data.end(), 0);
		}
	}
	working_graphics_state->disp_area = working_viewport->disp_area = disp_area;
	
	TOC("resz");

	//===

	working_graphics_state->use_paint_selection = false;
	int useFlag;
	//

	// draw spot texts:
	for (int i = 0; i < spot_texts.ls.size(); ++i)
	{
		auto t = spot_texts.get(i);
		if (!t->show[working_viewport_id]) continue;
		// Apply prop display mode filtering
		if (!viewport_test_prop_display(t)) continue;

		for (int j=0; j<t->texts.size(); ++j)
		{
			auto& ss = t->texts[j];
			glm::vec2 pos(0,0);
			// Compute world position with rotation-aware relative transform
			if ((ss.header & (1 << 0)) != 0) {
				bool hasRelative = (ss.header & (1 << 4)) != 0;
				glm::vec3 basePos = hasRelative && ss.relative.obj ? ss.relative.obj->current_pos : t->current_pos;
				glm::quat baseRot = hasRelative && ss.relative.obj ? ss.relative.obj->current_rot : t->current_rot;
				// Rotate local offset by base rotation, then translate
				glm::vec3 rotated = glm::vec3(glm::mat4_cast(baseRot) * glm::vec4(ss.position, 0.0f));
				pos = world2pixel(basePos + rotated, vm, pm, glm::vec2(w, h));
			} 

			// screen coord from top-left to bottom-right.
			if (ss.header & (1<<1)){
				pos.x += ss.ndc_offset.x*w; //uv_offset.
				pos.y += ss.ndc_offset.y*h;
			}
			if (ss.header & (1<<2))
			{
				pos += ss.pixel_offset * working_viewport->camera.dpi;
			}
			if (ss.header & (1<<3))
			{
				auto sz = ImGui::CalcTextSize(ss.text.c_str());
				pos.x -= sz.x * ss.pivot.x;
				pos.y -= sz.y * ss.pivot.y;
			}
			dl->AddText(ImVec2(disp_area.Pos.x + pos.x, disp_area.Pos.y + pos.y), t->texts[j].color, t->texts[j].text.c_str());
		}
	}

	ImGuizmo::SetOrthographic(false);
	ImGuizmo::SetDrawlist(dl);
	ImGuizmo::SetRect(disp_area.Pos.x, disp_area.Pos.y, w, h);
	ImGuizmo::SetGizmoSizeClipSpace(120.0f * working_viewport->camera.dpi / w);

	// special object handling: me::mouse
	{
		auto dispW = working_viewport->disp_area.Size.x;
		auto dispH = working_viewport->disp_area.Size.y;

		// Calculate the inverse of the projection-view matrix
		auto invPV = glm::inverse(pm * vm);

		// Normalize mouse coordinates to NDC space (-1 to 1)
		float ndcX = (2.0f * working_viewport->mouseX()) / dispW - 1.0f;
		float ndcY = 1.0f - (2.0f * working_viewport->mouseY()) / dispH; // Flip Y coordinate

		// Create a ray in NDC space
		glm::vec4 rayNDC = glm::vec4(ndcX, ndcY, -1.0f, 1.0f);

		// Transform the ray to world space
		glm::vec4 rayWorld = invPV * rayNDC;
		rayWorld /= rayWorld.w;

		// Ray origin and direction in world space
		glm::vec3 rayOrigin = working_viewport->camera.getPos();
		glm::vec3 rayDir = glm::normalize(glm::vec3(rayWorld) - rayOrigin);

		glm::vec3 intersection;
		bool validIntersection = false;

		if (wstate.pointer_mode == 0) { // operational plane mode - intersect with operational plane
			// Calculate the operational plane normal from the unit vectors
			glm::vec3 unitX = glm::normalize(wstate.operationalGridUnitX);
			glm::vec3 unitY = glm::normalize(wstate.operationalGridUnitY);
			glm::vec3 planeNormal = glm::normalize(glm::cross(unitX, unitY));

			// Use the operational grid pivot as the plane point
			glm::vec3 planePoint = wstate.operationalGridPivot;

			// Intersect ray with the operational plane
			float intersectionDistance;
			validIntersection = glm::intersectRayPlane(rayOrigin, rayDir, planePoint, planeNormal, intersectionDistance);
			if (validIntersection && intersectionDistance > 0) {
				intersection = rayOrigin + intersectionDistance * rayDir;
			}
			else {
				validIntersection = false;
			}
			wstate.valid_pointing = validIntersection;
		}
		else if (wstate.pointer_mode == 1){ // View Plane mode - intersect with view plane at operationalGridPivot
			glm::vec3 planeNormal = glm::normalize(working_viewport->camera.stare - working_viewport->camera.position);
			float intersectionDistance;
			validIntersection = glm::intersectRayPlane(rayOrigin, rayDir, wstate.operationalGridPivot, planeNormal, intersectionDistance);
			if (validIntersection && intersectionDistance > 0) {
				intersection = rayOrigin + intersectionDistance * rayDir;
			}
			else {
				validIntersection = false;
			}
			wstate.valid_pointing = validIntersection;
		}

		if (wstate.valid_pointing) {
			mouse_object->target_position = mouse_object->previous_position = wstate.pointing_pos = intersection;
		}
	}

	if (wstate.operation != nullptr) {
		wstate.operation->draw(disp_area, dl, vm, pm);
	}

	sg_reset_state_cache(); 

	static bool draw_3d = true, compose = true;
	if (ui.displayRenderDebug()) {
		// ImGui::Checkbox("draw_3d", &draw_3d);
		// ImGui::Checkbox("compose", &compose);
		ImGui::Checkbox("useEDL", &wstate.useEDL);
		// ImGui::Checkbox("useSSAO", &wstate.useSSAO);
		ImGui::Checkbox("useGround", &wstate.useGround);
		// ImGui::Checkbox("useShineBloom", &wstate.useBloom);
		// ImGui::Checkbox("useBorder", &wstate.useBorder);
	}
	
	TOC("pre-draw")


	int instance_count=0, node_count = 0;
	bool any_walkable = false;
	if (draw_3d){
		// gltf transform to get mats.
		std::vector<int> renderings;
		int transparent_objects_N = 0;

		if (!gltf_classes.ls.empty()) {

			for (int i = 0; i < gltf_classes.ls.size(); ++i)
			{
				auto t = gltf_classes.get(i);
				t->instance_offset = instance_count;
				instance_count += t->list_objects();
				renderings.push_back(node_count);
				node_count += t->count_nodes();
				transparent_objects_N += t->has_blending_material ? t->showing_objects[working_viewport_id].size() : t->showing_objects[working_viewport_id].size() - t->opaques;
			}
			
			TOC("cnt")
			if (node_count != 0) {
				static std::vector<s_pernode> per_node_meta;
				static std::vector<s_perobj> per_object_meta;

				int objmetah1 = (int)(ceil(node_count / 2048.0f)); //4096 width, stride 2 per node.
				int size1 = 4096 * objmetah1 * 32; //RGBA32F*2, 8floats=32B sizeof(s_transrot)=32.
				per_node_meta.resize(size1);

				int objmetah2 = (int)(ceil(instance_count / 4096.0f)); // stride 1 per instance.
				int size2 = 4096 * objmetah2 * 16; //4 uint: animationid|start time.
				per_object_meta.resize(size2);

				for (int i = 0; i < gltf_classes.ls.size(); ++i)
				{
					auto t = gltf_classes.get(i);
					if (t->showing_objects[working_viewport_id].empty()) continue;
					t->prepare_data(per_node_meta, per_object_meta, renderings[i], t->instance_offset);
				}
				
				TOC("prepare")
				// update really a lot of data...
				{
					updateTextureW4K(shared_graphics.instancing.node_meta, objmetah1, per_node_meta.data(), SG_PIXELFORMAT_RGBA32F);
					updateTextureW4K(shared_graphics.instancing.instance_meta, objmetah2, per_object_meta.data(), SG_PIXELFORMAT_RGBA32SI);
				}
				TOC("ud")

				//███ Compute node localmat: Translation Rotation on instance, node and Animation. note: we don't do hierarchy because webgl doesn't support barrier.
				sg_begin_pass(shared_graphics.instancing.animation_pass, shared_graphics.instancing.pass_action);
				sg_apply_pipeline(shared_graphics.instancing.animation_pip);
				for (int i = 0; i < gltf_classes.ls.size(); ++i)
				{
					auto t = gltf_classes.get(i);
					if (t->showing_objects[working_viewport_id].empty()) continue;
					t->compute_node_localmat(vm, renderings[i]); // also multiplies view matrix.
				}
				sg_end_pass();

				//███ Propagate node hierarchy, we propagate 2 times in a group, one time at most depth 4.
				// sum{n=1~l}{4^n*C_l^n} => 4, 24|, 124, 624|.
				if (ui.displayRenderDebug())
				{
					ImGui::Text("gltf passes=%d", gltf_class::max_passes);
				}
				for (int i = 0; i < int(ceil(gltf_class::max_passes / 2.0f)); ++i) {
					sg_begin_pass(shared_graphics.instancing.hierarchy_pass1, shared_graphics.instancing.hierarchy_pass_action);
					sg_apply_pipeline(shared_graphics.instancing.hierarchy_pip);
					for (int j = 0; j < gltf_classes.ls.size(); ++j)
					{
						auto t = gltf_classes.get(j);
						if (t->showing_objects[working_viewport_id].empty()) continue;
						t->node_hierarchy(renderings[j], i * 2);
					}
					sg_end_pass();

					sg_begin_pass(shared_graphics.instancing.hierarchy_pass2, shared_graphics.instancing.hierarchy_pass_action);
					sg_apply_pipeline(shared_graphics.instancing.hierarchy_pip);
					for (int j = 0; j < gltf_classes.ls.size(); ++j)
					{
						auto t = gltf_classes.get(j);
						if (t->showing_objects[working_viewport_id].empty()) continue;
						t->node_hierarchy(renderings[j], i * 2 + 1);
					}
					sg_end_pass();
				}
				TOC("propagate")
				
				sg_begin_pass(shared_graphics.instancing.final_pass, shared_graphics.instancing.pass_action);
				sg_apply_pipeline(shared_graphics.instancing.finalize_pip);
				// compute inverse of node viewmatrix.
				sg_apply_bindings(sg_bindings{
					.vertex_buffers = {
						//graphics_state.instancing.Z // actually nothing required.
					},
					.vs_images = {
						shared_graphics.instancing.objInstanceNodeMvMats1
					}
					});
				sg_draw(0, node_count, 1);
				sg_end_pass();

				//
			}
		}
		TOC("hierarchy")

		// first draw point clouds, so edl only reference point's depth => pc_depth.
		sg_begin_pass(working_graphics_state->pc_primitive.pass, &working_graphics_state->pc_primitive.pass_action);
		sg_apply_pipeline(shared_graphics.point_cloud_simple_pip);
		// should consider if pointcloud should draw "sprite" handle.

		long drawnpc = 0;
		for (int i=0; i<pointclouds.ls.size(); ++i)
		{
			// todo: perform some culling?
			auto t = pointclouds.get(i);
			if (t->n == 0) continue;
			if (!t->show[working_viewport_id]) continue;
			// Apply prop display mode filtering
			if (!viewport_test_prop_display(t)) continue;

			if (t->pc_type == 1) { any_walkable = true; }
			
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
			if (working_viewport->hover_type == 1 && working_viewport->hover_instance_id == i) {
				if ((t->flag & (1 << 7)) != 0)
					displaying |= (1 << 4);
				else if ((t->flag & (1 << 8)) != 0)
					hovering_pcid = working_viewport->hover_node_id;
			}

			sg_apply_bindings(sg_bindings{ .vertex_buffers = {t->pcBuf, t->colorBuf}, .vs_images = {t->pcSelection} });
			vs_params_t vs_params{
				.mvp = pv * translate(glm::mat4(1.0f), t->current_pos) * mat4_cast(t->current_rot) ,
				.dpi = working_viewport->camera.dpi ,
				.pc_id = i,
				.projection_mode = working_viewport->camera.ProjectionMode,
				.displaying = displaying,
				.hovering_pcid = hovering_pcid,
				.shine_color_intensity = t->shine_color,
				.hover_shine_color_intensity = wstate.hover_shine,
				.selected_shine_color_intensity = wstate.selected_shine,
			};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_vs_params, SG_RANGE(vs_params));
			sg_draw(0, t->n, 1);
			drawnpc += t->n; 
		}
		sg_end_pass();

		// --- edl lo-res pass
		if (wstate.useEDL && drawnpc > 0) {
			sg_begin_pass(working_graphics_state->edl_lres.pass, &shared_graphics.edl_lres.pass_action);
			sg_apply_pipeline(shared_graphics.edl_lres.pip);
			sg_apply_bindings(working_graphics_state->edl_lres.bind);
			depth_blur_params_t edl_params{ .kernelSize = 5, .scale = 1, .pnear = cam_near, .pfar = cam_far };
			sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(edl_params));
			sg_draw(0, 4, 1);
			sg_end_pass();
		}
		
		TOC("ptc")
		// Build walkable cache if needed
		if (any_walkable) {
			std::vector<float> walkable_cache_cpu;
			walkable_cache_cpu.resize(2048 * 268 * 4);
			// clear
			memset(walkable_cache_cpu.data(), 0, walkable_cache_cpu.size() * sizeof(float));

			// Collect local-maps only (local_map carries angular bins)
			std::vector<int> local_ids; local_ids.reserve(pointclouds.ls.size());
			for (int i = 0; i < pointclouds.ls.size(); ++i) {
				auto t = pointclouds.get(i);
				if (!t->show[working_viewport_id]) continue;
				if (t->pc_type == 1) local_ids.push_back(i);
			}
			// Shuffle local_ids for randomized processing
			// std::random_device rd;
			// std::mt19937 g(rd());
			// std::shuffle(local_ids.begin(), local_ids.end(), g);

			if (!local_ids.empty()) {
				// 0..2 rows: 3-tier hash, each texel: RGBA32F packs up to 4 indices (float bits store integer indices)
				auto hash_index = [](int xq, int yq) -> int {
					return (xq * 997 + yq) & 0x7ff;
					int a = yq * 4761041 + 4848659;
					int b = xq * 6595867 + 3307781;
					int ah = a % 5524067;
					int bh = b % 7237409;
					return (int)(abs(ah + bh) % 2048LL);
				};

				struct Slot { int ids[4]; int n; Slot(){ids[0]=ids[1]=ids[2]=ids[3]=-1; n=0;} };
				std::vector<Slot> hash_lv1(2048), hash_lv2(2048), hash_lv3(2048), hash_lv4(2048);

				auto push_slot = [](Slot& s, int id){ if (s.n < 4) s.ids[s.n++] = id; };
				// quantization for 4 radii
				auto push_local = [&](const glm::vec3& p, int lid){
					// 4-tier grid: 3m, 10m, 30m, 90m
					int x1 = (int)roundf(p.x / 3),  y1 = (int)roundf(p.y / 3);
					int x2 = (int)roundf(p.x / 10), y2 = (int)roundf(p.y / 10);
					int x3 = (int)roundf(p.x / 30), y3 = (int)roundf(p.y / 30);
					int x4 = (int)roundf(p.x / 90), y4 = (int)roundf(p.y / 90);
					push_slot(hash_lv1[hash_index(x1,y1)], lid);
					push_slot(hash_lv2[hash_index(x2,y2)], lid);
					push_slot(hash_lv3[hash_index(x3,y3)], lid);
					push_slot(hash_lv4[hash_index(x4,y4)], lid);
				};
				for (int lid : local_ids) {
					auto t = pointclouds.get(lid);
					push_local(t->current_pos, lid);
				}

				// build 8 rows (0..3 hashes, 4..7 meta) and upload in one call
				std::vector<float> rows;
				rows.resize(12 * 2048 * 4, 0.0f);
				for (int i = 0; i < 2048; ++i) {
					float* r0 = &rows[(0 * 2048 + i) * 4];
					float* r1 = &rows[(1 * 2048 + i) * 4];
					float* r2 = &rows[(2 * 2048 + i) * 4];
					float* r3 = &rows[(3 * 2048 + i) * 4];
					auto wslot = [&](Slot& s, float* row){
						row[0] = (float)(s.n > 0 ? s.ids[0] : -1);
						row[1] = (float)(s.n > 1 ? s.ids[1] : -1);
						row[2] = (float)(s.n > 2 ? s.ids[2] : -1);
						row[3] = (float)(s.n > 3 ? s.ids[3] : -1);
					};
					wslot(hash_lv1[i], r0);
					wslot(hash_lv2[i], r1);
					wslot(hash_lv3[i], r2);
					wslot(hash_lv4[i], r3);
				}
				// rows 4..11: local-map xytheta for up to 16384 entries (8 rows of 2048)
				for (int n = 0; n < (int)local_ids.size() && n < 16384; ++n) {
					int lid = local_ids[n];
					auto t = pointclouds.get(lid);
					float* px = &rows[((4 + (n / 2048)) * 2048 + (n % 2048)) * 4];
					px[0] = t->current_pos.x;
					px[1] = t->current_pos.y;
					glm::vec3 euler = glm::eulerAngles(t->current_rot);
					px[2] = euler.z; // theta
					px[3] = 0.0f;
				}
				// upload 12 rows at y=0..11 in a single call
				texUpdatePartial(shared_graphics.walkable_cache, 0, 0, 2048, 12, rows.data(), SG_PIXELFORMAT_RGBA32F);

				// do NOT touch angular bin rows here; they are updated only on add/remove
			}
		}

		// actual gltf rendering.

		if (node_count!=0) {
			sg_begin_pass(working_graphics_state->primitives.pass, &working_graphics_state->primitives.pass_action);

			auto pip = _sg_lookup_pipeline(&_sg.pools, shared_graphics.gltf_pip.id);
			if (wstate.activeClippingPlanes)
				pip->gl.cull_mode = SG_CULLMODE_NONE;
			else
				pip->gl.cull_mode = SG_CULLMODE_BACK;

			sg_apply_pipeline(shared_graphics.gltf_pip);

			for (int i = 0; i < gltf_classes.ls.size(); ++i) {
				auto t = gltf_classes.get(i);
				if (t->showing_objects[working_viewport_id].empty()) continue;
				if (t->dbl_face && !wstate.activeClippingPlanes) //currently back cull.
					setFaceCull(false);
				t->render(vm, pm, invVm, false, renderings[i], i);
				
				if (t->dbl_face && !wstate.activeClippingPlanes) //currently back cull.
					setFaceCull(true);
			}

			sg_end_pass();
		}

		TOC("gltf")

		// todo: 
		// geometries.
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
			if (!bunch->show[working_viewport_id]) continue;
			if (!viewport_test_prop_display(bunch)) continue;

			if (bunch->n>0)
			{
				if (!lbinited)
				{
					sg_begin_pass(working_graphics_state->line_bunch.pass, &shared_graphics.line_bunch.pass_action);
					sg_apply_pipeline(shared_graphics.line_bunch.line_bunch_pip);
					lbinited = true;
				}

				sg_apply_bindings(sg_bindings{ .vertex_buffers = {bunch->line_buf}, .fs_images = {} });
				line_bunch_params_t lb{
					.mvp = pv * translate(glm::mat4(1.0f), bunch->current_pos) * mat4_cast(bunch->current_rot),
					.dpi = working_viewport->camera.dpi, .bunch_id = i,
					.screenW = (float)working_viewport->disp_area.Size.x,
					.screenH = (float)working_viewport->disp_area.Size.y,
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
				sg_begin_pass(working_graphics_state->line_bunch.pass, &shared_graphics.line_bunch.pass_action);
				sg_apply_pipeline(shared_graphics.line_bunch.line_bunch_pip);
				lbinited = true;
			}
			std::vector<gpu_line_info> info;
			info.reserve(line_pieces.ls.size());

			for (int i=0; i<line_pieces.ls.size(); ++i)
			{
				auto t = line_pieces.get(i);
				if (!t->show[working_viewport_id]) continue;
				// Apply prop display mode filtering
				if (!viewport_test_prop_display(t)) continue;

				auto tmp = t->attrs;
				if (working_viewport->hover_type == 2 && working_viewport->hover_instance_id == -1 
					&& working_viewport->hover_node_id == i && (tmp.flags&(1<<5)!=0))
					tmp.flags |= (1 << 4);
				tmp.f_lid = (float)i;
				tmp.flags = t->flags[working_viewport_id];

				if (t->type == me_line_piece::straight || 
					t->type == me_line_piece::bezier && t->ctl_pnt.size()<1) { // fallback.
					if (t->propSt.obj != nullptr)
						tmp.st = t->propSt.obj->current_pos;
					if (t->propEnd.obj != nullptr)
						tmp.end = t->propEnd.obj->current_pos;
					info.push_back(tmp);
				}else if (t->type == me_line_piece::bezier)
				{
					// Compute 8 segments for the bezier curve using CPU
					// A cubic bezier curve requires 4 points: start, 2 control points, end
					// At least one control point is needed, if not fallback to straight line.
					
					// Use the start and end points from attrs if available
					// If propSt/propEnd are defined, use their current positions
					glm::vec3 start_point = t->propSt.obj != nullptr ? t->propSt.obj->current_pos : t->attrs.st;
					glm::vec3 end_point = t->propEnd.obj != nullptr ? t->propEnd.obj->current_pos : t->attrs.end;
					
					// Prepare the 4 points needed for a cubic bezier curve
					std::vector<glm::vec3> curve_points;
					curve_points.push_back(start_point);
					
					// Add control points with appropriate handling based on count
					if (t->ctl_pnt.size() == 1) {
						// With only one control point, we'll create a quadratic-like curve
						// by placing two control points at 1/3 and 2/3 along the line to the control point
						glm::vec3 cp = t->ctl_pnt[0];
						glm::vec3 cp1 = start_point + (cp - start_point) * 0.75f;
						glm::vec3 cp2 = end_point + (cp - end_point) * 0.75f;
						curve_points.push_back(cp1);
						curve_points.push_back(cp2);
					} else if (t->ctl_pnt.size() == 2) {
						// Exactly two control points - ideal case for cubic bezier
						curve_points.push_back(t->ctl_pnt[0]);
						curve_points.push_back(t->ctl_pnt[1]);
					} else {
						// More than two control points - use the first two
						curve_points.push_back(t->ctl_pnt[0]);
						curve_points.push_back(t->ctl_pnt[1]);
					}
					
					curve_points.push_back(end_point);
					
					// Now we should have exactly 4 points for a cubic bezier
					assert(curve_points.size() == 4);
					
					// Reference the points for more readable code
					const glm::vec3& P0 = curve_points[0];
					const glm::vec3& P1 = curve_points[1];
					const glm::vec3& P2 = curve_points[2];
					const glm::vec3& P3 = curve_points[3];
					
					// Compute cubic bezier curve segments
					glm::vec3 prev_point = P0; // Start with the first point

					// todo: make segments computation on GPU.
					const int segments = 64; // Number of line segments to approximate the curve
					
					// For each segment
					for (int j = 1; j <= segments; j++) {
						// Calculate t parameter for this segment (0.0 to 1.0)
						float t1 = static_cast<float>(j) / segments;
						float t2 = t1 * t1;       // t^2
						float t3 = t2 * t1;      // t^3
						float mt = 1.0f - t1;    // (1-t)
						float mt2 = mt * mt;    // (1-t)^2
						float mt3 = mt2 * mt;   // (1-t)^3
						
						// Cubic Bezier formula:
						// B(t) = (1-t)^3 * P0 + 3(1-t)^2 * t * P1 + 3(1-t) * t^2 * P2 + t^3 * P3
						// Where:
						// - P0 is the start point
						// - P1, P2 are control points
						// - P3 is the end point
						// - t ranges from 0 to 1
						glm::vec3 bezier_point = 
							mt3 * P0 +               // (1-t)^3 * P0
							3.0f * mt2 * t1 * P1 +    // 3(1-t)^2 * t * P1
							3.0f * mt * t2 * P2 +    // 3(1-t) * t^2 * P2
							t3 * P3;                 // t^3 * P3
						
						// Create a line segment from previous point to current bezier point
						auto seg_info = tmp;
						seg_info.st = prev_point;
						seg_info.end = bezier_point;
						
						// Add segment to the list
						info.push_back(seg_info);
						
						// Update previous point for next segment
						prev_point = bezier_point;
					}
				}
			}
			if (info.size() > 0) {
				auto sz = info.size() * sizeof(gpu_line_info);
				auto buf = sg_make_buffer(sg_buffer_desc{ .size = sz, .data = {info.data(), sz} });
				sg_apply_bindings(sg_bindings{ .vertex_buffers = {buf}, .fs_images = {} });

				line_bunch_params_t lb{
					.mvp = pv,
					.dpi = working_viewport->camera.dpi,
					.bunch_id = -1,
					.screenW = (float)working_viewport->disp_area.Size.x,
					.screenH = (float)working_viewport->disp_area.Size.y,
					.displaying = 0,
					//.hovering_pcid = hovering_pcid,
					//.shine_color_intensity = bunch->shine_color,
					.hover_shine_color_intensity = wstate.hover_shine,
					.selected_shine_color_intensity = wstate.selected_shine,
				};
				sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_line_bunch_params, SG_RANGE(lb));
				sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_line_bunch_params, SG_RANGE(lb));
				sg_draw(0, 9, info.size());
				sg_destroy_buffer(buf);
			}
		}

		if (lbinited)
		{
			sg_end_pass();
		}
		
		TOC("pieces")

		// draw sprites: must be the last (or depth will bad).
		std::vector<gpu_sprite> sprite_params;
		sprite_params.reserve(sprites.ls.size());
		for(int i=0; i<sprites.ls.size(); ++i)
		{
			auto s = sprites.get(i);
			if (s->type!= me_sprite::sprite_type::rgba_t) continue;
			if (!s->show[working_viewport_id]) continue;
			// Apply prop display mode filtering
			if (!viewport_test_prop_display(s)) continue;

			auto show_hover = working_viewport->hover_type == 3 && working_viewport->hover_instance_id == i && (s->per_vp_stat[working_viewport_id] & (1 << 0)) ? 1 << 4 : 0;
			auto show_selected = (s->per_vp_stat[working_viewport_id] & (1<<1)) ? 1 << 3 : 0;

			auto quat = s->current_rot;
			auto uvtl = s->rgba->uvStart;
			auto uvrb = s->rgba->uvEnd;
			if (s->rgba->type == 1)
			{
				// stereo 3d lr type, should use hologram.
				auto eye_pos = (shared_graphics.ETH_display.right_eye_world + shared_graphics.ETH_display.left_eye_world) * 0.5f;

				// auto le_eye_vec = shared_graphics.ETH_display.right_eye_world - shared_graphics.ETH_display.left_eye_world;
				// 
				// // Convert world positions to screen for drawing
				// auto dwh = glm::vec2(disp_area.Size.x, disp_area.Size.y);
				// glm::vec2 screenDownPos = world2pixel(s->current_pos + vv, vm, pm, dwh);
				// glm::vec2 screenHoverPos = world2pixel(s->current_pos, vm, pm, dwh);
				// 
				// // Add display area offset to get absolute screen positions
				// ImVec2 startPos = ImVec2(screenDownPos.x + disp_area.Pos.x, screenDownPos.y + disp_area.Pos.y);
				// ImVec2 endPos = ImVec2(screenHoverPos.x + disp_area.Pos.x, screenHoverPos.y + disp_area.Pos.y);

				// Draw a yellow line from down to hover position
				// dl->AddLine(startPos, endPos, IM_COL32(255, 255, 0, 255), 2.0f);
				

				auto mid = 0.5f * (uvrb.x + uvtl.x);
				if (working_graphics_state->ETH_display.eye_id%2==0)
				{
					// left eye...
					uvrb = glm::vec2(mid, uvrb.y);
				}else
				{
					//right eye...
					uvtl = glm::vec2(mid, uvtl.y);
				}
			}
			sprite_params.push_back(gpu_sprite{
				.translation = s->current_pos,
				.flag = (float)(show_hover | show_selected | (s->rgba->loaded ? (1 << 5) : 0) | s->display_flags),
				.quaternion = quat,
				.dispWH = s->dispWH,
				.uvLeftTop = uvtl,
				.RightBottom = uvrb,
				.myshine = s->shineColor,
				.rgbid = glm::vec2((float)((s->rgba->instance_id << 4) | (s->rgba->atlasId & 0xf)), (float)i)
				});
		}
		if (sprite_params.size()>0)
		{   //todo:....
			sg_begin_pass(working_graphics_state->sprite_render.pass, &shared_graphics.sprite_render.quad_pass_action);
			sg_apply_pipeline(shared_graphics.sprite_render.quad_image_pip);
			u_quadim_t quadim{
				.pvm = pv,
				.screenWH = glm::vec2(w,h),
				.hover_shine_color_intensity = wstate.hover_shine,
				.selected_shine_color_intensity = wstate.selected_shine,
				.time = ui.getMsGraphics()
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
			sg_begin_pass(working_graphics_state->sprite_render.stat_pass, shared_graphics.sprite_render.stat_pass_action);
			sg_apply_pipeline(shared_graphics.sprite_render.stat_pip);
			sg_apply_bindings(sg_bindings{.vertex_buffers = {shared_graphics.uv_vertices}, .fs_images = {working_graphics_state->sprite_render.viewed_rgb}});
			sg_draw(0, 4, 1);
			sg_end_pass();
		}

		bool initialized_svg_sprites = false;
		// first clear svg store, svg.draw_params.clear();
		// first check all sprites, if it's a svg. if is, add to svg.draw_params.
		// if any svg.draw_params, sg_begin_pass.
		// .... for each svg, draw instances with draw_params.
				// First check all sprites to see if any are SVG sprites
		for (int i=0; i<svg_store.ls.size(); ++i)
		{
			auto s = svg_store.get(i);
			s->svg_params.clear();
		}

		for (int i = 0; i < sprites.ls.size(); ++i) {
			auto s = sprites.get(i);
			if (s->type!=me_sprite::sprite_type::svg_t) continue; // Only process SVG sprites
			if (!s->show[working_viewport_id]) continue;
			// Apply prop display mode filtering
			if (!viewport_test_prop_display(s)) continue;

			// Check if this sprite has associated SVG data
			auto svg = s->svg;
			if (svg->loaded) {
				auto show_hover = working_viewport->hover_type == 3 && working_viewport->hover_instance_id == i && (s->per_vp_stat[working_viewport_id] & (1 << 0)) ? 1 << 4 : 0;
				auto show_selected = (s->per_vp_stat[working_viewport_id] & (1 << 1)) ? 1 << 3 : 0;

				svg->svg_params.push_back(gpu_svg_struct{
					.translation = s->current_pos,
					.flag = (float)(show_hover | show_selected | s->display_flags),
					.quaternion = s->current_rot,
					.dispWH = s->dispWH,
					.myshine = s->shineColor,
					.info = glm::vec2(0.0f, (float)i)
					});

				initialized_svg_sprites = true;
			}
		}

		// If we have SVG sprites to render, draw them
		if (initialized_svg_sprites) {
			sg_begin_pass(working_graphics_state->sprite_render.svg_pass, &shared_graphics.sprite_render.quad_pass_action);
			sg_apply_pipeline(shared_graphics.svg_pip);

			u_quadim_t quadim{
				.pvm = pv,
				.screenWH = glm::vec2(w,h),
				.hover_shine_color_intensity = wstate.hover_shine,
				.selected_shine_color_intensity = wstate.selected_shine,
				.time = ui.getMsGraphics()
			};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_u_quadim, SG_RANGE(quadim));
			//sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_u_quadim, SG_RANGE(quadim));

			for (int i = 0; i < svg_store.ls.size(); ++i) {
				auto s = svg_store.get(i);
				auto sz = s->svg_params.size() * sizeof(gpu_svg_struct);
				auto buf = sg_make_buffer(sg_buffer_desc{ .size = sz, .data = {s->svg_params.data(), sz} });
				sg_bindings sb = { .vertex_buffers = {s->svg_pos_color, buf} };
				sg_apply_bindings(sb);
				sg_draw(0, s->triangleCnt, s->svg_params.size());
				sg_destroy_buffer(buf);
			}

			sg_end_pass();
		}

		TOC("sprites")
		// world ui, mostly text.
		{
			ImFont* font = ImGui::GetFont(); // Get default font
			std::vector<gpu_text_quad> visible_handles;
			// Collect visible text and their data
			// handle text icons using instanced rendering
			for (int i = 0; i < handle_icons.ls.size(); ++i) {
				auto handle = handle_icons.get(i);
				if (!handle->show[working_viewport_id]) continue;
				if (!viewport_test_prop_display(handle)) continue;

				// Determine final position (pinned or direct)
				glm::vec3 finalPos = handle->current_pos;

				// Get the icon character
				unsigned int c = 'P'; // Default to 'P' if none specified
				if (handle->icon.size() > 0) {
					const char* s = handle->icon.c_str();
					const char* text_end = s + handle->icon.size();
					ImTextCharFromUtf8(&c, s, text_end);
				}

				// Find the glyph in the font
				const ImFontGlyph* glyph = font->FindGlyph((ImWchar)c);
				if (!glyph) continue;

				// Calculate UV coordinates in the font texture
				glm::vec2 uv_min = { glyph->U0, glyph->V0 };
				glm::vec2 uv_max = { glyph->U1, glyph->V1 };

				// Size in pixels - we'll use this to determine scaling 
				float char_width = glyph->X1 - glyph->X0;
				float char_height = glyph->Y1 - glyph->Y0;

				// Base size in world units - this is the desired height of the glyph
				// For handles, we want a fixed size regardless of distance from camera
				glm::vec2 size = handle->size * glm::vec2(char_width + 3, char_height + 3);
					//* working_viewport->camera.dpi; // Will be height-scaled in shader

				// Calculate flags based on requirements
				int flags = 0x20; // Billboard mode (bit 5)

				// Add flag bits based on handle properties
				bool has_border = false; // Default no border
				bool has_shine = false; // Show shine if bg color is not transparent

				bool is_selected = handle->selected[working_viewport_id]; // Default not selected
				bool is_hovering = handle->selectable[working_viewport_id] &&
					working_viewport->hover_type == 4 && working_viewport->hover_instance_id == i;
				bool bring_to_front = is_selected || is_hovering; // Handles typically should be in front

				// TODO: Implement actual hover and selection detection here
				// For now we'll assume all handles can be selected/hovered
				// and will rely on the renderer to apply these effects

				// Set flag bits
				if (has_border) flags |= 0x01;        // bit 0: border
				if (has_shine) flags |= 0x02;         // bit 1: shine
				if (bring_to_front) flags |= 0x04;    // bit 2: bring to front
				if (is_selected) flags |= 0x08;       // bit 3: selected
				if (is_hovering) flags |= 0x10;       // bit 4: hovering
				if (glyph->Colored) flags |= 0x80;    // bit 7: is colored glyph.

				// Add arrow flag if background is not fully transparent (bit 6)
				if (handle->bg_color & 0xFF000000) {
					flags |= 0x40;
				}

				//ui-type=1;
				flags |= 1 << 8;

				// Add to render list
				visible_handles.push_back({
					finalPos,                     // position
					0,             // rotation
					size,                         // size
					handle->txt_color,                // text_color
					handle->bg_color,         // bg_color 
					uv_min,                       // uv_min
					uv_max,                       // uv_max
					1,1, // offset lb
					(int8_t)(char_width), (int8_t)(char_height), // offset hb
					(uint8_t)(char_width + 1), (uint8_t)(char_height + 1),
					glm::vec2((float)flags, (float)i),                         // flags
					glm::vec2(0)
					});
			}

			// Process text along lines
			for (int i = 0; i < text_along_lines.ls.size(); ++i) {
				auto text_line = text_along_lines.get(i);
				if (!text_line->show[working_viewport_id]) continue;
				if (!viewport_test_prop_display(text_line)) continue;

				// Determine starting position
				glm::vec3 start_pos = text_line->current_pos;
				if (text_line->direction_prop.obj!=nullptr)
				{
					text_line->direction = text_line->direction_prop.obj->current_pos - start_pos;
				}

				// Get screen size for projection
				glm::vec2 screenSize = {(float)w, (float)h};
				
				// Check if the starting position is behind the camera
				glm::vec3 screenStart = world2screen(start_pos, vm, pm, screenSize);
				if (screenStart.z <= 0) {
					// Skip this text line entirely if it starts behind the camera
					continue;
				}
				
				// Project a point along the text line direction to determine if text is upside down
				glm::vec3 dirEndpoint = start_pos + glm::normalize(text_line->direction);
				glm::vec3 screenEndpoint = world2screen(dirEndpoint, vm, pm, screenSize);
				
				// Get the projected direction on screen
				glm::vec2 screenDir = normalize(glm::vec2(screenEndpoint.x - screenStart.x, screenEndpoint.y - screenStart.y));
				
				// Calculate rotation angle from projected direction vector
				float rotation_angle = atan2(screenDir.y, screenDir.x);
				
				// Check if text is upside down (rotated more than π/2 or less than -π/2)
				bool is_upside_down = (rotation_angle > glm::half_pi<float>() || rotation_angle < -glm::half_pi<float>());
				
				// If text is upside down, flip the rotation
				if (is_upside_down) {
					rotation_angle += glm::pi<float>(); // Rotate 180 degrees
				}


				// First pass: process the text to get the total length and character information
				float total_length = 0.0f;
				std::vector<std::tuple<unsigned int, const ImFontGlyph*, float>> chars;
				
				size_t j = 0;
				while (j < text_line->text.size()) {
					// Get UTF-8 character
					unsigned int c = 0;
					int bytes_read = 0;
					const char* s = text_line->text.c_str() + j;
					const char* text_end = text_line->text.c_str() + text_line->text.size();
					if (s < text_end) {
						bytes_read = ImTextCharFromUtf8(&c, s, text_end);
						if (bytes_read == 0) break; // Invalid UTF-8 character
						j += bytes_read; // Move to next character
					}
					else {
						break; // End of string
					}

					// Find the glyph in the font
					const ImFontGlyph* glyph = font->FindGlyph((ImWchar)c);
					if (!glyph) continue;
					
					// Store the character info and advance
					float advance = glyph->AdvanceX * text_line->size;
					chars.push_back(std::make_tuple(c, glyph, advance));
					total_length += advance;
				}
				
				// Second pass: render each character
				float offset = is_upside_down;
				int st = is_upside_down ? chars.size() - 1 : 0;
				int ed = is_upside_down ? 0 : chars.size();
				int step = is_upside_down ? -1 : 1;
				// For upside-down text, start from the opposite end
					
				// Process characters in reverse order
				float r2 = 0;
				for (int ci = st; is_upside_down?ci>=ed:ci<ed; ci+=step) {
					unsigned int c = std::get<0>(chars[ci]);
					const ImFontGlyph* glyph = std::get<1>(chars[ci]);
					float advance = std::get<2>(chars[ci]);

					r2 += advance / 2;
					offset += r2 * step;
					
					// Calculate UV coordinates in the font texture
					glm::vec2 uv_min = { glyph->U0, glyph->V0 };
					glm::vec2 uv_max = { glyph->U1, glyph->V1 };

					// Size in world units
					glm::vec2 size = {
						glyph->AdvanceX,
						font->FontSize
					};
					size *= text_line->size;
										
					// Set up flags
					int flags = 0x00;

					// Add flag bits based on handle properties
					bool has_border = false; // Default no border
					bool has_shine = false; // Show shine if bg color is not transparent

					bool is_selected = text_line->selected[working_viewport_id]; // Default not selected
					bool is_hovering = text_line->selectable[working_viewport_id] &&
						working_viewport->hover_type == 5 && working_viewport->hover_instance_id == i;
					bool bring_to_front = is_selected || is_hovering; // Handles typically should be in front

					if (has_border) flags |= 0x01;
					if (has_shine) flags |= 0x02;
					if (bring_to_front) flags |= 0x04;
					if (is_selected) flags |= 0x08;
					if (is_hovering) flags |= 0x10;
					if (glyph->Colored) flags |= 0x80;    // bit 7: is colored glyph.
					flags |= 2 << 8; // type=line of txt.

					float char_width = glyph->X1 - glyph->X0;
					float char_height = glyph->Y1 - glyph->Y0;

					// Add to render list - no need to flip UVs since we're rendering in reverse order
					visible_handles.push_back({
						start_pos,                      // position
						rotation_angle,                // rotation in radians
						size,                          // size
						text_line->color,              // text_color
						0x00000000,                    // bg_color (transparent)
						uv_min,                        // uv_min
						uv_max,                        // uv_max
						(int8_t)glyph->X0, (int8_t)(glyph->Y0), // glyph_x0, glyph_y0 // oops, y0 is flipped.
						(int8_t)glyph->X1, (int8_t)(glyph->Y1), // glyph_x1, glyph_y1
						(uint8_t)size.x, (uint8_t)size.y, // bbx, bby
						glm::vec2((float)flags, (float)i),     // flags, instance_id
						glm::vec2(offset, text_line->voff * size.y),
						});
					r2 = advance / 2;
					
				}
			}

			// If there are visible handles, render them with instancing
			if (!visible_handles.empty()) {
				sg_begin_pass(working_graphics_state->world_ui.pass, shared_graphics.world_ui.pass_action);

				sg_buffer ibuf = sg_make_buffer(sg_buffer_desc{
					.size = visible_handles.size() * sizeof(gpu_text_quad),
					.data = {visible_handles.data(), visible_handles.size() * sizeof(gpu_text_quad)},
					.label = "world-ui-instance-data"
					});

				// Apply pipeline and bindings
				sg_apply_pipeline(shared_graphics.world_ui.pip_txt);

				// Create a temporary sg_image that wraps the OpenGL texture
				sg_image tex_img = sg_make_image(sg_image_desc{
					.width = 1,
					.height = 1,
					.label = "imgui-font-atlas-texture",
					.gl_textures = (uint32_t)ImGui::GetIO().Fonts->TexID,  // Use the OpenGL texture ID
					});

				sg_apply_bindings(sg_bindings{
					.vertex_buffers = { ibuf },
					.fs_images = {
						tex_img,   // Use ImGui font atlas
						working_graphics_state->primitives.depth           // Depth buffer for behind-object check
					}
					});

				// Set uniforms
				txt_quad_params_t params;

				params.mvp = pm * vm;
				params.dpi = working_viewport->camera.dpi;
				params.screenW = (float)w;
				params.screenH = (float)h;
				params.behind_opacity = 0.5f;  // Opacity for objects behind other scene objects

				// Define shine colors and intensities
				params.hover_shine_color_intensity = wstate.hover_shine;
				params.selected_shine_color_intensity = wstate.selected_shine;

				sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(params));
				sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(params));

				// Draw all instances - each with 4 vertices in a triangle strip
				sg_draw(0, 4, visible_handles.size());

				// Clean up
				sg_destroy_buffer(ibuf);
				sg_destroy_image(tex_img);
				sg_end_pass();
			}
		}
		//  End of draw UI related worldspace objects....

		// === post processing ===

		useFlag = (wstate.useEDL ? 1 : 0) | (working_viewport->camera.ProjectionMode == 0 ?
			(wstate.useSSAO ? 2 : 0) | (wstate.useGround ? 4 : 0) : 0) |
			(transparent_objects_N > 0 ? 8 : 0);

		// ---ssao---
		if (wstate.useSSAO && working_viewport->camera.ProjectionMode==0) { // just disable SSAO for now.
			ssao_uniforms.P = pm;
			ssao_uniforms.iP = glm::inverse(pm);
			// ssao_uniforms.V = vm;
			ssao_uniforms.iV = glm::inverse(vm);
			ssao_uniforms.cP = campos;
			ssao_uniforms.uDepthRange[0] = cam_near;
			ssao_uniforms.uDepthRange[1] = cam_far;
			// ssao_uniforms.time = 0;// (float)working_viewport->getMsFromStart() * 0.00001f;
			ssao_uniforms.useFlag = useFlag;
			ssao_uniforms.time = ui.getMsGraphics();

			sg_begin_pass(working_graphics_state->ssao.pass, &shared_graphics.ssao.pass_action);
			sg_apply_pipeline(shared_graphics.ssao.pip);
			sg_apply_bindings(working_graphics_state->ssao.bindings);
			// sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(ssao_uniforms));
			sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(ssao_uniforms));
			sg_draw(0, 4, 1);
			sg_end_pass();
		}
		// === WBOIT pass ===
		// WBOIT gltf:
		if (transparent_objects_N > 0)
		{
			// ACCUM
			{
				sg_begin_pass(working_graphics_state->wboit.accum_pass,
					shared_graphics.wboit.accum_pass_action);

				auto pip = _sg_lookup_pipeline(&_sg.pools, shared_graphics.wboit.accum_pip.id);
				pip->gl.cull_mode = SG_CULLMODE_NONE;

				sg_apply_pipeline(shared_graphics.wboit.accum_pip);

				for (int i = 0; i < gltf_classes.ls.size(); ++i) {
					auto t = gltf_classes.get(i);
					if (t->showing_objects[working_viewport_id].size() == t->opaques && !t->has_blending_material) continue;
					t->wboit_accum(vm, pm, renderings[i], i);
				}

				sg_end_pass();
			}

			// REVEALAGE ( apply mouse selection, etc )
			{
				sg_begin_pass(working_graphics_state->wboit.reveal_pass, shared_graphics.wboit.reveal_pass_action);

				auto pip = _sg_lookup_pipeline(&_sg.pools, shared_graphics.wboit.reveal_pip.id);
				if (wstate.activeClippingPlanes)
					pip->gl.cull_mode = SG_CULLMODE_NONE;
				else
					pip->gl.cull_mode = SG_CULLMODE_BACK;

				sg_apply_pipeline(shared_graphics.wboit.reveal_pip);

				for (int i = 0; i < gltf_classes.ls.size(); ++i) {
					auto t = gltf_classes.get(i);
					if (t->showing_objects[working_viewport_id].size() == t->opaques && !t->has_blending_material) continue;
					if (t->dbl_face && !wstate.activeClippingPlanes) //currently back cull.
						setFaceCull(false);
					t->wboit_reveal(vm, pm, renderings[i], i);

					if (t->dbl_face && !wstate.activeClippingPlanes) //currently back cull.
						setFaceCull(true);
				}

				sg_end_pass();
			}

			// Blending to image.
			{
				sg_begin_pass(working_graphics_state->wboit.compose_pass,
					shared_graphics.wboit.compose_pass_action);
				sg_apply_pipeline(shared_graphics.wboit.compose_pip);
				sg_apply_bindings(working_graphics_state->wboit.compose_bind);
				sg_draw(0, 4, 1);
				sg_end_pass();
			}
		}
		else
		{
			// clear bloom2.
			sg_begin_pass(working_graphics_state->wboit.compose_pass,
				shared_graphics.wboit.compose_pass_action);
			sg_end_pass();
		}
		TOC("WBOIT blending")

		// shine-bloom.
		if (wstate.useBloom)
		{
			auto clear = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_CLEAR,.store_action = SG_STOREACTION_STORE, .clear_value = { 0.0f, 0.0f, 0.0f, 0.0f },  } } ,
			};
			auto keep = sg_pass_action{
			.colors = { {.load_action = SG_LOADACTION_LOAD, .store_action = SG_STOREACTION_STORE } },
			};

			auto bindingmerge = sg_bindings{
				.vertex_buffers = {shared_graphics.quad_vertices},
				.fs_images = {working_graphics_state->bloom1, working_graphics_state->bloom2}
			};

			auto binding2to1 = sg_bindings{
				.vertex_buffers = {shared_graphics.quad_vertices},
				.fs_images = {working_graphics_state->shine2}
			};
			auto binding1to2 = sg_bindings{
				.vertex_buffers = {shared_graphics.quad_vertices},
				.fs_images = {working_graphics_state->bloom1}
			};

			sg_begin_pass(working_graphics_state->ui_composer.shine_pass1to2, clear);
			sg_apply_pipeline(shared_graphics.ui_composer.pip_dilateX);
			sg_apply_bindings(bindingmerge);
			sg_draw(0, 4, 1);
			sg_end_pass();

			sg_begin_pass(working_graphics_state->ui_composer.shine_pass2to1, keep);
			sg_apply_pipeline(shared_graphics.ui_composer.pip_dilateY);
			sg_apply_bindings(binding2to1);
			sg_draw(0, 4, 1);
			sg_end_pass();
			
			sg_begin_pass(working_graphics_state->ui_composer.shine_pass1to2, keep);
			sg_apply_pipeline(shared_graphics.ui_composer.pip_blurX);
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


	// below to be revised: spotlight show.
	// if (wstate.useGround){
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
	// 	glm::mat4 ob;
	// 	sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(ob));
	// 	sg_draw(0, 6, ground_instances.size());
	// 	sg_destroy_buffer(graphics_state.ground_effect.spotlight_bind.vertex_buffers[1]);
	// }
    // }

	// todo: useGround->generic screen space reflection(use metadata of rendering).
	if (wstate.useGround && working_viewport->camera.ProjectionMode == 0) {
		sg_begin_pass(working_graphics_state->ground_effect.pass, shared_graphics.ground_effect.pass_action);

		sg_apply_pipeline(shared_graphics.ground_effect.cs_ssr_pip);
		sg_apply_bindings(working_graphics_state->ground_effect.bind);
		auto ug = uground_t{
			.w = float(w), .h = float(h), .pnear = cam_near, .pfar = cam_far,
			.ipmat = invPm, //glm::inverse(working_viewport->camera.GetProjectionMatrix()),// glm::inverse(pm),
			.ivmat = invVm, //glm::inverse(working_viewport->camera.GetViewMatrix()),
			.pmat = pm, //working_viewport->camera.GetProjectionMatrix(),//pm,
			.pv = pm * vm, //working_viewport->camera.GetProjectionMatrix()*working_viewport->camera.GetViewMatrix(),//,//pv,
			.campos = campos, //working_viewport->camera.position, // campos, //
			.time = ui.getMsGraphics()
		};

		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_window, SG_RANGE(ug));
		sg_draw(0, 4, 1);

		sg_end_pass();
	}
	TOC("ground")

	
	if (wstate.drawGuizmo){
	    int guizmoSz = 60 * working_viewport->camera.dpi;
	    auto viewManipulateRight = disp_area.Pos.x + w;
	    auto viewManipulateTop = disp_area.Pos.y + h;
	    auto viewMat = working_viewport->camera.GetViewMatrix();
		glm::vec3 camDirOld = glm::vec3(viewMat[0][2], viewMat[1][2], viewMat[2][2]);

		float* ptrView = &viewMat[0][0];

		//glm::mat4 ob;
	    //ImGuizmo::ViewManipulate(ptrView, (float*)&pm, ImGuizmo::ROTATE | ImGuizmo::TRANSLATE, ImGuizmo::LOCAL, (float*)&ob, working_viewport->camera.distance, ImVec2(viewManipulateRight - guizmoSz - 25*working_viewport->camera.dpi, viewManipulateTop - guizmoSz - 16*working_viewport->camera.dpi), ImVec2(guizmoSz, guizmoSz), 0x00000000);
		auto mod = ImGuizmo::ViewManipulate(ptrView, working_viewport->camera.distance, 
			ImVec2(viewManipulateRight - guizmoSz - 25 * working_viewport->camera.dpi, 
				viewManipulateTop - guizmoSz - 9 * working_viewport->camera.dpi), ImVec2(guizmoSz, guizmoSz), 0x00000000);
		if (mod) {
			glm::vec3 camDir = glm::vec3(viewMat[0][2], viewMat[1][2], viewMat[2][2]);
			glm::vec3 camUp = glm::vec3(viewMat[1][0], viewMat[1][1], viewMat[1][2]);

			camDir = glm::normalize(camDir);
			camUp = glm::normalize(camUp);
			auto alt = asin(camDir.z);
			auto azi = atan2(camDir.y, camDir.x);
			if (abs(alt - M_PI_2) < 0.05 || abs(alt + M_PI_2) < 0.05)
				azi = (alt > 0 ? -1 : 1) * atan2(camUp.y, camUp.x);

			auto diff = (azi - working_viewport->camera.Azimuth);
			diff = diff - round(diff / 3.14159265358979323846f / 2) * 3.14159265358979323846f * 2;
			if (abs(diff) > 0.1)
			{
				azi = working_viewport->camera.Azimuth + glm::sign(diff) * 0.1f;
				azi -= round(azi / 3.14159265358979323846f / 2) * 3.14159265358979323846f * 2;
			}

			if (!((isnan(azi) || isnan(alt))))
			{
				working_viewport->camera.Azimuth = azi;
				working_viewport->camera.Altitude = alt;
				working_viewport->camera.UpdatePosition();
			}
		}
	} 


	TOC("guizmo")
	// precompute walkable low-res RT before composing
	if (any_walkable) {
		sg_begin_pass(working_graphics_state->walkable_overlay.pass, &shared_graphics.walkable_passAction);
		sg_apply_pipeline(shared_graphics.walkable_pip);
		sg_apply_bindings(sg_bindings{ .vertex_buffers = { shared_graphics.quad_vertices }, 
			.fs_images = { shared_graphics.walkable_cache } });
		walkable_params_t wp{};
		wp.vm = vm;
		wp.pm = pm;
		wp.viewport_size = glm::vec2(w, h);
		wp.cam_pos_res = glm::vec4(campos, 4.0f);
		wp.grid_origin = glm::vec4(0, 0, 0, 0);
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_walkable_params, SG_RANGE(wp));
		sg_draw(0, 4, 1);
		sg_end_pass();
	}

	TOC("walkable")

	// draw Region3D volumetric clouds (minecraft-like) onto hi_color.
	{
		if (shared_graphics.region_cache_dirty) 
			BuildRegionVoxelCache();
		bool anyRegion = false;
		for (int i = 0; i < region_cloud_bunches.ls.size(); ++i) {
			auto bunch = std::get<0>(region_cloud_bunches.ls[i]);
			if (bunch == nullptr || bunch->items.empty()) continue;
			anyRegion = true; break;
		}
		if (anyRegion) {
			region3d_params_t rp{};
			rp.vm = vm;
			rp.pm = pm;
			rp.viewport_size = glm::vec2((float)w, (float)h);
			rp.cam_pos = working_viewport->camera.position;
			rp.quantize = working_viewport->workspace_state.back().voxel_quantize;
			rp.face_opacity = 0.3f;
			sg_begin_pass(working_graphics_state->region3d.pass, working_graphics_state->region3d.pass_action);
			sg_apply_pipeline(shared_graphics.region3d_pip);
			sg_apply_bindings(sg_bindings{ .vertex_buffers = { shared_graphics.quad_vertices },
				.fs_images = { shared_graphics.region_cache, working_graphics_state->primitives.depth,  } });
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_region3d_params, SG_RANGE(rp));
			sg_draw(0, 4, 1);
			sg_end_pass();
		}
	}

	TOC("region")

	// =========== Final composing ============
	sg_begin_pass(working_graphics_state->temp_render_pass, &shared_graphics.default_passAction);

	bool customized = false;

	if (working_graphics_state->skybox_image.valid)
	{
		// Render skybox image using equirectangular shader
		sg_apply_pipeline(shared_graphics.skybox.pip_img);
		sg_apply_bindings(sg_bindings{
			.vertex_buffers = { shared_graphics.quad_vertices },
			.fs_images = { working_graphics_state->skybox_image.image }
			});

		auto skybox_params = skybox_equirect_uniforms_t{
			.invVM = invVm,
			.invPM = invPm,
		};
		//sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_skybox_equirect_uniforms, SG_RANGE(skybox_params));
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_skybox_equirect_uniforms, SG_RANGE(skybox_params));

		sg_draw(0, 4, 1);
		customized = true;
	}

	if (shared_graphics.custom_bg_shader.valid) {
		// Render custom background shader
		sg_apply_pipeline(shared_graphics.custom_bg_shader.pipeline);
		sg_apply_bindings(sg_bindings{
			.vertex_buffers = { shared_graphics.quad_vertices }
			});

		// Set up uniforms for the shader
		struct {
			glm::vec2 iResolution;
			float iTime;
			float _pad;
			glm::vec3 iCameraPos;
			float _pad2;
			glm::mat4 iPVM;
			glm::mat4 iInvVM;
			glm::mat4 iInvPM;
		} uniforms;

		uniforms.iResolution = glm::vec2(w, h);
		uniforms.iTime = ui.getMsGraphics() / 1000.0f;
		uniforms.iCameraPos = campos;
		uniforms.iPVM = pv;
		uniforms.iInvVM = invVm;
		uniforms.iInvPM = invPm;

		sg_apply_uniforms(SG_SHADERSTAGE_FS, 0, SG_RANGE(uniforms));
		sg_draw(0, 4, 1);
		customized = true;
	}

	if (!customized && working_viewport->camera.ProjectionMode == 0 && working_viewport->workspace_state.back().useDefaultSky) {
		_draw_skybox(vm, pm);
	}

	// walkable overlay composite: render low-res to RT first, then upscale and blend here
	
	if (any_walkable) {
		// composite onto default temp render (already has skybox)
		sg_apply_pipeline(shared_graphics.utilities.pip_blend);
		shared_graphics.utilities.bind.fs_images[0] = working_graphics_state->walkable_overlay.low;
		sg_apply_bindings(&shared_graphics.utilities.bind);
		sg_draw(0, 4, 1);
	}
	


	// composing (aware of depth)
	if (compose) {
		sg_apply_pipeline(shared_graphics.composer.pip);
		sg_apply_bindings(working_graphics_state->composer.bind);
		auto wnd = window_t{
			.w = float(w), .h = float(h), .pnear = cam_near, .pfar = cam_far,
			.ipmat = invPm, //glm::inverse(pm),
			.ivmat = invVm, //glm::inverse(vm),
			.pmat = pm,
			.pv = pv,
			.campos = campos, 
			.lookdir = glm::normalize(working_viewport->camera.stare - working_viewport->camera.position),

			.facFac = facFac,
			.fac2Fac = fac2Fac,
			.fac2WFac = fac2WFac,
			.colorFac = colorFac,
			.reverse1 = reverse1,
			.reverse2 = reverse2,
			.edrefl = edrefl,

			.useFlag = useFlag
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

	// todo: revise this.
	if (wstate.useGround && working_viewport->camera.ProjectionMode == 0){
		sg_apply_pipeline(shared_graphics.utilities.pip_blend);
		shared_graphics.utilities.bind.fs_images[0] = working_graphics_state->ground_effect.ground_img;
		sg_apply_bindings(&shared_graphics.utilities.bind);
		sg_draw(0, 4, 1);
	}


	TOC("composing1")

	// ground reflection.

	// billboards

	// ui-composing. (border, shine, bloom)
	// shine-bloom
	if (wstate.useBloom) {
		sg_apply_pipeline(shared_graphics.ui_composer.pip_blurYFin);
		sg_apply_bindings(sg_bindings{ .vertex_buffers = {shared_graphics.quad_vertices},
			.fs_images = {working_graphics_state->shine2, working_graphics_state->bloom2} });
		sg_draw(0, 4, 1);
	}

	// border
	if (wstate.useBorder) {
		sg_apply_pipeline(shared_graphics.ui_composer.pip_border);
		sg_apply_bindings(working_graphics_state->ui_composer.border_bind);
		auto composing = ui_composing_t{
			.draw_sel = working_graphics_state->use_paint_selection ? 1.0f : 0.0f,
			.border_colors = {wstate.hover_border_color.x, wstate.hover_border_color.y, wstate.hover_border_color.z, wstate.hover_border_color.w,
				wstate.selected_border_color.x, wstate.selected_border_color.y, wstate.selected_border_color.z, wstate.selected_border_color.w,
				wstate.world_border_color.x, wstate.world_border_color.y, wstate.world_border_color.z, wstate.world_border_color.w},
				//.border_size = 5,
		};
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_ui_composing, SG_RANGE(composing));
		sg_draw(0, 4, 1);
	}

	if (wstate.drawGrid || wstate.useOperationalGrid) {
		if (wstate.drawGrid && wstate.useGround) { // infinite ground grid effect, visually better can be tuned off.
			sg_apply_pipeline(shared_graphics.skybox.pip_grid);
			sg_apply_bindings(sg_bindings{
				.vertex_buffers = { shared_graphics.quad_vertices },
				.fs_images = {working_graphics_state->primitives.depth}
				});
			auto foreground_u = u_user_shader_t{
				.invVM = invVm,
				.invPM = invPm,
				.iResolution = glm::vec2(w,h),
				.pvm = pv,
				.camera_pos = campos,
			};
			sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(foreground_u));
			sg_draw(0, 4, 1);
		}

		// Appearant grid with label:
		working_graphics_state->grid.Draw(working_viewport->camera, disp_area, dl, vm, pm);
	}

	TOC("fin composing")
	// we also need to draw the imgui drawlist on the temp_render texture.
	// Draw ImGui draw list onto temp_render texture
	if (dl->VtxBuffer.Size > 0) {
	    // Create temporary buffers for the draw data
	    const size_t vtx_buffer_size = dl->VtxBuffer.Size * sizeof(ImDrawVert);
	    const size_t idx_buffer_size = dl->IdxBuffer.Size * sizeof(ImDrawIdx);
	    
	    // Create temporary buffers for the draw data
	    sg_buffer vtx_buf = sg_make_buffer(sg_buffer_desc{
	        .size = vtx_buffer_size,
	        .data = {dl->VtxBuffer.Data, vtx_buffer_size},
	        .label = "imgui-vertices"
	    });
	    sg_buffer idx_buf = sg_make_buffer(sg_buffer_desc{
	        .size = idx_buffer_size,
	        .type = SG_BUFFERTYPE_INDEXBUFFER,
	        .data = {dl->IdxBuffer.Data, idx_buffer_size},
	        .label = "imgui-indices"
	    });

	    sg_apply_pipeline(shared_graphics.utilities.pip_imgui);

		// Set up orthographic projection matrix
		float L = disp_area.Pos.x;
		float R = disp_area.Pos.x + disp_area.Size.x;
		float T = disp_area.Pos.y;
		float B = disp_area.Pos.y + disp_area.Size.y;

		const float ortho_projection[4][4] = {
			{ 2.0f/(R-L),   0.0f,         0.0f,   0.0f },
			{ 0.0f,         2.0f/(T-B),   0.0f,   0.0f },
			{ 0.0f,         0.0f,        -1.0f,   0.0f },
			{ (R+L)/(L-R),  (T+B)/(B-T),  0.0f,   1.0f },
		};

		// Apply uniforms
		
		imgui_vs_params_t params = {
			.ProjMtx = *(glm::mat4*)ortho_projection,
		};
		sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_imgui_vs_params, SG_RANGE(params));

	    int idx_offset = 0;
	    for (int cmd_i = cmd_st; cmd_i < dl->CmdBuffer.Size; cmd_i++) {
	        ImDrawCmd* pcmd = &dl->CmdBuffer[cmd_i];
	        if (pcmd->UserCallback) {
				// ignore.
	        } else {
				// Project scissor/clipping rectangles into framebuffer space
				// ImVec2 clip_min((pcmd->ClipRect.x - disp_area.Pos.x), (pcmd->ClipRect.y - disp_area.Pos.y));
				// ImVec2 clip_max((pcmd->ClipRect.z - disp_area.Pos.x), (pcmd->ClipRect.w - disp_area.Pos.y));
				// if (clip_max.x <= clip_min.x || clip_max.y <= clip_min.y)
				// 	continue;
				//
				// // Apply scissor/clipping rectangle (Y is inverted)
				// sg_apply_scissor_rect(
				// 	(int)clip_min.x, 
				// 	(int)(disp_area.Size.y - clip_max.y),
				// 	(int)(clip_max.x - clip_min.x),
				// 	(int)(clip_max.y - clip_min.y),
				// 	true);
	            
				// apply uniforms
				
		        if (pcmd->TextureId) {
					sg_image tex_img = genImageFromPlatform((unsigned int)pcmd->TextureId);

				    // Apply bindings with the temporary texture
				    sg_bindings bind = {
				        .vertex_buffers = {vtx_buf},
				        .index_buffer = idx_buf,
				        .fs_images = {tex_img}
				    };
				    sg_apply_bindings(&bind);

				    // Draw the command
				    sg_draw(idx_offset, pcmd->ElemCount, 1);

				    // Destroy the temporary image wrapper
				    sg_destroy_image(tex_img);
				} else {
				    // Use default bindings for non-textured elements
				    sg_bindings bind = {
				        .vertex_buffers = {vtx_buf},
				        .index_buffer = idx_buf,
				        .fs_images = {shared_graphics.dummy_tex}
				    };
				    sg_apply_bindings(&bind);
				    sg_draw(idx_offset, pcmd->ElemCount, 1);
				}

	            // sg_draw(idx_offset, pcmd->ElemCount, 1);
			}
	        idx_offset += pcmd->ElemCount;
			
		    pcmd->UserCallback = skip_imgui_render;
		    pcmd->UserCallbackData = nullptr;

	    }
		dl->AddDrawCmd(); // Force a new command after us (see comment below)

	    // Cleanup temporary buffers
	    sg_destroy_buffer(vtx_buf);
	    sg_destroy_buffer(idx_buf);

	    // Clear the draw list since we've rendered it
		//dl->_ResetForNewFrame();
	}
	sg_end_pass();

	TOC("imgui_drawlist")

}

static void SaveGratingParams(const grating_param_t& params) {
    // 生成文件名
    std::string filename = "monitor_" + std::string(monitorName) + ".gratingparam";
    
    // 写入文件
    std::ofstream file(filename);
    if (file.is_open()) {
        file << "grating_interval_mm=" << params.grating_interval_mm << "\n";
        file << "grating_angle=" << atan2(grating_params.grating_dir.y, grating_params.grating_dir.x) * 180.0f / pi << "\n";
        file << "grating_to_screen_mm=" << params.grating_to_screen_mm << "\n";
        file << "grating_bias=" << params.grating_bias << "\n";
        file << "screen_size_physical_mm=" << params.screen_size_physical_mm.x << "," << params.screen_size_physical_mm.y << "\n";
        file << "slot_width_mm=" << params.slot_width_mm << "\n";
        file << "viewing_angle=" << params.viewing_angle << "\n";
        file << "beyond_viewing_angle=" << params.beyond_viewing_angle << "\n";
        file << "compensator_factor_1=" << params.compensator_factor_1.x << "," << params.compensator_factor_1.y << "\n";
        file << "leakings=" << params.leakings.x << "," << params.leakings.y << "\n";
        file << "dims=" << params.dims.x << "," << params.dims.y << "\n";
        file << "curved_angle_deg=" << params.curved_angle_deg << "\n";
        file << "curved_portion=" << params.curved_portion << "\n";
        file.close();
    }
}

static void LoadGratingParams(grating_param_t* params) {
    std::string filename = "monitor_" + std::string(monitorName) + ".gratingparam";

    // 读取文件
    std::ifstream file(filename);
    if (file.is_open()) {
        std::string line;
        while (std::getline(file, line)) {
            size_t eq = line.find('=');
            if (eq != std::string::npos) {
                std::string key = line.substr(0, eq);
                std::string value = line.substr(eq+1);
                
                try {
                    if (key == "grating_interval_mm") params->grating_interval_mm = std::stof(value);
					else if (key == "grating_angle") {
						auto angle_rad = std::stof(value) / 180 * pi;
						params->grating_dir = glm::vec2(cos(angle_rad), sin(angle_rad));
					}
                    else if (key == "grating_to_screen_mm") params->grating_to_screen_mm = std::stof(value);
                    else if (key == "grating_bias") params->grating_bias = std::stof(value);
                    else if (key == "screen_size_physical_mm") {
                        size_t comma = value.find(',');
                        if (comma != std::string::npos) {
                            params->screen_size_physical_mm.x = std::stof(value.substr(0, comma));
                            params->screen_size_physical_mm.y = std::stof(value.substr(comma+1));
                        }
                    }
                    else if (key == "slot_width_mm") params->slot_width_mm = std::stof(value);
                    else if (key == "viewing_angle") params->viewing_angle = std::stof(value);
                    else if (key == "beyond_viewing_angle") params->beyond_viewing_angle = std::stof(value);
                    else if (key == "compensator_factor_1") {
                        size_t comma = value.find(',');
                        if (comma != std::string::npos) {
                            params->compensator_factor_1.x = std::stof(value.substr(0, comma));
                            params->compensator_factor_1.y = std::stof(value.substr(comma+1));
                        }
                    }
                    else if (key == "leakings") {
                        size_t comma = value.find(',');
                        if (comma != std::string::npos) {
                            params->leakings.x = std::stof(value.substr(0, comma));
                            params->leakings.y = std::stof(value.substr(comma+1));
                        }
                    }
                    else if (key == "dims") {
                        size_t comma = value.find(',');
                        if (comma != std::string::npos) {
                            params->dims.x = std::stof(value.substr(0, comma));
                            params->dims.y = std::stof(value.substr(comma+1));
                        }
                    }
                    else if (key == "curved_angle_deg") params->curved_angle_deg = std::stof(value);
                    else if (key == "curved_portion") params->curved_portion = std::stof(value);
                } catch (...) {
                    // 忽略解析错误
                }
            }
        }
        file.close();
    }
}
void ProcessWorkspace(disp_area_t disp_area, ImDrawList* dl, ImGuiViewport* viewport)
{
	auto w = disp_area.Size.x;
	auto h = disp_area.Size.y;
	auto tic=std::chrono::high_resolution_clock::now();
	auto tic_st = tic;
	int span;
	
	auto vm = working_viewport->camera.GetViewMatrix();
	auto pm = working_viewport->camera.GetProjectionMatrix();
	auto invVm = glm::inverse(vm);
	auto invPm = glm::inverse(pm);


	auto& wstate = working_viewport->workspace_state.back();

	// Then render the temporary texture to screen
	if (working_viewport==&ui.viewports[0]){
		if (working_viewport->displayMode == viewport_state_t::Normal){
			sg_begin_default_pass(&shared_graphics.default_passAction, viewport->Size.x, viewport->Size.y);
			sg_apply_viewport(disp_area.Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area.Pos.y-viewport->Pos.y + h), w, h, false);
			sg_apply_scissor_rect(disp_area.Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area.Pos.y-viewport->Pos.y + h), w, h, false);
			
			sg_apply_pipeline(shared_graphics.utilities.pip_rgbdraw);
			sg_apply_bindings(sg_bindings{
				.vertex_buffers = {shared_graphics.quad_vertices},
				.fs_images = {working_graphics_state->temp_render}
			});
			sg_draw(0, 4, 1);
			sg_end_pass();
		}
		else if (working_viewport->displayMode == viewport_state_t::EyeTrackedHolography)
		{
			// working_viewport is right eye.
			sg_begin_default_pass(&shared_graphics.default_passAction, viewport->Size.x, viewport->Size.y);
			

			// Now render the grating display, eye pos is for debugging.

			// Show ImGui controls for grating parameters
			//if (ImGui::Begin("Grating Display Settings")) {
				ImGui::DragFloat("World2Physic", &grating_params.world2phy, 1.0f, 1.0f, 1000.0f, "%.1f");

				static float adjusting_fac = 1;
				ImGui::DragFloat("factoring", &adjusting_fac, 0.03, -10, 10);
				float vve = pow(2, adjusting_fac);
				ImGui::DragFloat("Grating Width (mm)", &grating_params.grating_interval_mm, 0.000003f*vve, 0.0001f, 5.0f, "%.6f");
				ImGui::DragFloat("Grating to Screen (mm)", &grating_params.grating_to_screen_mm, 0.00037f * vve, 0.0000f, 5.0f, "%.5f");
				ImGui::DragFloat("Grating Bias (T)", &grating_params.grating_bias, 0.0001f * vve);
				
				ImGui::Checkbox("Debug Eye Pos", &grating_params.debug_eye);
				if (grating_params.debug_eye){
					ImGui::DragFloat("Pupil Distance (mm)", &grating_params.pupil_distance_mm, 0.1f, 45.0f, 200.0f);
					ImGui::DragFloat("Eyes Pitch (degrees)", &grating_params.eyes_pitch_deg, 0.1f, -45.0f, 45.0f);
					ImGui::DragFloat3("Eyes Center Position (mm)", &grating_params.eyes_center_mm.x, 0.1f);
					
					// Calculate actual eye positions based on parameters
					float pitch_rad = grating_params.eyes_pitch_deg * 3.14159f / 180.0f;
					float half_ipd = grating_params.pupil_distance_mm * 0.5f;
					
					// Apply pitch rotation and offset from center
					grating_params.left_eye_pos_mm = grating_params.eyes_center_mm + 
						glm::vec3(-half_ipd, -half_ipd * sin(pitch_rad), 0.0f);
					grating_params.right_eye_pos_mm = grating_params.eyes_center_mm + 
						glm::vec3(half_ipd, -half_ipd * sin(pitch_rad), 0.0f);
					
				}

				// Display calculated positions (optional, for debugging)
				ImGui::Text("Left Eye: (%.1f, %.1f, %.1f)", 
					grating_params.left_eye_pos_mm.x,
					grating_params.left_eye_pos_mm.y, 
					grating_params.left_eye_pos_mm.z);
				ImGui::Text("Right Eye: (%.1f, %.1f, %.1f)",
					grating_params.right_eye_pos_mm.x,
					grating_params.right_eye_pos_mm.y,
					grating_params.right_eye_pos_mm.z);
				
				// Add grating angle control (in degrees)
				static float angle_degrees = atan2(grating_params.grating_dir.y, grating_params.grating_dir.x) * 180.0f / pi;

				if (ImGui::DragFloat("Grating Angle (degrees)", &angle_degrees, 0.0001f * vve,-999,999, "%.4f")) {
					float angle_rad = angle_degrees * pi / 180.0f;
					grating_params.grating_dir = glm::vec2(cos(angle_rad), sin(angle_rad));
				}

				// Add physical screen dimension controls
				ImGui::DragFloat2("Screen Size (mm)", &grating_params.screen_size_physical_mm.x, 0.1f, 10.0f, 2000.0f);

				ImGui::DragFloat("Pupil Factor", &grating_params.pupil_factor, 0.001f, 0.001f, 1.0f);
				
				// NEW: Add controls for slot width and feathering
				ImGui::Separator();
				ImGui::Text("Line Appearance:");
				ImGui::DragFloat("Slot Width (mm)", &grating_params.slot_width_mm, 0.001f, 0.001f, 1.0f, "%.3f");
				
				ImGui::DragFloat("Best View Angle(Deg)", &grating_params.viewing_angle, 0.1f, 10.0f, 90.0f);
				ImGui::DragFloat("beyond_viewing_angle(Deg)", &grating_params.beyond_viewing_angle, 0.1f, grating_params.viewing_angle, 150);
				ImGui::DragFloat2("compensator 1", &grating_params.compensator_factor_1.x, 0.001f, -10, 10);
				
				ImGui::DragFloat("Best View Angle(Deg) f", &grating_params.viewing_angle_f, 0.1f, 10.0f, 90.0f);
				ImGui::DragFloat("beyond_viewing_angle(Deg) f", &grating_params.beyond_viewing_angle_f, 0.1f, grating_params.viewing_angle, 150);
				ImGui::DragFloat2("grating leaking", &grating_params.leakings.x, 0.0003f, 0.0f, 1.0f);
				ImGui::DragFloat2("diminishing", &grating_params.dims.x, 0.0003f, 0.0f, 1.0f);

				// Curved display controls
				ImGui::Separator();
				ImGui::Text("Curved Display Settings:");
				ImGui::DragFloat("Curve Angle (degrees)", &grating_params.curved_angle_deg, 0.5f, 0.0f, 180.0f, "%.1f");
				if (ImGui::IsItemHovered()) {
					ImGui::SetTooltip("Total curvature angle of the display. 0 = flat, 60-120 typical for curved monitors");
				}
				ImGui::DragFloat("Curve Portion", &grating_params.curved_portion, 0.01f, 0.0f, 1.0f, "%.2f");
				if (ImGui::IsItemHovered()) {
					ImGui::SetTooltip("Portion of screen that is curved. 1.0 = entire screen, 0.5 = center half only");
				}
				
				ImGui::Separator();
				ImGui::Checkbox("Debug Red-Blue View", &grating_params.debug_show);
				ImGui::Checkbox("Show Right", &grating_params.show_right);
				ImGui::Checkbox("Show Left", &grating_params.show_left);

				if (ImGui::Button("Save params"))
					SaveGratingParams(grating_params);

				//ImGui::End();
			//}


			// Apply grating display pipeline
			sg_apply_viewport(disp_area.Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area.Pos.y-viewport->Pos.y + h), w, h, false);
			sg_apply_scissor_rect(disp_area.Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area.Pos.y-viewport->Pos.y + h), w, h, false);
			
			sg_apply_pipeline(shared_graphics.grating_display.pip);
						
			// Calculate physical size per pixel based on monitor dimensions
			float pixels_per_mm_x = monitorWidth / grating_params.screen_size_physical_mm.x;
			float pixels_per_mm_y = monitorHeight / grating_params.screen_size_physical_mm.y;

			// Calculate physical size of display area in mm
			float disp_area_width_mm = disp_area.Size.x / pixels_per_mm_x;
			float disp_area_height_mm = disp_area.Size.y / pixels_per_mm_y;

			// Get direction perpendicular to grating direction
			glm::vec2 grating_perp = glm::vec2(-grating_params.grating_dir.y, grating_params.grating_dir.x);
			glm::vec2 grating_perp_normalized = glm::normalize(grating_perp);

			// Convert display area position to physical coordinates (mm)
			glm::vec2 disp_left_top_mm = glm::vec2(
				(disp_area.Pos.x - monitorX) / pixels_per_mm_x,
				(disp_area.Pos.y - monitorY) / pixels_per_mm_y
			);
			glm::vec2 disp_left_bottom_mm = glm::vec2(
				(disp_area.Pos.x - monitorX) / pixels_per_mm_x,
				(disp_area.Pos.y + disp_area.Size.y - monitorY) / pixels_per_mm_y
			);

			auto disp_corner = grating_params.grating_dir.y < 0 ? disp_left_top_mm : disp_left_bottom_mm;
			// Project left-top point onto perpendicular direction
			float proj = glm::dot(disp_corner, grating_perp_normalized);

			// Calculate grating number (floor to get the first grating before this point)
			float start_grating = floor(proj / grating_params.grating_interval_mm);
			
			// printf("%f, %f\n", proj, start_grating);
			// Project corners onto perpendicular direction to find coverage needed
			glm::vec2 corners[4] = {
				glm::vec2(0, 0),
				glm::vec2(disp_area_width_mm, 0),
				glm::vec2(disp_area_width_mm, disp_area_height_mm),
				glm::vec2(0, disp_area_height_mm)
			};

			float min_proj = FLT_MAX;
			float max_proj = -FLT_MAX;
			for (int i = 0; i < 4; i++) {
				float proj = glm::dot(corners[i], grating_perp_normalized);
				min_proj = glm::min(min_proj, proj);
				max_proj = glm::max(max_proj, proj);
			}

			float coverage_length_mm = max_proj - min_proj;
			int num_gratings = (int)ceil(coverage_length_mm / grating_params.grating_interval_mm);


			// Set vertex shader uniforms
			grating_display_vs_params_t vs_params{
				.screen_size_mm = grating_params.screen_size_physical_mm,
				.grating_dir_width_mm = glm::vec3(grating_params.grating_dir.x, grating_params.grating_dir.y, grating_params.grating_interval_mm),
				.grating_to_screen_mm = grating_params.grating_to_screen_mm,
				.slot_width_mm = grating_params.slot_width_mm,
				.left_eye_pos_mm = grating_params.left_eye_pos_mm,
				.right_eye_pos_mm = grating_params.right_eye_pos_mm,
				.monitor = glm::vec4(monitorX, monitorY, monitorWidth, monitorHeight),
				.disp_area = glm::vec4(disp_area.Pos.x, disp_area.Pos.y, disp_area.Size.x, disp_area.Size.y),
				.start_grating = start_grating + grating_params.grating_bias,
				.best_viewing_angle = glm::vec2(grating_params.viewing_angle, grating_params.beyond_viewing_angle)/180.0f*pi,
				.viewing_compensator = grating_params.compensator_factor_1,
				.curved_angle_deg = grating_params.curved_angle_deg,
				.curved_portion = grating_params.curved_portion
			};

			// ImGui::Text("start_grating=%f", vs_params.start_grating);

			sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_grating_display_vs_params, SG_RANGE(vs_params));
			
			grating_display_fs_params_t fs_params{
				.screen_size_mm = grating_params.screen_size_physical_mm,
				.left_eye_pos_mm = grating_params.left_eye_pos_mm,
				.right_eye_pos_mm = grating_params.right_eye_pos_mm,
				//.pupil_factor = grating_params.pupil_factor,
				.slot_width_mm = grating_params.slot_width_mm,
				// .feather_width_mm = grating_params.feather_width_mm,

				.debug = grating_params.debug_show,
				.show_left = grating_params.show_left,
				.show_right =  grating_params.show_right,
				.offset = glm::vec2(disp_area.Pos.x - viewport->Pos.x, viewport->Pos.y + viewport->Size.y - disp_area.Pos.y - disp_area.Size.y),
				.best_viewing_angle = glm::vec2(grating_params.viewing_angle_f, grating_params.beyond_viewing_angle_f) / 180.0f * pi,
				.leakings = grating_params.leakings,
				.dims =  grating_params.dims
			};
			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_grating_display_fs_params, SG_RANGE(fs_params));

			// Set textures
			sg_apply_bindings(sg_bindings{
				.fs_images = {graphics_states[0].temp_render, working_graphics_state->temp_render}
			});
			// Draw the calculated number of gratings
			sg_draw(0, 12 * num_gratings, 1);

			sg_end_pass();
		}
		else if (working_viewport->displayMode == viewport_state_t::EyeTrackedHolography2)
		{
			// working_viewport is right eye.
			sg_begin_default_pass(&shared_graphics.default_passAction, viewport->Size.x, viewport->Size.y);

			// Apply grating display pipeline
			sg_apply_viewport(disp_area.Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area.Pos.y - viewport->Pos.y + h), w, h, false);
			sg_apply_scissor_rect(disp_area.Pos.x - viewport->Pos.x, viewport->Size.y - (disp_area.Pos.y - viewport->Pos.y + h), w, h, false);

			sg_apply_pipeline(shared_graphics.grating_display.pip2);

			// Set vertex shader uniforms
		lenticular_interlace_params_t fs_params{
			.disp_area = glm::vec4(disp_area.Pos.x - monitorX, disp_area.Pos.y - monitorY, disp_area.Size.x, disp_area.Size.y),
			.screen_params = glm::vec4((float)monitorWidth, (float)monitorHeight, 0.f, 0.f),
			.fill_color_left = working_viewport->fill_color_left,
			.fill_color_right = working_viewport->fill_color_right,
			.lenticular_left = glm::vec4(
				working_viewport->phase_init_left,
				working_viewport->period_total_left,
				working_viewport->period_fill_left,
				working_viewport->phase_init_row_increment_left),
			.lenticular_right = glm::vec4(
				working_viewport->phase_init_right,
				working_viewport->period_total_right,
				working_viewport->period_fill_right,
				working_viewport->phase_init_row_increment_right),
			.subpx_R = working_viewport->subpx_R,
			.subpx_G = working_viewport->subpx_G,
			.subpx_B = working_viewport->subpx_B
		};

			sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_lenticular_interlace_params, SG_RANGE(fs_params));

			// Set textures
			sg_apply_bindings(sg_bindings{
				.vertex_buffers = {shared_graphics.quad_vertices},
				.fs_images = {graphics_states[0].temp_render, working_graphics_state->temp_render}
			});
			// Draw the calculated number of gratings
			sg_draw(0, 4, 1);

			sg_end_pass();
			}
		sg_commit();
	}

	
	TOC("commit");
	
	// get_viewed_sprites(w, h);
	// TOC("gvs")
	// use async pbo to get things.

	// TOC("sel")

	
	// ImGui::SetNextWindowPos(ImVec2(disp_area.Pos.x + 16 * working_viewport->camera.dpi, disp_area.Pos.y +disp_area.Size.y - 8 * working_viewport->camera.dpi), ImGuiCond_Always, ImVec2(0, 1));

	if (working_viewport == ui.viewports) {

		// ImGuiWindowFlags window_flags = ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_MenuBar;
		// float height = ImGui::GetFrameHeight();
		//
		// if (ImGui::BeginViewportSideBar("##SecondaryMenuBar", viewport, ImGuiDir_Up, height, window_flags)) {
		// 	if (ImGui::BeginMenuBar()) {
		// 		ImGui::Text("Happy secondary menu bar");
		// 		ImGui::EndMenuBar();
		// 	}
		// 	ImGui::End();
		// }
		//
		// if (ImGui::BeginViewportSideBar("##MainStatusBar", viewport, ImGuiDir_Down, height, window_flags)) {
		// 	if (ImGui::BeginMenuBar()) {
		// 		ImGui::Text("Happy status bar");
		// 		ImGui::EndMenuBar();
		// 	}
		// 	ImGui::End();
		// }

		ImGui::SetNextWindowPos(ImVec2(disp_area.Pos.x + 8 * working_viewport->camera.dpi, disp_area.Pos.y + 8 * working_viewport->camera.dpi), ImGuiCond_Always, ImVec2(0, 0));
		ImGui::SetNextWindowViewport(viewport->ID);
		ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(1, 1));
		ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 0);
		auto color = ImGui::GetStyle().Colors[ImGuiCol_WindowBg]; color.w = 0;
		ImGui::PushStyleColor(ImGuiCol_WindowBg, color);
		ImGui::Begin("cyclegui_stat", NULL, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoDocking);

		char truncatedAppName[28];
		if (strlen(appName) > 24) {
			strncpy(truncatedAppName, appName, 24);
			strcpy(truncatedAppName + 24, "...");
		} else {
			strcpy(truncatedAppName, appName);
		}

		char titleBuf[64];
		snprintf(titleBuf, sizeof(titleBuf), "\u2b00 %s", truncatedAppName);
		if (ImGui::Button(titleBuf))
		{
			g_showCycleGuiDebug = true;
			g_selectedViewportIndex = working_viewport_id;
		}
		ImGui::SameLine(0, 5);

		if ( appStat && appStat[0] != '\0')
			ImGui::Text(appStat);

		ImVec2 buttonMin = ImGui::GetItemRectMin();
		ImVec2 buttonMax = ImGui::GetItemRectMax();
		char fpsBuf[64];
		snprintf(fpsBuf, sizeof(fpsBuf), "FPS=%.1f", ImGui::GetIO().Framerate);
		ImGui::Text(fpsBuf);
		// ImDrawList* drawList = ImGui::GetWindowDrawList();
		// ImVec2 fpsPos(buttonMin.x, buttonMax.y + ImGui::GetStyle().ItemInnerSpacing.y);
		// ImU32 textColor = ImGui::GetColorU32(ImGuiCol_Text);
		// drawList->AddText(ImGui::GetFont(), ImGui::GetFontSize(), fpsPos, textColor, fpsBuf);

		ImGui::End();
		ImGui::PopStyleColor();
		ImGui::PopStyleVar();
		ImGui::PopStyleVar();
		DrawCycleGuiDebugWindow(viewport);
	}

	TOC("guizmo");

	DrawViewportMenuBar();
	
	// workspace manipulations:
	ProcessInteractiveFeedback();
	ProcessOperationFeedback();

	// if (ProcessInteractiveFeedback()) 
	// 	goto fin;
	// if (ProcessOperationFeedback()) 
	// 	goto fin;

	fin:
	TOC("fin");
}


void select_operation::pointer_down()
{
	selecting = true;
	if (!ui.shift && !ui.alt)
		ClearSelection();
	if (selecting_mode == click)
	{
		clickingX = ui.mouseX;
		clickingY = ui.mouseY;
		// select but not trigger now.
	}
	else if (selecting_mode == drag)
	{
		select_start_x = working_viewport->mouseX();
		select_start_y = working_viewport->mouseY();
	}
	else if (selecting_mode == paint)
	{
		std::fill(painter_data.begin(), painter_data.end(), 0);
	}
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
void button_widget::process(disp_area_t disp_area, ImDrawList* dl)
{
	// state.
	if (!isKJHandling())
	{
		auto cx = center_uv.x * working_viewport->disp_area.Size.x + center_px.x* working_viewport->camera.dpi;
		auto cy = center_uv.y * working_viewport->disp_area.Size.y + center_px.y* working_viewport->camera.dpi;
		auto rx = 0.5f * (sz_uv.x * working_viewport->disp_area.Size.x + sz_px.x * working_viewport->camera.dpi);
		auto ry = 0.5f * (sz_uv.y * working_viewport->disp_area.Size.y + sz_px.y * working_viewport->camera.dpi);

		// foreach pointer, any pointer would trigger.
		auto px = working_viewport->mouseX();
		auto py = working_viewport->mouseY();
		auto tmp_pressed = ui.mouseLeft && &ui.viewports[ui.mouseCaptuingViewport]==working_viewport
			&& cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry;

		for (int i=0; i<ui.touches.size(); ++i)
		{
			auto& touch = ui.touches[i];
			if (touch.consumed && touch.id != pointer) continue;
			px = touch.touchX - disp_area.Pos.x;
			py = touch.touchY - disp_area.Pos.y;
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
	float w = (working_viewport->disp_area.Size.x * sz_uv.x + sz_px.x * working_viewport->camera.dpi) * 0.5f;
	float h = (working_viewport->disp_area.Size.y * sz_uv.y + sz_px.y * working_viewport->camera.dpi) * 0.5f;
	float rounding = std::min(w, h);
	{
		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi;
		dl->AddRectFilled(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xaa5c0751, rounding);
		auto c = !pressed ? (ImU32)ImColor::HSV(0.1f * id + 0.1f, 0.6, 1, 0.5) : (ImU32)ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.7);
		dl->AddRectFilled(ImVec2(cx - w, cy - h + (!pressed? -4:4)), ImVec2(cx + w, cy + h+(!pressed?-4:1)), c, rounding);
	}
	{
		auto sz = ImGui::CalcTextSize(display_text.c_str());

		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi - sz.x * 0.5f;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi - sz.y * 0.5f + (!pressed ? -4 : 0);

		dl->AddText(ImVec2(cx+1, cy+1), 0xff444444, display_text.c_str());
		dl->AddText(ImVec2(cx, cy), 0xffffffff, display_text.c_str());
	}
}

void toggle_widget::keyboardjoystick_map()
{
	if (keyboard_press.size() == 1 && keyboard_press[0]){
		if (lastPressCnt + 1 != ui.loopCnt)
			on = !on;
		lastPressCnt = ui.loopCnt;
	}
}
void toggle_widget::process(disp_area_t disp_area, ImDrawList* dl)
{
	// state.
	if (!isKJHandling())
	{
		auto cx = center_uv.x * working_viewport->disp_area.Size.x + center_px.x* working_viewport->camera.dpi;
		auto cy = center_uv.y * working_viewport->disp_area.Size.y + center_px.y* working_viewport->camera.dpi;
		auto rx = 0.5f * (sz_uv.x * working_viewport->disp_area.Size.x + sz_px.x * working_viewport->camera.dpi);
		auto ry = 0.5f * (sz_uv.y * working_viewport->disp_area.Size.y + sz_px.y * working_viewport->camera.dpi);

		// foreach pointer:
		auto px = working_viewport->mouseX();
		auto py = working_viewport->mouseY();
		if ( ui.mouseLeft && &ui.viewports[ui.mouseCaptuingViewport]==working_viewport && 
			ui.loopCnt== gesture_operation::trigger_loop) {
			if (cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry) {
				on = !on;
				// todo: consume the input.
			}
			pointer = -1; //not touched.
		}else{		
			for (int i=0; i<ui.touches.size(); ++i)
			{
				auto& touch = ui.touches[i];
				if (touch.id == pointer) touch.consumed = true;
				if (touch.consumed || !touch.starting) continue;
				px = touch.touchX - disp_area.Pos.x;
				py = touch.touchY - disp_area.Pos.y;
				if (cx - rx <= px && px <= cx + rx && cy - ry <= py && py <= cy + ry)
				{
					on = !on;
					touch.consumed = true;
					pointer = touch.id;
					break;
				}
			}

			auto resetPointer = true;
			for (int i = 0; i < ui.touches.size(); ++i)
				if (ui.touches[i].id == pointer) resetPointer = false;
			if (resetPointer) pointer = -1;
		}
	}

	// draw.
	float w = (working_viewport->disp_area.Size.x * sz_uv.x + sz_px.x * working_viewport->camera.dpi) * 0.5f;
	float h = (working_viewport->disp_area.Size.y * sz_uv.y + sz_px.y * working_viewport->camera.dpi) * 0.5f;
	float w2 = (working_viewport->disp_area.Size.x * sz_uv.x + (sz_px.x) * working_viewport->camera.dpi) * 0.5f * 0.6f - 4 * working_viewport->camera.dpi;
	float h2 = (working_viewport->disp_area.Size.y * sz_uv.y + (sz_px.y) * working_viewport->camera.dpi) * 0.5f - 4 * working_viewport->camera.dpi;
	float rounding = std::min(std::min(w, h), std::min(w2, h2));
	{
		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi;
		dl->AddRectFilled(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xa0333333, rounding);
		dl->AddRect(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xaa5c0751, rounding);
	}
	{
		float cx = disp_area.Pos.x + (disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi) + (on?1:-1) * w * 0.4;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi;
		auto c = on?(ImU32)ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.5):0x90a0a0a0;
		dl->AddRectFilled(ImVec2(cx - w2, cy - h2), ImVec2(cx + w2, cy + h2), c, rounding);
	}
	{
		auto sz = ImGui::CalcTextSize(display_text.c_str());

		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi - sz.x * 0.5f;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi - sz.y * 0.5f;

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
void throttle_widget::process(disp_area_t disp_area, ImDrawList* dl)
{
	// state.
	if (!isKJHandling())
	{
		auto cx = center_uv.x * working_viewport->disp_area.Size.x + center_px.x* working_viewport->camera.dpi;
		auto cy = center_uv.y * working_viewport->disp_area.Size.y + center_px.y* working_viewport->camera.dpi;
		auto rx = 0.5f * (sz_uv.x * working_viewport->disp_area.Size.x + sz_px.x * working_viewport->camera.dpi);
		auto ry = 0.5f * (sz_uv.y * working_viewport->disp_area.Size.y + sz_px.y * working_viewport->camera.dpi);

		// foreach pointer:
		auto px = working_viewport->mouseX();
		auto py = working_viewport->mouseY();
		if (ui.mouseLeft && &ui.viewports[ui.mouseCaptuingViewport]==working_viewport && 
			  ui.loopCnt== gesture_operation::trigger_loop && pointer == -1) {
			if ((onlyHandle && cx + (current_pos - 0.15) * rx <= px && px <= cx + (current_pos + 0.15) * rx ||
				cx - rx <= px && px <= cx + rx) && cy - ry <= py && py <= cy + ry) {
				pointer = -2; // indicate it's mouse input.
				// todo: consume the input.
			}
		}else
		{
			for (int i=0; i<ui.touches.size(); ++i)
			{
				auto& touch = ui.touches[i];
				px = touch.touchX - disp_area.Pos.x;
				py = touch.touchY - disp_area.Pos.y;
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


		if (pointer == -2 && !ui.mouseLeft ||
			pointer >=0 && std::all_of(ui.touches.begin(), ui.touches.end(), [this](const touch_state& p) { return p.id != this->pointer; }))
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
	float w = (working_viewport->disp_area.Size.x * sz_uv.x + sz_px.x * working_viewport->camera.dpi) * 0.5f;
	float h = (working_viewport->disp_area.Size.y * sz_uv.y + sz_px.y * working_viewport->camera.dpi) * 0.5f;
	float rounding = std::min(w, h)*0.8;
	{
		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi;
		dl->AddRectFilled(ImVec2(cx - w, cy - h), ImVec2(cx + w, cy + h), 0xaa5c0751, rounding);
		dl->AddRectFilled(ImVec2(cx - w+4, cy - h+4), ImVec2(cx + w-4, cy + h), 0xa0335333,rounding);
		// dl->AddQuadFilled(ImVec2(cx + w, cy + h), ImVec2(cx + w, cy -h), ImVec2(cx -w, cy -h), ImVec2(cx -w, cy + h), ImColor::HSV(0.1f * id, 1, 1, 1));
	}
	{
		float w2 = (working_viewport->disp_area.Size.x * sz_uv.x + (sz_px.x) * working_viewport->camera.dpi) * 0.5f * 0.4f - 4 * working_viewport->camera.dpi;
		float h2 = (working_viewport->disp_area.Size.y * sz_uv.y + (sz_px.y) * working_viewport->camera.dpi) * 0.5f;
		float cx = disp_area.Pos.x + (disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi) + current_pos * w * 0.6;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi;
		ImColor c = ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.5);
		dl->AddRectFilled(ImVec2(cx - w2, cy - h2+2), ImVec2(cx + w2, cy + h2), 0xee222222, rounding);
		dl->AddRectFilled(ImVec2(cx - w2, cy - h2+2), ImVec2(cx + w2, cy + h2 - 4), c, rounding);
	}
	{
		char value_s[40];
		sprintf(value_s, "%s:%0.2f", display_text.c_str(), value());
		auto sz = ImGui::CalcTextSize(value_s);

		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi - sz.x * 0.5f;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi - sz.y * 0.5f;

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

			skipped = false;
		}
		// else if (previouslyKJHandled)
		// {
		// 	if (bounceBack)
		// 	{
		// 		current_pos += (init_pos - current_pos) * 0.1f;
		// 	}
		// }
	}
}
void stick_widget::process(disp_area_t disp_area, ImDrawList* dl)
{
	// state.
	auto sz = std::min(sz_uv.x * working_viewport->disp_area.Size.x + sz_px.x * working_viewport->camera.dpi, sz_uv.y * working_viewport->disp_area.Size.y + sz_px.y * working_viewport->camera.dpi) * 0.5f;

	if (!isKJHandling())
	{
		auto cx = center_uv.x * working_viewport->disp_area.Size.x + center_px.x* working_viewport->camera.dpi;
		auto cy = center_uv.y * working_viewport->disp_area.Size.y + center_px.y* working_viewport->camera.dpi;

		// foreach pointer:
		auto px = working_viewport->mouseX();
		auto py = working_viewport->mouseY();
		if (ui.mouseLeft && &ui.viewports[ui.mouseCaptuingViewport]==working_viewport && 
			 ui.loopCnt== gesture_operation::trigger_loop && pointer == -1) {
			if ((onlyHandle && cx + (current_pos.x - 0.15) * sz <= px && px <= cx + (current_pos.x + 0.15) * sz || cx - sz <= px && px <= cx + sz) && 
				(onlyHandle && cy + (current_pos.y - 0.15) * sz <= py && py <= cy + (current_pos.y + 0.15) * sz || cy - sz <= py && py <= cy + sz)) {
				pointer = -2;
				// todo: consume the input.
			}
		}else
		{
			for (int i=0; i<ui.touches.size(); ++i)
			{
				auto& touch = ui.touches[i];
				px = touch.touchX - disp_area.Pos.x;
				py = touch.touchY - disp_area.Pos.y;
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

		if (pointer == -2 && !ui.mouseLeft ||
			pointer >= 0 && std::all_of(ui.touches.begin(), ui.touches.end(), [this](const touch_state& p) { return p.id != this->pointer; }))
		{
			// todo: replace with "pointer released"
			pointer = -1;
		}


		if (pointer != -1)
		{
			current_pos = glm::clamp(glm::vec2((px - cx) / (sz * 0.7f),(py - cy) / (sz * 0.7f)), -1.0f, 1.0f);
			skipped = false;
		}
		// else
		// {
		// 	if (bounceBack)
		// 	{
		// 		current_pos += (init_pos - current_pos) * 0.1f;
		// 	}
		// }
	}

	// draw.
	float rounding = sz * 0.666;
	{
		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi;
		dl->AddRectFilled(ImVec2(cx - sz, cy - sz), ImVec2(cx + sz, cy + sz), 0xaa5c0751, rounding);
		dl->AddRectFilled(ImVec2(cx - sz+2, cy -sz+2), ImVec2(cx + sz, cy + sz), 0xa0335333, rounding);
		dl->AddTriangleFilled(ImVec2(cx - sz * 0.8, cy), ImVec2(cx - sz * 0.7, cy + sz * 0.1), ImVec2(cx - sz * 0.7, cy - sz * 0.1), 0xa0221122);
		dl->AddTriangleFilled(ImVec2(cx + sz * 0.8, cy), ImVec2(cx + sz * 0.7, cy + sz * 0.1), ImVec2(cx + sz * 0.7, cy - sz * 0.1), 0xa0221122);
		dl->AddTriangleFilled(ImVec2(cx, cy - sz * 0.8), ImVec2(cx + sz * 0.1, cy - sz * 0.7), ImVec2(cx - sz * 0.1, cy - sz * 0.7), 0xa0221122);
		dl->AddTriangleFilled(ImVec2(cx, cy + sz * 0.8), ImVec2(cx + sz * 0.1, cy + sz * 0.7), ImVec2(cx - sz * 0.1, cy + sz * 0.7), 0xa0221122);
		// dl->AddQuadFilled(ImVec2(cx + w, cy + h), ImVec2(cx + w, cy -h), ImVec2(cx -w, cy -h), ImVec2(cx -w, cy + h), ImColor::HSV(0.1f * id, 1, 1, 1));
	}
	{
		float w2 = (working_viewport->disp_area.Size.x * sz_uv.x + (sz_px.x) * working_viewport->camera.dpi) * 0.5f * 0.6f - 4 * working_viewport->camera.dpi;
		float h2 = (working_viewport->disp_area.Size.y * sz_uv.y + (sz_px.y) * working_viewport->camera.dpi) * 0.5f * 0.6f - 4 * working_viewport->camera.dpi;
		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi + current_pos.x * sz * 0.4;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi + current_pos.y * sz * 0.4;
		float r = std::min(w2, h2);
		ImColor c = ImColor::HSV(0.1f * id + 0.1f, 1, 1, 0.5);
		dl->AddRectFilled(ImVec2(cx - r, cy - r+2), ImVec2(cx + r, cy + r), 0xee222222, rounding);
		dl->AddRectFilled(ImVec2(cx - r, cy - r+2), ImVec2(cx + r, cy + r - 4), c, rounding);
	}
	{
		char value_s[40];
		sprintf(value_s, "%s\n%0.2f,%0.2f", display_text.c_str(), current_pos.x, current_pos.y);
		auto sz = ImGui::CalcTextSize(value_s);

		float cx = disp_area.Pos.x + disp_area.Size.x * center_uv.x + center_px.x * working_viewport->camera.dpi - sz.x * 0.5f;
		float cy = disp_area.Pos.y + disp_area.Size.y * center_uv.y + center_px.y * working_viewport->camera.dpi - sz.y * 0.5f;

		dl->AddText(ImVec2(cx+1, cy+1), 0xff444444, value_s);
		dl->AddText(ImVec2(cx, cy), 0xffffffff, value_s);
	}
}

void stick_widget::process_default()
{
	if (bounceBack)
	{
		current_pos += (init_pos - current_pos) * 0.1f;
	}
}


void gesture_operation::draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm)
{
	for(int i=0; i<widgets.ls.size(); ++i)
	{
		auto w = widgets.get(i);
		w->id = i;
		w->skipped = true;
		w->process_keyboardjoystick();

		if (!(disp_area.Size.x==0 && disp_area.Size.y==0))
			w->process(disp_area, dl);

		if (w->skipped)
			w->process_default();
	}
	working_viewport->workspace_state.back().feedback = realtime_event;
}
gesture_operation::~gesture_operation()
{
	for (int i=0; i< widgets.ls.size(); ++i){
		auto gesture = widgets.get(i);
		delete gesture;
	}
}

void InitGraphics()
{
	InitPlatform();
	init_graphics();
}

void initialize_viewport(int id, int w, int h)
{
	printf("initialize viewport %d: %dx%d\n", id, w, h);
	ui.viewports[id].workspace_state.reserve(16);
	ui.viewports[id].workspace_state.push_back(workspace_state_desc{.id = 0, .name = "default", .operation = new no_operation});
	ui.viewports[id].camera.init(glm::vec3(0.0f, 0.0f, 0.0f), 10, w, h, 0.2);
	ui.viewports[id].disp_area.Size.x = w;
	ui.viewports[id].disp_area.Size.y = h;

	if (id == 0)
		ui.viewports[id].workspaceCallback = global_workspaceCallback;
	else {
		ui.viewports[id].workspaceCallback = aux_workspace_notify;
		// for auxiliary viewports, we process feedback vias stateCallback in UIstack.
	}

	switch_context(id);

	GenPasses(w, h);
	ui.viewports[id].graphics_inited = true;

}
//WORKSPACE FEEDBACK

#define WSFeedInt32(x) { *(int*)pr=x; pr+=4;}
#define WSFeedFloat(x) { *(float*)pr=x; pr+=4;}
#define WSFeedDouble(x) { *(double*)pr=x; pr+=8;}
#define WSFeedBytes(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WSFeedString(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WSFeedBool(x) {*(bool*)pr=x; pr+=1;}

// should be called inside before draw.

bool TestSpriteUpdate(unsigned char*& pr)
{
	// seems not needed.
	// if (!shared_graphics.allowData) 
	// 	return false;
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
	std::vector<me_rgba*> allocateList, candidateList; //allocate in atlas, candidate to update rgba 
	auto updateAtlas = -1;

	// at least find one atlas to update if needed.
	for (int i = 0; i < shown_rgba.size(); ++i)
	{
		if (shown_rgba[i]->width <= 0) continue;
		if (!shown_rgba[i]->invalidate && shown_rgba[i]->loaded) continue;
		if (shown_rgba[i]->width > atlas_sz || shown_rgba[i]->height > atlas_sz) continue; // definitely cannot fit.
		if (shown_rgba[i]->atlasId == -1)
		{
			// if not selected atlas to register, select an atlas.
			if (updateAtlas == -1)
			{
				//try to fit in at least one atlas. then we fill data for this atlas.
				std::vector<int> atlasSeq(argb_store.atlasNum);
				for (int j = 0; j < argb_store.atlasNum; ++j) atlasSeq[j] = j;
				std::sort(atlasSeq.begin(), atlasSeq.end(), [&pixels](const int& a, const int& b) {
					return pixels[a] > pixels[b];
					});
				for (int j = 0; j < argb_store.atlasNum; ++j)
				{
					if (shown_rgba[i]->width * shown_rgba[i]->height + pixels[atlasSeq[j]] > 4096 * 4096 * 0.9) break;
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

				// todo: atlas is insufficient.
				// todo 1: argb_store.atlasNum<16. expand atlas array by factor 2, at most 16(0xf), and copy existing atlas pixels to new atlas. by expanding we have new atals.
				if (argb_store.atlasNum < 32) {
					// Expand atlas array by factor of 2, at most 16
					int new_atlas_num = std::min(argb_store.atlasNum * 2, 32);
					
					// Create new atlas with more slices
					sg_image new_atlas = sg_make_image(sg_image_desc{
						.type = SG_IMAGETYPE_ARRAY,
						.width = atlas_sz,
						.height = atlas_sz,
						.num_slices = new_atlas_num,
						.usage = SG_USAGE_STREAM,
						.pixel_format = SG_PIXELFORMAT_RGBA8,
						.min_filter = SG_FILTER_LINEAR,
						.mag_filter = SG_FILTER_LINEAR,
					});
					printf("atlas number = %d >> %d\n", argb_store.atlasNum, new_atlas_num);
					// Copy existing atlas data to new atlas using platform function
					if (CopyTexArr(argb_store.atlas, new_atlas, argb_store.atlasNum, atlas_sz)) {
						// Destroy old atlas and replace with new one
						sg_destroy_image(argb_store.atlas);
						argb_store.atlas = new_atlas;
						
						// Update global texture array ID
						_sg_image_t* img = _sg_lookup_image(&_sg.pools, argb_store.atlas.id);
						rgbaTextureArrayID = img->gl.tex[img->cmn.active_slot];
						
						// Resize tracking vectors
						pixels.resize(new_atlas_num);
						reverseIdx.resize(new_atlas_num);
						
						// Use the newly created atlas slice (old atlasNum)
						updateAtlas = argb_store.atlasNum;
						argb_store.atlasNum = new_atlas_num;
						allocateList.clear(); // New atlas slice is empty
						goto atlasFound;
					}
				}
				
				// todo 2: no more atlas to create. we select an atlas with least viewed pixels, but allocate list won't contain rgba that is not viewed(occurrence==0).
				if (updateAtlas == -1) {
					// Find atlas with least viewed pixels (only considering currently viewed textures)
					int min_viewed_pixels = INT_MAX;
					int selected_atlas = -1;
					
					for (int j = 0; j < argb_store.atlasNum; ++j) {
						int viewed_pixels = 0;
						for (auto rgba_ptr : reverseIdx[j]) {
							if (rgba_ptr->occurrence > 0) { // only count currently viewed textures
								viewed_pixels += rgba_ptr->width * rgba_ptr->height;
							}
						}
						if (viewed_pixels < min_viewed_pixels) {
							min_viewed_pixels = viewed_pixels;
							selected_atlas = j;
						}
					}
					
					if (selected_atlas != -1) {
						updateAtlas = selected_atlas;
						// Build allocate list with only currently viewed textures
						allocateList.clear();
						for (auto rgba_ptr : reverseIdx[selected_atlas]) {
							if (rgba_ptr->occurrence > 0) { // only include currently viewed textures
								allocateList.push_back(rgba_ptr);
							}
						}
					}
				}
			}
		atlasFound:
			allocateList.push_back(shown_rgba[i]);
		}
		else
		{
			// already allocated(assigned atlasid/uv) but not loaded, ask backend to transfer.
			candidateList.push_back(shown_rgba[i]);
		}
	}

	// now we revise the "updatingAtlas", one for each iteration.
	if (updateAtlas >= 0)
	{
		// backup previous atlas rgba's uv.
		auto old_len = reverseIdx[updateAtlas].size();
		std::vector<glm::vec4> uvuvsrc(old_len);
		for (int i = 0; i < uvuvsrc.size(); ++i)
			uvuvsrc[i] = glm::vec4(allocateList[i]->uvStart, allocateList[i]->uvEnd);

		// try pack.
		std::vector<rect_type> rects(allocateList.size());
		std::vector<bool> allocated(allocateList.size());
		for (int k = 0; k < allocateList.size(); ++k)
		{
			rects[k].w = allocateList[k]->width;
			rects[k].h = allocateList[k]->height;
		}
		int shuffle_out = 0;
		int shuffle_plc = 0;
		int new_come = 0;
		auto report_successful = [&](rect_type& r) {
			// allocated.
			auto id = &r - rects.data();
			if (allocateList[id]->atlasId == -1) new_come += 1;
			else shuffle_plc += 1;
			allocateList[id]->atlasId = updateAtlas;
			allocateList[id]->uvStart = glm::vec2(r.x, r.y+r.h);
			allocateList[id]->uvEnd = glm::vec2((r.x + r.w), (r.y));
			allocated[id] = true;
			candidateList.push_back(allocateList[id]);
			return rectpack2D::callback_result::CONTINUE_PACKING;
			};
		auto report_unsuccessful = [&](rect_type& ri) {
			auto id = &ri - rects.data();
			allocated[id] = false;
			if (allocateList[id]->atlasId > -1) shuffle_out += 1;
			allocateList[id]->atlasId = -1;
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
		std::vector<shuffle> buffer; buffer.reserve(old_len);
		for (int i=0; i< old_len; ++i)
		{
			if (allocateList[i]->atlasId == updateAtlas && uvuvsrc[i] != glm::vec4(allocateList[i]->uvStart, allocateList[i]->uvEnd))
				buffer.push_back({ uvuvsrc[i], glm::vec4(allocateList[i]->uvStart, allocateList[i]->uvEnd) });
		}
		if (buffer.size() > 0) {
			printf("permute:%d out, %d in, %d replace\n", shuffle_out, new_come, buffer.size());
			// atlas permutation:
			PermuteAtlasSlice(argb_store.atlas, updateAtlas, atlas_sz, buffer);
		}
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
		shared_graphics.allowData = false;

		return true;
	}
	return false;
}

bool CaptureViewport(unsigned char*& pr)
{
	auto& wstate = working_viewport->workspace_state.back();
	if (!wstate.captureRenderedViewport)
		return false;
	wstate.captureRenderedViewport = false;

	int width, height;
	if (!getTextureWH(working_graphics_state->temp_render, width, height))
		return false;
	printf("capture viewport %d...\n", working_viewport_id);

    // Allocate buffer for RGBA texture data
    std::vector<unsigned char> rgba_pixels(width * height * 4);
    std::vector<unsigned char> rgb_pixels(width * height * 3);

	me_getTexBytes(working_graphics_state->temp_render, rgba_pixels.data(), 0, 0, width, height);

	WSFeedInt32(-1);
	WSFeedInt32(2);
    WSFeedInt32(width);
    WSFeedInt32(height);
	

    // Convert BGRA to RGB and flip Y-axis
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            // Calculate source and destination indices
            int src_idx = (y * width + x) * 4;                    // Source index in BGRA data
            int dst_idx = ((height - 1 - y) * width + x) * 3;     // Destination index in RGB data, flipped Y
            
            rgb_pixels[dst_idx + 0] = rgba_pixels[src_idx + 1]; 
            rgb_pixels[dst_idx + 1] = rgba_pixels[src_idx + 0]; 
            rgb_pixels[dst_idx + 2] = rgba_pixels[src_idx + 2]; 
        }
    }

    // Feed RGB pixel data
    WSFeedBytes(rgb_pixels.data(), width * height * 3);

	return true;
}

void DrawViewportMenuBar()
{
	if (!working_viewport->showMainMenuBar)
		return;

	auto my_ptr = working_viewport->mainMenuBarData;
	auto pr = working_viewport->ws_feedback_buf;
#define MyReadInt *((int*)my_ptr); my_ptr += 4
#define MyReadString (char*)(my_ptr + 4); my_ptr += *((int*)my_ptr) + 4

	std::vector<int> path;
	bool clicked = false;

	std::function<void(int)> process = [&process, &path, &my_ptr, &pr, &clicked](const int pos) {
		path.push_back(pos);

		auto type = MyReadInt;
		auto attr = MyReadInt;
		auto label = MyReadString;

		auto has_action = (attr & (1 << 0)) != 0;
		auto has_shortcut = (attr & (1 << 1)) != 0;
		auto selected = (attr & (1 << 2)) != 0;
		auto enabled = (attr & (1 << 3)) != 0;

		char* shortcut = nullptr;
		if (has_shortcut)
		{
			shortcut = MyReadString;
		}

		if (type == 0)
		{
			if (ImGui::MenuItem(label, shortcut, selected, enabled) && has_action)
			{
				auto pathLen = (int)path.size();
				auto ret = new int[pathLen];
				// ret[0] = pathLen;
				for (int k = 0; k < pathLen; ++k) ret[k] = path[k];
				WSFeedInt32(-1);
				WSFeedInt32(3);
				WSFeedBytes(ret, pathLen * 4);
				clicked = true;
			}
		}
		else
		{
			auto byte_cnt = MyReadInt;
			if (ImGui::BeginMenu(label, enabled))
			{
				auto sub_cnt = MyReadInt;
				for (int sub = 0; sub < sub_cnt; sub++) process(sub);
				ImGui::EndMenu();
			}
			else my_ptr += byte_cnt;
		}

		path.pop_back();
	};

	if (ImGui::BeginMainMenuBar())
	{
		auto all_cnt = MyReadInt;
		for (int cnt = 0; cnt < all_cnt; cnt++)
			process(cnt);
		ImGui::EndMainMenuBar();
	}

	working_viewport->clicked = clicked;
	working_viewport->mainmenu_cached_pr = pr;
}

bool MainMenuBarResponse(unsigned char*& pr)
{
	auto clicked = working_viewport->clicked;
	working_viewport->clicked = false;
	if (clicked){
		pr = working_viewport->mainmenu_cached_pr;
	}
	return clicked;
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

void select_operation::draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm)
{
	auto radius = paint_selecting_radius;
	if (selecting_mode == paint && !selecting)
	{
		auto pos = disp_area.Pos;
		dl->AddCircle(ImVec2(working_viewport->mouseX() + pos.x, working_viewport->mouseY() + pos.y), radius, 0xff0000ff);
	}
	if (selecting)
	{
		if (selecting_mode == drag)
		{
			auto pos = disp_area.Pos;
			auto st = ImVec2(std::min(working_viewport->mouseX(), select_start_x) + pos.x, std::min(working_viewport->mouseY(), select_start_y) + pos.y);
			auto ed = ImVec2(std::max(working_viewport->mouseX(), select_start_x) + pos.x, std::max(working_viewport->mouseY(), select_start_y) + pos.y);
			dl->AddRectFilled(st, ed, 0x440000ff);
			dl->AddRect(st, ed, 0xff0000ff);
		}
		else if (selecting_mode == paint)
		{
			auto pos = disp_area.Pos;
			dl->AddCircleFilled(ImVec2(working_viewport->mouseX() + pos.x, working_viewport->mouseY() + pos.y), radius, 0x440000ff);
			dl->AddCircle(ImVec2(working_viewport->mouseX() + pos.x, working_viewport->mouseY() + pos.y), radius, 0xff0000ff);

			auto w = disp_area.Size.x, h = disp_area.Size.y;

			// draw_image.
			for (int j = (working_viewport->mouseY() - radius) / 4; j <= (working_viewport->mouseY() + radius) / 4 + 1; ++j)
				for (int i = (working_viewport->mouseX() - radius) / 4; i <= (working_viewport->mouseX() + radius) / 4 + 1; ++i)
				{
					if (0 <= i && i < w / 4 && 0 <= j && j < h / 4 &&
						sqrtf((i * 4 - working_viewport->mouseX()) * (i * 4 - working_viewport->mouseX()) +
							(j * 4 - working_viewport->mouseY()) * (j * 4 - working_viewport->mouseY())) < radius)
					{
						painter_data[j * (w / 4) + i] = 255;
					}
				}

			//update texture;
			sg_update_image(working_graphics_state->ui_selection, sg_image_data{
					.subimage = {{ {painter_data.data(), (size_t)((w / 4) * (h / 4))} }}
				});
			working_graphics_state->use_paint_selection = true;
		}
	}
}
void select_operation::feedback(unsigned char*& pr)
{
	for (int gi = 0; gi < global_name_map.ls.size(); ++gi){
		auto nt = global_name_map.get(gi);
		auto name = global_name_map.getName(gi);
		RouteTypes(nt, 
			[&]	{
				// point cloud.
				auto t = (me_pcRecord*)nt->obj;
				
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
			}, [&](int class_id)
			{
				// gltf
				auto t = (gltf_object*)nt->obj;
				auto cls = gltf_classes.get(class_id);
				
				if (t->flags[working_viewport_id] & (1 << 3))
				{
					// selected as whole.
					WSFeedInt32(0);
					WSFeedString(name.c_str(), name.length());
				}
				if (t->flags[working_viewport_id] & (1 << 6))
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
				}

			}, [&]
			{
				// line piece.
				auto t = (me_line_piece*)nt->obj;
				if (t->flags[working_viewport_id] & (1<<3))
				{
					WSFeedInt32(0);
					WSFeedString(name.c_str(), name.length());
				}
			}, [&]
			{
				// sprites;
				auto t = (me_sprite*)nt->obj;
				if (t->per_vp_stat[working_viewport_id] & (1<<1)){
					WSFeedInt32(0);
					WSFeedString(name.c_str(), name.length());
				}
			},[&]
			{
				// spot texts.
			},[&]
			{
				// geometry.
			});
	}

	WSFeedInt32(-1);
}

void positioning_operation::draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm)
{
	// mouse pointer.
	auto mouseX = working_viewport->mouseX();
	auto mouseY = working_viewport->mouseY();

	auto& wstate = working_viewport->workspace_state.back();

	if (working_viewport->hover_obj != nullptr) {
		for (int i = 0; i < snaps.size(); ++i)
		{
			if (wildcardMatch(working_viewport->hover_obj->name, snaps[i]))
			{
				auto v3 = working_viewport->hover_obj->current_pos;
				if (working_viewport->hover_type == 1)
				{
					// todo: point cloud: use pointing point.
				}
				else if (working_viewport->hover_type == 2 && working_viewport->hover_instance_id < 0)
				{
					// line piece.
					auto t = (me_line_piece*)working_viewport->hover_obj;

					// Get mouse ray
					// glm::vec4 depthValue;
					// me_getTexFloats(working_graphics_state->primitives.depth, &depthValue, mouseX, disp_area.Size.y - mouseY, 1, 1);

					auto invPV = glm::inverse(pm * vm);
					float ndcX = (2.0f * mouseX) / disp_area.Size.x - 1.0f;
					float ndcY = 1.0f - (2.0f * mouseY) / disp_area.Size.y; // Flip Y
					glm::vec4 rayNDC = glm::vec4(ndcX, ndcY, -1.0f, 1.0f);
					glm::vec4 rayWorld = invPV * rayNDC;
					rayWorld /= rayWorld.w;
					glm::vec3 rayOrigin = working_viewport->camera.getPos();
					glm::vec3 rayDir = glm::normalize(glm::vec3(rayWorld) - rayOrigin);

					// Get line endpoints
					glm::vec3 lineStart = t->propSt.obj != nullptr ? t->propSt.obj->current_pos : t->attrs.st;
					glm::vec3 lineEnd = t->propEnd.obj != nullptr ? t->propEnd.obj->current_pos : t->attrs.end;

					if (t->type == me_line_piece::straight) {
						// For straight line, find closest point using the classic point-to-line formula
						glm::vec3 lineVec = lineEnd - lineStart;
						float lineLength = glm::length(lineVec);

						if (lineLength < 0.00001f) {
							// Line is effectively a point
							v3 = lineStart;
						}
						else {
							glm::vec3 lineDir = lineVec / lineLength;

							// Project ray origin onto line
							float projDist = glm::dot(rayOrigin - lineStart, lineDir);
							glm::vec3 projectedPoint = lineStart + projDist * lineDir;

							// Get closest point on line to ray origin
							float t_param = projDist / lineLength;
							t_param = glm::clamp(t_param, 0.0f, 1.0f); // Clamp to line segment

							// Final snap point
							v3 = lineStart + t_param * lineVec;

							// Find point along ray closest to the line segment
							glm::vec3 toLine = v3 - rayOrigin;
							float distAlongRay = glm::dot(toLine, rayDir);

							if (distAlongRay > 0) {
								// Ray is pointing toward line, use 3D distance between line and ray
								glm::vec3 rayPlaneNormal = glm::cross(lineDir, glm::cross(rayDir, lineDir));
								if (glm::length(rayPlaneNormal) > 0.00001f) {
									rayPlaneNormal = glm::normalize(rayPlaneNormal);

									// Plane containing the ray and normal to line
									float d = glm::dot(rayPlaneNormal, lineStart);

									// Intersection point
									float t = (d - glm::dot(rayPlaneNormal, rayOrigin)) /
										glm::dot(rayPlaneNormal, rayDir);

									if (t > 0) { // In front of camera
										glm::vec3 rayHitPoint = rayOrigin + t * rayDir;

										// Project hit point onto line to get final snap point
										float proj = glm::dot(rayHitPoint - lineStart, lineDir);
										proj = glm::clamp(proj, 0.0f, lineLength);
										v3 = lineStart + proj * lineDir;
									}
								}
							}
						}
					}
					else if (t->type == me_line_piece::bezier) {
						// For bezier curve, we'll evaluate it as a series of line segments
						float minDist = FLT_MAX;
						float minDistSq = FLT_MAX;
						glm::vec3 closestPoint = lineStart;

						std::vector<glm::vec3> curve_points;
						curve_points.push_back(lineStart);

						// Setup control points like in the rendering code
						if (t->ctl_pnt.size() == 1) {
							glm::vec3 cp = t->ctl_pnt[0];
							glm::vec3 cp1 = lineStart + (cp - lineStart) * 0.75f;
							glm::vec3 cp2 = lineEnd + (cp - lineEnd) * 0.75f;
							curve_points.push_back(cp1);
							curve_points.push_back(cp2);
						}
						else if (t->ctl_pnt.size() >= 2) {
							curve_points.push_back(t->ctl_pnt[0]);
							curve_points.push_back(t->ctl_pnt[1]);
						}
						else {
							// Fallback with no control points (should never happen)
							curve_points.push_back((lineStart + lineEnd) * 0.33f);
							curve_points.push_back((lineStart + lineEnd) * 0.67f);
						}

						curve_points.push_back(lineEnd);

						// Generate points along the bezier curve
						const int segments = 8; // Number of line segments to approximate the curve

						const glm::vec3& P0 = curve_points[0];
						const glm::vec3& P1 = curve_points[1];
						const glm::vec3& P2 = curve_points[2];
						const glm::vec3& P3 = curve_points[3];

						// Generate points along the bezier curve
						std::vector<glm::vec3> bezierPoints;
						bezierPoints.push_back(P0); // Start point

						for (int i = 1; i <= segments; i++) {
							float t = static_cast<float>(i) / segments;
							float t2 = t * t;
							float t3 = t2 * t;
							float mt = 1.0f - t;
							float mt2 = mt * mt;
							float mt3 = mt2 * mt;

							// Compute point on bezier curve
							glm::vec3 curvePoint =
								mt3 * P0 +
								3.0f * mt2 * t * P1 +
								3.0f * mt * t2 * P2 +
								t3 * P3;

							bezierPoints.push_back(curvePoint);
						}
						// Now find the closest segment to the ray
						for (int i = 0; i < bezierPoints.size() - 1; i++) {
							glm::vec3 segStart = bezierPoints[i];
							glm::vec3 segEnd = bezierPoints[i + 1];
							glm::vec3 segVec = segEnd - segStart;
							float segLength = glm::length(segVec);

							if (segLength < 0.00001f) continue;

							// Calculate closest point on segment to ray using the same formula as straight line
							glm::vec3 segDir = segVec / segLength;
							glm::vec3 w0 = rayOrigin - segStart;

							float a = glm::dot(rayDir, rayDir);
							float b = glm::dot(rayDir, segDir);
							float c = glm::dot(segDir, segDir);
							float d = glm::dot(rayDir, w0);
							float e = glm::dot(segDir, w0);

							float denom = a * c - b * b;

							glm::vec3 segPoint;

							// Check if ray and segment are nearly parallel
							if (abs(denom) < 0.00001f) {
								// Project ray origin onto segment
								float t = e;
								t = glm::clamp(t, 0.0f, segLength);
								segPoint = segStart + t * segDir;
							}
							else {
								// Calculate parameters for closest points
								float t1 = (b * e - c * d) / denom;
								float t2 = (a * e - b * d) / denom;

								// Clamp segment parameter to segment bounds
								t2 = glm::clamp(t2, 0.0f, segLength);
								segPoint = segStart + t2 * segDir;
							}

							// Calculate squared distance to ray
							glm::vec3 rayToPoint = segPoint - rayOrigin;
							float projOnRay = glm::dot(rayToPoint, rayDir);

							// Only consider points in front of camera
							if (projOnRay > 0) {
								glm::vec3 closestOnRay = rayOrigin + projOnRay * rayDir;
								float dist = glm::length(segPoint - closestOnRay);

								if (dist < minDist) {
									minDist = dist;
									closestPoint = segPoint;
								}
							}
						}

						v3 = closestPoint;
					}
				}


				// Convert 3D world position to screen coordinates
				glm::vec2 screenPos = world2pixel(v3, vm, pm, glm::vec2(disp_area.Size.x, disp_area.Size.y));

				// Add display area offset to get absolute screen position
				float screenX = screenPos.x + disp_area.Pos.x;
				float screenY = screenPos.y + disp_area.Pos.y;

				// Draw a marker at the snap point
				dl->AddCircleFilled(ImVec2(screenX, screenY), 5.0f, IM_COL32(255, 255, 0, 200));

				mouseX = screenPos.x;
				mouseY = screenPos.y;
				worldXYZ = v3;
				mouse_object->target_position = mouse_object->previous_position = v3;

				return;
			}
		}
	}

	// not snapping to any object:

	auto dispW = working_viewport->disp_area.Size.x;
	auto dispH = working_viewport->disp_area.Size.y;

	// ViewPlane-aware intersection logic
	glm::vec3 intersection = wstate.pointing_pos;
	bool validIntersection = wstate.valid_pointing;

	if (validIntersection) {
		worldXYZ = intersection;

		if (wstate.pointer_mode == 0 || wstate.pointer_mode == 1) {
			// Calculate screen position of intersection point
			glm::vec2 screen_center = world2pixel(intersection, vm, pm, glm::vec2(dispW, dispH));

			// Calculate screen positions of slightly offset points along world X and Y axes
			glm::vec2 screen_x_offset = world2pixel(intersection + wstate.operationalGridUnitX, vm, pm, glm::vec2(dispW, dispH));
			glm::vec2 screen_y_offset = world2pixel(intersection + wstate.operationalGridUnitY, vm, pm, glm::vec2(dispW, dispH));

			// Get screen-space directions by taking the difference
			glm::vec2 screen_x = screen_x_offset - screen_center;
			glm::vec2 screen_y = screen_y_offset - screen_center;

			// Normalize and scale the screen-space directions
			screen_x = glm::normalize(screen_x) * 25.0f; // Length in pixels, it means an infinite long line.
			screen_y = glm::normalize(screen_y) * 25.0f;

			// Add offset for display area position
			ImVec2 center = ImVec2(screen_center.x + disp_area.Pos.x, screen_center.y + disp_area.Pos.y);
			ImVec2 horizontal_start = ImVec2(center.x - screen_x.x, center.y - screen_x.y);
			ImVec2 horizontal_end = ImVec2(center.x + screen_x.x, center.y + screen_x.y);
			ImVec2 vertical_start = ImVec2(center.x - screen_y.x, center.y - screen_y.y);
			ImVec2 vertical_end = ImVec2(center.x + screen_y.x, center.y + screen_y.y);

			dl->AddLine(horizontal_start, horizontal_end, IM_COL32(255, 0, 0, 255));
			dl->AddLine(vertical_start, vertical_end, IM_COL32(255, 0, 0, 255));
		}else
		{
			// holo pointing.
		}
	}
}

void positioning_operation::feedback(unsigned char*& pr)
{
	// Feed the x and y coordinates of the intersection
	WSFeedFloat(worldXYZ.x);
	WSFeedFloat(worldXYZ.y);
	WSFeedFloat(worldXYZ.z);

	if (working_viewport->hover_obj==nullptr){
		WSFeedBool(false);
	}else
	{
		WSFeedBool(true);
		auto obj = working_viewport->hover_obj;
		WSFeedString(obj->name.c_str(), obj->name.length());
		WSFeedFloat(obj->target_position[0]);
		WSFeedFloat(obj->target_position[1]);
		WSFeedFloat(obj->target_position[2]);


		WSFeedInt32(working_viewport->hover_node_id);
		//??also output sub-object position?
	}
	// WSFeedString();
}


void guizmo_operation::feedback(unsigned char*& pr)
{
	// transform feedback.
	WSFeedInt32(referenced_objects.size());

	for (auto& oas : referenced_objects)
	{
		auto obj = oas.obj;
		WSFeedString(obj->name.c_str(), obj->name.length());
		WSFeedFloat(obj->target_position[0]);
		WSFeedFloat(obj->target_position[1]);
		WSFeedFloat(obj->target_position[2]);
		WSFeedFloat(obj->target_rotation[0]);
		WSFeedFloat(obj->target_rotation[1]);
		WSFeedFloat(obj->target_rotation[2]);
		WSFeedFloat(obj->target_rotation[3]);
	}
}


bool do_queryViewportState(unsigned char*& pr)
{
	auto& wstate = working_viewport->workspace_state.back();
	if (!wstate.queryViewportState)
		return false;
	wstate.queryViewportState = false;

	auto& cam = working_viewport->camera;
	WSFeedInt32(-1); //workspace comm.
	WSFeedInt32(1); //Viewport query feedback type
	WSFeedFloat(cam.position.x);
	WSFeedFloat(cam.position.y);
	WSFeedFloat(cam.position.z);
	WSFeedFloat(cam.stare.x);
	WSFeedFloat(cam.stare.y);
	WSFeedFloat(cam.stare.z);
	WSFeedFloat(cam.up.x);
	WSFeedFloat(cam.up.y);
	WSFeedFloat(cam.up.z);
	return true;
}


bool ProcessInteractiveFeedback()
{
	auto pr = working_viewport->ws_feedback_buf;
	for (auto processor : interactive_processing_list)
	{
		if (processor(pr))
		{
			working_viewport->workspaceCallback(working_viewport->ws_feedback_buf, pr - working_viewport->ws_feedback_buf);
			return true;
		}
	}
	return false;
}


bool ProcessOperationFeedback()
{
	auto pr = working_viewport->ws_feedback_buf;
	auto& wstate = working_viewport->workspace_state.back();
	auto pid = wstate.id; // wstate pointer.
	if (wstate.feedback == pending)
		return false;

	WSFeedInt32(pid);
	if (wstate.feedback == operation_canceled ) // canceled.
	{
		WSFeedBool(false);

		// terminal.
		working_viewport->pop_workspace_state();
		
		working_viewport->workspaceCallback(working_viewport->ws_feedback_buf, pr - working_viewport->ws_feedback_buf);
	}
	else {
		WSFeedBool(true); // have feedback value now.

		if (wstate.feedback == feedback_finished) // operation has finished.
		{ 
			WSFeedBool(true);
			wstate.operation->feedback(pr);
			working_viewport->pop_workspace_state();
			working_viewport->workspaceCallback(working_viewport->ws_feedback_buf, pr - working_viewport->ws_feedback_buf);
		}
		else if (wstate.feedback == feedback_continued)
		{
			WSFeedBool(false);
			wstate.operation->feedback(pr);
			working_viewport->workspaceCallback(working_viewport->ws_feedback_buf, pr - working_viewport->ws_feedback_buf);
			wstate.feedback = pending; // invalidate feedback.
		}
		else if (wstate.feedback == realtime_event) // live streaming event
		{
			WSFeedBool(false);
			wstate.operation->feedback(pr);
			realtimeUICallback(working_viewport->ws_feedback_buf, pr - working_viewport->ws_feedback_buf);
			wstate.feedback = pending; // invalidate feedback.
		}
	}

	// finalize:
	return true;
}
void guizmo_operation::draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm)
{
	glm::mat4 mat = glm::mat4_cast(gizmoQuat);
	mat[3] = glm::vec4(gizmoCenter, 1.0f);
	// Check if matrix contains NaN values and reset to identity if needed
	bool hasNaN = false;
	for (int i = 0; i < 4 && !hasNaN; i++) {
		for (int j = 0; j < 4 && !hasNaN; j++) {
			if (std::isnan(mat[i][j])) {
				hasNaN = true;
			}
		}
	}
	
	if (hasNaN) {
		mat = glm::mat4(1.0f);  // Reset to identity matrix
		gizmoQuat = glm::quat(1.0f, 0.0f, 0.0f, 0.0f);  // Reset to identity quaternion
		// Keep the current center position
		mat[3] = glm::vec4(gizmoCenter, 1.0f);
	}

	int getGType = ImGuizmo::ROTATE | ImGuizmo::TRANSLATE;
	ImGuizmo::Manipulate((float*)&vm, (float*)&pm, (ImGuizmo::OPERATION)getGType, ImGuizmo::LOCAL, (float*)&mat);

	glm::vec3 translation, scale;
	glm::quat rotation;
	glm::vec3 skew;
	glm::vec4 perspective;
	glm::decompose(mat, scale, gizmoQuat, gizmoCenter, skew, perspective);
	
	size_t write = 0;
	for (size_t read = 0; read < referenced_objects.size(); read++) {
		if (referenced_objects[read].obj != nullptr) {
			if (write != read) {
				referenced_objects[write] = referenced_objects[read];
				intermediates[write] = intermediates[read];
			}
			write++;
		}
	}
	if (write < referenced_objects.size()) {
		// shrink size.
		referenced_objects.resize(write);
		intermediates.resize(write);
	}
	if (write==0)
	{
		// nothing to move, just end.
		canceled();
		return;
	}

	// todo: add snap to object guizmo operation.

	for (int i = 0; i < referenced_objects.size(); i++)
	{
		auto nmat = mat * intermediates[i];
		glm::decompose(nmat, scale, referenced_objects[i].obj->target_rotation, referenced_objects[i].obj->target_position, skew, perspective);
	}

	if (realtime)
		working_viewport->workspace_state.back().feedback = realtime_event;

	// test ok is pressed.
	auto a = pm * vm * mat * glm::vec4(0, 0, 0, 1);
	glm::vec3 b = glm::vec3(a) / a.w;
	glm::vec2 c = glm::vec2(b);
	auto w = disp_area.Size.x, h = disp_area.Size.y;
	auto d = glm::vec2((c.x * 0.5f + 0.5f) * w + disp_area.Pos.x-16*working_viewport->camera.dpi, (-c.y * 0.5f + 0.5f) * h + disp_area.Pos.y + 50 * working_viewport->camera.dpi);
	ImGui::SetNextWindowPos(ImVec2(d.x, d.y), ImGuiCond_Always);
	ImGuiWindowClass nomerge;
	nomerge.ClassId = ImHashStr("NoMerge");
	nomerge.ViewportFlagsOverrideSet = ImGuiViewportFlags_NoAutoMerge;
	ImGui::SetNextWindowClass(&nomerge);
	ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, ImGui::GetStyle().FrameRounding);
	ImGui::Begin(("gizmo_checker_" + std::to_string(working_viewport_id)).c_str(), NULL, 
		ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoDocking);
	ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(0.0f, 1.0f, 0.0f, 1.0f));
	if (ImGui::Button("\uf00c"))
	{
		working_viewport->workspace_state.back().feedback = feedback_finished;
	}
	ImGui::PopStyleColor();
	ImGui::SameLine();
	ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(1.0f, 0.0f, 0.0f, 1.0f));
	if (ImGui::Button("\uf00d"))
	{
		canceled();
	}
	ImGui::PopStyleColor();
	ImGui::PopStyleVar();
	
	ImGui::End();
}

void guizmo_operation::canceled()
{
	
	glm::vec3 translation, scale;
	glm::quat rotation;
	glm::vec3 skew;
	glm::vec4 perspective;

	working_viewport->workspace_state.back().feedback = operation_canceled;
	// revoke operation.
	gizmoCenter = originalCenter;
	gizmoQuat = glm::identity<glm::quat>();
	glm::mat4 mat = glm::mat4_cast(gizmoQuat);
	mat[3] = glm::vec4(gizmoCenter, 1.0f);
	glm::decompose(mat, scale, gizmoQuat, gizmoCenter, skew, perspective);
	
	for (int i = 0; i < referenced_objects.size(); i++)
	{
		auto nmat = mat * intermediates[i];
		glm::decompose(nmat, scale, referenced_objects[i].obj->target_rotation, referenced_objects[i].obj->target_position, skew, perspective);
	}
}

bool guizmo_operation::selected_get_center()
{
	// obj_action_state.clear(); //don't need to clear since it's empty.
	glm::vec3 pos(0.0f);
	float n = 0;

	// selecting feedback.

	// similar to follow_mouse_operation::extract_follower()
	for (int gi = 0; gi < global_name_map.ls.size(); ++gi){
		auto nt = global_name_map.get(gi);
		auto name = global_name_map.getName(gi);
		RouteTypes(nt, 
			[&]	{
				// point cloud.
				auto t = (me_pcRecord*)nt->obj;
				
				if ((t->flag & (1 << 6)) || (t->flag & (1 << 9))) {   //selected point cloud
					pos += t->target_position;
					reference_t::push_list(referenced_objects, t);
					n += 1;
				}
			}, [&](int class_id)
			{
				// gltf
				auto t = (gltf_object*)nt->obj;
				auto cls = gltf_classes.get(class_id);
				
				if ((t->flags[working_viewport_id] & (1 << 3)) || (t->flags[working_viewport_id] & (1 << 6))) // selected gltf
				{
					pos += t->target_position;
					reference_t::push_list(referenced_objects, t);
					n += 1;
				}
			}, [&]
			{
				// line piece.
				auto t = (me_line_piece*)nt->obj;

				if (t->flags[working_viewport_id] & (1<<3)){
					pos += (t->attrs.st + t->attrs.end) * 0.5f;
					reference_t::push_list(referenced_objects, t);
					n += 1;
				}
			}, [&]
			{
				// sprites;
				auto t = (me_sprite*)nt->obj;
				if (t->per_vp_stat[working_viewport_id] & (1<<1))
				{
					pos += t->target_position;
					reference_t::push_list(referenced_objects, t);
					n += 1;
				}
			},[&]
			{
				// spot texts.
			},[&]
			{
				// geometry.
			});
	}

	gizmoCenter = originalCenter = pos / n;
	gizmoQuat = glm::identity<glm::quat>();

	glm::mat4 gmat = glm::mat4_cast(gizmoQuat);
	gmat[3] = glm::vec4(gizmoCenter, 1.0f);
	glm::mat4 igmat = glm::inverse(gmat);

	for (auto& st : referenced_objects)
	{
		glm::mat4 mat = glm::mat4_cast(st.obj->target_rotation);
		mat[3] = glm::vec4(st.obj->target_position, 1.0f);

		intermediates.push_back(igmat * mat);
	}

	return n > 0;
}

void me_obj::compute_pose()
{
	auto curTime = ui.getMsFromStart();
	auto progress = std::clamp((curTime - target_start_time) / std::max(target_require_completion_time - target_start_time, 0.0001f), 0.0f, 1.0f);

	if (anchor.obj!=nullptr)
	{
		auto anchor_pos = anchor.obj->current_pos;
		auto anchor_rot = anchor.obj->current_rot;

		if (anchor_subid>=0)
		{
			if (anchor.type>=1000)
			{
				// is gltf.
				auto node_id = anchor_subid;
				// Get the final computed matrix for this specific node/subobject
				auto gltf_obj = (gltf_object*)anchor.obj;
				auto node_matrix = last_iv * GetFinalNodeMatrix(gltf_obj->gltf_class_id, node_id, anchor.instance_id);
				
				// Extract position and rotation from the node's final matrix
				glm::vec3 node_scale;
				glm::quat node_rotation;
				glm::vec3 node_translation;
				glm::vec3 node_skew;
				glm::vec4 node_perspective;
				glm::decompose(node_matrix, node_scale, node_rotation, node_translation, node_skew, node_perspective);
				
				// Use the node's transformation instead of the object's base transformation
				anchor_pos = node_translation;
				anchor_rot = node_rotation;
			}
		}
		
		glm::mat4 anchorMat = glm::mat4_cast(anchor_rot);
		anchorMat[3] = glm::vec4(anchor_pos, 1.0f);

		glm::mat4 offsetMat = glm::mat4_cast(offset_rot);
		offsetMat[3] = glm::vec4(offset_pos, 1.0f);

		glm::mat4 finalMat = anchorMat * offsetMat;

		previous_position = target_position = current_pos = glm::vec3(finalMat[3]);
		previous_rotation = target_rotation = current_rot = glm::quat_cast(finalMat);
	}else{
		// compute rendering position:
		current_pos = Lerp(previous_position, target_position, progress);
		current_rot = SLerp(previous_rotation, target_rotation, progress);
	}
	if (isnan(current_pos.x))
	{
		printf("progress=%f\n", progress);
		assert(false);
	}
}

void RouteTypes(namemap_t* nt,
	std::function<void()> point_cloud, 
	std::function<void(int)> gltf, // argument: class-id.
	std::function<void()> line_piece,
	std::function<void()> sprites, 
	std::function<void()> spot_texts, 
	std::function<void()> not_used_now)
{
	auto type = nt->type;
	if (type == 1) point_cloud();
	else if (type == 1000) gltf(((gltf_object*)nt->obj)->gltf_class_id);
	else if (type == 2) line_piece();
	else if (type == 3) sprites();
	else if (type == 4) spot_texts();
	// else if (type == 5) line_bunch();
}

// Helper function to test if a prop should be displayed based on viewport PropDisplayMode
bool viewport_test_prop_display(me_obj* obj)
{
    if (working_viewport == nullptr || obj == nullptr)
        return true;
    
    // If no pattern specified, display everything
    if (working_viewport->namePatternForPropDisplayMode.empty())
        return true;
    
    // Check if the object name matches the pattern (optimized regex pattern matching)
    bool nameMatches = RegexMatcher::match(obj->name, working_viewport->namePatternForPropDisplayMode);
    
    // Return based on display mode
    if (working_viewport->propDisplayMode == viewport_state_t::PropDisplayMode::AllButSpecified)
        return !nameMatches; // Display all objects EXCEPT those matching the pattern
    else // NoneButSpecified mode
        return nameMatches; // Display ONLY objects matching the pattern
}

void switch_context(int vid)
{
	ImGuizmo::use_ctx(vid);
	working_viewport_id = vid;
	working_viewport = &ui.viewports[vid];
	working_graphics_state = &graphics_states[vid];

    // update default mapping
    special_objects.update("me::camera", camera_object = (me_special*)working_viewport->camera_obj);

    // ensure alias key matches current name using updateName; assign if not set
    if (vid > 0) {
		const std::string& pname = working_viewport->panelName;
		std::string alias = !pname.empty() ? (std::string("me::camera(") + pname + ")") : std::string();
		if (alias != working_viewport->cameraAliasKey)
			special_objects.updateName(working_viewport->cameraAliasKey, alias);
		working_viewport->cameraAliasKey = alias;
	}
}

void draw_viewport(disp_area_t region, int vid)
{
	auto wnd = ImGui::GetCurrentWindowRead();
	auto vp = ImGui::GetCurrentWindowRead()->Viewport;
	auto dl = ImGui::GetForegroundDrawList(vp);

	GenMonitorInfo();
	DefaultRenderWorkspace(region, dl);
	ProcessWorkspace(region, dl, vp);
    working_viewport->frameCnt += 1;

	_sg_image_t* img = _sg_lookup_image(&_sg.pools, working_graphics_state->temp_render.id);
	SOKOL_ASSERT(img->gl.target == GL_TEXTURE_2D);
	SOKOL_ASSERT(0 != img->gl.tex[img->cmn.active_slot]);

    uint32_t gl_texture_id = img->gl.tex[img->cmn.active_slot];
	auto opos = ImGui::GetCursorPos();
	ImGui::InvisibleButton(("viewport_" + std::to_string(vid) + "_capture").c_str(), ImVec2(region.Size.x, region.Size.y));
    ImGui::SetCursorPos(opos); // Reset cursor to image position
	ImGui::Image(
        (void*)(intptr_t)gl_texture_id,  // Texture ID as void*
        ImVec2(region.Size.x, region.Size.y),            // Make it fill the window
        ImVec2(0, 1),                   // Top-left UV
        ImVec2(1, 0)                    // Bottom-right UV
    );
	if (ImGui::IsItemHovered())
		ImGui::SetNextFrameWantCaptureMouse(false);
}
void draw_viewport_offscreen(disp_area_t region)
{
	auto dl = ImGui::GetForegroundDrawList(ImGui::GetMainViewport());
	DefaultRenderWorkspace(region, dl);
	ProcessInteractiveFeedback();
	working_viewport->frameCnt += 1;
}

inline bool ui_state_t::displayRenderDebug()
{
	if (ui.RenderDebug && working_viewport == &ui.viewports[0] && working_graphics_state == &graphics_states[0]) return true; else return false;
}

void ProcessWorkspaceQueue(void* ptr)
{
	switch_context(0);
	ActualWorkspaceQueueProcessor(ptr, ui.viewports[0]);
}

void viewport_state_t::clear()
{
	while(workspace_state.size()>0)
		destroy_state(this);
	workspace_state.push_back(workspace_state_desc{.id = 0, .name = "default", .operation = new no_operation});
	
	for (int i = 0; i < global_name_map.ls.size(); ++i){
		auto nt = global_name_map.get(i);
		RouteTypes(nt, 
			[&]	{
				// point cloud.
				auto t = (me_pcRecord*)nt->obj;
			}, [&](int class_id)
			{
				// gltf
				auto t = (gltf_object*)nt->obj;
				t->flags[working_viewport_id] = 0;
			}, [&]
			{
				// line piece.
			}, [&]
			{
				// sprites;
				// auto im = sprites.get(name);
				// delete im;
			},[&]
			{
				// spot texts.
			},[&]
			{
				// geometry.
			});
	}
}

void positioning_operation::canceled()
{
	working_viewport->workspace_state.back().feedback = operation_canceled;
}

void positioning_operation::pointer_down()
{
	clickingX = working_viewport->mouseX();
	clickingY = working_viewport->mouseY();
}

void positioning_operation::pointer_up()
{
	if (std::abs(clickingX - working_viewport->mouseX()) < 3 && std::abs(clickingY - working_viewport->mouseY()) < 3)
		working_viewport->workspace_state.back().feedback = feedback_finished;
}

void positioning_operation::pointer_move()
{
	if (real_time)
		working_viewport->workspace_state.back().feedback = realtime_event;
}

void mouse_action_operation::feedback(unsigned char*& pr)
{
	// Send mouse position
	WSFeedInt32(mouseX);
	WSFeedInt32(mouseY);
	
	// Send workspace position and size
	WSFeedInt32((int)working_viewport->disp_area.Pos.x);
	WSFeedInt32((int)working_viewport->disp_area.Pos.y);
	WSFeedInt32((int)working_viewport->disp_area.Size.x);
	WSFeedInt32((int)working_viewport->disp_area.Size.y);
	
	// Send mouse button states
	WSFeedBool(mouseLB);
	WSFeedBool(mouseRB);
	WSFeedBool(mouseMB);
	
	// Send mouse delta
	WSFeedInt32(mouseWheelDeltaX);
	WSFeedInt32(mouseWheelDeltaY);

	mouseWheelDeltaX = 0;
	mouseWheelDeltaY = 0;

	// Compute the time difference in ms for last_feedback_time
	auto now = std::chrono::high_resolution_clock::now();
	long long ms_diff = std::chrono::duration_cast<std::chrono::milliseconds>(now - last_feedback_time).count();
	last_feedback_time = now; // Update for next cycle
	
}

std::vector<std::function<bool(unsigned char*&)>> interactive_processing_list{
	MainMenuBarResponse,
	do_queryViewportState,
	CaptureViewport,
	TestSpriteUpdate
};

void follow_mouse_operation::canceled()
{
	// Set feedback to canceled
	working_viewport->workspace_state.back().feedback = operation_canceled;

    // Reset all objects to their original positions
    for (size_t i = 0; i < referenced_objects.size(); i++) {
        if (referenced_objects[i].obj == nullptr) continue;

        // Reset to original position
        referenced_objects[i].obj->target_position = original[i];
        referenced_objects[i].obj->target_rotation = referenced_objects[i].obj->current_rot;
        
        // Set target time for instant reset
        referenced_objects[i].obj->target_start_time = ui.getMsFromStart();
        referenced_objects[i].obj->target_require_completion_time = ui.getMsFromStart() + 300;
    }
}

void follow_mouse_operation::pointer_down()
{
	auto& wstate = working_viewport->workspace_state.back();

	if (!wstate.valid_pointing) {
		printf("invalid pointing, cancel.\n");
		canceled();
		return;
	}

	downX = working_viewport->mouseX();
	downY = working_viewport->mouseY();
    
    // Initialize hover position to the same as down position
    hoverWorldXYZ = downWorldXYZ = wstate.pointing_pos;
	printf("start dragging from %f,%f,%f.\n", downWorldXYZ.x, downWorldXYZ.y, downWorldXYZ.z);
	working = true;

	// Reset point trail for PointOnGrid mode
	pointTrailScreen.clear();
	hasLastTrailPoint = false;
}
void follow_mouse_operation::pointer_up()
{
	if (!(std::abs(downX - working_viewport->mouseX()) < 3 && std::abs(downY - working_viewport->mouseY()) < 3) || allow_same_place)
		working_viewport->workspace_state.back().feedback = feedback_finished;
	else
		canceled();
}

void follow_mouse_operation::feedback(unsigned char*& pr) 
{
    // Write start mouse 3D position
    *(float*)pr = downWorldXYZ.x; pr += 4;
    *(float*)pr = downWorldXYZ.y; pr += 4;
    *(float*)pr = downWorldXYZ.z; pr += 4;
    
    // Write end mouse 3D position
    *(float*)pr = hoverWorldXYZ.x; pr += 4;
    *(float*)pr = hoverWorldXYZ.y; pr += 4;
    *(float*)pr = hoverWorldXYZ.z; pr += 4;
    
    // Write start snapping object info
    bool has_start_snap = false;
    if (working_viewport->hover_obj != nullptr) {
        for (const auto& snap_name : snapsStart) {
            if (wildcardMatch(working_viewport->hover_obj->name, snap_name)) {
                has_start_snap = true;
                *(bool*)pr = true; pr += 1;
                
                // Write object name
                int length = working_viewport->hover_obj->name.length();
                *(int*)pr = length + 1; pr += 4;
                memcpy(pr, working_viewport->hover_obj->name.c_str(), length);
                pr += length;
                *pr = 0; pr += 1;
                
                break;
            }
        }
    }
    
    if (!has_start_snap) {
        *(bool*)pr = false; pr += 1;
    }
    
    // Write end snapping object info
    bool has_end_snap = false;
    if (working_viewport->hover_obj != nullptr) {
        for (const auto& snap_name : snapsEnd) {
            if (wildcardMatch(working_viewport->hover_obj->name, snap_name)) {
                has_end_snap = true;
                *(bool*)pr = true; pr += 1;
                
                // Write object name
                int length = ui.viewports[ui.mouseCaptuingViewport].hover_obj->name.length();
                *(int*)pr = length + 1; pr += 4;
                memcpy(pr, ui.viewports[ui.mouseCaptuingViewport].hover_obj->name.c_str(), length);
                pr += length;
                *pr = 0; pr += 1;
                
                break;
            }
        }
    }
    
    if (!has_end_snap) {
        *(bool*)pr = false; pr += 1;
    }
    
    // Write follower objects positions
    int follower_count = 0;
    for (auto& ref : referenced_objects) {
        if (ref.obj != nullptr) {
            follower_count++;
        }
    }
    
    *(int*)pr = follower_count; pr += 4;
    
    for (auto& ref : referenced_objects) {
        if (ref.obj == nullptr) continue;
        
        // Write object name
        int length = ref.obj->name.length();
        *(int*)pr = length + 1; pr += 4;
        memcpy(pr, ref.obj->name.c_str(), length);
        pr += length;
        *pr = 0; pr += 1;
        
        // Write position
        *(float*)pr = ref.obj->current_pos.x; pr += 4;
        *(float*)pr = ref.obj->current_pos.y; pr += 4;
        *(float*)pr = ref.obj->current_pos.z; pr += 4;
    }
}

void follow_mouse_operation::pointer_move()
{
	if (!working) 
		return;
	hoverX = working_viewport->mouseX();
	hoverY = working_viewport->mouseY();
	if (real_time)
		working_viewport->workspace_state.back().feedback = realtime_event;
}
void follow_mouse_operation::draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm) 
{
	if (!working) 
		return;

	auto& wstate = working_viewport->workspace_state.back();

    // Get display dimensions
    auto dispW = working_viewport->disp_area.Size.x;
    auto dispH = working_viewport->disp_area.Size.y;
    
	if (wstate.valid_pointing)
		hoverWorldXYZ = wstate.pointing_pos;
    
    // Convert world positions to screen for drawing
    glm::vec2 screenDownPos = world2pixel(downWorldXYZ, vm, pm, glm::vec2(dispW, dispH));
    glm::vec2 screenHoverPos = world2pixel(hoverWorldXYZ, vm, pm, glm::vec2(dispW, dispH));
    
    // Add display area offset to get absolute screen positions
    ImVec2 startPos = ImVec2(screenDownPos.x + disp_area.Pos.x, screenDownPos.y + disp_area.Pos.y);
    ImVec2 endPos = ImVec2(screenHoverPos.x + disp_area.Pos.x, screenHoverPos.y + disp_area.Pos.y);

	// Mode: 0-LineOnGrid (default), 1-RectOnGrid, 6-PointOnGrid
	// todo: if real_time, also draw the trail on ui-selection.
	if (mode == 1) {
		// RectOnGrid: draw rectangle from start to current hover
		ImVec2 minP(ImMin(startPos.x, endPos.x), ImMin(startPos.y, endPos.y));
		ImVec2 maxP(ImMax(startPos.x, endPos.x), ImMax(startPos.y, endPos.y));
		dl->AddRect(minP, maxP, IM_COL32(255, 255, 0, 255), 0.0f, 0, 2.0f);
	}
	else if (mode == 6) {
		// PointOnGrid: draw trail of points, only add when moved enough
		ImVec2 current = endPos;
		if (!hasLastTrailPoint) {
			pointTrailScreen.push_back(glm::vec2(startPos.x, startPos.y));
			lastTrailScreenPos = glm::vec2(startPos.x, startPos.y);
			hasLastTrailPoint = true;
		}
		float dx = current.x - lastTrailScreenPos.x;
		float dy = current.y - lastTrailScreenPos.y;
		if ((dx * dx + dy * dy) >= trailMinPixelStep * trailMinPixelStep) {
			pointTrailScreen.push_back(glm::vec2(current.x, current.y));
			lastTrailScreenPos = glm::vec2(current.x, current.y);
		}
		for (const auto& p : pointTrailScreen) {
			dl->AddCircleFilled(ImVec2(p.x, p.y), 3.0f, IM_COL32(255, 255, 0, 255));
		}
	}
	else {
		// Default (LineOnGrid): draw a line with arrow
		dl->AddLine(startPos, endPos, IM_COL32(255, 255, 0, 255), 2.0f);
		float arrowLength = 15.0f;
		float arrowAngle = 0.5f; // approx 30 degrees
		glm::vec2 lineDir = glm::normalize(glm::vec2(endPos.x - startPos.x, endPos.y - startPos.y));
		glm::vec2 perpDir = glm::vec2(-lineDir.y, lineDir.x);
		ImVec2 arrowLeft = ImVec2(
			endPos.x - arrowLength * (lineDir.x * cos(arrowAngle) + perpDir.x * sin(arrowAngle)),
			endPos.y - arrowLength * (lineDir.y * cos(arrowAngle) + perpDir.y * sin(arrowAngle))
		);
		ImVec2 arrowRight = ImVec2(
			endPos.x - arrowLength * (lineDir.x * cos(arrowAngle) - perpDir.x * sin(arrowAngle)),
			endPos.y - arrowLength * (lineDir.y * cos(arrowAngle) - perpDir.y * sin(arrowAngle))
		);
		dl->AddTriangleFilled(endPos, arrowLeft, arrowRight, IM_COL32(255, 255, 0, 255));
	}
    
    // Move follower objects if present
    if (!referenced_objects.empty()) {
        glm::vec3 translation = hoverWorldXYZ - downWorldXYZ;
        
        for (size_t i = 0; i < referenced_objects.size(); i++) {
            if (referenced_objects[i].obj == nullptr) continue;
            
            // Update the target position by adding the translation
			referenced_objects[i].obj->previous_position = referenced_objects[i].obj->target_position = referenced_objects[i].obj->current_pos = original[i] + translation;
        }
    }
}

// Read final node matrix from GPU using me_getTexBytes
// Note: This reads RGBA8 data but matrices are stored as RGBA32F, so precision will be lost
// For full precision, use me_getTexFloats instead
static glm::mat4 me_getNodeMatrixBytes(int node_id, int instance_id, int max_instances, int offset) {
	// Calculate the texture coordinates for this node/instance matrix
	int get_id = max_instances * node_id + instance_id + offset;
	int x = (get_id % 1024) * 2;
	int y = (get_id / 1024) * 2;
	
	// Read 2x2 block of RGBA8 pixels (each matrix takes 2x2 texels)
	uint8_t pixels[16]; // 4 pixels * 4 components each = 16 bytes
	me_getTexBytes(shared_graphics.instancing.objInstanceNodeMvMats1, pixels, x, y, 2, 2);
	
	// Convert RGBA8 to float matrix (with precision loss)
	// Note: This is a lossy conversion from 8-bit to float
	glm::mat4 matrix;
	for (int i = 0; i < 4; i++) {
		for (int j = 0; j < 4; j++) {
			int pixel_idx = (i / 2) * 2 + (j / 2); // Which of the 4 pixels
			int component_idx = (i % 2) * 2 + (j % 2); // Which component within that pixel
			int byte_idx = pixel_idx * 4 + component_idx;
			
			// Convert from 0-255 range to approximate float value
			// This is a very rough approximation and will lose precision
			matrix[j][i] = (float(pixels[byte_idx]) - 127.5f) / 127.5f;
		}
	}
	
	return matrix;
}

// Read final node matrix from GPU using me_getTexFloats (recommended for full precision)
static glm::mat4 me_getNodeMatrixFloats(int node_id, int instance_id, int max_instances, int offset) {
	// Calculate the texture coordinates for this node/instance matrix
	int get_id = max_instances * node_id + instance_id + offset;
	int x = (get_id % 1024) * 2;
	int y = (get_id / 1024) * 2;
	
	// Read 2x2 block of RGBA32F pixels (each matrix takes 2x2 texels)
	glm::vec4 pixels[4]; // 4 vec4s = 16 floats = 4x4 matrix
	me_getTexFloats(shared_graphics.instancing.objInstanceNodeMvMats1, pixels, x, y, 2, 2);
	
	// Reconstruct matrix from the 4 vec4s
	glm::mat4 matrix(
		pixels[0], // column 0
		pixels[1], // column 1  
		pixels[2], // column 2
		pixels[3]  // column 3
	);
	
	return matrix;
}

// Public interface to get the final computed node matrix
// Call this after all GPU matrix computation passes are complete
glm::mat4 GetFinalNodeMatrix(int class_id, int node_id, int instance_id) {
	if (class_id < 0 || class_id >= gltf_classes.ls.size()) {
		return glm::mat4(1.0f); // Identity matrix for invalid class
	}
	
	auto gltf_class_ptr = gltf_classes.get(class_id);
	if (!gltf_class_ptr || instance_id >= gltf_class_ptr->showing_objects[working_viewport_id].size()) {
		return glm::mat4(1.0f); // Identity matrix for invalid instance
	}
	
	int max_instances = (int)gltf_class_ptr->showing_objects[working_viewport_id].size();
	int offset = 0; // This should be the rendering offset for this class
	
	// Find the correct offset by iterating through previous classes
	for (int i = 0; i < class_id; i++) {
		auto prev_class = gltf_classes.get(i);
		if (prev_class) {
			offset += prev_class->count_nodes();
		}
	}
	
	// Use the float version for full precision
	return me_getNodeMatrixFloats(node_id, instance_id, max_instances, offset);
}