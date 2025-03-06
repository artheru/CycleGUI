
#include "me_impl.h"
#include "utilities.h"


// /// <summary>
// /// 
//  ██████  ██████  ███    ███ ███    ███  ██████  ███    ██ 
// ██      ██    ██ ████  ████ ████  ████ ██    ██ ████   ██ 
// ██      ██    ██ ██ ████ ██ ██ ████ ██ ██    ██ ██ ██  ██ 
// ██      ██    ██ ██  ██  ██ ██  ██  ██ ██    ██ ██  ██ ██ 
//  ██████  ██████  ██      ██ ██      ██  ██████  ██   ████ 
// /// </summary>
// ///

void NotifyWorkspaceUpdated()
{
	shared_graphics.allowData = true;
}
void actualRemove(namemap_t* nt)
{
	RouteTypes(nt, 
		[&]	{
			// point cloud.
			auto t = (me_pcRecord*)nt->obj;
			sg_destroy_image(t->pcSelection);
			sg_destroy_buffer(t->pcBuf);
			sg_destroy_buffer(t->colorBuf);
			delete[] t->cpuSelection;
			pointclouds.remove(nt->obj->name);
		}, [&](int class_id)
		{
			// gltf
			auto t = gltf_classes.get(class_id);
			t->objects.remove(nt->obj->name);
		}, [&]
		{
			// line piece.
			line_pieces.remove(nt->obj->name);
		}, [&]
		{
			// sprites;
			// auto im = sprites.get(name);
			sprites.remove(nt->obj->name);
			// delete im;
		},[&]
		{
			// spot texts.
			spot_texts.remove(nt->obj->name);
		},[&]
		{
			// geometry.
		});
}

void RemoveObject(std::string name)
{
	auto obj = global_name_map.get(name);
	actualRemove(obj);
}

void RemoveNamePattern(std::string name)
{
	// batch remove object.
	for (int i = 0; i < global_name_map.ls.size(); ++i)
		if (wildcardMatch(global_name_map.getName(i), name))
			actualRemove(global_name_map.get(i));
}

void AnchorObject(std::string earth, std::string moon, glm::vec3 rel_position, glm::quat rel_quaternion)
{
	// add anchoring.
	auto q = global_name_map.get(earth);
	if (q == nullptr) return;

	
	auto p = global_name_map.get(moon);
	if (p == nullptr) return;

	
	if (p->obj->anchor.obj!=nullptr){
		p->obj->anchor.remove_from_obj();
		p->obj->anchor.obj = nullptr;
	}

	auto oidx = q->obj->references.size();
	q->obj->references.push_back({ .accessor = nullptr, .offset = (size_t) -2, .ref = &p->obj->anchor});
	p->obj->anchor.obj_reference_idx = oidx;
	p->obj->anchor.obj = q->obj;

	p->obj->offset_pos = rel_position;
	p->obj->offset_rot = rel_quaternion;
}

void TransformSubObject(std::string objectNamePattern, uint8_t selectionMode, std::string subObjectName,
	int subObjectId, uint8_t actionMode, uint8_t transformType,
	glm::vec3 translation, glm::quat rotation, float timeMs)
{
	// Loop through all objects in the name map
	for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto mapping = global_name_map.get(i);
		std::string objName = global_name_map.getName(i);

		// Check if this object matches the pattern
		if (wildcardMatch(objName, objectNamePattern)) {
			RouteTypes(mapping,
				[&] {
					// Point cloud - not applicable for node transformation
				},
				[&](int class_id) {
					// GLTF object - apply sub-object transform
					auto t = (gltf_object*)mapping->obj;
					auto gltf_cls = gltf_classes.get(class_id);
					if (!gltf_cls) return;

					// Find the node index based on selection mode
					int nodeIndex = -1;
					if (selectionMode == 0) { // ByName
						// Find node by name
						for (size_t idx = 0; idx < gltf_cls->model.nodes.size(); idx++) {
							if (gltf_cls->model.nodes[idx].name == subObjectName) {
								nodeIndex = idx;
								break;
							}
						}
					}
					else { // ById
						// Use direct node ID
						if (subObjectId >= 0 && subObjectId < t->nodeattrs.size()) {
							nodeIndex = subObjectId;
						}
					}

					// Apply the transformation if we found a valid node
					if (nodeIndex >= 0 && nodeIndex < t->nodeattrs.size()) {
						auto& nodeAttr = t->nodeattrs[nodeIndex];

						if (actionMode == 1) { // Revert mode
							// Reset to default values from the original model
							auto& node = gltf_cls->model.nodes[nodeIndex];

							// Reset translation
							if (node.translation.size() == 3) {
								nodeAttr.translation = glm::vec3(
									node.translation[0],
									node.translation[1],
									node.translation[2]
								);
							}
							else {
								nodeAttr.translation = glm::vec3(0.0f);
							}

							// Reset rotation
							if (node.rotation.size() == 4) {
								nodeAttr.quaternion = glm::quat(
									node.rotation[3], // w
									node.rotation[0], // x
									node.rotation[1], // y
									node.rotation[2]  // z
								);
							}
							else {
								nodeAttr.quaternion = glm::quat(1.0f, 0.0f, 0.0f, 0.0f);
							}
						}
						else { // Set mode
							// Apply the specified transformation
							if (transformType == 0 || transformType == 1) { // Position or PosRot
								nodeAttr.translation = translation;
							}

							if (transformType == 0 || transformType == 2) { // Rotation or PosRot
								nodeAttr.quaternion = rotation;
							}
						}
					}
				},
				[&] {
					// Line bunch - not applicable
				},
				[&] {
					// Sprites - not applicable
				},
				[&] {
					// Spot texts - not applicable
				},
				[&] {
					// Other types - not applicable
				});
		}
	}
}

