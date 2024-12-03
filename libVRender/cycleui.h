#pragma once

#include <chrono>
#include <functional>
#include <map>
#include <set>
#include <stack>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <GLFW/glfw3.h>
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>

#include "imgui.h"
#include "messyengine.h"

// =============================== INTERFACE ==============================
extern unsigned char* cgui_stack;           // persisting for ui content.
extern bool cgui_refreshed;
extern char* appName, *appStat;

typedef void(*RealtimeUIFunc)(unsigned char* news, int length); //realtime means operation not queued.
typedef void(*NotifyWorkspaceChangedFunc)(unsigned char* news, int length); //realtime means operation not queued.
typedef void(*NotifyStateChangedFunc)(unsigned char* changedStates, int length);
extern NotifyStateChangedFunc stateCallback;
extern NotifyWorkspaceChangedFunc workspaceCallback;
extern RealtimeUIFunc realtimeUICallback;
extern void ExternDisplay(const char* filehash, int pid, const char* fname);
extern uint8_t* GetStreamingBuffer(std::string name, int length);

typedef void(*BeforeDrawFunc)();
extern BeforeDrawFunc beforeDraw;



void GenerateStackFromPanelCommands(unsigned char* buffer, int len);
void ProcessUIStack();
void ProcessWorkspaceQueue(void* ptr); // maps to implementation details. this always calls on main thread so is synchronized.

// =============================== Implementation details ==============================

struct me_obj;

// don't use smart_pointer because we could have pending "wild" object, so we use tref a dedicate reference class for me_obj.
template <typename T> struct tref
{
    T* obj;
};

struct namemap_t
{
	int type; // same as selection.
	int instance_id;
	me_obj* obj;
};
template <typename T> struct indexier;
extern indexier<namemap_t> global_name_map;

struct self_idref_t
{
	int instance_id;
};

// note: typeId cases:
// 1: pointcloud
// 2: line bunch/line piece
// 3: sprite
// 4: spot_texts
// 5: geometry;
// >=1000: 1000+k, k is class_id.

template <typename T>
struct indexier
{
	std::unordered_map<std::string, int> name_map;
	std::vector<std::tuple<T*, std::string>> ls;

	int add(std::string name, T* what)
	{
		auto it = name_map.find(name);
		if (it != name_map.end()) {
			throw "Already in indexier";
		}
		name_map[name] = ls.size();
		ls.push_back(std::tuple<T*, std::string>(what, name));
		auto iid = ls.size() - 1;

		if constexpr (!std::is_same_v<T, namemap_t> && std::is_base_of_v<me_obj, T>) {
			auto nt = new namemap_t();
			nt->instance_id = iid;
			nt->type = T::type_id;
			nt->obj = (me_obj*)what;
			what->name = name;
			global_name_map.add(name, nt);
		}
		if constexpr (std::is_base_of_v<self_idref_t,T>)
			((self_idref_t*)what)->instance_id = iid;
		return iid;
	}

	// this method doesn't free memory. noted.
	void remove(std::string name)
	{
		auto it = name_map.find(name);
		if (it != name_map.end()) {
			delete std::get<0>(ls[it->second]);
			if (ls.size() > 1) {
				// move last element to current pos.
				auto tup = ls[ls.size() - 1];
				ls[it->second] = tup;
				name_map[std::get<1>(tup)] = it->second;

				if constexpr (!std::is_same_v<T, namemap_t>) {
					global_name_map.get(std::get<1>(tup))->instance_id = it->second;
				}
				if constexpr (std::is_base_of_v<self_idref_t, T>)
					((self_idref_t*)std::get<0>(tup))->instance_id = it->second;
			}
			ls.pop_back();
		}
		name_map.erase(name);

		if constexpr (!std::is_same_v<T, namemap_t> && std::is_base_of_v<me_obj, T>) {
			global_name_map.remove(name);
		}
	}

	T* get(std::string name)
	{
		auto it = name_map.find(name);
		if (it != name_map.end())
			return std::get<0>(ls[it->second]);
		return nullptr;
	}

	int getid(std::string name)
	{
		auto it = name_map.find(name);
		if (it != name_map.end())
			return it->second;
		return -1;
	}

	std::string& getName(int id)
	{
		return std::get<1>(ls[id]);
	}

	T* get(int id)
	{
		return std::get<0>(ls[id]);
	}
};

struct abstract_operation
{
    bool working = false;
    virtual std::string Type() = 0;
    virtual void pointer_down() = 0;
    virtual void pointer_move() = 0;
    virtual void pointer_up() = 0;
    
