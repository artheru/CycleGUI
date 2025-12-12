
#include "cycleui.h"

namespace
{
	bool g_showCycleGuiDebug = false;
	bool g_allowCycleGuiModify = false;
	int g_selectedViewportIndex = -1;
	int g_selectedPropIndex = 0;

#ifndef __EMSCRIPTEN__
	struct monitor_desc_t
	{
		int index = -1;
		GLFWmonitor* monitor = nullptr;
		std::string name;
		int x = 0, y = 0, w = 0, h = 0;
	};

	static std::vector<monitor_desc_t> GetMonitors()
	{
		std::vector<monitor_desc_t> out;
		int monitorCount = 0;
		GLFWmonitor** monitors = glfwGetMonitors(&monitorCount);
		if (!monitors || monitorCount <= 0) return out;
		out.reserve(monitorCount);
		for (int i = 0; i < monitorCount; ++i)
		{
			monitor_desc_t m;
			m.index = i;
			m.monitor = monitors[i];
			const char* nm = glfwGetMonitorName(monitors[i]);
			m.name = nm ? nm : "Unknown";
			glfwGetMonitorWorkarea(monitors[i], &m.x, &m.y, &m.w, &m.h);
			out.push_back(std::move(m));
		}
		return out;
	}

	static int FindMonitorIndexForWindow(GLFWwindow* window)
	{
		if (!window) return -1;

		// If fullscreen, GLFW knows it directly.
		if (GLFWmonitor* mon = glfwGetWindowMonitor(window))
		{
			auto monitors = GetMonitors();
			for (auto& m : monitors)
				if (m.monitor == mon) return m.index;
			return -1;
		}

		// Otherwise pick the monitor with the largest intersection area.
		int wx = 0, wy = 0, ww = 0, wh = 0;
		glfwGetWindowPos(window, &wx, &wy);
		glfwGetWindowSize(window, &ww, &wh);

		auto monitors = GetMonitors();
		long long bestArea = -1;
		int best = -1;
		for (auto& m : monitors)
		{
			int ix0 = std::max(wx, m.x);
			int iy0 = std::max(wy, m.y);
			int ix1 = std::min(wx + ww, m.x + m.w);
			int iy1 = std::min(wy + wh, m.y + m.h);
			long long iw = std::max(0, ix1 - ix0);
			long long ih = std::max(0, iy1 - iy0);
			long long area = iw * ih;
			if (area > bestArea)
			{
				bestArea = area;
				best = m.index;
			}
		}
		return best;
	}
#endif

	const char* DisplayModeToString(viewport_state_t::DisplayMode mode)
	{
		switch (mode)
		{
		case viewport_state_t::Normal: return "Normal";
		case viewport_state_t::VR: return "VR";
		case viewport_state_t::EyeTrackedHolography: return "EyeTracked Holography";
		case viewport_state_t::EyeTrackedHolography2: return "EyeTracked Holography 2";
		default: return "Unknown";
		}
	}

	const char* PropDisplayModeToString(viewport_state_t::PropDisplayMode mode)
	{
		switch (mode)
		{
		case viewport_state_t::AllButSpecified: return "All But Specified";
		case viewport_state_t::NoneButSpecified: return "Only Specified";
		default: return "Unknown";
		}
	}

	const char* FeedbackModeToString(feedback_mode mode)
	{
		switch (mode)
		{
		case pending: return "Pending";
		case operation_canceled: return "Operation canceled";
		case feedback_finished: return "Feedback finished";
		case feedback_continued: return "Feedback continued";
		case realtime_event: return "Realtime event";
		default: return "Unknown";
		}
	}

