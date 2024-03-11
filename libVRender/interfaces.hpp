
// ██████   ██████  ██ ███    ██ ████████      ██████ ██       ██████  ██    ██ ██████
// ██   ██ ██    ██ ██ ████   ██    ██        ██      ██      ██    ██ ██    ██ ██   ██
// ██████  ██    ██ ██ ██ ██  ██    ██        ██      ██      ██    ██ ██    ██ ██   ██
// ██      ██    ██ ██ ██  ██ ██    ██        ██      ██      ██    ██ ██    ██ ██   ██
// ██       ██████  ██ ██   ████    ██         ██████ ███████  ██████   ██████  ██████


void AddPointCloud(std::string name, const point_cloud& what)
{
	auto t = pointclouds.get(name);
	if (t != nullptr) return; // if exist no add.

	auto capacity = what.isVolatile ? what.capacity : what.initN;
	// if (capacity < 65536) capacity = 65536;

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
	};

	gbuf->name = name;
	gbuf->position = what.position;
	gbuf->quaternion = what.quaternion;
	gbuf->flag = (1 << 4); // default: can select by point.

	memset(gbuf->cpuSelection, 0, sz*sz);
	pointclouds.add(name, gbuf);

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

void copyPartial(sg_buffer bufferSrc, sg_buffer bufferDst, int offset)
{
	_sg_buffer_t* buf = _sg_lookup_buffer(&_sg.pools, bufferDst.id);
	_sg_buffer_t* bufsrc = _sg_lookup_buffer(&_sg.pools, bufferSrc.id);
	GLenum gl_tgt = _sg_gl_buffer_target(buf->cmn.type);
	GLuint gl_buf = buf->gl.buf[buf->cmn.active_slot];
	SOKOL_ASSERT(gl_buf);
	_SG_GL_CHECK_ERROR();
	_sg_gl_cache_store_buffer_binding(gl_tgt);
	_sg_gl_cache_bind_buffer(gl_tgt, gl_buf);

	auto src = bufsrc->gl.buf[bufsrc->cmn.active_slot];
	glBindBuffer(GL_COPY_READ_BUFFER, src);

	glCopyBufferSubData(GL_COPY_READ_BUFFER, GL_ARRAY_BUFFER, 0, 0, offset);
	_sg_gl_cache_restore_buffer_binding(gl_tgt);
}

