#include "cycleui.h"
#include <array>
#include <stdio.h>
#include <functional>
#include <imgui_internal.h>
#include <iomanip>
#include <map>
#include <string>
#include <vector>
#include "imgui.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

unsigned char* cgui_stack = nullptr;

NotifyStateChangedFunc stateCallback;
NotifyWorkspaceChangedFunc workspaceCallback;
BeforeDrawFunc beforeDraw;

std::map<int, std::vector<unsigned char>> map;
std::vector<unsigned char> v_stack;
std::map<int, point_cloud> pcs;

#define ReadInt *((int*)ptr); ptr += 4
#define ReadString std::string(ptr + 4, ptr + 4 + *((int*)ptr)); ptr += *((int*)ptr) + 4
#define ReadBool *((bool*)ptr); ptr += 1
#define ReadFloat *((float*)ptr); ptr += 4
#define ReadArr(type, len) (type*)ptr; ptr += len * sizeof(type);

#define WriteInt32(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=x; pr+=4;}
#define WriteFloat(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=2; pr+=4; *(float*)pr=x; pr+=4;}
#define WriteDouble(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=3; pr+=4; *(double*)pr=x; pr+=8;}
#define WriteBytes(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=4; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteString(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=5; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteBool(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=6; pr+=4; *(bool*)pr=x; pr+=1;}

// should be called inside before draw.
void ProcessWorkspaceQueue(void* wsqueue)
{
	// process workspace:
	auto* ptr = (unsigned char*) wsqueue;
	auto& wstate = ui_state.workspace_state.top();
	if (wstate.selecting_mode == paint && !ui_state.selecting)
	{
		auto pos = ImGui::GetMainViewport()->Pos;
		ImGui::GetBackgroundDrawList(ImGui::GetMainViewport())->AddCircle(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), wstate.paint_selecting_radius, 0xff0000ff);
	}
	if (ui_state.selecting)
	{
		if (wstate.selecting_mode == drag)
		{
			auto pos = ImGui::GetMainViewport()->Pos;
			auto st = ImVec2(std::min(ui_state.mouseX, ui_state.select_start_x) + pos.x, std::min(ui_state.mouseY, ui_state.select_start_y) + pos.y);
			auto ed = ImVec2(std::max(ui_state.mouseX, ui_state.select_start_x) + pos.x, std::max(ui_state.mouseY, ui_state.select_start_y) + pos.y);
			ImGui::GetBackgroundDrawList(ImGui::GetMainViewport())->AddRectFilled(st, ed, 0x440000ff);
			ImGui::GetBackgroundDrawList(ImGui::GetMainViewport())->AddRect(st, ed,	0xff0000ff);
		}
		else if (wstate.selecting_mode == paint)
		{
			auto pos = ImGui::GetMainViewport()->Pos;
			ImGui::GetBackgroundDrawList(ImGui::GetMainViewport())->AddCircleFilled(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), wstate.paint_selecting_radius, 0x440000ff);
			ImGui::GetBackgroundDrawList(ImGui::GetMainViewport())->AddCircle(ImVec2(ui_state.mouseX + pos.x, ui_state.mouseY + pos.y), wstate.paint_selecting_radius, 0xff0000ff);
		}
	}

	int apiN = 0;
	while (true) {
		auto api = ReadInt;
		if (api == -1) return;

		std::function<void()> UIFuns[] = {
			[&]	{ //0
				auto name = ReadString;
				point_cloud pc;
				pc.isVolatile = ReadBool;
				pc.capacity = ReadInt;
				pc.initN = ReadInt;
				pc.x_y_z_Sz = ReadArr(glm::vec4, pc.initN);
				pc.color = ReadArr(uint32_t, pc.initN);
				pc.position[0] = ReadFloat; //this marco cannot be used as function, it actually consists of several statements(manipulating ptr).
				pc.position[1] = ReadFloat;
				pc.position[2] = ReadFloat;
				pc.quaternion[0] = ReadFloat;
				pc.quaternion[1] = ReadFloat;
				pc.quaternion[2] = ReadFloat;
				pc.quaternion[3] = ReadFloat;

				AddPointCloud(name, pc);
			},
			[&]
			{  //1
				auto name = ReadString;
				auto len = ReadInt;
				auto xyzSz = ReadArr(glm::vec4, len);
				auto color = ReadArr(uint32_t, len);

				AppendVolatilePoints(name, len, xyzSz, color);
			},
			[&]
			{  //2
				auto name = ReadString;

				ClearVolatilePoints(name);
			},
			[&]
			{  //3
				auto cls_name = ReadString;
				auto length = ReadInt;
				auto bytes = ReadArr(unsigned char, length);
				ModelDetail detail;
				detail.center.x = ReadFloat;
				detail.center.y = ReadFloat;
				detail.center.z = ReadFloat;
				detail.rotate.x = ReadFloat;
				detail.rotate.y = ReadFloat;
				detail.rotate.z = ReadFloat;
				detail.rotate.w = ReadFloat;
				detail.scale = ReadFloat;

				LoadModel(cls_name, bytes, length, detail);
			},
			[&]
			{  //4
				auto cls_name = ReadString;
				auto name = ReadString;
				glm::vec3 new_position;
				new_position.x = ReadFloat;
				new_position.y = ReadFloat;
				new_position.z = ReadFloat;
				glm::quat new_quaternion;
				new_quaternion.x = ReadFloat;
				new_quaternion.y = ReadFloat;
				new_quaternion.z = ReadFloat;
				new_quaternion.w = ReadFloat;

				PutModelObject(cls_name, name, new_position, new_quaternion);
			},
			[&]
			{  //5
				auto name = ReadString;
				glm::vec3 new_position;
				new_position.x = ReadFloat;
				new_position.y = ReadFloat;
				new_position.z = ReadFloat;
				glm::quat new_quaternion;
				new_quaternion.x = ReadFloat;
				new_quaternion.y = ReadFloat;
				new_quaternion.z = ReadFloat;
				new_quaternion.w = ReadFloat;
				auto time = ReadFloat;

				MoveObject(name, new_position, new_quaternion, time);
			}
		};
		UIFuns[api]();
		apiN++;
	}
}