void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time, uint8_t type, uint8_t coord)
{
	auto slot = global_name_map.get(name);
	if (slot == nullptr) return;

	// remove anchoring.
	if (slot->obj->anchor.obj!=nullptr){
		slot->obj->anchor.remove_from_obj();
		slot->obj->anchor.obj = nullptr;
	}

	slot->obj->previous_position = slot->obj->target_position;
	slot->obj->previous_rotation = slot->obj->target_rotation;

	if (type == 0 || type == 1) //pos enabled.
	{
		if (coord == 0)
		{
			slot->obj->target_position = new_position;
		}else
		{
			slot->obj->target_position = slot->obj->target_position + slot->obj->target_rotation * new_position;
		}
	}
	if (type == 0 || type == 2)
	{
		if (coord == 0)
		{
			slot->obj->target_rotation = new_quaternion;
		}
		else
		{
			slot->obj->target_rotation = slot->obj->target_rotation * new_quaternion;
		}
	}

	slot->obj->target_start_time = ui.getMsFromStart();
	if (time > 5000) {
		printf("move object %s time exceeds max allowed animation time=5s.\n");
		time = 5000;
	}
	slot->obj->target_require_completion_time = slot->obj->target_start_time + 100;
}

// selection doesn't go with wstate, it's very temporary.
void SetObjectSelected(std::string patternname)
{	
    for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto mapping = global_name_map.get(i);
        if (wildcardMatch(global_name_map.getName(i), patternname)) {
			RouteTypes(mapping, 
				[&]	{
					// point cloud.
					auto t = (me_pcRecord*)mapping->obj;
					t->flag |= (1 << 6);// select as a whole
				}, [&](int class_id)
				{
					// gltf
					auto t = (gltf_object*)mapping->obj;
					t->flags[working_viewport_id] |= (1 << 3);
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
					auto t = (me_sprite*)mapping->obj;
					t->per_vp_stat[working_viewport_id] |= 1 << 1;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
		}
	}
}

// shine is a property.
void SetObjectShine(std::string patternname, bool use, uint32_t color)
{
	auto f4 = ImGui::ColorConvertU32ToFloat4(color);
	auto c_v4 = glm::vec4(f4.x, f4.y, f4.z, f4.w);
	
    for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto mapping = global_name_map.get(i);
        if (wildcardMatch(global_name_map.getName(i), patternname)) {
			RouteTypes(mapping, 
				[&]	{
					// point cloud.
					auto t = (me_pcRecord*)mapping->obj;
					if (use) {
						t->flag |= 2;
						t->shine_color = c_v4;
					}
					else t->flag &= ~2;
				}, [&](int class_id)
				{
					// gltf
					auto t = (gltf_object*)mapping->obj;
					if (use){
						t->flags[working_viewport_id] |= 2;
						t->shine = color;
					}else t->flags[working_viewport_id] &= ~2;
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
		}
	}
}


void BringObjectFront(std::string patternname, bool bring2front)
{
    for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto mapping = global_name_map.get(i);
        if (wildcardMatch(global_name_map.getName(i), patternname)) {
		RouteTypes(mapping, 
			[&]	{
				// point cloud.
				auto t = (me_pcRecord*)mapping->obj;
				if (bring2front)
					t->flag |= (1 << 2);
				else
					t->flag &= ~(1 << 2);
			}, [&](int class_id)
			{
				// gltf
				auto t = (gltf_object*)mapping->obj;
				if (bring2front)
					t->flags[working_viewport_id] |= (1 << 2);
				else 
					t->flags[working_viewport_id] &= ~(1 << 2);
			}, [&]
			{
				// line bunch.
			}, [&]
			{
				// sprites;
			},[&]
			{
				// spot texts.
			},[&]
			{
				// widgets.remove(name);
			});
		}
	}
}

void SetObjectBorder(std::string patternname, bool use)
{
    for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto tname = global_name_map.get(i);
        if (wildcardMatch(global_name_map.getName(i), patternname)) {

		RouteTypes(tname, 
			[&]	{
				// point cloud.
				auto t = (me_pcRecord*)tname->obj;
				if (use)
					t->flag |= 1;
				else t->flag &= ~1;
			}, [&](int class_id)
			{
				// gltf
				auto t = (gltf_object*)tname->obj;
				if (use) t->flags[working_viewport_id] |= 1;
				else t->flags[working_viewport_id] &= ~1;
			}, [&]
			{
				// line bunch.
			}, [&]
			{
				// sprites;
			},[&]
			{
				// spot texts.
			},[&]
			{
				// widgets.remove(name);
			});
			}
	}
}


