//! KHR extension list (https://github.com/KhronosGroup/glTF/tree/master/extensions/2.0/Khronos)
#define KHR_DARCO_MESH_EXTENSION_NAME "KHR_draco_mesh_compression"
#define KHR_LIGHTS_PUNCTUAL_EXTENSION_NAME "KHR_lights_punctual"
#define KHR_MATERIALS_CLEARCOAT_EXTENSION_NAME "KHR_materials_clearcoat"
#define KHR_MATERIALS_PBR_SPECULAR_GLOSSINESS_EXTENSION_NAME "KHR_materials_pbrSpecularGlossiness"
#define KHR_MATERIALS_SHEEN_EXTENSION_NAME "KHR_materials_sheen"
#define KHR_MATERIALS_TRANSMISSION_EXTENSION_NAME "KHR_materials_transmission"
#define KHR_MATERIALS_UNLIT_EXTENSION_NAME "KHR_materials_unlit"
#define KHR_MATERIALS_VARIANTS_EXTENSION_NAME "KHR_materials_variants"
#define KHR_MESH_QUANTIZATION_EXTENSION_NAME "KHR_mesh_quantization"
#define KHR_TEXTURE_TRANSFORM_EXTENSION_NAME "KHR_texture_transform"
#include <chrono>

template <typename T>
void ReadGLTFData(const tinygltf::Model& model, const tinygltf::Accessor& accessor, std::vector<T>& output)
{
	//! Retrieving the data of the attributes
	const auto& bufferView = model.bufferViews[accessor.bufferView];
	const auto& buffer = model.buffers[bufferView.buffer];
	const T* bufData = reinterpret_cast<const T*>(&(buffer.data[accessor.byteOffset + bufferView.byteOffset]));
	const auto& numElements = accessor.count;

	//! Supporting KHR_mesh_quantization
	assert(accessor.componentType == TINYGLTF_COMPONENT_TYPE_FLOAT);
	output.reserve(numElements);

	if (accessor.componentType == TINYGLTF_COMPONENT_TYPE_FLOAT)
	{
		if (bufferView.byteStride == 0)
		{
			output.insert(output.end(), bufData, bufData + numElements);
		}
		else
		{
			auto bufferByte = reinterpret_cast<const unsigned char*>(bufData);
			for (size_t i = 0; i < numElements; ++i)
			{
				output.push_back(*reinterpret_cast<const T*>(bufferByte));
				bufferByte += bufferView.byteStride;
			}
		}
	}
}

template <typename Type> std::vector<Type> gltfGetVector(const tinygltf::Value& value);
//! Returns a value for a tinygltf::Value
template <typename Type> void gltfGetValue(const tinygltf::Value& value, const std::string& name, Type& val);

template <typename Type>
inline std::vector<Type> gltfGetVector(const tinygltf::Value& value)
{
	std::vector<Type> retVec{ 0 };
	if (value.IsArray() == false)
		return retVec;

	retVec.resize(value.ArrayLen());
	for (int i = 0; i < value.ArrayLen(); ++i)
	{
		retVec[i] = static_cast<Type>(value.Get(i).IsNumber() ? value.Get(i).Get<double>() : value.Get(i).Get<int>());
	}

	return retVec;
}

template <>
inline void gltfGetValue<int>(const tinygltf::Value& value, const std::string& name, int& val)
{
	if (value.Has(name))
	{
		val = value.Get(name).Get<int>();
	}
}

template <> 
inline void gltfGetValue<float>(const tinygltf::Value& value, const std::string& name, float& val)
{
	if (value.Has(name))
	{
		val = static_cast<float>(value.Get(name).Get<double>());
	}
}

template <>
inline void gltfGetValue<glm::vec2>(const tinygltf::Value& value, const std::string& name, glm::vec2& val)
{
	if (value.Has(name))
	{
		auto vec = gltfGetVector<float>(value.Get(name));;
		val = glm::vec2(vec[0], vec[1]);
	}
}

template <>
inline void gltfGetValue<glm::vec3>(const tinygltf::Value& value, const std::string& name, glm::vec3& val)
{
	if (value.Has(name))
	{
		auto vec = gltfGetVector<float>(value.Get(name));;
		val = glm::vec3(vec[0], vec[1], vec[2]);
	}
}

template <>
inline void gltfGetValue<glm::vec4>(const tinygltf::Value& value, const std::string& name, glm::vec4& val)
{
	if (value.Has(name))
	{
		auto vec = gltfGetVector<float>(value.Get(name));;
		val = glm::vec4(vec[0], vec[1], vec[2], vec[3]);
	}
}

