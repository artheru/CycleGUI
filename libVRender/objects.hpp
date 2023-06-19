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


void gltf_class::ProcessPrim( const tinygltf::Model& model, const tinygltf::Primitive& prim, gltfMesh mesh)
{
	gltf_class::gltfPrimitives gp;

	//! Only triangles supported.
	if (prim.mode != TINYGLTF_MODE_TRIANGLES)
		return;

	//! POSITION
	std::vector<glm::vec3> _positions;
	{
		const auto& accessor = model.accessors[prim.attributes.find("POSITION")->second];
		ReadGLTFData(model, accessor, _positions);
		gp.position = sg_make_buffer(sg_buffer_desc{
			.data = {
				_positions.data(),
				_positions.size()*sizeof(glm::vec3)
			}
		});
		gp.vcount = _positions.size();
	}

	//! Indices
	std::vector<unsigned int> _indices;
	if (prim.indices > -1)
	{
		const tinygltf::Accessor& indexAccessor = model.accessors[prim.indices];
		const tinygltf::BufferView& bufferView = model.bufferViews[indexAccessor.bufferView];
		const tinygltf::Buffer& buffer = model.buffers[bufferView.buffer];

		switch (indexAccessor.componentType)
		{
		case TINYGLTF_PARAMETER_TYPE_UNSIGNED_INT:
			for (int i = 0; i < indexAccessor.count; ++i)
				_indices.push_back(((uint32_t*)(buffer.data.data() + bufferView.byteOffset))[i]);
			break;
		case TINYGLTF_PARAMETER_TYPE_UNSIGNED_SHORT:
			for (int i = 0; i < indexAccessor.count; ++i)
				_indices.push_back(((uint16_t*)(buffer.data.data() + bufferView.byteOffset))[i]);
			break;
		default:
			std::cerr << "Unknown index component type : " << indexAccessor.componentType << " is not supported" << std::endl;
			return;
		}
	}
	else
	{
		//! Primitive without indices, creating them
		for (unsigned int i = 0; i < gp.vcount; ++i)
			_indices[i] = i;
	}
	gp.indices = sg_make_buffer(sg_buffer_desc{
		.type = SG_BUFFERTYPE_VERTEXBUFFER,
		.data = {_indices.data(), _indices.size() * 4},
		});
	gp.icount = _indices.size();


	//! NORMAL

	std::vector<glm::vec3> _normals(gp.vcount, glm::vec3(0.0f));
	{
		auto iter = prim.attributes.find("NORMAL");
		if (iter == prim.attributes.end())
		{
			//! You need to compute the normals
			for (size_t i = 0; i < gp.icount; i += 3)
			{
				unsigned int idx0 = _indices[i + 0];
				unsigned int idx1 = _indices[i + 1];
				unsigned int idx2 = _indices[i + 2];
				const auto& pos0 = _positions[idx0];
				const auto& pos1 = _positions[idx1];
				const auto& pos2 = _positions[idx2];
				const auto edge0 = glm::normalize(pos1 - pos0);
				const auto edge1 = glm::normalize(pos2 - pos0);
				const auto n = glm::normalize(glm::cross(edge0, edge1));
				_normals[idx0] += n;
				_normals[idx1] += n;
				_normals[idx2] += n;
			}
			for (int i = 0; i < gp.vcount; ++i)
				_normals[i] = glm::normalize(_normals[i]);
		}else
		{
			const auto& accessor = model.accessors[iter->second];
			ReadGLTFData(model, accessor, _normals);
		}
		gp.normal = sg_make_buffer(sg_buffer_desc{
			.data = {
				_normals.data(),
				_normals.size()*sizeof(glm::vec3)
			}
		});
	}

	//! TEXCOORD2
	std::vector<glm::vec2> _texCoords(gp.vcount, glm::vec2(0.0f));
	{
		auto iter = prim.attributes.find("TEXCOORD_0");
		if (iter == prim.attributes.end())
		{
			//! CubeMap projection
			for (unsigned int i = 0; i < gp.icount; ++i)
			{
				const auto& pos = _positions[i];
				float absX = std::fabs(pos.x);
				float absY = std::fabs(pos.y);
				float absZ = std::fabs(pos.z);

				int isXPositive = pos.x > 0.0f ? 1 : 0;
				int isYPositive = pos.y > 0.0f ? 1 : 0;
				int isZPositive = pos.z > 0.0f ? 1 : 0;

				float mapAxis{ 0.0f }, uc{ 0.0f }, vc{ 0.0f };
				//! Positive X
				if (isXPositive && absX >= absY && absX >= absZ)
				{
					//! u(0~1) goes from +z to -z
					//! v(0~1) goes from -y to +y
					mapAxis = absX;
					uc = -pos.z;
					vc = pos.y;
				}
				//! Negative X
				if (!isXPositive && absX >= absY && absX >= absZ)
				{
					//! u(0~1) goes from -z to +z
					//! v(0~1) goes from -y to +y
					mapAxis = absX;
					uc = pos.z;
					vc = pos.y;
				}
				//! Positive Y
				if (isYPositive && absY >= absX && absY >= absZ)
				{
					//! u(0~1) goes from -x to +x
					//! v(0~1) goes from +z to -z
					mapAxis = absY;
					uc = pos.x;
					vc = -pos.z;
				}
				//! Negative Y
				if (!isYPositive && absY >= absX && absY >= absZ)
				{
					//! u(0~1) goes from -x to +x
					//! v(0~1) goes from -z to +z
					mapAxis = absY;
					uc = pos.x;
					vc = pos.z;
				}
				//! Positive Z
				if (isZPositive && absY >= absX && absY >= absZ)
				{
					//! u(0~1) goes from -x to +x
					//! v(0~1) goes from -y to +y
					mapAxis = absZ;
					uc = pos.x;
					vc = pos.y;
				}
				//! Negative Z
				if (!isZPositive && absZ >= absX && absZ >= absY)
				{
					//! u(0~1) goes from +x to -x
					//! v(0~1) goes from -y to +y
					mapAxis = absZ;
					uc = -pos.x;
					vc = pos.y;
				}

				//! Convert range from (-1~1) into (0~1)
				float u = (uc / mapAxis + 1.0f) * 0.5f;
				float v = (vc / mapAxis + 1.0f) * 0.5f;

				_texCoords.push_back(glm::vec2(u, v));
			}
		}else
		{
			const auto& accessor = model.accessors[iter->second];
			ReadGLTFData(model, accessor, _texCoords);
		}
		gp.texcoord2 = sg_make_buffer(sg_buffer_desc{
			.data = {
				_texCoords.data(),
				_texCoords.size() * sizeof(glm::vec2)
			}
		});
	}


	//! TANGENT
	std::vector<glm::vec4> _tangents(gp.vcount, glm::vec4(0.0f));;
	{
		auto iter = prim.attributes.find("TANGENT");
		if (iter == prim.attributes.end())
		{
			//! Implementation in "Foundations of Game Engine Development : Volume2 Rendering"
			std::vector<glm::vec3> tangents(gp.vcount, glm::vec3(0.0f));
			std::vector<glm::vec3> bitangents(gp.vcount, glm::vec3(0.0f));
			for (size_t i = 0; i < gp.vcount; i += 3)
			{
				//! Local index
				unsigned int idx0 = _indices[i + 0];
				unsigned int idx1 = _indices[i + 1];
				unsigned int idx2 = _indices[i + 2];

				const auto& pos0 = _positions[idx0];
				const auto& pos1 = _positions[idx1];
				const auto& pos2 = _positions[idx2];

				const auto& uv0 = _texCoords[idx0];
				const auto& uv1 = _texCoords[idx1];
				const auto& uv2 = _texCoords[idx2];

				glm::vec3 e1 = pos1 - pos0, e2 = pos2 - pos0;
				float x1 = uv1.x - uv0.x, x2 = uv2.x - uv0.x;
				float y1 = uv1.y - uv0.y, y2 = uv2.y - uv0.y;

				const float r = 1.0f / (x1 * y2 - x2 * y1);
				glm::vec3 tangent = (e1 * y2 - e2 * y1) * r;
				glm::vec3 bitangent = (e2 * x1 - e1 * x2) * r;

				//! In case of degenerated UV coordinates
				if (x1 == 0 || x2 == 0 || y1 == 0 || y2 == 0)
				{
					const auto& nrm0 = _normals[idx0];
					const auto& nrm1 = _normals[idx1];
					const auto& nrm2 = _normals[idx2];
					const auto N = (nrm0 + nrm1 + nrm2) / glm::vec3(3.0f);

					if (std::abs(N.x) > std::abs(N.y))
						tangent = glm::vec3(N.z, 0, -N.x) / std::sqrt(N.x * N.x + N.z * N.z);
					else
						tangent = glm::vec3(0, -N.z, N.y) / std::sqrt(N.y * N.y + N.z * N.z);
					bitangent = glm::cross(N, tangent);
				}

				tangents[idx0] += tangent;
				tangents[idx1] += tangent;
				tangents[idx2] += tangent;
				bitangents[idx0] += bitangent;
				bitangents[idx1] += bitangent;
				bitangents[idx2] += bitangent;
			}

			for (unsigned int i = 0; i < gp.vcount; ++i)
			{
				const auto& n = _normals[i];
				const auto& t = tangents[i];
				const auto& b = bitangents[i];

				//! Gram schmidt orthogonalize
				glm::vec3 tangent = glm::normalize(t - n * glm::vec3(glm::dot(n, t)));
				//! Calculate the handedness
				float handedness = (glm::dot(glm::cross(t, b), n) > 0.0f) ? 1.0f : -1.0f;
				_tangents.emplace_back(tangent.x, tangent.y, tangent.z, handedness);
			}
		}
		else
		{
			const auto& accessor = model.accessors[iter->second];
			ReadGLTFData(model, accessor, _tangents);
		}
		gp.tangent = sg_make_buffer(sg_buffer_desc{
			.data = {
				_tangents.data(),
				_tangents.size() * sizeof(glm::vec4)
			}
		});
	}

	std::vector<glm::vec4> _colors(gp.vcount, glm::vec4(0.0f));;
	{
		auto iter = prim.attributes.find("COLOR_0");
		if (iter != prim.attributes.end())
		{
			const auto& accessor = model.accessors[iter->second];
			ReadGLTFData(model, accessor, _normals);
		}
		gp.color = sg_make_buffer(sg_buffer_desc{
			.data = {
				_colors.data(),
				_colors.size() * sizeof(glm::vec3)
			}
		});
	}

	gp.materialID = prim.material;
	mesh.primitives.push_back(gp);
}