void GenerateStackFromPanelCommands(unsigned char* buffer, int len)
{
	auto ptr = buffer;
	auto plen = ReadInt;

	for (int i = 0; i < plen;++i)
	{
		auto st_ptr = ptr;
		auto pid = ReadInt;
		if (map.find(pid) != map.end())
			map[pid].clear();

		auto name = ReadString;
		auto flag = ReadInt;
		auto panelWidth = ReadInt;
		auto panelHeight = ReadInt;
		auto panelLeft = ReadInt;
		auto panelTop = ReadInt;

		if ((flag & 2)!=0) //shutdown.
		{
			map.erase(pid);
		}
		else
		{
			auto& bytes = map[pid];
			bytes.reserve(ptr - st_ptr);
			std::copy(st_ptr, ptr, std::back_inserter(bytes));
			// initialized;

			auto commandLength = ReadInt;
			for (int j = 0; j < commandLength; ++j)
			{
				auto type = ReadInt;
				if (type == 0) //type 0: byte command.
				{
					auto len = ReadInt;
					bytes.reserve(bytes.size() + len);
					std::copy(ptr, ptr + len, std::back_inserter(bytes));
					ptr += len;
				}
				else if (type == 1) //type 1: cache.
				{
					auto len = ReadInt;
					bytes.reserve(bytes.size() + len);
					auto initLen = ReadInt;
					std::copy(ptr, ptr + initLen, std::back_inserter(bytes));
					for (int k = 0; k < len - initLen; ++k)
						bytes.push_back(0);
					ptr += initLen;
				}
			}
			for (int j = 0; j < 4; ++j)
				bytes.push_back(0);
		}
	}

	v_stack.clear();

	int mlen = map.size();
	for (size_t i = 0; i < 4; ++i) {
		v_stack.push_back(((uint8_t*)&mlen)[i]);
	}
	for (const auto& entry : map)
	{
		const auto& bytes = entry.second;
		v_stack.insert(v_stack.end(), bytes.begin(), bytes.end());
	}
	cgui_stack = v_stack.data();
}

struct wndState
{
	ImVec2 Pos, Size;
};
std::map<int, wndState> im;