inline void gltfGetTextureID(const tinygltf::Value& value, const std::string& name, int& id)
{
	if (value.Has(name))
	{
		id = value.Get(name).Get("index").Get<int>();
	}
}


void gltf_class::load_primitive(int node_idx, temporary_buffer& tmp)
{
	auto& node = model.nodes[node_idx];
	if (node.mesh != -1)
		for (auto& prim : model.meshes[node.mesh].primitives){
			int vcount = 0, v_st = tmp.position.size(), i_st = tmp.indices.size();

			//! POSITION
			{
				const auto& accessor = model.accessors[prim.attributes.find("POSITION")->second];
				vcount = accessor.count;
				ReadGLTFData(model, accessor, tmp.position);
			}

			//! Indices
			int icount = 0;
			if (prim.indices > -1)
			{
				const tinygltf::Accessor& indexAccessor = model.accessors[prim.indices];
				const tinygltf::BufferView& bufferView = model.bufferViews[indexAccessor.bufferView];
				const tinygltf::Buffer& buffer = model.buffers[bufferView.buffer];
				icount = indexAccessor.count;
				switch (indexAccessor.componentType)
				{
				case TINYGLTF_PARAMETER_TYPE_UNSIGNED_INT:
					for (int i = 0; i < indexAccessor.count; ++i)
						tmp.indices.push_back(v_st + ((uint32_t*)(buffer.data.data() + bufferView.byteOffset))[i]);
					break;
				case TINYGLTF_PARAMETER_TYPE_UNSIGNED_SHORT:
					for (int i = 0; i < indexAccessor.count; ++i)
						tmp.indices.push_back(v_st + ((uint16_t*)(buffer.data.data() + bufferView.byteOffset))[i]);
					break;
				default:
					std::cerr << "Unknown index component type : " << indexAccessor.componentType << " is not supported" << std::endl;
					return;
				}
			}
			else
			{
				//! Primitive without indices, creating them
				for (unsigned int i = 0; i < vcount; ++i)
					tmp.indices.push_back(v_st + i);
				icount = vcount;
			}

			//! NORMAL
			
			{
				auto iter = prim.attributes.find("NORMAL");
				if (iter == prim.attributes.end())
				{
					tmp.normal.resize(tmp.normal.size()+vcount, glm::vec3(0.0f));
					//! You need to compute the normals
					for (size_t i = 0; i < icount; i += 3)
					{
						unsigned int idx0 = tmp.indices[i_st+i + 0];
						unsigned int idx1 = tmp.indices[i_st+i + 1];
						unsigned int idx2 = tmp.indices[i_st+i + 2];
						const auto& pos0 = tmp.position[idx0];
						const auto& pos1 = tmp.position[idx1];
						const auto& pos2 = tmp.position[idx2];
						const auto edge0 = glm::normalize(pos1 - pos0);
						const auto edge1 = glm::normalize(pos2 - pos0);
						const auto n = glm::normalize(glm::cross(edge0, edge1));
						tmp.normal[idx0] += n;
						tmp.normal[idx1] += n;
						tmp.normal[idx2] += n;
					}
					for (int i = v_st; i < vcount; ++i)
						tmp.normal[i] = glm::normalize(tmp.normal[i]);
				}else
				{
					const auto& accessor = model.accessors[iter->second];
					ReadGLTFData(model, accessor, tmp.normal);
				}
			}
			
			{
				auto iter = prim.attributes.find("COLOR_0");
				
				if (iter == prim.attributes.end())
				{
					glm::vec4 color(1.0f);
					if (prim.material != -1)
					{
						auto& vals = model.materials[prim.material].values;
						auto iter = vals.find("baseColorFactor");
						if (iter != vals.end() && iter->second.number_array.size()==4)
						{
							color.r = iter->second.number_array[0];
							color.g = iter->second.number_array[1];
							color.b = iter->second.number_array[2];
						}
					}
					for (int i = 0; i < vcount; ++i)
						tmp.color.push_back(color);
				}
				else
				{
					const auto& accessor = model.accessors[iter->second];
					ReadGLTFData(model, accessor, tmp.color);
				}
			}
			
		}


	for (auto& nodeIdx : node.children)
		load_primitive(nodeIdx, tmp);
}