	const char* PropTypeToString(const namemap_t* entry)
	{
		if (!entry || !entry->obj) return "Unknown";

		switch (entry->type)
		{
		case me_pcRecord::type_id: return "Point Cloud";
		case me_line_piece::type_id: return "Line";
		case me_sprite::type_id: return "Sprite";
		case me_world_ui::type_id: return "World UI";
		case me_region_cloud_bunch::type_id: return "Region Cloud";
		case me_linebunch::type_id:
			if (line_bunches.get(entry->obj->name) == entry->obj)
				return "Line Bunch";
			if (geometries.get(entry->obj->name) == entry->obj)
				return "Geometry";
			return "Line/Geometry";
		default:
			if (entry->type >= gltf_object::type_id)
				return "GLTF Object";
			return "Unknown";
		}
	}

	void DrawWorkspaceStateDetails(workspace_state_desc& state, bool allowModify)
	{
		ImGui::Text("Workspace \"%s\" (id %d)", state.name.c_str(), state.id);
		ImGui::Text("Operation: %s", state.operation ? state.operation->Type().c_str() : "None");
		ImGui::Text("Feedback: %s", FeedbackModeToString(state.feedback));

		ImGui::BeginDisabled(!allowModify);
		ImGui::Separator();
		ImGui::TextUnformatted("Display options");
		ImGui::Checkbox("Use EDL", &state.useEDL);
		ImGui::SameLine();
		ImGui::Checkbox("Use SSAO", &state.useSSAO);
		ImGui::SameLine();
		ImGui::Checkbox("Use Ground", &state.useGround);
		ImGui::Checkbox("Use Border", &state.useBorder);
		ImGui::SameLine();
		ImGui::Checkbox("Use Bloom", &state.useBloom);
		ImGui::SameLine();
		ImGui::Checkbox("Draw Grid", &state.drawGrid);
		ImGui::SameLine();
		ImGui::Checkbox("Draw Guizmo", &state.drawGuizmo);
		ImGui::Checkbox("Bring to front on hover", &state.btf_on_hovering);

		ImGui::ColorEdit4("Hover shine", glm::value_ptr(state.hover_shine), ImGuiColorEditFlags_DisplayRGB | ImGuiColorEditFlags_Float);
		ImGui::ColorEdit4("Selected shine", glm::value_ptr(state.selected_shine), ImGuiColorEditFlags_DisplayRGB | ImGuiColorEditFlags_Float);
		ImGui::ColorEdit4("Hover border color", glm::value_ptr(state.hover_border_color), ImGuiColorEditFlags_DisplayRGB | ImGuiColorEditFlags_Float);
		ImGui::ColorEdit4("Selected border color", glm::value_ptr(state.selected_border_color), ImGuiColorEditFlags_DisplayRGB | ImGuiColorEditFlags_Float);
		ImGui::ColorEdit4("World border color", glm::value_ptr(state.world_border_color), ImGuiColorEditFlags_DisplayRGB | ImGuiColorEditFlags_Float);

		ImGui::Separator();
		ImGui::TextUnformatted("Operational grid");
		ImGui::Checkbox("Use operational grid", &state.useOperationalGrid);
		ImGui::DragFloat3("Grid pivot", glm::value_ptr(state.operationalGridPivot), 0.01f);
		ImGui::DragFloat3("Grid unit X", glm::value_ptr(state.operationalGridUnitX), 0.01f);
		ImGui::DragFloat3("Grid unit Y", glm::value_ptr(state.operationalGridUnitY), 0.01f);

		ImGui::Separator();
		ImGui::TextUnformatted("Voxel selection");
		ImGui::DragFloat("Voxel quantize", &state.voxel_quantize, 0.01f, 0.0f, 100.0f, "%.3f");
		ImGui::DragFloat("Voxel opacity", &state.voxel_opacity, 0.01f, 0.0f, 1.0f);

		ImGui::Separator();
		ImGui::TextUnformatted("Pointer");
		const char* pointerModes[] = { "Operational plane", "View plane", "Holographic 3D" };
		int pointerMode = std::clamp(state.pointer_mode, 0, (int)(IM_ARRAYSIZE(pointerModes) - 1));
		if (ImGui::Combo("Pointer mode", &pointerMode, pointerModes, IM_ARRAYSIZE(pointerModes)))
		{
			state.pointer_mode = pointerMode;
		}
		ImGui::Checkbox("Valid pointing", &state.valid_pointing);
		ImGui::DragFloat3("Pointing position", glm::value_ptr(state.pointing_pos), 0.01f);
		ImGui::EndDisabled();
	}

