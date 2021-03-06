// Copyright (c) 2012-2021 Wojciech Figat. All rights reserved.

#pragma once

#include "ResourceOwnerDX12.h"
#include "../IncludeDirectXHeaders.h"

#if GRAPHICS_API_DIRECTX12

/// <summary>
/// Interface for objects that can be bound to the shader slots in DirectX 12.
/// </summary>
class IShaderResourceDX12
{
public:

    IShaderResourceDX12()
        : SubresourceIndex(D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES)
    {
    }

    IShaderResourceDX12(int32 subresourceIndex)
        : SubresourceIndex(subresourceIndex)
    {
    }

public:

    /// <summary>
    /// Affected subresource index or -1 if use whole resource.
    /// </summary>
    int32 SubresourceIndex; // Note: this solves only resource states tracking per single subresource, not subresources range, if need to here should be range of subresources (for texture arrays, volume textures and cubemaps)

public:

    /// <summary>
    /// Determines whether this resource is depth/stencil buffer.
    /// </summary>
    /// <returns>True if this resource is depth/stencil buffer, otherwise false.</returns>
    virtual bool IsDepthStencilResource() const = 0;

    /// <summary>
    /// Gets CPU handle to the shader resource view descriptor.
    /// </summary>
    /// <returns>SRV</returns>
    virtual D3D12_CPU_DESCRIPTOR_HANDLE SRV() const = 0;

    /// <summary>
    /// Gets CPU handle to the unordered access view descriptor.
    /// </summary>
    /// <returns>UAV</returns>
    virtual D3D12_CPU_DESCRIPTOR_HANDLE UAV() const = 0;

    /// <summary>
    /// Gets the resource owner.
    /// </summary>
    /// <returns>Owner object.</returns>
    virtual ResourceOwnerDX12* GetResourceOwner() const = 0;
};

#endif
