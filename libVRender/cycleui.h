#pragma once

#include <map>
#include <string>
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



struct point_cloud
{
    std::vector<glm::vec4> x_y_z_Sz;
    std::vector<glm::vec4> color;
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


void SetObjectBaseAnimation(std::string name, std::string state);
void PlayObjectEmote(std::string name, std::string emote);
void SetObjectWeights(std::string name, std::string state);


void InitGL(int w, int h);
void DrawWorkspace(int w, int h);

void mouse_button_callback(GLFWwindow* window, int button, int action, int mods);

void cursor_position_callback(GLFWwindow* window, double xpos, double ypos);

void scroll_callback(GLFWwindow* window, double xoffset, double yoffset);