void ProcessUIStack()
{
	ImGuiStyle& style = ImGui::GetStyle();
	// ui_stack
	beforeDraw();

	auto ptr = cgui_stack;
	if (ptr == nullptr) return; // skip if not initialized.

	auto plen = ReadInt;

	unsigned char buffer[1024];
	auto pr = buffer;
	bool stateChanged = false;
	bool wndShown = true;

	for (int i = 0; i < plen; ++i)
	{
		auto pid = ReadInt;
		std::function<void()> UIFuns[] = {
			[&] { assert(false); }, // 0: this is not a valid control(cache command)
			[&] //1: text
			{
				auto str = ReadString;
				ImGui::Text(str.c_str());
			},
			[&] // 2: button
			{
				auto cid = ReadInt;
				auto str = ReadString;

				char buttonLabel[256];
				sprintf(buttonLabel, "%s##btn%d", str.c_str(), cid);
				if (ImGui::Button(buttonLabel)) {
					stateChanged = true;
					WriteInt32(1)
				}
			},
			[&] // 3: checkbox
			{
				auto cid = ReadInt;
				auto str = ReadString;
				auto checked = ReadBool;

				char checkboxLabel[256];
				sprintf(checkboxLabel, "%s##checkbox%d", str.c_str(), cid);
				if (ImGui::Checkbox(checkboxLabel, &checked)) {
					stateChanged = true;
					WriteBool(checked)
				}
			},
			[&] // 4: TextInput
			{
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto hint = ReadString;
				char* textBuffer = (char*)ptr;
				ptr += 256;
				ImGui::PushItemWidth(300);

				ImGui::InputTextWithHint(prompt.c_str(), hint.c_str(), textBuffer, 256);
				WriteString(textBuffer, strlen(textBuffer))
			},
			[&] // 5: Listbox
			{
				auto cid = ReadInt;
				auto prompt = ReadString;
				auto h = ReadInt;
				auto len = ReadInt;
				auto selecting = ReadInt;
				ImGui::SeparatorText(prompt.c_str());
				char lsbxid[256];
				sprintf(lsbxid, "%s##listbox", prompt.c_str());

				ImGuiWindowFlags window_flags = ImGuiWindowFlags_HorizontalScrollbar;
				if (ImGui::BeginChild(lsbxid, ImVec2(ImGui::GetContentRegionAvail().x, h * ImGui::GetTextLineHeightWithSpacing()), true, window_flags))
				{
					for (int n = 0; n < len; n++)
					{
						auto item = ReadString;
						sprintf(lsbxid, "%s##lb%s_%d", item.c_str(), prompt.c_str(), n);
						if (ImGui::Selectable(lsbxid, selecting == n)) {
							stateChanged = true;
							selecting = n;
							WriteInt32(n)
						}
						if (selecting == n)
							ImGui::SetItemDefaultFocus();
					}
				}else
				{
					for (int n = 0; n < len; n++)
					{
						auto item = ReadString;
					}
				}
				ImGui::EndChild();
			},
			[&] //6: button group
			{
				auto cid = ReadInt;
				auto prompt = ReadString;

				auto flag = ReadInt;
				auto buttons_count = ReadInt;

				ImGui::SeparatorText(prompt.c_str());

				float window_visible_x2 = ImGui::GetWindowPos().x + ImGui::GetWindowContentRegionMax().x;
				for (int n = 0; n < buttons_count; n++)
				{
					auto btn_txt = ReadString;
					char lsbxid[256];
					sprintf(lsbxid, "%s##btng%s_%d", btn_txt.c_str(), prompt.c_str(), n);

					auto sz = ImGui::CalcTextSize(btn_txt.c_str());
					sz.x += style.FramePadding.x * 2;
					sz.y += style.FramePadding.y * 2;
					if (ImGui::Button(lsbxid, sz))
					{
						stateChanged = true;
						WriteInt32(n)
					}
					float last_button_x2 = ImGui::GetItemRectMax().x;
					float next_button_x2 = last_button_x2 + style.ItemSpacing.x + sz.x; // Expected position if next button was on same line
					if (n < buttons_count - 1 && (next_button_x2 < window_visible_x2 || (flag & 1)!=0))
						ImGui::SameLine();
				}

			},
			[&] //7: Searchable Table.
			{
				auto cid = ReadInt;
				auto prompt = ReadString;

				ImGui::SeparatorText(prompt.c_str());

				char searcher[256];
				sprintf(searcher, "%s##search", prompt.c_str());
				auto skip = ReadInt; //from slot "row" to end.
				auto cols = ReadInt;
				ImGuiTableFlags flags = ImGuiTableFlags_RowBg | ImGuiTableFlags_Borders
					| ImGuiTableFlags_Resizable ;

				char* text = (char*)(ptr + skip);

				ImGui::PushItemWidth(200);
				ImGui::InputTextWithHint(searcher, "Search", text, 256);
				if (ImGui::BeginTable(prompt.c_str(), cols, flags))
				{
					for (int i = 0; i < cols; ++i)
					{
						auto header = ReadString;
						ImGui::TableSetupColumn(header.c_str());
					}
					ImGui::TableHeadersRow();

					auto rows = ReadInt;
					// Submit dummy contents
					for (int row = 0; row < rows; row++)
					{
						ImGui::TableNextRow();
						for (int column = 0; column < cols; column++)
						{
							ImGui::TableSetColumnIndex(column);
							auto type = ReadInt;
#define TableResponseBool(x) stateChanged=true; char ret[10]; ret[0]=1; *(int*)(ret+1)=row; *(int*)(ret+5)=column; ret[9]=x; WriteBytes(ret, 10);
#define TableResponseInt(x) stateChanged=true; char ret[13]; ret[0]=1; *(int*)(ret+1)=row; *(int*)(ret+5)=column; *(int*)(ret+9)=x; WriteBytes(ret, 13);
							if (type == 0)
							{
								auto label = ReadString;
								char hashadded[256];
								sprintf(hashadded, "%s##%d_%d", label.c_str(), row, column);
								if (ImGui::Selectable(hashadded))
								{
									TableResponseBool(true);
								};
							}else if (type == 1)
							{
								auto label = ReadString;
								char hashadded[256];
								sprintf(hashadded, "%s##%d_%d", label.c_str(), row, column);
								auto hint = ReadString;
								if (ImGui::Selectable(hashadded))
								{
									TableResponseBool(true);
								};
								if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort) && ImGui::BeginTooltip())
								{
									ImGui::PushTextWrapPos(ImGui::GetFontSize() * 35.0f);
									ImGui::TextUnformatted(hint.c_str());
									ImGui::PopTextWrapPos();
									ImGui::EndTooltip();
								}
							}else if (type == 2) // btn group
							{
								auto len = ReadInt;
								ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 2);
								ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(GImGui->Style.ItemInnerSpacing.x/2, GImGui->Style.ItemInnerSpacing.y));

								for (int i = 0; i < len; ++i)
								{
									auto label = ReadString;
									char lsbxid[256];
									sprintf(lsbxid, "%s##btng%s_%d", label.c_str(), prompt.c_str(), row);
									if (ImGui::SmallButton(lsbxid))
									{
										TableResponseInt(i);
									}
									if (i < len - 1) ImGui::SameLine();
								}
								ImGui::PopStyleVar(2);
							}else if (type ==3)
							{
								auto len = ReadInt;
								ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, 2);
								ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(GImGui->Style.ItemInnerSpacing.x / 2, GImGui->Style.ItemInnerSpacing.y));

								for (int i = 0; i < len; ++i)
								{
									auto label = ReadString;
									auto hint = ReadString;
									char lsbxid[256];
									sprintf(lsbxid, "%s##btng%s_%d", label.c_str(), prompt.c_str(), row);
									if (ImGui::SmallButton(lsbxid))
									{
										TableResponseInt(i);
									}
									if (ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort) && ImGui::BeginTooltip())
									{
										ImGui::PushTextWrapPos(ImGui::GetFontSize() * 35.0f);
										ImGui::TextUnformatted(hint.c_str());
										ImGui::PopTextWrapPos();
										ImGui::EndTooltip();
									}
									if (i < len - 1) ImGui::SameLine();
								}
								ImGui::PopStyleVar(2);
							}else if (type ==4) //checkbox.
							{
								auto len = ReadInt;
								for (int i = 0; i < len; ++i)
								{
									auto init = ReadBool;
									char lsbxid[256];
									sprintf(lsbxid, "##%s_%d_chk", prompt.c_str(), row);
									if (ImGui::Checkbox(lsbxid,&init))
									{
										TableResponseBool(init);
									}
								}
							}else if (type ==5) 
							{
								
							}
							else if (type == 6)
							{ // set color, doesn't apply to column.
								auto color = ReadInt;
								ImGui::TableSetBgColor(ImGuiTableBgTarget_RowBg1, color);
								column -= 1;
							}
						}
					}
					ImGui::EndTable();
					ptr += 256;
				}
				else ptr += skip;
			},
			[&]
			{ // 8 : closing button.
				auto cid = ReadInt;
				if (!wndShown)
				{
					stateChanged = true;
					WriteBool(true);
				}
			}
		};
		auto str = ReadString;

		// char windowLabel[256];
		// sprintf(windowLabel, "%s##pid%d", str.c_str(), pid);

		ImGuiWindowFlags window_flags = 0;
		auto flags = ReadInt;
		auto relPanel = ReadInt;
		auto relPivotX = ReadFloat;
		auto relPivotY = ReadFloat;
		auto myPivotX = ReadFloat;
		auto myPivotY = ReadFloat;
		auto panelWidth = ReadInt;
		auto panelHeight = ReadInt;
		auto panelLeft = ReadInt;
		auto panelTop = ReadInt;

		// Size:
		auto pivot = ImVec2(myPivotX, myPivotY);
		if ((flags & 8) !=0)
		{
			// not resizable
			if ((flags & (16)) != 0) // autoResized
				window_flags |= ImGuiWindowFlags_AlwaysAutoResize;
			else { // must have initial w/h
				window_flags |= ImGuiWindowFlags_NoResize;
				ImGui::SetNextWindowSize(ImVec2(panelWidth, panelHeight));
			}
		}else
		{
			// initial w/h, maybe read from ini.
			ImGui::SetNextWindowSize(ImVec2(panelWidth, panelHeight), ImGuiCond_FirstUseEver);
		}

		// Position
		auto vp = ImGui::GetMainViewport();
		if ((flags & 32) == 0) {
			window_flags |= ImGuiWindowFlags_NoMove;
			if (relPanel < 0 || !im.contains(relPanel))
				ImGui::SetNextWindowPos(ImVec2(panelLeft + vp->Pos.x, panelTop + vp->Pos.y), 0, pivot);
			else
			{
				auto& wnd = im[relPanel];
				ImGui::SetNextWindowPos(ImVec2(panelLeft + wnd.Pos.x +wnd.Size.x*relPivotX, panelTop + wnd.Pos.y+wnd.Size.y*relPivotY),0, pivot);
			}
		}else
		{
			// initialize w/h.

			if ((flags & 128) !=0)
			{
				window_flags |= ImGuiWindowFlags_NoCollapse;
				window_flags |= ImGuiDockNodeFlags_NoCloseButton;
				window_flags |= ImGuiWindowFlags_NoDocking;
				auto io = ImGui::GetIO();
				ImGui::SetNextWindowPos(ImVec2(io.MousePos.x, io.MousePos.y), ImGuiCond_Appearing, ImVec2(0.5,0.5));
			}
			else {
				if (relPanel < 0 || !im.contains(relPanel))
					ImGui::SetNextWindowPos(ImVec2(panelLeft + vp->Pos.x, panelTop + vp->Pos.y), ImGuiCond_Appearing, pivot);
				else
				{
					auto& wnd = im[relPanel];
					ImGui::SetNextWindowPos(ImVec2(panelLeft + wnd.Pos.x + wnd.Size.x * relPivotX, panelTop + wnd.Pos.y + wnd.Size.y * relPivotY), ImGuiCond_Appearing, pivot);
				}
			}
		}

		// Decorators
		if ((flags & 4) == 0)
			window_flags |= ImGuiWindowFlags_NoTitleBar;
		if ((flags & 64) !=0)
		{
			ImGuiWindowClass topmost;
			topmost.ClassId = ImHashStr("TopMost");
			topmost.ViewportFlagsOverrideSet = ImGuiViewportFlags_TopMost;
			ImGui::SetNextWindowClass(&topmost);
		}


		bool* p_show = (flags & 256) ? &wndShown : NULL;
		ImGui::Begin(str.c_str(), p_show, window_flags);

		ImGui::PushItemWidth(ImGui::GetFontSize() * -6);
		if (flags & 1) // freeze.
		{
			ImGui::BeginDisabled(true);
		}
		while (true)
		{
			auto ctype = ReadInt;
			if (ctype == 0) break;
			UIFuns[ctype]();
		}
		if (flags & 1) // freeze.
		{
			ImGui::EndDisabled();
		}

		im.insert_or_assign(pid, wndState{ .Pos = ImGui::GetWindowPos(),.Size = ImGui::GetWindowSize() });
		ImGui::End();

		//ImGui::PopStyleVar(1);
	}

	// workspace manipulations:

	if (stateChanged)
		stateCallback(buffer, pr - buffer);
}

