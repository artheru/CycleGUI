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

#include "imgui.h"
#include "messyengine.h"

#ifdef _DEBUG
#define DBG printf
#else
#define DBG(...) ;
#endif

// =============================== INTERFACE ==============================
extern unsigned char* cgui_stack;           // persisting for ui content.
extern bool cgui_refreshed;
extern char* appName, *appStat;

typedef void(*RealtimeUIFunc)(unsigned char* news, int length); //realtime means operation not queued.
typedef void(*NotifyWorkspaceChangedFunc)(unsigned char* news, int length); //realtime means operation not queued.
typedef void(*NotifyStateChangedFunc)(unsigned char* changedStates, int length);
extern NotifyStateChangedFunc stateCallback;
extern NotifyWorkspaceChangedFunc global_workspaceCallback;
extern RealtimeUIFunc realtimeUICallback;
extern void ExternDisplay(const char* filehash, int pid, const char* fname);
extern uint8_t* GetStreamingBuffer(std::string name, int length);
extern void GoFullScreen(bool fullscreen);
extern void showWebPanel(const char* url);  // Add webview panel display function

typedef void(*BeforeDrawFunc)();
extern BeforeDrawFunc beforeDraw;



void GenerateStackFromPanelCommands(unsigned char* buffer, int len);
void ProcessUIStack();
void ProcessWorkspaceQueue(void* ptr); // maps to implementation details. this always calls on main thread so is synchronized.

// =============================== Implementation details ==============================

extern int fuck_dbg;

// constants for atmospheric scattering
const float e = 2.71828182845904523536028747135266249775724709369995957;
const float pi = 3.141592653589793238462643383279502884197169;

#define MAX_VIEWPORTS 8

struct me_obj;

// don't use smart_pointer because we could have pending "wild" object, so we use tref a dedicate reference class for me_obj.
struct namemap_t
{
	int type; // same as selection.
	int instance_id;
	me_obj* obj;
};

struct reference_t :namemap_t
{
    static void push_list(std::vector<reference_t>& referenced_objects, me_obj* t);
    size_t obj_reference_idx;
    reference_t(namemap_t nt, int obj_ref_idx)
        : namemap_t(nt), obj_reference_idx(obj_ref_idx) {}
    reference_t() : namemap_t{0, 0, nullptr}, obj_reference_idx(0) {}
    void remove_from_obj();
};

void set_reference(reference_t& p, me_obj* t);

struct dereference_t
{
    std::function<std::vector<reference_t>*()> accessor = nullptr;
	size_t offset;

    reference_t* ref; // if accessor is null, use ref.
};

// fucked..
struct me_obj
{
    std::string name;
    bool show[MAX_VIEWPORTS] = { true, true, true, true, true, true, true, true }; // 8 trues.
    //todo: add border shine etc?

    std::vector<dereference_t> references;
    size_t push_reference(std::function<std::vector<reference_t>*()> dr, size_t offset);

    // animation easing:
    glm::vec3 target_position = glm::zero<glm::vec3>();
    glm::quat target_rotation = glm::identity<glm::quat>();

    glm::vec3 previous_position = glm::zero<glm::vec3>();
    glm::quat previous_rotation = glm::identity<glm::quat>();
    float target_start_time=0, target_require_completion_time =0 ;

    glm::vec3 current_pos = glm::zero<glm::vec3>();
    glm::quat current_rot = glm::identity<glm::quat>();

    bool current_pose_computed = false;
    reference_t anchor;
    int anchor_subid = -1; // not anchor to subobject.
    void remove_anchor();

    glm::vec3 offset_pos = glm::zero<glm::vec3>();
    glm::quat offset_rot = glm::identity<glm::quat>();

    void compute_pose();

