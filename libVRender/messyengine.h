#pragma once

#include <imgui_internal.h>
#include <glm/glm.hpp>

// unit: m

#define M_PI       3.14159265358979323846f
#define M_PI_2     1.57079632679489661923f
#define M_PI_4     0.785398163397448309616f

#define cam_near 0.0625f
#define cam_far 4096.0f

class Camera {
public:
    glm::vec3 stare, position;

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
    const float gap = 0.02f;
    float OrthoFactor =1500;
    float dpi=1;

    Camera(glm::vec3 stare, float dist, float width, float height, float minDist);

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
    
    GroundGrid();

    void Draw(Camera& cam, ImGuiDockNode* disp_area);

private:
    float lastY = 0;
    
    bool LineSegCrossBorders(glm::vec2 p, glm::vec2 q, int availEdge, glm::vec2& pq);

    static glm::vec2 ConvertWorldToScreen(glm::vec3 input, glm::mat4 v, glm::mat4 p, glm::vec2 screenSize);
};


extern Camera* camera;
extern GroundGrid* grid;

extern std::string pressedKeys;