
void AddPointCloud(std::string name, point_cloud what)
{
	auto it = pointClouds.find(name);
	if (it != pointClouds.end())
		pointClouds.erase(it);

	gpu_point_cloud gbuf;
	gbuf.pc = what;
	sg_buffer_desc vbufDesc = {
		.data = { what.x_y_z_Sz.data(), what.x_y_z_Sz.size() * sizeof(glm::vec4)},
	};
	gbuf.pcBuf = sg_make_buffer(&vbufDesc);
	sg_buffer_desc cbufDesc = {
		.data = { what.color.data(), what.color.size() * sizeof(glm::vec4) },
	};
	gbuf.colorBuf = sg_make_buffer(&cbufDesc);
	pointClouds[name] = gbuf;

	std::cout << "Added point cloud '" << name << "'" << std::endl;
}

void RemovePointCloud(std::string name) {
	auto it = pointClouds.find(name);
	if (it != pointClouds.end()) {
		pointClouds.erase(it);
		std::cout << "Removed point cloud '" << name << "'" << std::endl;
	}
	else {
		std::cout << "Point cloud '" << name << "' not found" << std::endl;
	}
}

void ModifyPointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion) {
	auto it = pointClouds.find(name);
	if (it != pointClouds.end()) {
		auto& gpu = it->second;
		gpu.pc.position = new_position;
		gpu.pc.quaternion = new_quaternion;
		std::cout << "Modified point cloud '" << name << "'" << std::endl;
	}
	else {
		std::cout << "Point cloud '" << name << "' not found" << std::endl;
	}
}


void LoadModel(std::string cls_name, unsigned char* bytes, int length, ModelDetail detail)
{
	// should be synced into main thread.
	tinygltf::Model model;
	tinygltf::TinyGLTF loader;
	std::string err;
	std::string warn;
	bool res = loader.LoadBinaryFromMemory(&model, &err, &warn, bytes, length);
	if (!res) {
		std::cerr << "err loading class " << cls_name << ":" << err << std::endl;
		return;
	}
	
	classes[cls_name] = new gltf_class(model, cls_name, detail.center, detail.radius);
}

void PutObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion)
{
	// should be synced into main thread.
	auto iter = classes.find(cls_name);
	if (iter == classes.end()) return;// no such class.

	iter->second->objects[name] = gltf_object{
		.position = new_position,
		.quaternion = new_quaternion,
		.weights = std::vector<float>(iter->second->morphTargets,0),
	};
}
void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time)
{
	
}

void SetObjectBaseAnimation(std::string name, std::string state)
{
	
}
void PlayObjectEmote(std::string name, std::string emote)
{
	
}
void SetObjectWeights(std::string name, std::string state)
{
	
}