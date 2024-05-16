#include "cycleui.h"
#include <array>
#include <stdio.h>
#include <functional>
#include <imgui_internal.h>
#include <iomanip>
#include <iostream>
#include <map>
#include <string>
#include <vector>

#include "imgui.h"
#include "implot.h"
#include "ImGuizmo.h"
// #include "imfilebrowser.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

namespace ImPlot
{
	struct ScrollingBuffer;
}

unsigned char* cgui_stack = nullptr;
bool cgui_refreshed = false;
char* appName = (char*)"Untitled App";

NotifyStateChangedFunc stateCallback;
NotifyWorkspaceChangedFunc workspaceCallback;
BeforeDrawFunc beforeDraw;

std::map<int, std::vector<unsigned char>> map;
std::vector<unsigned char> v_stack;
std::map<int, point_cloud> pcs;

#define ReadInt *((int*)ptr); ptr += 4
#define ReadString std::string(ptr + 4, ptr + 4 + *((int*)ptr)); ptr += *((int*)ptr) + 4
#define ReadBool *((bool*)ptr); ptr += 1
#define ReadByte *((unsigned char*)ptr); ptr += 1
#define ReadFloat *((float*)ptr); ptr += 4
#define ReadArr(type, len) (type*)ptr; ptr += len * sizeof(type);

#define WriteInt32(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=x; pr+=4;}
#define WriteFloat(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=2; pr+=4; *(float*)pr=x; pr+=4;}
#define WriteDouble(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=3; pr+=4; *(double*)pr=x; pr+=8;}
#define WriteBytes(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=4; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteString(x, len) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=5; pr+=4; *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len;}
#define WriteBool(x) {*(int*)pr=pid; pr+=4; *(int*)pr=cid; pr+=4; *(int*)pr=6; pr+=4; *(bool*)pr=x; pr+=1;}

glm::vec4 convertToVec4(uint32_t value) {
	// Extract 8-bit channels from the 32-bit integer
	float r = (value >> 24) & 0xFF;
	float g = (value >> 16) & 0xFF;
	float b = (value >> 8) & 0xFF;
	float a = value & 0xFF;

	// Normalize the channels to [0.0, 1.0]
	return glm::vec4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
}

