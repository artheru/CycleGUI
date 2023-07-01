#pragma once
#include <GL/glew.h>
#include <GL/glu.h>

#ifdef _DEBUG
#define SOKOL_DEBUG
#define USE_DBG_UI
#define SOKOL_TRACE_HOOKS
#endif
#define SOKOL_IMPL
#define SOKOL_GLES3

#if defined(_MSC_VER)
#pragma warning(disable:4100)
#pragma warning(disable:4804)
#pragma warning(disable:4127)
#pragma warning(disable:4018)
#elif (defined(__GNUC__) || defined(__GNUG__)) && !defined(__clang__)
#pragma GCC diagnostic ignored "-Wbool-compare"
#pragma GCC diagnostic ignored "-Wsign-compare"
#endif

#define NOMINMAX

// note: sokol_gfx is modified to allow passing depth texture
#include "sokol_gfx.h"
#include "sokol_time.h"
#include "sokol_log.h"

#define OFFSCREEN_SAMPLE_COUNT 1

#include "cycleui.h"
#include "messyengine.h"
#include <imgui.h>
#include <iomanip>
#include <ios>
#include <iostream>
#include <sstream>
#include <unordered_map>

#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/type_ptr.hpp>
#include <glm/gtx/string_cast.hpp>


#define TINYGLTF_IMPLEMENTATION
#define STB_IMAGE_IMPLEMENTATION
#define STB_IMAGE_WRITE_IMPLEMENTATION
#define TINYGLTF_USE_RAPIDJSON
//#define TINYGLTF_NOEXCEPTION //! optional. disable exception handling.
#define TINYGLTF_ENABLE_DRACO
#define TINYGLTF_USE_CPP14

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

#include "lib/tinygltf/tiny_gltf.h"

// ███    ███ ███████         ██   ██ ███████  █████  ██████  ███████ ██████  ███████
// ████  ████ ██              ██   ██ ██      ██   ██ ██   ██ ██      ██   ██ ██
// ██ ████ ██ █████           ███████ █████   ███████ ██   ██ █████   ██████  ███████
// ██  ██  ██ ██              ██   ██ ██      ██   ██ ██   ██ ██      ██   ██      ██
// ██      ██ ███████ ███████ ██   ██ ███████ ██   ██ ██████  ███████ ██   ██ ███████

#include "shaders/point_cloud_simple.h"
#include "shaders/ground_plane.h"
#include "shaders/dbg.h"
#include "shaders/depth_blur.h"
#include "shaders/gltf.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

static struct {
	struct {
		sg_image color, depthTest, depth, normal, position;
		sg_pass pass;
		sg_pass_action pass_action;
	} primitives; // draw points, doesn't need binding.

	struct
	{
		sg_buffer instanceID, obj_translate, obj_quat;   // instanced attribute.

		sg_pipeline pip;
		sg_image objInstanceNodeMvMats, objInstanceNodeNormalMats;
		std::vector<int> objOffsets;
		sg_pass pass; sg_pass_action pass_action;
	} instancing;

	struct {
		sg_image shadow_map, depthTest;
		sg_pass pass;
		sg_pass_action pass_action;
	} shadow_map;
	sg_pipeline shadow_pip;

	struct {
		sg_image color, depth;
		sg_pass pass;
		sg_pass_action pass_action;
		sg_bindings bind;
	} edl_lres;

	struct {
		sg_image depth;
		sg_pass pass;
		sg_pass_action pass_action;
	} pc_primitive; // draw points, doesn't need binding.

	sg_pipeline edl_lres_pip;

	struct {
		sg_pass_action pass_action;
		sg_pipeline pip;
		sg_bindings bind;
	} composer;

	sg_buffer quad_vertices;

	struct
	{
		sg_pipeline pip;
		sg_bindings bind;
	} dbg;

	struct
	{
		sg_pipeline pip;
		sg_bindings bind;
	} skybox;

	sg_pipeline gltf_pip, gltf_ground_pip;
	sg_pipeline gltf_pip_depth, pc_pip_depth;

