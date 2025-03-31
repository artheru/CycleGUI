# UIOperation: 
Workspace.UIOps.cs: + class for operation, note the feedback type, parse.
cycleui_impl.cpp: +ActualWorkspaceQueueProcessor
cycleui.h: +cpp definition, data structure.
messyengine_impl.cpp: draw and operation, if should output value, set feedback type(enum feedback_mode), then use overriden `feedback(uchar* pr)` to output encoded.

## FollowMouse Operation

The FollowMouse operation allows the user to drag a line on the world workspace. When follower objects are specified, they will move by the same amount as the dragged line.

### C# Implementation (Workspace.UIOps.cs)
- `FollowMouse` class extends `WorkspaceUIOperation<FollowFeedback>`
- The operation has two main modes: XYPlane and ViewPlane
  - XYPlane: Mouse movements are projected onto the XY plane (z=0)
  - ViewPlane: Mouse movements are projected onto a plane passing through the initial click point, with normal parallel to the view direction
- Methods for setting follower objects and snapping objects
- Deserializes feedback that includes:
  - Start and end mouse positions
  - Snapping object information
  - Current positions of all follower objects

### C++ Implementation
- `follow_mouse_operation` in cycleui.h defines the base structure
- Implementation in messyengine_impl.cpp handles:
  - Tracking mouse movement with pointer_down(), pointer_move(), and pointer_up() methods
  - The draw() method renders a yellow line with an arrow from the start to current mouse position
  - Moving follower objects by the same translation vector
  - Projecting mouse movements according to the selected mode (XY plane or view plane)
  - Providing feedback to the C# layer via the feedback() method

### Key Features
- **Depth-based positioning:** Uses me_getTexFloats to get the depth at the clicked point
- **Two modes of operation:** XY plane or view plane projection
- **Visual feedback:** Draws a yellow line with an arrow to show the movement direction
- **Object following:** Specified objects move by the same amount as the drag

### Usage Example
```csharp
var followOp = new FollowMouse();
followOp.SetFollowerObjects(new string[] { "object1", "object2" });
followOp.SetStartSnappingObjects(new string[] { "snap1", "snap2" });
followOp.SetEndSnappingObjects(new string[] { "snap3", "snap4" });
followOp.follower_mode = FollowMouse.FollowMode.XYPlane;
followOp.feedback = (data, op) => {
    // Process feedback data
    // data.mouse_start, data.mouse_end contain the start and end positions
    // data.follower_objects_position contains the updated positions
};
followOp.Start();
```
