#pragma once

#include "CoreMinimal.h"
#include "TerraforgeTerrainSystem.generated.h"

UENUM(BlueprintType)
enum class ETerraforgeTerrainSystem : uint8
{
    Block UMETA(DisplayName = "Block Voxels"),
    SmoothDensity UMETA(DisplayName = "Smooth Density")
};