
void AddPointCloud(std::string name, point_cloud& what)
{
	auto it = pointClouds.find(name);
	if (it != pointClouds.end())
		pointClouds.erase(it);

	gpu_point_cloud gbuf;
	gbuf.n = what.x_y_z_Sz.size();
	gbuf.position = what.position;
	gbuf.quaternion = what.quaternion;

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
	}
}

void ModifyPointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion) {
	auto it = pointClouds.find(name);
	if (it != pointClouds.end()) {
		auto& gpu = it->second;
		gpu.position = new_position;
		gpu.quaternion = new_quaternion;
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
	
	classes[cls_name] = new gltf_class(model, cls_name, detail.center, detail.scale, detail.rotate);
}

void PutModelObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion)
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

// Geometries:
void PutBoxGeometry(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float length, float width, float height)
{
	
}

void PutShereGeometry(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float radius)
{

}

void PutConeGeometry(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float radius, float height)
{

}

void PutCylinderGeometry(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float radius, float height)
{

}

void PutExtrudedGeometry(std::string name, glm::vec3 new_position, glm::quat new_quaternion, std::vector<glm::vec2>& shape, float height)
{
	
}

void PutExtrudedBorderGeometry(std::string name, glm::vec3 new_position, glm::quat new_quaternion, std::vector<glm::vec2>& shape, float height)
{

}

void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time)
{

}

enum object_state
{
	on_hover, after_click, always
};

void PushWorkspaceState(std::string state_name)
{
	
}

void SetObjectProperty(std::string name, std::string properties); // draw_walkable(only for point cloud)
void SetObjectShine(std::string name, glm::vec3 color, float value, std::string condition){}; //condition: hover, static, click, selected
void SetObjectBorder(std::string name, glm::vec3 color, std::string condition){};
void SetObjectBehaviour(std::string name, std::string behaviour){}; // including pointcloud.
// movable(xyz), rotatable(xyz), selectable/sub_selectable, snapping, have_action(right click mouse),
// this also triggers various events for cycle ui.

// Display a billboard form following the object, if object visible, also, only show 10 billboard top most.
void SetObjectBillboard(std::string name, std::string billboardFormName, std::string behaviour){}; //

void PopWorkspaceState(std::string state_name)
{

}
void SetWorkspaceSelector(std::string selector)
{
	// circle painter, rect selector, click selector
}

void FocusObject(std::string name){}


void DiscardObject(std::string name) {};


void SetObjectBaseAnimation(std::string name, std::string state)
{
	
}
void PlayObjectEmote(std::string name, std::string emote)
{
	
}
void SetObjectWeights(std::string name, std::string state)
{
	
}