void AppendVolatilePoints(std::string name, int length, glm::vec4* xyzSz, uint32_t* color)
{
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	auto newSz = t->n + length;
	if (newSz > t->capacity) {
		// need refresh.
		auto capacity = 0;
		if (newSz < 65535) capacity = 65535;
		else capacity = (int)pow(4, ceil(log(newSz) / log(4)));

		// todo: use just one fucking buffer!.
		auto pcbuf = sg_make_buffer(
			sg_buffer_desc{ .size = capacity * sizeof(glm::vec4), .usage = SG_USAGE_STREAM, });
		copyPartial(t->pcBuf, pcbuf, t->n * sizeof(glm::vec4));

		auto cbuf = sg_make_buffer(
			sg_buffer_desc{ .size = capacity * sizeof(uint32_t), .usage = SG_USAGE_STREAM, } );
		copyPartial(t->pcBuf, pcbuf, t->n * sizeof(uint32_t));

		int sz = ceil(sqrt(capacity / 8));
		auto pcSelection = sg_make_image(sg_image_desc{
			.width = sz, .height = sz,
			.usage = SG_USAGE_STREAM,
			.pixel_format = SG_PIXELFORMAT_R8UI,
		});
		sg_destroy_buffer(t->pcBuf);
		sg_destroy_buffer(t->colorBuf);
		sg_destroy_image(t->pcSelection);
		t->pcBuf = pcbuf;
		t->colorBuf = cbuf;
		t->pcSelection = pcSelection;
		printf("refresh volatile pc %s from %d to %d\n", name.c_str(), t->capacity, capacity);
		t->capacity = capacity;
	}
	// assert(t->n + length <= t->capacity);
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


unsigned char* AppendSpotTexts(std::string name, int length, void* pointer)
{
	auto t = spot_texts.get(name);
	if (t == nullptr) {
		t = new me_stext();
		spot_texts.add(name, t);
	}
	unsigned char* ptr = (unsigned char* )pointer;
	for (int i = 0; i < length; ++i) {
		stext ss;
		ss.position = *(glm::vec3*)ptr; ptr += sizeof(glm::vec3);
		ss.color = *(uint32_t*)ptr; ptr += 4;
		ss.text= std::string(ptr + 4, ptr + 4 + *((int*)ptr)); ptr += *((int*)ptr) + 4;
		t->texts.push_back(ss);
	}
	return ptr;
}
void ClearSpotTexts(std::string name)
{
	auto t = spot_texts.get(name);
	if (t == nullptr) return;
	t->texts.clear();
}



void RemovePointCloud(std::string name) {
	auto t = pointclouds.get(name);
	if (t == nullptr) return;
	sg_destroy_image(t->pcSelection);
	sg_destroy_buffer(t->pcBuf);
	sg_destroy_buffer(t->colorBuf);
	delete[] t->cpuSelection;
	pointclouds.remove(name);
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


//  ██      ██ ███    ██ ███████ ███████ 
//  ██      ██ ████   ██ ██      ██      
//  ██      ██ ██ ██  ██ █████   ███████ 
//  ██      ██ ██  ██ ██ ██           ██ 
//  ███████ ██ ██   ████ ███████ ███████ 
//                                       
//                                       


unsigned char* AppendLines2Bunch(std::string name, int length, void* pointer)
{
	auto unitSz = sizeof(gpu_line_info); //(sizeof(glm::vec3) + sizeof(glm::vec3) + 4 + 4); //meta, color.
	//32bytes: start end meta color.
	auto t = line_bunches.get(name);
	if (t == nullptr) {
		t = new me_linebunch();
		t->line_buf = sg_make_buffer(
			sg_buffer_desc{ .size = 64 * unitSz, .usage = SG_USAGE_STREAM, });
		t->capacity = 64;
		t->n = 0;
		line_bunches.add(name, t);
	}

	auto newSz = t->n + length;
	if (newSz > t->capacity) {
		// need refresh.
		auto capacity = newSz < 4096 ? 4096 : (int)pow(4, ceil(log(newSz) / log(4)));

		auto line_buf = sg_make_buffer(
			sg_buffer_desc{ .size = capacity * unitSz, .usage = SG_USAGE_STREAM, });
		copyPartial(t->line_buf, line_buf, t->n * unitSz);
		
		sg_destroy_buffer(t->line_buf);
		t->line_buf = line_buf;

		printf("refresh line bunch %s from %d to %d\n", name.c_str(), t->capacity, capacity);
		t->capacity = capacity;
	}

	// assert(t->n + length <= t->capacity);
	int sz = length * unitSz; //start end arrow color
	updatePartial(t->line_buf, t->n * sizeof(glm::vec4), { pointer, (size_t)sz });
	t->n += length;
	
	return ((unsigned char*)pointer) + sz;
}

void ClearLineBunch(std::string name)
{
	auto t = line_bunches.get(name);
	if (t == nullptr) return;
	t->n = 0;
}

void AddStraightLine(std::string name, const line_info& what)
{
	auto t = line_pieces.get(name);
	auto lp = t == nullptr ? new me_line_piece : t;
	if (what.propStart.size() > 0) {
		auto ns = global_name_map.get(what.propStart);
		if (ns != nullptr)
			lp->propSt = ns->obj;
	}

	if (what.propEnd.size() > 0) {
		auto ns = global_name_map.get(what.propEnd);
		if (ns != nullptr)
			lp->propEnd = ns->obj;
	}
	lp->attrs.st = what.start;
	lp->attrs.end = what.end;
	lp->attrs.arrowType = what.arrowType;
	lp->attrs.width = what.width;
	lp->attrs.dash = what.dash;
	lp->attrs.color = what.color;
	lp->attrs.flags = 0;


	if (t == nullptr)
	{
		line_pieces.add(name, lp, 1);
	}
}

//  ██████  ██      ████████ ███████     ███    ███  ██████  ██████  ███████ ██      
// ██       ██         ██    ██          ████  ████ ██    ██ ██   ██ ██      ██      
// ██   ███ ██         ██    █████       ██ ████ ██ ██    ██ ██   ██ █████   ██      
// ██    ██ ██         ██    ██          ██  ██  ██ ██    ██ ██   ██ ██      ██      
//  ██████  ███████    ██    ██          ██      ██  ██████  ██████  ███████ ███████ 

void LoadModel(std::string cls_name, unsigned char* bytes, int length, ModelDetail detail)
{
	if (gltf_classes.get(cls_name) != nullptr) return; // already registered.
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
	auto cid = gltf_classes.getid(cls_name);
	if (cid == -1) return;
	auto t = gltf_classes.get(cid);

	auto oldobj = t->objects.get(name);
	if (oldobj == nullptr) {
		auto gltf_ptr = new gltf_object(t);
		gltf_ptr->name = name;
		gltf_ptr->cur_translation = gltf_ptr->position = new_position;
		gltf_ptr->cur_rotation = gltf_ptr->quaternion = new_quaternion;
		if (t->animations.size() > 0) {
			gltf_ptr->baseAnimId = gltf_ptr->playingAnimId = gltf_ptr->nextAnimId = 0;
			gltf_ptr->animationStartMs = ui_state.getMsFromStart();
		}
		t->objects.add(name, gltf_ptr, cid + 1000);
	}else
	{
		oldobj->cur_translation = oldobj->position = new_position;
		oldobj->cur_rotation = oldobj->quaternion = new_quaternion;
	}
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
	auto slot = global_name_map.get(name);
	if (slot == nullptr) return;
	slot->obj->position = new_position;
	slot->obj->quaternion = new_quaternion;
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

	std::cout << "begin:" << state_name << std::endl;
}
void PopWorkspace()
{
	ui_state.workspace_state.pop();
}


void SetObjectSelected(std::string name)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;

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
			testgltf->flags |= (1 << 3);
			//ui_state.selected.insert({ mapping->type, pointclouds.getid(name),false });
		}
	}
}

