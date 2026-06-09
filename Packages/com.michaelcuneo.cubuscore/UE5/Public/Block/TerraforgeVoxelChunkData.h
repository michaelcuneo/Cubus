#pragma once

#include "CoreMinimal.h"
#include "Core/TerraforgeVoxelMath.h"
#include "Core/TerraforgeVoxelTypes.h"

struct FTerraforgeVoxelChunkData
{
    FIntVector ChunkCoord = FIntVector::ZeroValue;
    TArray<FTerraforgeVoxel> Voxels;

    FTerraforgeVoxelChunkData()
    {
        Voxels.SetNumZeroed(TerraforgeVoxel::ChunkVolume);
    }

    explicit FTerraforgeVoxelChunkData(const FIntVector& InChunkCoord)
        : ChunkCoord(InChunkCoord)
    {
        Voxels.SetNumZeroed(TerraforgeVoxel::ChunkVolume);
    }

    FORCEINLINE FTerraforgeVoxel GetVoxel(const int32 X, const int32 Y, const int32 Z) const
    {
        return Voxels[TerraforgeVoxel::FlattenIndex(X, Y, Z)];
    }

    FORCEINLINE void SetVoxel(const int32 X, const int32 Y, const int32 Z, const FTerraforgeVoxel& Voxel)
    {
        Voxels[TerraforgeVoxel::FlattenIndex(X, Y, Z)] = Voxel;
    }

    FORCEINLINE bool IsSolid(const int32 X, const int32 Y, const int32 Z) const
    {
        return GetVoxel(X, Y, Z).IsSolid();
    }

    FORCEINLINE FIntVector LocalToWorldVoxel(const int32 X, const int32 Y, const int32 Z) const
    {
        return FIntVector(
            ChunkCoord.X * TerraforgeVoxel::ChunkSize + X,
            ChunkCoord.Y * TerraforgeVoxel::ChunkSize + Y,
            ChunkCoord.Z * TerraforgeVoxel::ChunkSize + Z
        );
    }

    bool HasAnySolidVoxel() const
    {
        for (const FTerraforgeVoxel& Voxel : Voxels)
        {
            if (Voxel.IsSolid())
            {
                return true;
            }
        }

        return false;
    }

    void FillTestTerrain()
    {
        for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
        {
            for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
            {
                for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
                {
                    FTerraforgeVoxel Voxel;

                    if (Z < 16)
                    {
                        Voxel.MaterialId = 1;
                    }

                    SetVoxel(X, Y, Z, Voxel);
                }
            }
        }
    }

    void FillTestHill()
    {
        const FVector2D Center(
            TerraforgeVoxel::ChunkSize * 0.5,
            TerraforgeVoxel::ChunkSize * 0.5
        );

        for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
        {
            for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
            {
                for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
                {
                    const FVector2D P(X, Y);
                    const double Distance = FVector2D::Distance(P, Center);

                    const double Height = 20.0 - Distance * 0.45;

                    FTerraforgeVoxel Voxel;

                    if (Z < Height)
                    {
                        Voxel.MaterialId = 1;
                    }

                    SetVoxel(X, Y, Z, Voxel);
                }
            }
        }
    }

    void FillSphereTerrain()
    {
        // Center of the test sphere in world voxel coordinates.
        // Keeping it around origin makes it span negative and positive chunks.
        const FVector SphereCenter(0.0, 0.0, 0.0);

        // Radius in voxel units, not Unreal units.
        // With VoxelSize = 50, radius 85 voxels = 42.5 meters.
        const double BaseRadius = 85.0;

        // Surface noise amount in voxels.
        const double SurfaceNoiseStrength = 8.0;

        for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
        {
            for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
            {
                for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
                {
                    const FIntVector WorldVoxel = LocalToWorldVoxel(X, Y, Z);

                    const FVector P(
                        static_cast<double>(WorldVoxel.X),
                        static_cast<double>(WorldVoxel.Y),
                        static_cast<double>(WorldVoxel.Z)
                    );

                    const FVector Direction = P - SphereCenter;
                    const double Distance = Direction.Length();

                    // Cheap deterministic pseudo-noise using sin/cos.
                    // This gives the sphere a more natural lumpy surface.
                    const double N =
                        FMath::Sin(static_cast<double>(WorldVoxel.X) * 0.071) * 0.45 +
                        FMath::Cos(static_cast<double>(WorldVoxel.Y) * 0.063) * 0.35 +
                        FMath::Sin(static_cast<double>(WorldVoxel.Z) * 0.057) * 0.30 +
                        FMath::Sin(static_cast<double>(WorldVoxel.X + WorldVoxel.Y + WorldVoxel.Z) * 0.031) * 0.50;

                    const double LocalRadius = BaseRadius + N * SurfaceNoiseStrength;

                    FTerraforgeVoxel Voxel;

                    if (Distance <= LocalRadius)
                    {
                        Voxel.MaterialId = 1;
                    }

                    SetVoxel(X, Y, Z, Voxel);
                }
            }
        }
    }