ui_state_t ui_state;
bool initialize()
{
	ui_state.workspace_state.push(workspace_state_desc{});
	return true;
}
static bool initialized = initialize();

void mouse_button_callback(GLFWwindow* window, int button, int action, int mods)
{
	static float clickingX, clickingY;

	if (ImGui::GetIO().WantCaptureMouse)
		return;

	auto& wstate = ui_state.workspace_state.top();

	if (action == GLFW_PRESS)
	{
		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			ui_state.mouseLeft = true;

			// todo: if anything else should do

			ui_state.selecting = true;
			if (!ui_state.ctrl)
				ClearSelection();
			if (wstate.selecting_mode == click)
			{
				clickingX = ui_state.mouseX;
				clickingY = ui_state.mouseY;
				// select but not trigger now.
			}
			else if (wstate.selecting_mode == drag)
			{
				ui_state.select_start_x = ui_state.mouseX;
				ui_state.select_start_y = ui_state.mouseY;
			}
			else if (wstate.selecting_mode == paint)
			{
				std::fill(ui_state.painter_data.begin(), ui_state.painter_data.end(), 0);
			}
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			ui_state.mouseMiddle = true;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			ui_state.mouseRight = true;
			break;
		}
	}
	else if (action == GLFW_RELEASE)
	{
		switch (button)
		{
		case GLFW_MOUSE_BUTTON_LEFT:
			ui_state.mouseLeft = false;
			if (ui_state.selecting)
			{
				ui_state.selecting = false;
				if (wstate.selecting_mode == click)
				{
					if (abs(ui_state.mouseX - clickingX)<10 && abs(ui_state.mouseY - clickingY)<10)
					{
						// trigger, (postponed to draw workspace)
						ui_state.extract_selection = true;
					}
				}else
				{
					ui_state.extract_selection = true;
				}
			}
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			ui_state.mouseMiddle = false;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			ui_state.mouseRight = false;
			break;
		}
	}
}

