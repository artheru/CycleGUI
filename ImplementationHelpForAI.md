# CycleGUI Implementation Guide for AI

This document provides methodology and architecture overview for extending CycleGUI.

---

## File Architecture

### C# Layer
- **Workspace.UIOps.cs / Workspace.Props.cs**: Define operations and properties, implement `Serialize()` method
- **PanelBuilder.Controls.cs**: Define UI controls with unique command IDs
- **CycleGUI.cs**: Core GUI management
- **Panel.cs**: Panel lifecycle and state management

### C++ Layer
- **cycleui_impl.cpp**: Command parsing and dispatch
  - `ActualWorkspaceQueueProcessor`: Parse workspace commands (67+ handlers)
  - `ProcessUIStack`: Parse UI control commands
- **cycleui.h**: API definitions and data structures
- **me_impl.h**: Graphics objects and rendering data structures
- **interfaces.hpp**: Bridge between parsed commands and graphics engine
- **messyengine_impl.cpp**: Rendering pipeline and operation implementations
- **init_impl.hpp**: Graphics initialization (pipelines, buffers, textures)
- **objects.hpp**: GLTF object handling
- **shaders/*.glsl**: Shader source files (auto-converted to .h by gen_shader.bat)

---

## Object System Architecture

### Object Type Hierarchy

All workspace objects inherit from `me_obj`:

```cpp
struct me_obj {
    std::string name;
    int type_id;                     // Object type identifier
    glm::vec3 previous_position, target_position, current_pos;
    glm::quat previous_rotation, target_rotation, current_rot;
    reference_t anchor;              // Can reference another object
    glm::vec3 offset_pos;
    glm::quat offset_rot;
    int anchor_subid = -1;
    
    bool show[MAX_VIEWPORTS];                      // Per-viewport visibility
    bool propDisplayVisible[MAX_VIEWPORTS];        // Computed visibility based on name patterns
    
    virtual void compute_pose();                   // Interpolate position/rotation
};
```

### Object Types

| Type ID | Object Type | Struct | Indexer |
|---------|-------------|--------|---------|
| -1 | Special (mouse, camera) | `me_special` | `special_objects` |
| 1 | Point Cloud | `me_pcRecord` | `pointclouds` |
| 2 | Line (piece/bunch) | `me_line_obj` | `line_pieces`, `line_bunches` |
| 3 | Sprite (Image/SVG) | `me_sprite` | `sprites` |
| 4 | World UI (Handle/Text) | `me_world_ui` | `handle_icons`, `text_along_lines` |
| 8 | Gaussian Splats | `me_gaussian_splats` | `gaussian_splats` |
| 1000+ | GLTF Model | `gltf_object` | `gltf_classes` |

### RouteTypes Pattern

Use `RouteTypes()` to uniformly process different object types:

```cpp
for (int gi = 0; gi < global_name_map.ls.size(); ++gi) {
    auto nt = global_name_map.get(gi);
    auto name = global_name_map.getName(gi);
    RouteTypes(nt, 
        [&] { /* point cloud */ auto t = (me_pcRecord*)nt->obj; },
        [&](int class_id) { /* gltf */ auto t = (gltf_object*)nt->obj; },
        [&] { /* line */ auto t = (me_line_obj*)nt->obj; },
        [&] { /* sprites */ auto t = (me_sprite*)nt->obj; },
        [&] { /* spot texts */ auto t = (me_world_ui*)nt->obj; },
        [&] { /* geometry */ });
}
```

### Object Reference System

Objects can reference other objects (e.g., camera following object):

```cpp
// Set reference
Workspace.Prop(new SetObjectMoonTo { 
    name = "follower", 
    earth = "target_object" 
});