	sg_bindings gltf_ground_binding;

	struct
	{
		// generate ssao
		sg_pipeline pip;
		sg_bindings bindings;
		sg_pass pass;
		sg_pass_action pass_action;
		sg_image image;

		// blur
		sg_bindings blur_bindings;
		sg_pass blur_pass;
		sg_image blur_image;
	} ssao;

	struct
	{
		sg_pipeline pip;
	} kuwahara_blur;

} graphics_state;

Camera* camera;
GroundGrid* grid;

sg_pass_action passAction;

sg_pipeline point_cloud_simple_pip;
sg_pipeline point_cloud_composer_pip;

sg_shader ground_shader;
sg_pipeline ground_pip;


void GenPasses(int w, int h);
void ResetEDLPass();


struct gpu_point_cloud
{
	point_cloud pc;
	sg_buffer pcBuf;
	sg_buffer colorBuf;
};
std::unordered_map<std::string, gpu_point_cloud> pointClouds;


struct gltf_object
{
	glm::vec3 position;
	glm::quat quaternion = glm::identity<glm::quat>();
	std::vector<float> weights;

	glm::vec2 speed; // translation, rotation.
	float elapsed;
	std::string baseAnim;
	std::string playingAnim, nextAnim; // if currently playing is final, switch to nextAnim, and nextAnim:=baseAnim
};

class gltf_class
{
	tinygltf::Model model;
	std::string name;
	int n_indices = 0;
	int totalvtx = 0;
	glm::mat4 i_mat; //centralize and swap z
	std::vector<std::tuple<int, int>> node_length_id;

	std::vector<glm::vec4> node_mats_hierarchy_vec;

	sg_image node_mats_hierarchy;

	//sg_image instanceData; // uniform samplar, x:instance, y:node, (x,y)->data
	//sg_image node_mats, NImodelViewMatrix, NInormalMatrix;

	sg_buffer indices, positions, normals, colors, node_ids;

	//sg_image morph_targets
	struct temporary_buffer
	{
		std::vector<int> indices;
		std::vector<glm::vec3> position, normal;
		std::vector<glm::vec4> color;
		std::vector<int> node_id;
	};
	void load_primitive(int node_idx, temporary_buffer& tmp);
	//void ImportMaterials(const tinygltf::Model& model);
	void update_node(int nodeIdx, std::vector<glm::mat4>& writemat, std::vector<glm::mat4>& readmat, int parent_idx);

	// returns if it has mesh children, i.e. important routing.s
	bool init_node(int node_idx, std::vector<glm::mat4>& writemat, std::vector<glm::mat4>& readmat, int parent_idx, int depth);

	void countvtx(int node_idx);
	void CalculateSceneDimension();

	struct SceneDimension
	{
		glm::vec3 center = { 0.0f, 0.0f, 0.0f };
		float radius{ 0.0f };
	};
public:
	std::vector<glm::mat4> nodes_local_mat;
	std::vector<bool> important_node;

	int class_id, morphTargets;

	void render(const glm::mat4& vm, const glm::mat4& pm, bool shadow_map);
	int compute_mats(const glm::mat4& vm, int offset); // return new offset.
	std::unordered_map<std::string, gltf_object> objects;
	gltf_class(const tinygltf::Model& model, std::string name, glm::vec3 center, float radius);
	SceneDimension sceneDim;
};

std::unordered_map<std::string, gltf_class*> classes;

#ifdef __EMSCRIPTEN__
EM_JS(void, melog, (const char* c_str), {
	const str = UTF8ToString(c_str);console.log(str);
	});
#endif

void checkGLError(const char* file, int line)
{
	GLenum error = glGetError();
	if (error != GL_NO_ERROR) {
		// const char* errorString = reinterpret_cast<const char*>(gluErrorString(error));
#ifdef __EMSCRIPTEN__
		char buf[2048];
		sprintf(buf, "GL Error at %s(%d): %d", file, line, error);
		melog(buf);
#else
		std::cerr << "OpenGL error at " << file << ":" << line << " - " << error << std::endl;
#endif
	}
}