void ProcessWorkspaceQueue(void* wsqueue)
{
	// process workspace:
	auto* ptr = (unsigned char*)wsqueue;
	auto* wstate = &ui_state.workspace_state.top();

	int apiN = 0;
	AllowWorkspaceData();

	std::function<void()> UIFuns[] = {
		[&] { //0
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
			pc.handleStr = ReadString;

			AddPointCloud(name, pc);
		},
		[&]
		{  //1
			auto name = ReadString;
			auto len = ReadInt;
			//std::cout << "vpnts=" << len << std::endl;
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
			uint8_t type = ReadByte;
			uint8_t coord = ReadByte;

			glm::vec3 new_position;
			new_position.x = ReadFloat;
			new_position.y = ReadFloat;
			new_position.z = ReadFloat;
			glm::quat new_quaternion;
			new_quaternion.x = ReadFloat;
			new_quaternion.y = ReadFloat;
			new_quaternion.z = ReadFloat;
			new_quaternion.w = ReadFloat;
			auto time = ReadInt;
			
			MoveObject(name, new_position, new_quaternion, time, type, coord);
		},

		[&]
		{  //6
			auto name = ReadString;
			auto selectable = ReadBool;

			SetObjectSelectable(name, selectable);  
		},

		[&]
		{
			//7 : Set Selection.
			ClearSelection();
			auto len = ReadInt;
			for (int i = 0; i < len; ++i)
			{
				auto str = ReadString;
				SetObjectSelected(str);
			}
		},
		[&]
		{
			//8: generate a new stack to select.
			auto id = ReadInt;
			auto str = ReadString;
			BeginWorkspace(id, str); // default is select.
			wstate = &ui_state.workspace_state.top();
		},
		[&]
		{
			//9 : end operation
			PopWorkspace();
			wstate = &ui_state.workspace_state.top();
		},
		[&]
		{
			//10: Guizmo MoveXYZ/RotateXYZ.
			auto id = ReadInt;
			auto str = ReadString;
			BeginWorkspace(id, str);
			wstate = &ui_state.workspace_state.top();

			auto realtime = ReadBool;
			auto type = ReadInt;
			if (type == 0)
				wstate->function = gizmo_moveXYZ;
			else if (type == 1)
				wstate->function = gizmo_rotateXYZ;

			wstate->gizmo_realtime = realtime;
			ui_state.selectedGetCenter = true;
		},
		[&]
		{
			// 11： Set apperance.
			wstate->useEDL = ReadBool;
			wstate->useSSAO = ReadBool;
			wstate->useGround = ReadBool;
			wstate->useBorder = ReadBool;
			wstate->useBloom = ReadBool;
			wstate->drawGrid = ReadBool;
			int colorTmp;
			colorTmp = ReadInt;
			wstate->hover_shine = convertToVec4(colorTmp);
			colorTmp = ReadInt;
			wstate->selected_shine = convertToVec4(colorTmp);
			colorTmp = ReadInt;
			wstate->hover_border_color = convertToVec4(colorTmp);
			colorTmp = ReadInt;
			wstate->selected_border_color = convertToVec4(colorTmp);
			colorTmp = ReadInt;
			wstate->world_border_color = convertToVec4(colorTmp);
		},
		[&]
		{
			// 12: Draw spot text
			auto name = ReadString;
			auto len = ReadInt;
			// std::cout << "draw spot texts" << len << " on " << name << std::endl;

			ptr = AppendSpotTexts(name, len, ptr);
		},
		[&]
		{
			// 13: Clear spot text.
			auto name = ReadString;
			ClearSpotTexts(name);
		},
		[&]
		{
			//14: Set Camera view.
			
			glm::vec3 lookAt;
			lookAt.x = ReadFloat;
			lookAt.y = ReadFloat;
			lookAt.z = ReadFloat;
			camera->stare = lookAt;
			camera->Azimuth = ReadFloat;
			camera->Altitude = ReadFloat;
			camera->distance = ReadFloat;
			camera->UpdatePosition();
		},
		[&]
		{	//15: SET CAMERA TYPE.
			camera->_fov = ReadFloat;
		},
		[&]
		{  //16: SetSubSelectable.
			auto name = ReadString;
			auto selectable = ReadBool;

			SetObjectSubSelectable(name, selectable);
		},
		[&] { //17：Add Line bunch for temporary draw.
			auto name = ReadString;
			auto len = ReadInt;
			// std::cout << "draw spot texts" << len << " on " << name << std::endl;

			ptr = AppendLines2Bunch(name, len, ptr);
		},
		[&] { //18： RemoveObject //todo.
			auto name = ReadString;
			RemoveModelObject(name);
		},
		[&]
		{  //19:  Clear temp lines text.
			auto name = ReadString;
			ClearLineBunch(name);
		},
		[&]
		{  //20: put image
			auto name = ReadString;
			
			auto billboard = ReadBool;
			auto displayH = ReadFloat;
			auto displayW = ReadFloat;
			glm::vec3 new_position;
			new_position.x = ReadFloat;
			new_position.y = ReadFloat;
			new_position.z = ReadFloat;
			glm::quat new_quaternion;
			new_quaternion.x = ReadFloat;
			new_quaternion.y = ReadFloat;
			new_quaternion.z = ReadFloat;
			new_quaternion.w = ReadFloat;
			auto rgbaName = ReadString;
			AddImage(name, billboard, glm::vec2(displayH, displayW), new_position, new_quaternion, rgbaName);
		},
		[&]
		{
			//21: Add line.
			auto name = ReadString;
			auto propstart = ReadString;
			auto propend = ReadString;
			glm::vec3 start;
			start.x = ReadFloat;
			start.y = ReadFloat;
			start.z = ReadFloat;
			glm::vec3 end;
			end.x = ReadFloat;
			end.y = ReadFloat;
			end.z = ReadFloat;
			auto meta = ReadArr(unsigned char, 4);
			glm::uint color = ReadInt;

			auto lineType = ReadInt;
			if (lineType == 0)
			{
				// straight line.
				AddStraightLine(name, {name, propstart, propend, start, end, meta[0], meta[1], meta[2], color});
			}
			if (lineType==1)
			{
				// beziercurve.
				auto additionalControlPnts = ReadInt;
				auto v3ptr = ReadArr(glm::vec3, additionalControlPnts);
			}
		},
		[&]
		{  //22: PutRGBA
			auto name = ReadString;
			auto width = ReadInt;
			auto height = ReadInt;

			PutRGBA(name, width, height);
		},
		[&]
		{  //23: Update RGBA (internal use)
			auto name = ReadString;
			auto len = ReadInt;
			auto rgba = ReadArr(char, len);

			UpdateRGBA(name, len, rgba);
		},
		[&]
		{
			//24: Stream RGBA
			auto name = ReadString;
			// set the target RGBA into streaming mode.
			SetRGBAStreaming(name);
		},
		[&]
		{
			//25: invalidate RGBA(internal use)
			auto name = ReadString;
			InvalidateRGBA(name);
		},
		[&]
		{
			//26: 
		}
	};
	while (true) {
		auto api = ReadInt;
		if (api == -1) break;

		UIFuns[api]();
		//std::cout << "ws api call" << api << std::endl;
		apiN++;
	}
#ifdef __EMSCRIPTEN__
	// if (apiN > 0)
	// 	printf("WS processed %d apis of %dB\n", apiN, (int)(ptr - (unsigned char*)wsqueue));
#endif
	// std::cout << "ws process bytes=" << (int)(ptr - (unsigned char*) wsqueue) << std::endl;
}

// todo: deprecate this.
void GenerateStackFromPanelCommands(unsigned char* buffer, int len)
{
	auto ptr = buffer;
	auto plen = ReadInt;

	for (int i = 0; i < plen;++i)
	{
		auto st_ptr = ptr;
		auto pid = ReadInt;

		auto name = ReadString;
		auto flag = ReadInt;

		// GetPanelProperties Magic.
		ptr += 4 * 9;
		int exceptionSz = *(int*)ptr;
		ptr += exceptionSz + 4;

		if ((flag & 2) == 0) //shutdown.
		{
			// std::cout << "shutdown " << pid << std::endl;
			map.erase(pid);
		}
		else
		{
			// std::cout << "show " << pid << ":"<< name << std::endl;
			
			auto& bytes = map[pid];
			bytes.clear();
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
				// else if (type == 1) //type 1: cache.
				// {
				// 	auto len = ReadInt;
				// 	bytes.reserve(bytes.size() + len);
				// 	auto initLen = ReadInt;
				// 	std::copy(ptr, ptr + initLen, std::back_inserter(bytes));
				// 	for (int k = 0; k < len - initLen; ++k)
				// 		bytes.push_back(0);
				// 	ptr += initLen;
				// }
			}
			for (int j = 0; j < 4; ++j)
				bytes.push_back(j + 1); //01 02 03 04 as terminal
		}
	}

	v_stack.clear();

	// number of panels:
	int mlen = map.size();
	//std::cout << "displaying windows(" << mlen <<"):" << std::endl;
	for (size_t i = 0; i < 4; ++i) {
		v_stack.push_back(((uint8_t*)&mlen)[i]);
	}

	// for each panel:
	for (const auto& entry : map)
	{
		const auto& bytes = entry.second;
		v_stack.insert(v_stack.end(), bytes.begin(), bytes.end());
	}
	cgui_stack = v_stack.data();
	cgui_refreshed = true;
}

