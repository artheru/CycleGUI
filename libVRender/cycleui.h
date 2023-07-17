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
	none, drag, paint, click, multi_drag_click
};

struct workspace_state_desc
{
    std::unordered_set<std::string> hoverables, sub_hoverables;

    std::vector<std::string> selectables;
    std::vector<std::string> sub_selectables;

    selecting_modes selecting_mode = none;
    float paint_selecting_radius = 10;
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

    // to uniform.
    int hover_type, hover_instance_id, hover_node_id;

    std::stack<workspace_state_desc> workspace_state;
};
extern ui_state_t ui_state;

struct point_cloud
{
    std::vector<glm::vec4> x_y_z_Sz;
    std::vector<glm::vec4> color;
    // maximum 12.5M selector (8bit)
    int flag; //1:show handle, 2: can select handle, 3: can select point, 4: no EDL
    glm::vec3 position = glm::zero<glm::vec3>();
    glm::quat quaternion = glm::identity<glm::quat>();
};

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

void AddPointCloud(std::string name, point_cloud& what);
void ModifyPointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion);
void RemovePointCloud(std::string name);

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

// animation
void SetObjectBaseAnimation(std::string name, std::string state);
void PlayObjectEmote(std::string name, std::string emote);
void SetObjectWeights(std::string name, std::string state);

// object behaviour
void SetWorkspaceShine(glm::vec3 color, float value);

void SetObjectShineOnHover(std::string name);
void SetSubObjectShineOnHover(std::string name);
void SetObjectBorderOnHover(std::string name);
void SetSubObjectBorderOnHover(std::string name);
void BringObjectFrontOnHover(std::string name);
void BringSubObjectFrontOnHover(std::string name);

void BringObjectFront(std::string name);
void BringSubObjectFront(std::string name, int subid);

void SetObjectShine(std::string name, glm::vec4 color); // shine color + intensity.
void CancelObjectShine(std::string name);

void SetObjectBorder(std::string name);
void CancelObjectBorder(std::string name);
void SetSubObjectBorder(std::string name, int subid);

void SetObjectSelectable(std::string name);
void SetObjectSubSelectable(std::string name);

void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius = 0); //"none", "click", "drag", "drag+click", "painter(r=123)"

void SetObjectBillboard(std::string name, std::vector<unsigned char> ui_stack);
void SetSubObjectBillboard(std::string name, int subid, std::vector<unsigned char> ui_stack);

void InitGL(int w, int h);
void DrawWorkspace(int w, int h);

void mouse_button_callback(GLFWwindow* window, int button, int action, int mods);

void cursor_position_callback(GLFWwindow* window, double xpos, double ypos);

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset);
