# PopMenuButton Widget Implementation

## Overview
A new UI control widget `PopMenuButton` has been added to CycleGUI that behaves like Windows Forms' `ToolStripSplitButton`. This widget displays a button with text and a dropdown arrow, supporting both unified and split-button modes.

## Features

### Two Operating Modes

#### 1. Unified Mode (`clickBtn = null`)
- Clicking anywhere on the button shows the dropdown menu
- Ideal for simple dropdown menus without a primary action

#### 2. Split Mode (`clickBtn != null`)
- Visual separator between text and arrow
- Clicking text part: triggers the primary action (`clickBtn`)
- Clicking arrow part: shows the dropdown menu
- Ideal for "quick action + options" scenarios (e.g., "Build" + build options)

## Implementation Details

### Control Type ID
- **Type ID**: 36
- **Location**: Replaces the previous ButtonDropdown implementation

### Files Modified

#### 1. `CycleGUI/PanelBuilder.Controls.cs`
Added the C# API method `PopMenuButton`:

```csharp
public void PopMenuButton(string buttonTxt, MenuItem[] menu, Action clickBtn = null)
```

**Parameters:**
- `buttonTxt`: Button text to display
- `menu`: Array of `MenuItem` objects defining the dropdown menu structure
- `clickBtn`: Optional action triggered when clicking the text part (null = unified mode)

**MenuItem Structure:**
```csharp
public MenuItem(string label, Action onClick = null, string shortcut = null, 
                bool selected = false, bool enabled = true, 
                List<MenuItem> subItems = null)
```

- `label`: Menu item text
  - Use `"-"` for horizontal separator line (will render as `ImGui::Separator()`)
  - Regular text for menu items
- `onClick`: Action to execute when item is clicked
- `shortcut`: Keyboard shortcut text (display only)
- `selected`: Shows checkmark next to item
- `enabled`: Whether item is clickable
- `subItems`: Nested submenu items

**Separator Rendering:**
Menu items with `label = "-"` are automatically detected and rendered as horizontal separator lines using `ImGui::Separator()` instead of menu items. This provides visual grouping in dropdown menus.

#### 2. `libVRender/cycleui_impl.cpp`
Added C++ implementation in the UIFuns array at index 36:
- Reads button text, split mode flag, and menu structure
- Renders unified or split button based on mode
- Shows popup menu using ImGui's BeginPopup/EndPopup
- Processes menu items recursively (supports nested submenus)
- Handles callbacks for both button clicks and menu selections

#### 3. `Examples/LearnCycleGUI/Demo/DemoControlsHandler.cs`
Added comprehensive demo with three examples:
- Simple dropdown menu (unified mode)
- Split button with quick action
- Complex menu with shortcuts and conditional states

## Usage Examples

### Example 1: Simple Dropdown Menu (Unified Mode)

```csharp
pb.PopMenuButton("File Operations", new[]
{
    new MenuItem("New File", () => Console.WriteLine("New")),
    new MenuItem("Open File", () => Console.WriteLine("Open")),
    new MenuItem("-"), // Separator
    new MenuItem("Recent Files", subItems: new List<MenuItem>
    {
        new MenuItem("Document1.txt", () => OpenFile("Document1.txt")),
        new MenuItem("Document2.txt", () => OpenFile("Document2.txt")),
        new MenuItem("Document3.txt", () => OpenFile("Document3.txt"))
    }),
    new MenuItem("-"),
    new MenuItem("Exit", () => Application.Exit())
});
```

**Behavior:** Clicking anywhere on the button shows the menu.

### Example 2: Split Button Mode

```csharp
pb.PopMenuButton("Build", new[]
{
    new MenuItem("Build Solution", () => BuildSolution()),
    new MenuItem("Rebuild Solution", () => RebuildSolution()),
    new MenuItem("Clean Solution", () => CleanSolution()),
    new MenuItem("-"),
    new MenuItem("Build Configuration", subItems: new List<MenuItem>
    {
        new MenuItem("Debug", () => SetConfiguration("Debug")),
        new MenuItem("Release", () => SetConfiguration("Release"))
    })
}, clickBtn: () => QuickBuild()); // Primary action
```

**Behavior:** 
- Clicking "Build" text → Calls `QuickBuild()`
- Clicking arrow → Shows build options menu

### Example 3: Menu with States and Shortcuts

