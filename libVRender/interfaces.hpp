#pragma once

#include "me_impl.h"
#include "platform.hpp"
#include "utilities.h"
#include <cctype>


// /// <summary>
// /// 
//  ██████  ██████  ███    ███ ███    ███  ██████  ███    ██ 
// ██      ██    ██ ████  ████ ████  ████ ██    ██ ████   ██ 
// ██      ██    ██ ██ ████ ██ ██ ████ ██ ██    ██ ██ ██  ██ 
// ██      ██    ██ ██  ██  ██ ██  ██  ██ ██    ██ ██  ██ ██ 
//  ██████  ██████  ██      ██ ██      ██  ██████  ██   ████ 
// /// </summary>
// ///

// void NotifyWorkspaceUpdated()
// {
// 	shared_graphics.allowData = true;
// }

void freePointCloud(me_pcRecord* t)
{
	if (t->pc_type == 1)
	{
		local_maps.remove(t->localmap->idx);
	}
	sg_destroy_image(t->pcSelection);
	sg_destroy_buffer(t->pcBuf);
	sg_destroy_buffer(t->colorBuf);
	delete[] t->cpuSelection;
}
void actualRemove(namemap_t* nt)
{
	RouteTypes(nt, 
		[&]	{
			// point cloud.
			auto t = (me_pcRecord*)nt->obj;
			freePointCloud(t);
			pointclouds.remove(nt->obj->name);
			
		}, [&](int class_id)
		{
			// gltf
			auto t = gltf_classes.get(class_id);
			t->objects.remove(nt->obj->name);
		}, [&]
		{
			// line piece.
			auto lp = (me_line_piece*)nt->obj;
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
			auto ui = (me_world_ui*)nt->obj;
			ui->remove();
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
	for (int i = 0; i < global_name_map.ls.size();) {
		if (wildcardMatch(global_name_map.getName(i), name))
			actualRemove(global_name_map.get(i));
		else i++;
	}
}

// do auto unbind.
void set_reference(reference_t& p, me_obj* t)
{
	if (p.obj != nullptr) {
		p.remove_from_obj();
		p.obj = nullptr;
	}
	auto oidx = t->references.size();
	t->references.push_back({ .accessor = nullptr, .offset = (size_t)-2, .ref = &p });
	p.obj_reference_idx = oidx;
	p.obj = t;
}

void AnchorObject(std::string earth, std::string moon, glm::vec3 rel_position, glm::quat rel_quaternion)
{
	// add anchoring.
	auto p = global_name_map.get(moon);
	if (p == nullptr) return;

	// 1: test if earth is empty, if empty reset anchor.
	namemap_t* q = nullptr;
	int sub_id = -1;
	if (earth.size() != 0) {
		// split earth by [earth_major]::[sub].
		auto pos = earth.find("::");
		auto earth_major = (pos != std::string::npos) ? earth.substr(0, pos) : earth;
		q = global_name_map.get(earth_major);
		if (q == nullptr)
			q = global_name_map.get(earth); // try full name... (fuck, wrong naming for me::camera...)
		else if (pos != std::string::npos && q != nullptr) {
			// get sub:
			auto earth_sub = earth.substr(pos + 2);
			if (earth_sub.size() != 0) {
				p->obj->anchor.type = q->type; // use type to indicate me_obj type, this is not auto set.

				// if earth_sub is like "#number", "#123", get the sub object, else get the object.
				// Check if earth_sub starts with '#' followed by a number
				if (earth_sub[0] == '#' && earth_sub.size() > 1) {
					std::string number_part = earth_sub.substr(1);
					bool is_number = true;
					for (char c : number_part) {
						if (!std::isdigit(c)) {
							is_number = false;
							break;
						}
					}
					
					if (is_number)
						sub_id = std::stoi(number_part);
				} else {
					// Find node by name in the GLTF model
					if (q->type>=1000) //is gltf_object.
					{
						auto gltf_cls = gltf_classes.get(((gltf_object*)q->obj)->gltf_class_id);
						for (size_t idx = 0; idx < gltf_cls->model.nodes.size(); idx++) {
							if (gltf_cls->model.nodes[idx].name == earth_sub) {
								sub_id = idx;
								break;
							}
						}
					}
				}
			}
		}
	}
	if (q == nullptr)
	{
		p->obj->remove_anchor();
		return;
	};

	p->obj->anchor_subid = sub_id;
	set_reference(p->obj->anchor, q->obj);

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

inline void me_obj::remove_anchor()
{
	if (anchor.obj != nullptr) {
		anchor.remove_from_obj();
		anchor.obj = nullptr;
	}
}

// move object use namepattern.

void actualMove(namemap_t* slot, glm::vec3 new_position, glm::quat new_quaternion, float time, uint8_t type, uint8_t coord)
{
	// remove anchoring.
	slot->obj->remove_anchor();

	slot->obj->previous_position = slot->obj->target_position;
	slot->obj->previous_rotation = slot->obj->target_rotation;

	if (type == 0 || type == 1) //pos enabled.
	{
		if (coord == 0)
		{
			slot->obj->target_position = new_position;
		}
		else
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
		DBG("move object %s time exceeds max allowed animation time=5s.\n");
		time = 5000;
	}
	slot->obj->target_require_completion_time = slot->obj->target_start_time + time;

}

void MoveObject(std::string name, glm::vec3 new_position, glm::quat new_quaternion, float time, uint8_t type, uint8_t coord)
{
	auto slot = global_name_map.get(name);
	if (slot == nullptr) return;
	actualMove(slot, new_position, new_quaternion, time, type, coord);
}

void MoveObjectByPattern(std::string name_pattern, glm::vec3 new_position, glm::quat new_quaternion, float time, uint8_t type, uint8_t coord)
{
	// Loop through all objects in the name map
	for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto slot = global_name_map.get(i);
		std::string objName = global_name_map.getName(i);

		// Check if this object matches the pattern
		if (wildcardMatch(objName, name_pattern)) 
			actualMove(slot, new_position, new_quaternion, time, type, coord);
	}
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
					auto t = (me_line_piece*)mapping->obj;
					t->flags[working_viewport_id] |= (1 << 3);
				}, [&]
				{
					// sprites;
					auto t = (me_sprite*)mapping->obj;
					t->per_vp_stat[working_viewport_id] |= 1 << 1;
				},[&]
				{
					// world ui
					auto t = (me_world_ui*)mapping->obj;
					t->selected[working_viewport_id] = true;
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

void SetObjectTransparency(std::string patternname, float transparency)
{
    for (int i = 0; i < global_name_map.ls.size(); ++i) {
        auto tname = global_name_map.get(i);
        if (wildcardMatch(global_name_map.getName(i), patternname)) {	
			RouteTypes(tname, 
				[&]	{
					// point cloud.
				}, [&](int class_id)
				{
					// gltf
					auto t = (gltf_object*)tname->obj;
					unsigned char val = transparency * 255;
					t->flags[working_viewport_id] = ((val << 8) | (t->flags[working_viewport_id] & 0xffff00ff));
				}, [&]
				{
					// line bunch.
				}, [&]
				{
					// sprites;
				}, [&]
				{
					// spot texts.
				}, [&]
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
    std::string regex_str, bool on_off,
    std::function<void(namemap_t*)> switchAction,
    std::vector<reference_t>& switchOnList, const char* what_attribute)
{
    auto matched = 0;

    for (int i = 0; i < global_name_map.ls.size(); ++i) {
		auto tname = global_name_map.get(i);
		if (RegexMatcher::match(global_name_map.getName(i), regex_str)) {
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
				for (reference_t& sw : switchOnList)
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

    DBG("switch attr %s for `%s` : %s %d objects for ws_%d\n", what_attribute, regex_str.c_str(), on_off?"ON":"OFF", matched, working_viewport-ui.viewports);
}

void SetShowHide(std::string namePattern, bool show)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        namePattern, !show,
        [show](namemap_t* nt) { nt->obj->show[working_viewport_id] = show; },
        wstate.hidden_objects,
		"hidden"
    );
}

void SetApplyCrossSection(std::string namePattern, bool apply)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        namePattern, !apply,
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

void SetObjectSelectable(std::string namePattern, bool selectable)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        namePattern, selectable,
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
						piece->flags[working_viewport_id] |= (1 << 5);
					else
						piece->flags[working_viewport_id] &= ~(1 << 5);
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
					// world ui
					auto ui = (me_world_ui*)nt->obj;
					ui->selectable[working_viewport_id] = true;
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
void SetObjectSubSelectable(std::string namePattern, bool subselectable)
{
    auto& wstate = working_viewport->workspace_state.back();
    SwitchMEObjectAttribute(
        namePattern, subselectable,
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
	auto capacity = what.isVolatile ? what.capacity : what.initN;
	if (capacity <= 0) capacity = 1;
	bool isVolatile = what.isVolatile || (what.initN <= 0);
	// if (capacity < 65536) capacity = 65536;

	auto pcbuf = sg_make_buffer(isVolatile ?
		sg_buffer_desc{ .size = capacity * sizeof(glm::vec4), .usage = SG_USAGE_STREAM, } :
		sg_buffer_desc{ .data = { what.x_y_z_Sz, capacity * sizeof(glm::vec4) }, });
	
	auto cbuf = sg_make_buffer(isVolatile ?
		sg_buffer_desc{ .size = capacity * sizeof(uint32_t), .usage = SG_USAGE_STREAM, } :
		sg_buffer_desc{ .data = { what.color, capacity * sizeof(uint32_t) }, });

	int sz = (int)ceilf(sqrtf(ceilf(capacity / 8.0f)));
	if (sz < 1) sz = 1;

	me_pcRecord* gbuf = nullptr;
	auto t = global_name_map.get(name);
	if (t != nullptr) {
		gbuf = (me_pcRecord*)t->obj;
		freePointCloud(gbuf);
	}
	else {
		gbuf = new me_pcRecord();
		pointclouds.add(name, gbuf);
	}
	gbuf->isVolatile = isVolatile;
	gbuf->capacity = (int)capacity;
	gbuf->n = (int)what.initN;
	gbuf->pcBuf = pcbuf;
	gbuf->colorBuf = cbuf;
	gbuf->pcSelection = sg_make_image(sg_image_desc{
		.width = sz, .height = sz,
		.usage = SG_USAGE_STREAM,
		.pixel_format = SG_PIXELFORMAT_R8UI,
	});
	gbuf->cpuSelection = new unsigned char[sz*sz];
	memset(gbuf->cpuSelection, 0, sz* sz);

	gbuf->name = name;
	gbuf->previous_position=gbuf->target_position = what.position;
	gbuf->previous_rotation = gbuf->target_rotation= what.quaternion;
	gbuf->flag = (1 << 4); // default: can select by point.

	// set type/flags and precompute angular distances for local map
	gbuf->pc_type = (int)what.type;
	if (gbuf->pc_type == 1) {
		gbuf->localmap = new me_localmap();
		gbuf->localmap->angular256.assign(128, 65535);
		for (int i = 0; i < what.initN; ++i) {
			const glm::vec4& p = what.x_y_z_Sz[i];
			float angle = atan2f(p.y, p.x);
			if (angle < 0) angle += 6.28318530718f;
			int idx = (int)floorf(angle * (128.0f / 6.28318530718f));
			if (idx < 0) idx = 0; else if (idx > 127) idx = 127;
			float dist = sqrtf(p.x * p.x + p.y * p.y);
			float mm = floorf(dist * 10.0f + 0.5f); // 0.1m units
			if (mm > 65534) mm = 65534;
			float prev = gbuf->localmap->angular256[idx];
			if (prev == 65535 || mm < prev) gbuf->localmap->angular256[idx] = mm;
		}
		int id = local_maps.insert(gbuf->localmap);
		gbuf->localmap->idx = id;

		// todo: partial update cache.
		{
			int gx = id % 64;
			int gy = id / 64;
			float row32x4[32 * 4];
			for (int k = 0; k < 32; ++k) {
				for (int c = 0; c < 4; ++c) {
					int bin = k * 4 + c;
					uint16_t mm = (bin < (int)gbuf->localmap->angular256.size()) ? (uint16_t)gbuf->localmap->angular256[bin] : (uint16_t)65535;
					row32x4[k * 4 + c] = (mm == 65535u) ? 1e9f : (float)mm * 0.1f;
				}
			}
			texUpdatePartial(shared_graphics.walkable_cache, gx * 32, 12 + gy, 32, 1, row32x4, SG_PIXELFORMAT_RGBA32F);
		}
	}

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
			sg_buffer_desc{ .size = capacity * sizeof(uint32_t), .usage = SG_USAGE_STREAM, });
		copyPartial(t->colorBuf, cbuf, t->n * sizeof(uint32_t));

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
		DBG("refresh volatile pc %s from %d to %d(len=%d)\n", name.c_str(), t->capacity, capacity, newSz);
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
			auto rptr = global_name_map.get(rstr);
			if (rptr != nullptr)
				set_reference(ss.relative, global_name_map.get(rstr)->obj);
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

	const int ilines = 4096;
	//32bytes: start end meta color.
	auto t = line_bunches.get(name);
	if (t == nullptr) {
		t = new me_linebunch();
		t->line_buf = sg_make_buffer(
			sg_buffer_desc{ .size = ilines * unitSz, .usage = SG_USAGE_STREAM, });
		t->capacity = ilines;
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
		
		DBG("refresh line bunch %s from %d to %d\n", name.c_str(), t->capacity, capacity);
		t->capacity = capacity;
	}

	// assert(t->n + length <= t->capacity);
	int sz = length * unitSz; //start end arrow color
	updatePartial(t->line_buf, t->n * unitSz, { pointer, (size_t)sz });
	t->n += length;
	
	return ((unsigned char*)pointer) + sz;
}

void ClearLineBunch(std::string name)
{
	auto t = line_bunches.get(name);
	if (t == nullptr) return;
	t->n = 0;
}

me_line_piece* add_line_piece(std::string name, const line_info& what)
{
	auto t = line_pieces.get(name);
	auto lp = t == nullptr ? new me_line_piece : t;
	if (what.propStart.size() > 0) {
		auto ns = global_name_map.get(what.propStart);
		if (ns != nullptr)
			set_reference(lp->propSt, ns->obj);
	}

	if (what.propEnd.size() > 0) {
		auto ns = global_name_map.get(what.propEnd);
		if (ns != nullptr)
			set_reference(lp->propEnd, ns->obj);
	}
	lp->attrs.st = what.start;
	lp->attrs.end = what.end;
	lp->attrs.arrowType = what.arrowType;
	lp->attrs.width = what.width;
	lp->attrs.dash = what.dash;
	lp->attrs.color = what.color;

	if (t == nullptr)
	{
		line_pieces.add(name, lp);
	}
	return lp;
}

void AddStraightLine(std::string name, const line_info& what)
{
	add_line_piece(name, what);
}

void AddBezierCurve(std::string name, const line_info& what, const std::vector<glm::vec3>& controlPoints)
{
	auto lp = add_line_piece(name, what);
	// Store control points
	lp->type = me_line_piece::bezier;
	lp->ctl_pnt = controlPoints;
}

// ██ ███    ███  █████   ██████  ███████
// ██ ████  ████ ██   ██ ██       ██
// ██ ██ ████ ██ ███████ ██   ███ █████
// ██ ██  ██  ██ ██   ██ ██    ██ ██
// ██ ██      ██ ██   ██  ██████  ███████


void AddImage(std::string name, int flag, glm::vec2 disp, glm::vec3 pos, glm::quat quat, std::string rgbaName)
{
	auto im = sprites.get(name);
	if (im == nullptr)
	{
		im = new me_sprite();
		sprites.add(name, im);
	}
	im->display_flags = flag; 
	im->dispWH = disp;
	im->previous_position = im->target_position = pos;
	im->previous_rotation = im->target_rotation = quat;
	im->resName = rgbaName;

	// Check if this is an SVG image (name starts with "svg:")
	if (rgbaName.substr(0, 4) == "svg:") {
		// Handle SVG image
		im->type = me_sprite::sprite_type::svg_t;
		std::string svgName = rgbaName.substr(4); // Remove "svg:" prefix
		auto svg_ptr = svg_store.get(svgName);
		if (svg_ptr == nullptr) {
			// Create new SVG entry if it doesn't exist
			svg_ptr = new me_svg();
			svg_store.add(svgName, svg_ptr);
		}
		im->svg = svg_ptr;
	} else {
		im->type = me_sprite::sprite_type::rgba_t;
		// Handle regular RGBA image
		auto rgba_ptr = argb_store.rgbas.get(rgbaName);
		if (rgba_ptr == nullptr) {
			rgba_ptr = new me_rgba();
			rgba_ptr->width = -1; // dummy
			rgba_ptr->loaded = false;
			argb_store.rgbas.add(rgbaName, rgba_ptr);
		}
		im->rgba = rgba_ptr;
	}
}

void PutRGBA(std::string name, int width, int height, int type)
{
	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) {
		rgba_ptr = new me_rgba();
		argb_store.rgbas.add(name, rgba_ptr);
	}

	rgba_ptr->type = type;
	if (!(rgba_ptr->width == width && rgba_ptr->height == height)) {
		rgba_ptr->width = width;
		rgba_ptr->height = height;
		rgba_ptr->atlasId = -1;
		rgba_ptr->loaded = false;
	}
}

void UpdateRGBA(std::string name, int len, char* rgba)
{
	//printf("update rgba:%s, len=%d\n", name.c_str(), len);
	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) return; // no such rgba...
	if (rgba_ptr->width == -1) return; // dummy rgba, not available.
	if (rgba_ptr->atlasId < 0) return; // not yet allocated.
	if (len != rgba_ptr->width * rgba_ptr->height * 4) 
		throw "rgba size not match";
	rgba_ptr->loaded = true;
	rgba_ptr->invalidate = false;
	me_update_rgba_atlas(argb_store.atlas, rgba_ptr->atlasId, (int)(rgba_ptr->uvStart.x), (int)(rgba_ptr->uvEnd.y), rgba_ptr->height, rgba_ptr->width, rgba, SG_PIXELFORMAT_RGBA8);
}

void SetRGBAStreaming(std::string name)
{
	auto rgba_ptr = argb_store.rgbas.get(name);
	if (rgba_ptr == nullptr) { // no such rgba, add dummy.
		rgba_ptr = new me_rgba();

		argb_store.rgbas.add(name, rgba_ptr);
	}
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
	if (rgba_ptr == nullptr || rgba_ptr->width < 0) return { 1,1,-2 };

	rgba_ptr->occurrence = 999999;
	if (rgba_ptr->streaming && rgba_ptr->atlasId!=-1 && rgba_ptr->loadLoopCnt<ui.loopCnt)
	{
		auto ptr = GetStreamingBuffer(name, rgba_ptr->width, rgba_ptr->height);
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
	if (rgba_ptr->atlasId!=-1)
		return { rgba_ptr->width,rgba_ptr->height, -3 }; //not loaded
    return { rgba_ptr->width,rgba_ptr->height, -1 };  //OOM
}

struct vert_attr
{
	glm::vec3 v_pos;
	uint32_t color;
};
// Parse SVG and create triangulated mesh
bool ParseSVG(me_svg* svg, std::vector<vert_attr>& attributes) {
    if (svg->content.empty()) {
        return false;
    }

    // Parse SVG with more detail (increase tesselation for curves)
    NSVGimage* image = nsvgParse(const_cast<char*>(svg->content.c_str()), "px", 96.0f);
    if (!image) {
        return false;
    }

    // Store SVG dimensions for normalization
    float svgWidth = image->width;
    float svgHeight = image->height;
    
    // Use higher tesselation for better curve approximation
    const float curveTesselation = 1.0f; // Lower value = more detail

    // For each shape in the SVG
    // for (NSVGshape* shape = image->shapes; shape != NULL; shape = shape->next) {
    //     if (!(shape->flags & NSVG_FLAGS_VISIBLE)) {
    //         continue;
    //     }
    //
    //     // Get fill information
    //     uint32_t fillColor = shape->fill.color;
    //     bool hasFill = shape->fill.type != NSVG_PAINT_NONE;
    //     
    //     // Get gradient information if available
    //     bool hasGradient = false;
    //     NSVGgradient* gradient = nullptr;
    //     if (shape->fill.type == NSVG_PAINT_LINEAR_GRADIENT || 
    //         shape->fill.type == NSVG_PAINT_RADIAL_GRADIENT) {
    //         hasGradient = true;
    //         gradient = shape->fill.gradient;
    //     }
    //     
    //     // Get stroke color
    //     uint32_t strokeColor = shape->stroke.color;
    //     bool hasStroke = shape->stroke.type != NSVG_PAINT_NONE && shape->strokeWidth > 0;
    //
    //     // For each path in the shape
    //     for (NSVGpath* path = shape->paths; path != NULL; path = path->next) {
    //         if (path->npts < 4) { // Need at least one segment (4 values for a cubic bezier)
    //             continue;
    //         }
    //
    //         // Create a higher detail path tesselation for better curve approximation
    //         // Convert bezier curves to line segments
    //         std::vector<std::array<float, 2>> polygon;
    //         
    //         // Extract path points
    //         float* p = &path->pts[0];
    //         float* prevP = nullptr;
    //         
    //         // First point
    //         polygon.push_back({p[0], p[1]});
    //         
    //         // Process each bezier segment
    //         for (int i = 0; i < path->npts-1; i += 3) {
    //             float* p0 = &path->pts[i*2];
    //             float* p1 = &path->pts[(i+1)*2];
    //             float* p2 = &path->pts[(i+2)*2];
    //             float* p3 = &path->pts[(i+3)*2];
    //             
    //             // Calculate how many line segments to use based on curve complexity
    //             float dx = p3[0] - p0[0];
    //             float dy = p3[1] - p0[1];
    //             float len = sqrtf(dx*dx + dy*dy);
    //             int segments = std::max(2, (int)(len / curveTesselation));
    //             
    //             // Generate points along the curve
    //             for (int j = 1; j <= segments; j++) {
    //                 float t = (float)j / (float)segments;
    //                 float t1 = 1.0f - t;
    //                 
    //                 // Cubic bezier interpolation
    //                 float x = t1*t1*t1*p0[0] + 3.0f*t1*t1*t*p1[0] + 3.0f*t1*t*t*p2[0] + t*t*t*p3[0];
    //                 float y = t1*t1*t1*p0[1] + 3.0f*t1*t1*t*p1[1] + 3.0f*t1*t*t*p2[1] + t*t*t*p3[1];
    //                 
    //                 polygon.push_back({x, y});
    //             }
    //         }
    //
    //         // If the path is closed, ensure first and last points match
    //         if (path->closed && polygon.size() > 1) {
    //             if (fabs(polygon[0][0] - polygon.back()[0]) > 0.01f || 
    //                 fabs(polygon[0][1] - polygon.back()[1]) > 0.01f) {
    //                 polygon.push_back(polygon[0]);
    //             }
    //         }
    //
    //         // Skip paths with too few points for triangulation
    //         if (polygon.size() < 3) {
    //             continue;
    //         }
    //
    //         // Prepare data for earcut
    //         using Point = std::array<float, 2>;
    //         std::vector<std::vector<Point>> polygons = { polygon };
    //         
    //         // Triangulate the polygon
    //         std::vector<uint32_t> indices = mapbox::earcut<uint32_t>(polygons);
    //         
    //         // No triangles generated
    //         if (indices.empty()) {
    //             continue;
    //         }
    //
    //         // Process filled paths
    //         if (hasFill) {
    //             // Handle different fill types
    //             if (hasGradient && gradient) {
    //                 // Add triangulated vertices with gradient color
    //                 for (size_t i = 0; i < indices.size(); i += 3) {
    //                     for (size_t j = 0; j < 3; j++) {
    //                         vert_attr vertex;
    //                         Point pt = polygon[indices[i + j]];
    //                         vertex.v_pos = glm::vec3(pt[0], pt[1], 0.0f);
    //                         
    //                         // Calculate color based on gradient
    //                         if (gradient->nstops > 0) {
    //                             float tx = pt[0] / svgWidth;
    //                             float ty = pt[1] / svgHeight;
    //                             
    //                             // For radial gradient
    //                             if (shape->fill.type == NSVG_PAINT_RADIAL_GRADIENT) {
    //                                 float dx = pt[0] - gradient->xform[4];
    //                                 float dy = pt[1] - gradient->xform[5];
    //                                 float r = sqrtf(dx*dx + dy*dy);
    //                                 float t = r / gradient->radius;
    //                                 
    //                                 // Find gradient stop
    //                                 uint32_t color1 = gradient->stops[0].color;
    //                                 uint32_t color2 = gradient->stops[0].color;
    //                                 float stopPos = 0.0f;
    //                                 
    //                                 for (int s = 0; s < gradient->nstops-1; s++) {
    //                                     if (t >= gradient->stops[s].offset && t < gradient->stops[s+1].offset) {
    //                                         color1 = gradient->stops[s].color;
    //                                         color2 = gradient->stops[s+1].color;
    //                                         stopPos = (t - gradient->stops[s].offset) / 
    //                                                  (gradient->stops[s+1].offset - gradient->stops[s].offset);
    //                                         break;
    //                                     }
    //                                 }
    //                                 
    //                                 // Interpolate colors
    //                                 uint8_t r1 = (color1 >> 0) & 0xFF;
    //                                 uint8_t g1 = (color1 >> 8) & 0xFF;
    //                                 uint8_t b1 = (color1 >> 16) & 0xFF;
    //                                 uint8_t a1 = (color1 >> 24) & 0xFF;
    //                                 
    //                                 uint8_t r2 = (color2 >> 0) & 0xFF;
    //                                 uint8_t g2 = (color2 >> 8) & 0xFF;
    //                                 uint8_t b2 = (color2 >> 16) & 0xFF;
    //                                 uint8_t a2 = (color2 >> 24) & 0xFF;
    //                                 
    //                                 uint8_t r = r1 + (r2 - r1) * stopPos;
    //                                 uint8_t g = g1 + (g2 - g1) * stopPos;
    //                                 uint8_t b = b1 + (b2 - b1) * stopPos;
    //                                 uint8_t a = a1 + (a2 - a1) * stopPos;
    //                                 
    //                                 // Convert to RGBA for OpenGL
    //                                 uint32_t color = (a << 24) | (b << 16) | (g << 8) | r;
    //                                 vertex.color = color;
    //                             }
    //                             // For linear gradient
    //                             else {
    //                                 // Apply gradient transformation
    //                                 float gx = tx * gradient->xform[0] + ty * gradient->xform[2] + gradient->xform[4];
    //                                 float gy = tx * gradient->xform[1] + ty * gradient->xform[3] + gradient->xform[5];
    //                                 
    //                                 // Calculate position along gradient
    //                                 float t = gx; // simplified, should use proper gradient vector
    //                                 t = std::max(0.0f, std::min(1.0f, t));
    //                                 
    //                                 // Find gradient stop
    //                                 uint32_t color1 = gradient->stops[0].color;
    //                                 uint32_t color2 = gradient->stops[0].color;
    //                                 float stopPos = 0.0f;
    //                                 
    //                                 for (int s = 0; s < gradient->nstops-1; s++) {
    //                                     if (t >= gradient->stops[s].offset && t < gradient->stops[s+1].offset) {
    //                                         color1 = gradient->stops[s].color;
    //                                         color2 = gradient->stops[s+1].color;
    //                                         stopPos = (t - gradient->stops[s].offset) / 
    //                                                  (gradient->stops[s+1].offset - gradient->stops[s].offset);
    //                                         break;
    //                                     }
    //                                 }
    //                                 
    //                                 // Interpolate colors
    //                                 uint8_t r1 = (color1 >> 0) & 0xFF;
    //                                 uint8_t g1 = (color1 >> 8) & 0xFF;
    //                                 uint8_t b1 = (color1 >> 16) & 0xFF;
    //                                 uint8_t a1 = (color1 >> 24) & 0xFF;
    //                                 
    //                                 uint8_t r2 = (color2 >> 0) & 0xFF;
    //                                 uint8_t g2 = (color2 >> 8) & 0xFF;
    //                                 uint8_t b2 = (color2 >> 16) & 0xFF;
    //                                 uint8_t a2 = (color2 >> 24) & 0xFF;
    //                                 
    //                                 uint8_t r = r1 + (r2 - r1) * stopPos;
    //                                 uint8_t g = g1 + (g2 - g1) * stopPos;
    //                                 uint8_t b = b1 + (b2 - b1) * stopPos;
    //                                 uint8_t a = a1 + (a2 - a1) * stopPos;
    //                                 
    //                                 // Convert to RGBA for OpenGL
    //                                 uint32_t color = (a << 24) | (b << 16) | (g << 8) | r;
    //                                 vertex.color = color;
    //                             }
    //                         } else {
    //                             // Default to solid color if no stops
    //                             // Convert ABGR to RGBA for OpenGL
    //                             uint32_t glFillColor = ((fillColor & 0xFF) << 24) | // Alpha
    //                                                  ((fillColor & 0xFF00) << 8) | // Blue
    //                                                  ((fillColor & 0xFF0000) >> 8) | // Green
    //                                                  ((fillColor & 0xFF000000) >> 24); // Red
    //                             vertex.color = glFillColor;
    //                         }
    //                         attributes.push_back(vertex);
    //                     }
    //                 }
    //             } else {
    //                 // Solid fill color
    //                 // Convert ABGR to RGBA for OpenGL
    //                 uint32_t glFillColor = ((fillColor & 0xFF) << 24) | // Alpha
    //                                       ((fillColor & 0xFF00) << 8) | // Blue
    //                                       ((fillColor & 0xFF0000) >> 8) | // Green
    //                                       ((fillColor & 0xFF000000) >> 24); // Red
    //                 
    //                 // Add triangulated vertices to attributes
    //                 for (size_t i = 0; i < indices.size(); i += 3) {
    //                     for (size_t j = 0; j < 3; j++) {
    //                         vert_attr vertex;
    //                         Point pt = polygon[indices[i + j]];
    //                         // Convert to 3D space (z=0 for flat SVG)
    //                         vertex.v_pos = glm::vec3(pt[0], pt[1], 0.0f);
    //                         vertex.color = glFillColor;
    //                         attributes.push_back(vertex);
    //                     }
    //                 }
    //             }
    //         }
    //         
    //         // Process stroked paths with enhanced detail
    //         if (hasStroke) {
    //             // Convert ABGR to RGBA for OpenGL
    //             uint32_t glStrokeColor = ((strokeColor & 0xFF) << 24) | // Alpha
    //                                     ((strokeColor & 0xFF00) << 8) | // Blue
    //                                     ((strokeColor & 0xFF0000) >> 8) | // Green
    //                                     ((strokeColor & 0xFF000000) >> 24); // Red
    //             
    //             // Generate line strip for stroke with more detail
    //             if (polygon.size() >= 2) {
    //                 float halfWidth = shape->strokeWidth * 0.5f;
    //                 
    //                 // For each line segment in the path
    //                 for (size_t i = 0; i < polygon.size() - 1; i++) {
    //                     Point p1 = polygon[i];
    //                     Point p2 = polygon[i + 1];
    //                     
    //                     // Calculate line direction
    //                     glm::vec2 line(p2[0] - p1[0], p2[1] - p1[1]);
    //                     float lineLength = glm::length(glm::vec2(line));
    //                     
    //                     if (lineLength > 0.001f) {
    //                         // Normalize line vector
    //                         line = glm::normalize(glm::vec2(line));
    //                         
    //                         // Calculate perpendicular vector
    //                         glm::vec2 perp(-line.y, line.x);
    //                         
    //                         // Create quad vertices
    //                         glm::vec3 v1(p1[0] + perp.x * halfWidth, p1[1] + perp.y * halfWidth, 0.0f);
    //                         glm::vec3 v2(p1[0] - perp.x * halfWidth, p1[1] - perp.y * halfWidth, 0.0f);
    //                         glm::vec3 v3(p2[0] + perp.x * halfWidth, p2[1] + perp.y * halfWidth, 0.0f);
    //                         glm::vec3 v4(p2[0] - perp.x * halfWidth, p2[1] - perp.y * halfWidth, 0.0f);
    //                         
    //                         // First triangle
    //                         attributes.push_back({v1, glStrokeColor});
    //                         attributes.push_back({v2, glStrokeColor});
    //                         attributes.push_back({v3, glStrokeColor});
    //                         
    //                         // Second triangle
    //                         attributes.push_back({v2, glStrokeColor});
    //                         attributes.push_back({v4, glStrokeColor});
    //                         attributes.push_back({v3, glStrokeColor});
    //                     }
    //                 }
    //                 
    //                 // Close the path if it's closed
    //                 if (path->closed && polygon.size() >= 3) {
    //                     Point p1 = polygon[polygon.size() - 1];
    //                     Point p2 = polygon[0];
    //                     
    //                     // Calculate line direction
    //                     glm::vec2 line(p2[0] - p1[0], p2[1] - p1[1]);
    //                     float lineLength = glm::length(glm::vec2(line));
    //                     
    //                     if (lineLength > 0.001f) {
    //                         // Normalize line vector
    //                         line = glm::normalize(glm::vec2(line));
    //                         
    //                         // Calculate perpendicular vector
    //                         glm::vec2 perp(-line.y, line.x);
    //                         
    //                         // Create quad vertices
    //                         glm::vec3 v1(p1[0] + perp.x * halfWidth, p1[1] + perp.y * halfWidth, 0.0f);
    //                         glm::vec3 v2(p1[0] - perp.x * halfWidth, p1[1] - perp.y * halfWidth, 0.0f);
    //                         glm::vec3 v3(p2[0] + perp.x * halfWidth, p2[1] + perp.y * halfWidth, 0.0f);
    //                         glm::vec3 v4(p2[0] - perp.x * halfWidth, p2[1] - perp.y * halfWidth, 0.0f);
    //                         
    //                         // First triangle
    //                         attributes.push_back({v1, glStrokeColor});
    //                         attributes.push_back({v2, glStrokeColor});
    //                         attributes.push_back({v3, glStrokeColor});
    //                         
    //                         // Second triangle
    //                         attributes.push_back({v2, glStrokeColor});
    //                         attributes.push_back({v4, glStrokeColor});
    //                         attributes.push_back({v3, glStrokeColor});
    //                     }
    //                 }
    //                 
    //                 // Handle line joins and caps for better appearance
    //                 if (shape->strokeLineJoin == NSVG_JOIN_ROUND) {
    //                     // Add round joins at each vertex (except first and last for open paths)
    //                     int startIdx = path->closed ? 0 : 1;
    //                     int endIdx = path->closed ? polygon.size() : polygon.size() - 1;
    //                     
    //                     for (int i = startIdx; i < endIdx; i++) {
    //                         Point p = polygon[i];
    //                         
    //                         // Create a small circle at each joint
    //                         const int segments = 8;
    //                         for (int j = 0; j < segments; j++) {
    //                             float angle1 = 2.0f * M_PI * j / segments;
    //                             float angle2 = 2.0f * M_PI * (j+1) / segments;
    //                             
    //                             glm::vec3 center(p[0], p[1], 0.0f);
    //                             glm::vec3 v1 = center + glm::vec3(cosf(angle1) * halfWidth, sinf(angle1) * halfWidth, 0.0f);
    //                             glm::vec3 v2 = center + glm::vec3(cosf(angle2) * halfWidth, sinf(angle2) * halfWidth, 0.0f);
    //                             
    //                             // Triangle fan
    //                             attributes.push_back({center, glStrokeColor});
    //                             attributes.push_back({v1, glStrokeColor});
    //                             attributes.push_back({v2, glStrokeColor});
    //                         }
    //                     }
    //                 }
    //             }
    //         }
    //     }
    // }

    // Free the parsed SVG image
    nsvgDelete(image);

    // Update buffer info
    svg->triangleCnt = attributes.size() / 3;
    
    return true;
}

// Function to declare a new SVG
void DeclareSVG(std::string name, std::string svgContent) {
    auto svg = svg_store.get(name);
    if (svg == nullptr) {
        svg = new me_svg();
		svg_store.add(name, svg);
    }else
    {
		sg_destroy_buffer(svg->svg_pos_color);
    }

    svg->content = svgContent;
	svg->loaded = false;

    // Parse the SVG content
	std::vector<vert_attr> attrs;
    ParseSVG(svg, attrs);
    
    // Create GPU buffers
    // Skip if no vertices or parse error
	if (attrs.empty()) {
		return;
	}

	// Create single interleaved buffer
	svg->svg_pos_color = sg_make_buffer({
		.size = attrs.size() * sizeof(vert_attr),
		.data = { attrs.data(), attrs.size() * sizeof(vert_attr) },
		});

	svg->loaded = true;
}


//  ██████  ██      ████████ ███████     ███    ███  ██████  ██████  ███████ ██      
// ██       ██         ██    ██          ████  ████ ██    ██ ██   ██ ██      ██      
// ██   ███ ██         ██    █████       ██ ████ ██ ██    ██ ██   ██ █████   ██      
// ██    ██ ██         ██    ██          ██  ██  ██ ██    ██ ██   ██ ██      ██      
//  ██████  ███████    ██    ██          ██      ██  ██████  ██████  ███████ ███████ 

void LoadModel(std::string cls_name, unsigned char* bytes, int length, ModelDetail detail)
{
	auto cls = gltf_classes.get(cls_name);
	if (cls == nullptr) {
		cls = new gltf_class();
		gltf_classes.add(cls_name, cls);
	} else {
		if (length==0)
		{
			// just modify model parameters...
			cls->dbl_face = detail.force_dbl_face;
			cls->color_bias = detail.color_bias;
			cls->color_scale = detail.contrast;
			cls->brightness = detail.brightness;
			cls->normal_shading = detail.normal_shading;
			cls->i_mat = glm::translate(glm::mat4(1.0f), -detail.center) * glm::scale(glm::mat4(1.0f), glm::vec3(detail.scale)) * glm::mat4_cast(detail.rotate);
			return;
		}
		cls->clear_me_buffers();
	}

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
	
	cls->dbl_face = detail.force_dbl_face;
	cls->color_bias = detail.color_bias;
	cls->color_scale = detail.contrast;
	cls->brightness = detail.brightness;
	cls->normal_shading = detail.normal_shading;
	
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
	size_t color_size = mesh_data.nvtx * sizeof(glm::u8vec4); // One color per vertex
	buffer.data.resize(pos_size + normal_size + color_size);

	// Copy positions
	memcpy(buffer.data.data(), mesh_data.positions, pos_size);
	
	// Copy normals
	memcpy(buffer.data.data() + pos_size, normals.data(), normal_size);

	// Generate and copy colors
	std::vector<int> colors(mesh_data.nvtx);
	std::fill(colors.begin(), colors.end(), mesh_data.color);
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
	colorAcc.componentType = TINYGLTF_COMPONENT_TYPE_UNSIGNED_BYTE;
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
	mesh_cls->color_bias = glm::vec3(0);
	mesh_cls->color_scale = 1;
	mesh_cls->brightness = 1;
	mesh_cls->normal_shading = 0.1f;

	DBG("define mesh cls:%s\n", cls_name.c_str());
}

void PutModelObject(std::string cls_name, std::string name, glm::vec3 new_position, glm::quat new_quaternion)
{
	// should be synced into main thread.
	auto cid = gltf_classes.getid(cls_name);
	if (cid == -1) return;
	auto t = gltf_classes.get(cid);

	auto ob = global_name_map.get(name);
	if (ob != nullptr)
	{
		auto oldobj = (gltf_object*)(ob->obj);
		auto obcid = oldobj->gltf_class_id;
		
		if (obcid != cid)
		{
			// change type.
			gltf_classes.get(obcid)->objects.remove(name, &t->objects); //transfer indexier
			oldobj->gltf_class_id = cid;
			oldobj->nodeattrs.resize(t->model.nodes.size());
			DBG("redefine %s to mesh %s\n", name.c_str(), cls_name.c_str());
		}
		oldobj->previous_position = oldobj->target_position = new_position;
		oldobj->previous_rotation = oldobj->target_rotation = new_quaternion;
		return;
	}

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
	

	DBG("put %s of mesh %s\n", name.c_str(), cls_name.c_str());
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
					((me_line_piece*)tn.obj)->flags[working_viewport_id] &= ~(1 << 5);
				}, [&] { //sprites
					((me_sprite*)tn.obj)->per_vp_stat[working_viewport_id] &= ~(1 << 0);
				},[&]  {  //world ui
					((me_world_ui*)tn.obj)->selectable[working_viewport_id] = false;
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
					((me_line_piece*)tn.obj)->flags[working_viewport_id] |= (1 << 5);
				}, [&] { // sprites;
					((me_sprite*)tn.obj)->per_vp_stat[working_viewport_id] |= (1 << 0);
				},[&]  {  // world ui
					((me_world_ui*)tn.obj)->selectable[working_viewport_id] = true;
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



//  ██████  ████████ ██   ██ ███████ ██████  ███████ 
// ██    ██    ██    ██   ██ ██      ██   ██ ██      
// ██    ██    ██    ███████ █████   ██████  ███████ 
// ██    ██    ██    ██   ██ ██      ██   ██      ██ 
//  ██████     ██    ██   ██ ███████ ██   ██ ███████ 

void SetWorkspacePropDisplayMode(int mode, std::string namePattern) {
	viewport_state_t::PropDisplayMode propMode = mode == 0
		? viewport_state_t::PropDisplayMode::AllButSpecified
		: viewport_state_t::PropDisplayMode::NoneButSpecified;

	bool changed = (propMode != working_viewport->propDisplayMode || 
	                working_viewport->namePatternForPropDisplayMode.compare(namePattern) != 0);

	if (changed)
		DBG("Set prop display of vp %d mode to %s with pattern '%s'\n", working_viewport_id,
			mode == viewport_state_t::PropDisplayMode::AllButSpecified ? "AllButSpecified" : "NoneButSpecified",
			namePattern.c_str());

	working_viewport->propDisplayMode = propMode;
	working_viewport->namePatternForPropDisplayMode = namePattern;
	
	// Recompute propDisplayVisible for all objects when pattern changes
	if (changed)
		recompute_all_prop_display_visible(working_viewport_id);
}


static std::vector<std::string> split_shader_lines(const std::string& source) {
	std::vector<std::string> lines;
	if (source.empty()) {
		lines.emplace_back();
		return lines;
	}
	size_t start = 0;
	while (start < source.size()) {
		size_t end = source.find('\n', start);
		size_t segment_end = (end == std::string::npos) ? source.size() : end;
		size_t len = segment_end - start;
		if (len > 0 && source[start + len - 1] == '\r')
			--len;
		lines.emplace_back(source.substr(start, len));
		if (end == std::string::npos)
			break;
		start = end + 1;
	}
	return lines;
}

static std::vector<int> extract_log_line_numbers(const std::string& log) {
	std::vector<int> numbers;
	size_t pos = 0;
	while (pos < log.size()) {
		size_t open = log.find('(', pos);
		if (open == std::string::npos)
			break;
		if (open == 0 || !std::isdigit(static_cast<unsigned char>(log[open - 1]))) {
			pos = open + 1;
			continue;
		}
		size_t close = log.find(')', open);
		if (close == std::string::npos)
			break;
		bool digits_inside = true;
		for (size_t i = open + 1; i < close; ++i) {
			if (!std::isdigit(static_cast<unsigned char>(log[i]))) {
				digits_inside = false;
				break;
			}
		}
		if (!digits_inside) {
			pos = close + 1;
			continue;
		}
		size_t after = close + 1;
		while (after < log.size() && std::isspace(static_cast<unsigned char>(log[after])))
			++after;
		if (after >= log.size() || log[after] != ':') {
			pos = close + 1;
			continue;
		}
		numbers.push_back(std::stoi(log.substr(open + 1, close - open - 1)));
		pos = close + 1;
	}
	return numbers;
}

static std::string build_line_snippet(const std::vector<std::string>& lines, const std::vector<int>& line_numbers) {
	if (lines.empty() || line_numbers.empty())
		return {};
	std::vector<int> unique_lines = line_numbers;
	std::sort(unique_lines.begin(), unique_lines.end());
	unique_lines.erase(std::unique(unique_lines.begin(), unique_lines.end()), unique_lines.end());

	const size_t max_contexts = 6;
	size_t contexts = 0;
	std::string snippet;
	for (int ln : unique_lines) {
		if (ln < 1 || ln > static_cast<int>(lines.size()))
			continue;
		if (contexts++ >= max_contexts) {
			snippet += "... additional locations omitted ...\n";
			break;
		}
		int start = std::max(1, ln - 1);
		int end = std::min(static_cast<int>(lines.size()), ln + 1);
		for (int idx = start; idx <= end; ++idx) {
			snippet += (idx == ln) ? "> " : "  ";
			snippet += "L";
			snippet += std::to_string(idx);
			snippet += ": ";
			snippet += lines[idx - 1];
			snippet += "\n";
		}
		snippet += "\n";
	}
	return snippet;
}

static std::string build_fallback_listing(const std::vector<std::string>& lines, size_t max_lines) {
	if (lines.empty())
		return {};
	size_t count = std::min(max_lines, lines.size());
	std::string listing;
	for (size_t i = 0; i < count; ++i) {
		listing += "  L";
		listing += std::to_string(i + 1);
		listing += ": ";
		listing += lines[i];
		listing += "\n";
	}
	if (lines.size() > max_lines)
		listing += "... truncated ...\n";
	return listing;
}

static std::string build_friendly_shader_error(const std::string& compile_log, const std::string& user_code) {
	std::string message = "Failed to compile custom background shader.\n";
	if (!compile_log.empty())
		message += compile_log;
	else
		message += "The GPU driver returned an empty error log.";

	auto lines = split_shader_lines(user_code);
	auto numbers = extract_log_line_numbers(compile_log);
	auto snippet = build_line_snippet(lines, numbers);
	if (!snippet.empty()) {
		message += "\nProblematic source context:\n";
		message += snippet;
	}
	else {
		auto listing = build_fallback_listing(lines, 40);
		if (!listing.empty()) {
			message += "\nUser shader preview:\n";
			message += listing;
		}
	}
	return message;
}

static std::string gather_shader_compile_log(const std::string& fragment_source) {
	GLuint shader = glCreateShader(GL_FRAGMENT_SHADER);
	if (shader == 0)
		return "Unable to create a GL shader for diagnostics.";
	const GLchar* src = fragment_source.c_str();
	glShaderSource(shader, 1, &src, nullptr);
	glCompileShader(shader);
	GLint log_len = 0;
	glGetShaderiv(shader, GL_INFO_LOG_LENGTH, &log_len);
	std::string log;
	if (log_len > 1) {
		log.resize(static_cast<size_t>(log_len));
		GLsizei written = 0;
		glGetShaderInfoLog(shader, log_len, &written, log.data());
		if (written > 0 && written <= log_len)
			log.resize(static_cast<size_t>(written));
	}
	glDeleteShader(shader);
	return log;
}

                                                  
void SetCustomBackgroundShader(std::string shaderCode) {
    // Clean up existing shader if any
	if (shared_graphics.custom_bg_shader.valid) {
		sg_destroy_pipeline(shared_graphics.custom_bg_shader.pipeline);
		sg_destroy_shader(shared_graphics.custom_bg_shader.shader);
	}
    
    shared_graphics.custom_bg_shader.code = shaderCode;
    shared_graphics.custom_bg_shader.valid = false;
    shared_graphics.custom_bg_shader.errorMessage.clear();
    
    // Prepare the shader code with appropriate uniforms and entry points
    std::string fragmentShaderSource = R"(#version 300 es
precision highp float;
uniform vec3 iResolution;
uniform float iTime;
uniform vec3 iCameraPos; 
uniform mat4 iPVM;
uniform mat4 iInvVM;
uniform mat4 iInvPM;
in vec2 texcoord;
out vec4 fragColor;

// User shader code begins
)";
    fragmentShaderSource += "\n#line 1\n";
    fragmentShaderSource += shaderCode;
    fragmentShaderSource += "\n#line 100000\n";
    fragmentShaderSource += R"(// User shader code ends

void main() {
    vec2 fragCoord = texcoord * iResolution.xy;
	fragColor.a = 1.0;
    mainImage(fragColor, fragCoord);
}
)";

    // Create shader
    sg_shader_desc desc = {};
    desc.attrs[0].name = "position";
    desc.vs.source = R"(#version 300 es
precision highp float;
in vec2 position;
out vec2 texcoord;

void main() {
    texcoord = position * 0.5 + 0.5;
    gl_Position = vec4(position, 1.0, 1.0); // Use z=1.0 for far plane
}
)";
    desc.vs.entry = "main";
    desc.fs.source = fragmentShaderSource.c_str();
    desc.fs.entry = "main";
    
    // Add uniforms
    // Calculate exact size: 2 floats + 1 float + padding + 3 floats + padding + 3 mat4s
    // vec2(8) + float(4) + vec3(12) + padding(4) + 3*mat4(3*64) = 220 bytes
    desc.fs.uniform_blocks[0].size = 224;
    desc.fs.uniform_blocks[0].layout = SG_UNIFORMLAYOUT_STD140;
    desc.fs.uniform_blocks[0].uniforms[0].name = "iResolution";
    desc.fs.uniform_blocks[0].uniforms[0].type = SG_UNIFORMTYPE_FLOAT3;
    desc.fs.uniform_blocks[0].uniforms[1].name = "iTime";
    desc.fs.uniform_blocks[0].uniforms[1].type = SG_UNIFORMTYPE_FLOAT;
    desc.fs.uniform_blocks[0].uniforms[2].name = "iCameraPos";
    desc.fs.uniform_blocks[0].uniforms[2].type = SG_UNIFORMTYPE_FLOAT3;
    desc.fs.uniform_blocks[0].uniforms[3].name = "iPVM";
    desc.fs.uniform_blocks[0].uniforms[3].type = SG_UNIFORMTYPE_MAT4;
    desc.fs.uniform_blocks[0].uniforms[4].name = "iInvVM";
    desc.fs.uniform_blocks[0].uniforms[4].type = SG_UNIFORMTYPE_MAT4;
    desc.fs.uniform_blocks[0].uniforms[5].name = "iInvPM";
    desc.fs.uniform_blocks[0].uniforms[5].type = SG_UNIFORMTYPE_MAT4;
    
    desc.label = "custom_background_shader";
    
    // Try to create the shader
    shared_graphics.custom_bg_shader.shader = sg_make_shader(&desc);
    
    // Check if shader creation was successful
    if (sg_query_shader_state(shared_graphics.custom_bg_shader.shader) == SG_RESOURCESTATE_VALID) {
        // Create pipeline
        sg_pipeline_desc pip_desc = {};
        pip_desc.shader = shared_graphics.custom_bg_shader.shader;
        pip_desc.layout.attrs[0].format = SG_VERTEXFORMAT_FLOAT2;
        pip_desc.primitive_type = SG_PRIMITIVETYPE_TRIANGLE_STRIP;
        pip_desc.sample_count = OFFSCREEN_SAMPLE_COUNT;
		pip_desc.colors[0].blend = { .enabled = true,
				.src_factor_rgb = SG_BLENDFACTOR_SRC_ALPHA,
				.dst_factor_rgb = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
				.src_factor_alpha = SG_BLENDFACTOR_ZERO,
				.dst_factor_alpha = SG_BLENDFACTOR_ONE, };
        pip_desc.label = "custom_background_pipeline";
        
        shared_graphics.custom_bg_shader.pipeline = sg_make_pipeline(&pip_desc);
        shared_graphics.custom_bg_shader.valid = true;
        shared_graphics.custom_bg_shader.errorMessage.clear();
    } else {
        auto compile_log = gather_shader_compile_log(fragmentShaderSource);
        auto friendly = build_friendly_shader_error(compile_log, shaderCode);
        shared_graphics.custom_bg_shader.errorMessage = friendly;
        DBG("%s\n", friendly.c_str());
    }
}

void DisableCustomBackgroundShader() {
	if (shared_graphics.custom_bg_shader.valid) {
		sg_destroy_pipeline(shared_graphics.custom_bg_shader.pipeline);
		sg_destroy_shader(shared_graphics.custom_bg_shader.shader);
		shared_graphics.custom_bg_shader.valid = false;
	}
}

void SetModelObjectProperty(std::string namePattern, const ModelObjectProperties& props)
{
    for (int i = 0; i < global_name_map.ls.size(); ++i) {
        auto mapping = global_name_map.get(i);
        if (wildcardMatch(global_name_map.getName(i), namePattern)) {
            RouteTypes(mapping, 
                [&] {
                    // point cloud - not applicable
                }, 
                [&](int class_id) {
                    // gltf object
                    auto t = (gltf_object*)mapping->obj;
                    if (props.baseAnimId_set) {
                        t->baseAnimId = props.baseAnimId;
                    }
                    if (props.nextAnimId_set) {
                        t->nextAnimId = props.nextAnimId;
						t->anim_switch = true;
                    }
                    if (props.material_variant_set) {
                        t->material_variant = props.material_variant;
                    }
                    if (props.team_color_set) {
                        t->team_color = props.team_color;
                    }
                    if (props.base_stopatend_set) {
                        t->baseAnimStopAtEnd = props.base_stopatend;
                    }
                    if (props.next_stopatend_set) {
                        t->nextAnimStopAtEnd = props.next_stopatend;
                    }
                    if (props.animate_asap_set && props.animate_asap) {
                        t->anim_switch_asap = true;
                    }
                }, 
                [&] {
                    // line piece - not applicable
                }, 
                [&] {
                    // sprite - not applicable
                },
                [&] {
                    // spot texts - not applicable
                },
                [&] {
                    // geometry - not applicable
                }
            );
        }
    }
}



void AddHandleIcon(std::string name, const handle_icon_info& info)
{
	auto t = handle_icons.get(name);
	auto hi = t == nullptr ? new me_handle_icon : t;
	
	hi->name = name;
	hi->current_pos = hi->target_position = hi->previous_position = info.position;
	hi->current_rot = hi->target_rotation = hi->previous_rotation = info.quat;
	hi->icon = info.icon;
	hi->txt_color = info.color;
	hi->bg_color = info.handle_color;
	hi->size = info.size;
		
	if (t == nullptr) {
		handle_icons.add(name, hi);
	}
}

void AddTextAlongLine(std::string name, const text_along_line_info& info)
{
	auto t = text_along_lines.get(name);
	auto tal = t == nullptr ? new me_text_along_line : t;
	
	tal->name = name;
	tal->current_pos = tal->target_position = tal->previous_position = info.start;
	tal->direction = info.direction;
	tal->text = info.text;
	tal->bb = info.bb;
	tal->color = info.color;
	tal->size = info.size;
	tal->voff = info.voff;

	if (info.dirProp.size() > 0) {
		auto ns = global_name_map.get(info.dirProp);
		if (ns != nullptr)
			set_reference(tal->direction_prop, ns->obj);
	}
	
	if (t == nullptr) {
		text_along_lines.add(name, tal);
	}
}

void SetGridAppearance(bool pivot_set, glm::vec3 pivot,
                      bool unitX_set, glm::vec3 unitX,
                      bool unitY_set, glm::vec3 unitY)
{
	auto& wstate = working_viewport->workspace_state.back();
	// Update operational grid parameters
	if (pivot_set) {
		wstate.operationalGridPivot = pivot;
	}
	
	if (unitX_set) {
		wstate.operationalGridUnitX = unitX;
	}
	
	if (unitY_set) {
		wstate.operationalGridUnitY = unitY;
	}
}

void SetGridAppearanceByView(bool pivot_set, glm::vec3 pivot)
{
	auto& wstate = working_viewport->workspace_state.back();
	// Update operational grid parameters
	if (pivot_set) {
		wstate.operationalGridPivot = pivot;
	}
	auto& camera = working_viewport->camera;

	// Extract unit vectors from the camera's view matrix
	// The view matrix encodes the camera's coordinate system
	glm::mat4 viewMatrix = camera.GetViewMatrix();
	
	// Extract the right and up vectors from the view matrix
	// The view matrix transforms world coordinates to camera coordinates
	// The first three columns represent the camera's right, up, and forward vectors (but in camera space)
	// We need the inverse to get world space vectors
	glm::mat4 invViewMatrix = glm::inverse(viewMatrix);
	
	// Extract right (X) and up (Y) vectors from the inverse view matrix
	wstate.operationalGridUnitX = glm::vec3(invViewMatrix[0]); // Right vector
	wstate.operationalGridUnitY = glm::vec3(invViewMatrix[1]); // Up vector
}

void SetSkyboxImage(int width, int height, int len, char* imageData) {
	// Clean up existing skybox image if any
	if (working_graphics_state->skybox_image.valid) {
		sg_destroy_image(working_graphics_state->skybox_image.image);
	}

	working_graphics_state->skybox_image.valid = false;

	// Validate input
	if (width <= 0 || height <= 0 || len <= 0 || imageData == nullptr) {
		working_graphics_state->skybox_image.errorMessage = "Invalid skybox image parameters";
		return;
	}

	// Expected size for RGBA8 format
	if (len != width * height * 4) {
		working_graphics_state->skybox_image.errorMessage = "Image data size doesn't match dimensions";
		return;
	}

	// Create image
	working_graphics_state->skybox_image.image = sg_make_image(sg_image_desc{
		.width = width,
		.height = height,
		.pixel_format = SG_PIXELFORMAT_RGBA8,
		.min_filter = SG_FILTER_LINEAR,
		.mag_filter = SG_FILTER_LINEAR,
		.wrap_u = SG_WRAP_REPEAT,
		.wrap_v = SG_WRAP_CLAMP_TO_EDGE,
		.data = {.subimage = {{ {
			.ptr = imageData,
			.size = (size_t)len
		}}}},
		.label = "skybox_equirectangular"
	});

	// Check if image creation was successful
	if (sg_query_image_state(working_graphics_state->skybox_image.image) != SG_RESOURCESTATE_VALID) {
		working_graphics_state->skybox_image.errorMessage = "Failed to create skybox image";
		return;
	}

	working_graphics_state->skybox_image.valid = true;
}

void RemoveSkyboxImage() {
	if (working_graphics_state->skybox_image.valid) {
		sg_destroy_image(working_graphics_state->skybox_image.image);
		working_graphics_state->skybox_image.valid = false;
	}
}

// forward decls for region voxel cache helpers
static void BuildRegionVoxelCache();

static inline int region_hash_index(int xq, int yq, int zq){
	// 18-bit hash width (256K buckets)
	return (xq * 73856093 ^ yq * 19349663 ^ zq * 83492791) & 0x3ffff;
}

static void BuildRegionVoxelCache()
{
	printf("rebuild voxel cache\n");
	// Build 2-tier hash: tier 0..1 with voxel sizes q, 10q (q from current wstate)
	float q = 1.0f;
	if (working_viewport) {
		q = std::max(working_viewport->workspace_state.back().voxel_quantize, 1e-6f);
	}
	float tiers[2] = { q, q*6.0f };

	// RGBA16I texture, width=2048, height=2048; initialize R to -32768 (empty sentinel)
	const int TEX_W = 2048;
	const int TEX_H = 2048;
	const int CHANNELS = 4;
	std::vector<int16_t> cache(TEX_W * TEX_H * CHANNELS, 0);
	for (size_t i = 0; i < (size_t)TEX_W * (size_t)TEX_H; ++i) cache[i * CHANNELS + 0] = (int16_t)-32768;

	auto pack_rgba4444 = [](uint32_t abgr)->int16_t{
		uint32_t A = (abgr >> 24) & 0xffu;
		uint32_t B = (abgr >> 16) & 0xffu;
		uint32_t G = (abgr >> 8)  & 0xffu;
		uint32_t R = (abgr)       & 0xffu;
		uint32_t r4 = R >> 4;
		uint32_t g4 = G >> 4;
		uint32_t b4 = B >> 4;
		uint32_t a4 = A >> 4;
		uint16_t p = (uint16_t)((r4 << 12) | (g4 << 8) | (b4 << 4) | (a4));
		return (int16_t)p;
	};

	int collide = 0;
	auto try_insert = [&](int tier, const glm::ivec3& cell, uint32_t color){
		int hx = region_hash_index(cell.x, cell.y, cell.z);
		int by = hx / 256;           // 0..1023 per tier
		int bx = hx - by * 256;      // 0..255 buckets per row
		int base_x = bx * 8;         // 8 slots per bucket
		int y = tier * 1024 + by;    // 2 tiers
		for (int k = 0; k < 8; ++k){
			int x = base_x + k;
			size_t idx = (size_t(y) * TEX_W + (size_t)x) * CHANNELS;
			int16_t r = cache[idx + 0];
			if (r == (int16_t)-32768){
				cache[idx + 0] = (int16_t)glm::clamp(cell.x, -32767, 32767);
				cache[idx + 1] = (int16_t)glm::clamp(cell.y, -32767, 32767);
				cache[idx + 2] = (int16_t)glm::clamp(cell.z, -32767, 32767);
				cache[idx + 3] = pack_rgba4444(color);
				return;
			}
			// duplicate check
			if (r != (int16_t)-32768){
				int16_t cx = cache[idx + 0];
				int16_t cy = cache[idx + 1];
				int16_t cz = cache[idx + 2];
				if (cx == (int16_t)cell.x && cy == (int16_t)cell.y && cz == (int16_t)cell.z) return;
			}
		}
		// if full, overwrite first slot (simple fallback)
		size_t idx0 = (size_t(y) * TEX_W + (size_t)base_x) * CHANNELS;
		cache[idx0 + 0] = (int16_t)glm::clamp(cell.x, -32767, 32767);
		cache[idx0 + 1] = (int16_t)glm::clamp(cell.y, -32767, 32767);
		cache[idx0 + 2] = (int16_t)glm::clamp(cell.z, -32767, 32767);
		cache[idx0 + 3] = pack_rgba4444(color);
		collide += 1;
	};

	for (int t = 0; t < 2; ++t){
		collide = 0;
		int total = 0;
		for (size_t i = 0; i < region_cloud_bunches.ls.size(); ++i){
			auto bunch = std::get<0>(region_cloud_bunches.ls[i]);
			if (!bunch) continue;
			for (auto& inst : bunch->items){
				uint32_t color = inst.color; // ABGR packed RGBA8
				float qs = tiers[t];
				glm::vec3 wq = inst.center / qs;
				glm::ivec3 f = glm::ivec3(glm::floor(wq));
				try_insert(t, f, color);
				total += 1;
			}
		}
		//printf("collision=%d/%d\n", collide, total);
	}

	// upload RGBA16SI texture
	texUpdatePartial(shared_graphics.region_cache, 0, 0, TEX_W, TEX_H, cache.data(), SG_PIXELFORMAT_RGBA16SI);
	shared_graphics.region_cache_dirty = false;
}

void ClearRegion3D(std::string name)
{
	auto t = region_cloud_bunches.get(name);
	if (t == nullptr) return;
	if (t->items.size() > 0) {
		t->items.clear();
		shared_graphics.region_cache_dirty = true;
	}
}

void AppendRegions3D(std::string name, int count, packed_region3d_t* regions)
{
	auto t = region_cloud_bunches.get(name);
	if (t == nullptr) {
		t = new me_region_cloud_bunch();
		region_cloud_bunches.add(name, t);
	}
	for (int i=0;i<count;i++)
		t->items.push_back(regions[i]);
	if (count > 0)
		shared_graphics.region_cache_dirty = true;
}

void FrameToFit(std::string name, float margin)
{
    auto nt = global_name_map.get(name);
    if (nt == nullptr || nt->obj == nullptr) return;
    if (nt->type < 1000) return; // only glTF

    auto obj = (gltf_object*)nt->obj;
    auto cls = gltf_classes.get(obj->gltf_class_id);
    if (!cls) return;

    glm::vec3 C = obj->target_position + cls->sceneDim.center;
	glm::vec3 h = glm::max(cls->sceneDim.halfExtents * 0.65f, glm::vec3(0.001f));

    auto& cam = working_viewport->camera;
    cam.extset = true;
    cam.stare = C;

    float vw = std::max(1, working_viewport->disp_area.Size.x);
    float vh = std::max(1, working_viewport->disp_area.Size.y);
    float aspect = vw / vh;

    if (cam.ProjectionMode == 0) {
        float theta_v = glm::radians(std::max(1.0f, cam._fov));
        float theta_h = 2.0f * atanf(aspect * tanf(theta_v * 0.5f));
        float d_box = std::max(
            h.y / std::max(tanf(theta_v * 0.5f), 1e-4f),
            h.x / std::max(tanf(theta_h * 0.5f), 1e-4f)
        );
        float R = glm::length(h);
        float half_min = std::min(theta_v * 0.5f, theta_h * 0.5f);
        float d_sph = R / std::max(sinf(half_min), 1e-4f);
        cam.distance = margin * std::max(d_box, d_sph);
    } else {
        float needX = h.x / std::max(cam._width, 1.0f);
        float needY = h.y / std::max(cam._height, 1.0f);
        float need = std::max(needX, needY);
        cam.distance = margin * cam.OrthoFactor * need;
    }
}

void UpdateHoloScreen(int bw, int bh, int blen, float* fptr)
{
	auto ws = &graphics_states[working_viewport_id];
	if (fptr==0)
	{
		if (ws->holo_biasfix.valid)
		{
			sg_destroy_image(ws->holo_biasfix.image);
			ws->holo_biasfix.valid = false;
		}
		return;
	}

	if (ws->holo_biasfix.valid)
	{
		sg_destroy_image(ws->holo_biasfix.image);
		ws->holo_biasfix.valid = false;
	}

	if (bw > 0 && bh > 0 && blen == bw * bh)
	{
		auto img = sg_make_image(sg_image_desc{
			.width = bw,
			.height = bh,
			.pixel_format = SG_PIXELFORMAT_R32F,
			.min_filter = SG_FILTER_LINEAR,
			.mag_filter = SG_FILTER_LINEAR,
			.data = {.subimage = {{ { fptr, (size_t)blen * sizeof(float) } }}},
			.label = "holo_biasfix_image"
			});
		if (sg_query_image_state(img) == SG_RESOURCESTATE_VALID)
		{
			ws->holo_biasfix.image = img;
			ws->holo_biasfix.valid = true;
			ws->holo_biasfix.width = bw;
			ws->holo_biasfix.height = bh;
		}
		else
		{
			sg_destroy_image(img);
		}
	}
}