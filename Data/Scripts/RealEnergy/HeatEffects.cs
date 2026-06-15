using System.Collections.Generic;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.HeatManagement
{
    public class HeatEffects : IHeatEffects
    {
        private const float SteamRestartThreshold = 0.75f;

        private readonly Dictionary<IMyCubeBlock, MyLight> _lights = new Dictionary<IMyCubeBlock, MyLight>();
        private readonly Dictionary<IMyCubeBlock, MyParticleEffect> blocksAtSmoke = new Dictionary<IMyCubeBlock, MyParticleEffect>();
        private readonly Dictionary<IMyCubeBlock, double> _steamStartTimes = new Dictionary<IMyCubeBlock, double>();

        public void UpdateLightsPosition()
        {
            var toRemove = new List<IMyCubeBlock>();
            foreach (var kvp in _lights)
            {
                var block = kvp.Key;
                if (block == null || block.MarkedForClose)
                {
                    kvp.Value.Clear();
                    toRemove.Add(block);
                    continue;
                }

                kvp.Value.Position = block.GetPosition() + block.WorldMatrix.Up * 0.2f;
                kvp.Value.UpdateLight();
            }
            foreach (var block in toRemove)
                _lights.Remove(block);

            // Restart steam effects every tick — large grids update heat infrequently,
            // so the particle can expire before the next GetHeatChange call.
            double now = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            foreach (var kvp in blocksAtSmoke)
            {
                var effect = kvp.Value;
                var duration = effect.DurationMax;
                if (duration <= 0) continue;
                double startTime;
                if (!_steamStartTimes.TryGetValue(kvp.Key, out startTime)) continue;
                if ((now - startTime) >= duration * SteamRestartThreshold)
                {
                    effect.Play();
                    _steamStartTimes[kvp.Key] = now;
                }
            }
        }

        // Call once per tick for each battery with current heat
        public void UpdateBlockHeatLight(IMyCubeBlock block, float heat)
        {
            if (!Config.Instance.HEAT_GLOW_INDICATION)
            {
                return;
            }
            MyLight light;
            if (!_lights.TryGetValue(block, out light))
            {
                // Create new light
                light = CreateLightForBlock(block);
                _lights[block] = light;
            }

            // Get ambient temperature for the block
            float ambient = HeatSession.Api.Utils.CalculateAmbientTemperature(block);
            float delta = heat - ambient;

            if (delta <= 0f)
            {
                light.Intensity = 0f;
                light.Color = Color.Black;
            }
            else
            {
                // Normalize delta for glow (e.g., 0°C = no glow, 100°C above ambient = max glow)
                float normalizedDelta = MathHelper.Clamp(delta / 100f, 0f, 1f);
                light.Intensity = normalizedDelta * 50f;  // adjust max brightness as needed
                light.Color = Color.Lerp(Color.Black, Color.OrangeRed, normalizedDelta);
            }

            // MyVisualScriptLogicProvider.SetHighlightLocal(block.Name, 3, 300, light.Color);

            // Update light position (in case block moves)
            light.Position = block.GetPosition() + block.WorldMatrix.Up * 0.2f;
            light.UpdateLight();
        }

        private MyLight CreateLightForBlock(IMyCubeBlock block)
        {
            var position = block.GetPosition() + block.WorldMatrix.Up * 0.2f;
            var color = Color.Transparent.ToVector4(); // start with invisible or black
            bool isLargeGrid = block.CubeGrid.GridSizeEnum == MyCubeSize.Large;
            float range = isLargeGrid ? 2.5f : 1.5f;
            string debugName = block.Name + "_heatLight";

            var light = new MyLight();
            light.Start(position, color, range, debugName);
            light.Intensity = 10f;
            return light;
        }

        // Clean up lights for batteries that no longer exist or have cooled down
        public void Cleanup(List<IMyCubeBlock> blocks)
        {
            var toRemove = new List<IMyCubeBlock>();
            foreach (var kvp in _lights)
            {
                if (!blocks.Contains(kvp.Key) || kvp.Key.MarkedForClose)
                {
                    kvp.Value.Clear();
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var b in toRemove)
                _lights.Remove(b);
        }

        public void InstantiateSteam(IMyCubeBlock block)
        {
            MyParticleEffect oldEffect;
            if (blocksAtSmoke.TryGetValue(block, out oldEffect))
            {
                var duration = oldEffect.DurationMax;
                double startTime;
                double now = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
                if (duration <= 0 || !_steamStartTimes.TryGetValue(block, out startTime) || (now - startTime) < duration * SteamRestartThreshold)
                    return;
                oldEffect.Play();
                _steamStartTimes[block] = now;
                return;
            }

            MyParticleEffect effect;
            var position = block.GetPosition();
            Vector3D forward = block.WorldMatrix.Forward;
            position += forward * (block.CubeGrid.GridSize * 0.5);
            MatrixD matrix = block.WorldMatrix;
            uint parentId = (uint)(block.EntityId & 0xFFFFFFFF);
            if (MyParticlesManager.TryCreateParticleEffect("OxyLeakLarge", ref matrix, ref position, parentId, out effect))
            {
                effect.UserScale = block.CubeGrid.GridSize / 5;
                effect.UserColorMultiplier = Color.White;
                effect.UserColorIntensityMultiplier = 5;
                effect.Autodelete = false;
                blocksAtSmoke[block] = effect;
                _steamStartTimes[block] = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            }
        }

        public void InstantiateSmoke(IMyCubeBlock block)
        {
            MyParticleEffect effect;
            var position = block.GetPosition();
            MatrixD matrix = block.WorldMatrix;
            uint parentId = (uint)(block.EntityId & 0xFFFFFFFF);
            if (MyParticlesManager.TryCreateParticleEffect("Smoke_Construction", ref matrix, ref position, parentId, out effect))
            {
                blocksAtSmoke[block] = effect;
            }
        }

        public void RemoveSmoke(IMyCubeBlock battery)
        {
            if (battery == null)
                return;
            MyParticleEffect effect;
            if (blocksAtSmoke.TryGetValue(battery, out effect))
            {
                effect.Stop();
                effect.Clear();
                blocksAtSmoke.Remove(battery);
                _steamStartTimes.Remove(battery);
            }
        }
    }
}