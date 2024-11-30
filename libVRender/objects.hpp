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

#include "me_impl.h"
template <typename T>
void GLTFHelper0(const tinygltf::Model& model, const void* bufData, gltf_class::temporary_buffer& tmp, int stride, int skin, int ne)
{
	auto bufferByte = reinterpret_cast<const T*>(bufData);
	for (size_t i = 0; i < ne; ++i)
	{
		auto j0 = bufferByte[0];
		auto j1 = bufferByte[1];
		auto j2 = bufferByte[2];
		auto j3 = bufferByte[3];
		tmp.joints.push_back({ j0,j1,j2,j3 });
		auto sk = model.skins[skin];
		tmp.jointNodes.push_back({
				sk.joints[j0],
				sk.joints[j1],
				sk.joints[j2],
				sk.joints[j3],
			});
		bufferByte = reinterpret_cast<const T*>((((unsigned char*)bufferByte)+stride));
	}
}

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

void gltf_class::import_material(temporary_buffer& tmp)
{
	tmp._sceneMaterials.reserve(model.materials.size());

	for (const auto& mat : model.materials)
	{
		GLTFMaterial material;
		material.alphaCutoff = static_cast<float>(mat.alphaCutoff);
		material.alphaMode = mat.alphaMode == "MASK" ? 1 : (mat.alphaMode == "BLEND" ? 2 : 0);
		material.doubleSided = mat.doubleSided ? 1 : 0;
		material.emissiveFactor = glm::vec3(mat.emissiveFactor[0], mat.emissiveFactor[1], mat.emissiveFactor[2]);
		material.emissiveTexture = mat.emissiveTexture.index;
		material.normalTexture = mat.normalTexture.index;
		material.normalTextureScale = static_cast<float>(mat.normalTexture.scale);
		material.occlusionTexture = mat.occlusionTexture.index;
		material.occlusionTextureStrength = static_cast<float>(mat.occlusionTexture.strength);

		//! PBR Metallic roughness
		auto& pbr = mat.pbrMetallicRoughness;
		material.baseColorFactor = glm::vec4(pbr.baseColorFactor[0], pbr.baseColorFactor[1], pbr.baseColorFactor[2], pbr.baseColorFactor[3]);
		material.baseColorTexture = pbr.baseColorTexture.index;
		basecolortex = material.baseColorTexture; //todo;

		material.metallicFactor = static_cast<float>(pbr.metallicFactor);
		material.metallicRoughnessTexture = pbr.metallicRoughnessTexture.index;
		material.roughnessFactor = static_cast<float>(pbr.roughnessFactor);

		//! KHR_materials_pbrSpecularGlossiness
		if (mat.extensions.find(KHR_MATERIALS_PBR_SPECULAR_GLOSSINESS_EXTENSION_NAME) != mat.extensions.end())
		{
			material.shadingModel = 1;

			const auto& ext = mat.extensions.find(KHR_MATERIALS_PBR_SPECULAR_GLOSSINESS_EXTENSION_NAME)->second;
			gltfGetValue<glm::vec4>(ext, "diffuseFactor", material.specularGlossiness.diffuseFactor);
			gltfGetValue<float>(ext, "glossinessFactor", material.specularGlossiness.glossinessFactor);
			gltfGetValue<glm::vec3>(ext, "specularFactor", material.specularGlossiness.specularFactor);
			gltfGetTextureID(ext, "diffuseTexture", material.specularGlossiness.diffuseTexture);
			gltfGetTextureID(ext, "specularGlossinessTexture", material.specularGlossiness.specularGlossinessTexture);
		}

		// KHR_texture_transform
		if (pbr.baseColorTexture.extensions.find(KHR_TEXTURE_TRANSFORM_EXTENSION_NAME) != pbr.baseColorTexture.extensions.end())
		{
			const auto& ext = pbr.baseColorTexture.extensions.find(KHR_TEXTURE_TRANSFORM_EXTENSION_NAME)->second;
			auto& tt = material.textureTransform;
			gltfGetValue<glm::vec2>(ext, "offset", tt.offset);
			gltfGetValue<glm::vec2>(ext, "scale", tt.scale);
			gltfGetValue<float>(ext, "rotation", tt.rotation);
			gltfGetValue<int>(ext, "texCoord", tt.texCoord);

			// Computing the transformation
			glm::mat3 translation = glm::mat3(1, 0, tt.offset.x, 0, 1, tt.offset.y, 0, 0, 1);
			glm::mat3 rotation = glm::mat3(cos(tt.rotation), sin(tt.rotation), 0, -sin(tt.rotation), cos(tt.rotation), 0, 0, 0, 1);
			glm::mat3 scale = glm::mat3(tt.scale.x, 0, 0, 0, tt.scale.y, 0, 0, 0, 1);
			tt.uvTransform = scale * rotation * translation;
		}

		// KHR_materials_unlit
		if (mat.extensions.find(KHR_MATERIALS_UNLIT_EXTENSION_NAME) != mat.extensions.end())
		{
			material.unlit.active = 1;
		}

		// KHR_materials_clearcoat
		if (mat.extensions.find(KHR_MATERIALS_CLEARCOAT_EXTENSION_NAME) != mat.extensions.end())
		{
			const auto& ext = mat.extensions.find(KHR_MATERIALS_CLEARCOAT_EXTENSION_NAME)->second;
			gltfGetValue<float>(ext, "clearcoatFactor", material.clearcoat.factor);
			gltfGetTextureID(ext, "clearcoatTexture", material.clearcoat.texture);
			gltfGetValue<float>(ext, "clearcoatRoughnessFactor", material.clearcoat.roughnessFactor);
			gltfGetTextureID(ext, "clearcoatRoughnessTexture", material.clearcoat.roughnessTexture);
			gltfGetTextureID(ext, "clearcoatNormalTexture", material.clearcoat.normalTexture);
		}

		// KHR_materials_sheen
		if (mat.extensions.find(KHR_MATERIALS_SHEEN_EXTENSION_NAME) != mat.extensions.end())
		{
			const auto& ext = mat.extensions.find(KHR_MATERIALS_SHEEN_EXTENSION_NAME)->second;
			gltfGetValue<glm::vec3>(ext, "sheenColorFactor", material.sheen.colorFactor);
			gltfGetTextureID(ext, "sheenColorTexture", material.sheen.colorTexture);
			gltfGetValue<float>(ext, "sheenRoughnessFactor", material.sheen.roughnessFactor);
			gltfGetTextureID(ext, "sheenRoughnessTexture", material.sheen.roughnessTexture);
		}

		// KHR_materials_transmission
		if (mat.extensions.find(KHR_MATERIALS_TRANSMISSION_EXTENSION_NAME) != mat.extensions.end())
		{
			const auto& ext = mat.extensions.find(KHR_MATERIALS_TRANSMISSION_EXTENSION_NAME)->second;
			gltfGetValue<float>(ext, "transmissionFactor", material.transmission.factor);
			gltfGetTextureID(ext, "transmissionTexture", material.transmission.texture);
		}

		tmp._sceneMaterials.emplace_back(material);
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

			if (prim.targets.size() > 0)
			{
				// has morph targets. morph targets only use 2 targets, just enough for simplistic morphing animation.
				// don't use morph target for facial expression.
				morphTargets = (int)prim.targets.size();
				for (auto target : prim.targets)
					ReadGLTFData(model, model.accessors[target["POSITION"]], tmp.morphtargets);
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
					glm::vec4 color(0.5f, 0.5f, 0.5f, 1.0f);
					if (prim.material != -1)
					{
						auto& vals = model.materials[prim.material].values;
						auto iter = vals.find("baseColorFactor");
						if (iter != vals.end() && iter->second.number_array.size()==4)
						{
							color.r = iter->second.number_array[0];
							color.g = iter->second.number_array[1];
							color.b = iter->second.number_array[2];
							color.a = iter->second.number_array[3];
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

			{
				auto iter = prim.attributes.find("TEXCOORD_0");
				auto id = prim.material != -1 ? model.materials[prim.material].pbrMetallicRoughness.baseColorTexture.index : -1;
				if (iter == prim.attributes.end() || id == -1)
				{
					glm::vec2 uv(-1.0f);
					for (int i = 0; i < vcount; ++i)
						tmp.texcoord.push_back(uv);
				}
				else
				{
					const auto& accessor = model.accessors[iter->second];
					auto st = tmp.texcoord.size();
					ReadGLTFData(model, accessor, tmp.texcoord);
					
					auto originW = model.images[id].width;
					auto originH = model.images[id].height;
					auto biasX = float(tmp.rectangles[id].x) / tmp.atlasW;
					auto biasY = float(tmp.rectangles[id].y) / tmp.atlasH;
					auto scaleX = float(originW) / tmp.atlasW;
					auto scaleY = float(originH) / tmp.atlasH;
					for (int i = st; i < tmp.texcoord.size(); ++i)
						tmp.texcoord[i] = glm::vec2(tmp.texcoord[i].x * scaleX + biasX, tmp.texcoord[i].y * scaleY + biasY);
				}
			}

			//skinning:
			{
				// GLTF requires at most 4 influences: https://github.com/KhronosGroup/glTF-Blender-IO/issues/81
				auto iter1 = prim.attributes.find("JOINTS_0");
				auto iter2 = prim.attributes.find("WEIGHTS_0");

				if (iter1 == prim.attributes.end())
				{
					for (int i = 0; i < vcount; ++i) {
						tmp.joints.push_back(glm::uvec4(-1));
						tmp.weights.push_back(glm::vec4(-1));
						tmp.jointNodes.push_back(glm::vec4(-1));
					}
				}
				else
				{
					auto accessor = model.accessors[iter1->second];
					//! Retrieving the data of the attributes
					const auto& bufferView = model.bufferViews[accessor.bufferView];
					const auto& buffer = model.buffers[bufferView.buffer];
					const void* bufData = &(buffer.data[accessor.byteOffset + bufferView.byteOffset]);
					const auto& numElements = accessor.count;
					auto stride = bufferView.byteStride;
					if (accessor.componentType == TINYGLTF_COMPONENT_TYPE_UNSIGNED_BYTE)
						GLTFHelper0<unsigned char>(model, bufData, tmp, stride == 0 ? 4 : stride, node.skin, numElements);
					else if (accessor.componentType == TINYGLTF_COMPONENT_TYPE_UNSIGNED_SHORT)
						GLTFHelper0<unsigned short>(model, bufData, tmp, stride == 0 ? 8 : stride, node.skin, numElements);
					else if (accessor.componentType == TINYGLTF_COMPONENT_TYPE_UNSIGNED_INT)
						GLTFHelper0<unsigned int>(model, bufData, tmp, stride == 0 ? 16 : stride, node.skin, numElements);

					ReadGLTFData(model, model.accessors[iter2->second], tmp.weights);
				}
			}
		}


	for (auto& nodeIdx : node.children)
		load_primitive(nodeIdx, tmp);
}

int gltf_class::count_nodes()
{
	return showing_objects.size() * model.nodes.size();
}

void gltf_class::prepare_data(std::vector<s_pernode>& tr_per_node, std::vector<s_perobj>& per_obj, int offset_node, int offset_instance)
{
	auto& root_node_list = model.scenes[model.defaultScene > -1 ? model.defaultScene : 0].nodes;
	auto instances = showing_objects.size();
	for (int i=0; i<instances; ++i)
	{
		auto object = showing_objects[i];
		// for (int i = 0; i < 8; ++i) {
		// 	gltf_displaying.shine_colors.push_back(object->shineColor[i]);
		// 	gltf_displaying.flags.push_back(object->flags[i]);
		// }
		// maybe doesn't have.
		for(int j=0; j<model.nodes.size(); ++j)
			tr_per_node[offset_node + i + j * instances] = object->nodeattrs[j];
		for (auto nodeIdx : root_node_list) {
			tr_per_node[offset_node + i + nodeIdx*instances].quaternion = object->current_rot;
			tr_per_node[offset_node + i + nodeIdx*instances].translation = object->current_pos;
		}

		// if currently not playing
		auto currentTime = ui_state.getMsFromStart();
		if (object->playingAnimId < 0)
		{
			object->playingAnimId = object->nextAnimId;
			object->nextAnimId = object->baseAnimId;
			object->animationStartMs = currentTime;
		}else
		{
			auto expectEnd = object->animationStartMs + animations[object->playingAnimId].duration;
			if (currentTime > expectEnd)
			{
				//switch:
				object->playingAnimId = object->nextAnimId;
				object->nextAnimId = object->baseAnimId;
				object->animationStartMs = expectEnd;
				// printf("%s animation end on %d\n", object->name.c_str(), expectEnd);
			}
		}

		auto& obj_info = per_obj[offset_instance + i];

		obj_info.anim_id = object->playingAnimId;
		obj_info.elapsed = currentTime - object->animationStartMs; // elapsed compute on gpu.
		// printf("%s.elapsed=%d\n", object->name.c_str(), obj_info.elapsed);
		obj_info.shineColor = object->shine;
		obj_info.flag = object->flags;
	}

}

void gltf_class::compute_node_localmat(const glm::mat4& vm, int offset) {
	sg_apply_bindings(sg_bindings{
		.vertex_buffers = {
			// originalLocals
			itrans, irot, iscale
		},
		.vs_images = {
			animtimes,
			graphics_state.instancing.node_meta,
			graphics_state.instancing.instance_meta,
			animap,
			animdt,
			parents,
		}
		});
	animator_t transform{
		.max_instances = (int)showing_objects.size(),
		.instance_offset = instance_offset,
		.node_amount = (int)model.nodes.size(),
		.offset = offset,
		.viewMatrix = vm,
		.i_mat = i_mat,
		.flags = (model.animations.size() > 0 ? 1 : 0)
	};
	sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(transform));
	sg_draw(0, model.nodes.size(), showing_objects.size());
}

void gltf_class::node_hierarchy(int offset, int pass){
	if (pass >= int(ceil(passes / 2.0f) * 2))
		return;
	sg_apply_bindings(sg_bindings{
		.vertex_buffers = {
			//originalLocals // actually nothing required.
		},
		.vs_images = {
			(pass&1)?graphics_state.instancing.objInstanceNodeMvMats2:graphics_state.instancing.objInstanceNodeMvMats1,
			parents
		}
		});
	hierarchical_uniforms_t transform{
		.max_nodes = (int)model.nodes.size(),
		.max_instances = (int)showing_objects.size(),
		.pass_n = pass,
		.offset = offset,
	};
	sg_apply_uniforms(SG_SHADERSTAGE_VS, 0, SG_RANGE(transform));
	sg_draw(0, model.nodes.size(), showing_objects.size());
}

inline int gltf_class::list_objects()
{	auto instances = objects.ls.size();
	showing_objects.clear();
	showing_objects_name.clear();
	for (int i=0; i<instances; ++i)
	{
		auto ptr = objects.get(i);
		if (!ptr->show) continue;
		showing_objects.push_back(ptr);
		showing_objects_name.push_back(&objects.getName(i));
	}
	return showing_objects.size();
}

inline void gltf_class::render(const glm::mat4& vm, const glm::mat4& pm, bool shadow_map, int offset, int class_id)
{

	auto& wstate = ui_state.workspace_state.top();

	gltf_mats_t gltf_mats = {
		.projectionMatrix = pm,
		.viewMatrix = vm,
		.max_instances = int(showing_objects.size()),
		.offset = offset,  // node offset.
		.node_amount = int(model.nodes.size()),

		.class_id = class_id,
		.obj_offset = instance_offset,
		.hover_instance_id = ui_state.hover_type == class_id + 1000 ? ui_state.hover_instance_id : -1,
		.hover_node_id = ui_state.hover_node_id,
		.hover_shine_color_intensity = wstate.hover_shine,
		.selected_shine_color_intensity = wstate.selected_shine,

		.display_options = wstate.btf_on_hovering ? 1 : 0,
		.time = (float)ui_state.getMsFromStart(),

		.cs_center = glm::vec4(wstate.crossSectionPlanePos, wstate.useCrossSection ? 2 : 0),
		.cs_direction = glm::vec4(wstate.clippingDirection,0)
	};

	// draw. todo: add morphing in the shader.
	sg_apply_bindings(sg_bindings{
		.vertex_buffers = {
			positions,
			normals,
			colors,
			texcoords,
			node_metas,
			joints,
			jointNodes,
			weights,
		},
		.index_buffer = indices,
		.vs_images = {
			animtimes,

			graphics_state.instancing.instance_meta,
			graphics_state.instancing.node_meta,
			graphics_state.instancing.objInstanceNodeMvMats1, //always into mat1.
			graphics_state.instancing.objInstanceNodeNormalMats,

			skinInvs, //skinning inverse mats.
			animap,
			morphdt,
		},
		.fs_images = {
			atlas
		}
		});
	sg_apply_uniforms(SG_SHADERSTAGE_VS, SLOT_gltf_mats, SG_RANGE(gltf_mats));
	sg_apply_uniforms(SG_SHADERSTAGE_FS, SLOT_gltf_mats, SG_RANGE(gltf_mats));

	sg_draw(0, n_indices, showing_objects.size());
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
		node_ctx_id.push_back(std::tuple(totalvtx - ototalvtx, node_idx, ototalvtx));
	}
	for (auto& nodeIdx : node.children)
		countvtx(nodeIdx);
}



bool gltf_class::init_node(int node_idx, std::vector<glm::mat4>& writemat, std::vector<glm::mat4>& readmat, int parent_idx, int depth, temporary_buffer& tmp)
{
	auto& node = model.nodes[node_idx];

	//! Gets transformation info from the given node
	glm::mat4 local{ 1.0f };

	glm::vec3 translation{ 0.0f };
	glm::vec3 scale{ 1.0f };
	glm::quat rotation{ 1.0f, 0.0f, 0.0f, 0.0f };
	if (node.matrix.empty() == false)
	{
		float* nodeMatPtr = glm::value_ptr(local);
		for (int i = 0; i < 16; ++i)
			nodeMatPtr[i] = static_cast<float>(node.matrix[i]);
		translation = glm::vec3(local[3]);
		rotation = glm::quat_cast(local);
		scale.x = glm::length(glm::vec3(local[0])); // First column
		scale.y = glm::length(glm::vec3(local[1])); // Second column
		scale.z = glm::length(glm::vec3(local[2])); // Third column
	}
	else
	{
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
	tmp.it[node_idx] = translation;
	tmp.ir[node_idx] = rotation;
	tmp.is[node_idx] = scale;

	if (node.skin>=0)
	{
		// have skin.
		tmp.skins;
	}


	bool imp = node.mesh != -1;
	for (int childNode : model.nodes[node_idx].children) {
		imp |= init_node(childNode, writemat, writemat, node_idx, depth + 1, tmp);
	}

	return imp;
}


void gltf_class::apply_gltf(const tinygltf::Model& model, std::string name, glm::vec3 center, float scale, glm::quat rotate) {
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
	t.texcoord.reserve(totalvtx);
	t.node_meta.reserve(totalvtx);

	//skining:
	t.joints.reserve(totalvtx);
	t.jointNodes.reserve(totalvtx);
	t.weights.reserve(totalvtx);

	std::vector<glm::mat4> skin_invMats;
	std::vector<int> perSkinIdx;
	
	for (int i=0; i<model.skins.size(); ++i)
	{
		perSkinIdx.push_back(skin_invMats.size());
		ReadGLTFData(model, model.accessors[model.skins[i].inverseBindMatrices], skin_invMats);
	}
	auto ivh = std::max(1, (int)ceil(skin_invMats.size() / 512));
	skin_invMats.reserve(ivh * 512);
	skinInvs = sg_make_image(sg_image_desc{
		.width = 2048, //512 mats per row, 4comp per mat.
		.height = ivh,
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		.data = {.subimage = {{ {
			.ptr = skin_invMats.data(),  // Your mat4 data here
			.size = ivh * (512 * sizeof(glm::mat4))
		}}}}
	});

	std::map<int, int> node_vstart;
	for (int i = 0; i < node_ctx_id.size(); ++i) {
		auto nodeid = std::get<1>(node_ctx_id[i]);
		auto vcnt = std::get<0>(node_ctx_id[i]);
		node_vstart[nodeid] = std::get<2>(node_ctx_id[i]);
		auto skin = model.nodes[nodeid].skin;
		if (skin >= 0) skin = perSkinIdx[skin];
		for (int j = 0; j < vcnt ;++j) {
			t.node_meta.push_back({ nodeid, skin });
		}
	}

	import_material(t);

	if (model.images.size() > 0) {
		auto max_side = 4096;
		for (const auto& im : model.images)
			t.rectangles.emplace_back(rectpack2D::rect_xywh(0, 0, im.width, im.height));

		const auto discard_step = -4;
		auto report_successful = [](rect_type&) {
			return rectpack2D::callback_result::CONTINUE_PACKING;
		};

		auto report_unsuccessful = [](rect_type&) {
			return rectpack2D::callback_result::ABORT_PACKING;
		};
		const auto result_size = rectpack2D::find_best_packing<spaces_type>(
			t.rectangles,
			rectpack2D::make_finder_input(
				max_side,
				discard_step,
				report_successful,
				report_unsuccessful,
				rectpack2D::flipping_option::DISABLED
			)
		);
		printf("create atlas of %dx%d for %s\n", result_size.w, result_size.h, name.c_str());
		// std::cout << name << "Resultant bin: " << result_size.w << " " << result_size.h << std::endl;

		t.atlasH = result_size.h;
		t.atlasW = result_size.w;
		std::vector<unsigned char> finIm(t.atlasW * t.atlasH * 4); // Initialize with zeros
		for (size_t i = 0; i < model.images.size(); ++i) {
			auto& image = model.images[i];
			auto& rect = t.rectangles[i];

			// Copy each image into the finIm at the specified location
			for (int y = 0; y < image.height; ++y) {
				for (int x = 0; x < image.width; ++x) {
					int destIndex = ((rect.y + y) * t.atlasW + (rect.x + x)) * 4;
					int srcIndex = (y * image.width + x) * 4;

					if (destIndex < t.atlasW * t.atlasH * 4) {
						finIm[destIndex] = image.image[srcIndex];
						finIm[destIndex + 1] = image.image[srcIndex + 1];
						finIm[destIndex + 2] = image.image[srcIndex + 2];
						finIm[destIndex + 3] = image.image[srcIndex + 3];
					}
				}
			}
		}

		atlas = sg_make_image(sg_image_desc{
			.width = t.atlasW ,
			.height = t.atlasH ,
			.pixel_format = SG_PIXELFORMAT_RGBA8,
			.min_filter = SG_FILTER_LINEAR,
			.mag_filter = SG_FILTER_LINEAR,
			.wrap_u = SG_WRAP_CLAMP_TO_BORDER,
			.wrap_v = SG_WRAP_CLAMP_TO_BORDER,
			.border_color = SG_BORDERCOLOR_OPAQUE_WHITE,
			.data = {.subimage = {{ {
				.ptr = finIm.data(),  // Your mat4 data here
				.size = finIm.size()
			}}}},
			.label = name.c_str()
			});
	}else
		atlas = graphics_state.dummy_tex;

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

	texcoords = sg_make_buffer(sg_buffer_desc{
		.data = {t.texcoord.data(), t.texcoord.size() * sizeof(glm::vec2)},
		});

	joints = sg_make_buffer(sg_buffer_desc{
		.data = {t.joints.data(), t.joints.size() * sizeof(glm::vec4)},
		});
	jointNodes = sg_make_buffer(sg_buffer_desc{
		.data = {t.jointNodes.data(), t.jointNodes.size() * sizeof(glm::vec4)},
		});
	weights = sg_make_buffer(sg_buffer_desc{
		.data = {t.weights.data(), t.weights.size() * sizeof(glm::vec4)},
		});

	node_metas= sg_make_buffer(sg_buffer_desc{
		.data = {t.node_meta.data(), t.node_meta.size() * sizeof(glm::vec2)},
	});
	

	std::vector<glm::mat4> world(model.nodes.size(),glm::mat4(1.0f));
	std::vector<glm::mat4> rootmat(1, glm::mat4(1.0));

	t.raw_parents.resize(model.nodes.size());
	t.it.resize(model.nodes.size());
	t.ir.resize(model.nodes.size());
	t.is.resize(model.nodes.size());
	t.localMatVec.resize(model.nodes.size());
	for (auto nodeIdx : scene.nodes)
		init_node(nodeIdx, world, rootmat, -1, 1, t);
	

	// todo: just patch for bad formed gltfs.
	auto bbMin = glm::vec3(std::numeric_limits<float>::max());
	auto bbMax = glm::vec3(std::numeric_limits<float>::min());
	
	for (int i=0; i<t.position.size(); ++i)
	{
		auto pv = glm::vec3(world[t.node_meta[i].x] * glm::vec4(t.position[i], 1.0f));
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
	
	originalLocals = sg_make_buffer(sg_buffer_desc{
		.data = {t.localMatVec.data(), t.localMatVec.size() * sizeof(glm::mat4)},
		});
	itrans = sg_make_buffer(sg_buffer_desc{
		.data = {t.it.data(), t.it.size() * sizeof(glm::vec3)},
		});
	irot = sg_make_buffer(sg_buffer_desc{
		.data = {t.ir.data(), t.ir.size() * sizeof(glm::quat)},
		});
	iscale = sg_make_buffer(sg_buffer_desc{
		.data = {t.is.data(), t.is.size() * sizeof(glm::vec3)},
		});



	iter_times = (maxdepth - 1);
	if (iter_times == 0) passes = 0;
	else if (iter_times <= 4) passes = 1;
	else if (iter_times <= 24) passes = 2;
	else if (iter_times <= 124) passes = 3;
	else if (iter_times <= 624) passes = 4;
	else throw "node hierarchy too deep!";

	nodeMatSelector = passes & 1;

	max_passes = std::max(passes, max_passes);

	auto sz = model.nodes.size();
	auto w = (int)ceil(sqrt(model.nodes.size() * std::max(passes, 1)));
	t.all_parents.reserve(w * w);
	for (int i = 0; i < sz; ++i)
		t.all_parents.push_back(t.raw_parents[i]);
	for (int j = 0; j < passes - 1; ++j)
		for (int i = 0; i < sz; ++i)
			if (t.all_parents[i + sz * j] == -1)
				t.all_parents.push_back(-1);
			else
			{
				auto p = t.all_parents[i + sz * j];
				for (int k = 0; k < 4 && p!=-1; ++k)
					p = t.all_parents[p + sz * j];
				t.all_parents.push_back(p);
			}
	parents = sg_make_image(sg_image_desc{
		.width = w,
		.height = w,
		.pixel_format = SG_PIXELFORMAT_R32SI,
		.data = {.subimage = {{ {
			.ptr = t.all_parents.data(),  // Your mat4 data here
			.size = w * w * sizeof(int)
		}}}}
		});

	// node animations:
	// node: animationid-node map=>(idx, len);
	// animap: idx => len*(time, t/r/s) => nodeid
	std::map<int, std::vector<std::string>> node_channels;
	for (int aid = 0; aid < model.animations.size(); ++aid)
	{
		auto& animation = model.animations[aid];
		float animLen = 0; 
		for (int cid = 0; cid < animation.channels.size(); ++cid)
		{
			auto& channel = animation.channels[cid];
			if (node_channels.find(channel.target_node) == node_channels.end())
				node_channels[channel.target_node] = std::vector({ channel.target_path });
			else node_channels[channel.target_node].push_back(channel.target_path);
		}
	}
	std::vector<glm::ivec4> anim_meta(model.animations.size() * model.nodes.size() * 4); //t/r/s/w
	std::vector<float> anim_time; //t/r/s
	std::vector<glm::vec4> anim_data; //mat44:time. to use write back to 1.
	std::vector<float> morphtarget_data;
	std::vector<float> animation_times;
	std::map<int, int> node_targetsPos_map;
	for(int aid=0; aid<model.animations.size(); ++aid)
	{
		auto& animation = model.animations[aid];
		float animLen = 0;
		for (int cid=0; cid<animation.channels.size(); ++cid)
		{
			auto& channel = animation.channels[cid];
			auto idx_data = anim_data.size();
			auto idx_time = anim_time.size();

			ReadGLTFData(model, model.accessors[animation.samplers[channel.sampler].input], anim_time);
			animation_times.push_back(anim_time[anim_time.size() - 1]);
			animLen = std::max(animLen, anim_time[anim_time.size() - 1]);

			auto len = anim_time.size() - idx_time;

			auto wid = (model.nodes.size() * aid + channel.target_node) * 4;

			if (channel.target_path == "translation")
			{
				std::vector<glm::vec3> tmp;
				ReadGLTFData(model, model.accessors[animation.samplers[channel.sampler].output], tmp);
				for(int j=0; j<len; ++j)
					anim_data.push_back(glm::vec4(tmp[j], 0));
				anim_meta[wid] = { idx_data, len, idx_time, 3 };
			}
			else if (channel.target_path == "scale")
			{
				std::vector<glm::vec3> tmp;
				ReadGLTFData(model, model.accessors[animation.samplers[channel.sampler].output], tmp);
				for (int j = 0; j < len; ++j)
					anim_data.push_back(glm::vec4(tmp[j], 0));
				anim_meta[wid+1] = { idx_data, len, idx_time, 3 };
			}
			else if (channel.target_path == "rotation")
			{
				// std::vector<glm::vec4> quats;
				// ReadGLTFData(model, model.accessors[animation.samplers[channel.sampler].output], quats);
				// auto qq = quats[quats.size() - 1];
				// printf("r%d(nid%d):%f,%f,%f,%f\n", cid, channel.target_node, qq.x, qq.y, qq.z, qq.w);
				ReadGLTFData(model, model.accessors[animation.samplers[channel.sampler].output], anim_data);
				anim_meta[wid+2] = { idx_data, len, idx_time, 4};
			}
			else if (channel.target_path == "weights")
			{
				std::vector<float> tmp;
				ReadGLTFData(model, model.accessors[animation.samplers[channel.sampler].output], tmp);
				auto stride = tmp.size() / len; // this equals targetsN
				
				int wdata_pos;
				// test if prim's targets are already included:
				if (node_targetsPos_map.find(channel.target_node) == node_targetsPos_map.end())
				{
					// create targets.
					auto& prim = model.meshes[model.nodes[channel.target_node].mesh].primitives[0];
					auto& t = prim.targets;
					auto targetsN = t.size();
					assert(stride == targetsN);
					auto st = morphtarget_data.size() + 1;
					morphtarget_data.push_back(targetsN);
					morphtarget_data.push_back(model.accessors[prim.attributes.find("POSITION")->second].count);
					morphtarget_data.push_back(node_vstart.at(channel.target_node)); // the how many targets we have.
					for (int i = 0; i < targetsN; ++i)
					{
						std::vector<glm::vec3> tmp2;
						ReadGLTFData(model, model.accessors[t[i].at("POSITION")], tmp2);
						for (int j = 0; j < tmp2.size(); ++j)
						{
							morphtarget_data.push_back(tmp2[j].x);
							morphtarget_data.push_back(tmp2[j].y);
							morphtarget_data.push_back(tmp2[j].z);
						}
					}
					wdata_pos = node_targetsPos_map[channel.target_node] = st;
				}
				else 
					wdata_pos = node_targetsPos_map[channel.target_node];

				idx_data = morphtarget_data.size(); // we could have appended [target] into anim_data, idx_data is thus changed.
				for (int k=0; k<tmp.size(); ++k)
					morphtarget_data.push_back(tmp[k]);

				anim_meta[wid+3] = { idx_data, len, idx_time, wdata_pos };
					// idx_data*4 because we use R32 instead of RGBA32 (so position need a little bit more operations).
					// stride is replaced by an offset of anim_data, using by render pass.
				//todo.
			}
		}
		animations.push_back({ animation.name, (long)(animLen*1000) });
	}
	auto amh = std::max(1, (int)ceil((model.animations.size() * model.nodes.size())/2048.0f));
	anim_meta.reserve(amh * 2048);
	animap = sg_make_image(sg_image_desc{
		.width = 2048,
		.height = amh,
		.pixel_format = SG_PIXELFORMAT_RGBA32UI,
		.data = {.subimage = {{ {
			.ptr = anim_meta.data(),  // Your mat4 data here
			.size = amh * 2048 * sizeof(glm::ivec4)
		}}}}
		});

	auto atw = std::max(1, (int)ceil(sqrt(anim_time.size())));
	anim_time.reserve(atw * atw);
	animtimes = sg_make_image(sg_image_desc{
		.width = atw,
		.height = atw,
		.pixel_format = SG_PIXELFORMAT_R32F,
		.data = {.subimage = {{ {
			.ptr = anim_time.data(),  // Your mat4 data here
			.size = atw * atw * sizeof(float)
		}}}}
		});
	auto adw = std::max(1, (int)ceil(sqrt(anim_data.size())));
	anim_data.reserve(adw* adw);
	animdt = sg_make_image(sg_image_desc{
		.width = adw,
		.height = adw,
		.pixel_format = SG_PIXELFORMAT_RGBA32F,
		.data = {.subimage = {{ {
			.ptr = anim_data.data(),  // Your mat4 data here
			.size = adw * adw * sizeof(glm::vec4)
		}}}}
		});
	// weights animations:

	auto mtw = std::max(1, (int)ceil(sqrt(morphtarget_data.size())));
	morphtarget_data.reserve(mtw * mtw);
	morphdt = sg_make_image(sg_image_desc{
		.width = mtw,
		.height = mtw,
		.pixel_format = SG_PIXELFORMAT_R32F,
		.data = {.subimage = {{ {
			.ptr = morphtarget_data.data(),  // Your mat4 data here
			.size = mtw * mtw * sizeof(float)
		}}}}
		});

	// node: animationid-node map=>(idx, len), samplar.
}

inline gltf_object::gltf_object(gltf_class* cls) 
{
	this->nodeattrs.resize(cls->model.nodes.size());
	this->gltf_class_id = cls->instance_id;
}

void gltf_class::clear_me_buffers() {
    sg_destroy_buffer(positions);
    sg_destroy_buffer(normals);
    sg_destroy_buffer(colors);
    sg_destroy_buffer(indices);
    sg_destroy_buffer(texcoords);
    sg_destroy_buffer(node_metas);
    sg_destroy_buffer(joints);
    sg_destroy_buffer(jointNodes);
    sg_destroy_buffer(weights);

    sg_destroy_image(animap);
    sg_destroy_image(animtimes);
    sg_destroy_image(animdt);
    sg_destroy_image(morphdt);
    sg_destroy_image(skinInvs);
    sg_destroy_image(parents);

	if (atlas.id != graphics_state.dummy_tex.id)
		sg_destroy_image(atlas);
}