// ██     ██  ██████  ██████  ██   ██ ███████ ██████   █████   ██████ ███████       ███████ ████████  █████   ██████ ██   ██ 
// ██     ██ ██    ██ ██   ██ ██  ██  ██      ██   ██ ██   ██ ██      ██            ██         ██    ██   ██ ██      ██  ██  
// ██  █  ██ ██    ██ ██████  █████   ███████ ██████  ███████ ██      █████   █████ ███████    ██    ███████ ██      █████   
// ██ ███ ██ ██    ██ ██   ██ ██  ██       ██ ██      ██   ██ ██      ██                 ██    ██    ██   ██ ██      ██  ██  
//  ███ ███   ██████  ██   ██ ██   ██ ███████ ██      ██   ██  ██████ ███████       ███████    ██    ██   ██  ██████ ██   ██ 
                                                                                                                          
                                                                                                                          


void SwitchMEObjectAttribute(
    std::string patternname, bool on_off,
    std::function<void(namemap_t*)> switchAction,
    std::vector<reference_t>& switchOnList, const char* what_attribute)
{
    auto matched = 0;

    for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto tname = global_name_map.get(i);
        if (wildcardMatch(global_name_map.getName(i), patternname)) {
            // Apply the switch action
			switchAction(tname);

			bool onList = false;
			int idx = -1;
			if (switchOnList.size()>0)
	            for (auto ref : tname->obj->references)
					if (ref.accessor!=nullptr && ref.accessor() == &switchOnList)
	                {
						onList = true;
						idx = ref.offset;
						break;
					}

			if (!onList)
				for (reference_t sw : switchOnList)
					assert(sw.obj != tname->obj);

			if (!on_off){
				// if off, Check if object is in switchOnList
				
                if (onList){
                    // Fast removal: Move last element to current position and pop back
					// ptrRef will be removed.
					assert(tname->obj->references[switchOnList[idx].obj_reference_idx].offset == idx);

					// remove from object.
					switchOnList[idx].remove_from_obj();

					while (switchOnList.back().obj == nullptr)
						switchOnList.pop_back();
					// remove reference from switchonlist.
                    if (idx < switchOnList.size() - 1) {
						switchOnList[idx] = switchOnList.back();
						//assert(switchOnList[idx].obj->references[switchOnList[idx].obj_reference_idx].accessor() == &switchOnList);
						switchOnList[idx].obj->references[switchOnList[idx].obj_reference_idx].offset = idx;
						// printf("obj `%s` reference to attr_%s updated to offset %d.\n", switchOnList[idx].obj->name.c_str(), what_attribute, idx);
					}

                    switchOnList.pop_back();
					// printf("attr `%s` reference on obj `%s` removed.\n", what_attribute, tname->obj->name.c_str());
				}
			}
			else{
				// if on, check if 
	            if (!onList) {
	                // Add to list if not already there
					auto ptr = &switchOnList;
					auto idx=tname->obj->push_reference([ptr] { return ptr; }, switchOnList.size());
					switchOnList.push_back(reference_t(*tname, idx));
					// printf("attr `%s` reference on obj `%s`, ptr %x added.\n", what_attribute, tname->obj->name.c_str(), &switchOnList.back());
	            }
			}

            matched++;
        }
    }

    printf("switch attr %s for `%s` : %s %d objects for ws_%d\n", what_attribute, patternname.c_str(), on_off?"ON":"OFF", matched, working_viewport-ui.viewports);
}

