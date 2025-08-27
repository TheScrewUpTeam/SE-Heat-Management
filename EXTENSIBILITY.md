# Extending Heat Management System (HMS)

This guide explains how 3rd party modders can integrate with the Heat Management System (HMS) mod for Space Engineers. You only need to include the `HmsApiV1.0.cs` file in your own mod project to get started.

Current API Version: 1.0.1

---

## 1. Getting Started

- **Copy** `HmsApiV1.0.cs` from this mod into your own mod's source folder.
- **Reference** it in your project (no other files are required).

---

## 2. Initialization

In your session/component/script, initialize the API:

```csharp
private HmsApi _hmsApi;

public void InitHmsApi()
{
    _hmsApi = new HmsApi(OnHmsReady);
}

private void OnHmsReady()
{
    // Now you can use _hmsApi.Utils and _hmsApi.Effects and register Custom Heat Behavior(s)
}
```

---

## 3. Using the API

### Utility Functions

Access heat-related calculations and queries via `_hmsApi.Utils` (see `IHeatUtils` interface for all available methods):

```csharp
// Basic heat operations
float heat = _hmsApi.Utils.GetHeat(block);
_hmsApi.Utils.SetHeat(block, 100f);
// OR
float newHeat = _hmsApi.Utils.ApplyHeatChange(block, 50f);

// Environmental queries
float ambientTemp = _hmsApi.Utils.CalculateAmbientTemperature(block);
float airDensity = _hmsApi.Utils.GetAirDensity(block);
float windSpeed = _hmsApi.Utils.GetBlockWindSpeed(block);
bool isPressurized = _hmsApi.Utils.IsBlockInPressurizedRoom(block);

// Heat exchange calculations
float exchangeAmount = _hmsApi.Utils.GetExchangeUniversal(block, neighborBlock, deltaTime);
var networkData = _hmsApi.Utils.GetNetworkData(block);
```

### Effects

Trigger visual effects via `_hmsApi.Effects` (see `IHeatEffects` interface):

```csharp
_hmsApi.Effects.InstantiateSmoke(batteryBlock);
```

---

## 4. Heat Network Integration

The HMS now includes support for heat networks (like heat pipes). You can query network data and calculate heat exchange between blocks in the network:

```csharp
// Get heat network data for a block
var networkData = _hmsApi.Utils.GetNetworkData(block);
if (networkData != null)
{
    // Block is part of a heat network
    var networkSize = networkData.length;
}

// Calculate heat exchange considering both direct contact and network connections
float exchangeAmount = _hmsApi.Utils.GetExchangeUniversal(block, otherBlock, deltaTime);
```

## 5. Registering Custom Heat Behaviors

To add your own heat logic for specific blocks, use `RegisterHeatBehaviorFactory`:

```csharp
_hmsApi.RegisterHeatBehaviorFactory(
    grid => grid.GetFatBlocks<IMyCubeBlock>().Where(b => b.BlockDefinition.SubtypeName == "MyCustomBlock").ToList(),
    block => new MyCustomHeatBehavior(block)
);
```

- The **first function** selects which blocks on a grid should have your custom heat logic.
- The **second function** creates your custom `AHeatBehavior` for each selected block.

#### Example Custom Behavior

```csharp
public class MyCustomHeatBehavior : HmsApi.AHeatBehavior
{
    public MyCustomHeatBehavior(IMyCubeBlock block) : base(block) { }

    public override float GetHeatChange(float deltaTime)
    {
        // Your custom heat logic here
        return 0f;
    }

    public override void SpreadHeat(float deltaTime)
    {
        // Optional: implement heat spreading
    }

    public override void Cleanup()
    {
        // Optional: cleanup logic
    }

    public override void ReactOnNewHeat(float heat)
    {
        // Optional: respond to heat changes
    }
}
```

---

## 5. Best Practices

- Only register your factory once (e.g., in your session/component init).
- Use the provided interfaces for all heat-related queries and effects.
- See the XML comments in `HmsApiV1.0.cs` for detailed documentation on each method.

---

## 6. Troubleshooting

- Ensure your mod loads after HMS in the mod list.
- If `OnHmsReady` is not called, check that HMS is enabled and up to date.
- Use log output to debug integration issues.

---

## 7. License

See `LICENSE.txt` for usage terms.

---

For questions or advanced integration, see the comments in `HmsApiV1.0.cs` or contact the HMS maintainers via Discord https://discord.com/invite/Zy6GT4nGfC.
