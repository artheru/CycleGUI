This file works as a guide for AI to add functionality or revise CycleGUI library.
this file should only contains methodology.
only add gereral help, don't add help to specific functionality.
when the implementation keypoints is missing, add to this fil

# Files: 
Workspace.UIOps.cs/Workspace.Props.cs: + class for operation, note the feedback type, parse.
cycleui_impl.cpp: ActualWorkspaceQueueProcessor:workspace api, mainly parse arguments, no rendering. use api in cycleui.h to pass to interface.hpp to process messyengine related thing.
cycleui.h: +cpp definition, data structure.
me_impl.h: +graphics definition and data structure.
interface.hpp: +bridge parsed api argument to graphics.
init_impl.hpp: initialization.
objects.hpp: gltf object handling.
messyengine_impl.cpp: draw and operation, if should output value, set feedback type(enum feedback_mode), then use overriden `feedback(uchar* pr)` to output encoded.

# to add API(panel api/workspace api):
api-id, +1 on the last API in the api list > must read cycleui_impl.cpp!
implement panel api in PanelBuilder.Controls.cs + cycleui_impl.cpp ProcessUIStack
implement workspace api in Workspace.UIOps.cs/Workspace.Props.cs + cycleui_impl.cpp ActualWorkspaceQueueProcessor + interfaces.hpp + cycleui.h

# UI Control Implementation Pattern:
we are not using imgui for c# development though we do map imgui controls into panelbuilder.controls.cs. read panelbuilder.controls.cs!
1. Add method to PanelBuilder.Controls.cs with proper command ID
2. Find the highest existing case number in cycleui_impl.cpp UIFuns array
3. Add new lambda function case in UIFuns array with proper parameter reading
4. Use ImGui controls (DragFloat2, Button, etc.) for UI rendering
5. Handle state changes with stateChanged = true and appropriate Write functions
6. Ensure proper memory management for string/array parameters

# Workspace Property Implementation Pattern:
1. Create new class inheriting from WorkspaceProp in Workspace.Props.cs
2. Implement Serialize() method with unique command ID (find highest existing)
3. Add case handler in ActualWorkspaceQueueProcessor in cycleui_impl.cpp
4. Parse parameters using Read* macros (ReadString, ReadFloat, ReadInt, etc.)
5. Call appropriate interface function (AddStraightLine, AddVector, etc.)
6. Update data structures in cycleui.h and me_impl.h if needed

# Data Structure Extension Guidelines:
1. sometimes a me_object could reference another me_object. we have a smart pointer liker arch referece_t. use set_reference to set reference, also use remove_from_obj to dereference
- Always add new fields to the end of structures for compatibility
- Use boolean flags for feature enablement (e.g., isVector)
- Maintain backward compatibility with existing serialization
- Consider memory alignment and packing for performance

# Command ID Management:
- Workspace operations: Sequential numbering, check ActualWorkspaceQueueProcessor
- UI controls: Sequential numbering, check UIFuns array in ProcessUIStack
- Always verify highest existing ID before adding new ones
- Document new IDs in implementation files

# Memory Management:
- Use Read* macros for parameter extraction from byte stream
- ptr advancement is automatic with Read* functions
- Write* functions for sending data back to C# layer
- String parameters need proper null termination handling

# Threading Considerations:
- Painter operations are thread-safe through lock mechanism
- UI controls run on main UI thread
- Workspace operations may run on background threads

# C++ Macro Usage Rules:
- ReadInt, ReadBool, ReadFloat, ReadString are MACROS, not functions!
- MUST use assignment syntax: auto value = ReadInt; NOT ReadInt() function calls
- CANNOT use macros directly in function parameters or conditionals
- Always assign to temporary variable first, then use the variable
- Example: 
  CORRECT: auto color = ReadInt; colors[ImGuiCol_Text] = convertToVec4(color);
  WRONG: colors[ImGuiCol_Text] = convertToVec4(ReadInt);

# C# UI Controls:
- DO NOT use ImGui.NET in CycleGUI - use PanelBuilder.Controls.cs instead
- Available controls: Button, ColorEdit, DragFloat, DragVector2, CheckBox, Label, etc.
- Use pb.CollapsingHeaderStart() and pb.CollapsingHeaderEnd() for collapsible sections
- Use pb.SameLine() to put controls on the same line
- Always check PanelBuilder.Controls.cs for available control methods

# glsl:
only create glsl file, don't modify .h file, they're generated with gen_shader.bat
we have multiple viewport so we use shared_graphics to store commonly used things and working_graphics_state[vid] to store viewport related things.

# build:
just, don't build by yourself. human developer will do this.