    virtual void canceled() = 0;

    // virtual void restore() = 0;

    virtual void feedback(unsigned char*& pr) = 0;
};

enum feedback_mode
{
    pending, operation_canceled, feedback_finished, feedback_continued, realtime_event
};

struct workspace_state_desc
{
    int id;
    std::string name;

    // todo: change these into tref<me_obj>
    std::unordered_set<std::string> hoverables, sub_hoverables, bringtofronts;
     
    std::vector<tref<me_obj>> hidden_objects, use_cross_section;

    // display parameters.
    bool useEDL = true, useSSAO = true, useGround = true, useBorder = true, useBloom = true, drawGrid = true, drawGuizmo = true;
    glm::vec4 hover_shine = glm::vec4(0.6, 0.6, 0, 0.6), selected_shine = glm::vec4(1, 0, 0, 1);
    glm::vec4 hover_border_color = glm::vec4(1, 1, 0, 1), selected_border_color = glm::vec4(1, 0, 0, 1), world_border_color = glm::vec4(1, 1, 1, 1);
    bool btf_on_hovering = true; //brint to front on hovering.
    bool useCrossSection = false; glm::vec3 crossSectionPlanePos, clippingDirection;//no appear on clipping direction

    abstract_operation* operation;
    feedback_mode feedback;
};

struct no_operation : abstract_operation
{
    std::string Type() override { return "no operation"; }

    void pointer_down() override {};
    void pointer_move() override {};
    void pointer_up() override {};
    void canceled() override {};

    void feedback(unsigned char*& pr) override { };
};

struct widget_definition
{
    std::string widget_name, display_text;
    std::vector<std::string> keyboard_mapping;
    std::vector<std::string> joystick_mapping;

    std::vector<bool> keyboard_press;
    std::vector<float> joystick_value; // if joystick is not available, value is NaN.

    int id = 0;
    int kj_handle_loop = -1; //if 1 frames no kj handle, use touch screen handler.
    int pointer = -1; //

    // at least one key stroke or axis!=0.
    bool isKJHandling();
    virtual std::string WidgetType() = 0;
    virtual void process(ImGuiDockNode* disp_area, ImDrawList* dl) = 0;
    virtual void feedback(unsigned char*& pr) = 0;
    
    bool previouslyKJHandled;
    void process_keyboardjoystick();
    virtual void keyboardjoystick_map() = 0;
};

struct toggle_widget:widget_definition
{
    glm::vec2 center_uv, center_px,sz_uv,sz_px;
    bool on;

	void feedback(unsigned char*& pr) override;
	std::string WidgetType() override { return "toggle"; }
    void process(ImGuiDockNode* disp_area, ImDrawList* dl) override;

    int lastPressCnt;
    void keyboardjoystick_map() override;
};

struct button_widget:widget_definition
{
    // todo: flag "onlyOnClick".
    glm::vec2 center_uv, center_px,sz_uv,sz_px;
    bool pressed;

	void feedback(unsigned char*& pr) override;
	std::string WidgetType() override { return "just touch"; }
    void process(ImGuiDockNode* disp_area, ImDrawList* dl) override;
    void keyboardjoystick_map() override;
};

struct throttle_widget:widget_definition
{
    std::string WidgetType() override { return "throttle"; }
    void init();
    glm::vec2 center_uv, center_px,sz_uv,sz_px;

    bool bounceBack;
    bool onlyHandle, dualWay, vertical;
    std::string shortcutX, shortcutY;

    float init_pos;
    float current_pos; //=> -1~1.

    float value() { return dualWay ? current_pos : (current_pos + 1) / 2; };
    void process(ImGuiDockNode* disp_area, ImDrawList* dl) override;
	void feedback(unsigned char*& pr) override;
    void keyboardjoystick_map() override;
};

struct stick_widget:widget_definition
{
    std::string WidgetType() override { return "stick"; }
    void init();
    glm::vec2 center_uv, center_px,sz_uv,sz_px;

    bool bounceBack;
    bool onlyHandle;
    std::string shortcutX, shortcutY;

    glm::vec2 init_pos;
    glm::vec2 current_pos; //=> -1~1.

    void process(ImGuiDockNode* disp_area, ImDrawList* dl) override;
	void feedback(unsigned char*& pr) override;

    void keyboardjoystick_map() override;
};

struct gesture_operation : abstract_operation
{
    indexier<widget_definition> widgets;
    std::string Type() override { return "gesture"; }
    ~gesture_operation();