// In C++: reference_t anchor field
set_reference(obj->anchor, target_obj);
anchor.obj->compute_pose();  // Updates follower position
```

---

## Rendering Pipeline

### Main Rendering Flow

**Function: `DefaultRenderWorkspace(disp_area_t disp_area, ImDrawList* dl)`**

1. **Pose Computation** - `compute_pose()` for all objects (interpolation)
2. **Camera Update** - `camera_manip()`
3. **Point Clouds** - EDL-aware rendering
4. **GLTF Models** - Multi-pass hierarchy computation
5. **Line Objects** - Line bunches and pieces
6. **Sprites** - RGBA and SVG sprites
7. **World UI** - Handle icons and text along lines
8. **Post-Processing**:
   - SSAO (Screen Space Ambient Occlusion)
   - WBOIT (Weighted Blended Order-Independent Transparency)
   - Bloom (Shine effect)
   - Ground SSR (Screen Space Reflection)
   - Region3D (Volumetric voxels)
9. **Compositing** - Final composition with custom shaders
10. **UI Overlay** - ImGui draw list rendering

### Rendering Passes

CycleGUI uses multiple render passes (Sokol GFX):

- `pc_primitive.pass`: Point cloud rendering → depth + color
- `edl_lres.pass`: Eye-Dome Lighting (point cloud enhancement)
- `primitives.pass`: GLTF opaque objects
- `wboit.accum_pass`: Transparent object accumulation
- `wboit.reveal_pass`: Transparent object revealage
- `wboit.compose_pass`: Blend transparency
- `ssao.pass`: SSAO generation
- `ground_effect.pass`: Ground reflection
- `region3d.pass`: Volumetric region rendering
- `temp_render_pass`: Final composition
- `default_pass`: Blit to screen

### Graphics State

- **shared_graphics**: Shared across all viewports (pipelines, buffers, atlases)
- **working_graphics_state**: Per-viewport render targets and state
- **working_viewport**: Current viewport being rendered
- **working_viewport_id**: Current viewport index

Use `switch_context(vid)` to change current viewport.

---

## Selection System

### Selection Modes

```cpp
enum selecting_mode_t { 
    click = 0,   // Single click selection
    drag = 1,    // Rectangle drag selection
    paint = 2    // Paint-brush selection
};
```

### How Selection Works

1. **TCIN Texture**: Type-Class-Instance-Node texture stores object IDs
   - `x`: Object type (1=pointcloud, 2=line, 3=sprite, 4=ui, 1000+=gltf)
   - `y, z, w`: Encoded instance ID and sub-ID

2. **Click Selection**: Check 7x7 pixel patch around cursor
3. **Drag Selection**: Read TCIN for rectangle region
4. **Paint Selection**: Use `painter_data` array (4x4 downsampled grid)

5. **Fine Selection**: For point clouds and handles, project all points to screen and test against selection region

### Object Flags

Point clouds use bit flags (in `flag` field):
```
bit 0: border
bit 1: shine
bit 2: bring to front
bit 3: selected
bit 4: can select by point
bit 5: selectable by handle
bit 6: selected as whole
bit 7: selectable (whole object)
bit 8: sub-selectable
bit 9: currently sub-selected
```

GLTF objects use similar flags in `flags[viewport_id]`.

---

## Feedback System

### Feedback Modes

```cpp
enum feedback_mode {
    pending,              // No feedback yet
    feedback_finished,    // Operation completed, send final result
    feedback_continued,   // Send intermediate result, keep operation active
    realtime_event,       // Real-time streaming (gesture controls)
    operation_canceled    // Operation canceled, revert changes
};
```

### Sending Feedback to C#

Use WSFeed macros in `feedback(unsigned char*& pr)`:

```cpp
void my_operation::feedback(unsigned char*& pr) {
    WSFeedInt32(someInt);
    WSFeedFloat(someFloat);
    WSFeedString(str.c_str(), str.length());
    WSFeedBool(someBool);
    WSFeedBytes(data, length);
}
```

### Interactive Processing

Register feedback processors in `interactive_processing_list`:

```cpp
std::vector<std::function<bool(unsigned char*&)>> interactive_processing_list{
    MainMenuBarResponse,
    do_queryViewportState,
    do_queryInputState,
    do_queryGraphics,
    CaptureViewport,
    TestSpriteUpdate,
    report_custom_shader_exception
};
```

---

## Adding New APIs

### 1. Panel Control

**C# Side (PanelBuilder.Controls.cs):**
```csharp
public bool MyControl(string label, ref float value) {
    var (cb, myid) = start(label, 99); // 99 = new command ID
    bool changed = _panel.PopState(myid, out var newVal);
    if (changed) value = (float)newVal;
    
    cb.Append(value);
    commands.Add(new ByteCommand(cb.AsMemory()));
    return changed;
}
```

**C++ Side (cycleui_impl.cpp ProcessUIStack):**

Find the highest case number in UIFuns array (currently ~105), add:

```cpp
[&] { // 106: MyControl
    auto myid = ReadInt;
    auto label = ReadString;
    auto value = ReadFloat;
    
    if (ImGui::DragFloat(label, &value, 0.1f)) {
        WriteInt(myid);
        WriteFloat(value);
        stateChanged = true;
    }
}
```

### 2. Workspace Property

**C# Side (Workspace.Props.cs):**

```csharp
public class PutMyObject : WorkspaceProp {
    public Vector3 position;
    public float size;
    
