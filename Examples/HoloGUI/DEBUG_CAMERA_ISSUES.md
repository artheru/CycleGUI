# Camera Data Stoppage Debugging Guide

## Overview
This guide helps debug the SH431UL camera data stoppage issue where threads exit with codes like `0x80070103` (ERROR_NO_MORE_ITEMS).

## Debug Features Added

### 1. Enhanced Logging
All debug output includes:
- Timestamp (HH:mm:ss.fff)
- Log level (DEBUG/WARN/ERROR)
- Thread ID
- Component ([CAM], [GRAB], [SYS])

### 2. Camera Monitoring
- **Connection Health**: Monitors if camera data flow stops
- **Automatic Reconnection**: Attempts to reconnect up to 5 times
- **Frame Counting**: Tracks total frames received
- **Buffer Validation**: Checks for invalid buffers

### 3. System Resource Monitoring
- **Memory Usage**: Working set, private, virtual memory
- **Thread Count**: Total thread count monitoring
- **Handle Count**: Windows handle monitoring
- **USB Device Status**: WMI queries for camera devices
- **CPU Usage**: Process CPU time tracking

### 4. DirectShow Monitoring
- **Filter Graph State**: Monitors DirectShow graph health
- **COM Object Management**: Better error handling
- **HR Code Logging**: DirectShow HRESULT error codes

## How to Debug

### Step 1: Enable Debug Output
1. Run your application in **Debug mode** in Visual Studio
2. Open **Debug > Windows > Output**
3. Set "Show output from:" to **Debug**
4. Look for log messages with timestamps

### Step 2: Monitor the Logs
Look for these key patterns:

#### Normal Operation:
```
[12:34:56.789] [DEBUG] [T123] Starting camera polling cycle
[12:34:56.790] [CAM-DEBUG] [T123] Creating SH431ULCamera for index 0
[12:34:56.895] [CAM-DEBUG] [T123] DirectShow graph started successfully
[12:34:57.123] [DEBUG] [T123] Eye FPS=30.00, Total frames: 1234
```

#### Warning Signs:
```
[12:34:56.789] [CAM-ERROR] [T123] Failed to render stream, HR: 0x80070103
[12:34:56.790] [WARN] [T123] Camera data timeout after 5 seconds
[12:34:56.791] [SYS-WARN] [T123] USB Camera device issue - Status: Error
```

### Step 3: Use Manual Debug Tools
The UI now includes buttons:
- **Force Reconnect**: Manually restart camera connection
- **Check DirectShow**: Check DirectShow filter status
- **Log System Info**: Dump current system state

### Step 4: Analyze Thread Exit Codes

#### Common Exit Codes:
- **0 (0x0)**: Normal exit
- **2147942659 (0x80070103)**: ERROR_NO_MORE_ITEMS - DirectShow enumeration ended
- **Other non-zero**: Various errors

#### ERROR_NO_MORE_ITEMS (0x80070103):
This typically means:
1. DirectShow filter graph disconnected
2. Camera device was unplugged or went to sleep
3. USB controller reset
4. Driver issue

### Step 5: Monitor System Resources
Watch for:
- **Memory leaks**: Increasing working set over time
- **Handle leaks**: Increasing handle count
- **Thread issues**: Threads stuck in Wait state
- **USB device problems**: Device status != "OK"

### Step 6: Check Windows Event Logs
1. Open **Event Viewer**
2. Check **Windows Logs > System**
3. Look for USB/device errors around the time of camera failure

## Troubleshooting Common Issues

### Issue: Camera stops after few minutes
**Look for**: 
- "Camera data timeout" messages
- USB device status changes
- Memory/handle leaks

**Solutions**:
- Check USB power management settings
- Update camera drivers
- Check USB cable/hub

### Issue: Immediate camera failure
**Look for**: 
- DirectShow HR error codes
- "Failed to create" messages
- COM initialization errors

**Solutions**:
- Run as Administrator
- Check DirectShow runtime
- Verify camera permissions

### Issue: Intermittent failures
**Look for**: 
- System resource issues
- Thread state warnings
- Timing-related errors

**Solutions**:
- Check system load
- Verify thread synchronization
- Monitor for competing applications

## Advanced Debugging

### Enable More Verbose Logging
In `MySH431ULSteoro.cs`, change:
```csharp
private const int DATA_TIMEOUT_SECONDS = 5;
```
to:
```csharp
private const int DATA_TIMEOUT_SECONDS = 2; // More sensitive
```

### Monitor DirectShow Graph with GraphEdit
1. Install Windows SDK
2. Run `graphedt.exe`
3. Connect to running process to see filter graph state

### Use Process Monitor
1. Download ProcMon from Microsoft
2. Filter by your process name
3. Look for file/registry access patterns before failures

## Expected Output Example

```
[12:00:00.000] [DEBUG] [T1] Initializing MySH431ULSteoro
[12:00:00.001] [SYS-DEBUG] [T1] Starting system resource monitoring
[12:00:00.002] [SYS-DEBUG] [T1] === SYSTEM INFORMATION ===
[12:00:00.003] [SYS-DEBUG] [T1] OS Version: Microsoft Windows NT 10.0.19045.0
[12:00:00.004] [SYS-DEBUG] [T1] CLR Version: 4.0.30319.42000
[12:00:00.010] [DEBUG] [T2] Starting camera polling cycle
[12:00:00.011] [DEBUG] [T2] Found 2 cameras: USB_Camera_Device_1234, SH431UL_Camera_5678
[12:00:00.012] [CAM-DEBUG] [T2] Creating SH431ULCamera for index 1
[12:00:00.050] [CAM-DEBUG] [T2] Creating DirectShow filter graph
[12:00:00.055] [CAM-DEBUG] [T2] DirectShow graph started successfully
[12:00:01.000] [DEBUG] [T2] Eye FPS=30.00, Total frames: 30
[12:00:30.000] [SYS-DEBUG] [T3] Memory - Working Set: 125MB, Private: 98MB, Virtual: 150MB
[12:00:30.001] [SYS-DEBUG] [T3] Thread count: 15
```

This comprehensive debugging should help you identify exactly where and why the camera data stops. 