
// ██████   ██████  ██ ███    ██ ████████      ██████ ██       ██████  ██    ██ ██████
// ██   ██ ██    ██ ██ ████   ██    ██        ██      ██      ██    ██ ██    ██ ██   ██
// ██████  ██    ██ ██ ██ ██  ██    ██        ██      ██      ██    ██ ██    ██ ██   ██
// ██      ██    ██ ██ ██  ██ ██    ██        ██      ██      ██    ██ ██    ██ ██   ██
// ██       ██████  ██ ██   ████    ██         ██████ ███████  ██████   ██████  ██████


void AddPointCloud(std::string name, const point_cloud& what)
{
	auto capacity = what.isVolatile ? what.capacity : what.initN;

	auto pcbuf = sg_make_buffer(what.isVolatile ?
		sg_buffer_desc{ .size = capacity * sizeof(glm::vec4), .usage = SG_USAGE_STREAM, } :
		sg_buffer_desc{ .data = { what.x_y_z_Sz, capacity * sizeof(glm::vec4) }, });
	
	auto cbuf = sg_make_buffer(what.isVolatile ?
		sg_buffer_desc{ .size = capacity * sizeof(uint32_t), .usage = SG_USAGE_STREAM, } :
		sg_buffer_desc{ .data = { what.color, capacity * sizeof(uint32_t) }, });

	int sz = ceil(sqrt(capacity / 8));
	me_pcRecord* gbuf= new me_pcRecord{
		.isVolatile = what.isVolatile,
		.capacity = (int)capacity,
		.n = (int)what.initN,
		.pcBuf = pcbuf,
		.colorBuf = cbuf,
		.pcSelection = sg_make_image(sg_image_desc{
			.width = sz, .height = sz,
			.usage = SG_USAGE_STREAM,
			.pixel_format = SG_PIXELFORMAT_R8UI,
		}),
		.cpuSelection = new unsigned char[sz*sz],
		.position = what.position,
		.quaternion = what.quaternion,
		.flag = 0,
	};

	memset(gbuf->cpuSelection, 0, sz*sz);
	name_map.add(name, new namemap_t{ 0, pointclouds.add(name, gbuf) });

	std::cout << "Added point cloud '" << name << "'" << std::endl;
}

void updatePartial(sg_buffer buffer, int offset, const sg_range& data)
{
	_sg_buffer_t* buf = _sg_lookup_buffer(&_sg.pools, buffer.id);
	GLenum gl_tgt = _sg_gl_buffer_target(buf->cmn.type);
	GLuint gl_buf = buf->gl.buf[buf->cmn.active_slot];
	SOKOL_ASSERT(gl_buf);
	_SG_GL_CHECK_ERROR();
	_sg_gl_cache_store_buffer_binding(gl_tgt);
	_sg_gl_cache_bind_buffer(gl_tgt, gl_buf);
	glBufferSubData(gl_tgt, offset, (GLsizeiptr)data.size, data.ptr);
	_sg_gl_cache_restore_buffer_binding(gl_tgt);
}

void AppendVolatilePoints(std::string name, int length, glm::vec4* xyzSz, uint32_t* color)
{
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	assert(t->n + length <= t->capacity);
	updatePartial(t->pcBuf, t->n * sizeof(glm::vec4), { xyzSz, length * sizeof(glm::vec4) });
	updatePartial(t->colorBuf, t->n * sizeof(uint32_t), { color, length * sizeof(uint32_t) });
	t->n += length;
}
void ClearVolatilePoints(std::string name)
{
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	t->n = 0;
}

void RemovePointCloud(std::string name) {
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	sg_destroy_image(t->pcSelection);
	sg_destroy_buffer(t->pcBuf);
	sg_destroy_buffer(t->colorBuf);
	delete[] t->cpuSelection;
	pointclouds.remove(name);
	name_map.remove(name);
}