void SetShowHide(std::string name, bool show)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        name, !show,
        [show](namemap_t* nt) { nt->obj->show[working_viewport_id] = show; },
        wstate.hidden_objects,
		"hidden"
    );
}

void SetApplyCrossSection(std::string name, bool apply)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        name, !apply,
        [apply](namemap_t* nt)
        {
			RouteTypes(nt, 
				[&]	{
					// point cloud
				}, [&](int class_id)
				{
					// gltf/mesh
						if (apply)
							((gltf_object*)nt->obj)->flags[working_viewport_id] &= ~(1 << 7);
						else 
							((gltf_object*)nt->obj)->flags[working_viewport_id] |= (1 << 7);
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
        },
        wstate.no_cross_section,
		"ignore cross section"
    );
}

void SetObjectSelectable(std::string name, bool selectable)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        name, selectable,
        [selectable](namemap_t* nt)
        {
			RouteTypes(nt, 
				[&]	{
					// point cloud
					// todo: change to per viewport flag.
					auto testpc = (me_pcRecord*)nt->obj;
					if (selectable)
						testpc->flag |= (1 << 7);
					else
						testpc->flag &= ~(1 << 7);
				}, [&](int class_id)
				{
					// gltf/mesh
					auto testgltf = (gltf_object*)nt->obj;
					if (selectable)
						testgltf->flags[working_viewport_id] |= (1 << 4);
					else
						testgltf->flags[working_viewport_id] &= ~(1 << 4);
				}, [&]
				{
					// line piece/line bunch no work.
					auto piece = (me_line_piece*)nt->obj;
					if (selectable)
						piece->attrs.flags |= (1 << 5);
					else
						piece->attrs.flags &= ~(1 << 5);
				}, [&]
				{
					auto testsprite = (me_sprite*)nt->obj;
					if (selectable)
						testsprite->per_vp_stat[working_viewport_id] |= (1 << 0);
					else
						testsprite->per_vp_stat[working_viewport_id] &= ~(1 << 0);
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
        },
        wstate.selectables,
		"selectable"
    );
}

// todo: ad
void SetObjectSubSelectable(std::string name, bool subselectable)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        name, subselectable,
        [subselectable](namemap_t* nt)
        {
			RouteTypes(nt, 
				[&]	{
					// point cloud
					auto testpc = (me_pcRecord*)nt->obj;
					if (subselectable)
						testpc->flag |= (1 << 8);
					else
						testpc->flag &= ~(1 << 8);
				}, [&](int class_id)
				{
					// gltf/mesh
					auto testgltf = (gltf_object*)nt->obj;
					if (subselectable)
						testgltf->flags[working_viewport_id] |= (1 << 5);
					else
						testgltf->flags[working_viewport_id] &= ~(1 << 5);
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
        },
        wstate.sub_selectables,
		"sub_selectable"
    );
}

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
	gbuf->previous_position=gbuf->target_position = what.position;
	gbuf->previous_rotation = gbuf->target_rotation= what.quaternion;
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
		ss.header = *(unsigned char*)ptr; ptr += 1;
		if (ss.header & (1<<0)){
			ss.position = *(glm::vec3*)ptr; ptr += sizeof(glm::vec3);
		}
		if (ss.header & (1<<1)){
			ss.ndc_offset =  *(glm::vec2*)ptr; ptr += sizeof(glm::vec2);
		}
		if (ss.header & (1<<2))
		{
			ss.pixel_offset = *(glm::vec2*)ptr; ptr += sizeof(glm::vec2);
		}
		if (ss.header & (1<<3))
		{
			ss.pivot = *(glm::vec2*)ptr; ptr += sizeof(glm::vec2);
		}
		if (ss.header & (1<<4)){
			auto rstr=std::string(ptr + 4, ptr + 4 + *((int*)ptr)); ptr += *((int*)ptr) + 4;
			if (rstr.length() == 0)
				ss.relative = nullptr;
			auto rptr = global_name_map.get(rstr);
			if (rptr != nullptr)
				ss.relative = global_name_map.get(rstr)->obj;
		}

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
		line_pieces.add(name, lp);
	}
}