struct wndState
{
	int inited = 0;
	bool pendingAction = false;
	uint64_t time_start_interact;
	ImVec2 Pos, Size;
	ImGuiWindow* im_wnd;

	// creation params:
	int minH, minW;
};

std::map<int, wndState> im;

template <typename TType>
struct cacher
{
	bool touched = false;
	TType caching;
};

class cacheBase
{
	inline static std::vector<cacheBase*> all_cache;
public:
	cacheBase()
	{
		all_cache.push_back(this);
	}
	static void untouch()
	{
		for (const auto& dictionary : all_cache) {
			dictionary->untouch();
		}
	}
	static void finish()
	{
		for (const auto& dictionary : all_cache) {
			dictionary->finish();
		}
	}
};

template <typename TType>
class cacheType {
	inline static cacheType* inst = nullptr; // Declaration
	std::map<std::string, cacher<TType>> cache;

public:
	static cacheType* get() {
		if (inst == nullptr) {
			inst = new cacheType(); // Create an instance of cacheType with specific TType
		}
		return inst;
	}

	void untouch()
	{
		for (auto auto_ : cache)
			auto_.second.touched = false;
	}

	TType& get_or_create(std::string key) {
		if (cache.find(key) == cache.end()) {
			cache.emplace(key, cacher<TType>{}); // Default-construct TType
			cache[key].touched = true;
		}
		return cache[key].caching;
	}

	bool exist(std::string key) {
		return cache.find(key) != cache.end();
	}

	void finish()
	{
		cache.erase(std::remove_if(cache.begin(), cache.end(),
			[](const std::pair<std::string, cacher<int>>& entry) {
				return !entry.second.touched;
			}),
			cache.end());
	}
};


// utility structure for realtime plot
struct ScrollingBuffer {
	bool hold = false;

	int MaxSize;
	int Offset;
	ImVector<ImVec2> Data;
	double latestSec;

	ScrollingBuffer(int max_size = 2000) {
		MaxSize = max_size;
		Offset = 0;
		Data.reserve(MaxSize);
	}
	void AddPoint(float x, float y) {
		if (Data.size() < MaxSize)
			Data.push_back(ImVec2(x, y));
		else {
			Data[Offset] = ImVec2(x, y);
			Offset = (Offset + 1) % MaxSize;
		}
	}
	void Erase() {
		if (Data.size() > 0) {
			Data.shrink(0);
			Offset = 0;
		}
	}
};

std::string current_triggering;
std::vector<std::string> split(const std::string &str, char delimiter) {
    std::vector<std::string> tokens;
    std::string token;
    for (char ch : str) {
        if (ch == delimiter) {
            if (!token.empty()) {
                tokens.push_back(token);
                token.clear();
            }
        } else {
            token += ch;
        }
    }
    if (!token.empty()) {
        tokens.push_back(token);
    }
    return tokens;
}
bool parse_chord(std::string key) {
	static std::unordered_map<std::string, int> keyMap = {
	    {"space", GLFW_KEY_SPACE},
	    {"left", GLFW_KEY_LEFT},
	    {"right", GLFW_KEY_RIGHT},
	    {"up", GLFW_KEY_UP},
	    {"down", GLFW_KEY_DOWN},
	    {"backspace", GLFW_KEY_BACKSPACE},
	    {"del", GLFW_KEY_DELETE},
	    {"ins", GLFW_KEY_INSERT},
	    {"enter", GLFW_KEY_ENTER},
	    {"tab", GLFW_KEY_TAB},
	    {"esc", GLFW_KEY_ESCAPE},
	    {"pgup", GLFW_KEY_PAGE_UP},
	    {"pgdn", GLFW_KEY_PAGE_DOWN},
	    {"home", GLFW_KEY_HOME},
	    {"end", GLFW_KEY_END},
	    {"pause", GLFW_KEY_PAUSE},
	    {"f1", GLFW_KEY_F1},
	    {"f2", GLFW_KEY_F2},
	    {"f3", GLFW_KEY_F3},
	    {"f4", GLFW_KEY_F4},
	    {"f5", GLFW_KEY_F5},
	    {"f6", GLFW_KEY_F6},
	    {"f7", GLFW_KEY_F7},
	    {"f8", GLFW_KEY_F8},
	    {"f9", GLFW_KEY_F9},
	    {"f10", GLFW_KEY_F10},
	    {"f11", GLFW_KEY_F11},
	    {"f12", GLFW_KEY_F12},
	};
    std::vector<std::string> parts = split(key, '+');

	bool ctrl = false, alt = false, shift = false;
    int mainkey = -1;
    
    for (const std::string& p : parts) {
        if (p == "ctrl") ctrl = true;
        else if (p == "alt") alt = true;
        else if (p == "shift") shift = true;
        else if (keyMap.find(p) != keyMap.end()) mainkey = keyMap[p];
        else if (p.length() == 1) mainkey = toupper(p[0]);
    }

    bool ctrl_pressed = !ctrl || (ctrl && (glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS || glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_RIGHT_CONTROL) == GLFW_PRESS));
    bool alt_pressed = !alt || (alt && (glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_LEFT_ALT) == GLFW_PRESS || glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_RIGHT_ALT) == GLFW_PRESS));
    bool shift_pressed = !shift || (shift && (glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_LEFT_SHIFT) == GLFW_PRESS || glfwGetKey(glfwGetCurrentContext(), GLFW_KEY_RIGHT_SHIFT) == GLFW_PRESS));
	bool mainkey_pressed = mainkey != -1 && (glfwGetKey(glfwGetCurrentContext(), mainkey) == GLFW_PRESS);

	if (ctrl_pressed && alt_pressed && shift_pressed && mainkey_pressed) //triggered.
	{
		if (current_triggering != key)
		{
			current_triggering = key;
			return true;
		}
		return false;
	}
    if (current_triggering == key)
	    current_triggering = "";
	return false;
}

