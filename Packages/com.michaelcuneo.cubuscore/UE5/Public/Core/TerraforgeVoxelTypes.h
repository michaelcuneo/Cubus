#pragma once

#include "CoreMinimal.h"

struct FTerraforgeVoxel
{
    uint16 MaterialId = 0;

    FORCEINLINE bool IsAir() const
    {
        return MaterialId == 0;
    }

    FORCEINLINE bool IsSolid() const
    {
        return MaterialId != 0;
    }
};

enum class ETerraforgeVoxelFace : uint8
{
    XPositive,
    XNegative,
    YPositive,
    YNegative,
    ZPositive,
    ZNegative
};

UENUM(BlueprintType)
enum class ETerraforgeVoxelMeshingMode : uint8
{
    Naive UMETA(DisplayName = "Naive"),
    Greedy UMETA(DisplayName = "Greedy")
};

UENUM(BlueprintType)
enum class ETerraforgeVoxelTerrainMode : uint8
{
    Flat UMETA(DisplayName = "Flat"),
    SeamlessHeight UMETA(DisplayName = "Seamless Height"),
    Sphere UMETA(DisplayName = "Sphere"),
    IslandVolume UMETA(DisplayName = "Island Volume")
};

UENUM(BlueprintType)
enum class ETerraforgeVoxelSurfaceMode : uint8
{
    Blocky UMETA(DisplayName = "Blocky"),
    Smooth UMETA(DisplayName = "Smooth")
};