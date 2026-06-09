#pragma once

#include "CoreMinimal.h"
#include "GameFramework/SaveGame.h"
#include "World/TerraforgeTerrainSystem.h"
#include "TerraforgeWorldSaveGame.generated.h"

USTRUCT(BlueprintType)
struct TERRAFORGEVOXEL_API FTerraforgeSavedDensityVoxelV2
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
struct TERRAFORGEVOXEL_API FTerraforgeSavedDensityChunkV2
{
    GENERATED_BODY()

    UPROPERTY()
    FIntVector ChunkCoord = FIntVector::ZeroValue;

    UPROPERTY()
    TArray<FTerraforgeSavedDensityVoxelV2> Voxels;
};

USTRUCT(BlueprintType)
struct TERRAFORGEVOXEL_API FTerraforgeSavedBlockVoxel
{
    GENERATED_BODY()

    UPROPERTY()
    int32 VoxelIndex = 0;

    UPROPERTY()
    int32 MaterialId = 0;
};

USTRUCT(BlueprintType)
struct TERRAFORGEVOXEL_API FTerraforgeSavedBlockChunk
{
    GENERATED_BODY()

    UPROPERTY()
    FIntVector ChunkCoord = FIntVector::ZeroValue;

    UPROPERTY()
    TArray<FTerraforgeSavedBlockVoxel> Voxels;
};

UCLASS()
class TERRAFORGEVOXEL_API UTerraforgeWorldSaveGame : public USaveGame
{
    GENERATED_BODY()

public:
    UPROPERTY()
    int32 SaveVersion = 1;

    UPROPERTY()
    int32 BlockMinChunkZ = -1;

    UPROPERTY()
    int32 BlockMaxChunkZ = 2;

    UPROPERTY()
    ETerraforgeTerrainSystem TerrainSystem =
        ETerraforgeTerrainSystem::SmoothDensity;

    UPROPERTY()
    int32 ChunkSize = 0;

    UPROPERTY()
    double VoxelSize = 0.0;

    UPROPERTY()
    int32 RadiusInChunks = 0;

    UPROPERTY()
    int32 DensityMinChunkZ = 0;

    UPROPERTY()
    int32 DensityMaxChunkZ = 0;

    UPROPERTY()
    TArray<FTerraforgeSavedDensityChunkV2> SavedDensityChunks;

    UPROPERTY()
    TArray<FTerraforgeSavedBlockChunk> SavedBlockChunks;
};