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
#include <random>

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

	// pass action clearing to transparent for walkable overlay
	sg_pass_action walkable_passAction;

	struct
	{
		sg_pipeline pip_sky, pip_grid;
		sg_bindings bind;

		sg_pipeline pip_img;
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

	sg_pipeline region3d_pip;
	// region voxel hashed cache (multi-tier)
	sg_image region_cache;    // RGBA32UI, width=2048, height=32 (4 tiers * 8 rows per tier)
	bool region_cache_dirty = false;


	struct {
		sg_pipeline accum_pip, reveal_pip, compose_pip;
		sg_bindings bind;
		sg_pass_action accum_pass_action, reveal_pass_action, compose_pass_action;
	} wboit;

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
	sg_pipeline svg_pip; // Pipeline for SVG rendering

	struct {
		sg_pipeline pip;
	} composer;

	// world-ui part:
	struct {
		sg_pipeline pip_txt;
		sg_pass_action pass_action;
	} world_ui;

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

	// Walkable region cache (RGBA32F, width=4096, height=264)
	sg_image walkable_cache;
	sg_pipeline walkable_pip;
 
	struct
	{
		sg_pipeline pip;

	}grating_display;

	bool allowData = true;

	struct
	{
		glm::vec3 left_eye_world;
		glm::vec3 right_eye_world;
		bool render_left; // true: left, false:right
	} ETH_display;
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

	struct SkyboxImageData {
		sg_image image;
		bool valid = false;
		std::string errorMessage;
	} skybox_image;

	// WBOIT (Weighted Blended Order-Independent Transparency)
	struct {
		sg_image accum, revealage, wboit_composed, w_accum, wboit_emissive;
		sg_pass accum_pass, reveal_pass, compose_pass;
		sg_bindings compose_bind;
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

	sg_image ui_selection, bordering, bloom1, bloom2, shine2;

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
		sg_pass pass, stat_pass, svg_pass;
		sg_image viewed_rgb, occurences;
	} sprite_render;

	struct {
		sg_pass pass;
		sg_image depthTest;
	} world_ui;

	struct {
		sg_image low; // w/4 x h/4 RGBA8 overlay
		sg_pass pass;
	} walkable_overlay;
 
	struct {
		sg_pass pass;
		sg_pass_action pass_action;
	} region3d;

	sg_image temp_render, temp_render_depth;// , final_image;
	sg_pass temp_render_pass, msaa_render_pass;

	GroundGrid grid;

	bool inited = false;
	disp_area_t disp_area={0,0};

	// aux:
	bool use_paint_selection = false;

	struct
	{
		int eye_id; //(0,1), (2,3), ...
	}ETH_display;
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
me_special* camera_object;

// everything in messyengine is a me_obj, even if whatever.
//me_obj move to cycleui.h

struct me_localmap;
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

	// slam/local map extensions
	int pc_type = 0; // 0: normal_map, 1: local_map, 2: slam_map
	me_localmap* localmap;
};
indexier<me_pcRecord> pointclouds;
struct me_localmap
{
	int idx;
	std::vector<float> angular256; // for slam_map: 256 bins, 0.1m unit, 65535 means empty
};
FixedPool<me_localmap*> local_maps(16384);

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

struct me_region_cloud_bunch : me_obj
{
	const static int type_id = 6;
	std::vector<packed_region3d_t> items;
};
indexier<me_region_cloud_bunch> region_cloud_bunches;

// dedicate put line prop.
struct me_line_piece : me_obj
{
	const static int type_id = 2;
	char flags[MAX_VIEWPORTS] = { 0 };

	reference_t propSt, propEnd;
	//me_obj *propSt=nullptr, *propEnd=nullptr;
	enum line_type{ straight, bezier};
	line_type type = straight;

	// bezier:
	std::vector<glm::vec3> ctl_pnt;

	gpu_line_info attrs; // for seperated put line
	~me_line_piece()
	{
		propSt.remove_from_obj();
		propEnd.remove_from_obj();
	}
};
indexier<me_line_piece> line_pieces;

///====*********************************************************************************************************

// rgba is a resource kind... used in sprites. 
struct me_rgba:self_idref_t
{
	int width=0, height=0, atlasId=-1, loadLoopCnt;
	int type = 0; // display type. 0: normal, 1: stereo_3d_lr.
	bool loaded, invalidate, streaming;
	glm::vec2 uvStart;
	glm::vec2 uvEnd;

	// temporary:
	int occurrence;
};

struct
{
	sg_image atlas; //array of atlas. each of 4096 sz. at most 16.
	std::vector<int> usedPixels;
	int atlasNum;
	indexier<me_rgba> rgbas;
} argb_store;
void process_argb_occurrence(const float* data, int ww, int hh);
struct shuffle { glm::vec4 src, dst; };

