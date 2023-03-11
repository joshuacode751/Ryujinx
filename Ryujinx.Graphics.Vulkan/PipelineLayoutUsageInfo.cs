namespace Ryujinx.Graphics.Vulkan
{
    struct PipelineLayoutUsageInfo
    {
        public readonly uint Stages;
        public readonly uint BindlessTexturesCount;
        public readonly uint BindlessSamplersCount;
        public readonly bool UsePushDescriptors;

        public PipelineLayoutUsageInfo(uint stages, uint bindlessTexturesCount, uint bindlessSamplersCount, bool usePushDescriptors)
        {
            Stages = stages;
            BindlessTexturesCount = bindlessTexturesCount;
            BindlessSamplersCount = bindlessSamplersCount;
            UsePushDescriptors = usePushDescriptors;
        }
    }
}