    void pointer_down() override;
    void pointer_move() override;
    void pointer_up() override;
    void canceled() override {}
    void feedback(unsigned char*& pr) override;
    void manipulate(ImGuiDockNode* disp_area, ImDrawList* dl);
};

enum selecting_modes
{
	click, drag, paint
};

struct select_operation : abstract_operation
{
    bool selecting, ctrl, extract_selection;
    float select_start_x, select_start_y; // drag
    float clickingX, clickingY;
    selecting_modes selecting_mode = click;
    std::vector<unsigned char> painter_data; 
    float paint_selecting_radius = 10;

    std::string Type() override { return "select"; }

    void pointer_down() override;;
    void pointer_move() override;
    void pointer_up() override;
    void canceled() override { selecting = false; };
    
    void feedback(unsigned char*& pr) override;
};


enum guizmo_modes
{
    gizmo_moveXYZ, gizmo_rotateXYZ
};

struct guizmo_operation : abstract_operation
{
    guizmo_modes mode;
    glm::vec3 gizmoCenter, originalCenter;
    glm::quat gizmoQuat;
    bool realtime = false;

	struct obj_action_state_t{
		me_obj* obj;
		glm::mat4 intermediate;
	};
	std::vector<obj_action_state_t> obj_action_state;

    std::string Type() override { return "guizmo"; }

    void pointer_down() override {};
    void pointer_move() override {};
    void pointer_up() override {};
    void canceled() override {};

    void selected_get_center();
    void manipulate(ImGuiDockNode* disp_area, glm::mat4 vm, glm::mat4 pm, int h, int w, ImGuiViewport* viewport);
    void feedback(unsigned char*& pr) override;
};


struct selected
{
    int type, instance_id;
    bool sub_selected;
};

typedef std::chrono::time_point<std::chrono::high_resolution_clock> mytime;
struct touch_state
{
    int id;
    float touchX, touchY;
    bool starting = false;
    bool consumed = false;
};

struct ui_state_t
{
	mytime started_time;
	uint64_t getMsFromStart();
    int frameCnt = 0, loopCnt = 0;

	struct{
        int width;
		int height;
        int offsetY;
        int advanceX;
        uint8_t rgba[256 * 256 * 4]; // in case too big.
    }app_icon;

    // ********* DISPLAY STATS ******
    int workspace_w, workspace_h;

    //******* POINTER **********
    float mouseX, mouseY; // mouseXYS: mouse pointing pos project to the ground plane.
    bool mouseLeft, mouseMiddle, mouseRight;
    int mouseLeftDownFrameCnt;

    std::set<int> prevTouches;
	std::vector<touch_state> touches;

    // to uniform. type:1 pc, 1000+gltf class XXX
    int hover_type, hover_instance_id, hover_node_id;
    
    std::stack<workspace_state_desc> workspace_state;
    void pop_workspace_state();

    // ****** MODIFIER *********
    bool ctrl;

    // other utilities.
    bool refreshStare = false;

#ifdef _DEBUG
    bool displayRenderDebug = true;
#else
    bool displayRenderDebug = false;
#endif

};
extern ui_state_t ui_state;


void AllowWorkspaceData();

// ***************************************************************************
// ME object manipulations:
void RemoveObject(std::string name);
void RemoveNamePattern(std::string name);
void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time, uint8_t type, uint8_t coord);

// Workspace temporary apply:
void SetShowHide(std::string name, bool show); 
void SetApplyCrossSection(std::string name, bool show);

// *************************************** Object Types **********************
// pointcloud, gltf, line, line-extrude, sprite. future expands: road, wall(door), floor, geometry

void RouteTypes(namemap_t* type, std::function<void()> point_cloud,
    std::function<void(int)> gltf, 
    std::function<void()> line_bunch, 
    std::function<void()>sprites,
    std::function<void()>spot_texts,
    std::function<void()> not_used_now);

// ------- Point Cloud -----------
struct point_cloud
{
    bool isVolatile;
    int capacity; // if volatile use this.
    // initial:
    int initN;
    glm::vec4* x_y_z_Sz; //initN size
    uint32_t* color;     //initN size.
    // maximum 12.5M selector (8bit)
    glm::vec3 position = glm::zero<glm::vec3>();
    glm::quat quaternion = glm::identity<glm::quat>();
    std::string handleStr;
};
void AddPointCloud(std::string name, const point_cloud& what);
void AppendVolatilePoints(std::string name, int length, glm::vec4* xyzSz, uint32_t* color);
void ClearVolatilePoints(std::string name);