// Include nanosvg and earcut headers
#define NANOSVG_IMPLEMENTATION

#include "lib/nanosvg/nanosvg.h"
#include "lib/nanosvg/earcut.hpp"

// Structure to store SVG data
struct gpu_svg_struct;
struct me_svg {
	std::string content;
	int occurrence = 0;

	bool loaded = false;
	int triangleCnt = 0;
	sg_buffer svg_pos_color = { 0 }; // Interleaved buffer for position and color

	std::vector<gpu_svg_struct> svg_params;
};

// Global store for SVG objects
indexier<me_svg> svg_store;

// sprite for display RGBA, it's picturebox
struct me_sprite : me_obj
{
	const static int type_id = 3;

	me_rgba* rgba; // res type obj will never die.
	me_svg* svg;

	enum sprite_type { rgba_t, svg_t } type;
	
	std::string resName;
	glm::vec2 dispWH;
	glm::vec3 pixel_offset_rot;
	int shineColor = 0xffffffff;

	int display_flags; // border, shine, front, selected, hovering, loaded,
		// display type(0 normal, 1 billboard), no attenuation(1: distance don't affect sprite size, 0: affect.)


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
	glm::vec2 rgbid;
};

// SVG sprite GPU data structure
struct gpu_svg_struct
{
	glm::vec3 translation;
	float flag;
	glm::quat quaternion;
	glm::vec2 dispWH;
	int myshine;
	glm::vec2 info;  // First value can store extra flags, second can store instance ID
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
	unsigned int shineColor; // all used. //todo: just use 0xFFF, so we can save 20bit.
	unsigned int flag; //0:border, 1:shine, 2:front, 3:selected, 4:hovering, 5:ignore cross-section, 6:add normal shading,
	//bits 8-15 reserved for transparency (8-bit value from 0-255)
};

// can only select one sub for gltf_object.
// shine border bringtofront only apply to leaf node.
struct gltf_object : me_obj
{
	const static int type_id = 1000;

	bool anim_switch = false; // if set new animation, we should switch.
	bool anim_switch_asap = false;
	int baseAnimId=-1, playingAnimId=-1, nextAnimId=-1;
	bool baseAnimStopAtEnd = false, playingAnimStopAtEnd = false, nextAnimStopAtEnd = false;
	// if currently playing is final, switch to nextAnim, and nextAnim:=baseAnim
	// -1 if no animation.
	long animationStartMs; // in second.
	// todo: consider animation blending.

	int material_variant; // KHR_materials_variants
	unsigned int team_color;

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

struct gltf_class:self_idref_t
{
public:
	struct tex_info
	{
		glm::vec4 texcoord{0}; // uv.xy for base color, uv.zw for emissive
		glm::vec4 atlasinfo{0}; // base color atlas info
		glm::vec4 em_atlas{0}; // emissive atlas info
		glm::vec2 tex_weight{0}; // .x for basecolor, .y for emissive

		// any more texture add here.
	};
	struct v_node_info
	{
		float node_id; // fuck, vertex can only have float.
		char skin_idx; // it can't be > 255 skin isn't it?
		char env_intensity; // we're not PBR, just show the approximately correct effect:
			// if high metalness and low roughness, it should reflect envmap to prevent "totally black"
		char dummy1;
		char dummy2;
	};

	struct temporary_buffer
	{
		// per vertex:
		std::vector<int> indices;
		std::vector<glm::vec3> position, normal;
		std::vector<glm::u8vec4> color;
		std::vector<tex_info> tex;
		std::vector<v_node_info> node_meta; //node_id, skin_idx(-1 if NA).

		std::vector<glm::vec4> joints;
		std::vector<glm::vec4> jointNodes;
		std::vector<glm::vec4> weights;

		// instance shared:
		std::vector<int> raw_parents, all_parents;
		std::vector<glm::mat4> localMatVec;
		std::vector<glm::vec3> it;
		std::vector<glm::quat> ir;
		std::vector<glm::vec3> is;

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
	
	sg_buffer indices, positions, normals, colors, texs, node_metas, joints, jointNodes, weights;

	//sg_image morph_targets
	void load_primitive(int node_idx, temporary_buffer& tmp);
	void process_primitive(const tinygltf::Primitive& prim, int node_idx, temporary_buffer& tmp);

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
	float normal_shading = 0;

	// rendering variables:
	int instance_offset; int node_offset;

	// statics:
	tinygltf::Model model;
	SceneDimension sceneDim;
	glm::mat4 i_mat; //centralize and swap z
	// std::vector<bool> important_node;
	unsigned char nodeMatSelector = 0; //???