ImGuiID leftPanels, rightPanels, dockspace_id;
ImGuiDockNode* dockingRoot;

bool init_docking = false;
void SetupDocking()
{
	init_docking = true;
	dockspace_id = ImGui::GetID("CycleGUIMainDock");

	// ImGui::DockBuilderRemoveNode(dockspace_id); // clear any previous layout
	if ((dockingRoot = ImGui::DockBuilderGetNode(dockspace_id)) != NULL) return;
	ImGui::DockBuilderAddNode(dockspace_id, ImGuiDockNodeFlags_DockSpace);// Add empty node
	dockingRoot = ImGui::DockBuilderGetNode(dockspace_id);

	ImGui::DockBuilderFinish(dockspace_id);
	// ImGui::DockBuilderSetNodeSize(dockspace_id, ImVec2(960, (float)640));
	//
	// ImGuiID dock_id_graph_editor = dockspace_id; // This variable tracks the central node
	// leftPanels = ImGui::DockBuilderSplitNode(dock_id_graph_editor, ImGuiDir_Left, 0.333f, NULL, &dock_id_graph_editor);
	// rightPanels = ImGui::DockBuilderSplitNode(dock_id_graph_editor, ImGuiDir_Right, 0.333f, NULL, &dock_id_graph_editor);
}

std::vector<int> no_modal_pids;

void StepWndSz(ImGuiSizeCallbackData* data)
{
	auto step = 64;
	wndState* minSz = (wndState*)data->UserData;
	auto calX = std::max((int)minSz->minW, (int)(data->CurrentSize.x / step + 0.5f) * step);
	auto calY = std::max((int)minSz->minH, (int)data->CurrentSize.y+10);
	data->DesiredSize = ImVec2(calX, calY);
}

#ifdef __EMSCRIPTEN__
extern double g_dpi;
#endif

