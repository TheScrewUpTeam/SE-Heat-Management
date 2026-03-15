# Extending Heat Management System (HMS)

This guide explains how 3rd party modders can integrate with the Heat Management System (HMS) mod for Space Engineers. You only need to include the `HmsApiV1.0.cs` file in your own mod project to get started.

Current API Version: 1.0.2

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

## 7. Terminal Property Integration

### Heat Temperature Display

HMS automatically adds a read-only "Heat Temperature" property to all terminal blocks in the control panel. This property displays the current heat value of any block using the HMS system.

**Features:**

- Displays current heat value in degrees Celsius
- Updates in real-time as block temperatures change
- Read-only (cannot be modified by players)
- Visible for all blocks with heat management enabled
- Available in both single-player and multiplayer

**Accessing the Property:**

The Heat Temperature property is accessible programmatically via in-game scripts using the `GetValue<T>()` method:

```csharp
public void Main(string argument, UpdateType updateSource)
{
    var battery = GridTerminalSystem.GetBlockWithName("Test Battery");
    float temp = battery.GetValue<float>("HeatTemperature");
    Echo($"Temperature: {temp}");
}
```

This allows you to read the current heat value from any terminal block in your programmable block scripts.

---

## 8. O2 Distribution System Integration

The HMS now includes an internal O2 Distribution System that can be used by 3rd party mods to manage oxygen consumption across conveyor-connected blocks. This system automatically accounts for O2 production and storage across the entire conveyor network.

### Consuming O2

```csharp
// Consume O2 from the distribution system
// Returns: The amount of O2 that could not be fulfilled by production/storage
float unmetDemand = _hmsApi.Utils.ConsumeO2(
    amount: 10f,              // Amount of O2 to consume (in L)
    deltaTime: 0.016f,        // Time interval that consumtion happened (in seconds)
    block: consumingBlock     // The block consuming the O2
);

if (unmetDemand > 0.001f)
{
    // Not enough O2 available - handle shortage
    ApplyO2ProductionPenalty(unmetDemand);
}
```

### Checking O2 Availability

```csharp
// Check if there's enough O2 without consuming anything
// Useful for previewing O2 requirements or condition checking
bool hasEnough = _hmsApi.Utils.HasEnoughO2(
    amount: 5f,               // Amount of O2 required (in L)
    deltaTime: 0.016f,        // Time interval that consumtion happened (in seconds)
    block: requestingBlock    // The block checking for O2
);

if (hasEnough)
{
    // Safe to start O2-dependent operations
    ActivateO2DependedSystems();
}
```

### How It Works

The O2 Distribution System automatically:
- Collects O2 from all connected producers (Oxygen Farms, Tanks) via conveyors
- Tracks available O2 storage capacity across the network
- Distributes O2 consumption fairly based on requests
- Returns any unmet demand for your mod to handle

**Requirements:**
- Blocks must be conveyor-connected to the O2 network
- O2 producers/generators must be available in the network
- Blocks must provide their EntityId when requesting O2

### Integration Example

```csharp
public class MyO2DependentBlock : MyLogicComponent
{
    private HmsApi _hmsApi;
    private float _o2ConsumptionRate = 5.0f; // L/s

    public override void UpdateAfterSimulation()
    {
        if (_hmsApi != null && _hmsApi.Utils != null)
        {
            // Try to consume required O2
            float unmetO2 = _hmsApi.Utils.ConsumeO2(
                _o2ConsumptionRate * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS,
                MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS,
                this.Block
            );

            if (unmetO2 > 0.001f)
            {
                // O2 shortage - reduce efficiency, damage block, emit warning, etc.
                ApplyO2ShortagePenalty(unmetO2);
            }
        }
    }
}
```

## 9. License

See `LICENSE.txt` for usage terms.

---

For questions or advanced integration, see the comments in `HmsApiV1.0.cs` or contact the HMS maintainers via Discord https://discord.com/invite/Zy6GT4nGfC.
