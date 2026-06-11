# HeatManagement: Global Reconfiguration Plan

Migration from factory-based block behavior system to `MyGameLogicComponent` per block type,
with a grid-level `GridHeatComponent` replacing the manually managed `GridHeatManager`.

---

## Architecture Target

| Layer | Old | New |
|---|---|---|
| Grid | `GridHeatManager` in `Session._gridHeatManagers` dict | `GridHeatComponent : MyGameLogicComponent` on `MyObjectBuilder_CubeGrid` |
| O2 | `GridO2Manager` (already a component) | Unchanged — `GridHeatComponent` gets it via `Entity.GameLogic.GetAs<GridO2Manager>()` (composite-safe) |
| Block | `IHeatBehaviorFactory` + `IHeatBehavior` class | `AHeatGameLogicComponent : MyGameLogicComponent, IHeatBehavior` |
| Session | Grid dict + entity callbacks + ownership watch | API broadcast + config sync only |
| Satellite mods | `IHeatBehaviorFactory` registration | Unchanged — factory path kept alive |

### Key principles
- `GridHeatComponent` always exists on every grid (SE manages lifecycle). Inactive on NPC grids.
- Block components call `Entity.CubeGrid.Components.Get<GridHeatComponent>()` — always succeeds, no timing race.
- NPC/player ownership transitions handled inside `GridHeatComponent` itself via grid event subscription.
- `IGridHeatManager` interface preserved — satellite mods holding a reference still compile and work.
- `IHeatBehaviorFactory` registration still works for external mods (pipes also use it internally for now).

---

## Phase 0 — Grid Foundation: GridHeatComponent ✓ DONE

**Goal:** Replace `GridHeatManager` + Session dict with a self-managing grid-level component.

### Files changed
- `NEW: Data/Scripts/RealEnergy/GridHeatComponent.cs`
- `MODIFY: Data/Scripts/RealEnergy/Session.cs`
- `MODIFY: Data/Scripts/RealEnergy/HeatApi.cs` (if IGridHeatManager needs adjustment)
- `DELETE: Data/Scripts/RealEnergy/GridHeatManager.cs` (logic moved, not lost)

### Steps

1. **Create `GridHeatComponent`**
   - `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]`
   - Implements `IGridHeatManager`
   - Ports ALL logic from `GridHeatManager`: behavior dict, `UpdateBlocksTemp`, `UpdateNeighborsTemp`,
     `UpdateVisuals`, pipe manager list, `TryGetHeatBehaviour`, `TryReactOnHeat`, `GetScaleBasedOnBlocksCount`, etc.
   - Add `bool _active` flag (default `true`)
   - `UpdateAfterSimulation()` / `UpdateBeforeSimulation()`: skip all work when `!_active`

2. **NPC grid / LIMIT_TO_PLAYER_GRIDS handling**
   - In `Init()`: subscribe to `((IMyCubeGrid)Entity).OnBlockOwnershipChanged` (or equivalent ownership event)
   - `EvaluateActive()`: mirrors `Session.IsPlayerGrid()` logic. Sets `_active` accordingly.
   - Called from `Init()` once (initial state) and from the ownership event handler.
   - When deactivating: stop updates, but keep the behavior dict intact (blocks re-register on reactivation).
   - When reactivating: trigger `CollectExistingBehaviors()` (scans grid for already-attached block components and factory behaviors).

3. **O2 integration**
   - Remove `AttachO2Manager` / `_o2manager` field pattern.
   - `ConsumeO2` / `HasEnoughO2`: lazy `Components.Get<GridO2Manager>()` call each time (or cache after first successful get).
   - Remove `HeatSession.AttachO2GridManager()` static method.

4. **Factory behavior support (backward compat)**
   - `GridHeatComponent` still has `CollectHeatBehaviors(factories)` called once on `UpdateOnceBeforeFrame()`.
   - `OnBlockAdded(IMySlimBlock)` still calls factory `OnBlockAdded` for externally registered factories.
   - This keeps satellite mod factory registrations working.

5. **Session changes**
   - Remove `_gridHeatManagers: Dictionary<IMyCubeGrid, GridHeatManager>`
   - Remove `OnEntityAdded` / `OnEntityRemoved` grid callbacks
   - Remove `AttachO2GridManager()`
   - Remove `OnGridOwnershipChanged()` and `_ownershipSubscribedGrids`
   - Remove `IsPlayerGrid()` (moved into `GridHeatComponent.EvaluateActive()`)
   - Keep: `ServerSideUpdates()` — but now iterates via `MyAPIGateway.Entities` or a static registry in `GridHeatComponent` instead of Session dict.
   - Alternatively: Session drives updates by iterating `GridHeatComponent._activeComponents` static list.