	void DrawWorkspaceStack(viewport_state_t& viewport, bool allowModify)
	{
		ImGui::TextUnformatted("Workspace stack (top to bottom)");
		if (viewport.workspace_state.empty())
		{
			ImGui::TextDisabled("No workspace states available.");
			return;
		}

		for (int idx = static_cast<int>(viewport.workspace_state.size()) - 1; idx >= 0; --idx)
		{
			auto& state = viewport.workspace_state[idx];
			ImGui::PushID(idx);
			std::string header = state.name.empty() ? "Unnamed workspace" : state.name;
			if (ImGui::TreeNodeEx(header.c_str(), ImGuiTreeNodeFlags_DefaultOpen, "#%d %s", idx, header.c_str()))
			{
				DrawWorkspaceStateDetails(state, allowModify);
				ImGui::TreePop();
			}
			ImGui::PopID();
		}
	}

	void DrawViewportCameraSection(viewport_state_t& viewport, bool allowModify)
	{
		Camera& cam = viewport.camera;
		ImGui::TextUnformatted("Camera");
		ImGui::BeginDisabled(!allowModify);
		ImGui::DragFloat3("Stare", glm::value_ptr(cam.stare), 0.05f);
		ImGui::DragFloat3("Position", glm::value_ptr(cam.position), 0.05f);
		ImGui::DragFloat("Distance", &cam.distance, 0.1f, 0.0f, 10000.0f);
		ImGui::DragFloat("Altitude", &cam.Altitude, 0.01f);
		ImGui::DragFloat("Azimuth", &cam.Azimuth, 0.01f);
		ImGui::DragFloat("Field of view", &cam._fov, 0.1f, 1.0f, 179.0f);
		ImGui::DragFloat("Ortho factor", &cam.OrthoFactor, 1.0f, 1.0f, 10000.0f);
		const char* projectionModes[] = { "Perspective", "Orthographic" };
		int projectionMode = std::clamp(cam.ProjectionMode, 0, 1);
		if (ImGui::Combo("Projection mode", &projectionMode, projectionModes, IM_ARRAYSIZE(projectionModes)))
		{
			cam.ProjectionMode = projectionMode;
		}
		// if rect, show texture, if default skybox, show sun_altitude tweaking, if custom bg, show shader code editor.
		ImGui::DragFloat("sun altitude", &working_viewport->sun_altitude, 0.01f, 0, 1.57f);
		ImGui::EndDisabled();
		ImGui::Text("Up: (%.2f, %.2f, %.2f)", cam.up.x, cam.up.y, cam.up.z);
		ImGui::Text("Move right: (%.2f, %.2f, %.2f)", cam.moveRight.x, cam.moveRight.y, cam.moveRight.z);
		ImGui::Text("Move front: (%.2f, %.2f, %.2f)", cam.moveFront.x, cam.moveFront.y, cam.moveFront.z);
	}