void cursor_position_callback(GLFWwindow* window, double xpos, double ypos)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	double deltaX = xpos - ui_state.mouseX;
	double deltaY = ypos - ui_state.mouseY;
	ui_state.mouseX = xpos;
	ui_state.mouseY = ypos;



	auto& wstate = ui_state.workspace_state.top();

	if (ui_state.mouseLeft)
	{
		// Handle left mouse button dragging (in process ui stack)
	}
	else if (ui_state.mouseMiddle || ui_state.selecting)
	{
		// Handle middle mouse button dragging
		if (ui_state.selecting)
		{
			if (wstate.selecting_mode == paint)
			{
				// draw_image.
			}
		}
		else {
			camera->RotateAzimuth(-deltaX);
			camera->RotateAltitude(deltaY * 1.5f);
		}
	}
	else if (ui_state.mouseRight || ui_state.selecting)
	{
		// Handle right mouse button dragging
		if (ui_state.selecting)
		{
			ui_state.selecting = false; // cancel selecting.
		}
		else {
			auto d = camera->distance * 0.0016f;
			camera->PanLeftRight(-deltaX * d);
			camera->PanBackForth(deltaY * d);
		}
	}
}

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset)
{
	if (ImGui::GetIO().WantCaptureMouse || ui_state.selecting)
		return;

	// Handle mouse scroll

	camera->Zoom(-yoffset * 0.1f);
}

void key_callback(GLFWwindow* window, int key, int scancode, int action, int mods) {
	// Check if the Ctrl key (left or right) is pressed
	if (key == GLFW_KEY_LEFT_CONTROL || key == GLFW_KEY_RIGHT_CONTROL) {
		if (action == GLFW_PRESS) {
			ui_state.ctrl = true;
		}
		else if (action == GLFW_RELEASE) {
			ui_state.ctrl = false;
		}
	}
}