# Heat Management Mod for Space Engineers 🚀🔥

Welcome to the **Heat Management** mod!
Are your batteries too chill? Thrusters running a fever? Vents just blowing hot air?
This mod brings a whole new layer of engineering challenge to your grids: **HEAT!**

---

## What Does This Mod Do?

- **Adds heat simulation** to batteries, thrusters, vents, and exhaust blocks.
- **Blocks generate, transfer, and lose heat** based on their activity and environment.
- **Heat networks** allow efficient heat transfer through specialized heat pipes.
- **Visual feedback** with configurable heat glow and smoke effects.
- **Overheating batteries?** Watch out—they might just go out with a bang! 💥
- **Vents and thrusters** can help cool things down, but you'll need to manage your grid's thermal balance.
- **Neighboring blocks** exchange heat, so your ship's design really matters!
- **Customizable!** All major parameters are configurable per save—tune it to your liking.

---

## Features

- **Realistic(ish) heat transfer** between blocks and through heat networks.
- **Visual feedback:** Configurable heat glow and smoke effects for hot blocks.
- **Heat pipe networks** for efficient thermal management across your grid.
- **Per-save config file:** Tweak the mod's behavior for each world.
- **Auto-updating config** system that preserves your settings during updates.
- **Enhanced battery management** with configurable discharge heat settings.
- **Exploding batteries** if you ignore the laws of thermodynamics.
- **Fun for engineers, masochists, and anyone who thinks vanilla is too easy.**

---

## How to Use

1. **Install the mod** like any other Space Engineers mod.
2. **Start a new world or load an existing one.**
3. After first load, a config file (`TSUT_HeatManagement_Config.xml`) will appear in your save's Storage folder.
4. **Edit the config** to tweak heat rates, thresholds, and more. (Save and reload the world to apply changes.)
5. **Build, overclock, and try not to melt your ship!**

---

## Configuration

See the [Configuration Guide](CONFIGURATION.md) for detailed instructions on all available config variables and how to use them.

- The config file is saved as `TSUT_HeatManagement_Config.xml` in your world's Storage folder.
- You can change heat coefficients, critical temperatures, and more.
- **Auto-updating config** system that preserves your settings while keeping the mod up to date.
- **New features:**
  - `HEAT_GLOW_INDICATION` (true/false) — Toggle visual heat glow effects.
  - `DISCHARGE_HEAT_CONFIGURABLE` (true/false) — Enable custom heat settings for battery discharge.
  - `HEATPIPE_CONDUCTIVITY` — Control the efficiency of heat pipe networks.
- The mod will always use the config from your current save, so each world can have its own heat rules.

---

# Extensibility

This mod is now fully extensible! 3rd party developers can integrate their own heat logic, effects, and custom behaviors using the provided API (v1.0.1).

Key features for modders:
- Easy integration with just one file (`HmsApiV1.0.cs`)
- Heat network support for advanced thermal management
- Comprehensive utility functions for heat calculations
- Visual effects API for custom heat indicators

For a complete integration guide and examples, see [EXTENSIBILITY.md](./EXTENSIBILITY.md).

If you are a modder and want to add your own heat behaviors or interact with the heat system, follow the instructions in that file.

---

## Known Issues

- Your ship may unexpectedly become a fireball. This is a feature, not a bug.

---

## Credits

- Mod by **TSUT** (The Screw-Up Team)
- Inspired by all the engineers who thought "What if batteries could explode?"

---

## License

MIT License.  
Go wild, but don't blame us if your base melts.

---

**Stay cool, engineers!** 😎 