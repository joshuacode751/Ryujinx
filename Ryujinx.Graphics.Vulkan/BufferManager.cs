using Ryujinx.Graphics.GAL;
using Silk.NET.Vulkan;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VkFormat = Silk.NET.Vulkan.Format;

namespace Ryujinx.Graphics.Vulkan
{
    class BufferManager : IDisposable
    {
        private const MemoryPropertyFlags DefaultBufferMemoryFlags =
            MemoryPropertyFlags.MemoryPropertyHostVisibleBit |
            MemoryPropertyFlags.MemoryPropertyHostCoherentBit |
            MemoryPropertyFlags.MemoryPropertyHostCachedBit;

        private const MemoryPropertyFlags DeviceLocalBufferMemoryFlags =
            MemoryPropertyFlags.MemoryPropertyDeviceLocalBit;

        private const MemoryPropertyFlags DeviceLocalMappedBufferMemoryFlags =
            MemoryPropertyFlags.MemoryPropertyDeviceLocalBit |
            MemoryPropertyFlags.MemoryPropertyHostVisibleBit |
            MemoryPropertyFlags.MemoryPropertyHostCoherentBit;

        private const MemoryPropertyFlags FlushableDeviceLocalBufferMemoryFlags =
            MemoryPropertyFlags.MemoryPropertyHostVisibleBit |
            MemoryPropertyFlags.MemoryPropertyHostCoherentBit |
            MemoryPropertyFlags.MemoryPropertyDeviceLocalBit;

        private const BufferUsageFlags DefaultBufferUsageFlags =
            BufferUsageFlags.BufferUsageTransferSrcBit |
            BufferUsageFlags.BufferUsageTransferDstBit |
            BufferUsageFlags.BufferUsageUniformTexelBufferBit |
            BufferUsageFlags.BufferUsageStorageTexelBufferBit |
            BufferUsageFlags.BufferUsageUniformBufferBit |
            BufferUsageFlags.BufferUsageStorageBufferBit |
            BufferUsageFlags.BufferUsageIndexBufferBit |
            BufferUsageFlags.BufferUsageVertexBufferBit |
            BufferUsageFlags.BufferUsageTransformFeedbackBufferBitExt;

        private const int BufferSizeDeviceLocalThreshold = 512 * 1024; // 512kb

        private readonly PhysicalDevice _physicalDevice;
        private readonly Device _device;

        private readonly IdList<BufferHolder> _buffers;

        public int BufferCount { get; private set; }

        public StagingBuffer StagingBuffer { get; }

        public BufferManager(VulkanRenderer gd, PhysicalDevice physicalDevice, Device device)
        {
            _physicalDevice = physicalDevice;
            _device = device;
            _buffers = new IdList<BufferHolder>();
            StagingBuffer = new StagingBuffer(gd, this);
        }

        public BufferHandle CreateWithHandle(VulkanRenderer gd, int size, BufferAllocationType baseType = BufferAllocationType.HostMapped, BufferHandle storageHint = default)
        {
            var holder = Create(gd, size, baseType: baseType, storageHint: storageHint);
            if (holder == null)
            {
                return BufferHandle.Null;
            }

            BufferCount++;

            ulong handle64 = (uint)_buffers.Add(holder);

            return Unsafe.As<ulong, BufferHandle>(ref handle64);
        }

        public unsafe (Silk.NET.Vulkan.Buffer buffer, MemoryAllocation allocation, BufferAllocationType resultType) CreateBacking(
            VulkanRenderer gd,
            int size,
            BufferAllocationType type,
            bool forConditionalRendering = false,
            BufferAllocationType fallbackType = BufferAllocationType.Auto
            )
        {
            var usage = DefaultBufferUsageFlags;

            if (forConditionalRendering && gd.Capabilities.SupportsConditionalRendering)
            {
                usage |= BufferUsageFlags.BufferUsageConditionalRenderingBitExt;
            }
            else if (gd.Capabilities.SupportsIndirectParameters)
            {
                usage |= BufferUsageFlags.BufferUsageIndirectBufferBit;
            }

            var bufferCreateInfo = new BufferCreateInfo()
            {
                SType = StructureType.BufferCreateInfo,
                Size = (ulong)size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive
            };

            gd.Api.CreateBuffer(_device, in bufferCreateInfo, null, out var buffer).ThrowOnError();
            gd.Api.GetBufferMemoryRequirements(_device, buffer, out var requirements);

            MemoryAllocation allocation;

            do
            {
                var allocateFlags = type switch
                {
                    BufferAllocationType.HostMapped => DefaultBufferMemoryFlags,
                    BufferAllocationType.DeviceLocal => DeviceLocalBufferMemoryFlags,
                    BufferAllocationType.DeviceLocalMapped => DeviceLocalMappedBufferMemoryFlags,
                    _ => DefaultBufferMemoryFlags
                };

                // If an allocation with this memory type fails, fall back to the previous one.
                try
                {
                    allocation = gd.MemoryAllocator.AllocateDeviceMemory(_physicalDevice, requirements, allocateFlags);
                }
                catch (VulkanException)
                {
                    allocation = default;
                }
            }
            while (allocation.Memory.Handle == 0 && (--type != fallbackType));

            if (allocation.Memory.Handle == 0UL)
            {
                gd.Api.DestroyBuffer(_device, buffer, null);
                return default;
            }

            gd.Api.BindBufferMemory(_device, buffer, allocation.Memory, allocation.Offset);

            return (buffer, allocation, type);
        }