// ██ ███    ███  █████   ██████  ███████
// ██ ████  ████ ██   ██ ██       ██
// ██ ██ ████ ██ ███████ ██   ███ █████
// ██ ██  ██  ██ ██   ██ ██    ██ ██
// ██ ██      ██ ██   ██  ██████  ███████


void me_update_rgba_atlas(sg_image simg, int an, int sx, int sy, int h, int w, const void* data, sg_pixel_format format)
{
	_sg_image_t* img = _sg_lookup_image(&_sg.pools, simg.id);

	_sg_gl_cache_store_texture_binding(0);
	_sg_gl_cache_bind_texture(0, img->gl.target, img->gl.tex[img->cmn.active_slot]);
	GLenum gl_img_target = img->gl.target;
	glTexSubImage3D(gl_img_target, 0,
		sx, sy, an,
		w, h, 1,
		GL_RGBA, GL_UNSIGNED_BYTE,
		// _sg_gl_teximage_format(format), _sg_gl_teximage_type(format),
		data);
	_sg_gl_cache_restore_texture_binding(0);
	_SG_GL_CHECK_ERROR();
}

void AddImage(std::string name, int flag, glm::vec2 disp, glm::vec3 pos, glm::quat quat, std::string rgbaName)
{
	auto im = sprites.get(name);
	if (im == nullptr)
	{
		im = new me_sprite();
		sprites.add(name, im);
	}
	im->display_flags = flag; // billboard ? (1 << 5) : 0; 
	im->dispWH = disp;
	im->previous_position = im->target_position = pos;
	im->previous_rotation = im->target_rotation = quat;
	im->rgbaName = rgbaName;
	auto rgba_ptr = argb_store.rgbas.get(rgbaName);
	if (rgba_ptr == nullptr) //create if none.
	{
		rgba_ptr = new me_rgba();
		rgba_ptr->width = -1; //dummy;
		rgba_ptr->loaded = false;
		argb_store.rgbas.add(rgbaName, rgba_ptr);
	}
	im->rgba = rgba_ptr;
}

void PutRGBA(std::string name, int width, int height)
{
	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) {
		rgba_ptr = new me_rgba();
		argb_store.rgbas.add(name, rgba_ptr);
	}

	rgba_ptr->width = width;
	rgba_ptr->height = height;
}

void UpdateRGBA(std::string name, int len, char* rgba)
{
	//printf("update rgba:%s, len=%d\n", name.c_str(), len);
	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) return; // no such rgba...
	if (rgba_ptr->width == -1) return; // dummy rgba, not available.
	if (rgba_ptr->atlasId < 0) return; // not yet allocated.
	if (len != rgba_ptr->width * rgba_ptr->height * 4) 
		throw "size not match";
	rgba_ptr->loaded = true;
	rgba_ptr->invalidate = false;
	me_update_rgba_atlas(argb_store.atlas, rgba_ptr->atlasId, (int)(rgba_ptr->uvStart.x), (int)(rgba_ptr->uvEnd.y), rgba_ptr->height, rgba_ptr->width, rgba, SG_PIXELFORMAT_RGBA8);
}

void SetRGBAStreaming(std::string name)
{
	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) return; // no such rgba...
	rgba_ptr->streaming = true;
}

void InvalidateRGBA(std::string name)
{
	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) return; // no such rgba...
	rgba_ptr->invalidate = true;
}

rgba_ref UIUseRGBA(std::string name){

	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) return { .layerid = -1 };

	rgba_ptr->occurrence = 999999;
	if (rgba_ptr->streaming && rgba_ptr->atlasId!=-1 && rgba_ptr->loadLoopCnt<ui.loopCnt)
	{
		auto ptr = GetStreamingBuffer(name, rgba_ptr->width * rgba_ptr->height * 4);
		me_update_rgba_atlas(argb_store.atlas, rgba_ptr->atlasId,
			(int)(rgba_ptr->uvStart.x), (int)(rgba_ptr->uvEnd.y), rgba_ptr->height, rgba_ptr->width, ptr
			, SG_PIXELFORMAT_RGBA8);
		rgba_ptr->loaded = true;
	}
	
    // return { .layerid = -1 };
	if (rgba_ptr->loaded)
	{
		return { rgba_ptr->width,rgba_ptr->height, rgba_ptr->atlasId, rgba_ptr->uvStart / (float)atlas_sz, rgba_ptr->uvEnd / (float)atlas_sz };
	}
    return { .layerid = -1 };
}

