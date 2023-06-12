#pragma once
#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>

#include <string>
#include <map>

// unit: m

#define M_PI       3.14159265358979323846f
#define M_PI_2     1.57079632679489661923f
#define M_PI_4     0.785398163397448309616f


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

    float _width, _height, _aspectRatio, _fov = 60;
    int ProjectionMode = 0;
    float _minDist;
    const float gap = 0.01f;
    float OrthoFactor =1500;

    Camera(glm::vec3 stare, float dist, float width, float height, float minDist);

    void RotateAzimuth(float delta);

    void RotateAltitude(float delta);

    void PanLeftRight(float delta);

    void PanBackForth(float delta);

    void ElevateUpDown(float delta);

    void Zoom(float delta);

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

private:
    GLuint vboHandle;
    GLuint vaoHandle;
    GLuint programHandle;
    std::map<std::string, int> uniformLocations;
    GLfloat red = 138.0f / 256.0f;
    GLfloat green = 43.0f / 256.0f;
    GLfloat blue = 226.0f / 256.0f;

public:
    typedef char GLchar;
    GroundGrid();

    void Draw(Camera& cam);

private:
    GLfloat lastY = 0;
    
    bool LineSegCrossBorders(glm::vec2 p, glm::vec2 q, int availEdge, glm::vec2& pq);

    static glm::vec2 ConvertWorldToScreen(glm::vec3 input, glm::mat4 v, glm::mat4 p, glm::vec2 screenSize);
};


extern Camera* camera;
extern GroundGrid* grid;