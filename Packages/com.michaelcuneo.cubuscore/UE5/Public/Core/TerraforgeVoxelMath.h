#pragma once
#pragma once

#include "CoreMinimal.h"

namespace TerraforgeVoxel
{
    static constexpr int32 ChunkSize = 32;
    static constexpr int32 ChunkVolume = ChunkSize * ChunkSize * ChunkSize;

    // 100.0f means one voxel is one Unreal meter.
    static constexpr float VoxelSize = 100.0f;

    FORCEINLINE int32 FloorDiv(const int32 A, const int32 B)
    {
        check(B != 0);

        const int32 Q = A / B;
        const int32 R = A % B;

        if ((R != 0) && ((R < 0) != (B < 0)))
        {
            return Q - 1;
        }

        return Q;
    }

    FORCEINLINE int32 PositiveMod(const int32 A, const int32 B)
    {
        check(B > 0);

        const int32 R = A % B;
        return R < 0 ? R + B : R;
    }

    FORCEINLINE int32 FlattenIndex(const int32 X, const int32 Y, const int32 Z)
    {
        check(X >= 0 && X < ChunkSize);
        check(Y >= 0 && Y < ChunkSize);
        check(Z >= 0 && Z < ChunkSize);

        return X + ChunkSize * (Y + ChunkSize * Z);
    }

    FORCEINLINE FIntVector WorldVoxelToChunkCoord(const FIntVector& WorldVoxelCoord)
    {
        return FIntVector(
            FloorDiv(WorldVoxelCoord.X, ChunkSize),
            FloorDiv(WorldVoxelCoord.Y, ChunkSize),
            FloorDiv(WorldVoxelCoord.Z, ChunkSize)
        );
    }

    FORCEINLINE FIntVector WorldVoxelToLocalCoord(const FIntVector& WorldVoxelCoord)
    {
        return FIntVector(
            PositiveMod(WorldVoxelCoord.X, ChunkSize),
            PositiveMod(WorldVoxelCoord.Y, ChunkSize),
            PositiveMod(WorldVoxelCoord.Z, ChunkSize)
        );
    }

    FORCEINLINE FVector LocalVoxelToPosition(const int32 X, const int32 Y, const int32 Z)
    {
        return FVector(
            static_cast<double>(X) * VoxelSize,
            static_cast<double>(Y) * VoxelSize,
            static_cast<double>(Z) * VoxelSize
        );
    }
}