void ProcessUIStack()
{
	ImGuiStyle& style = ImGui::GetStyle();

	if (!init_docking)
		SetupDocking();

	// create the main dockspace over the entire editor window
	ImGuiViewport* viewport = ImGui::GetMainViewport();
	ImGui::SetNextWindowPos(viewport->Pos);
	ImGui::SetNextWindowSize(viewport->Size);
	ImGui::SetNextWindowViewport(viewport->ID);
	ImGui::SetNextWindowBgAlpha(0.0f);

	ImGuiWindowFlags window_flags = ImGuiWindowFlags_NoDocking;
	window_flags |= ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove;
	window_flags |= ImGuiWindowFlags_NoBringToFrontOnFocus | ImGuiWindowFlags_NoNavFocus;

	ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, 0.0f);
	ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, 0.0f);
	ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0.0f, 0.0f));
	ImGui::Begin("CycleGUI Dockspace", NULL, window_flags);
	ImGui::PopStyleVar(3);

	ImGuiDockNodeFlags dockspace_flags = ImGuiDockNodeFlags_PassthruCentralNode | ImGuiDockNodeFlags_NoDockingInCentralNode;
	ImGui::DockSpace(dockspace_id, ImVec2(0.0f, 0.0f), dockspace_flags);
	ImGui::End();

	// ui_stack
	beforeDraw();

	auto ptr = cgui_stack;
	if (ptr == nullptr) return; // skip if not initialized.

	auto plen = ReadInt;

	unsigned char buffer[1024];
	auto pr = buffer;
	bool stateChanged = false;
	bool wndShown = true;

	cacheBase::untouch();
	
	auto io = ImGui::GetIO();

	// Position
	auto vp = ImGui::GetMainViewport();
	static double sec = 0;
	sec += io.DeltaTime;

	int modalpid = -1;
	for (int i = 0; i < plen; ++i)
	{
		auto pid = ReadInt;
		std::function<void()> UIFuns[] = {
			[&]
			{
				assert(false);
			}, // 0: this is not a valid control(cache command)
			[&] //1: text
			{
				auto str = ReadString;
				ImGui::TextWrapped(str.c_str());
			},
			[&] // 2: button
			{
				auto cid = ReadInt;
				auto str = ReadString;
				auto shortcut = ReadString;
				auto hint = ReadString;

				char buttonLabel[256];
				sprintf(buttonLabel, "%s##btn%d", str.c_str(), cid);
				if (ImGui::Button(buttonLabel) || parse_chord(shortcut)) {
					stateChanged = true;
					WriteInt32(1)
				}

				if (hint.length() > 0 && ImGui::IsItemHovered(ImGuiHoveredFlags_DelayShort))
					ImGui::SetTooltip(hint.c_str());
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
				auto defTxt = ReadString;
				auto inputOnShow = ReadBool;

				//ImGui::PushItemWidth(300);
				char tblbl[256];
				sprintf(tblbl, "##%s-tb-%d", prompt.c_str(), cid);
				using ti = std::tuple<char[256], char[256]>; //get<0>:buffer, get<1>:default.
				auto init = cacheType<ti>::get()->exist(tblbl);
				auto& tiN = cacheType<ti>::get()->get_or_create(tblbl);
				auto& textBuffer = std::get<0>(tiN);
				auto& cacheddef = std::get<1>(tiN);

				if (!init || strcmp(defTxt.c_str(),cacheddef)){
					memcpy(textBuffer, defTxt.c_str(), 256);
					memcpy(cacheddef, defTxt.c_str(), 256);
				}

				// make focus on the text input if possible.
				if (inputOnShow && ImGui::IsWindowAppearing())
					ImGui::SetKeyboardFocusHere();
				ImGui::Text(prompt.c_str());
				ImGui::Indent(style.IndentSpacing / 2);
				ImGui::SetNextItemWidth(-16);

				if (ImGui::InputTextWithHint(tblbl, hint.c_str(), textBuffer, 256, ImGuiInputTextFlags_EnterReturnsTrue)) {
					stateChanged = true;
					// patch.
					auto ocid = cid;
					cid += 1;
					WriteBool(true);
					cid = ocid;
				}

				ImGui::Unindent(style.IndentSpacing / 2);
				//ImGui::PopItemWidth();

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

				if (prompt.size() > 0)
					ImGui::SeparatorText(prompt.c_str());

				float window_visible_x2 = ImGui::GetWindowPos().x + ImGui::GetWindowContentRegionMax().x;
				auto xx = 0;
				for (int n = 0; n < buttons_count; n++)
				{
					auto btn_txt = ReadString;
					char lsbxid[256];
					sprintf(lsbxid, "%s##btng%s_%d", btn_txt.c_str(), prompt.c_str(), n);

					auto sz = ImGui::CalcTextSize(btn_txt.c_str());
					sz.x += style.FramePadding.x * 2;
					sz.y += style.FramePadding.y * 2;
					
					if (n>0 && (xx + style.ItemSpacing.x + sz.x < window_visible_x2 || (flag & 1) != 0))
						ImGui::SameLine();

					if (ImGui::Button(lsbxid))
					{
						stateChanged = true;
						WriteInt32(n)
					}
					xx = ImGui::GetItemRectMax().x;
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
				ImGui::PushItemWidth(200);

				auto& searchTxt = cacheType<char[256]>::get()->get_or_create(searcher);
				ImGui::InputTextWithHint(searcher, "Search", searchTxt, 256);

				ImGui::PopItemWidth();
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
#define TableResponseInt(x) stateChanged=true; char ret[13]; ret[0]=0; *(int*)(ret+1)=row; *(int*)(ret+5)=column; *(int*)(ret+9)=x; WriteBytes(ret, 13);
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
			},
			[&]
			{
				// 9: realtime plot.
				auto prompt = ReadString;
				
				auto value = ReadFloat;

				auto& plotting = cacheType<ScrollingBuffer>::get()->get_or_create(prompt);
				
				ImGui::SeparatorText(prompt.c_str());
				ImGui::SameLine();
				if (ImGui::Checkbox("HOLD", &plotting.hold) && !plotting.hold)
					plotting.Erase();

				if (ImPlot::BeginPlot(prompt.c_str(), ImVec2(-1, 150))) {
					ImPlot::SetupAxes(nullptr, nullptr, 0, 0);
					if (plotting.hold)
					{
						ImPlot::SetupAxisLimits(ImAxis_X1, plotting.latestSec - 10, plotting.latestSec, ImGuiCond_Always);
						ImPlot::SetupAxisLimits(ImAxis_Y1, 0, 1);
						ImPlot::SetNextFillStyle(IMPLOT_AUTO_COL, 0.5f);
						ImPlot::PlotLine(prompt.c_str(), &plotting.Data[0].x, &plotting.Data[0].y, plotting.Data.size(), 0, plotting.Offset, 2 * sizeof(float));
					}
					else {
						plotting.AddPoint(sec, value);

						ImPlot::SetupAxisLimits(ImAxis_X1, sec - 10, sec, ImGuiCond_Always);
						ImPlot::SetupAxisLimits(ImAxis_Y1, 0, 1);
						ImPlot::SetNextFillStyle(IMPLOT_AUTO_COL, 0.5f);
						ImPlot::PlotLine(prompt.c_str(), &plotting.Data[0].x, &plotting.Data[0].y, plotting.Data.size(), 0, plotting.Offset, 2 * sizeof(float));

						plotting.latestSec = sec;
					}
					ImPlot::EndPlot();
				} 
			},
			[&]
			{
				// 10: dragfloat.
				auto cid = ReadInt;
				auto prompt = ReadString;

				float* val = (float*)ptr; ptr += 4;
				auto step = ReadFloat;
				auto min_v = ReadFloat;
				auto max_v = ReadFloat;

				if (ImGui::DragFloat(prompt.c_str(), val, step, min_v, max_v))
				{
					stateChanged = true;
					WriteFloat(*val);
				}
			},
			[&]
			{
				// 11: seperator text.
				auto str = ReadString;
				ImGui::SeparatorText(str.c_str());
			},
			[&]
			{
				// 12: display file link
				auto displayname = ReadString;
				auto filehash = ReadString;
				auto fname = ReadString;
				char lsbxid[256];
				auto& displayed = cacheType<long long>::get()->get_or_create(filehash);
				sprintf(lsbxid, "\uf0c1 %s", displayname.c_str());
				auto enabled = displayed < ui_state.getMsFromStart() + 1000;
				if (!enabled) ImGui::BeginDisabled(true);
				
				ImGui::PushStyleColor(ImGuiCol_Button, IM_COL32(120, 80, 0, 255));
				if (ImGui::Button(lsbxid))
				{
					ExternDisplay(filehash.c_str(), pid, fname.c_str());
				}
				ImGui::PopStyleColor();
				if (!enabled) ImGui::EndDisabled();
			}
		};
		auto str = ReadString;
		auto& mystate = cacheType<wndState>::get()->get_or_create(str.c_str());

#ifdef __EMSCRIPTEN__
		auto dpiScale = g_dpi;
#else
		auto dpiScale = mystate.inited < 1 ? vp->DpiScale : mystate.im_wnd->Viewport->DpiScale;
#endif
		//std::cout << "draw " << pid << " " << str << ":"<<i<<"/"<<plen << std::endl;
		// char windowLabel[256];
		// sprintf(windowLabel, "%s##pid%d", str.c_str(), pid);

		ImGuiWindowFlags window_flags = 0;
		auto flags = ReadInt;

		// consider * scale to overcome highdpi effects.
		auto relPanel = ReadInt;
		auto relPivotX = ReadFloat;
		auto relPivotY = ReadFloat;
		auto myPivotX = ReadFloat;
		auto myPivotY = ReadFloat;
		auto panelWidth = ReadInt;
		auto panelHeight = ReadInt;
		auto panelLeft = ReadInt;
		auto panelTop = ReadInt;

		panelWidth *= dpiScale;
		panelHeight *= dpiScale;
		panelLeft *= dpiScale;
		panelTop *= dpiScale;


		auto except = ReadString;
		// Size:
		auto pivot = ImVec2(myPivotX, myPivotY);
		if ((flags & 8) !=0)
		{
			// not resizable
			if ((flags & (16)) != 0) { // autoResized
				window_flags |= ImGuiWindowFlags_AlwaysAutoResize;
				mystate.minH = panelHeight;
				mystate.minW = panelWidth;
				ImGui::SetNextWindowSizeConstraints(ImVec2(0, 0), ImVec2(FLT_MAX, FLT_MAX), StepWndSz, &mystate);
			}
			else { // must have initial w/h
				window_flags |= ImGuiWindowFlags_NoResize;
				ImGui::SetNextWindowSize(ImVec2(panelWidth, panelHeight));
			}
		}else
		{
			// initial w/h, maybe read from ini.
			ImGui::SetNextWindowSize(ImVec2(panelWidth, panelHeight), ImGuiCond_FirstUseEver);
		}


		auto fixed = (flags & 32) == 0;
		auto modal = (flags & 128) != 0;
		if (except.length() > 0) modal = false; //no modal for erronous window.

		auto initdocking = (flags >> 9) & 0b111;
		auto dockSplit = (flags & (1 << 12)) != 0;

		auto pos_cond = ImGuiCond_Appearing;
		if (fixed) {
			window_flags |= ImGuiWindowFlags_NoMove;
			pos_cond = ImGuiCond_Always;
		}
		ImVec2 applyPos, applyPivot = pivot;
		if (relPanel < 0 || im.find(relPanel) == im.end()) {
			applyPos = ImVec2(panelLeft + vp->Pos.x + vp->Size.x * relPivotX, panelTop + vp->Pos.y + vp->Size.y * relPivotY);
		}
		else
		{
			auto& wnd = im[relPanel];
			applyPos = ImVec2(panelLeft + wnd.Pos.x + wnd.Size.x * relPivotX, panelTop + wnd.Pos.y + wnd.Size.y * relPivotY);
		}

		if (modal)
		{
			// window_flags |= ImGuiWindowFlags_NoCollapse;
			// window_flags |= ImGuiDockNodeFlags_NoCloseButton;
			// window_flags |= ImGuiWindowFlags_NoDocking;
			// window_flags |= ImGuiWindowFlags_Modal;
			// sometimes needed, sometimes no.
			//if (relPanel < 0)
			//	applyPos = ImVec2(io.MousePos.x, io.MousePos.y);
			applyPos = ImVec2(vp->Pos.x + vp->Size.x / 2, vp->Pos.y + vp->Size.y / 2);
			applyPivot = ImVec2(0.5, 0.5);
		}

		if (!fixed && !modal && ((initdocking & 4) != 0))
		{
			if (mystate.inited < 1) {
				ImGuiAxis requiredAxis = (initdocking & 1) == 0 ? ImGuiAxis_X : ImGuiAxis_Y;
				int sgn = (initdocking & 2) == 0 ? 1 : -1;

				auto name = str.c_str();
				ImGuiID window_id = ImHashStr(name);
				ImGuiWindowSettings* settings = ImGui::FindWindowSettingsByID(window_id);
				if (settings == NULL) {
					ImGuiDockNode* root = ImGui::DockBuilderGetNode(dockspace_id);
					auto central = ImGui::DockNodeGetRootNode(root)->CentralNode;
					auto snode = central;
					auto pnode = central->ParentNode;
					auto docked = false;
					// traverse up central to see if a node has splitted with required direction.
					while (pnode != nullptr)
					{
						if (pnode->SplitAxis == requiredAxis)
						{
							auto othernode = pnode->ChildNodes[0] == snode ? pnode->ChildNodes[1] : pnode->ChildNodes[0];
							if (requiredAxis == ImGuiAxis_X && ImSign(othernode->Pos.x - snode->Pos.x) * sgn > 0 ||
								requiredAxis == ImGuiAxis_Y && ImSign(othernode->Pos.y - snode->Pos.y) * sgn > 0)
							{
								ImGuiID oid;
								if (!dockSplit) {
									// todo: balance windows count for descendant nodes.
									while (!othernode->IsLeafNode())
										othernode = othernode->ChildNodes[0];
									oid = othernode->ID;

								}else
								{
									auto ppos = othernode->Pos;
									while (!othernode->IsLeafNode())
									{
										if (othernode->ChildNodes[0]->Pos.x!=ppos.x || othernode->ChildNodes[0]->Pos.y!=ppos.y)
											othernode = othernode->ChildNodes[0];
										else othernode = othernode->ChildNodes[1];
									}
									if (othernode->Pos.x==ppos.x && othernode->Pos.y==ppos.y)
									{
										ImGuiDir dir;
										if (requiredAxis == ImGuiAxis_X)
											dir = ImGuiDir_Down;
										if (requiredAxis == ImGuiAxis_Y)
											dir = ImGuiDir_Right;
										oid = ImGui::DockBuilderSplitNode(othernode->ID, dir, 0.5f, NULL, NULL);
									}
									else
										oid = othernode->ID;
								}

								ImGui::DockBuilderDockWindow(name, oid);
								docked = true;
								break;
							}
						}
						snode = pnode;
						pnode = pnode->ParentNode;
					}


					if (!docked) {
						ImGuiDir dir;
						if (requiredAxis == ImGuiAxis_X && sgn == 1)
							dir = ImGuiDir_Right;
						if (requiredAxis == ImGuiAxis_X && sgn == -1)
							dir = ImGuiDir_Left;
						if (requiredAxis == ImGuiAxis_Y && sgn == 1)
							dir = ImGuiDir_Up;
						if (requiredAxis == ImGuiAxis_Y && sgn == -1)
							dir = ImGuiDir_Down;

						// ImGuiContext& g = *GImGui;
						// ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
						// auto pp = ImGui::DockContextFindNodeByID(&g, central->ID);
						// std::cout << "central id=" << central->ID <<", dlen="<< g.DockContext.Nodes.Data.Size<< ", ptr=" << (int)pp << std::endl;
						// std::cout << "node sz=" << node->Size.x<<"," << node->Size.y << ", pos=" << node->Pos.x<<"," << node->Pos.x << std::endl;

						ImGui::DockBuilderDockWindow(name, ImGui::DockBuilderSplitNode(central->ID, dir, 0.33333f, NULL, NULL));
					}
				}
			}
		}
		else {
			ImGui::SetNextWindowPos(applyPos, pos_cond, applyPivot);
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

		if (modal) {
			if (modalpid != -1)
				no_modal_pids.push_back(modalpid);
			if (std::find(no_modal_pids.begin(),no_modal_pids.end(),pid)== no_modal_pids.end() && modalpid==-1){
				ImGui::OpenPopup(str.c_str());
				ImGui::BeginPopupModal(str.c_str(), p_show, window_flags); // later popup modal should override previous popup modals.
				modalpid = pid;
			}else
				ImGui::Begin(str.c_str(), p_show, window_flags);
		}
		else
			ImGui::Begin(str.c_str(), p_show, window_flags);

		//ImGui::PushItemWidth(ImGui::GetFontSize() * -6);
		if (mystate.pendingAction && cgui_refreshed)
			mystate.pendingAction = false;
		auto should_block = flags & 1 || mystate.pendingAction || (except.length() > 0) ;
		if (should_block) // freeze.
		{
			ImGui::BeginDisabled(true);
		}
		bool beforeLayoutStateChanged = stateChanged;
		while (true)
		{
			auto ctype = ReadInt;
			if (ctype == 0x04030201) break;
			UIFuns[ctype]();
		}
		if (should_block) // freeze.
		{
			ImGui::EndDisabled();
		}

		if (except.length()>0)
		{
			// show message and retry button.
			GImGui->CurrentWindow->DC.CursorPos.x = GImGui->CurrentWindow->Pos.x + 16;
			GImGui->CurrentWindow->DC.CursorPos.y = GImGui->CurrentWindow->Pos.y + 48;
            ImGui::PushStyleColor(ImGuiCol_ChildBg, IM_COL32(128, 0, 0, 200));
            if (ImGui::BeginChild("ResizableChild", ImVec2(-FLT_MIN, -FLT_MIN), true, ImGuiTableFlags_NoSavedSettings)){
				ImGui::Text("UI operation throws error：");
				ImGui::Separator();
				if (ImGui::Button("\uf0c5 Copy Error Message"))
					ImGui::SetClipboardText(except.c_str());
				ImGui::TextWrapped(except.c_str());
	            // ImGui::InputTextMultiline("##source", (char*)except.c_str(), except.length(), ImVec2(-FLT_MIN, ImGui::GetTextLineHeight() * 16), ImGuiInputTextFlags_ReadOnly);
				ImGui::SeparatorText("Actions");
				
				if (mystate.pendingAction)
					ImGui::BeginDisabled(true);
				if (ImGui::Button("Retry"))
				{
					//cid:-2 type int 0.
					*(int*)pr=pid; pr+=4; *(int*)pr=-2; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=0; pr+=4;
					stateChanged = true;
				}
				ImGui::SameLine();
            ImGui::PushStyleColor(ImGuiCol_Button, IM_COL32(255, 0, 0, 255));
				if (ImGui::Button("Shutdown"))
				{
					// close this cid.
					//cid:-2 type int 1.
					*(int*)pr=pid; pr+=4; *(int*)pr=-2; pr+=4; *(int*)pr=1; pr+=4; *(int*)pr=1; pr+=4;
					stateChanged = true;
				}
            ImGui::PopStyleColor();
				if (mystate.pendingAction)
					ImGui::BeginDisabled(false);
			}
            ImGui::PopStyleColor();
            ImGui::EndChild();
		}
		
		if (!beforeLayoutStateChanged && stateChanged)
		{
			mystate.pendingAction = true;
			mystate.time_start_interact = ui_state.getMsFromStart();
		}

		mystate.inited += 1;
		mystate.Pos = ImGui::GetWindowPos();
		mystate.Size = ImGui::GetWindowSize();
		mystate.im_wnd = ImGui::GetCurrentWindow();

		if (mystate.pendingAction && mystate.time_start_interact+1000<ui_state.getMsFromStart())
		{
			ImGuiWindow* window = mystate.im_wnd;
	        // Render
			auto radius = 20;
			ImVec2 pos(mystate.Pos.x+mystate.Size.x/2,mystate.Pos.y+mystate.Size.y/2);
			
			// Render
	        window->DrawList->PathClear();
	        
	        int num_segments = 30;
			int time = ui_state.getMsFromStart();
			int start = abs(ImSin(GImGui->Time)*(num_segments-9));
	        
	        const float a_min = IM_PI*2.0f * ((float)start) / (float)num_segments;
	        const float a_max = IM_PI*2.0f * ((float)num_segments-3) / (float)num_segments;

	        const ImVec2 centre = ImVec2(pos.x, pos.y);
	        
	        for (int i = 0; i < num_segments; i++) {
	            const float a = a_min + ((float)i / (float)num_segments) * (a_max - a_min);
	            window->DrawList->PathLineTo(ImVec2(centre.x + ImCos(a+GImGui->Time*8) * radius,
	                                                centre.y + ImSin(a+GImGui->Time*8) * radius));
	        }

	        window->DrawList->PathStroke((ImU32)0xffffffff, false, 5);
		}
		im.insert_or_assign(pid, mystate);

		if (modalpid == pid)
			ImGui::EndPopup();
		else
			ImGui::End();
		//ImGui::PopStyleVar(1);
	}

	cacheBase::finish();
	cgui_refreshed = false;

	if (modalpid == -1 && no_modal_pids.size() > 0)
		no_modal_pids.pop_back();

	if (stateChanged)
		stateCallback(buffer, pr - buffer);

}

ui_state_t ui_state;
bool initialize()
{
	ui_state.workspace_state.push(workspace_state_desc{.id=0, .name="default"});
	ui_state.started_time = std::chrono::high_resolution_clock::now();
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
			//if (wstate.action_state == 1) break; //submitting...

			// todo: if anything else should do
			if (wstate.function == selectObj) {
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
			}
			// process move/rotate in main.cpp after guizmo.
			break;
		case GLFW_MOUSE_BUTTON_MIDDLE:
			ui_state.mouseMiddle = true;
			ui_state.refreshStare = true;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			ui_state.mouseRight = true;
			if (ui_state.selecting)
				ui_state.selecting = false;
			// todo: cancel...
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
			ui_state.lastClickedMs = ui_state.getMsFromStart();
			ui_state.clickedMouse = 1;
			break;
		case GLFW_MOUSE_BUTTON_RIGHT:
			// if (wstate.right_click_select && ui_state.selecting && )
			// {
			// 	
			// }
			ui_state.mouseRight = false;
			break;
		}
	}
}