void SetObjectShine(std::string name, uint32_t color)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;

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
			testgltf->flags |= 2;
			testgltf->shine = color;
		}
	}
}


uint32_t convertTo12BitColor(uint32_t originalColor) {
	// Mask and shift to extract RGB channels
	uint8_t red = (originalColor >> 16) & 0xFF;
	uint8_t green = (originalColor >> 8) & 0xFF;
	uint8_t blue = originalColor & 0xFF;

	// Downsample RGB channels to the range 0-15
	uint8_t downsampledRed = (red * 15) / 255;
	uint8_t downsampledGreen = (green * 15) / 255;
	uint8_t downsampledBlue = (blue * 15) / 255;

	// Combine downsampled channels into a 12-bit color
	uint32_t resultColor = 0;
	resultColor |= (downsampledRed << 8) & 0xF00;
	resultColor |= (downsampledGreen << 4) & 0x0F0;
	resultColor |= downsampledBlue & 0x00F;

	return resultColor;
}

void SetSubObjectBorderShine(std::string name, int subid, bool border, bool shine, uint32_t color)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;

	if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			testgltf->nodeattrs[subid].flag = ((int)testgltf->nodeattrs[subid].flag) & (1 << 3) | (border ? 1 : 0) | (shine ? (2 | (convertTo12BitColor(color) << 8)) : 0);
		}
	}
}

void CancelSubObjectBorderShine(std::string name, int subid)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;

	if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			testgltf->nodeattrs[subid].flag = ((int)testgltf->nodeattrs[subid].flag) & (1 << 3);
		}
	}
}

void BringObjectFront(std::string name)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;

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
			testgltf->flags |= 2;
		}
	}
}

void SetObjectBorder(std::string name)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;

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
			testgltf->flags |= 1;
		}
	}
}



void SetObjectSelectable(std::string name, bool selectable)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;
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
				testgltf->flags |= (1 << 4);
			else
				testgltf->flags &= ~(1 << 4);
		}
	}
}

// todo: ad
void SetObjectSubSelectable(std::string name, bool subselectable)
{
	auto mapping = global_name_map.get(name);
	if (mapping == nullptr) return;
	auto& wstate = ui_state.workspace_state.top();
	wstate.sub_hoverables.insert(name);

	if (mapping->type == 0) {
		auto testpc = pointclouds.get(name);
		if (testpc != nullptr)
		{
			if (subselectable)
				testpc->flag |= (1 << 8);
			else
				testpc->flag &= ~(1 << 8);
		}
	}
	else if (mapping->type >= 1000)
	{
		auto testgltf = gltf_classes.get(mapping->type - 1000)->objects.get(name);
		if (testgltf != nullptr)
		{
			if (subselectable)
				testgltf->flags |= (1 << 5);
			else
				testgltf->flags &= ~(1 << 5);
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
			auto& t = std::get<0>(objs.ls[j]);
			auto& name = std::get<1>(objs.ls[j]);
			if (wstate.hoverables.find(name) != wstate.hoverables.end())
				t->flags |= (1 << 4);
			else
				t->flags &= ~(1 << 4);
			if (wstate.sub_hoverables.find(name) != wstate.hoverables.end())
				t->flags |= (1 << 5);
			else
				t->flags &= ~(1 << 5);
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

void RemoveModelObject(std::string name)
{
	// todo.
}