	int morphTargets = 0;

	glm::vec3 color_bias;
	float color_scale;
	float brightness = 1;

	void render(const glm::mat4& vm, const glm::mat4& pm, const glm::mat4& iv, bool shadow_map, int offset, int class_id);
	void wboit_reveal(const glm::mat4& vm, const glm::mat4& pm, int offset, int class_id);
	void wboit_accum(const glm::mat4& vm, const glm::mat4& pm, int offset, int class_id);

	int count_nodes();
	void prepare_data(std::vector<s_pernode>& tr_per_node, std::vector<s_perobj>& per_obj, int offset_node, int offset_instance); // return new offset, also perform 4 depth hierarchy.
	void compute_node_localmat(const glm::mat4& vm, int offset);
	void node_hierarchy(int offset, int pass); // perform 4 depth hierarchy.

	indexier<gltf_object> objects;

	int list_objects();

	int opaques = 0;
	bool has_blending_material = false;

	std::vector<gltf_object*> showing_objects[MAX_VIEWPORTS]; // refereshed each iteration.
	
	std::map<std::string, int> name_nodeId_map;
	std::map<int, std::string> nodeId_name_map;
	std::vector<AnimationDefine> animations;

	// first rotate, then scale, finally center.
	void apply_gltf(const tinygltf::Model& model, std::string name, glm::vec3 center,
		float scale, glm::quat rotate);
	void clear_me_buffers();

	inline static int max_passes = 0 ;
};

indexier<gltf_class> gltf_classes;



// ██    ██  ██  
// ██    ██  ██  
// ██    ██  ██  
// ██    ██  ██  
//  ██████   ██  

struct gpu_text_quad
{
	glm::vec3 position;     // World position anchor
	float rad;              // Rotation in rad, flip if up-side down..
	glm::vec2 size;         // Size in pixels
	uint32_t text_color;    // Text color (RGBA)
	uint32_t bg_color;      // Background/handle color (RGBA)
	glm::vec2 uv_min;       // Character UV min in font atlas
	glm::vec2 uv_max;       // Character UV max in font atlas
	int8_t glyph_x0, glyph_y0, glyph_x1, glyph_y1; // Glyph coordinates from ImGui Font Atlas
	uint8_t bbx, bby;
	glm::vec2 flags;            // Bit flags: 0x01=border, 0x02=shine, 0x04=front, 0x08=selected, 0x10=hovering, 0x20=use screen cord, 0x40=has_arrow
	glm::vec2 offset;
};

struct me_world_ui:me_obj
{
	const static int type_id = 4;
	bool selectable[MAX_VIEWPORTS] = { false };
	bool selected[MAX_VIEWPORTS] = { false };
	virtual void remove() = 0;
};

// spot texts, special kind, only added via painter.drawtext....
struct stext
{
	glm::vec3 position; //or screen ratio.
	glm::vec2 ndc_offset, pixel_offset; //will multiply by dpi.
	glm::vec2 pivot;
	std::string text;
	uint32_t color;
	// todo: remove this, bad behavious.
	unsigned char header; //0:have world pos, 1: have screen ratio pos, 2: have screen pixel offset, 3: have pivot. 4: have relative.
	reference_t relative; //transform my position to whom? nullptr for absolute. need to check this if workspace prop is removed.

	~stext()
	{
		relative.remove_from_obj();
	}
};
struct me_stext;
indexier<me_stext> spot_texts;
struct me_stext : me_world_ui
{
	std::vector<stext> texts;
	void remove() override { spot_texts.remove(this->name); };
};

struct me_handle_icon;
indexier<me_handle_icon> handle_icons;
struct me_handle_icon :me_world_ui {
public:
	std::string name;
	float size;
	std::string icon;
	uint32_t txt_color;       // Text color
	uint32_t bg_color; // Handle background color
	bool show[MAX_VIEWPORTS] = { true };

	void remove() override { handle_icons.remove(this->name); };
};


// Text Along Line implementation
struct me_text_along_line;
indexier<me_text_along_line> text_along_lines;
struct me_text_along_line :me_world_ui {
public:
	std::string name;
	glm::vec3 direction;
	std::string text;

	float size, voff;
	bool bb;
	uint32_t color;

	bool show[MAX_VIEWPORTS] = { true };

	reference_t direction_prop;

	~me_text_along_line()
	{
		direction_prop.remove_from_obj();
	}
	void remove() override { text_along_lines.remove(this->name); };
};









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

bool viewport_test_prop_display(me_obj* obj);

// graphics tuning:
float GLTF_illumfac = 8.5f;
float GLTF_illumrng = 1.1f;