    void FillSeamlessTestTerrain()
    {
        for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
        {
            for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
            {
                for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
                {
                    const FIntVector WorldVoxel = LocalToWorldVoxel(X, Y, Z);

                    const double Height =
                        14.0
                        + FMath::Sin(static_cast<double>(WorldVoxel.X) * 0.10) * 5.0
                        + FMath::Cos(static_cast<double>(WorldVoxel.Y) * 0.10) * 5.0
                        + FMath::Sin(static_cast<double>(WorldVoxel.X + WorldVoxel.Y) * 0.045) * 4.0;

                    FTerraforgeVoxel Voxel;

                    if (WorldVoxel.Z < Height)
                    {
                        Voxel.MaterialId = 1;
                    }

                    SetVoxel(X, Y, Z, Voxel);
                }
            }
        }
    }

    void FillIslandVolumeTerrain()
    {
        /*
         * IslandVolume is a better prototype terrain mode than Sphere.
         *
         * It creates:
         * - rolling terrain on top
         * - thick underground volume
         * - rough cliffs / edge falloff
         * - simple cave pockets
         *
         * All calculations use world voxel coordinates, so chunks line up seamlessly.
         */

         // Approximate horizontal island radius in voxel units.
         // With VoxelSize = 50, 95 voxels = 47.5 meters.
        const double IslandRadius = 95.0;

        // Base vertical placement in world voxel units.
        const double BaseHeight = 8.0;

        // Mountain/hill amplitude.
        const double HeightAmplitude = 34.0;

        // Where the bottom of the island volume fades out.
        const double BaseBottom = -42.0;

        for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
        {
            for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
            {
                for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
                {
                    const FIntVector WorldVoxel = LocalToWorldVoxel(X, Y, Z);

                    const double WX = static_cast<double>(WorldVoxel.X);
                    const double WY = static_cast<double>(WorldVoxel.Y);
                    const double WZ = static_cast<double>(WorldVoxel.Z);

                    const double Distance2D = FMath::Sqrt(WX * WX + WY * WY);

                    /*
                     * Edge falloff:
                     * 1 near center, 0 near outer radius.
                     */
                    const double EdgeT = FMath::Clamp(Distance2D / IslandRadius, 0.0, 1.0);
                    const double EdgeFalloff = 1.0 - EdgeT * EdgeT * (3.0 - 2.0 * EdgeT);

                    /*
                     * Cheap deterministic pseudo-noise.
                     * We will replace this with real FastNoiseLite/Perlin later.
                     */
                    const double LargeNoise =
                        FMath::Sin(WX * 0.035) * 0.45 +
                        FMath::Cos(WY * 0.031) * 0.40 +
                        FMath::Sin((WX + WY) * 0.022) * 0.50;

                    const double MediumNoise =
                        FMath::Sin(WX * 0.091 + WY * 0.037) * 0.35 +
                        FMath::Cos(WY * 0.083 - WX * 0.029) * 0.30;

                    const double RidgeNoise =
                        FMath::Abs(FMath::Sin(WX * 0.047 + WY * 0.061)) * 0.65;

                    const double Height =
                        BaseHeight +
                        EdgeFalloff * HeightAmplitude +
                        LargeNoise * 12.0 +
                        MediumNoise * 6.0 +
                        RidgeNoise * 5.0;

                    /*
                     * Bottom surface also rises near edges, giving an island/landmass shape
                     * instead of an infinite slab.
                     */
                    const double Bottom =
                        BaseBottom +
                        (1.0 - EdgeFalloff) * 38.0 +
                        FMath::Sin(WX * 0.041) * 3.0 +
                        FMath::Cos(WY * 0.039) * 3.0;

                    /*
                     * Basic cave mask. This carves air pockets only underground.
                     */
                    const double CaveNoise =
                        FMath::Sin(WX * 0.105 + WZ * 0.071) +
                        FMath::Cos(WY * 0.097 - WZ * 0.064) +
                        FMath::Sin((WX + WY + WZ) * 0.052);

                    const bool bInsideVerticalVolume =
                        WZ <= Height && WZ >= Bottom;

                    const bool bInsideHorizontalIsland =
                        Distance2D <= IslandRadius;

                    const bool bUnderground =
                        WZ < Height - 8.0;

                    const bool bCave =
                        bUnderground &&
                        CaveNoise > 2.05 &&
                        EdgeFalloff > 0.18;

                    FTerraforgeVoxel Voxel;

                    if (bInsideHorizontalIsland && bInsideVerticalVolume && !bCave)
                    {
                        Voxel.MaterialId = 1;
                    }

                    SetVoxel(X, Y, Z, Voxel);
                }
            }
        }
    }
};