6. **Add to `GridHeatComponent`**
   - `public void RegisterBehavior(IMyCubeBlock block, IHeatBehavior behavior)` — adds to dict
   - `public void UnregisterBehavior(IMyCubeBlock block)` — removes from dict
   - `private void CollectExistingBehaviors()` — on (re)activation, scans for already-existing block components

### Test criteria
- [x] Game loads, grids heat normally
- [x] `LIMIT_TO_PLAYER_GRIDS = false` (default): all grids active
- [x] `LIMIT_TO_PLAYER_GRIDS = true`: NPC grids inactive (no heat updates, no CPU cost)
- [x] Capture NPC grid → heat activates within one tick
- [x] Lose grid to NPC → heat deactivates
- [x] Grid merge / split works (SE calls component lifecycle)
- [x] `TryGetHeatBehaviour()` returns correct behaviors
- [x] Pipe networks still work (factory path)

**Known deferred issue:** Event controller threshold sliders missing. `BeforeGameLogicInit` → `CreateTerminalControls` fires before `EventControllerBlockLogic.Init()` adds the components → `CreateTerminalInterfaceControls` never called. Affects both `BlockTemperatureChanged` and `GridMaxTemperatureChanged`. Fix deferred to post-refactor.

---

## Phase 1 — Block Component Foundation: AHeatGameLogicComponent ✓ DONE

**Goal:** Create base class. Nothing uses it yet.

### Files changed
- `NEW: Data/Scripts/RealEnergy/AHeatGameLogicComponent.cs`

### Steps

1. `public abstract class AHeatGameLogicComponent : MyGameLogicComponent, IHeatBehavior`
2. `protected GridHeatComponent _gridHeatComponent`
3. `public override void UpdateOnceBeforeFrame()`
   - `_gridHeatComponent = ((IMyCubeBlock)Entity).CubeGrid.Components.Get<GridHeatComponent>()`
   - `_gridHeatComponent.RegisterBehavior((IMyCubeBlock)Entity, this)`
4. `public override void Close()`
   - `_gridHeatComponent?.UnregisterBehavior((IMyCubeBlock)Entity)`
5. Abstract members: `GetHeatChange(float)`, `SpreadHeat(float)`, `Cleanup()`, `ReactOnNewHeat(float)`
6. `IMyCubeBlock Block => (IMyCubeBlock)Entity`

### Test criteria
- [x] Mod loads, no errors, zero behavior change

---

## Phase 2 — Battery ✓ DONE

**Goal:** Merge `BatteryGameLogic` + `BatteryHeatManager` → single component.

### Files changed
- `NEW: Data/Scripts/RealEnergy/Behaviors/BatteryHeatComponent.cs`
- `DELETE: Data/Scripts/RealEnergy/BatteryGameLogic.cs` (logic moved)
- `MODIFY: Data/Scripts/RealEnergy/Session.cs` (remove `BatteryHeatManagerFactory` registration)
- `MODIFY: Data/Scripts/RealEnergy/Behaviors/BatteryHeatManager.cs` (remove factory class, keep if helpers needed)

### Steps

1. `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock), false)]`
2. `public class BatteryHeatComponent : AHeatGameLogicComponent`
3. Port heat logic from `BatteryHeatManager`
4. Port terminal control registration from `BatteryGameLogic.UpdateOnceBeforeFrame()` → call `BatteryTerminalControls.Register()` (extract to static class like `VentTerminalControls`)
5. Remove `BatteryHeatManagerFactory` from `Session.LoadData()` factory registrations

### Test criteria
- [x] Battery charges/discharges with correct heat
- [x] Terminal controls (heat property) appear
- [x] Placed mid-game heats up correctly
- [x] Existing save: heat value preserved (reads from `block.Storage` GUID — no change)

---

## Phase 3 — Connector

### Files changed
- `NEW: Data/Scripts/RealEnergy/Behaviors/ConnectorHeatComponent.cs`
- `MODIFY: Session.cs` (remove factory)

### Steps
1. `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipConnector), false)]`
2. Port `ConnectorHeatManager` logic
3. Remove `ConnectorHeatManagerFactory`

### Test criteria
- [x] Connector heat on connection/disconnection
- [x] Placed mid-game works

