#pragma once

#include "CoreMinimal.h"
#include "GameFramework/SaveGame.h"
#include "TerraforgeDensitySaveGame.generated.h"

USTRUCT(BlueprintType)
struct TERRAFORGEVOXEL_API FTerraforgeSavedDensityVoxel
{
    GENERATED_BODY()

    UPROPERTY()
    int32 VoxelIndex = 0;

    UPROPERTY()
    float Density = -1.0f;

    UPROPERTY()
    int32 MaterialId = 0;
};

USTRUCT(BlueprintType)
struct TERRAFORGEVOXEL_API FTerraforgeSavedDensityChunk
{
    GENERATED_BODY()

    UPROPERTY()
    FIntVector ChunkCoord = FIntVector::ZeroValue;

    UPROPERTY()
    TArray<FTerraforgeSavedDensityVoxel> Voxels;
};

UCLASS()
class TERRAFORGEVOXEL_API UTerraforgeDensitySaveGame : public USaveGame
{
    GENERATED_BODY()

public:
    UPROPERTY()
    int32 SaveVersion = 2;

    UPROPERTY()
    int32 ChunkSize = 0;

    UPROPERTY()
    double VoxelSize = 0.0;

    UPROPERTY()
    int32 RadiusInChunks = 0;

    UPROPERTY()
    int32 MinChunkZ = 0;

    UPROPERTY()
    int32 MaxChunkZ = 0;

    UPROPERTY()
    int32 BlockMinChunkZ = -1;

    UPROPERTY()
    int32 BlockMaxChunkZ = 2;

    UPROPERTY()
    bool bAutoCalculateVerticalChunkRange = true;

    UPROPERTY()
    int32 ExtraVerticalChunksAbove = 1;

    UPROPERTY()
    int32 ExtraVerticalChunksBelow = 1;

    UPROPERTY()
    TArray<FTerraforgeSavedDensityChunk> SavedChunks;
};