```csharp
bool clipboardHasData = CheckClipboard();
bool textSelected = HasSelection();

pb.PopMenuButton("Edit", new[]
{
    new MenuItem("Cut", () => Cut(), "Ctrl+X", enabled: textSelected),
    new MenuItem("Copy", () => Copy(), "Ctrl+C", enabled: textSelected),
    new MenuItem("Paste", () => Paste(), "Ctrl+V", enabled: clipboardHasData),
    new MenuItem("-"),
    new MenuItem("Find", () => ShowFindDialog(), "Ctrl+F"),
    new MenuItem("Replace", () => ShowReplaceDialog(), "Ctrl+H"),
    new MenuItem("-"),
    new MenuItem("Select All", () => SelectAll(), "Ctrl+A", selected: textSelected)
}, clickBtn: () => QuickEdit());
```

**Features demonstrated:**
- Conditional enabled/disabled items
- Keyboard shortcuts (display only)
- Selected state (checkmark)
- Split button with primary action

## Visual Appearance

### Unified Mode
```
┌──────────────────────▼┐
│ Button Text          │
└──────────────────────┘
```
- Single unified button with standard styling
- Down arrow (▼) indicator on the right side
- Clicking anywhere shows the dropdown menu
- Standard hover/active states

### Split Mode
```
┌───────────────────┬──┐
│ Button Text       │▼ │
└───────────────────┴──┘
```
- Two distinct clickable areas with shared border
- Visual separator line between text and arrow parts
- Text part (left): Triggers primary action (`clickBtn`)
- Arrow part (right): Shows dropdown menu
- Independent hover states for each part
  - Hovering over text → text background highlights
  - Hovering over arrow → arrow background highlights
- Rounded corners on outer edges
- Professional ToolStripSplitButton appearance

## Technical Details

### C# Side (PanelBuilder.Controls.cs)

**Encoding Process:**
1. Generates unique control ID using hash
2. Encodes split mode flag
3. Recursively processes menu structure:
   - For each item: type (item/submenu), attributes, label
   - Attributes encode: has_action, has_shortcut, selected, enabled
   - Submenus are processed recursively
4. Returns callback for both button and menu actions

**Callback Handling:**
- Byte 0 = 0: Button clicked (calls `clickBtn`)
- Byte 0 = 1: Menu item selected (decodes path and invokes item's `onClick`)

### C++ Side (cycleui_impl.cpp)

**Rendering:**
- Reads button text and split flag
- Parses menu structure (stored for popup)
- Renders button(s):
  - **Unified**: Single button with arrow overlay
  - **Split**: Two buttons with separator line
- Opens popup on arrow click
- Processes menu recursively using lambda function

**Menu Processing:**
- Recursive lambda `process_menu` handles nested structure
- Tracks path through menu hierarchy
- Returns path to C# when item is clicked
- Supports separators (label = "-")

## Integration with Existing Widgets

PopMenuButton uses the same menu system as:
- **SetMainMenuBar** (type 24): Shares menu encoding/decoding logic
- **MenuItem class**: Reuses existing menu item structure
- **Panel callbacks**: Integrates with standard state management

## Demo Location

The demo is located in `Examples/LearnCycleGUI/Demo/DemoControlsHandler.cs` under the "PopMenuButton" collapsing header, demonstrating:

1. **Simple dropdown** - File operations with nested recent files
2. **Split button** - Build action with configuration menu
3. **Advanced menu** - Edit menu with shortcuts and conditional states
4. **Action log** - Shows all triggered actions for testing

## Advantages Over Previous Implementation

### ButtonDropdown (old) vs PopMenuButton (new):

| Feature | ButtonDropdown | PopMenuButton |
|---------|---------------|---------------|
| Menu structure | Simple string array | Rich MenuItem hierarchy |
| Nested submenus | ❌ No | ✅ Yes |
| Separators | ❌ No | ✅ Yes ("-" label) |
| Shortcuts display | ❌ No | ✅ Yes |
| Selected state | ❌ No | ✅ Yes (checkmarks) |
| Enabled/disabled | ❌ No | ✅ Per-item control |
| Split mode | ❌ No | ✅ Yes |
| Primary action | ❌ No | ✅ Yes (clickBtn) |
| Callback system | Simple boolean | Rich action system |

## Testing

Run the LearnCycleGUI example and navigate to the "PopMenuButton" section to see all features in action:

```bash
cd Examples/LearnCycleGUI
dotnet run
```

## Notes

- Menu items with `label = "-"` are rendered as separators
- Shortcuts are display-only (actual keyboard handling must be done separately)
- The selected state shows a checkmark but doesn't toggle automatically
- Nested submenus can be arbitrarily deep
- All menu callbacks are executed in the C# context
- The widget follows ImGui styling and adapts to theme changes
- No linter errors in C#, C++, or demo code