---

## Phase 4 — Rotor + Piston ✓ DONE

Two separate commits.

### Rotor
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorRotor), false)]` + `MyObjectBuilder_MotorAdvancedRotor` shim
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorStator), false)]` + `MyObjectBuilder_MotorAdvancedStator` shim
- Rotor head is **passive** (`GetHeatChange` only returns `_lastHeatChange`) — stator drives both sides

### Piston
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_PistonBase), false)]` + `MyObjectBuilder_ExtendedPistonBase` shim
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_PistonTop), false)]`
- Piston top is **passive** — same reason as rotor head

### Test criteria (each)
- [x] Moving part heats under load
- [x] Placed mid-game works

### Known issue (deferred): Grid merge loses block component
Merging a new grid into an existing grid causes the merged grid's blocks (observed: battery) to lose their `MyGameLogicComponent`. Heat data disappears from detailed info. Root cause unknown — needs investigation. Fix deferred to post-Phase-7 cleanup or dedicated phase.

---

## Phase 5 — Thruster ✓ DONE

### Files changed
- `NEW: Data/Scripts/RealEnergy/Behaviors/ThrusterHeatComponent.cs`
- `MODIFY: Session.cs` (remove factory)

### Steps
1. `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]` — attaches to ALL thrusters
2. `UpdateOnceBeforeFrame()`: check `((IMyThrust)Entity).BlockDefinition.SubtypeName.Contains("AtmosphericThrust")`
   - If false: **do not call base** (do not register with GridHeatComponent), return
3. Port `ThrusterHeatManager` logic for matching blocks
4. Remove `ThrusterHeatManagerFactory`

### Test criteria
- [x] Atmospheric thrusters heat under thrust
- [x] Ion/hydrogen thrusters: no heat behavior registered
- [x] Placed mid-game works

---

## Phase 6 — Vent ✓ DONE

**Note:** `VentGameLogic : MyGameLogicComponent` already exists in `VentHeatManager.cs`. Expand it.

### Files changed
- `NEW: Data/Scripts/RealEnergy/Behaviors/VentHeatComponent.cs`
- `MODIFY: Data/Scripts/RealEnergy/Behaviors/VentHeatManager.cs` (gutted to empty stub)
- `MODIFY: Session.cs` (remove factory)

### Steps
1. Rename `VentGameLogic` → `VentHeatComponent`, extend `AHeatGameLogicComponent`
2. Absorb `VentHeatManager` logic into the component
3. Absorb `VentTerminalControls` into component as private static method
4. `_gridManager` reference → use `_gridHeatComponent` (obtained in base `UpdateOnceBeforeFrame()`)
5. Remove `VentHeatManagerFactory`
6. Drop dead O2 tank-walking methods (`FindConnectedO2Tanks`, `GetO2Available`, `ConsumeO2`) — O2 goes through `GridO2Manager`

### Important: O2Turbo setting
- `block.Storage[Config.O2TurboKey]` stores player-configured L/s value
- This is a **player setting**, not heat state — keep in `block.Storage`, do NOT migrate
- `GetO2Turbo()` / `SetO2Turbo()` static methods: unchanged

### Test criteria
- [x] Vent passive cooling works
- [x] Vent active cooling (working=true) works
- [x] Turbo mode: O2 consumed, steam effect shown — fixed in Phase 11
- [x] O2Turbo setting persists across save/load
- [x] Terminal controls (slider, actions) appear and function

---

## Phase 7 — Exhaust + HeatVent ✓ DONE

Two separate commits, same pattern as Phase 6.

