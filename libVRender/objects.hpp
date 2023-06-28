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


void gltf_class::update_node(int nodeIdx, std::vector<glm::mat4>& writemat, std::vector<glm::mat4>& readmat, int parent_idx)
{
	auto& node = model.nodes[nodeIdx];
	int i = 0;
	auto this_st = nodeIdx * objects.size();
	auto parent_st = parent_idx * objects.size();
	for (auto& obj : objects)
	{
		writemat[this_st + i] = readmat[parent_st + i] * nodes_local_mat[nodeIdx];
		++i;
	}

	for (int childNode : node.children) {
		update_node(childNode, writemat, writemat, nodeIdx);
	}
}

inline void gltf_class::render(const glm::mat4& vm, const glm::mat4& pm, bool shadow_map)
{
	// todo: culling out of screen objects.

	auto start = std::chrono::high_resolution_clock::now();

	// generate instanceID node mat4 (todo: gpu generate, along with animation of t/r/s mat.
	std::vector<glm::mat4> modelViews, normalMats ;
	modelViews.resize(objects.size()*model.nodes.size(), glm::mat4(1.0f));
	normalMats.reserve(objects.size() * model.nodes.size());
	std::vector<glm::mat4> rootmat;
	rootmat.reserve(objects.size());
	
	for (const auto& obj : objects)
		rootmat.push_back(translate(vm, obj.second.position) * mat4_cast(obj.second.quaternion) * i_mat);

	int defaultScene = model.defaultScene > -1 ? model.defaultScene : 0;
	const auto& scene = model.scenes[defaultScene];
	for (auto nodeIdx : scene.nodes) {
		update_node(nodeIdx, modelViews, rootmat, 0);
	}
	auto end = std::chrono::high_resolution_clock::now();
	std::chrono::duration<double> elapsed = end - start;
	ImGui::Text(std::format("update node time={}ms", elapsed.count()*1000).c_str());
	
	sg_image NImodelViewMatrix = sg_make_image(sg_image_desc{
		.width = (int)(objects.size()*4),
		.height = int(model.nodes.size()),
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		.wrap_u = SG_WRAP_CLAMP_TO_EDGE,
		.wrap_v = SG_WRAP_CLAMP_TO_EDGE,
		.data = {.subimage = {{ {
			.ptr = modelViews.data(),  // Your mat4 data here
			.size = modelViews.size()*sizeof(glm::mat4)
		}}}}
	});

	auto endtc = std::chrono::high_resolution_clock::now();
	std::chrono::duration<double> elapsed01 = endtc - start;
	ImGui::Text(std::format("btccp={}ms", elapsed01.count() * 1000).c_str());

	// non-morphing draws:
	if (!shadow_map)
	{
		for (int i = 0; i < modelViews.size(); ++i) {
			normalMats.push_back(transpose(glm::inverse(modelViews[i])));
		}
		auto end01 = std::chrono::high_resolution_clock::now();
		std::chrono::duration<double> elapsed01 = end01 - start;
		ImGui::Text(std::format("tp={}ms", elapsed01.count() * 1000).c_str());

		sg_image NInormalMatrix = sg_make_image(sg_image_desc{
			.width = (int)(objects.size() * 4), // waste 1 column
			.height = int(model.nodes.size()),
			.pixel_format = SG_PIXELFORMAT_RGBA32F,
			.wrap_u = SG_WRAP_CLAMP_TO_EDGE,
			.wrap_v = SG_WRAP_CLAMP_TO_EDGE,
			.data = {.subimage = {{ {
				.ptr = normalMats.data(),  // Your mat4 data here
				.size = normalMats.size() * sizeof(glm::mat4)
			}}}}
			});

		auto end0 = std::chrono::high_resolution_clock::now();
		std::chrono::duration<double> elapsed0 = end0 - start;
		ImGui::Text(std::format("create={}ms", elapsed0.count() * 1000).c_str());

		// instance_id buffer: just refresh once.
		std::vector<int> ids(objects.size());
		for (int i = 0; i < objects.size(); ++i) ids[i] = i;
		sg_update_buffer(instanceID, sg_range{
			.ptr = ids.data(),
			.size = ids.size() * sizeof(int)
		});

		auto end = std::chrono::high_resolution_clock::now();
		std::chrono::duration<double> elapsed = end - start;
		ImGui::Text(std::format("upload={}ms", elapsed.count() * 1000).c_str());

		// draw.
		sg_apply_pipeline(graphics_state.gltf_pip);
		sg_apply_bindings(sg_bindings{
			.vertex_buffers = {
				instanceID,
				positions,
				normals,
				colors,
				node_ids
			},
			.index_buffer = indices,
			.vs_images = {
				NImodelViewMatrix,
				NInormalMatrix,
			}
			});
		gltf_mats_t gltf_mats = {
			.projectionMatrix = pm,
			.viewMatrix = vm,
		};
		sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_gltf_mats, SG_RANGE(gltf_mats));
		sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_gltf_mats, SG_RANGE(gltf_mats));

		sg_draw(0, n_indices, objects.size());

		auto end1 = std::chrono::high_resolution_clock::now();
		std::chrono::duration<double> elapsed1 = end1 - start;
		ImGui::Text(std::format("findraw={}ms", elapsed1.count() * 1000).c_str());

		sg_destroy_image(NInormalMatrix);
	}
	else {
		sg_apply_pipeline(graphics_state.gltf_pip_depth);
		sg_apply_bindings(sg_bindings{
			.vertex_buffers = {
				instanceID,
				positions,
				node_ids
			},
			.index_buffer = indices,
			.vs_images = {	NImodelViewMatrix, }
			});
		gltf_mats_t gltf_mats = {
			.projectionMatrix = pm,
			.viewMatrix = vm,
		};
		sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_gltf_mats, SG_RANGE(gltf_mats));

		sg_draw(0, n_indices, objects.size());
	}

	// todo: morphing draws:

	// end.
	sg_destroy_image(NImodelViewMatrix);
}