    ~me_obj()
    {
        anchor.remove_from_obj();
    }
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
            printf("put meobj `%s` @ %x\n", name.c_str(), what);
			global_name_map.add(name, nt);
		}
		if constexpr (std::is_base_of_v<self_idref_t,T>)
			((self_idref_t*)what)->instance_id = iid;
		return iid;
	}

	// this method delete ptr.
    // ** if no_delete, must add immediately!!!
	void remove(std::string name, indexier<T>* transfer=nullptr)
	{
		auto it = name_map.find(name);
        if (it == name_map.end()) return;
		

        auto ptr = std::get<0>(ls[it->second]);
        if constexpr (std::is_base_of_v<me_obj, T>)
        {
            // 1. ref unlink.
            if (!transfer) {
                for (auto ref : ((me_obj*)ptr)->references) {
                    if (ref.accessor != nullptr)
                        (*ref.accessor())[ref.offset].obj = nullptr;
                    else
                        (*ref.ref).obj = nullptr;
                }

                printf("remove %s @ %x, %d references\n", name.c_str(), ptr, ((me_obj*)ptr)->references.size());
            }
        }

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

		name_map.erase(name);

        if (transfer == nullptr)
        {
            if constexpr (!std::is_same_v<T, namemap_t> && std::is_base_of_v<me_obj, T>) {
                global_name_map.remove(name);
            }
            delete ptr;
        }
        else
        {
            auto itt = transfer->name_map.find(name);
            if (itt != transfer->name_map.end()) {
                throw "Transfer but already in indexier";
            }

            transfer->name_map[name] = transfer->ls.size();
            transfer->ls.push_back(std::tuple<T*, std::string>(ptr, name));
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

struct disp_area_t
{
    struct { int x, y; } Size;
    struct { int x, y; } Pos;
} ;

struct abstract_operation
{
    virtual std::string Type() = 0;
    virtual void pointer_down() = 0;
    virtual void pointer_move() = 0;
    virtual void pointer_up() = 0;
    
    virtual void canceled() = 0;

    // virtual void restore() = 0;

    virtual void feedback(unsigned char*& pr) = 0;
    virtual void destroy() = 0;
    virtual void draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm) {};
};

enum feedback_mode
{
    pending, operation_canceled, feedback_finished, feedback_continued, realtime_event,
};


struct workspace_state_desc
{
    int id;
    std::string name;

    std::vector<reference_t> hidden_objects, no_cross_section, selectables, sub_selectables;

    // display parameters.
    bool useEDL = true, useSSAO = true, useGround = true, useBorder = true, useBloom = true, drawGrid = true, drawGuizmo = true;
    glm::vec4 hover_shine = glm::vec4(0.6, 0.6, 0, 0.6), selected_shine = glm::vec4(1, 0, 0, 1);
    glm::vec4 hover_border_color = glm::vec4(1, 1, 0, 1), selected_border_color = glm::vec4(1, 0, 0, 1), world_border_color = glm::vec4(1, 1, 1, 1);
    bool btf_on_hovering = true; //brint to front on hovering.

    // Grid appearance state
    bool useOperationalGrid = false;
    glm::vec3 operationalGridPivot = glm::vec3(0, 0, 0);
    glm::vec3 operationalGridUnitX = glm::vec3(1, 0, 0);
    glm::vec3 operationalGridUnitY = glm::vec3(0, 1, 0);

    // pointer state
    int pointer_mode = 0; // 0: operational plane 2d, 1: view plane 2d. 2: holo 3d.
    glm::vec3 pointing_pos;
    bool valid_pointing = false;

    // New clipping planes structure
    struct ClippingPlane {
        glm::vec3 center;
        glm::vec3 direction;
    };
    ClippingPlane clippingPlanes[4];
    int activeClippingPlanes = 0;

    abstract_operation* operation;
    feedback_mode feedback;

    bool queryViewportState = false, captureRenderedViewport = false;
};

struct no_operation : abstract_operation
{
    std::string Type() override { return "no operation"; }

    void pointer_down() override {};
    void pointer_move() override {};
    void pointer_up() override {};
    void canceled() override {};

    void feedback(unsigned char*& pr) override { };
    void destroy() override {};
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
    virtual void process(disp_area_t disp_area, ImDrawList* dl) = 0;
    virtual void feedback(unsigned char*& pr) = 0;
    
    bool previouslyKJHandled;
    void process_keyboardjoystick();
    virtual void keyboardjoystick_map() = 0;

    bool skipped = false;
    virtual void process_default() {};
};

struct toggle_widget:widget_definition
{
    glm::vec2 center_uv, center_px,sz_uv,sz_px;
    bool on;

	void feedback(unsigned char*& pr) override;
	std::string WidgetType() override { return "toggle"; }
    void process(disp_area_t disp_area, ImDrawList* dl) override;

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
    void process(disp_area_t disp_area, ImDrawList* dl) override;
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
    void process(disp_area_t disp_area, ImDrawList* dl) override;
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

    void process(disp_area_t disp_area, ImDrawList* dl) override;
	void feedback(unsigned char*& pr) override;

    void keyboardjoystick_map() override;
    void process_default() override;
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
    void draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm) override;
    
    void destroy() override { delete this; };
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
    void destroy() override {};
    void draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm) override;
};

