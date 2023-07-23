#include "cycleui.h"
#include <array>
#include <stdio.h>
#include <functional>
#include <iomanip>
#include <map>
#include <string>
#include <vector>
#include "imgui.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

unsigned char* stack = nullptr;
NotifyStateChangedFunc stateCallback;
BeforeDrawFunc beforeDraw;

std::map<int, std::vector<unsigned char>> map;
std::vector<unsigned char> v_stack;
std::map<int, point_cloud> pcs;

#define ReadInt *((int*)ptr); ptr += 4
#define ReadString std::string(ptr + 4, ptr + 4 + *((int*)ptr)); ptr += *((int*)ptr) + 4
#define ReadBool *((bool*)ptr); ptr += 1

#define WriteInt32(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=x; pr+=4;}
#define WriteFloat(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=2; pr+=4; *(float*)pr=x; pr+=4;}
#define WriteDouble(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=3; pr+=4; *(double*)pr=x; pr+=8;}
#define WriteBytes(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=4; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteString(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=5; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteBool(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=6; pr+=4; *(bool*)pr=x; pr+=1;}


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

		if (flag & 2) //shutdown.
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
	stack = v_stack.data();
}


void ProcessUIStack()
{
	// process workspace:
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

	// ui_stack
	beforeDraw();
	auto ptr = stack;
	if (ptr == nullptr) return; // skip if not initialized.

	auto plen = ReadInt;

	unsigned char buffer[1024];
	auto pr = buffer;
	bool stateChanged = false;
	for (int i = 0; i < plen; ++i)
	{
		auto pid = ReadInt;
		std::array<std::function<void()>, 5> UIFuns = {
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
				ImGui::InputTextWithHint(prompt.c_str(), hint.c_str(), textBuffer, 256);
				WriteString(textBuffer, strlen(textBuffer))
			}
		};
		auto str = ReadString;

		char windowLabel[256];
		sprintf(windowLabel, "%s##pid%d", str.c_str(), pid);
		ImGui::Begin(windowLabel);

		ImGui::PushItemWidth(ImGui::GetFontSize() * -6);
		auto flags = ReadInt;
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
		ImGui::End();
	}
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