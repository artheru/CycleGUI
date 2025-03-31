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
#define MSAA 1
#define atlas_sz 4096

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

#include "lib/rectpack2d/finders_interface.h"

using spaces_type = rectpack2D::empty_spaces<false, rectpack2D::default_empty_spaces>;
using rect_type = rectpack2D::output_rect_t<spaces_type>;


// ███    ███ ███████         ██   ██ ███████  █████  ██████  ███████ ██████  ███████
// ████  ████ ██              ██   ██ ██      ██   ██ ██   ██ ██      ██   ██ ██
// ██ ████ ██ █████           ███████ █████   ███████ ██   ██ █████   ██████  ███████
// ██  ██  ██ ██              ██   ██ ██      ██   ██ ██   ██ ██      ██   ██      ██
// ██      ██ ███████ ███████ ██   ██ ███████ ██   ██ ██████  ███████ ██   ██ ███████

#include <chrono>

#include "shaders/shaders.h"

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

static struct
{
	sg_buffer quad_vertices, uv_vertices;
	sg_pass_action default_passAction;
	sg_image dummy_tex;

	struct
	{
		sg_pipeline pip_sky, pip_grid;
		sg_bindings bind;
	} skybox;
	
	struct {
		sg_pass_action pass_action;
		sg_pipeline line_bunch_pip;
	} line_bunch; // draw points, doesn't need binding.
	
	struct
	{
		// generate ssao
		sg_pipeline pip;
		sg_pass_action pass_action;
	} ssao;
	
	struct {
		sg_pipeline pip_border, pip_dilateX, pip_dilateY, pip_blurX, pip_blurYFin;
	} ui_composer;
	

	struct {
		sg_pipeline spotlight_pip, cs_ssr_pip;
		sg_pass_action pass_action;
		sg_bindings spotlight_bind;
	} ground_effect;
	
	struct {
		sg_pass_action quad_pass_action, stat_pass_action;
		sg_pipeline quad_image_pip, stat_pip;
	} sprite_render;

	struct
	{
		sg_pipeline pip_rgbdraw, pip_blend, pip_imgui;
		sg_bindings bind;
	} utilities;

	
	struct {
		sg_pipeline pip;
		sg_pass_action pass_action;
	} edl_lres;

	struct CustomShaderData {
		std::string code;
		sg_shader shader;
		sg_pipeline pipeline;
		bool valid = false;
		std::string errorMessage;
	} custom_bg_shader;

	sg_pipeline point_cloud_simple_pip;
	sg_pipeline point_cloud_composer_pip;
	sg_pipeline grid_pip;

	struct {
		sg_pipeline pip;
	} composer;

	struct
	{
		// Z means 1,2,3,4,5.....
		sg_buffer Z;

		sg_pipeline animation_pip, hierarchy_pip, finalize_pip; // animation already consider world position.
		sg_image objInstanceNodeMvMats1, objInstanceNodeNormalMats,
			objInstanceNodeMvMats2, 
			instance_meta;
		sg_pass animation_pass; sg_pass_action pass_action, hierarchy_pass_action;

		sg_pass hierarchy_pass1;
		sg_pass hierarchy_pass2;

		sg_pass final_pass;

		sg_image node_meta, node_meta_shine;
	} instancing;

	sg_pipeline gltf_pip;

	struct
	{
		sg_pipeline pip;

	}grating_display;

	bool allowData = true;
} shared_graphics;

struct per_viewport_states {
	struct {
		sg_image color, depthTest, depth, normal, position;
		sg_pass pass;
		sg_pass_action pass_action;
	} primitives; // draw points, doesn't need binding.

	struct {
		sg_image shadow_map, depthTest;
		sg_pass pass;
		sg_pass_action pass_action;
	} shadow_map;
	sg_pipeline shadow_pip;

	struct {
		sg_image color, depth;
		sg_pass pass;
		sg_bindings bind;
	} edl_lres;

	struct {
		sg_image depth;
		sg_pass pass;
		sg_pass_action pass_action;
	} pc_primitive; // draw points, doesn't need binding.


	// WBOIT (Weighted Blended Order-Independent Transparency)
	struct {
		sg_image accum, revealage;
		sg_pipeline composite_pip;
		sg_bindings bind;
		sg_pass_action pass_action;
	} wboit;

	struct {
		sg_bindings cs_ssr_bind;
		sg_image ground_img;
		sg_pass pass;
		sg_bindings bind;
	} ground_effect;

	struct {
		sg_bindings bind;
	} composer;

	struct {
		sg_pass shine_pass1to2, shine_pass2to1;
		sg_bindings border_bind;
	} ui_composer;

	struct
	{
		// generate ssao
		sg_bindings bindings;
		sg_pass pass;
		sg_image image;
	} ssao;

	sg_image TCIN; //type/class-instance-nodeid.

	sg_image ui_selection, bordering, bloom, shine2;

	struct
	{
		sg_image temporary_1; //scale downed image, bloom->temporary_1->temporary_2->1->screen composing.
		sg_image temporary_2;
	} bloom_effect;