struct follow_mouse_operation : abstract_operation
{
    int mode;

    bool real_time;
    bool allow_same_place;

    float downX, downY;
    float hoverX, hoverY;

    bool working = false;
    // Add world positions for mouse coordinates in 3D space
    glm::vec3 downWorldXYZ;
    glm::vec3 hoverWorldXYZ;

    std::vector<reference_t> referenced_objects;
    std::vector<glm::vec3> original;

    std::vector<std::string> snapsStart;
    std::vector<std::string> snapsEnd;

    std::string Type() override { return "follow_mouse"; }

    void pointer_down() override;;
    void pointer_move() override;
    void pointer_up() override;
    void canceled() override;

    void feedback(unsigned char*& pr) override;
    void destroy() override;
	void draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm) override;

};

struct positioning_operation : abstract_operation
{
    int mode;  // 0 = GridPlane, 1 = ViewPlane, 2 = 3D

    bool real_time;
    float clickingX, clickingY;
    glm::vec3 worldXYZ;

    std::vector<std::string> snaps;

    std::string Type() override { return "positioning"; }

    void pointer_down() override;;
    void pointer_move() override;
    void pointer_up() override;
    void canceled() override;

    void draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm) override;
    void feedback(unsigned char*& pr) override;
    void destroy() override {};
};

enum guizmo_modes
{
    gizmo_moveXYZ, gizmo_rotateXYZ
};

struct guizmo_operation : abstract_operation
{
    guizmo_modes mode;

    std::vector<std::string> snaps;

    glm::vec3 gizmoCenter, originalCenter;
    glm::quat gizmoQuat;
    bool realtime = false;

	std::vector<reference_t> referenced_objects;
    std::vector<glm::mat4> intermediates;

    std::string Type() override { return "guizmo"; }

    void pointer_down() override {};
    void pointer_move() override {};
    void pointer_up() override {};
    void canceled() override;

    bool selected_get_center(); // if returen false, then selection cannot move.
    void draw(disp_area_t disp_area, ImDrawList* dl, glm::mat4 vm, glm::mat4 pm) override;
    void feedback(unsigned char*& pr) override;

    ~guizmo_operation(); // should clear obj_action_state;
    void destroy() override { delete this; };
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

struct viewport_state_t {
    int frameCnt;
    bool active, assigned, graphics_inited;
    ImGuiWindow* imguiWindow; // Track the ImGui window for the viewport

	// ********* DISPLAY STATS ******
    bool holography_loaded_params = false;
    enum DisplayMode {
        Normal,
        VR, //not used, since imgui doesn't have good suport.
        EyeTrackedHolography
    };
    DisplayMode displayMode;

    enum PropDisplayMode
    {
	    AllButSpecified, // default to display all prop, except the ones specified.
    	NoneButSpecified, // only display the targeting props.
    };
    PropDisplayMode propDisplayMode;
    std::string namePatternForPropDisplayMode;

    disp_area_t disp_area;
    Camera camera;
    float sun_altitude; // default skybox.

    // displaying states
    bool refreshStare = false;

    // UIOperations
    // to uniform. type:1 pc, 1000+gltf class XXX
    int hover_type, hover_instance_id, hover_node_id;
    me_obj* hover_obj;
    float mouseX();
	float mouseY();

    // workspace operations.
    std::vector<workspace_state_desc> workspace_state;
    void pop_workspace_state();
    void clear();

    // feedback.
    unsigned char ws_feedback_buf[1024 * 1024 * 32];
    NotifyWorkspaceChangedFunc workspaceCallback;

    // menu thing:
    bool showMainMenuBar = false, clicked = false;
    unsigned char* mainMenuBarData, *mainmenu_cached_pr;
};

// grating display for eye tracked display.
const float g_ang = -79.7280f / 180 * pi;
// const float g_ang = -pi / 2;
struct grating_param_t {
	float world2phy = 100; // world 1 equivalent to physical ?mm

	float grating_interval_mm = 0.609901f;
	float grating_to_screen_mm = 0.62752;
	float grating_bias = -0.668f;

	float slot_width_mm = 0.035f;