	void DrawViewportDetails(viewport_state_t& viewport, int viewportIndex, bool allowModify)
	{
		std::string nameSuffix = viewport.panelName.empty() ? "" : (" - " + viewport.panelName);
		ImGui::Text("Viewport #%d%s", viewportIndex, nameSuffix.c_str());
		ImGui::Text("Display mode: %s", DisplayModeToString(viewport.displayMode));

		ImGui::BeginDisabled(!allowModify);

		ImGui::Text("Prop display mode: %s", PropDisplayModeToString(viewport.propDisplayMode));
		// todo: allow to manual change prop display mode....

		// Create an editable buffer from the string
		char buffer[256];
		std::snprintf(buffer, sizeof(buffer), "%s", viewport.namePatternForPropDisplayMode.c_str());

		if (ImGui::InputText("Name pattern", buffer, sizeof(buffer)))
		{
			viewport.namePatternForPropDisplayMode = buffer; // update std::string after editing
		}

		ImGui::EndDisabled();

		ImGui::Text("Camera alias key: %s", viewport.cameraAliasKey.c_str());
		ImGui::Text("Window: `%s`", viewport.wndStr.c_str());

		// Per-viewport fullscreen/window toggle (when backed by an actual GLFW window).
#ifndef __EMSCRIPTEN__
		GLFWwindow* glfwWindow = nullptr;
		if (viewport.imguiWindow && viewport.imguiWindow->Viewport)
			glfwWindow = (GLFWwindow*)viewport.imguiWindow->Viewport->PlatformHandle;
		if (viewportIndex == 0)
			glfwWindow = (GLFWwindow*)ImGui::GetMainViewport()->PlatformHandle;

		if (glfwWindow)
		{
			bool isFullscreen = glfwGetWindowMonitor(glfwWindow) != nullptr;
			int modeSel = isFullscreen ? 1 : 0;
			ImGui::Separator();
			ImGui::TextUnformatted("Window mode");
			ImGui::BeginDisabled(!allowModify);
			if (ImGui::RadioButton("Window##vp_mode_window", modeSel == 0)) modeSel = 0;
			ImGui::SameLine();
			if (ImGui::RadioButton("Fullscreen##vp_mode_fullscreen", modeSel == 1)) modeSel = 1;

			auto monitors = GetMonitors();
			int monIdx = FindMonitorIndexForWindow(glfwWindow);
			if (monIdx >= 0 && monIdx < (int)monitors.size())
				ImGui::Text("Monitor: #%d %s", monIdx, monitors[monIdx].name.c_str());
			else
				ImGui::TextDisabled("Monitor: (unknown)");

			if (modeSel == 1 && !isFullscreen)
			{
				// Switch into fullscreen (prefer current monitor if detected, else 0).
				int useIdx = (monIdx >= 0 && monIdx < (int)monitors.size()) ? monIdx : 0;
				if (!monitors.empty())
				{
					auto* m = monitors[useIdx].monitor;
					const GLFWvidmode* vm = glfwGetVideoMode(m);
					// Store windowed geometry for restore
					glfwGetWindowPos(glfwWindow, &viewport.lastWindowX, &viewport.lastWindowY);
					glfwGetWindowSize(glfwWindow, &viewport.lastWindowW, &viewport.lastWindowH);
					glfwSetWindowMonitor(glfwWindow, m, 0, 0, vm->width, vm->height, vm->refreshRate);
				}
			}
			else if (modeSel == 0 && isFullscreen)
			{
				// Restore to windowed mode
				glfwSetWindowMonitor(glfwWindow, NULL,
					viewport.lastWindowX, viewport.lastWindowY,
					viewport.lastWindowW, viewport.lastWindowH,
					0);
			}
			ImGui::EndDisabled();
		}else
		{
			ImGui::Text("Cannot get viewport window.");
		}
#else
		ImGui::Separator();
		ImGui::TextDisabled("Window/fullscreen switching not available on WASM.");
#endif

		ImGui::Separator();
		DrawViewportCameraSection(viewport, allowModify);

		ImGui::Separator();
		DrawWorkspaceStack(viewport, allowModify);
	}