int gltf_class::count_nodes()
{
	return objects.ls.size() * model.nodes.size();
}

void gltf_class::prepare_data(std::vector<s_transrot>& tr_per_node, std::vector<s_animation>& animation_info, int offset_node, int offset_instance)
{
	auto curTime = ui_state.getMsFromStart();
	auto& root_node_list = model.scenes[model.defaultScene > -1 ? model.defaultScene : 0].nodes;
	for (int i=0; i<objects.ls.size(); ++i)
	{
		auto object = objects.get(i);

		for (int i = 0; i < 8; ++i) {
			gltf_displaying.shine_colors.push_back(object->shineColor[i]);
			gltf_displaying.flags.push_back(object->flags[i]);
		}

		auto progress = std::clamp((curTime - object->target_start_time) / std::max(object->target_require_completion_time - object->target_start_time, 0.0001f), 0.0f, 1.0f);
		
		// compute rendering position:
		auto displaying_translation = Lerp(object->cur_translation, object->position,  progress);
		auto displaying_rotation = Lerp(object->cur_rotation, object->quaternion,  progress);

		std::copy(object->per_node_additives.begin(), object->per_node_additives.end(), tr_per_node.begin() + offset_node);
		for (auto nodeIdx : root_node_list) {
			tr_per_node[offset_node + nodeIdx].quaternion = displaying_rotation;
			tr_per_node[offset_node + nodeIdx].translation = displaying_translation;
		}

		for (int j=0; j<2; ++j)
			if (object->playingAnimId == 0)
			{
				object->playingAnimId = object->nextAnimId;
				object->nextAnimId = object->baseAnimId;
			}

		animation_info[offset_instance].anim_id = object->playingAnimId;
		animation_info[offset_instance].elapsed = object->animationStart;
	}

}

void gltf_class::compute_node_localmat(const glm::mat4& vm, int offset) {
	sg_apply_bindings(sg_bindings{
		.vertex_buffers = {
			originalLocals
		},
		.vs_images = {
			graphics_state.instancing.per_node_transrot,
			graphics_state.instancing.per_ins_animation,
			animation_data,
			parents
		}
		});
	animator_t transform{
		.max_instances = (int)objects.ls.size(),
		.offset = offset,
		.viewMatrix = vm,
	};
	sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(transform));
	sg_draw(0, model.nodes.size(), objects.ls.size());
}

void gltf_class::node_hierarchy(int offset, int pass){
	if (pass >= passes)
		return;
	sg_apply_bindings(sg_bindings{
		.vertex_buffers = {
			originalLocals // actually nothing required.
		},
		.vs_images = {
			(pass&1)?graphics_state.instancing.objInstanceNodeMvMats1:graphics_state.instancing.objInstanceNodeMvMats2,
			parents
		}
		});
	hierarchical_uniforms_t transform{
		.max_nodes = (int)model.nodes.size(),
		.max_instances = (int)objects.ls.size(),
		.pass_n = pass,
		.offset = offset,
	};
	sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(transform));
	sg_draw(0, model.nodes.size(), objects.ls.size());
}


// compute inverse of node viewmatrix.
void gltf_class::node_complete(int offset)
{
	sg_apply_bindings(sg_bindings{
		.vertex_buffers = {
			originalLocals // actually nothing required.
		},
		.vs_images = {
			graphics_state.instancing.objInstanceNodeNormalMats
		}
	});
	node_final_uniforms_t transform{
		.max_nodes = (int)model.nodes.size(),
		.max_instances = (int)objects.ls.size(),
		.offset = offset,
	};
	sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(transform));
	sg_draw(0, model.nodes.size(), objects.ls.size());
}

inline void gltf_class::render(const glm::mat4& vm, const glm::mat4& pm, bool shadow_map, int offset, int class_id)
{

	auto& wstate = ui_state.workspace_state.top();

	gltf_mats_t gltf_mats = {
		.projectionMatrix = pm,
		.viewMatrix = vm,
		.max_instances = int(objects.ls.size()),
		.offset = offset,  // node offset.

		.class_id = class_id,
		.obj_offset = instance_offset,
		.hover_instance_id = ui_state.hover_type == class_id+1000? ui_state.hover_instance_id:-1,
		.hover_node_id = ui_state.hover_node_id,
		.hover_shine_color_intensity = wstate.hover_shine,
		.selected_shine_color_intensity = wstate.selected_shine,
		.time = ui_state.getMsFromStart()
	};

	// draw. todo: add morphing in the shader.
	sg_apply_pipeline(graphics_state.gltf_pip);
	sg_apply_bindings(sg_bindings{
		.vertex_buffers = {
			positions,
			normals,
			colors,
			node_ids
		},
		.index_buffer = indices,
		.vs_images = {
			graphics_state.instancing.objFlags,
			graphics_state.instancing.objShineIntensities,
			(passes&1)?graphics_state.instancing.objInstanceNodeMvMats2:graphics_state.instancing.objInstanceNodeMvMats1,///
			graphics_state.instancing.objInstanceNodeNormalMats,
		}
		});
	sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_gltf_mats, SG_RANGE(gltf_mats));
	sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_gltf_mats, SG_RANGE(gltf_mats));

	sg_draw(0, n_indices, objects.ls.size());
}


