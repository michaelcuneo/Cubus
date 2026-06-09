#pragma once

#include "CoreMinimal.h"
#include "Block/TerraforgeVoxelMesher.h"

class TERRAFORGEVOXEL_API FTerraforgeMarchingCubes
{
public:
    using FDensityLookup =
        TFunctionRef<float(const FIntVector& WorldVoxelCoord)>;

    using FMaterialLookup =
        TFunctionRef<uint16(const FIntVector& WorldVoxelCoord)>;

    static void GenerateMesh(
        const FIntVector& ChunkCoord,
        FDensityLookup DensityLookup,
        FMaterialLookup MaterialLookup,
        FTerraforgeVoxelMeshData& OutMesh,
        int32 CellStep = 1,
        bool bSmoothNormalsAcrossMaterialBoundaries = false
    );

private:
    static FVector InterpolateVertex(
        const FVector& P0,
        const FVector& P1,
        float D0,
        float D1
    );
};