### Exhaust
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_???), false)]` — verify ObjectBuilder type from current factory
- Port `ExhaustHeatManager`

### HeatVent
- Verify ObjectBuilder type
- Port `HeatVentManager`

### Test criteria (each)
- [x] Heat rejection at correct rates
- [x] Effects (steam/smoke) correct

---

## Phase 8 — Heat Pipe Network ✓ DONE

Pipe management internalized into `GridHeatComponent`. `HeatPipeManagerFactory` unregistered — now static utility only.

### What changed
- `GridHeatComponent`: owns `_pipeNetworks` list; `CollectPipeNetworks`, `OnPipeBlockAdded`, `OnPipeBlockRemoved` handle topology internally; `GetHeatPipeManagers()` O(1); `UpdatePipeNetworks` iterates `_pipeNetworks` directly
- `HeatPipeManager`: `RemoveBlock(block, gridManager, behaviorMap)` → `RemoveNode(block) : List<HeatPipeManager>` — no external state mutation
- `HeatPipeManagerFactory`: removed `IHeatBehaviorFactory` impl; static geometry helpers remain
- `IMultiBlockHeatBehavior`: removed `RemoveBlock` (internal concern)
- `Session`: removed `HeatPipeManagerFactory` registration

### Test criteria
- [x] Pipe networks form on grid load
- [x] New pipe extends network
- [x] New pipe merges networks
- [x] Pipe removal splits network
- [x] Pipe removal dissolves network
- [x] Deactivate clears `_pipeNetworks`; reactivation rebuilds
- [x] Heat spreads through network

---

## Phase 9 — Backward Compat Audit ✓ DONE

Verify satellite mod surface intact:

| API | Status |
|---|---|
| `IHeatBehaviorFactory` interface | Implementable, `RegisterHeatBehaviorFactory()` works |
| `IGridHeatManager` interface | `GridHeatComponent` implements it |
| `IHeatRegistry` | Unchanged |
| `IHeatUtils` | Unchanged |
| `IHeatBehavior` | Unchanged |
| `TryGetHeatBehaviour()` | Returns behavior for component-backed AND factory-backed blocks |
| `GetHeat()` / `SetHeat()` | Unchanged (`block.Storage` GUID path intact) |
| `HeatSession.Api` | Unchanged |

### Test criteria
- [x] Satellite mod with custom factory: loads, behaviors registered, `TryGetHeatBehaviour` works
- [x] Satellite mod calling `IHeatUtils` methods: correct results
- [x] No compile errors in satellite mod source against new API

---

## Phase 10 — Session Cleanup ✓ DONE

Remove dead code from `Session.cs`:

- Factory scanning calls for migrated block types (battery, connector, rotor, piston, thruster, vent, exhaust, heatvent)
- `OnBlockAdded` factory dispatch for those types (pipes still need it)
- Merge `BatteryGameLogic.cs` into `BatteryHeatComponent.cs`, delete `BatteryGameLogic.cs`
- Any remaining `GridHeatManager` references
- `CollectHeatBehaviors` calls for migrated types

`Session.cs` should end up as: API init, config sync, network handlers, update dispatch hook only.

### Test criteria
- [ ] Full regression: new game + loaded save
- [ ] All block types heat correctly
- [ ] `LIMIT_TO_PLAYER_GRIDS = true`: NPC grids unaffected
- [ ] Pipe networks intact
- [ ] Multiplayer: heat synced to clients

---

## Phase 11 — O2 Distribution System ✓ PARTIALLY DONE

**Context:** Turbo mode O2 consumption broken pre-refactor. Root cause found and fixed. Debug infrastructure added. Remaining bugs in VentHeatComponent still open.

### Completed this phase

#### Logging infrastructure
- `NEW: Data/Scripts/RealEnergy/HeatLog.cs` — `LS` subsystem constants + `HeatLog` static wrapper
- `Config.LOG_FLAGS` bitmask added (0=off, 1=Grid, 2=Behavior, 4=Pipe, 8=O2, 16=Sync, 32=Net)
- AND condition: flag must be set AND grid name must contain `"HeatDebug"` (grid=null skips name check)
- All `MyLog.Default.*` calls in `GridHeatComponent`, `HeatPipeManager`, `Session`, `HeatUtils`, `Networking`, `HmsApiV1.0` migrated to `HeatLog.Info/Warn` with subsystem tags
- Log prefix format: `[HeatManagement.{SubSystem}]`

#### Root cause fix — `GridO2Manager.NeedsUpdate` was commented out
- `GridO2Manager.Init()`: restored `NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME`
- `GridO2Manager.UpdateOnceBeforeFrame()`: wheel grid guard → `NeedsUpdate = EACH_FRAME`
- Without this, `UpdateAfterSimulation` never ran → `Initialize()` never called → `blockToManager` always empty → `ConsumeO2` always returned full amount unconsumed

#### `GameLogic.GetAs<T>()` composite fix
- `GridHeatComponent._o2Manager` lazy property: `Components.Get<GridO2Manager>()` → `GameLogic.GetAs<GridO2Manager>()`
- `Components.Get<T>()` cannot pierce `MyCompositeGameLogicComponent` wrapper — `GameLogic.GetAs<T>()` is the correct SE API when multiple `[MyEntityComponentDescriptor]` classes target the same entity type

#### O2 network debug visualization
- Battery "Show Heat Networks" checkbox now also draws O2 networks as blue lines (`.05f` thick, `Square` material, `Vector4(0,0,1,1)`)
- `ConveyorManager.ShowDebugGraph()`: draws lines from `_referenceBlock` to all other blocks in network
- `GridO2Manager.ShowDebugGraph()`: iterates `blockToManager.Values.Distinct()`
- `GridHeatComponent.UpdateVisuals()`: calls `O2Manager?.ShowDebugGraph()` every frame when `_showDebug` is true
- Note: `GizmoDrawLine` material ignores blue channel — `Square` material required for true blue

#### Network build stats logging
- `Initialize()` logs block type counts per grid
- `ProcessScheduledBlocks()` logs each block's network assignment with size
- Summary log: N networks, M blocks total

### Files changed
- `NEW: Data/Scripts/RealEnergy/HeatLog.cs`
- `MODIFY: Data/Scripts/RealEnergy/Config.cs` (LOG_FLAGS added)
- `MODIFY: Data/Scripts/RealEnergy/GridHeatComponent.cs` (logging, O2Manager lazy prop fix, UpdateVisuals O2 debug)
- `MODIFY: Data/Scripts/RealEnergy/O2Distribution/GridO2Manager.cs` (NeedsUpdate fix, ShowDebugGraph, logging)
- `MODIFY: Data/Scripts/RealEnergy/O2Distribution/ConveyorManager.cs` (ShowDebugGraph, BlockCount prop)
- `MODIFY: Data/Scripts/RealEnergy/Behaviors/HeatPipeManager.cs` (logging migration)
- `MODIFY: Data/Scripts/RealEnergy/Session.cs`, `HeatUtils.cs`, `Networking.cs`, `HmsApiV1.0.cs` (logging migration)
- `MODIFY: CONFIGURATION.md` (VENT_TURBO_COOLING_RATE, LOG_FLAGS added; version → 1.3.3)

### Remaining bugs in VentHeatComponent (not yet fixed)
- [x] Unit mismatch: `ConsumeO2(turboO2Usage, deltaTime, Block)` — should pass `turboO2Usage * deltaTime` (L/s × s = L)
- [x] Missing deltaTime in cooling calc: `turboO2Usage * Config.VENT_TURBO_COOLING_RATE / capacity` — needs `* deltaTime`
- [x] Steam effect: `InstantiateSteam` — effect now loops via session-time tracking + `Play()` with `SteamRestartThreshold = 0.75f`
- [x] Minor: `CalculateO2Production` called twice in `ConveyorManager.Consume()`

### Test criteria
- [x] O2 network builds on grid load (logs confirm)
- [x] O2 network debug visualization draws (blue lines, battery checkbox)
- [x] Turbo mode: O2 tanks drain at configured L/s rate
- [x] Steam effect appears when O2 consumed, persists (not restarted every tick)
- [x] Warning shown when O2 unavailable
- [x] No O2 drain when vent not working or turbo set to 0

---

## Rollback Table

| Phase | Rollback |
|---|---|
| 0 | Revert `Session.cs`, delete `GridHeatComponent.cs`, restore `GridHeatManager.cs` |
| 1 | Delete `AHeatGameLogicComponent.cs` |
| 2–7 | Re-add removed factory to `Session.LoadData()` |
| 8 | N/A (nothing changed) |
| 9 | N/A |
| 10 | `git revert` the cleanup commit |
| 11 | `git revert` the O2 fix commit |

---

## Open Questions / Gotchas

- **Rotor/Piston ObjectBuilder types:** verify exact `MyObjectBuilder_*` names — rotor has base + advanced variants.
- **Exhaust/HeatVent ObjectBuilder types:** verify before writing descriptors.
- **GridHeatComponent update loop:** currently Session drives `UpdateBlocksTemp` etc. After migration, `GridHeatComponent.UpdateAfterSimulation()` drives it — verify tick rate / interval logic matches.
- **Grid split/merge:** SE calls `Close()` on components of the split grid and creates new grid entity. Components re-attach via descriptors automatically. Verify behaviors re-register correctly.
- **Wheel grids:** `GridO2Manager` checks `HeatSession.IsWheelGrid()`. `GridHeatComponent` should do the same check in `EvaluateActive()`.
- **block.Storage heat value:** `HeatUtils.GetHeat/SetHeat` uses a GUID key. No migration planned — keep as-is for save compatibility.