inline void gltf_class::countvtx(int node_idx)
{
	auto& node = model.nodes[node_idx];

	if (node.mesh != -1) {
		name_nodeId_map[node.name] = node_idx;
		nodeId_name_map[node_idx] = node.name;

		int ototalvtx = totalvtx;
		for (auto& primitive : model.meshes[node.mesh].primitives) {
			assert(primitive.mode == TINYGLTF_MODE_TRIANGLES);

			totalvtx += model.accessors[primitive.attributes.find("POSITION")->second].count;
		}
		node_ctx_id.push_back(std::tuple(totalvtx - ototalvtx, node_idx));
	}
	for (auto& nodeIdx : node.children)
		countvtx(nodeIdx);
}



bool gltf_class::init_node(int node_idx, std::vector<glm::mat4>& writemat, std::vector<glm::mat4>& readmat, int parent_idx, int depth, temporary_buffer& tmp)
{
	auto& node = model.nodes[node_idx];

	//! Gets transformation info from the given node
	glm::mat4 local{ 1.0f };

	if (node.matrix.empty() == false)
	{
		float* nodeMatPtr = glm::value_ptr(local);
		for (int i = 0; i < 16; ++i)
			nodeMatPtr[i] = static_cast<float>(node.matrix[i]);
	}
	else
	{
		glm::vec3 translation{ 0.0f };
		glm::vec3 scale{ 1.0f };
		glm::quat rotation{ 0.0f, 0.0f, 0.0f, 0.0f };
		if (node.translation.empty() == false)
			translation = glm::vec3(node.translation[0], node.translation[1], node.translation[2]);
		if (node.scale.empty() == false)
			scale = glm::vec3(node.scale[0], node.scale[1], node.scale[2]);
		if (node.rotation.empty() == false)
			rotation = glm::quat(node.rotation[3], node.rotation[0], node.rotation[1], node.rotation[2]);
		
		local =
			glm::scale(glm::translate(glm::mat4(1.0f), translation) *
				glm::mat4_cast(rotation), scale);
	}
	//std::cout << "init node " << node_idx << " at depth " << depth <<", mesh?"<< node.mesh<< std::endl;

	if (depth > maxdepth) maxdepth = depth;
	
	writemat[node_idx] = readmat[std::max(parent_idx,0)] * local;

	tmp.localMatVec[node_idx] = local;
	tmp.raw_parents[node_idx] = parent_idx;


	bool imp = node.mesh != -1;
	for (int childNode : model.nodes[node_idx].children) {
		imp |= init_node(childNode, writemat, writemat, node_idx, depth + 1, tmp);
	}

	return imp;
}