	struct {
		sg_pass pass;
	} line_bunch; // draw points, doesn't need binding.

	struct
	{
		sg_pass pass;
		sg_pass_action pass_action;
	} line_pieces;

	struct {
		sg_pass pass, stat_pass;
		sg_image viewed_rgb, occurences;
	} sprite_render;

	sg_image temp_render, temp_render_depth;// , final_image;
	sg_pass temp_render_pass, msaa_render_pass;

	GroundGrid grid;

	bool inited = false;
	disp_area_t disp_area={0,0};

	// aux:
	bool use_paint_selection = false;
};

per_viewport_states graphics_states[MAX_VIEWPORTS + 1]; // the extra one for VR/eyetrack-holo

per_viewport_states* working_graphics_state;
viewport_state_t* working_viewport;
int working_viewport_id;





void GenPasses(int w, int h);
void ResetEDLPass();


// ██     ██  ██████  ██████  ██   ██ ███████ ██████   █████   ██████ ███████     ██ ████████ ███████ ███    ███ ███████ 
// ██     ██ ██    ██ ██   ██ ██  ██  ██      ██   ██ ██   ██ ██      ██          ██    ██    ██      ████  ████ ██      
// ██  █  ██ ██    ██ ██████  █████   ███████ ██████  ███████ ██      █████       ██    ██    █████   ██ ████ ██ ███████ 
// ██ ███ ██ ██    ██ ██   ██ ██  ██       ██ ██      ██   ██ ██      ██          ██    ██    ██      ██  ██  ██      ██ 
//  ███ ███   ██████  ██   ██ ██   ██ ███████ ██      ██   ██  ██████ ███████     ██    ██    ███████ ██      ██ ███████ 
// Workspace Items                                                                                                                  

indexier<namemap_t> global_name_map;

struct me_special:me_obj
{
	const static int type_id = -1;
};
indexier<me_special> special_objects;
me_special* mouse_object;

// everything in messyengine is a me_obj, even if whatever.
//me_obj move to cycleui.h

struct me_pcRecord : me_obj
{
	const static int type_id = 1;
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

	glm::vec4 shine_color; //todo: change to uint.
	//unsigned char displaying.
};
indexier<me_pcRecord> pointclouds;

///====*********************************************************************************************************

// spot texts, special kind, only added via painter.drawtext....
struct stext
{
	glm::vec3 position; //or screen ratio.
	glm::vec2 ndc_offset, pixel_offset; //will multiply by dpi.
	glm::vec2 pivot;
	std::string text;
	uint32_t color;
	unsigned char header; //0:have world pos, 1: have screen ratio pos, 2: have screen pixel offset, 3: have pivot. 4: have relative.
	me_obj* relative; //transform my position to whom? nullptr for absolute. need to check this if workspace prop is removed.
};
struct me_stext : me_obj
{
	const static int type_id = 4;
	std::vector<stext> texts;
};

indexier<me_stext> spot_texts;

///====*********************************************************************************************************

// line, two kinds: linebunch(a lot of lines, put via painter), line piece(single line, put via workspace.prop.add).
struct gpu_line_info
{
	glm::vec3 st, end;
	unsigned char arrowType, dash, width, flags;//flags:border, shine, front, selected, hover | [selectable(not used on gpu)]
	unsigned int color;
	float f_lid;
	// todo: maybe, add a glm::vec2 to indicate tail direction to avoid "broken curve"
};

// line bunch, also special, add via painter.drawline.
struct me_linebunch: me_obj
{
	const static int type_id = 5;
	sg_buffer line_buf; // the buffer is directly filled with gpu_line_infop, via AppendToLineBunch.
	int capacity, n;
};
indexier<me_linebunch> line_bunches; // line bunch doesn't remove, only clear. it mainly used for painter draw.

// dedicate put line prop.
struct me_line_piece : me_obj
{
	const static int type_id = 2;
	me_obj *propSt=nullptr, *propEnd=nullptr;
	enum line_type{ straight, bezier, arc};
	line_type type = straight;

	// bezier:
	std::vector<glm::vec3> ctl_pnt;

	gpu_line_info attrs; // for seperated put line
};
indexier<me_line_piece> line_pieces;

///====*********************************************************************************************************

// rgba is a resource kind... used in sprites. 
struct me_rgba:self_idref_t
{
	int width, height, atlasId=-1, loadLoopCnt;
	bool loaded, invalidate, streaming;
	glm::vec2 uvStart;
	glm::vec2 uvEnd;

	// temporary:
	int occurrence;
};

struct
{
	sg_image atlas; //array of atlas. each of 4096 sz.
	std::vector<int> usedPixels;
	int atlasNum;
	indexier<me_rgba> rgbas;

} argb_store;


// sprite for display RGBA, it's picturebox
struct me_sprite : me_obj
{
	const static int type_id = 3;

	me_rgba* rgba;
	std::string rgbaName;
	glm::vec2 dispWH;
	glm::vec3 pixel_offset_rot;
	int shineColor = 0xffffffff;

