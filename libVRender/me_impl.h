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
#ifndef __EMSCRIPTEN__
#define TINYGLTF_ENABLE_DRACO
#endif
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

#include <chrono>

#include "shaders/point_cloud_simple.h"
#include "shaders/ground_plane.h"
#include "shaders/dbg.h"
#include "shaders/depth_blur.h"
#include "shaders/gltf.h"
#include "shaders/gltf_a.h"
#include "shaders/composer.h"
#include "shaders/ground_reflection.h"

#ifdef _MSC_VER 
#define sprintf sprintf_s
#endif

// math part:

template <typename Type>
Type Lerp(Type prev, Type next, const float keyframe)
{
	return (1.0f - keyframe) * prev + keyframe * next;
}

template <typename Type>
Type SLerp(Type prev, Type next, const float keyframe)
{
	float dotProduct = glm::dot(prev, next);

	//! Make sure we take the shortest path in case dot product is negative
	if (dotProduct < 0.0)
	{
		next = -next;
		dotProduct = -dotProduct;
	}

	//! If the two quaternions are too close to each other, just linear interpolate between the 4D vector
	if (dotProduct > 0.9995)
		return glm::normalize((1.0f - keyframe) * prev + keyframe * next);

	//! Perform the spherical linear interpolation
	float theta0 = std::acos(dotProduct);
	float theta = keyframe * theta0;
	float sinTheta = std::sin(theta);
	float sinTheta0Inv = 1.0 / (std::sin(theta0) + 1e-6);

	float scalePrevQuat = std::cos(theta) - dotProduct * sinTheta * sinTheta0Inv;
	float scaleNextQuat = sinTheta * sinTheta0Inv;
	return scalePrevQuat * prev + scaleNextQuat * next;
}

//
//
// namespace ozz
// {
// 	namespace animation
// 	{
// 		class Animation;
// 	}
// }

static struct {
	struct {
		sg_image color, depthTest, depth, normal, position;
		sg_pass pass;
		sg_pass_action pass_action;
	} primitives; // draw points, doesn't need binding.

	struct
	{
		// Z means 1,2,3,4,5.....
		sg_buffer Z;

		sg_pipeline animation_pip, hierarchy_pip, finalize_pip; // animation already consider world position.
		sg_image objInstanceNodeMvMats1, objInstanceNodeNormalMats,
			objInstanceNodeMvMats2, 
			//objFlags, //objShineIntensities,
			instance_meta;
		std::vector<int> objOffsets;
		sg_pass animation_pass; sg_pass_action pass_action;

		sg_pass hierarchy_pass1;
		sg_pass hierarchy_pass2;

		sg_pass final_pass;

		sg_image node_meta, node_meta_shine;
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
		sg_pipeline pip;
		sg_bindings bind;
	} ground_effect;

	struct {
		sg_pipeline pip;
		sg_bindings bind;
	} composer;

	sg_buffer quad_vertices;

	struct {
		sg_pipeline pip_border, pip_dilateX, pip_dilateY, pip_blurX, pip_blurYFin;
		sg_pass shine_pass1to2, shine_pass2to1;
		sg_bindings border_bind;
	} ui_composer;

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


	sg_pass_action default_passAction;
	sg_image TCIN; //type/class-instance-nodeid.

	sg_image ui_selection, bordering, shine1, shine2;

	sg_image dummy_tex;
} graphics_state;

Camera* camera;
GroundGrid* grid;


sg_pipeline point_cloud_simple_pip;
sg_pipeline point_cloud_composer_pip;

sg_shader ground_shader;
sg_pipeline ground_pip;


void GenPasses(int w, int h);
void ResetEDLPass();

struct me_obj
{
	std::string name;
	glm::vec3 position = glm::zero<glm::vec3>();
	glm::quat quaternion = glm::identity<glm::quat>();
};

struct me_pcRecord : me_obj
{
	bool isVolatile;
	int capacity, n;
	sg_buffer pcBuf;
	sg_buffer colorBuf;
	sg_image pcSelection;
	unsigned char* cpuSelection;

