#pragma once

#include "CoreMinimal.h"
#include "Core/TerraforgeVoxelTypes.h"
#include "Core/TerraforgeVoxelMath.h"

struct FTerraforgeDensityVoxel
{
    float Density = -1.0f;
    uint16 MaterialId = 0;

    FORCEINLINE bool IsSolid() const
    {
        return Density > 0.0f;
    }
};

struct FTerraforgeDensityChunkData
{
    FIntVector ChunkCoord = FIntVector::ZeroValue;
    TArray<FTerraforgeDensityVoxel> Voxels;

    FTerraforgeDensityChunkData()
    {
        Voxels.SetNum(
            TerraforgeVoxel::ChunkSize *
            TerraforgeVoxel::ChunkSize *
            TerraforgeVoxel::ChunkSize
        );
    }

    explicit FTerraforgeDensityChunkData(const FIntVector& InChunkCoord)
        : ChunkCoord(InChunkCoord)
    {
        Voxels.SetNum(
            TerraforgeVoxel::ChunkSize *
            TerraforgeVoxel::ChunkSize *
            TerraforgeVoxel::ChunkSize
        );
    }

    FORCEINLINE int32 GetIndex(
        const int32 X,
        const int32 Y,
        const int32 Z
    ) const
    {
        return X + TerraforgeVoxel::ChunkSize *
            (Y + TerraforgeVoxel::ChunkSize * Z);
    }

    FORCEINLINE bool IsInBounds(
        const int32 X,
        const int32 Y,
        const int32 Z
    ) const
    {
        return
            X >= 0 && X < TerraforgeVoxel::ChunkSize &&
            Y >= 0 && Y < TerraforgeVoxel::ChunkSize &&
            Z >= 0 && Z < TerraforgeVoxel::ChunkSize;
    }

    FORCEINLINE FIntVector LocalToWorldVoxel(
        const int32 X,
        const int32 Y,
        const int32 Z
    ) const
    {
        return FIntVector(
            ChunkCoord.X * TerraforgeVoxel::ChunkSize + X,
            ChunkCoord.Y * TerraforgeVoxel::ChunkSize + Y,
            ChunkCoord.Z * TerraforgeVoxel::ChunkSize + Z
        );
    }

    FORCEINLINE const FTerraforgeDensityVoxel& GetVoxel(
        const int32 X,
        const int32 Y,
        const int32 Z
    ) const
    {
        return Voxels[GetIndex(X, Y, Z)];
    }

    FORCEINLINE FTerraforgeDensityVoxel& GetVoxelMutable(
        const int32 X,
        const int32 Y,
        const int32 Z
    )
    {
        return Voxels[GetIndex(X, Y, Z)];
    }

    FORCEINLINE void SetVoxel(
        const int32 X,
        const int32 Y,
        const int32 Z,
        const FTerraforgeDensityVoxel& Voxel
    )
    {
        Voxels[GetIndex(X, Y, Z)] = Voxel;
    }

    bool HasSurfaceCrossing() const
    {
        bool bHasSolid = false;
        bool bHasAir = false;

        for (const FTerraforgeDensityVoxel& Voxel : Voxels)
        {
            if (Voxel.Density > 0.0f)
            {
                bHasSolid = true;
            }
            else
            {
                bHasAir = true;
            }

            if (bHasSolid && bHasAir)
            {
                return true;
            }
        }

        return false;
    }

    bool HasAnySolidVoxel() const
    {
        for (const FTerraforgeDensityVoxel& Voxel : Voxels)
        {
            if (Voxel.Density > 0.0f)
            {
                return true;
            }
        }

        return false;
    }

    void FillFromDensityFunction(
        TFunctionRef<float(const FVector& WorldVoxelPosition)> DensityFunction,
        const uint16 SolidMaterialId
    )
    {
        for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
        {
            for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
            {
                for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
                {
                    const FIntVector WorldVoxel =
                        LocalToWorldVoxel(X, Y, Z);

                    const FVector WorldVoxelPosition(
                        static_cast<double>(WorldVoxel.X),
                        static_cast<double>(WorldVoxel.Y),
                        static_cast<double>(WorldVoxel.Z)
                    );

                    const float Density =
                        DensityFunction(WorldVoxelPosition);

                    FTerraforgeDensityVoxel Voxel;
                    Voxel.Density = Density;
                    Voxel.MaterialId =
                        Density > 0.0f ? SolidMaterialId : 0;

                    SetVoxel(X, Y, Z, Voxel);
                }
            }
        }
    }
};