uint64_t ui_state_t::getMsFromStart() {
	return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::high_resolution_clock::now() - started_time).count();
}

void cursor_position_callback(GLFWwindow* window, double rx, double ry)
{
	if (ImGui::GetIO().WantCaptureMouse)
		return;

	if (!dockingRoot) return;
	auto vp = ImGui::GetMainViewport();
	auto central = ImGui::DockNodeGetRootNode(dockingRoot)->CentralNode;
	auto xpos = rx - central->Pos.x + vp->Pos.x;
	auto ypos = ry - central->Pos.y + vp->Pos.y;

	double deltaX = xpos - ui_state.mouseX;
	double deltaY = ypos - ui_state.mouseY;

	//ImGuiDockNode* node = ImGui::DockBuilderGetNode(ImGui::GetID("CycleGUIMainDock"));
	ui_state.mouseX = xpos;// - central->Pos.x + vp->Pos.x;
	ui_state.mouseY = ypos;


	auto& wstate = ui_state.workspace_state.top();

	if (ui_state.mouseLeft)
	{
		// Handle left mouse button dragging (in process ui stack)
	}
	else if (ui_state.mouseMiddle && ui_state.mouseRight)
	{
		camera->Rotate(deltaY * 1.5f, -deltaX);
		
	}
	else if (ui_state.mouseMiddle)
	{
		// Handle middle mouse button dragging
		camera->RotateAzimuth(-deltaX);
		camera->RotateAltitude(deltaY * 1.5f);
	}
	else if (ui_state.mouseRight)
	{
		// Handle right mouse button dragging
		if (ui_state.selecting)
		{
			ui_state.selecting = false; // cancel selecting.
		}
		else {
			//todo: if pitch exceed certain value, pan on camera coordination.
			auto d = camera->distance * 0.0016f;
			camera->PanLeftRight(-deltaX * d);
			if (abs(camera->Altitude)<M_PI_4)
			{
				auto s = sin(camera->Altitude);
				auto c = cos(camera->Altitude);
				auto fac = 1-s /0.7071;
				camera->ElevateUpDown(deltaY * d * fac);
				camera->PanBackForth(deltaY * d * (1 - fac) - (deltaY * d * fac * s / c));

			}else{
				camera->PanBackForth(deltaY * d);
			}
		}
	}
}

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset)
{
	if (ImGui::GetIO().WantCaptureMouse || ui_state.selecting)
		return;

	// Handle mouse scroll
	if (ui_state.mouseMiddle)
	{
		// move vertically.
		camera->ElevateUpDown(yoffset * 0.1f);
	}
	else {
		// zoom
		camera->Zoom(-yoffset * 0.1f);
	}
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
	if (key == GLFW_KEY_ESCAPE)
	{
		if (action == GLFW_PRESS)
		{
			// if not submitted, reset.
			_clear_action_state();
		}
	}
}



void _clear_action_state()
{
	ui_state.selecting = false;
	auto& wstate = ui_state.workspace_state.top();
	//wstate.action_state = 0;
}