//  ██████  ██      ████████ ███████     ███    ███  ██████  ██████  ███████ ██      
// ██       ██         ██    ██          ████  ████ ██    ██ ██   ██ ██      ██      
// ██   ███ ██         ██    █████       ██ ████ ██ ██    ██ ██   ██ █████   ██      
// ██    ██ ██         ██    ██          ██  ██  ██ ██    ██ ██   ██ ██      ██      
//  ██████  ███████    ██    ██          ██      ██  ██████  ██████  ███████ ███████ 

void LoadModel(std::string cls_name, unsigned char* bytes, int length, ModelDetail detail)
{
	// if (gltf_classes.get(cls_name) != nullptr) return; // already registered.
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
	
	auto cls = gltf_classes.get(cls_name);
	if (cls == nullptr) {
		cls = new gltf_class();
		gltf_classes.add(cls_name, cls);
	} else {
		cls->clear_me_buffers();
	}
	cls->dbl_face = false;
	cls->apply_gltf(model, cls_name, detail.center, detail.scale, detail.rotate);

	// if any gltf_objects, reset status.
	auto instances = cls->objects.ls.size();
	for (int i=0; i<instances; ++i)
	{
		auto ptr = cls->objects.get(i);
		ptr->nodeattrs.clear();
		ptr->nodeattrs.resize(cls->model.nodes.size());
	}
}

