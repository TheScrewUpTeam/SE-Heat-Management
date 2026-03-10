# HeatManagement O2 Distribution Implementation Summary

## Status: In Progress

## Overview
Implemented conveyor-aware O2 distribution system for HeatManagement mod, allowing vents to only consume O2 from conveyor-connected tanks instead of all tanks on grid.

## Architecture

### Core Components Created

#### 1. O2 Network Interfaces (O2Distribution/)
- **IO2Producer** - Implemented by blocks that produce O2 (vents in depressurize mode)
- **IO2Consumer** - Implemented by blocks that consume O2 (vents in turbo mode)
- **IO2Storage** - For O2 storage tanks (future use)

#### 2. ConveyorNetworkBehavior
- Manages O2 distribution across conveyor-connected blocks
- Network topology via `MyVisualScriptLogicProvider.IsConveyorConnected()`
- Distributes O2: producers → storage → consumers each tick
- Implements `IMultiBlockHeatBehavior` for HeatManagement integration

#### 3. ConveyorNetworkBehaviorFactory
- Priority 25 (runs after vent managers at 20)
- `CollectHeatBehaviors()` - scans grid, creates networks
- `OnBlockAdded()` - adds blocks to existing or new networks

#### 4. VentHeatManager Enhancement
- Implements both `IO2Consumer` and `IO2Producer`
- Producer: extracts O2 from atmosphere (depressurize mode)
- Consumer: uses O2 for turbo cooling

## Critical Bug Discovered & Fixed

### Bug: Block vs Behavior Interface Check
**Original Code:**
```csharp
if (block is IO2Producer || block is IO2Consumer...)  // WRONG!
```

**Root Cause:** `IMyCubeBlock` (game block) can never implement interfaces defined in behaviors. Must check the behavior instead.

**Fixed Code:**
```csharp
IHeatBehavior behavior;
if (behaviorMap.TryGetValue(block, out behavior))
{
    if (behavior is IO2Producer || behavior is IO2Consumer...)  // CORRECT!
```

## H2Real Mod Integration

### Extension Pattern
- H2Real extends HeatManagement via message-based API
- Registers behaviors through `_api.RegisterHeatBehaviorFactory()`
- Uses `HmsApiV1.0.cs` as client stub

### Compatibility Path
**Option A:** Add O2 interfaces to `HmsApiV2.0.cs` (recommended)
- Extension mods can copy V2.0 and implement interfaces
- HeatManagement registers O2 behaviors automatically

**Option B:** Dynamic capability detection
- Detect O2 methods via `GetO2Consumption()` / `GetO2Production()`
- Wrap remote behaviors in dynamic adapters

## Backward Compatibility

### Current HMS + Old H2Real
- H2Real directly consumes from all tanks (old method)
- Continues to work unchanged
- No network awareness

### Future: HMS + New H2Real
- H2Real implements IO2Consumer in its handlers
- Removes direct tank consumption calls
- Let ConveyorNetworkBehavior handle distribution

## Testing Checklist

### Initial Load
- [ ] Check `[O2 DEBUG]` messages appear in chat
- [ ] Confirm `Found X O2 blocks` with X > 0
- [ ] Verify network created for conveyor-connected vents

### Runtime Operations
- [ ] Monitor `[O2Distribution] Production=` values
- [ ] Watch `[O2Distribution] Consumer:` messages
- [ ] Confirm O2 flows only through connected conveyors

### Network Changes
- [ ] Place/move vent, see OnBlockAdded logs
- [ ] Break conveyor connection, see split detection
- [ ] Reconnect, see network merge

## Code Review & Fixes Applied

### Critical Fixes in ConveyorNetworkBehavior.cs

#### Lines 19-57: CollectHeatBehaviors
- Changed to check `behavior` instead of `block` for O2 interfaces
- Added comprehensive debug logging at each stage
- Solved C# 6.0 compatibility (out variable declaration issue)

#### Lines 84-100: Update
- Added per-tick O2 distribution logging
- Tracks production → consumption flow
- Shows warnings when storage insufficient

#### Lines 16-45: TryAddToNetwork
- Added logging for network creation/merging
- Documents producer/consumer/storage role assignment

### VentHeatManager.cs
- Added IO2Consumer and IO2Producer interface implementations
- Hard-coded to backward compatibility layer for now
- Ready for future network integration

## Next Steps

### 1. Verify Initial Load Detection
- Test grid with depressurizing vent
- Confirm vent detected as O2-capable
- Verify levels displayed in chat

### 2. Implement Network-Based Consumption
- Modify VentHeatManager to request from network instead of direct tanks
- Add backward compatibility fallback
- Test conveyor connectivity restrictions

### 3. Test Extension Mods (H2Real)
- Add IO2Consumer to H2Real handlers
- Verify automatic integration with conveyor networks
- Test thrusters consuming O2 through conveyors

### 4. Add Production Tracking
- Add GUI indicator for O2 production/consumption
- Show network status in block info
- Display conveyor connectivity status

## Key Technical Decisions

### Why Check Behaviors, Not Blocks
Game objects (IMyCubeBlock) can never implement mod-defined interfaces. Only proxy/manager objects (behaviors) can. This is fundamental to SE modding architecture.

### Network Merging/Dividing
ConveyorNetworkBehavior doesn't actively merge networks. Instead:
1. CollectHeatBehaviors creates networks for all blocks
2. CheckNetworkIntegrity detects splits
3. GridHeatManager calls behavior.CheckNetworkIntegrity() each tick
4. If split detected, network updates

### Priority System
- VentHeatManagerFactory: Priority 20
- ConveyorNetworkBehaviorFactory: Priority 25
- Ensures vent behaviors exist before network scanning

## Open Questions

1. Confirm `TryGetHeatBehaviour` method signature and availability
2. Verify network gets correct IMyCubeBlock reference
3. Test with real conveyor setups
4. Measure performance impact of logging

## Debug Commands

To enable debug logging in-game:
- Look for `[O2 DEBUG]` and `[O2Distribution]` prefixes
- All O2-related messages start with these tags
- Check chat during grid load and each tick

## File Locations

- `Data/Scripts/RealEnergy/O2Distribution/` - All new O2 code
- `Data/Scripts/RealEnergy/ConvertersNetworkBehavior.cs` - Update method
- `Data/Scripts/RealEnergy/Behaviors/VentHeatManager.cs` - O2 interfaces implemented
- `Data/Scripts/RealEnergy/Session.cs` - Factory registration

## Commands to Test

```csharp
// Enable in-game debugging
data.Scripts.RealEnergy.O2Distribution.ConveyorNetworkBehavior.enableDebug = true;
```

## Critical Lines Modified

Line 33 (ConveyorNetworkBehavior.cs): `if (behavior is IO2...)` - Changed from `block is IO2...`
Line 45-47: Added debug logging
Line 19: Added network hash ID tracking

## Dependencies

- HeatManagement.Core (for IHeatBehavior, IGridHeatManager)
- SpaceEngineers API (for IMyCubeBlock, MyVisualScriptLogicProvider)
- VRage (for ShowMessage logging)

## Notes for Next Chat

When continuing this conversation, focus on:
1. Actual in-game debug messages received
2. Whether vent shows as "O2-capable BEHAVIOR"
3. Network creation/destruction messages
4. O2 production/consumption values displayed