inline void gltf_class::ImportMaterials(const tinygltf::Model& model)
{
	materials.reserve(model.materials.size());

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

		materials.emplace_back(material);
	}
}


void gltf_class::render_node(tinygltf::Node node)
{
	// pass 1: opaque objects:
	if (node.mesh != -1)
	{
		auto mesh = meshes[node.mesh];
		for (const auto& primitive : mesh.primitives)
		{
			auto material = materials[primitive.materialID];
			// todo: render primitive / mesh, + instance.
			// should consider each instance's seperate node rotation/translation.

			sg_update_buffer(instances, &(sg_range){
				.ptr = state.pos,
					.size = (size_t)state.cur_num_particles * sizeof(hmm_vec3)
			});
		}
	}
	for (int childNode : node.children)
		render_node(model.nodes[childNode]);

	// todo: pass 2: wboit for transmissive objects
}

inline void gltf_class::render()
{
	// generate instanceID buffer.
	
	int defaultScene = model.defaultScene > -1 ? model.defaultScene : 0;
	const auto& scene = model.scenes[defaultScene];
	for (auto nodeIdx : scene.nodes)
		render_node(model.nodes[nodeIdx]);
}

inline gltf_class::gltf_class(const tinygltf::Model& model, std::string name)
{
	this->model = model;
	this->name = name;

	for (const auto& mesh : model.meshes)
	{
		gltfMesh gmesh;
		for (const auto& prim : mesh.primitives)
		{
			ProcessPrim(model, prim, gmesh);
		}
		meshes.push_back(gmesh);
	}
	
	//! Import materials from the model
	ImportMaterials(model);

	//! Finally import images from the model
	for (int i=0; i<model.images.size(); ++i)
	{
		const auto& image = model.images[i];
		char buf[40];
		sprintf(buf, "cls_%s_%d", name, i);
		textures.push_back(sg_make_image(sg_image_desc{
			.width = image.width,
			.height = image.height,
			.num_mipmaps = 3,
			.pixel_format = SG_PIXELFORMAT_RGBA8,
			.sample_count = 1,
			.min_filter = SG_FILTER_LINEAR_MIPMAP_LINEAR,
			.mag_filter = SG_FILTER_LINEAR,
			.wrap_u = SG_WRAP_REPEAT,
			.wrap_v = SG_WRAP_REPEAT,
			.data={.subimage = {{ {
				.ptr = &image.image[0],
				.size = image.image.size()
			} }}},
			.label = buf,
		}));
	}

	instances = sg_make_buffer(sg_buffer_desc {
		.size = 65535 * sizeof(glm::mat4), 
			.usage = SG_USAGE_STREAM,
			.label = "instance-data"
	});
}