	float pupil_distance_mm = 69.5f;  // My IPD
	float eyes_pitch_deg = 0.0f;      // Rotation around X axis
	glm::vec3 eyes_center_mm = glm::vec3(298.0f, 166.0f, 400.0f); // Center point between eyes
	glm::vec3 left_eye_pos_mm;        // Calculated position (x:left->right, y:top->down, z:in->out).
	glm::vec3 right_eye_pos_mm;       // Calculated position
	glm::vec2 grating_dir = glm::vec2(cos(g_ang), sin(g_ang));
	glm::vec2 screen_size_physical_mm = glm::vec2(596.0f, 332.0f);

	float pupil_factor = 1.0f;

	bool debug_show = 0, show_right=1, show_left=1, debug_eye;

	float viewing_angle = 10;
	float beyond_viewing_angle = 52;
	glm::vec2 compensator_factor_1 = glm::vec2(0.151, 0.099);

	float viewing_angle_f = 15;
	float beyond_viewing_angle_f = 35;

	glm::vec2 leakings = glm::vec2(0.04,0.6); // 
	glm::vec2 dims = glm::vec2(0.45,1); // 
};
extern grating_param_t grating_params;

struct ui_state_t
{
	mytime started_time;
	uint64_t getMsFromStart();
    float getMsGraphics();
    int loopCnt = 0;

	struct{
        int width;
		int height;
        int offsetY;
        int advanceX;
        uint8_t rgba[256 * 256 * 4]; // in case too big.
    }app_icon;

    viewport_state_t viewports[MAX_VIEWPORTS]; // at most 8 viewport per terminal.

    //******* POINTER **********
    float mouseX, mouseY; // related to screen top-left.
    bool mouseLeft, mouseMiddle, mouseRight;
    int mouseLeftDownLoopCnt;
    bool mouseTriggered = false;
    int mouseCaptuingViewport;

    std::set<int> prevTouches;
    std::vector<touch_state> touches;
    
    // ****** MODIFIER *********
    bool ctrl;

    bool displayRenderDebug();
#ifdef _DEBUG
    bool RenderDebug = true;
#else
    bool RenderDebug = false;
#endif

};
extern ui_state_t ui; 


void NotifyWorkspaceUpdated();
void DeapplyWorkspaceState();
void ReapplyWorkspaceState();

// bg renderer
void SetCustomBackgroundShader(std::string shaderCode);
void DisableCustomBackgroundShader();

// workspace stack.
void _clear_action_state();
template <typename workspaceType> void BeginWorkspace(int id, std::string state_name, viewport_state_t& viewport);
std::string GetWorkspaceName();
void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius = 0); //"none", "click", "drag", "drag+click", "painter(r=123)"
void SetWorkspacePropDisplayMode(int mode, std::string namePattern);
void SetGridAppearance(bool pivot_set, glm::vec3 pivot,bool unitX_set, glm::vec3 unitX,bool unitY_set, glm::vec3 unitY);
void SetGridAppearanceByView(bool pivot_set, glm::vec3 pivot);
// ***************************************************************************
// ME object manipulations:
void RemoveObject(std::string name);
void RemoveNamePattern(std::string name);
void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time, uint8_t type, uint8_t coord);
void AnchorObject(std::string earth, std::string moon, glm::vec3 rel_position, glm::quat rel_quaternion);
void TransformSubObject(std::string objectNamePattern, uint8_t selectionMode, std::string subObjectName,
    int subObjectId, uint8_t actionMode, uint8_t transformType,
    glm::vec3 translation, glm::quat rotation, float timeMs);

// Workspace temporary apply:
void SetShowHide(std::string name, bool show); 
void SetApplyCrossSection(std::string name, bool show);

// *************************************** Object Types **********************
// pointcloud, gltf, line, line-extrude, sprite. future expands: road, wall(door), floor, geometry


// * Helper function.
void RouteTypes(namemap_t* type, std::function<void()> point_cloud,
    std::function<void(int)> gltf, 
    std::function<void()> line_piece, 
    std::function<void()>sprites,
    std::function<void()>spot_texts,
    std::function<void()> not_used_now);

/*  template for traversing objects.
 *	for (int gi = 0; gi < global_name_map.ls.size(); ++gi){
		auto nt = global_name_map.get(gi);
		auto name = global_name_map.getName(gi);
		RouteTypes(nt, 
			[&]	{
				// point cloud.
				auto t = (me_pcRecord*)nt->obj;
			}, [&](int class_id)
			{
				// gltf
				auto t = (gltf_object*)nt->obj;
				auto cls = gltf_classes.get(class_id);

			}, [&]
			{
				// line piece.
			}, [&]
			{
				// sprites;
				auto t = (me_sprite*)nt->obj;
			},[&]
			{
				// world ui
			},[&]
			{
				// geometry.
			});
	}
	*/

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
void AddBezierCurve(std::string name, const line_info& what, const std::vector<glm::vec3>& controlPoints);



