# Extending Heat Management System (HMS)

This guide explains how 3rd party modders can integrate with HMS. Copy `HmsApiV1.0.cs` into your mod project — no other files required.

Current API Version: 1.0.2

---

## 1. Getting Started

Copy `HmsApiV1.0.cs` from this mod into your own mod's source folder and reference it in your project.

---

## 2. Adding Custom Heat Logic (Component Approach — Recommended)

Extend `HmsApi.AHmsBlockComponent` and decorate it with `[MyEntityComponentDescriptor]`. Space Engineers auto-attaches the component to every matching block — no session-level wiring required.

```csharp
using TSUT.HeatManagement;
using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.ObjectBuilders;

[MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyCustomBlock), false)]
public class MyCustomBlockHeat : HmsApi.AHmsBlockComponent
{
    public override float GetHeatChange(float deltaTime)
    {
        // Return heat to add (positive) or remove (negative) per tick
        return 50f * deltaTime; // e.g. constant 50 °C/s heat source
    }

    public override void SpreadHeat(float deltaTime)
    {
        // Call built-in conduction to neighboring blocks
        SpreadHeatStandard(deltaTime);
    }

    protected override void OnHmsInit()
    {
        // Called once, only on player-owned grids, after HMS is ready.
        // Register terminal controls and subscribe SE events here.
        Block.AppendingCustomInfo += OnAppendCustomInfo;
    }

    public override void OnDetachedFromHeatSystem()
    {
        // HMS-specific cleanup only (e.g. cancel heat-related state).
        // Do NOT unsubscribe SE events here — use Close() for that.
    }

    public override void ReactOnNewHeat(float heat)
    {
        // React to temperature changes — trigger effects, damage, etc.
        if (heat > 800f)
            HmsApi.Instance?.Effects?.InstantiateSmoke(Block);
        else
            HmsApi.Instance?.Effects?.RemoveSmoke(Block);
    }

    public override void Close()
    {
        // Unsubscribe SE events here, not in OnDetachedFromHeatSystem.
        Block.AppendingCustomInfo -= OnAppendCustomInfo;
        base.Close();
    }

    private void OnAppendCustomInfo(IMyTerminalBlock b, StringBuilder info) { /* ... */ }
}
```

That's all. No `HmsApi` constructor call needed — the component initializes HMS automatically and handles late-joining blocks (placed after world load) correctly.

If you also need the API in a session component (e.g. for `Utils` queries outside of block logic), access the shared instance:

```csharp
var api = HmsApi.Instance; // non-null once HMS is loaded
if (api?.Utils != null)
{
    float heat = api.Utils.GetHeat(someBlock);
}
```

Or subscribe to the ready callback:

```csharp
new HmsApi(() =>
{
    // HmsApi.Instance.Utils is now available
});
```

---

## 3. Using the API

### Utility Functions

```csharp
// Basic heat operations
float heat = HmsApi.Instance.Utils.GetHeat(block);
HmsApi.Instance.Utils.SetHeat(block, 100f);
float newHeat = HmsApi.Instance.Utils.ApplyHeatChange(block, 50f);

// Environmental queries
float ambientTemp = HmsApi.Instance.Utils.CalculateAmbientTemperature(block);
float airDensity  = HmsApi.Instance.Utils.GetAirDensity(block);
float windSpeed   = HmsApi.Instance.Utils.GetBlockWindSpeed(block);
bool pressurized  = HmsApi.Instance.Utils.IsBlockInPressurizedRoom(block);

// Heat exchange calculations
float exchange   = HmsApi.Instance.Utils.GetExchangeUniversal(block, neighbor, deltaTime);
var networkData  = HmsApi.Instance.Utils.GetNetworkData(block);
```

### Effects

```csharp
HmsApi.Instance.Effects.InstantiateSmoke(block);
HmsApi.Instance.Effects.RemoveSmoke(block);
HmsApi.Instance.Effects.UpdateBlockHeatLight(block, heat);
```

---

## 4. Heat Network Integration

```csharp
var networkData = HmsApi.Instance.Utils.GetNetworkData(block);
if (networkData != null)
{
    int size  = networkData.Value.length;
    float avg = networkData.Value.averageTemperature;
}

// Exchange considering both adjacency and pipe networks
float exchange = HmsApi.Instance.Utils.GetExchangeUniversal(block, otherBlock, deltaTime);
```

---

## 5. O2 Distribution System Integration

```csharp
// Consume O2 — returns unmet demand
float unmet = HmsApi.Instance.Utils.ConsumeO2(amount: 10f, deltaTime: 0.016f, block: myBlock);
if (unmet > 0.001f)
    ApplyO2Penalty(unmet);

// Check without consuming
bool hasEnough = HmsApi.Instance.Utils.HasEnoughO2(amount: 5f, deltaTime: 0.016f, block: myBlock);
```

---

## 6. Terminal Property Integration

HMS automatically adds a read-only "Heat Temperature" terminal property to all blocks. Readable from programmable block scripts:

```csharp
float temp = battery.GetValue<float>("HeatTemperature");
```

---

## 7. Configuration

```csharp
HmsApi.HmsConfig cfg = HmsApi.Instance.Utils.GetHmsConfig();
float criticalTemp = cfg.CRITICAL_TEMP;
```

---

## 8. Advanced: Factory Registration

Use this approach when you cannot use `[MyEntityComponentDescriptor]` — for example, when block selection logic is dynamic or data-driven at runtime.

```csharp
new HmsApi(() =>
{
    HmsApi.Instance.RegisterHeatBehaviorFactory(
        grid => grid.GetFatBlocks<IMyCubeBlock>()
                    .Where(b => b.BlockDefinition.SubtypeName == "MyCustomBlock")
                    .ToList(),
        block => new MyCustomHeatBehavior(block)
    );
});

public class MyCustomHeatBehavior : HmsApi.AHeatBehavior
{
    public MyCustomHeatBehavior(IMyCubeBlock block) : base(block) { }

    public override float GetHeatChange(float deltaTime) => 0f;
    public override void SpreadHeat(float deltaTime) { }
    public override void Cleanup() { }
    public override void ReactOnNewHeat(float heat) { }
}
```

---

## 9. Troubleshooting

- Ensure your mod loads after HMS in the mod list.
- If `ReactOnNewHeat` / `GetHeatChange` are never called, verify the `[MyEntityComponentDescriptor]` type matches your block's `MyObjectBuilder_*` type exactly.
- `HmsApi.Instance` is null until HMS loads — guard with `?.` or check `Utils != null`.
- Use log output to debug integration issues.

---

## 10. License

See `LICENSE.txt` for usage terms.

For questions or advanced integration, see the XML comments in `HmsApiV1.0.cs` or contact the HMS maintainers via Discord https://discord.com/invite/Zy6GT4nGfC.