    protected internal override void Serialize(CB cb) {
        cb.Append(70); // New command ID (find highest in ActualWorkspaceQueueProcessor)
        cb.Append(name);
        cb.Append(position.X);
        cb.Append(position.Y);
        cb.Append(position.Z);
        cb.Append(size);
    }
    
    internal override void Submit() {
        SubmitReversible($"myobj#{name}");
    }
    
    public override void Remove() {
        RemoveProp($"myobj#{name}", name);
    }
}
```

**C++ Side:**

1. **cycleui.h** - Add data structure:
```cpp
struct my_object_info {
    glm::vec3 position;
    float size;
};
void AddMyObject(std::string name, const my_object_info& what);
```

2. **me_impl.h** - Add object type:
```cpp
struct me_my_object : me_obj {
    const static int type_id = 9; // New type ID
    sg_buffer data_buffer;
    float size;
};
indexier<me_my_object> my_objects;
```

3. **cycleui_impl.cpp** - Add command handler:
```cpp
[&] { // 70: PutMyObject
    auto name = ReadString;
    auto posX = ReadFloat;
    auto posY = ReadFloat;
    auto posZ = ReadFloat;
    auto size = ReadFloat;
    
    my_object_info info;
    info.position = glm::vec3(posX, posY, posZ);
    info.size = size;
    
    AddMyObject(name, info);
}
```

4. **interfaces.hpp** - Implement Add function:
```cpp
void AddMyObject(std::string name, const my_object_info& what) {
    auto t = global_name_map.get(name);
    me_my_object* obj = nullptr;
    
    if (t != nullptr) {
        obj = (me_my_object*)t->obj;
        // Clean up old resources
    } else {
        obj = new me_my_object();
        my_objects.add(name, obj);
    }
    
    obj->name = name;
    obj->target_position = what.position;
    obj->size = what.size;
    
    // Create GPU resources (buffers, textures, etc.)
}
```

5. **messyengine_impl.cpp** - Add rendering code in `DefaultRenderWorkspace`:
```cpp
// Render my objects
for (int i = 0; i < my_objects.ls.size(); ++i) {
    auto obj = my_objects.get(i);
    if (!obj->show[working_viewport_id]) continue;
    if (!obj->propDisplayVisible[working_viewport_id]) continue;
    
    // Apply transformations and render
}
```

6. **Update RouteTypes** - Add handler in `RouteTypes()` function

---

## Critical Implementation Details

### C++ Macro Rules

**Read Macros** (defined in cycleui_impl.cpp):
```cpp
#define ReadInt *(int*)ptr; ptr += 4
#define ReadFloat *(float*)ptr; ptr += 4
#define ReadBool *(bool*)ptr; ptr += 1
#define ReadString (char*)(ptr + 4); ptr += *((int*)ptr) + 4
#define ReadArr(type, len) (type*)ptr; ptr += len * sizeof(type)
```

**MUST use assignment**: 
- ✅ `auto value = ReadInt;`
- ❌ `myFunc(ReadInt)` - WRONG! Macro expands to multiple statements

**Write Macros** (for feedback):
```cpp
#define WSFeedInt32(x) { *(int*)pr=x; pr+=4; }
#define WSFeedFloat(x) { *(float*)pr=x; pr+=4; }
#define WSFeedBool(x) { *(bool*)pr=x; pr+=1; }
#define WSFeedString(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len; }
#define WSFeedBytes(x, len) { *(int*)pr=len; pr+=4; memcpy(pr, x, len); pr+=len; }
```

### Object Flag Management

Objects use per-viewport flags to track state:

**Point Cloud Flags** (`me_pcRecord::flag`):
- Use bitwise operations: `flag |= (1 << bit)` to set, `flag &= ~(1 << bit)` to clear
- Test: `if (flag & (1 << bit))`

**GLTF Flags** (`gltf_object::flags[viewport_id]`):
- Same bitwise pattern
- Always use viewport-specific index

### Multi-Viewport Support

```cpp
// Switch to viewport
switch_context(vid);  // Updates working_viewport, working_graphics_state, working_viewport_id

// Access current viewport
working_viewport->camera.GetViewMatrix();
working_viewport->workspace_state.back();