void DefineMesh(std::string cls_name, custom_mesh_data& mesh_data)
{
	// Create a minimal GLTF model
	tinygltf::Model model;
	
	// Add scene
	tinygltf::Scene scene;
	scene.nodes.push_back(0);
	model.scenes.push_back(scene);
	model.defaultScene = 0;

	// Add node
	tinygltf::Node node;
	node.mesh = 0;
	model.nodes.push_back(node);

	// Add mesh with one primitive
	tinygltf::Mesh mesh;
	tinygltf::Primitive primitive;
	primitive.mode = TINYGLTF_MODE_TRIANGLES;

	// Calculate normals if needed
	std::vector<glm::vec3> normals;
	 
	normals.resize(mesh_data.nvtx);
	if (mesh_data.smooth) {
		// Initialize normals to zero
		std::fill(normals.begin(), normals.end(), glm::vec3(0));
		
		// Accumulate face normals for each vertex
		for (size_t i = 0; i < mesh_data.nvtx; i += 3) {
			glm::vec3 v1 = mesh_data.positions[i + 1] - mesh_data.positions[i];
			glm::vec3 v2 = mesh_data.positions[i + 2] - mesh_data.positions[i];
			glm::vec3 normal = glm::normalize(glm::cross(v1, v2));
			
			// Add face normal to all vertices of this triangle
			normals[i] += normal;
			normals[i + 1] += normal;
			normals[i + 2] += normal;
		}
		
		// Normalize accumulated normals
		for (auto& normal : normals) {
			if (glm::length(normal) > 0.0001f) { // Avoid normalizing zero vectors
				normal = glm::normalize(normal);
			}
		}
	} else {
		// Flat shading - each triangle gets its own face normal
		for (size_t i = 0; i < mesh_data.nvtx; i += 3) {
			glm::vec3 v1 = mesh_data.positions[i + 1] - mesh_data.positions[i];
			glm::vec3 v2 = mesh_data.positions[i + 2] - mesh_data.positions[i];
			glm::vec3 normal = glm::normalize(glm::cross(v1, v2));
			
			// Assign the same face normal to all vertices of this triangle
			normals[i] = normal;
			normals[i + 1] = normal;
			normals[i + 2] = normal;
		}
	}

	// Add buffer for vertex data (positions, normals, and colors)
	tinygltf::Buffer buffer;
	size_t pos_size = mesh_data.nvtx * sizeof(glm::vec3);
	size_t normal_size = normals.size() * sizeof(glm::vec3);
	size_t color_size = mesh_data.nvtx * sizeof(glm::vec4); // One color per vertex
	buffer.data.resize(pos_size + normal_size + color_size);

	// Copy positions
	memcpy(buffer.data.data(), mesh_data.positions, pos_size);
	
	// Copy normals
	memcpy(buffer.data.data() + pos_size, normals.data(), normal_size);

	// Generate and copy colors
	std::vector<glm::vec4> colors(mesh_data.nvtx);
	auto c = ImGui::ColorConvertU32ToFloat4(mesh_data.color);
	std::fill(colors.begin(), colors.end(), glm::vec4(c.x, c.y, c.z, c.w));
	memcpy(buffer.data.data() + pos_size + normal_size, colors.data(), color_size);

	model.buffers.push_back(buffer);

	// Add buffer views
	// Positions
	tinygltf::BufferView posView;
	posView.buffer = 0;
	posView.byteOffset = 0;
	posView.byteLength = pos_size;
	posView.target = TINYGLTF_TARGET_ARRAY_BUFFER;
	model.bufferViews.push_back(posView);

	// Normals
	tinygltf::BufferView normalView;
	normalView.buffer = 0;
	normalView.byteOffset = pos_size;
	normalView.byteLength = normal_size;
	normalView.target = TINYGLTF_TARGET_ARRAY_BUFFER;
	model.bufferViews.push_back(normalView);

	// Colors
	tinygltf::BufferView colorView;
	colorView.buffer = 0;
	colorView.byteOffset = pos_size + normal_size;
	colorView.byteLength = color_size;
	colorView.target = TINYGLTF_TARGET_ARRAY_BUFFER;
	model.bufferViews.push_back(colorView);

	// Add accessors
	// Positions
	tinygltf::Accessor posAcc;
	posAcc.bufferView = 0;
	posAcc.byteOffset = 0;
	posAcc.componentType = TINYGLTF_COMPONENT_TYPE_FLOAT;
	posAcc.count = mesh_data.nvtx;
	posAcc.type = TINYGLTF_TYPE_VEC3;
	model.accessors.push_back(posAcc);
	primitive.attributes["POSITION"] = 0;

	// Normals
	tinygltf::Accessor normalAcc;
	normalAcc.bufferView = 1;
	normalAcc.byteOffset = 0;
	normalAcc.componentType = TINYGLTF_COMPONENT_TYPE_FLOAT;
	normalAcc.count = normals.size();
	normalAcc.type = TINYGLTF_TYPE_VEC3;
	model.accessors.push_back(normalAcc);
	primitive.attributes["NORMAL"] = 1;

	// Colors
	tinygltf::Accessor colorAcc;
	colorAcc.bufferView = 2;
	colorAcc.byteOffset = 0;
	colorAcc.componentType = TINYGLTF_COMPONENT_TYPE_FLOAT;
	colorAcc.count = colors.size();
	colorAcc.type = TINYGLTF_TYPE_VEC4;
	model.accessors.push_back(colorAcc);
	primitive.attributes["COLOR_0"] = 2;

	// Add primitive to mesh
	mesh.primitives.push_back(primitive);
	model.meshes.push_back(mesh);

	// Find existing or create new gltf_class
	auto mesh_cls = gltf_classes.get(cls_name);
	if(!mesh_cls) {
		mesh_cls = new gltf_class();
		gltf_classes.add(cls_name, mesh_cls);
	} else {
		mesh_cls->clear_me_buffers();
	}
	mesh_cls->apply_gltf(model, cls_name, glm::vec3(0), 1.0f, glm::identity<glm::quat>());
	mesh_cls->dbl_face = true;

	printf("define mesh cls:%s\n", cls_name.c_str());
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
		gltf_ptr->previous_position = gltf_ptr->target_position = new_position;
		gltf_ptr->previous_rotation = gltf_ptr->target_rotation = new_quaternion;
		if (t->animations.size() > 0) {
			gltf_ptr->baseAnimId = gltf_ptr->playingAnimId = gltf_ptr->nextAnimId = 0;
			gltf_ptr->animationStartMs = ui.getMsFromStart();
		}else
		{
			gltf_ptr->baseAnimId = gltf_ptr->playingAnimId = gltf_ptr->nextAnimId = -1;
		}
		t->objects.add(name, gltf_ptr);
	}else
	{
		oldobj->previous_position = oldobj->target_position = new_position;
		oldobj->previous_rotation = oldobj->target_rotation = new_quaternion;
	}

	printf("put %s of mesh %s\n", name.c_str(), cls_name.c_str());
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



// movable(xyz), rotatable(xyz), selectable/sub_selectable, snapping, have_action(right click mouse),
// this also triggers various events for cycle ui.

// Display a billboard form following the object, if object visible, also, only show 10 billboard top most.
void SetObjectBillboard(std::string name, std::string billboardFormName, std::string behaviour){}; //

