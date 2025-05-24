This file works as a guide for AI to add functionality or revise CycleGUI library.
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
api-id, +1 on the last API in the api list.
implement panel api in PanelBuilder.Controls.cs + cycleui_impl.cpp ProcessUIStack
implement workspace api in Workspace.UIOps.cs/Workspace.Props.cs + cycleui_impl.cpp ActualWorkspaceQueueProcessor + interfaces.hpp + cycleui.h

# glsl:
only create glsl file, don't modify .h file, they're generated with gen_shader.bat
we have multiple viewport so we use shared_graphics to store commonly used things and working_graphics_state[vid] to store viewport related things.