// Per-viewport data
obj->show[working_viewport_id];
obj->flags[working_viewport_id];
```

**Important**: 
- Viewport 0 = main workspace
- Viewports 1-15 = auxiliary viewports (created by `GUI.PromptWorkspaceViewport`)

---

## Operation System

### Operation Base Class

```cpp
struct abstract_operation {
    virtual std::string Type() = 0;
    virtual void pointer_down() = 0;   // Mouse/touch press
    virtual void pointer_move() = 0;   // Mouse/touch move
    virtual void pointer_up() = 0;     // Mouse/touch release
    virtual void canceled() = 0;       // Cancel operation
    virtual void feedback(unsigned char*& pr) = 0;  // Send result to C#
    virtual void draw(disp_area_t, ImDrawList*, glm::mat4 vm, glm::mat4 pm) = 0;
    virtual void destroy() = 0;
};
```

### Common Operations

1. **select_operation**: Object selection (click/drag/paint)
2. **guizmo_operation**: 3D transform gizmo
3. **follow_mouse_operation**: Mouse-following operations (drag, measure)
4. **positioning_operation**: Get 3D position from mouse click
5. **gesture_operation**: Virtual joystick/buttons
6. **mouse_action_operation**: Register custom mouse event handlers

### Operation Lifecycle

```cpp
// Start operation
BeginWorkspace<my_operation>(id, name, vstate);
wstate->operation = new my_operation();

// Process input
wstate->operation->pointer_down();
wstate->operation->pointer_move();
wstate->operation->pointer_up();

// Set feedback mode
wstate->feedback = feedback_finished; // or feedback_continued, realtime_event

// System calls feedback()
wstate->operation->feedback(pr);

// End operation (automatic when feedback_finished or operation_canceled)
working_viewport->pop_workspace_state();
```

---

## Gesture System (UseGesture)

### Widget Architecture

All widgets inherit from `widget_definition`:

```cpp
struct widget_definition {
    std::string widget_name, display_text;
    std::vector<std::string> keyboard_mapping;
    std::vector<std::string> joystick_mapping;
    
    int kj_handle_loop;  // Track keyboard/joystick handling
    int pointer;         // Touch pointer ID
    