	void DrawViewportsTab(bool allowModify)
	{
		std::vector<int> indices;
		indices.reserve(MAX_VIEWPORTS);
		for (int i = 0; i < MAX_VIEWPORTS; ++i)
		{
			auto& vp = ui.viewports[i];
			if (vp.assigned || vp.graphics_inited || vp.active || !vp.workspace_state.empty())
			{
				indices.push_back(i);
			}
		}

		if (indices.empty())
		{
			ImGui::TextDisabled("No viewport data available.");
			return;
		}

		if (g_selectedViewportIndex < 0 || g_selectedViewportIndex >= MAX_VIEWPORTS ||
			std::find(indices.begin(), indices.end(), g_selectedViewportIndex) == indices.end())
		{
			g_selectedViewportIndex = indices.front();
		}

		ImGui::BeginChild("viewport_list", ImVec2(180.0f, 0.0f), true);
		for (int idx : indices)
		{
			auto& vp = ui.viewports[idx];
			ImGui::PushID(idx);
			std::string label = vp.panelName.empty() ? ("Viewport " + std::to_string(idx)) : vp.panelName;
			if (ImGui::Selectable(label.c_str(), g_selectedViewportIndex == idx))
			{
				g_selectedViewportIndex = idx;
			}
			if (!vp.wndStr.empty())
			{
				ImGui::TextDisabled("%s", vp.wndStr.c_str());
			}
			ImGui::PopID();
		}
		ImGui::EndChild();

		ImGui::SameLine();
		ImGui::BeginChild("viewport_details", ImVec2(0.0f, 0.0f), false);
		DrawViewportDetails(ui.viewports[g_selectedViewportIndex], g_selectedViewportIndex, allowModify);
		ImGui::EndChild();
	}

	void DrawPropVisibility(me_obj* obj, bool allowModify)
	{
		ImGui::BeginDisabled(!allowModify);
		for (int i = 0; i < MAX_VIEWPORTS; ++i)
		{
			ImGui::PushID(i);
			char label[32];
			sprintf(label, "Show V%d", i);
			ImGui::Checkbox(label, &obj->show[i]);
			if ((i % 4) != 3)
			{
				ImGui::SameLine();
			}
			ImGui::PopID();
		}
		ImGui::EndDisabled();
	}

	void DrawPointCloudDetails(me_pcRecord* pc)
	{
		ImGui::Text("Points: %d / %d", pc->n, pc->capacity);
		ImGui::Text("Volatile: %s", pc->isVolatile ? "Yes" : "No");
		ImGui::Text("Handle type mask: 0x%X", pc->handleType);
		ImGui::Text("Flags: 0x%X", pc->flag);
	}

	void DrawLinePieceDetails(me_line_piece* line)
	{
		ImGui::Text("Line type: %s", line->type == me_line_piece::straight ? "Straight" : "Bezier");
		ImGui::Text("Start (%.2f, %.2f, %.2f)", line->attrs.st.x, line->attrs.st.y, line->attrs.st.z);
		ImGui::Text("End   (%.2f, %.2f, %.2f)", line->attrs.end.x, line->attrs.end.y, line->attrs.end.z);
		ImGui::Text("Dash: %u Width: %u", line->attrs.dash, line->attrs.width);
	}

	void DrawSpriteDetails(me_sprite* sprite)
	{
		ImGui::Text("Sprite resource: %s", sprite->resName.c_str());
		ImGui::Text("Display size: %.1f x %.1f", sprite->dispWH.x, sprite->dispWH.y);
		ImGui::Text("Type: %s", sprite->type == me_sprite::rgba_t ? "RGBA" : "SVG");
		ImGui::Text("Display flags: 0x%X", sprite->display_flags);
	}

	void DrawLineBunchDetails(me_linebunch* bunch)
	{
		ImGui::Text("Segments: %d / %d", bunch->n, bunch->capacity);
		ImGui::Text("GPU buffer id: %u", bunch->line_buf.id);
	}

	void DrawRegionCloudDetails(me_region_cloud_bunch* regions)
	{
		ImGui::Text("Region items: %zu", regions->items.size());
	}

	void DrawWorldUiDetails(me_world_ui* worldUi)
	{
		ImGui::Text("World UI selectable: %s", worldUi->selectable[0] ? "Varies" : "Per viewport");
	}

	void DrawGltfDetails(gltf_object* obj)
	{
		ImGui::Text("Animation base: %d, playing: %d, next: %d", obj->baseAnimId, obj->playingAnimId, obj->nextAnimId);
		ImGui::Text("Material variant: %d", obj->material_variant);
		ImGui::Text("Team color: 0x%X", obj->team_color);
		ImGui::Text("Nodes: %zu", obj->nodeattrs.size());
		ImGui::Text("GLTF class id: %d", obj->gltf_class_id);
	}