inline gltf_class::gltf_class(const tinygltf::Model& model, std::string name, glm::vec3 center, float scale, glm::quat rotate)
{
	// GltfImporter importer(model);
	// importer.Parse(this->skels, this->animations);

	this->model = model;
	this->name = name;

	int defaultScene = model.defaultScene > -1 ? model.defaultScene : 0;
	
	temporary_buffer t;
	const auto& scene = model.scenes[defaultScene];
	for (auto nodeIdx : scene.nodes)
		countvtx(nodeIdx);

	t.indices.reserve(totalvtx*3); //?
	t.position.reserve(totalvtx);
	t.normal.reserve(totalvtx);
	t.color.reserve(totalvtx);
	t.node_id.reserve(totalvtx);

	for (int i = 0; i < node_ctx_id.size(); ++i)
		for (int j = 0; j < std::get<0>(node_ctx_id[i]);++j)
			t.node_id.push_back(std::get<1>(node_ctx_id[i]));

	for (auto nodeIdx : scene.nodes) 
		load_primitive(nodeIdx, t);

	n_indices = t.indices.size();

	indices = sg_make_buffer(sg_buffer_desc{
		.type = SG_BUFFERTYPE_INDEXBUFFER,
		.data = {t.indices.data(), t.indices.size() * sizeof(int)},
		});
	positions= sg_make_buffer(sg_buffer_desc{
		.data = {t.position.data(), t.position.size() * sizeof(glm::vec3)},
		});
	normals = sg_make_buffer(sg_buffer_desc{
		.data = {t.normal.data(), t.normal.size() * sizeof(glm::vec3)},
		});

	// colors to rgba8.
	colors = sg_make_buffer(sg_buffer_desc{
		.data = {t.color.data(), t.color.size() * sizeof(glm::vec4)},
		});

	node_ids= sg_make_buffer(sg_buffer_desc{
		.data = {t.node_id.data(), t.node_id.size() * sizeof(float)},
	});
	

	std::vector<glm::mat4> world(model.nodes.size(),glm::mat4(1.0f));
	std::vector<glm::mat4> rootmat(1, glm::mat4(1.0));

	t.raw_parents.resize(model.nodes.size());
	t.localMatVec.resize(model.nodes.size());
	for (auto nodeIdx : scene.nodes)
		init_node(nodeIdx, world, rootmat, -1, 1, t);
	

	// todo: just patch for bad formed gltfs.
	auto bbMin = glm::vec3(std::numeric_limits<float>::max());
	auto bbMax = glm::vec3(std::numeric_limits<float>::min());
	
	for (int i=0; i<t.position.size(); ++i)
	{
		auto pv = glm::vec3(world[t.node_id[i]] * glm::vec4(t.position[i], 1.0f));
		bbMin = { std::min(bbMin.x, pv.x), std::min(bbMin.y, pv.y), std::min(bbMin.z, pv.z) };
		bbMax = { std::max(bbMax.x, pv.x), std::max(bbMax.y, pv.y), std::max(bbMax.z, pv.z) };
	}

	if (bbMin == bbMax)
	{
		bbMin = glm::vec3(-1.0f);
		bbMax = glm::vec3(1.0f);
	}
	
	sceneDim.center = center;
	sceneDim.radius = glm::length(bbMax - bbMin) * 0.8f * scale;
	
	i_mat = glm::translate(glm::mat4(1.0f), -sceneDim.center) * glm::scale(glm::mat4(1.0f), glm::vec3(scale)) * glm::mat4_cast(rotate);

	for (auto nodeIdx : scene.nodes)
	{
		auto inormalized = i_mat * t.localMatVec[nodeIdx];
		t.localMatVec[nodeIdx] = inormalized;
	}

	originalLocals = sg_make_buffer(sg_buffer_desc{
		.data = {t.localMatVec.data(), t.localMatVec.size() * sizeof(glm::mat4)},
		});

	iter_times = (maxdepth - 1);
	if (iter_times == 0) passes = 0;
	else if (iter_times <= 4) passes = 1;
	else if (iter_times <= 24) passes = 2;
	else if (iter_times <= 124) passes = 3;
	else if (iter_times <= 624) passes = 4;
	else throw "node hierarchy too deep!";

	max_hierarchy_depth = std::max(iter_times, max_hierarchy_depth);

	auto sz = model.nodes.size();
	auto w = (int)ceil(sqrt(model.nodes.size() * passes));
	t.all_parents.reserve(w * w);
	for (int i = 0; i < sz; ++i)
		t.all_parents.push_back(t.raw_parents[i]);
	for (int j = 0; j < passes - 1; ++j)
		for (int i = 0; i < sz; ++i)
			if (t.all_parents[i + sz * j] == -1)
				t.all_parents.push_back(-1);
			else 
				t.all_parents.push_back(t.all_parents[t.all_parents[i + sz * j]]);


	parents = sg_make_image(sg_image_desc{
		.width = w,
		.height = w,
		.pixel_format = SG_PIXELFORMAT_R32UI,
		.data = {.subimage = {{ {
			.ptr = t.all_parents.data(),  // Your mat4 data here
			.size = t.all_parents.size() * sizeof(int)
		}}}}
	});
}

inline gltf_object::gltf_object(gltf_class* cls) :flags{ 0 | (-1 << 8),-1,-1,-1,-1,-1,-1,-1 }
{
	//this->weights = std::vector<float>(cls->morphTargets, 0);
}