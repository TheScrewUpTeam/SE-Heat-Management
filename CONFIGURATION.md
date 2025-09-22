# Heat Management Mod Configuration Guide

This document explains each configuration variable available in the mod, helping you tailor the heat management system to your needs. Edit these values in the configuration file to adjust the mod's behavior.

## Configuration Variables

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `HEAT_COOLDOWN_COEFF` | float | 20.0 | **Heat cooldown coefficient.** Controls how quickly heat dissipates from blocks into the atmosphere. Increase for faster cooling, decrease for slower cooling. |
| `HEAT_RADIATION_COEFF` | float | 5.0 | **Heat radiation coefficient.** Controls how much heat is radiated into space from blocks. Higher values mean more heat is lost to space. |
| `DISCHARGE_HEAT_FRACTION` | float | 0.20 | **Discharge heat fraction.** The fraction of energy discharged from batteries that is converted into heat. Raise to make batteries generate more heat per discharge. |
| `THERMAL_CONDUCTIVITY` | float | 500.0 | **Thermal conductivity.** Governs how efficiently heat spreads between connected blocks. Higher values mean heat equalizes faster across the grid. |
| `HEATPIPE_CONDUCTIVITY` | float | 3000.0 | **Heat pipe conductivity.** Controls how efficiently heat pipes transfer heat between connected blocks. Higher values mean heat pipes are more effective at heat transfer. |
| `VENT_COOLING_RATE` | float | 5000.0 | **Vent cooling rate.** The amount of heat removed per tick by a vent. Increase to make vents more effective at cooling. |
| `THRUSTER_COOLING_RATE` | float | 35000.0 | **Thruster cooling rate.** The amount of heat removed per tick by thrusters. Increase to make thrusters more effective at cooling themselves. |
| `CRITICAL_TEMP` | float | 150.0 | **Critical temperature.** The temperature at which heat source blocks are considered overheated and may explode. |
| `SMOKE_TRESHOLD` | float (derived) | 135.0 | **Smoke threshold.** Calculated as 90% of `CRITICAL_TEMP`. When block temperature exceeds this, visual smoke effects may appear. |
| `WIND_COOLING_MULT` | float | 0.1 | **Wind cooling multiplier.** Modifies how much wind (planetary atmosphere) helps cool blocks. Increase for stronger wind cooling effects. |
| `LIMIT_TO_PLAYER_GRIDS` | bool | false | **Limit to player grids.** If true, only grids owned by players are affected by the heat system. Set to false to include all grids. |
| `EXHAUST_HEAT_REJECTION_RATE` | float | 5000.0 | Controls the rate at which exhaust blocks reject heat to the environment (joules/second). |
| `DISCHARGE_HEAT_CONFIGURABLE` | bool | false | **Discharge heat configuration.** If true, allows configuring discharge heat settings individually. |
| `HEAT_GLOW_INDICATION` | bool | true | **Heat glow effects.** If true, enables visual glow effects on blocks as they heat up. |
| `MAIN_UPDATE_INTERVAL_TICKS` | int | 30 | **Base update interval.** The number of ticks between heat system updates. One tick is 1/60th of a second. |
| `UPDATE_INTERVAL_SCALE_50` | int | 1 | **Scale for small grids.** Update interval multiplier for grids with up to 50 heat-blocks. x1 means update every 30 ticks (0.5 seconds). |
| `UPDATE_INTERVAL_SCALE_100` | int | 3 | **Scale for medium grids.** Update interval multiplier for grids with 51-100 heat-blocks. x3 means update every 90 ticks (1.5 seconds). |
| `UPDATE_INTERVAL_SCALE_400` | int | 4 | **Scale for large grids.** Update interval multiplier for grids with 101-400 heat-blocks. x4 means update every 120 ticks (2 seconds). |
| `UPDATE_INTERVAL_SCALE_1000` | int | 10 | **Scale for very large grids.** Update interval multiplier for grids with 401-1000 heat-blocks. x10 means update every 300 ticks (5 seconds). |
| `UPDATE_INTERVAL_SCALE_1500` | int | 15 | **Scale for huge grids.** Update interval multiplier for grids with 1001-1500 heat-blocks. x15 means update every 450 ticks (7.5 seconds). |
| `UPDATE_INTERVAL_SCALE_2000` | int | 30 | **Scale for massive grids.** Update interval multiplier for grids with 1501-2000 heat-blocks. x30 means update every 900 ticks (15 seconds). |
| `UPDATE_INTERVAL_SCALE_ENOURMOUS` | int | 120 | **Scale for enormous grids.** Update interval multiplier for grids with over 2000 heat-blocks. x120 means update every 3600 ticks (60 seconds). |


## Performance Configuration
The heat management system provides several configuration options to fine-tune performance based on your server's needs. These settings allow you to balance between simulation accuracy and server performance.

### Base Update Interval
The `MAIN_UPDATE_INTERVAL_TICKS` parameter affects the entire heat management system's update frequency. This is particularly important for servers with many players or grids:
- Lower values (e.g., 15-20) provide more accurate heat simulation but require more processing power
- Higher values (e.g., 45-60) reduce server load but make heat changes less granular
- The default value of 30 (0.5 seconds) provides a good balance for most scenarios

### Grid Scale Configuration
The update interval scale parameters provide fine-grained control over how frequently different sized grids update their heat calculations. This allows server administrators to:
- Maintain high update frequency for smaller grids while reducing it for larger ones
- Target specific grid sizes that may be causing performance issues
- Balance between simulation accuracy and server performance based on their specific needs

For example:
- Small bases (≤50 blocks) update every 0.5s by default
- Medium stations (51-400 blocks) update every 1.5-2s
- Large installations (401-2000 blocks) gradually scale up to 15s
- Enormous structures (>2000 blocks) update every 60s

For optimal heat simulation accuracy, lower values are better as they provide more frequent updates and more precise heat calculations. However, this comes at the cost of increased server load. Server administrators should monitor performance and adjust these values based on:
- Number of active players
- Total number of grids
- Server performance metrics
- Desired simulation accuracy

## Configuration Version
Current config version: 1.2.4
Auto-update: By default, the configuration will automatically update when new versions are released.

## How to Edit
- The configuration file is named `TSUT_HeatManagement_Config.xml` and is located in your world storage.
- Edit the values as needed, save the file, and reload your world or restart the server for changes to take effect.

If you have questions or need help, please refer to the README or contact the mod author. 