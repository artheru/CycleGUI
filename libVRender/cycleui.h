#pragma once

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


typedef void(*NotifyStateChangedFunc)(unsigned char* changedStates, int length);
extern unsigned char* stack;
extern NotifyStateChangedFunc stateCallback;

typedef void(*BeforeDrawFunc)();
extern BeforeDrawFunc beforeDraw;



void GenerateStackFromPanelCommands(unsigned char* buffer, int len);
void ProcessUIStack();


enum selecting_modes
{
	click, drag, paint
};

struct workspace_state_desc
{
    std::unordered_set<std::string> hoverables, sub_hoverables, bringtofronts;

    selecting_modes selecting_mode = click;
    float paint_selecting_radius = 10;
};

struct selected
{
    int type, instance_id, node_id;
};

struct ui_state_t
{
    int workspace_w, workspace_h;
    float mouseX, mouseY, mouseXS, mouseYS; // mouseXYS: mouse pointing pos project to the ground plane.
    bool mouseLeft, mouseMiddle, mouseRight;

    bool selecting = false;
    bool extract_selection = false;

    float select_start_x, select_start_y; // drag
    std::vector<unsigned char> painter_data; 

    std::string mousePointingType, mousePointingInstance;
    int mousePointingSubId;
	// int mouse_type, mouse_instance, mouse_subID; //type:1~999, internal, 1000~inf: gltf.
    //glm::vec4 hover_id;

    // to uniform. type:1 pc, 1000+gltf class XXX
    int hover_type, hover_instance_id, hover_node_id;
    
    std::vector<selected> selected;

    glm::vec4 hover_shine = glm::vec4(0.6, 0.6, 0, 0.6), selected_shine = glm::vec4(1, 0, 0, 1);

    glm::vec4 hover_border_color = glm::vec4(1, 1, 0, 1), selected_border_color = glm::vec4(1, 0, 0, 1), world_border_color = glm::vec4(1, 1, 1, 1);

    std::stack<workspace_state_desc> workspace_state;

    std::vector<glm::vec4> selpix;
    bool ctrl;
};
extern ui_state_t ui_state;


// *************************************** Object Types **********************
// pointcloud, gltf, line, line-extrude, sprite. future expands: road, wall(door), floor, geometry
struct point_cloud
{
    std::vector<glm::vec4> x_y_z_Sz;
    std::vector<uint32_t> color;
    // maximum 12.5M selector (8bit)
    glm::vec3 position = glm::zero<glm::vec3>();
    glm::quat quaternion = glm::identity<glm::quat>();
};
void AddPointCloud(std::string name, point_cloud& what);
void ManipulatePointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion);
void SetPointCloudBehaviour(std::string name, bool showHandle, bool selectByHandle, bool selectByPoints);
void RemovePointCloud(std::string name);

struct line
{
    std::vector<std::tuple<glm::vec4, glm::vec4>> lines;
    std::vector<float> widths;
    std::vector<glm::vec4> color;
};

struct mesh
{
    std::vector<glm::vec3> vertices;
    std::vector<float> indices;
    
};


struct sprite
{
    int channels; //1 or 3
    void* data;
    int spriteW, spriteH; //pixel width/height

    float width, height; //displaying width/height
};
void AddSprite(std::string name, sprite& what);
void ModifySprite(std::string name, glm::vec3 new_position, glm::quat new_quaternion);
void RemoveSprite(std::string name);

// object manipulation:
struct ModelDetail
{
    glm::vec3 center = glm::vec3(0);
    glm::quat rotate = glm::identity<glm::quat>();
    float scale = 1;
};
void LoadModel(std::string cls_name, unsigned char* bytes, int length, ModelDetail detail);
void PutModelObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion);
void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time);


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
void BeginWorkspace(std::string mode);
std::string GetWorkspaceName();

void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius = 0); //"none", "click", "drag", "drag+click", "painter(r=123)"

void SetObjectSelectable(std::string name);
void SetObjectSubSelectable(std::string name);
void CancelObjectSelectable(std::string name);
void CancelObjectSubSelectable(std::string name);

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