unsigned char* AppendSpotTexts(std::string name, int length, void* pointer);
void ClearSpotTexts(std::string name);

void SetPointCloudBehaviour(std::string name, bool showHandle, bool selectByHandle, bool selectByPoints);




// -------- LINE ----------------
unsigned char* AppendLines2Bunch(std::string name, int length, void* pointer);
void ClearLineBunch(std::string name);

struct line_info
{
    std::string name, propStart, propEnd;
    glm::vec3 start, end;
    unsigned char arrowType, dash, width;
    unsigned int color;
};
void AddStraightLine(std::string name, const line_info& what);

// void AddWidgetImage(std::string name, glm::vec2 wh, glm::vec2 pos, glm::vec2 wh_px, glm::vec2 pos_px, float deg, std::string rgbaName);
// -------- IMAGE ---------------
void AddImage(std::string name, int flag, glm::vec2 disp, glm::vec3 pos, glm::quat quat, std::string rgbaName);
void PutRGBA(std::string name, int width, int height);
void InvalidateRGBA(std::string name);
void UpdateRGBA(std::string name, int len, char* rgba);
void SetRGBAStreaming(std::string name);

// object manipulation:
struct ModelDetail
{
    glm::vec3 center = glm::vec3(0);
    glm::quat rotate = glm::identity<glm::quat>();
    float scale = 1;
};
void LoadModel(std::string cls_name, unsigned char* bytes, int length, ModelDetail detail);

struct custom_mesh_data {
    int nvtx;
	glm::vec3* positions;  // xyz per vertex
    unsigned int color;
    bool smooth; //control normal.
};

void PutModelObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion);
void DefineMesh(std::string cls_name, custom_mesh_data& mesh_data);



// *************************** Object Manipulation ***********************************
// animation
void SetObjectBaseAnimation(std::string name, std::string state);
void PlayObjectEmote(std::string name, std::string emote);
void SetObjectWeights(std::string name, std::string state);

// object behaviour


// shine color + intensity. for each object can set a shine color, and at most 7 shines for subobject
// if any channel: shine color*intensity > 0.5, bloom.
void SetObjectShine(std::string name, uint32_t color);
void CancelObjectShine(std::string name);

void SetObjectBorder(std::string name);
void CancelObjectBorder(std::string name);

void SetSubObjectBorderShine(std::string name, int subid, bool border, uint32_t color);

// workspace stack.

void _clear_action_state();

template <typename workspaceType> void BeginWorkspace(int id, std::string state_name);

std::string GetWorkspaceName();

void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius = 0); //"none", "click", "drag", "drag+click", "painter(r=123)"
//void SetWorkspaceNextAction(action_type type);

// ui related
void SetObjectSelectable(std::string name, bool selectable = true);
void SetObjectSubSelectable(std::string name, bool subselectable);
void CancelObjectSelectable(std::string name);
void CancelObjectSubSelectable(std::string name);

void SetObjectSelected(std::string name);
void ClearSelection();

void BringObjectFront(std::string name);
void BringSubObjectFront(std::string name, int subid);
void CancelBringObjectFront(std::string name);
void CancelBringSubObjectFront(std::string name, int subid);

void SetObjectBillboard(std::string name, std::vector<unsigned char> ui_stack);
void SetSubObjectBillboard(std::string name, int subid, std::vector<unsigned char> ui_stack);


// cycle ui internal usage.
void InitGL(int w, int h);
void DrawWorkspace(int w, int h);
void ProcessBackgroundWorkspace();


// callbacks.
void mouse_button_callback(GLFWwindow* window, int button, int action, int mods);

void cursor_position_callback(GLFWwindow* window, double xpos, double ypos);

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset);

void key_callback(GLFWwindow* window, int key, int scancode, int action, int mods);
void touch_callback(std::vector<touch_state> touches);


// fucked..
struct me_obj
{
    std::string name;
    bool show = true;
    //todo: add border shine etc?

    std::vector<tref<me_obj>*> references;

    // animation easing:
    glm::vec3 target_position = glm::zero<glm::vec3>();
    glm::quat target_rotation = glm::identity<glm::quat>();

    glm::vec3 previous_position = glm::zero<glm::vec3>();
    glm::quat previous_rotation = glm::identity<glm::quat>();
    float target_start_time, target_require_completion_time;

    glm::vec3 current_pos;
    glm::quat current_rot;

    std::tuple<glm::vec3, glm::quat> compute_pose();

    ~me_obj() {
        // Notify all references that this object is being deleted
        for (auto ref : references) {
            ref->obj = nullptr;
        }
    }
};

