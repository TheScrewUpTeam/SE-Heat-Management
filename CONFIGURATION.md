# Heat Management Mod Configuration Guide

This document explains each configuration variable available in the mod, helping you tailor the heat management system to your needs. Edit these values in the configuration file to adjust the mod's behavior.

## Configuration Variables

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `HEAT_COOLDOWN_COEFF` | float | 20.0 | **Heat cooldown coefficient.** Controls how quickly heat dissipates from blocks into the atmosphere. Increase for faster cooling, decrease for slower cooling. |
| `HEAT_RADIATION_COEFF` | float | 5.0 | **Heat radiation coefficient.** Controls how much heat is radiated into space from blocks. Higher values mean more heat is lost to space. |
| `DISCHARGE_HEAT_FRACTION` | float | 0.20 | **Discharge heat fraction.** The fraction of energy discharged from batteries that is converted into heat. Raise to make batteries generate more heat per discharge. |
| `THERMAL_CONDUCTIVITY` | float | 200.0 | **Thermal conductivity.** Governs how efficiently heat spreads between connected blocks. Higher values mean heat equalizes faster across the grid. |
| `HEATPIPE_CONDUCTIVITY` | float | 2000.0 | **Heat pipe conductivity.** Controls how efficiently heat pipes transfer heat between connected blocks. Higher values mean heat pipes are more effective at heat transfer. |
| `VENT_COOLING_RATE` | float | 1000.0 | **Vent cooling rate.** The amount of heat removed per tick by a vent. Increase to make vents more effective at cooling. |
| `THRUSTER_COOLING_RATE` | float | 25000.0 | **Thruster cooling rate.** The amount of heat removed per tick by thrusters. Increase to make thrusters more effective at cooling themselves. |
| `CRITICAL_TEMP` | float | 150.0 | **Critical temperature.** The temperature at which heat source blocks are considered overheated and may explode. |
| `SMOKE_TRESHOLD` | float (derived) | 135.0 | **Smoke threshold.** Calculated as 90% of `CRITICAL_TEMP`. When block temperature exceeds this, visual smoke effects may appear. |
| `WIND_COOLING_MULT` | float | 0.1 | **Wind cooling multiplier.** Modifies how much wind (planetary atmosphere) helps cool blocks. Increase for stronger wind cooling effects. |
| `LIMIT_TO_PLAYER_GRIDS` | bool | false | **Limit to player grids.** If true, only grids owned by players are affected by the heat system. Set to false to include all grids. |
| `EXHAUST_HEAT_REJECTION_RATE` | float | 5000 | Controls the rate at which exhaust blocks reject heat to the environment (joules/second). |


## How to Edit
- The configuration file is named `TSUT_HeatManagement_Config.xml` and is located in your world storage.
- Edit the values as needed, save the file, and reload your world or restart the server for changes to take effect.

If you have questions or need help, please refer to the README or contact the mod author. 