#pragma once

#include "CoreMinimal.h"
#include "Engine/DataAsset.h"
#include "Terrain/TerraforgeTerrainGenerationProfile.h"
#include "TerraforgeBiomeDefinition.generated.h"

UCLASS(BlueprintType, Blueprintable)
class TERRAFORGEVOXEL_API UTerraforgeBiomeDefinition : public UDataAsset
{
    GENERATED_BODY()

public:
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Terraforge|Biome", meta = (ClampMin = "0", ClampMax = "255"))
    int32 BiomeId = 1;

    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Terraforge|Biome")
    FName BiomeName = TEXT("Biome");

    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Terraforge|Biome")
    FTerraforgeTerrainGenerationProfile GenerationProfile;
};