inline void gltf_class::countvtx(int node_idx)
{
	auto& node = model.nodes[node_idx];
	if (node.mesh != -1) {
		int ototalvtx = totalvtx;
		for (auto& primitive : model.meshes[node.mesh].primitives) {
			assert(primitive.mode == TINYGLTF_MODE_TRIANGLES);

			totalvtx += model.accessors[primitive.attributes.find("POSITION")->second].count;
		}
		node_length_id.push_back(std::tuple(totalvtx - ototalvtx, node_idx));
	}
	for (auto& nodeIdx : node.children)
		countvtx(nodeIdx);
}



void gltf_class::init_node(int node_idx, std::vector<glm::mat4>& writemat, std::vector<glm::mat4>& readmat, int parent_idx, int depth)
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
	std::cout << "init node " << node_idx << " at depth " << depth <<", mesh?"<< node.mesh<< std::endl;

	nodes_local_mat[node_idx] = local;
	writemat[node_idx] = readmat[parent_idx] * local;

	for (int childNode : model.nodes[node_idx].children) {
		init_node(childNode, writemat, writemat, node_idx, depth+1);
	}
}

inline gltf_class::gltf_class(const tinygltf::Model& model, std::string name, glm::vec3 center, float radius)
{
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

	for (int i = 0; i < node_length_id.size(); ++i)
		for (int j = 0; j < std::get<0>(node_length_id[i]);++j)
			t.node_id.push_back(std::get<1>(node_length_id[i]));

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
	colors = sg_make_buffer(sg_buffer_desc{
		.data = {t.color.data(), t.color.size() * sizeof(glm::vec4)},
		});
	node_ids= sg_make_buffer(sg_buffer_desc{
		.data = {t.node_id.data(), t.node_id.size() * sizeof(int)},
	});
	
	nodes_local_mat= std::vector<glm::mat4>(model.nodes.size(), glm::mat4(1.0f));
	instanceID = sg_make_buffer(sg_buffer_desc {
		.size = 65536,
		.usage = SG_USAGE_STREAM,
	});

	std::vector<glm::mat4> world(model.nodes.size(),glm::mat4(1.0f));
	std::vector<glm::mat4> rootmat(1, glm::mat4(1.0));
	
	for (auto nodeIdx : scene.nodes)
		init_node(nodeIdx, world, rootmat, 0, 1);

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
	sceneDim.radius = glm::length(bbMax - bbMin) * 0.8f;
	
	i_mat = glm::mat4(1.0f);
	
	i_mat = glm::rotate(glm::mat4(1.0f), glm::radians(90.0f), glm::vec3(1.0f, 0.0f, 0.0f)) * 
		glm::translate(glm::mat4(1.0f), -sceneDim.center);

}