	void DrawPropDetails(namemap_t* entry, bool allowModify)
	{
		if (!entry)
		{
			ImGui::TextDisabled("No prop selected.");
			return;
		}

		me_obj* obj = entry->obj;
		if (!obj)
		{
			ImGui::TextDisabled("Prop data unavailable (null object).");
			return;
		}

		ImGui::Text("Name: %s", obj->name.c_str());
		ImGui::Text("Type: %s", PropTypeToString(entry));
		ImGui::Text("Instance id: %d", entry->instance_id);

		ImGui::Separator();
		DrawPropVisibility(obj, allowModify);

		ImGui::Separator();
		switch (entry->type)
		{
		case me_pcRecord::type_id:
			DrawPointCloudDetails(static_cast<me_pcRecord*>(obj));
			break;
		case me_line_piece::type_id:
			DrawLinePieceDetails(static_cast<me_line_piece*>(obj));
			break;
		case me_sprite::type_id:
			DrawSpriteDetails(static_cast<me_sprite*>(obj));
			break;
		case me_world_ui::type_id:
			DrawWorldUiDetails(static_cast<me_world_ui*>(obj));
			break;
		case me_region_cloud_bunch::type_id:
			DrawRegionCloudDetails(static_cast<me_region_cloud_bunch*>(obj));
			break;
		case me_linebunch::type_id:
			if (line_bunches.get(obj->name) == obj)
				DrawLineBunchDetails(static_cast<me_linebunch*>(obj));
			else if (geometries.get(obj->name) == obj)
				ImGui::TextUnformatted("Geometry instance (details not yet implemented).");
			else
				ImGui::TextDisabled("No additional information for this prop type.");
			break;
		default:
			if (entry->type >= gltf_object::type_id)
				DrawGltfDetails(static_cast<gltf_object*>(obj));
			else
				ImGui::TextDisabled("No additional information for this prop type.");
			break;
		}
	}

	void DrawPropsTab(bool allowModify)
	{
		int count = static_cast<int>(global_name_map.ls.size());
		if (count == 0)
		{
			ImGui::TextDisabled("No props registered.");
			return;
		}

		if (g_selectedPropIndex < 0 || g_selectedPropIndex >= count || global_name_map.get(g_selectedPropIndex) == nullptr)
		{
			for (int i = 0; i < count; ++i)
			{
				if (auto* entry = global_name_map.get(i); entry && entry->obj)
				{
					g_selectedPropIndex = i;
					break;
				}
			}
		}

		ImGui::BeginChild("prop_list", ImVec2(220.0f, 0.0f), true, ImGuiWindowFlags_HorizontalScrollbar);
		for (int i = 0; i < count; ++i)
		{
			auto* entry = global_name_map.get(i);
			if (!entry || !entry->obj) continue;

			ImGui::PushID(i);
			const std::string& name = global_name_map.getName(i);
			std::string label = name + "##prop";
			if (ImGui::Selectable(label.c_str(), g_selectedPropIndex == i))
			{
				g_selectedPropIndex = i;
			}
			ImGui::TextDisabled("%s", PropTypeToString(entry));
			ImGui::PopID();
		}
		ImGui::EndChild();

		ImGui::SameLine();
		ImGui::BeginChild("prop_details", ImVec2(0.0f, 0.0f), false);
		if (g_selectedPropIndex >= 0 && g_selectedPropIndex < count)
		{
			DrawPropDetails(global_name_map.get(g_selectedPropIndex), allowModify);
		}
		else
		{
			ImGui::TextDisabled("Select a prop to inspect its details.");
		}
		ImGui::EndChild();
	}