void ManipulatePointCloud(std::string name, glm::vec3 new_position, glm::quat new_quaternion) {
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	t->position = new_position;
	t->quaternion = new_quaternion;
}

void SetPointCloudBehaviour(std::string name, bool showHandle, bool selectByHandle, bool selectByPoints)
{
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	if (showHandle)
		t->flag |= (1 << 3);
	else
		t->flag &= ~(1 << 3);

	if (selectByHandle)
		t->flag |= (1 << 5);
	else
		t->flag &= ~(1 << 5);

	if (selectByPoints)
		t->flag |= (1 << 4);
	else
		t->flag &= ~(1 << 4);
}

//  ██████  ██      ████████ ███████     ███    ███  ██████  ██████  ███████ ██      
// ██       ██         ██    ██          ████  ████ ██    ██ ██   ██ ██      ██      
// ██   ███ ██         ██    █████       ██ ████ ██ ██    ██ ██   ██ █████   ██      
// ██    ██ ██         ██    ██          ██  ██  ██ ██    ██ ██   ██ ██      ██      
//  ██████  ███████    ██    ██          ██      ██  ██████  ██████  ███████ ███████ 

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
	auto cid = gltf_classes.getid(cls_name);
	auto oid = t->objects.add(name, new gltf_object{
		.position = new_position,
		.quaternion = new_quaternion,
		.weights = std::vector<float>(t->morphTargets,0),

		.flags={0 | (-1<<8),-1,-1,-1,-1,-1,-1,-1}
		});
	name_map.add(name, new namemap_t{ cid+1000, oid });
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

void BeginWorkspace(int id, std::string state_name)
{
	// effectively eliminate action state.
	_clear_action_state();

	ui_state.workspace_state.push(workspace_state_desc{ .id = id, .name = state_name });
}
void PopWorkspace()
{
	ui_state.workspace_state.pop();
}


void SetObjectSelected(std::string name)
{
	auto mapping = name_map.get(name);

	if (mapping->type == 0) {
		auto pcid = pointclouds.getid(name);
		auto testpc = pointclouds.get(pcid);
		if (testpc != nullptr)
		{
			testpc->flag |= (1 << 6);// select as a whole
			//ui_state.selected.insert({ 0,pcid,false });
		}
	}
	else if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			testgltf->flags[0] |= (1 << 3);
			//ui_state.selected.insert({ mapping->type, pointclouds.getid(name),false });
		}
	}
}

void SetObjectShine(std::string name, uint32_t color)
{
	auto mapping = name_map.get(name);
	auto f4 = ImGui::ColorConvertU32ToFloat4(color);
	auto c_v4 = glm::vec4(f4.x, f4.y, f4.z, f4.w);

	if (mapping->type==0){
		auto testpc = pointclouds.get(name);
		if (testpc != nullptr)
		{
			testpc->flag |= 2;
			testpc->shine_color = c_v4;
		}
	}else if (mapping->type>=1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf!=nullptr)
		{
			testgltf->flags[0] |= 2;
			testgltf->shineColor[0] = color;
		}
	}
}


void SetSubObjectBorderShine(std::string name, int subid, bool border, uint32_t color)
{
	auto mapping = name_map.get(name);

	if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			for (int i = 0; i<7; ++i)
			{
				if ((testgltf->flags[i+1] >> 8) == subid || (testgltf->flags[i + 1] >> 8) == -1)
				{
					testgltf->flags[i + 1] = (border ? 1 : 0) | 2 | (subid<<8);
					testgltf->shineColor[i + 1] = color;
				};
			}
		}
	}
}

void CancelSubObjectBorderShine(std::string name, int subid)
{
	auto mapping = name_map.get(name);

	if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			int i = 0;
			for (; i < 7; ++i)
			{
				if ((testgltf->flags[i + 1] >> 8) == subid)
				{
					testgltf->flags[i + 1] = -1; //subid is cleared.
				}
			}
		}
	}
}