// -------- Image ----------------
void AddImage(std::string name, int flag, glm::vec2 disp, glm::vec3 pos, glm::quat quat, std::string rgbaName);
void PutRGBA(std::string name, int width, int height);
void InvalidateRGBA(std::string name);
void UpdateRGBA(std::string name, int len, char* rgba);
void SetRGBAStreaming(std::string name);
struct rgba_ref
{
    int width, height, layerid=-1;
    glm::vec2 uvStart, uvEnd;
};
rgba_ref UIUseRGBA(std::string name);

// -------- SVG ----------------
void DeclareSVG(std::string name, std::string svgContent);



// -------- GLTF ---------------
struct ModelDetail
{
    glm::vec3 center = glm::vec3(0);
    glm::quat rotate = glm::identity<glm::quat>();
    float scale = 1;
    glm::vec3 color_bias = glm::vec3(0); // Add color bias (0-1 range)
    float contrast = 1;
    float brightness = 1;
    bool force_dbl_face = false;
    float normal_shading = 0;
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


// -------- Workspace UI ---------------

// Handle Icon structure
struct handle_icon_info {
	std::string name;
	glm::vec3 position;
    float size;
	std::string icon;
	uint32_t color;       // Text color
	uint32_t handle_color; // Handle background color
};

// Text Along Line structure
struct text_along_line_info {
	std::string name;
	glm::vec3 start;
    std::string dirProp;
	glm::vec3 direction;
	std::string text;
    float size, voff;
    bool bb;
	uint32_t color;
};

// Add handle icon to the workspace
void AddHandleIcon(std::string name, const handle_icon_info& info);

// Add text along line to the workspace
void AddTextAlongLine(std::string name, const text_along_line_info& info);


// *************************** Object Manipulation ***********************************
// object specific invokations.
struct ModelObjectProperties {
    bool baseAnimId_set = false;
    int baseAnimId = -1;
    
    bool nextAnimId_set = false;
    int nextAnimId = -1;
    
    bool material_variant_set = false;
    int material_variant = 0;
    
    bool team_color_set = false;
    uint32_t team_color = 0;
    
    bool base_stopatend_set = false;
    bool base_stopatend = false;
    
    bool next_stopatend_set = false;
    bool next_stopatend = false;
    
    bool animate_asap_set = false;
    bool animate_asap = false;
    
    // Additional properties can be added here in the future
};
void SetModelObjectProperty(std::string namePattern, const ModelObjectProperties& props);


// shine color + intensity. for each object can set a shine color, and at most 7 shines for subobject
// if any channel: shine color*intensity > 0.5, bloom.
void SetObjectShine(std::string patternname, bool use, uint32_t color);
void SetObjectBorder(std::string patternname, bool use);
void SetObjectTransparency(std::string patternname, float transparency);
void SetSubObjectBorderShine(std::string name, bool use, int subid, bool border, uint32_t color);


// ui related
void SetObjectSelectable(std::string name, bool selectable = true);
void SetObjectSubSelectable(std::string name, bool subselectable);

void SetObjectSelected(std::string patternname);
void ClearSelection();

void BringObjectFront(std::string name, bool bring2front);
void BringSubObjectFront(std::string name, bool bring2front, int subid);

void SetObjectBillboard(std::string name, std::vector<unsigned char> ui_stack);
void SetSubObjectBillboard(std::string name, int subid, std::vector<unsigned char> ui_stack);


// cycle ui internal usage.
void InitGraphics();
void initialize_viewport(int id, int w, int h);
void DrawMainWorkspace();
void ProcessBackgroundWorkspace();
void BeforeDrawAny();
void FinalizeFrame();
void ActualWorkspaceQueueProcessor(void* wsqueue, viewport_state_t& vstate);


// callbacks.
void mouse_button_callback(GLFWwindow* window, int button, int action, int mods);

void cursor_position_callback(GLFWwindow* window, double xpos, double ypos);

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset);

void key_callback(GLFWwindow* window, int key, int scancode, int action, int mods);
void touch_callback(std::vector<touch_state> touches);


// multi viewport:
void draw_viewport(disp_area_t region, int vid);
void aux_workspace_notify(unsigned char* news, int length);
void switch_context(int vid);
void destroy_state(viewport_state_t* state);