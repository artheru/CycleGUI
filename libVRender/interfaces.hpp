
void AddPointCloud(std::string name, point_cloud& what)
{
	sg_buffer_desc vbufDesc = {
		.data = { what.x_y_z_Sz.data(), what.x_y_z_Sz.size() * sizeof(glm::vec4)},
	};
	sg_buffer_desc cbufDesc = {
		.data = { what.color.data(), what.color.size() * sizeof(glm::vec4) },
	};
	me_pcRecord* gbuf= new me_pcRecord{
		.n = (int)what.x_y_z_Sz.size(),
		.pcBuf = sg_make_buffer(&vbufDesc),
		.colorBuf = sg_make_buffer(&cbufDesc),
		.position = what.position,
		.quaternion = what.quaternion,
		.flag = what.flag,
	};

	pointclouds.add(name, gbuf);

	std::cout << "Added point cloud '" << name << "'" << std::endl;
}

void RemovePointCloud(std::string name) {
	pointclouds.remove(name);
}

void ModifyPointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion) {
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	t->position = new_position;
	t->quaternion = new_quaternion;
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
	
	gltf_classes.add(cls_name, new gltf_class(model, cls_name, detail.center, detail.scale, detail.rotate));
}

void PutModelObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion)
{
	// should be synced into main thread.
	auto t = gltf_classes.get(cls_name);
	if (t == nullptr) return;
	t->objects.add(name, new gltf_object{
		.position = new_position,
		.quaternion = new_quaternion,
		.weights = std::vector<float>(t->morphTargets,0),
	});
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


void SetObjectShineOnHover(std::string name, glm::vec3 color, float value);


void BringObjectFront(std::string name, std::string condition){}

void SetObjectBorder(std::string name, glm::vec3 color, std::string condition)
{
};
void SetObjectBehaviour(std::string name, std::string behaviour){}; // including pointcloud.
// movable(xyz), rotatable(xyz), selectable/sub_selectable, snapping, have_action(right click mouse),
// this also triggers various events for cycle ui.

// Display a billboard form following the object, if object visible, also, only show 10 billboard top most.
void SetObjectBillboard(std::string name, std::string billboardFormName, std::string behaviour){}; //

void PopWorkspaceState(std::string state_name)
{

}
void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius = 0)
{
	ui_state.workspace_state.top().selecting_mode = mode;
	ui_state.workspace_state.top().paint_selecting_radius = painter_radius;
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