    virtual void process(disp_area_t, ImDrawList*) = 0;
    virtual void feedback(unsigned char*& pr) = 0;
    virtual void keyboardjoystick_map() = 0;  // Map input to widget state
};
```

### Widget Types

1. **button_widget**: Press/release button
2. **toggle_widget**: On/off switch
3. **throttle_widget**: Slider (single or dual direction)
4. **stick_widget**: 2D joystick

### Input Priority

1. **Keyboard/Joystick**: If `isKJHandling()` returns true
2. **Touch Screen**: If no K/J input for 1 frame
3. **Mouse**: Only if not captured by ImGui

---

## Shader System

### Custom Background Shader

Users can provide ShaderToy-style GLSL:

```glsl
void mainImage(out vec4 fragColor, in vec2 fragCoord) {
    // Available uniforms:
    // iResolution (vec3): viewport width, height, aspect
    // iTime (float): time in seconds
    // iCameraPos (vec3): camera position
    // iPVM (mat4): projection * view matrix
    // iInvVM (mat4): inverse view matrix
    // iInvPM (mat4): inverse projection matrix
    
    vec2 uv = fragCoord / iResolution.xy;
    fragColor = vec4(uv, 0.5, 1.0);
}
```

Shader compilation in `interfaces.hpp::SetCustomBackgroundShader()`.

### Shader Files

- Source: `libVRender/shaders/*.glsl`
- Generated: `libVRender/shaders/*.h`
- Use `CheckAndGenerateShaders.ps1` to regenerate headers

**Don't manually edit .h files!**

---

## Touch Input Handling

### Touch State Machine

States in `process_remaining_touches()`:
- **State 0**: No touch
- **State 1**: One finger down (prepare)
- **State 2**: One finger drag (left mouse button)
- **State 3**: Two fingers (pinch/zoom, right mouse button)
- **State 5**: Three fingers (rotate, middle mouse button)
- **State 7**: Two finger pan (ImGui wheel event)

### Touch Consumption

```cpp
struct touch_state {
    int id;
    float touchX, touchY;
    bool starting;       // First frame of this touch
    bool consumed;       // Already handled by a widget/operation
};
```

Widgets should set `touch.consumed = true` to prevent other handlers from using it.

---

## Performance Optimization

### Object Culling

Check visibility before rendering:
```cpp
if (!obj->show[working_viewport_id]) continue;
if (!obj->propDisplayVisible[working_viewport_id]) continue;
```

### Prop Display Mode

Control what objects are visible per-viewport:

```cpp
// SetWorkspacePropDisplayMode
viewport.propDisplayMode = AllButSpecified;  // Show all except pattern
viewport.namePatternForPropDisplayMode = "hidden_*";

// Recompute for all objects
recompute_all_prop_display_visible(viewport_id);
```

### GPU Instancing

GLTF models use instancing:
- `node_meta` texture: Per-node transforms
- `instance_meta` texture: Per-instance animation state
- Multi-pass hierarchy propagation (up to depth 8)

### Atlas Management

Sprites use texture atlas (`argb_store.atlas`):
- Dynamic allocation with rectangle packing
- LRU-style replacement based on `occurrence` (pixels viewed)
- Auto-expansion up to 32 slices

---

## Special Objects

### Built-in Special Objects

- **me::mouse**: Mouse position in 3D (computed via ray-plane intersection)
- **me::camera**: Current camera position
- **me::camera(viewport_title)**: Specific viewport's camera

Usage in C#:
```csharp
Workspace.Prop(new PutVector {
    start = Vector3.Zero,
    propEnd = "me::mouse"  // Vector follows mouse
});
```

### Name Wildcards

Supports wildcard matching:
- `"object_*"`: Matches `object_1`, `object_2`, etc.
- `"*_suffix"`: Matches anything ending with `_suffix`
- Implemented in `wildcardMatch()` function

---

## Color Format

### ABGR Format (uint32)

CycleGUI uses ABGR format: `0xAABBGGRR`

```cpp
// C++
uint32_t red = 0xff0000ff;    // Opaque red
uint32_t semi_transparent_green = 0x8000ff00;

// C#
uint color = Color.Red.RGBA8();  // Extension method converts to ABGR
```

### Conversion

```cpp
// RGB (0-1) to ABGR
uint8_t r = (uint8_t)(glm::clamp(rgb.x, 0.0f, 1.0f) * 255);
uint8_t g = (uint8_t)(glm::clamp(rgb.y, 0.0f, 1.0f) * 255);
uint8_t b = (uint8_t)(glm::clamp(rgb.z, 0.0f, 1.0f) * 255);
uint8_t a = (uint8_t)(glm::clamp(opacity, 0.0f, 1.0f) * 255);
uint32_t color = (a << 24) | (b << 16) | (g << 8) | r;
```

---

## Debugging

### Timing Macros

```cpp
TOC("my_operation")  // Print time since last TOC call
```

### Debug Output

```cpp
DBG("Debug message: %d\n", value);  // Only in debug builds
```

### Render Debug Window

```cpp
if (ui.displayRenderDebug()) {
    ImGui::Text("Debug info");
    ImGui::Checkbox("Toggle feature", &feature);
}
```

Access by clicking the app name in top-left corner.

---

## Best Practices

### 1. Object Lifecycle

```cpp
// Add object
my_objects.add(name, obj);

// Update object (reuse existing)
auto t = my_objects.get(name);
if (t != nullptr) { /* update */ } 
else { t = new me_my_object(); my_objects.add(name, t); }

// Remove object
my_objects.remove(name);
delete obj;
```

### 2. GPU Resource Management

```cpp
// Create buffer
sg_buffer buf = sg_make_buffer(sg_buffer_desc{
    .size = dataSize,
    .usage = SG_USAGE_IMMUTABLE,  // or SG_USAGE_STREAM for dynamic
    .data = { dataPtr, dataSize }
});

// Update buffer (if STREAM usage)
sg_update_buffer(buf, sg_range{ data, size });

// Destroy when done
sg_destroy_buffer(buf);
```

### 3. Reference Management

```cpp
// Set reference
set_reference(obj->anchor, target_obj);

// Check reference
if (obj->anchor.obj != nullptr) {
    auto target_pos = obj->anchor.obj->current_pos;
}

// Clear reference (called automatically on object destruction)
obj->anchor.remove_from_obj();
```

### 4. Viewport Safety

Always check viewport validity:
```cpp
if (viewport_id < 0 || viewport_id >= MAX_VIEWPORTS) return;
if (!obj->show[working_viewport_id]) continue;
```

### 5. NaN Protection

```cpp
if (!std::isfinite(value.x) || !std::isfinite(value.y)) continue;
if (std::isnan(value)) continue;
```

---

## Common Pitfalls

### ❌ Don't: Use Read macros in function calls
```cpp
// WRONG
AddObject(ReadString, ReadFloat);