        public unsafe BufferHolder Create(VulkanRenderer gd, int size, bool forConditionalRendering = false, BufferAllocationType baseType = BufferAllocationType.HostMapped, BufferHandle storageHint = default)
        {
            BufferAllocationType type = baseType;

            if (baseType == BufferAllocationType.Auto)
            {
                if (gd.IsSharedMemory)
                {
                    baseType = BufferAllocationType.HostMapped;
                    type = baseType;
                }
                else
                {
                    type = size >= BufferSizeDeviceLocalThreshold ? BufferAllocationType.DeviceLocal : BufferAllocationType.HostMapped;
                }

                if (storageHint != BufferHandle.Null)
                {
                    if (TryGetBuffer(storageHint, out var holder))
                    {
                        type = holder.DesiredType;
                    }
                }
            }

            (Silk.NET.Vulkan.Buffer buffer, MemoryAllocation allocation, BufferAllocationType resultType) =
                CreateBacking(gd, size, type, forConditionalRendering);

            if (buffer.Handle != 0)
            {
                return new BufferHolder(gd, _device, buffer, allocation, size, baseType, resultType);
            }

            return null;
        }

        public Auto<DisposableBufferView> CreateView(BufferHandle handle, VkFormat format, int offset, int size, Action invalidateView)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.CreateView(format, offset, size, invalidateView);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBuffer(CommandBuffer commandBuffer, BufferHandle handle, bool isWrite, bool isSSBO = false)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetBuffer(commandBuffer, isWrite, isSSBO);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBuffer(CommandBuffer commandBuffer, BufferHandle handle, int offset, int size, bool isWrite)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetBuffer(commandBuffer, offset, size, isWrite);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBufferI8ToI16(CommandBufferScoped cbs, BufferHandle handle, int offset, int size)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetBufferI8ToI16(cbs, offset, size);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetAlignedVertexBuffer(CommandBufferScoped cbs, BufferHandle handle, int offset, int size, int stride, int alignment)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetAlignedVertexBuffer(cbs, offset, size, stride, alignment);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBufferTopologyConversion(CommandBufferScoped cbs, BufferHandle handle, int offset, int size, IndexBufferPattern pattern, int indexSize)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetBufferTopologyConversion(cbs, offset, size, pattern, indexSize);
            }

            return null;
        }

        public Auto<DisposableBuffer> GetBuffer(CommandBuffer commandBuffer, BufferHandle handle, bool isWrite, out int size)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                size = holder.Size;
                return holder.GetBuffer(commandBuffer, isWrite);
            }

            size = 0;
            return null;
        }

        public PinnedSpan<byte> GetData(BufferHandle handle, int offset, int size)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                return holder.GetData(offset, size);
            }

            return new PinnedSpan<byte>();
        }

        public void SetData<T>(BufferHandle handle, int offset, ReadOnlySpan<T> data) where T : unmanaged
        {
            SetData(handle, offset, MemoryMarshal.Cast<T, byte>(data), null, null);
        }

        public void SetData(BufferHandle handle, int offset, ReadOnlySpan<byte> data, CommandBufferScoped? cbs, Action endRenderPass)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                holder.SetData(offset, data, cbs, endRenderPass);
            }
        }

        public void Delete(BufferHandle handle)
        {
            if (TryGetBuffer(handle, out var holder))
            {
                holder.Dispose();
                _buffers.Remove((int)Unsafe.As<BufferHandle, ulong>(ref handle));
            }
        }

        private bool TryGetBuffer(BufferHandle handle, out BufferHolder holder)
        {
            return _buffers.TryGetValue((int)Unsafe.As<BufferHandle, ulong>(ref handle), out holder);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (BufferHolder buffer in _buffers)
                {
                    buffer.Dispose();
                }

                _buffers.Clear();
                StagingBuffer.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