	void DrawPerformanceTab(bool allowModify)
	{
		ImGui::Text("Collected timing counters (TOC)");
		ImGui::Separator();

		if (allowModify)
		{
			if (ImGui::Button("Reset profiling"))
			{
				reset_tic();
			}
			ImGui::SameLine();
		}

		if (ImGui::Button("Copy to clipboard"))
		{
			ImGui::SetClipboardText(staticString.c_str());
		}

		ImGui::BeginChild("profiling_log", ImVec2(0.0f, 0.0f), true, ImGuiWindowFlags_HorizontalScrollbar);
		ImGui::TextUnformatted(staticString.c_str());
		ImGui::EndChild();
	}

	const char* GetButtonConfigName(ui_state_t::WorkspaceOperationBTN btn)
	{
		switch (btn)
		{
		case ui_state_t::MouseLB: return "LB";
		case ui_state_t::MouseMB: return "MB";
		case ui_state_t::MouseRB: return "RB";
		case ui_state_t::CtrlMouseLB: return "^LB";
		case ui_state_t::CtrlMouseMB: return "^MB";
		case ui_state_t::CtrlMouseRB: return "^RB";
		default: return "Unknown";
		}
	}

	void DrawButtonConfigRadio(const char* label, ui_state_t::WorkspaceOperationBTN& target,
		ui_state_t::WorkspaceOperationBTN& other1, ui_state_t::WorkspaceOperationBTN& other2)
	{
		ImGui::TextUnformatted(label);
		ImGui::Indent();

		for (int i = 0; i < 6; i++)
		{
			ui_state_t::WorkspaceOperationBTN btn = static_cast<ui_state_t::WorkspaceOperationBTN>(i);
			bool isSelected = (target == btn);

			// Create unique label for each radio button to avoid name collisions
			std::string radioLabel = std::string(GetButtonConfigName(btn)) + "##" + label;

			if (ImGui::RadioButton(radioLabel.c_str(), isSelected))
			{
				// Swap if another action is using this button
				if (other1 == btn)
				{
					other1 = target;
				}
				else if (other2 == btn)
				{
					other2 = target;
				}
				target = btn;
			}
			if (i != 5) ImGui::SameLine(0, 5);
		}

		ImGui::Unindent();
	}

	void DrawManualSettingsTab(bool allowModify)
	{
		ImGui::TextWrapped("Enable 'allow modify' to adjust runtime parameters. Values update immediately.");
		ImGui::Separator();
		ImGui::TextUnformatted("SSAO Settings");

		ImGui::DragFloat("uSampleRadius", &ssao_uniforms.uSampleRadius, 0.1, 0, 100);
		ImGui::DragFloat("uBias", &ssao_uniforms.uBias, 0.003, -0.5, 0.5);
		ImGui::DragFloat2("uAttenuation", ssao_uniforms.uAttenuation, 0.01, -10, 10);
		ImGui::DragFloat("weight", &ssao_uniforms.weight, 0.1, -10, 10);
		ImGui::DragFloat2("uDepthRange", ssao_uniforms.uDepthRange, 0.05, 0, 100);


		ImGui::TextUnformatted("Bloom Settings");
		ImGui::DragFloat("GLTF_illumfac", &GLTF_illumfac, 0.1f, 0, 300);
		ImGui::DragFloat("GLTF_illumrng", &GLTF_illumrng, 0.001f, 1.0, 1.5f);

		ImGui::BeginDisabled(!allowModify);
		ImGui::SeparatorText("Workspace Mouse Button Configuration");
		DrawButtonConfigRadio("Operation Trigger:", ui.operation_trigger, ui.workspace_pan, ui.workspace_orbit);
		DrawButtonConfigRadio("Workspace Pan:", ui.workspace_pan, ui.operation_trigger, ui.workspace_orbit);
		DrawButtonConfigRadio("Workspace Orbit:", ui.workspace_orbit, ui.operation_trigger, ui.workspace_pan);

		ImGui::EndDisabled();
	}