// CORRECT
auto name = ReadString;
auto value = ReadFloat;
AddObject(name, value);
```

### ❌ Don't: Forget to update both position and rotation
```cpp
// WRONG - only sets target
obj->target_position = newPos;

// CORRECT - set all three for smooth interpolation
obj->previous_position = obj->target_position = obj->current_pos = newPos;
```

### ❌ Don't: Modify shader .h files directly
Use `gen_shader.bat` to regenerate from .glsl sources.

### ❌ Don't: Forget viewport index
```cpp
// WRONG - uses wrong viewport
obj->show[0] = true;

// CORRECT - uses current viewport
obj->show[working_viewport_id] = true;
```

### ✅ Do: Check object existence before access
```cpp
auto obj = my_objects.get(name);
if (obj == nullptr) return;  // or create new
```

### ✅ Do: Use propDisplayVisible for culling
```cpp
if (!obj->propDisplayVisible[working_viewport_id]) continue;
```

### ✅ Do: Cleanup resources on remove
```cpp
if (obj->buffer.id != 0) sg_destroy_buffer(obj->buffer);
delete obj;
```

---

## Testing Workflow

1. **Implement C# API** in Workspace.Props.cs
2. **Add command handler** in cycleui_impl.cpp (find next available ID)
3. **Implement C++ function** in interfaces.hpp
4. **Add data structures** in cycleui.h and me_impl.h if needed
5. **Add rendering code** in messyengine_impl.cpp
6. **Test in LearnCycleGUI** - add demo in appropriate Demo*.cs file
7. **Verify Web version** - ensure no platform-specific code

---

## Advanced Topics

### Weighted Blended Order-Independent Transparency (WBOIT)

Three-pass rendering for transparent objects:
1. **Accum Pass**: Accumulate weighted colors
2. **Reveal Pass**: Compute revealage
3. **Compose Pass**: Blend with opaque scene

### Eye-Dome Lighting (EDL)

Two-pass filter for point cloud enhancement:
1. Low-res depth blur
2. Compose with depth-aware shading

### Region3D (Volumetric Voxels)

- Hash-based voxel cache (18-bit hash, 256K buckets)
- Multi-tier storage in texture
- Raymarching in fragment shader
- Use `Painter.DrawRegion3D()` to add voxels

### SLAM Local Map Support

Point clouds with `type = 1`:
- Precompute angular distance bins (256 bins, 0.1m units)
- Stored in `walkable_cache` texture
- Used for navigation/path planning visualization

---

## Example: Adding DrawDot to Painter

This shows the full pipeline for a simple feature:

**1. No C# changes needed** (Painter.DrawDot already exists)

**2. C++ Implementation** (already in interfaces.hpp):
```cpp
void AddDot(glm::vec3 pos, uint32_t color) {
    // Dots are just points with size=5
    AddPointToBunch(painter_name, pos, 5.0f, color);
}
```

Done! The existing point cloud renderer handles it.

---

## Code Study Checklist

When adding a feature, study these sections:

- [ ] Find similar existing feature
- [ ] Check command ID availability
- [ ] Understand data flow: C# → cycleui_impl.cpp → interfaces.hpp → messyengine_impl.cpp
- [ ] Identify required GPU resources (buffers, textures, pipelines)
- [ ] Check if RouteTypes needs update
- [ ] Verify multi-viewport compatibility
- [ ] Test object removal/cleanup
- [ ] Add demo to LearnCycleGUI

---

## Quick Reference

### Find Next Available Command ID

```bash
# Workspace commands
grep -n "^\t\[&\]" libVRender/cycleui_impl.cpp | tail -1

# Panel UI commands
grep -n "case [0-9]*:" libVRender/cycleui_impl.cpp | grep ProcessUIStack | tail -1
```

### Common Patterns

**Add Object**:
```
C#: Serialize(CB) with Append()
C++: Read* macros → Call Add*() function in interfaces.hpp
```

**Remove Object**:
```
C#: WorkspaceProp.RemoveNamePattern()
C++: Route to indexier.remove() → delete resources
```

**Update Object**:
```
C#: Call Workspace.Prop() again with same name
C++: Check if exists, reuse or create new
```

---

**Remember**: LearnCycleGUI is the best documentation - study its Demo*.cs files to see real usage patterns!