	int display_flags; // border, shine, front, selected, hovering, loaded, display type
	// display type list:
	// 0: normal world,
	// 1: billboard world -> pixel.
	// 2: billboard world -> ndc.

	unsigned char per_vp_stat[MAX_VIEWPORTS] = { 0 };  // selectable, selected.
};
indexier<me_sprite> sprites;

struct gpu_sprite
{
	glm::vec3 translation;
	float flag;
	glm::quat quaternion;
	glm::vec2 dispWH;
	glm::vec2 uvLeftTop, RightBottom;
	int myshine;
	float rgbid;
};

///====*********************************************************************************************************

// geometry. todo....
struct me_geometry3d : me_obj
{
	const static int type_id = 5;
    virtual void applyArguments() = 0;
};
struct me_box_geometry:me_geometry3d
{
	int w, h;

};
indexier<me_geometry3d> geometries;


///====*********************************************************************************************************
///
///   GLTF/MESH.

struct gltf_class;

struct s_pernode //4*4*2=32bytes per node.
{
	glm::vec3 translation;
	float flag; 
	glm::quat quaternion = glm::quat(1, 0, 0, 0);
};

struct s_perobj //4*3=12Bytes per instance.
{
	unsigned int anim_id;  //32bit, won't use this much.
	unsigned int elapsed;  //32bit, actually won't use all

	//unsigned int animation; //anim_id:8bit, elapsed:24bit,
	//unsigned int morph; //manual morphing idx.
	unsigned int shineColor; // all used.
	unsigned int flag; //border, shine, front, selected, hovering, ignore cross-section
	//bits 8-15 reserved for transparency (8-bit value from 0-255)
};

// can only select one sub for gltf_object.
// shine border bringtofront only apply to leaf node.
struct gltf_object : me_obj
{
	const static int type_id = 1000;

	int baseAnimId=-1, playingAnimId=-1, nextAnimId=-1;
	// if currently playing is final, switch to nextAnim, and nextAnim:=baseAnim
	// -1 if no animation.
	long animationStartMs; // in second.
	// todo: consider animation blending.

	std::vector<s_pernode> nodeattrs;
	//translation+flag+rotation, not applicable for root node (root node directly use me_obj's trans+rot)
	// nodemeta: 0:border, 1: shine, 2: front, 3:selected. ... 8bit~19bit:shine-color.

	int shine = 0;
	int flags[MAX_VIEWPORTS]={0};
		// flag: 0:border, 1: shine, 2: bring to front, 3: selected(as whole), 4:selectable, 5: subselectable, 6:sub-selected. 7:ignore cross-section
				 // 8-15: transparency (8-bit value from 0-255, default 255)
	int gltf_class_id;
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

struct gltf_class:self_idref_t
{
public:
	struct vertex_info
	{
		glm::vec2 texcoord{0};
		glm::vec4 atlasinfo{0};
	};

	struct temporary_buffer
	{
		// per vertex:
		std::vector<int> indices;
		std::vector<glm::vec3> position, normal;
		std::vector<glm::vec4> color;
		std::vector<vertex_info> texcoord;
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
private:
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

	// sg_buffer originalLocals;

	sg_buffer itrans, irot, iscale;

	//sg_image instanceData; // uniform samplar, x:instance, y:node, (x,y)->data
	//sg_image node_mats, NImodelViewMatrix, NInormalMatrix;
	
	sg_buffer indices, positions, normals, colors, texcoords, node_metas, joints, jointNodes, weights;

	//sg_image morph_targets
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
	// property
	bool dbl_face = false;

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
	void prepare_data(std::vector<s_pernode>& tr_per_node, std::vector<s_perobj>& per_obj, int offset_node, int offset_instance); // return new offset, also perform 4 depth hierarchy.
	void compute_node_localmat(const glm::mat4& vm, int offset);
	void node_hierarchy(int offset, int pass); // perform 4 depth hierarchy.

	indexier<gltf_object> objects;

	int list_objects();
	std::vector<gltf_object*> showing_objects; // refereshed each iteration.
	std::vector<std::string*> showing_objects_name; // refereshed each iteration.

	std::map<std::string, int> name_nodeId_map;
	std::map<int, std::string> nodeId_name_map;
	std::vector<AnimationDefine> animations;

	// first rotate, then scale, finally center.
	void apply_gltf(const tinygltf::Model& model, std::string name, glm::vec3 center, float scale, glm::quat rotate);
	void clear_me_buffers();

	inline static int max_passes = 0 ;
};

indexier<gltf_class> gltf_classes;


///====*********************************************************************************************************
///








// ██    ██ ████████ ██ ██      ██ ████████ ██    ██ 
// ██    ██    ██    ██ ██      ██    ██     ██  ██  
// ██    ██    ██    ██ ██      ██    ██      ████   
// ██    ██    ██    ██ ██      ██    ██       ██    
//  ██████     ██    ██ ███████ ██    ██       ██    


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