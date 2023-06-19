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


#define TINYGLTF_IMPLEMENTATION
#define STB_IMAGE_IMPLEMENTATION
#define STB_IMAGE_WRITE_IMPLEMENTATION
#define TINYGLTF_NOEXCEPTION //! optional. disable exception handling.
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
		sg_image color, depth;
		sg_pass pass;
		sg_pass_action pass_action;
		sg_bindings bind;
	} edl_lres;

	struct {
		sg_image color, depth;
		sg_pass pass;
		sg_pass_action pass_action;
	} edl_hres; // draw points, doesn't need binding.

	sg_pipeline edl_lres_pip;

	struct {
		sg_pass_action pass_action;
		sg_pipeline pip;
		sg_bindings bind;
	} edl_composer;

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

	sg_pipeline gltf_pip;
	sg_pipeline all_depth;
	sg_pipeline ssao_pip;
	sg_pipeline shadow_pip;
} graphics_state;

Camera* camera;
GroundGrid* grid;

sg_pass_action passAction;

sg_pipeline point_cloud_simple_pip;
sg_pipeline point_cloud_composer_pip;

sg_shader ground_shader;
sg_pipeline ground_pip;


void GenEDLPasses(int w, int h);
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

	std::vector<glm::mat4> nodes_t;

	glm::vec2 speed; // translation, rotation.
	float elapsed;
	std::string baseAnim;
	std::string playingAnim, nextAnim; // if currently playing is final, switch to nextAnim, and nextAnim:=baseAnim
};

class gltf_class
{
	tinygltf::Model model;
	std::string name;
	
	struct gltfPrimitives
	{
		int materialID;
		sg_buffer indices;
		sg_buffer position;
		sg_buffer normal;
		sg_buffer texcoord2;
		sg_buffer tangent;
		sg_buffer color;
		int icount, vcount;
	};
	struct GLTFMaterial
	{
		int shadingModel{ 0 };  //! 0: metallic-roughness, 1: specular-glossiness

		//! pbrMetallicRoughness
		glm::vec4  baseColorFactor{ 1.0f, 1.0f, 1.0f, 1.0f };
		int   baseColorTexture{ -1 };
		float metallicFactor{ 1.0f };
		float roughnessFactor{ 1.0f };
		int   metallicRoughnessTexture{ -1 };

		int   emissiveTexture{ -1 };
		glm::vec3  emissiveFactor{ 0.0f, 0.0f, 0.0f };
		int   alphaMode{ 0 }; //! 0 : OPAQUE, 1 : MASK, 2 : BLEND
		float alphaCutoff{ 0.5f };
		int   doubleSided{ 0 };

		int   normalTexture{ -1 };
		float normalTextureScale{ 1.0f };
		int   occlusionTexture{ -1 };
		float occlusionTextureStrength{ 1.0f };


		//! Extensions

		struct KHR_materials_clearcoat
		{
			float factor{ 0.0f };
			int   texture{ -1 };
			float roughnessFactor{ 0.0f };
			int   roughnessTexture{ -1 };
			int   normalTexture{ -1 };
		};

		struct KHR_materials_pbrSpecularGlossiness
		{
			glm::vec4  diffuseFactor{ 1.0f, 1.0f, 1.0f, 1.0f };
			int   diffuseTexture{ -1 };
			glm::vec3  specularFactor{ 1.0f, 1.f, 1.0f };
			float glossinessFactor{ 1.0f };
			int   specularGlossinessTexture{ -1 };
		};

		struct KHR_materials_sheen
		{
			glm::vec3  colorFactor{ 0.0f, 0.0f, 0.0f };
			int   colorTexture{ -1 };
			float roughnessFactor{ 0.0f };
			int   roughnessTexture{ -1 };

		};

		struct KHR_materials_transmission
		{
			float factor{ 0.0f };
			int   texture{ -1 };
		};

		struct KHR_materials_unlit
		{
			int active{ 0 };
		};

		struct KHR_texture_transform
		{
			glm::vec2  offset{ 0.0f, 0.0f };
			float rotation{ 0.0f };
			glm::vec2  scale{ 1.0f };
			int   texCoord{ 0 };
			glm::mat3  uvTransform{ 1 };  // Computed transform of offset, rotation, scale
		};
		KHR_materials_pbrSpecularGlossiness specularGlossiness;
		KHR_texture_transform               textureTransform;
		KHR_materials_clearcoat             clearcoat;
		KHR_materials_sheen                 sheen;
		KHR_materials_transmission          transmission;
		KHR_materials_unlit                 unlit;
	};
	
	struct gltfMesh
	{
		std::vector<gltfPrimitives> primitives;
	};
	std::vector<gltfMesh> meshes;
	std::vector<GLTFMaterial> materials;
	std::vector<sg_image> textures;

	sg_buffer instances_model_matrix, instanceIDs, weights;

	void ProcessPrim(const tinygltf::Model& model, const tinygltf::Primitive& prim, gltfMesh& mesh);
	void ImportMaterials(const tinygltf::Model& model);
	void render_node(int node_idx, std::vector<glm::mat4> node_mat);

	void init_node(int node_idx, glm::mat4 current);
public:
	std::vector<glm::mat4> initial_nodes_mat;
	int morphTargets = 0;
	void render(const glm::mat4& vm, const glm::mat4& pm);
	std::unordered_map<std::string, gltf_object> objects;
	gltf_class(const tinygltf::Model& model, std::string name);
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