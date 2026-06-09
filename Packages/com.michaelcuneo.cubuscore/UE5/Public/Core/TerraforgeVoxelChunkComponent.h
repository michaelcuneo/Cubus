#pragma once

#include "CoreMinimal.h"
#include "ProceduralMeshComponent.h"
#include "Block/TerraforgeVoxelMesher.h"
#include "TerraforgeVoxelChunkComponent.generated.h"

UCLASS(ClassGroup = (TerraforgeVoxel), meta = (BlueprintSpawnableComponent))
class TERRAFORGEVOXEL_API UTerraforgeVoxelChunkComponent : public UProceduralMeshComponent
{
    GENERATED_BODY()

public:
    explicit UTerraforgeVoxelChunkComponent(
        const FObjectInitializer& ObjectInitializer = FObjectInitializer::Get()
    );

    void InitializeChunk(
        const FIntVector& InChunkCoord,
        bool bInGenerateCollision,
        float InBaseVoxelSize
    );

    void ApplyMesh(const FTerraforgeVoxelMeshData& MeshData);

    FORCEINLINE FIntVector GetChunkCoord() const
    {
        return ChunkCoord;
    }

private:
    FIntVector ChunkCoord = FIntVector::ZeroValue;
    bool bGenerateCollision = true;
};