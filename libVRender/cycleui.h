#pragma once

#include <chrono>
#include <map>
#include <stack>
#include <string>
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
extern char* appName;

typedef void(*NotifyWorkspaceChangedFunc)(unsigned char* news, int length);
typedef void(*NotifyStateChangedFunc)(unsigned char* changedStates, int length);
extern NotifyStateChangedFunc stateCallback;
extern NotifyWorkspaceChangedFunc workspaceCallback;
extern void ExternDisplay(const char* filehash, int pid, const char* fname);
extern uint8_t* GetStreamingBuffer(std::string name, int length);

typedef void(*BeforeDrawFunc)();
extern BeforeDrawFunc beforeDraw;



void GenerateStackFromPanelCommands(unsigned char* buffer, int len);
void ProcessUIStack();
void ProcessWorkspaceQueue(void* ptr); // maps to implementation details.

// =============================== Implementation details ==============================

enum selecting_modes
{
	click, drag, paint
};

enum action_type
{
    selectObj, //feedback: object/subobject

    gizmoXYZ, gizmo_moveXY, gizmo_rotateXY, gizmo_allXY, gizmo_moveXYZ, gizmo_rotateXYZ, //display a gizmo, complete after clicking OK.

    dragLine, //start->end.
    clickPos, //feedback is position + hovering item.

    placeObjXY, //obj follows mouse, after click object is placed and action is completed.
    placeObjZ,

	moveRotateObjZ, moveRotateObjX, moveRotateObjY, 
};

struct workspace_state_desc
{
    int id;
    std::string name;

    action_type function;

    bool gizmo_realtime;
    
    bool right_click_select = false; //

    bool finished = false;

    std::unordered_set<std::string> hoverables, sub_hoverables, bringtofronts;
    selecting_modes selecting_mode = click;
    float paint_selecting_radius = 10;

    bool useEDL = true, useSSAO = true, useGround = true, useBorder = true, useBloom = true, drawGrid = true;
    glm::vec4 hover_shine = glm::vec4(0.6, 0.6, 0, 0.6), selected_shine = glm::vec4(1, 0, 0, 1);
    glm::vec4 hover_border_color = glm::vec4(1, 1, 0, 1), selected_border_color = glm::vec4(1, 0, 0, 1), world_border_color = glm::vec4(1, 1, 1, 1);
    bool btf_on_hovering = true;
};

struct selected
{
    int type, instance_id;
    bool sub_selected;
};

typedef std::chrono::time_point<std::chrono::high_resolution_clock> mytime;
struct ui_state_t
{
	mytime started_time;
	struct{
        int width;
		int height;
        int offsetY;
        int advanceX;
        uint8_t rgba[256 * 256 * 4]; // in case too big.
    }app_icon;
	uint64_t getMsFromStart();

    bool displayRenderDebug = false;

    int workspace_w, workspace_h;
    float mouseX, mouseY, mouseXS, mouseYS; // mouseXYS: mouse pointing pos project to the ground plane.
    bool mouseLeft, mouseMiddle, mouseRight;

    // for dlbclick.
    int clickedMouse = -1; //0:left, 1:middle, 2:right.
    float lastClickedMs = -999;

    // should put into wstate.
    bool selecting = false;
    bool extract_selection = false;

    bool selectedGetCenter = false;
    glm::vec3 gizmoCenter, originalCenter;
    glm::quat gizmoQuat;

    float select_start_x, select_start_y; // drag
    std::vector<unsigned char> painter_data; 

	// int mouse_type, mouse_instance, mouse_subID; //type:1~999, internal, 1000~inf: gltf.
    //glm::vec4 hover_id;

    // to uniform. type:1 pc, 1000+gltf class XXX
    int hover_type, hover_instance_id, hover_node_id;
    
    int feedback_type = -1; //1: selection, 2: transform.

    std::stack<workspace_state_desc> workspace_state;

    bool ctrl;

    bool refreshStare = false;
};
extern ui_state_t ui_state;


// *************************************** Object Types **********************
// pointcloud, gltf, line, line-extrude, sprite. future expands: road, wall(door), floor, geometry

void AllowWorkspaceData();

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
void RemovePointCloud(std::string name);



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

struct mesh
{
    std::vector<glm::vec3> vertices;
    std::vector<float> indices;
};

// -------- IMAGE ---------------
void AddImage(std::string name, bool billboard, glm::vec2 disp, glm::vec3 pos, glm::quat quat, std::string rgbaName);
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
void PutModelObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion);
void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time, uint8_t type, uint8_t coord);
void RemoveModelObject(std::string name);


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
void BeginWorkspace(int id, std::string state_name);
void _clear_action_state();
std::string GetWorkspaceName();

void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius = 0); //"none", "click", "drag", "drag+click", "painter(r=123)"
void SetWorkspaceNextAction(action_type type);

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

void PopWorkspace();


// cycle ui internal usage.
void InitGL(int w, int h);
void DrawWorkspace(int w, int h);

void mouse_button_callback(GLFWwindow* window, int button, int action, int mods);

void cursor_position_callback(GLFWwindow* window, double xpos, double ypos);

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset);

void key_callback(GLFWwindow* window, int key, int scancode, int action, int mods);