void SetSubObjectBorder(std::string name, int subid)
{
	auto mapping = name_map.get(name);
	if (mapping->type == 0) {
		auto testpc = pointclouds.get(name);
		if (testpc != nullptr)
		{
			testpc->flag |= 1;
		}
	}
	else if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			testgltf->flags[0] |= 1;
		}
	}
}

void BringObjectFront(std::string name)
{
	auto mapping = name_map.get(name);
	if (mapping->type == 0) {
		auto testpc = pointclouds.get(name);
		if (testpc != nullptr)
		{
			testpc->flag |= 2;
		}
	}
	else if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			testgltf->flags[0] |= 2;
		}
	}
}

void SetObjectBorder(std::string name)
{
	auto mapping = name_map.get(name);
	if (mapping->type == 0) {
		auto testpc = pointclouds.get(name);
		if (testpc != nullptr)
		{
			testpc->flag |= 1;
		}
	}
	else if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			testgltf->flags[0] |= 1;
		}
	}
}



void SetObjectSelectable(std::string name, bool selectable)
{
	auto mapping = name_map.get(name);
	auto& wstate = ui_state.workspace_state.top();
	wstate.hoverables.insert(name);

	if (mapping->type == 0) {
		auto testpc = pointclouds.get(name);
		if (testpc != nullptr)
		{
			if (selectable)
				testpc->flag |= (1 << 7);
			else
				testpc->flag &= ~(1 << 7);
		}
	}
	else if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			if (selectable)
				testgltf->flags[0] |= (1 << 4);
			else
				testgltf->flags[0] &= ~(1 << 4);
		}
	}
}

void SetObjectSubSelectable(std::string name)
{
	auto mapping = name_map.get(name);
	auto& wstate = ui_state.workspace_state.top();
	wstate.sub_hoverables.insert(name);

	if (mapping->type == 0) {
		auto testpc = pointclouds.get(name);
		if (testpc != nullptr)
		{
			testpc->flag |= (1 << 8);
		}
	}
	else if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			testgltf->flags[0] |= (1 << 5);
		}
	}
}


// movable(xyz), rotatable(xyz), selectable/sub_selectable, snapping, have_action(right click mouse),
// this also triggers various events for cycle ui.

// Display a billboard form following the object, if object visible, also, only show 10 billboard top most.
void SetObjectBillboard(std::string name, std::string billboardFormName, std::string behaviour){}; //

void PopWorkspaceState(std::string state_name)
{
	// todo: not finished.
	auto& wstate = ui_state.workspace_state.top();

	// prepare flags for selectable/subselectables.
	for (int i = 0; i < pointclouds.ls.size(); ++i)
	{
		if (wstate.hoverables.find(std::get<1>(pointclouds.ls[i]))!= wstate.hoverables.end())
			pointclouds.get(i)->flag |= (1 << 7);
		else
			pointclouds.get(i)->flag &= ~(1 << 7);

		if (wstate.sub_hoverables.find(std::get<1>(pointclouds.ls[i])) != wstate.sub_hoverables.end())
			pointclouds.get(i)->flag |= (1 << 8);
		else
			pointclouds.get(i)->flag &= ~(1 << 8);
	}

	for (int i = 0; i < gltf_classes.ls.size(); ++i)
	{
		auto objs = gltf_classes.get(i)->objects;
		for (int j = 0; j < objs.ls.size(); ++j)
		{
			if (wstate.hoverables.find(std::get<1>(objs.ls[i])) != wstate.hoverables.end())
				objs.get(i)->flags[0] |= (1 << 4);
			else
				objs.get(i)->flags[0] &= ~(1 << 4);
			if (wstate.sub_hoverables.find(std::get<1>(objs.ls[i])) != wstate.hoverables.end())
				objs.get(i)->flags[0] |= (1 << 5);
			else
				objs.get(i)->flags[0] &= ~(1 << 5);
		}
	}
}
void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius)
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