void DeapplyWorkspaceState()
{
	auto& wstate = working_viewport->workspace_state.back();
	// Hidden object
	for (auto tn : wstate.hidden_objects)
		if (tn.obj != nullptr)
			tn.obj->show[working_viewport_id] = true;
	
	// Selectables:	
	for (auto tn : wstate.selectables)
		if (tn.obj != nullptr)
			RouteTypes(&tn, 
				[&]	{ // point cloud.
					((me_pcRecord*)tn.obj)->flag &= ~(1 << 7);
				}, [&](int class_id) { // gltf
					((gltf_object*)tn.obj)->flags[working_viewport_id] &= ~(1 << 4);
				}, [&] {// line bunch.
					((me_line_piece*)tn.obj)->attrs.flags &= ~(1 << 5);
				}, [&] { //sprites
					((me_sprite*)tn.obj)->per_vp_stat[working_viewport_id] &= ~(1 << 0);
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});

	// Sub-Selectables:
	for (auto tn : wstate.sub_selectables)
		if (tn.obj != nullptr)
			RouteTypes(&tn, 
				[&]	{ // point cloud.
					((me_pcRecord*)tn.obj)->flag &= ~(1 << 8);
				}, [&](int class_id) { // gltf
					((gltf_object*)tn.obj)->flags[working_viewport_id] &= ~(1 << 5);
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
	// use cross section?:
	for (auto tn : wstate.no_cross_section)
		if (tn.obj != nullptr)
			RouteTypes(&tn, 
				[&]	{
					// point cloud.
				}, [&](int class_id) { // gltf
					((gltf_object*)tn.obj)->flags[working_viewport_id] &= ~(1 << 7);
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
	
}

void ReapplyWorkspaceState()
{
	auto& w2state = working_viewport->workspace_state.back();

	// Remove null objects from selectables
	auto removeNullRefs = [](std::vector<reference_t>* purging_container) {
		purging_container->erase(
			std::remove_if(purging_container->begin(), purging_container->end(),
				[](const reference_t& ref) { return ref.obj == nullptr; }
			),
			purging_container->end()
		);
		for (size_t i=0; i<purging_container->size(); ++i){
			auto& reference = (*purging_container)[i];
			reference.obj->references[reference.obj_reference_idx] = { .accessor = [purging_container]() { return purging_container; }, .offset = i };
		}//.offset = &reference-&container[0]; // reapply after container is modified.
	};

	removeNullRefs(&w2state.selectables);
	removeNullRefs(&w2state.hidden_objects);
	removeNullRefs(&w2state.sub_selectables); 
	removeNullRefs(&w2state.no_cross_section);

	// Hidden object
	for (auto tn : w2state.hidden_objects)
		if (tn.obj != nullptr)
			tn.obj->show[working_viewport_id] = false;

	// Selectables:		
	for (auto tn : w2state.selectables)
		if (tn.obj != nullptr)
			RouteTypes(&tn, 
				[&]	{ // point cloud.
					((me_pcRecord*)tn.obj)->flag |= (1 << 7);
				}, [&](int class_id) { // gltf
					((gltf_object*)tn.obj)->flags[working_viewport_id] |= (1 << 4);
				}, [&] {// line bunch.
					((me_line_piece*)tn.obj)->attrs.flags |= (1 << 5);
				}, [&] { // sprites;
					((me_sprite*)tn.obj)->per_vp_stat[working_viewport_id] |= (1 << 0);
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
	
	// Sub-Selectables:
	for (auto tn : w2state.sub_selectables)
		if (tn.obj != nullptr)
			RouteTypes(&tn, 
				[&]	{ // point cloud.
					((me_pcRecord*)tn.obj)->flag |= (1 << 8);
				}, [&](int class_id) { // gltf
					((gltf_object*)tn.obj)->flags[working_viewport_id] |= (1 << 5);
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});
	
	// use cross section?:	
	for (auto tn : w2state.no_cross_section)
		if (tn.obj != nullptr)
			RouteTypes(&tn, 
				[&]	{ // point cloud.
				}, [&](int class_id) { // gltf
					((gltf_object*)tn.obj)->flags[working_viewport_id] |= (1 << 7);
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				},[&]
				{
					// spot texts.
				},[&]
				{
					// widgets.remove(name);
				});

}
void SetWorkspaceSelectMode(selecting_modes mode, float painter_radius)
{
	auto sel_op = (select_operation*)working_viewport->workspace_state.back().operation;
	sel_op->selecting_mode = mode;
	sel_op->paint_selecting_radius = painter_radius;
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