	void DrawWorkspaceTab()
	{
		ImGui::SeparatorText("Mouse");
		ImGui::Text("Pos: (%.1f, %.1f)", ui.mouseX, ui.mouseY);
		ImGui::Text("Buttons: L=%d M=%d R=%d", ui.mouseLeft ? 1 : 0, ui.mouseMiddle ? 1 : 0, ui.mouseRight ? 1 : 0);
		ImGui::Text("Capturing viewport: %d", ui.mouseCaptuingViewport);

		ImGui::SeparatorText("Touch");
		ImGui::Text("Touches: %d", (int)ui.touches.size());
		if (ImGui::TreeNode("Touch list"))
		{
			for (int i = 0; i < (int)ui.touches.size(); ++i)
			{
				auto& t = ui.touches[i];
				ImGui::Text("#%d id=%d pos=(%.1f, %.1f) starting=%d consumed=%d",
					i, t.id, t.touchX, t.touchY, t.starting ? 1 : 0, t.consumed ? 1 : 0);
			}
			ImGui::TreePop();
		}

		ImGui::SeparatorText("Keyboard");
		ImGui::Text("Modifiers: ctrl=%d shift=%d alt=%d", ui.ctrl ? 1 : 0, ui.shift ? 1 : 0, ui.alt ? 1 : 0);
		ImGui::Text("Chord map sizes: last=%d this=%d", (int)ui.lastChordTriggered.size(), (int)ui.thisChordTriggered.size());

		ImGui::SeparatorText("Joystick");
		ImGui::Text("Joysticks present: %d", ui.joystickCount);
		if (ImGui::TreeNode("Joystick values"))
		{
			for (auto& kv : ui.joystickValues)
				ImGui::Text("%s = %.3f", kv.first.c_str(), kv.second);
			ImGui::TreePop();
		}

		ImGui::SeparatorText("Monitors");
#ifndef __EMSCRIPTEN__
		{
			auto monitors = GetMonitors();
			ImGui::Text("Monitor count: %d", (int)monitors.size());
			for (auto& m : monitors)
				ImGui::Text("#%d %s area=(%d,%d %dx%d)", m.index, m.name.c_str(), m.x, m.y, m.w, m.h);
		}
#else
		ImGui::TextDisabled("Monitors not available on WASM.");
#endif
	}

	void DrawCycleGuiDebugWindow(ImGuiViewport* viewport)
	{
		if (!g_showCycleGuiDebug)
		{
			return;
		}

		ImGui::SetNextWindowSize(ImVec2(720.0f, 520.0f), ImGuiCond_FirstUseEver);
		if (viewport)
		{
			ImGui::SetNextWindowViewport(viewport->ID);
		}

		if (ImGui::Begin("CycleGUI Debug", &g_showCycleGuiDebug, ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoDocking))
		{
			if (ImGui::BeginTabBar("CycleGuiDebugTabs", ImGuiTabBarFlags_None))
			{
				const char* allowLabel = g_allowCycleGuiModify ? "\uf058 allow modify" : "\uf1db allow modify";
				if (ImGui::TabItemButton(allowLabel, ImGuiTabItemFlags_Leading | ImGuiTabItemFlags_NoTooltip))
				{
					g_allowCycleGuiModify = !g_allowCycleGuiModify;
				}

				if (ImGui::BeginTabItem("Viewports"))
				{
					DrawViewportsTab(g_allowCycleGuiModify);
					ImGui::EndTabItem();
				}
				if (ImGui::BeginTabItem("Props"))
				{
					DrawPropsTab(g_allowCycleGuiModify);
					ImGui::EndTabItem();
				}
				if (ImGui::BeginTabItem("Performance"))
				{
					DrawPerformanceTab(g_allowCycleGuiModify);
					ImGui::EndTabItem();
				}
				if (ImGui::BeginTabItem("Workspace"))
				{
					DrawWorkspaceTab();
					ImGui::EndTabItem();
				}
				if (ImGui::BeginTabItem("Settings"))
				{
					DrawManualSettingsTab(g_allowCycleGuiModify);
					ImGui::EndTabItem();
				}
				ImGui::EndTabBar();
			}
		}
		ImGui::End();
	}
}