	int flag;
	//0:border, 1: shine, 2: bring to front,
	//3:show handle, 4: can select by point, 5: can select by handle, 6: currently selected as whole,
	// 7: selectable, 8: sub-selectable. 9: currently sub-selected.

	int handleType = 0; //bit0:rect, bit1: circle, bit2: cross

	glm::vec4 shine_color;
	//unsigned char displaying.
};

struct stext
{
	glm::vec3 position;
	std::string text;
	uint32_t color;
};
struct me_stext
{
	std::vector<stext> texts;
};

template <typename T>
struct indexier
{
	std::unordered_map<std::string, int> name_map;
	std::vector<std::tuple<T*, std::string>> ls;

	int add(std::string name, T* what)
	{
		remove(name);
		name_map[name] = ls.size();
		ls.push_back(std::tuple<T*, std::string>(what, name));
		return ls.size() - 1;
	}

	void remove(std::string name)
	{
		auto it = name_map.find(name);
		if (it != name_map.end()) {
			delete std::get<0>(ls[it->second]);
			if (ls.size() > 1) {
				// move last element to current pos.
				auto tup = ls[ls.size() - 1];
				ls[it->second] = tup;
				ls.pop_back();
				name_map[std::get<1>(tup)] = it->second;
			}
		}
		name_map.erase(name);
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

	T* get(int id)
	{
		return std::get<0>(ls[id]);
	}
};

struct namemap_t
{
	int type; // same as selection.
	int instance_id;
	me_obj* obj;
};
indexier<namemap_t> name_map;

indexier<me_pcRecord> pointclouds;


indexier<me_stext> spot_texts;

struct
{
	float sun_altitude;
} scene;

class gltf_class;

struct s_pernode //4*4*2=32bytes per node.
{
	glm::vec3 translation;
	float flag; 
	glm::quat quaternion = glm::quat(1, 0, 0, 0);
};

struct s_perobj //4*3=12Bytes per instance.
{
	unsigned int anim_id; 
	unsigned int elapsed;  //32bit/65s

	//unsigned int animation; //anim_id:8bit, elapsed:24bit,
	//unsigned int morph; //manual morphing idx.
	unsigned int shineColor; //
	unsigned int flag;
};

// can only select one sub for gltf_object.
// shine border bringtofront only apply to leaf node.
struct gltf_object : me_obj
{
	glm::vec3 cur_translation;
	glm::quat cur_rotation;
	float target_start_time, target_require_completion_time;
	
	int baseAnimId, playingAnimId, nextAnimId; // if currently playing is final, switch to nextAnim, and nextAnim:=baseAnim
	long animationStartMs; // in second.
	// todo: consider animation blending.

	std::vector<s_pernode> nodeattrs;
	//translation+flag+rotation, not applicable for root node (root node directly use me_obj's trans+rot)
	// nodemeta: 0:border, 1: shine, 2: front, 3:selected. ... 8bit~19bit:shine-color.

	int shine = 0;
	int flags = 0;
	// todo: remove this.
	// int shineColor[8];
	// flag: 0:border, 1: shine, 2: bring to front, 3: selected(as whole), 4:selectable, 5: subselectable, 6:sub-selected.
	
	gltf_object(gltf_class* cls);
};


namespace GLTFExtension
{
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

	bool CheckRequiredExtensions(const tinygltf::Model& model);
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
	GLTFExtension::KHR_materials_pbrSpecularGlossiness specularGlossiness;
	GLTFExtension::KHR_texture_transform               textureTransform;
	GLTFExtension::KHR_materials_clearcoat             clearcoat;
	GLTFExtension::KHR_materials_sheen                 sheen;
	GLTFExtension::KHR_materials_transmission          transmission;
	GLTFExtension::KHR_materials_unlit                 unlit;
};

#include "lib/rectpack2d/finders_interface.h"

using spaces_type = rectpack2D::empty_spaces<true, rectpack2D::default_empty_spaces>;
using rect_type = rectpack2D::output_rect_t<spaces_type>;
class gltf_class
{
	bool mutable_nodes = true; // make all nodes localmat=worldmat

