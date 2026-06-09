#pragma once

#include "CoreMinimal.h"
#include "TerraforgeTerrainGenerationProfile.generated.h"

USTRUCT(BlueprintType)
struct FTerraforgeTerrainGenerationProfile
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain Shape")
    bool bUseWorldEdgeFalloff = false;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain Shape", meta = (ClampMin = "1.0", ClampMax = "10000000.0"))
    float WorldEdgeRadius = 100000.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain Shape", meta = (ClampMin = "-100000.0", ClampMax = "100000.0"))
    float WorldEdgeTargetHeight = -55.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain Shape", meta = (ClampMin = "-10000.0", ClampMax = "10000.0"))
    float BaseHeight = 14.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain Shape", meta = (ClampMin = "0.0", ClampMax = "10000.0"))
    float HeightScale = 78.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Caves", meta = (ClampMin = "0.0", ClampMax = "500.0"))
    float CaveStrength = 0.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Caves", meta = (ClampMin = "0.0001", ClampMax = "1.0"))
    float CaveFrequency = 0.035f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Caves", meta = (ClampMin = "0.0", ClampMax = "10000.0"))
    float CaveStartDepth = 18.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Materials", meta = (ClampMin = "1", ClampMax = "65535"))
    int32 SurfaceMaterialId = 1;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Materials", meta = (ClampMin = "1", ClampMax = "65535"))
    int32 SubsurfaceMaterialId = 2;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Materials", meta = (ClampMin = "1", ClampMax = "65535"))
    int32 StoneMaterialId = 3;
};