using System.Collections.Generic;
using Sandbox.Game.Lights;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.HeatManagement
{
    public class HeatEffects : IHeatEffects
    {
        private readonly Dictionary<IMyCubeBlock, MyLight> _lights = new Dictionary<IMyCubeBlock, MyLight>();
        private readonly Dictionary<IMyCubeBlock, MyParticleEffect> blocksAtSmoke = new Dictionary<IMyCubeBlock, MyParticleEffect>();

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

                // Update light position based on block's current position
                kvp.Value.Position = block.GetPosition() + block.WorldMatrix.Up * 0.2f;
                kvp.Value.UpdateLight();
            }
            foreach (var block in toRemove)
                _lights.Remove(block);
        }

        // Call once per tick for each battery with current heat
        public void UpdateBlockHeatLight(IMyCubeBlock block, float heat)
        {
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
                float normalizedDelta = MathHelper.Clamp(delta / 50f, 0f, 1f);
                light.Intensity = normalizedDelta * 50f;  // adjust max brightness as needed
                light.Color = Color.Lerp(Color.Black, Color.OrangeRed, normalizedDelta);
            }

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


        public void InstantiateSmoke(IMyCubeBlock battery)
        {
            MyParticleEffect effect;
            var position = battery.GetPosition();
            MatrixD matrix = battery.WorldMatrix;
            uint parentId = (uint)(battery.EntityId & 0xFFFFFFFF);
            if (MyParticlesManager.TryCreateParticleEffect("Smoke_Construction", ref matrix, ref position, parentId, out effect))
            {
                blocksAtSmoke[battery] = effect;
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
                blocksAtSmoke.Remove(battery);
            }
        }
    }
}