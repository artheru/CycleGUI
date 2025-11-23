#pragma once

#include <imgui_internal.h>
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
#include <glm/glm.hpp>

// unit: m

#define M_PI       3.14159265358979323846f
#define M_PI_2     1.57079632679489661923f
#define M_PI_4     0.785398163397448309616f

#define cam_near 0.0625f
#define cam_far 4096.0f

struct disp_area_t;

class Camera {
public:
    bool extset = false;

    glm::vec3 stare, position;
    glm::mat4 vm;

    float distance;
    float Azimuth = -M_PI_2;
    float Altitude = M_PI_2;

    glm::vec3 up = glm::vec3(0.0f, 0.0f, 1.0f);
    glm::vec3 moveFront = glm::vec3(0.0f, 1.0f, 0.0f);
    glm::vec3 moveRight = glm::vec3(1.0f, 0.0f, 0.0f);

    float rotateSpeed = 0.001745f;

    float _width, _height, _aspectRatio, _fov = 45;
    int ProjectionMode = 0;
    float _minDist;
    float gap = 0.02f;
    float OrthoFactor =1500;
    float dpi=1;
    
    // Camera constraints
    glm::vec2 azimuth_range = glm::vec2(-FLT_MAX, FLT_MAX);
    glm::vec2 altitude_range = glm::vec2(-M_PI_2, M_PI_2);
    glm::vec2 x_range = glm::vec2(-FLT_MAX, FLT_MAX);
    glm::vec2 y_range = glm::vec2(-FLT_MAX, FLT_MAX);
    glm::vec2 z_range = glm::vec2(-FLT_MAX, FLT_MAX);
    glm::vec2 pan_range_x = glm::vec2(-FLT_MAX, FLT_MAX);
    glm::vec2 pan_range_y = glm::vec2(-FLT_MAX, FLT_MAX);
    glm::vec2 pan_range_z = glm::vec2(-FLT_MAX, FLT_MAX);
    bool mmb_freelook = false;

    // anchor type for applying external camera offset/anchor
    // 0: anchor both (default), 1: anchor stare only, 2: anchor position only
    int anchor_type = 0;

    void init(glm::vec3 stare, float dist, float width, float height, float minDist);

    void RotateAzimuth(float delta);

    void RotateAltitude(float delta);

    void Rotate(float deltaAlt, float deltaAzi);

    void PanLeftRight(float delta);

    void PanBackForth(float delta);

    void ElevateUpDown(float delta);

    void Zoom(float delta);

    void GoFrontBack(float delta); // how much to go?

    void Reset(glm::vec3 stare, float dist);

    void Resize(float width, float height);

    bool test_apply_external();

    glm::vec3 getPos();
    glm::vec3 getStare();

    glm::mat4 GetViewMatrix();

    glm::mat4 GetProjectionMatrix();

    void UpdatePosition();
};

class GroundGrid {
public:
    float width;
    float height;
    
    float red = 138.0f / 256.0f;
    float green = 43.0f / 256.0f;
    float blue = 226.0f / 256.0f;

	void Draw(Camera& cam, disp_area_t disp_area, ImDrawList* dl, glm::mat4 viewMatrix, glm::mat4 projectionMatrix);
	
private:
    float lastY = 0;
    float lastX = 0;
    
    bool LineSegCrossBorders(glm::vec2 p, glm::vec2 q, int availEdge, glm::vec2& pq);
    
    void DrawGridInternal(Camera& cam, disp_area_t disp_area, ImDrawList* dl, 
                         glm::mat4 viewMatrix, glm::mat4 projectionMatrix, 
                         bool isOperational);

    static glm::vec2 ConvertWorldToScreen(glm::vec3 input, glm::mat4 v, glm::mat4 p, glm::vec2 screenSize);
};

// Get the final computed node matrix from GPU after all matrix computation passes
// Call this after GPU matrix computation is complete
// Parameters:
//   class_id: Index of the glTF class
//   node_id: Index of the node within the glTF model
//   instance_id: Index of the instance of this glTF class
// Returns: The final 4x4 transformation matrix for the specified node/instance
glm::mat4 GetFinalNodeMatrix(int class_id, int node_id, int instance_id);