	std::string name;
	int n_indices = 0;
	int totalvtx = 0;
	int maxdepth = 0, iter_times=0, passes=0;

	std::vector<std::tuple<int, int, int>> node_ctx_id; //vcnt, nodeid, vstart
	
	sg_image animap, animtimes, animdt, morphdt;
	sg_image skinInvs;
	
	sg_image parents; //row-wise, round1{node1p,node2p,...},round2....
	sg_image atlas;

	int basecolortex = -1;//todo.

	sg_buffer originalLocals;

	sg_buffer itrans, irot, iscale;

	//sg_image instanceData; // uniform samplar, x:instance, y:node, (x,y)->data
	//sg_image node_mats, NImodelViewMatrix, NInormalMatrix;
	
	sg_buffer indices, positions, normals, colors, texcoords, node_metas, joints, jointNodes, weights;
	
	//sg_image morph_targets
	struct temporary_buffer
	{
		// per vertex:
		std::vector<int> indices;
		std::vector<glm::vec3> position, normal;
		std::vector<glm::vec4> color;
		std::vector<glm::vec2> texcoord;
		std::vector<glm::vec2> node_meta; //node_id, skin_idx(-1 if NA).

		std::vector<glm::vec4> joints;
		std::vector<glm::vec4> jointNodes;
		std::vector<glm::vec4> weights;

		// instance shared:
		std::vector<int> raw_parents, all_parents;
		std::vector<glm::mat4> localMatVec;
		std::vector<glm::vec3> it;
		std::vector<glm::quat> ir;
		std::vector<glm::vec3> is;
		
		std::vector<GLTFMaterial> _sceneMaterials;
		std::vector<glm::vec3> morphtargets;

		// temporary:
		int atlasW, atlasH;
		std::vector< rectpack2D::output_rect_t<spaces_type>> rectangles;
		std::vector<glm::mat4> skins;
	};
	void load_primitive(int node_idx, temporary_buffer& tmp);
	void import_material(temporary_buffer& tmp);

	// returns if it has mesh children, i.e. important routing.s
	bool init_node(int node_idx, std::vector<glm::mat4>& writemat, std::vector<glm::mat4>& readmat, int parent_idx, int depth, temporary_buffer& tmp);

	void countvtx(int node_idx);
	void CalculateSceneDimension();

	struct SceneDimension
	{
		glm::vec3 center = { 0.0f, 0.0f, 0.0f };
		float radius{ 0.0f };
	};
	struct AnimationDefine
	{
		std::string name;
		long duration;
	};

public:
	// rendering variables:
	int instance_offset; int node_offset;

	// statics:
	tinygltf::Model model;
	SceneDimension sceneDim;
	glm::mat4 i_mat; //centralize and swap z
	// std::vector<bool> important_node;
	unsigned char nodeMatSelector = 0;

	int morphTargets = 0;

	void render(const glm::mat4& vm, const glm::mat4& pm, bool shadow_map, int offset, int class_id);

	int count_nodes();
	void prepare_data(std::vector<s_pernode>& tr_per_node, std::vector<s_perobj>& animation_info, int offset_node, int offset_instance); // return new offset, also perform 4 depth hierarchy.
	void compute_node_localmat(const glm::mat4& vm, int offset);
	void node_hierarchy(int offset, int pass); // perform 4 depth hierarchy.

	indexier<gltf_object> objects;
	std::map<std::string, int> name_nodeId_map;
	std::map<int, std::string> nodeId_name_map;
	std::vector<AnimationDefine> animations;

	// first rotate, then scale, finally center.
	gltf_class(const tinygltf::Model& model, std::string name, glm::vec3 center, float scale, glm::quat rotate);

	inline static int max_passes = 0 ;
};

indexier<gltf_class> gltf_classes;


// struct gltf_displaying_t
// {
// 	std::vector<int> flags; //int
// 	std::vector<int> shine_colors; // rgba
// } gltf_displaying;

#ifdef __EMSCRIPTEN__
EM_JS(void, melog, (const char* c_str), {
	const str = UTF8ToString(c_str);console.log(str);
	});
#endif

// checkGLError(__FILE__, __LINE__);
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