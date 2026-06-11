# HeatManagement: Global Reconfiguration Plan

Migration from factory-based block behavior system to `MyGameLogicComponent` per block type,
with a grid-level `GridHeatComponent` replacing the manually managed `GridHeatManager`.

---

## Architecture Target

| Layer | Old | New |
|---|---|---|
| Grid | `GridHeatManager` in `Session._gridHeatManagers` dict | `GridHeatComponent : MyGameLogicComponent` on `MyObjectBuilder_CubeGrid` |
| O2 | `GridO2Manager` (already a component) | Unchanged — `GridHeatComponent` gets it via `Components.Get<GridO2Manager>()` |
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

## Phase 2 — Battery

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
- [ ] Battery charges/discharges with correct heat
- [ ] Terminal controls (heat property) appear
- [ ] Placed mid-game heats up correctly
- [ ] Existing save: heat value preserved (reads from `block.Storage` GUID — no change)

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
- [ ] Connector heat on connection/disconnection
- [ ] Placed mid-game works

---

## Phase 4 — Rotor + Piston

Two separate commits.

### Rotor
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorRotor), false)]`
- (also `MyObjectBuilder_MotorAdvancedRotor` if covered — verify against current factory)

### Piston
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_PistonBase), false)]`

### Test criteria (each)
- [ ] Moving part heats under load
- [ ] Placed mid-game works

---

## Phase 5 — Thruster

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
- [ ] Atmospheric thrusters heat under thrust
- [ ] Ion/hydrogen thrusters: no heat behavior registered
- [ ] Placed mid-game works

---

## Phase 6 — Vent

**Note:** `VentGameLogic : MyGameLogicComponent` already exists in `VentHeatManager.cs`. Expand it.

### Files changed
- `MODIFY: Data/Scripts/RealEnergy/Behaviors/VentHeatManager.cs`
- `MODIFY: Session.cs` (remove factory)

### Steps
1. Rename `VentGameLogic` → `VentHeatComponent`, extend `AHeatGameLogicComponent`
2. Absorb `VentHeatManager` logic into the component
3. `_gridManager` reference → use `_gridHeatComponent` (obtained in base `UpdateOnceBeforeFrame()`)
4. `VentTerminalControls.Register()` still called from `UpdateOnceBeforeFrame()`
5. Remove `VentHeatManagerFactory`

### Important: O2Turbo setting
- `block.Storage[Config.O2TurboKey]` stores player-configured L/s value
- This is a **player setting**, not heat state — keep in `block.Storage`, do NOT migrate
- `GetO2Turbo()` / `SetO2Turbo()` static methods: unchanged

### Test criteria
- [ ] Vent passive cooling works
- [ ] Vent active cooling (working=true) works
- [ ] Turbo mode: O2 consumed, steam effect shown
- [ ] O2Turbo setting persists across save/load
- [ ] Terminal controls (slider, actions) appear and function

---

## Phase 7 — Exhaust + HeatVent

Two separate commits, same pattern as Phase 6.

### Exhaust
- `[MyEntityComponentDescriptor(typeof(MyObjectBuilder_???), false)]` — verify ObjectBuilder type from current factory
- Port `ExhaustHeatManager`

### HeatVent
- Verify ObjectBuilder type
- Port `HeatVentManager`

### Test criteria (each)
- [ ] Heat rejection at correct rates
- [ ] Effects (steam/smoke) correct

---

## Phase 8 — Heat Pipe Network (DEFERRED)

`HeatPipeManager` implements `IMultiBlockHeatBehavior` — one instance manages N connected pipe blocks.
This does not map cleanly to per-block components without a network coordinator.

**Decision: leave `HeatPipeManagerFactory` registered for now.**

Pipes continue working through the old factory path. `GridHeatComponent` still calls factory's
`CollectHeatBehaviors` and `OnBlockAdded` for registered factories.

Future work (separate plan):
- `HeatPipeComponent` per pipe block
- `HeatPipeNetworkCoordinator` (grid-level or standalone) handles connectivity graph
- OR: keep factory as permanent solution (pipes are fundamentally multi-block, factory fits)

---

## Phase 9 — Backward Compat Audit

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
- [ ] Satellite mod with custom factory: loads, behaviors registered, `TryGetHeatBehaviour` works
- [ ] Satellite mod calling `IHeatUtils` methods: correct results
- [ ] No compile errors in satellite mod source against new API

---

## Phase 10 — Session Cleanup

Remove dead code from `Session.cs`:

- Factory scanning calls for migrated block types (battery, connector, rotor, piston, thruster, vent, exhaust, heatvent)
- `OnBlockAdded` factory dispatch for those types (pipes still need it)
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

## Rollback Table

| Phase | Rollback |
|---|---|
| 0 | Revert `Session.cs`, delete `GridHeatComponent.cs`, restore `GridHeatManager.cs` |
| 1 | Delete `AHeatGameLogicComponent.cs` |
| 2–7 | Re-add removed factory to `Session.LoadData()` |
| 8 | N/A (nothing changed) |
| 9 | N/A |
| 10 | `git revert` the cleanup commit |

---

## Open Questions / Gotchas

- **Rotor/Piston ObjectBuilder types:** verify exact `MyObjectBuilder_*` names — rotor has base + advanced variants.
- **Exhaust/HeatVent ObjectBuilder types:** verify before writing descriptors.
- **GridHeatComponent update loop:** currently Session drives `UpdateBlocksTemp` etc. After migration, `GridHeatComponent.UpdateAfterSimulation()` drives it — verify tick rate / interval logic matches.
- **Grid split/merge:** SE calls `Close()` on components of the split grid and creates new grid entity. Components re-attach via descriptors automatically. Verify behaviors re-register correctly.
- **Wheel grids:** `GridO2Manager` checks `HeatSession.IsWheelGrid()`. `GridHeatComponent` should do the same check in `EvaluateActive()`.
- **block.Storage heat value:** `HeatUtils.GetHeat/SetHeat` uses a GUID key. No migration planned — keep as-is for save compatibility.
