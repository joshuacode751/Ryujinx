using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Vulkan
{
    class PipelineLayoutCache
    {
        private readonly PipelineLayoutCacheEntry[] _plce;
        private readonly List<PipelineLayoutCacheEntry> _plceMinimal;
        private readonly List<(PipelineLayoutUsageInfo, PipelineLayoutCacheEntry)> _plceBindless;

        public PipelineLayoutCache()
        {
            _plce = new PipelineLayoutCacheEntry[1 << Constants.MaxShaderStages];
            _plceMinimal = new List<PipelineLayoutCacheEntry>();
            _plceBindless = new List<(PipelineLayoutUsageInfo, PipelineLayoutCacheEntry)>();
        }

        public PipelineLayoutCacheEntry Create(VulkanRenderer gd, Device device, ShaderSource[] shaders)
        {
            var plce = new PipelineLayoutCacheEntry(gd, device, shaders);
            _plceMinimal.Add(plce);
            return plce;
        }

        public PipelineLayoutCacheEntry GetOrCreate(VulkanRenderer gd, Device device, uint stages, bool usePushDescriptors)
        {
            if (_plce[stages] == null)
            {
                _plce[stages] = new PipelineLayoutCacheEntry(gd, device, new PipelineLayoutUsageInfo(stages, 0, 0, usePushDescriptors));
            }

            return _plce[stages];
        }

        public PipelineLayoutCacheEntry GetOrCreate(VulkanRenderer gd, Device device, PipelineLayoutUsageInfo usageInfo)
        {
            foreach ((var entryUsageInfo, var entry) in _plceBindless)
            {
                if (entryUsageInfo.Stages == usageInfo.Stages &&
                    entryUsageInfo.UsePushDescriptors == usageInfo.UsePushDescriptors &&
                    entryUsageInfo.BindlessTexturesCount >= usageInfo.BindlessTexturesCount &&
                    entryUsageInfo.BindlessSamplersCount >= usageInfo.BindlessSamplersCount)
                {
                    return entry;
                }
            }

            var plce = new PipelineLayoutCacheEntry(gd, device, usageInfo);

            _plceBindless.Add((usageInfo, plce));

            return plce;
        }

        protected virtual unsafe void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < _plce.Length; i++)
                {
                    _plce[i]?.Dispose();
                }

                foreach (var plce in _plceMinimal)
                {
                    plce.Dispose();
                }

                _plceMinimal.Clear();

                foreach ((_, var plce) in _plceBindless)
                {
                    plce.Dispose();
                }

                _plceBindless.Clear();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
