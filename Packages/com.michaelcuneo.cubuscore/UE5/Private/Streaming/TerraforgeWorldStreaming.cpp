// Streaming implementation for ATerraforgeWorldActor
#include "Streaming/TerraforgeWorldStreaming.h"
#include "World/TerraforgeWorldActor.h"
#include "Kismet/GameplayStatics.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/Character.h"
#include "Components/CapsuleComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Async/Async.h"
#include "Core/TerraforgeVoxelMath.h"
#include "Density/TerraforgeMarchingCubes.h"
#include "Block/TerraforgeVoxelMesher.h"
#include "Core/TerraforgeVoxelChunkComponent.h"
#include "Core/TerraforgeVoxelLog.h"

bool ATerraforgeWorldActor::ShouldUseRuntimeStreaming() const
{
    const UWorld* World = GetWorld();

    return
        bEnableRuntimeStreaming &&
        World != nullptr &&
        World->IsGameWorld();
}

bool ATerraforgeWorldActor::GetStreamingViewerChunkCoord(
    FIntVector& OutViewerChunkCoord
) const
{
    UWorld* World = GetWorld();

    if (World == nullptr)
    {
        return false;
    }

    APawn* PlayerPawn =
        UGameplayStatics::GetPlayerPawn(World, 0);

    if (PlayerPawn == nullptr)
    {
        return false;
    }

    const FVector PlayerWorldLocation =
        PlayerPawn->GetActorLocation();

    const FVector PlayerLocalLocation =
        GetActorTransform().InverseTransformPosition(PlayerWorldLocation);

    const double ChunkWorldSize =
        GetChunkWorldSize();

    OutViewerChunkCoord = FIntVector(
        FMath::FloorToInt(PlayerLocalLocation.X / ChunkWorldSize),
        FMath::FloorToInt(PlayerLocalLocation.Y / ChunkWorldSize),
        0
    );

    return true;
}

void ATerraforgeWorldActor::BuildStreamingChunkSet(
    const FIntVector& ViewerChunkCoord,
    const int32 Radius,
    TSet<FIntVector>& OutChunkCoords
) const
{
    OutChunkCoords.Reset();

    const int32 SafeRadius =
        FMath::Max(0, Radius);

    if (TerrainSystem == ETerraforgeTerrainSystem::Block)
    {
        int32 MinChunkZ = BlockMinChunkZ;
        int32 MaxChunkZ = BlockMaxChunkZ;

        if (MinChunkZ > MaxChunkZ)
        {
            Swap(MinChunkZ, MaxChunkZ);
        }

        const int32 SideLength = SafeRadius * 2 + 1;
        OutChunkCoords.Reserve(
            SideLength * SideLength * (MaxChunkZ - MinChunkZ + 1)
        );

        for (int32 Z = MinChunkZ; Z <= MaxChunkZ; ++Z)
        {
            for (int32 Y = ViewerChunkCoord.Y - SafeRadius; Y <= ViewerChunkCoord.Y + SafeRadius; ++Y)
            {
                for (int32 X = ViewerChunkCoord.X - SafeRadius; X <= ViewerChunkCoord.X + SafeRadius; ++X)
                {
                    OutChunkCoords.Add(
                        FIntVector(X, Y, Z)
                    );
                }
            }
        }

        return;
    }

    const int32 SafeBelowSurface =
        FMath::Max(0, SmoothStreamingChunksBelowSurface);

    const int32 SafeAboveSurface =
        FMath::Max(0, SmoothStreamingChunksAboveSurface);

    const int32 SideLength = SafeRadius * 2 + 1;
    OutChunkCoords.Reserve(
        SideLength * SideLength * (SafeBelowSurface + SafeAboveSurface + 1)
    );

    for (int32 Y = ViewerChunkCoord.Y - SafeRadius; Y <= ViewerChunkCoord.Y + SafeRadius; ++Y)
    {
        for (int32 X = ViewerChunkCoord.X - SafeRadius; X <= ViewerChunkCoord.X + SafeRadius; ++X)
        {
            const double CenterWorldVoxelX =
                static_cast<double>(X * TerraforgeVoxel::ChunkSize) +
                static_cast<double>(TerraforgeVoxel::ChunkSize) * 0.5;

            const double CenterWorldVoxelY =
                static_cast<double>(Y * TerraforgeVoxel::ChunkSize) +
                static_cast<double>(TerraforgeVoxel::ChunkSize) * 0.5;

            const FTerraforgeTerrainSample SurfaceSample =
                SampleTerrainAtWorldVoxelPosition(
                    FVector(
                        CenterWorldVoxelX,
                        CenterWorldVoxelY,
                        0.0
                    )
                );

            const int32 SurfaceChunkZ =
                FMath::FloorToInt(
                    static_cast<double>(SurfaceSample.SurfaceHeight) /
                    static_cast<double>(TerraforgeVoxel::ChunkSize)
                );

            const int32 MinChunkZ =
                SurfaceChunkZ - SafeBelowSurface;

            const int32 MaxChunkZ =
                SurfaceChunkZ + SafeAboveSurface;

            for (int32 Z = MinChunkZ; Z <= MaxChunkZ; ++Z)
            {
                OutChunkCoords.Add(
                    FIntVector(X, Y, Z)
                );
            }
        }
    }
}

void ATerraforgeWorldActor::UpdateRuntimeStreaming()
{
    if (!ShouldUseRuntimeStreaming())
    {
        return;
    }

    FIntVector ViewerChunkCoord = FIntVector::ZeroValue;

    if (!GetStreamingViewerChunkCoord(ViewerChunkCoord))
    {
        return;
    }

    const bool bStreamingConfigUnchanged =
        bCachedStreamingChunkSetsValid &&
        CachedStreamingViewerChunkCoord == ViewerChunkCoord &&
        CachedStreamingViewDistanceInChunks == ViewDistanceInChunks &&
        CachedStreamingUnloadPaddingInChunks == StreamingUnloadPaddingInChunks &&
        CachedSmoothStreamingChunksBelowSurface == SmoothStreamingChunksBelowSurface &&
        CachedSmoothStreamingChunksAboveSurface == SmoothStreamingChunksAboveSurface &&
        CachedStreamingTerrainSystem == TerrainSystem;

    if (bStreamingConfigUnchanged)
    {
        return;
    }

    TSet<FIntVector> DesiredChunkCoords;
    BuildStreamingChunkSet(
        ViewerChunkCoord,
        ViewDistanceInChunks,
        DesiredChunkCoords
    );

    QueueMissingStreamingChunks(
        ViewerChunkCoord,
        DesiredChunkCoords
    );

    TSet<FIntVector> KeepChunkCoords;
    BuildStreamingChunkSet(
        ViewerChunkCoord,
        ViewDistanceInChunks + StreamingUnloadPaddingInChunks,
        KeepChunkCoords
    );

    UnloadChunksOutsideStreamingSet(KeepChunkCoords);

    CachedStreamingViewerChunkCoord = ViewerChunkCoord;
    CachedStreamingViewDistanceInChunks = ViewDistanceInChunks;
    CachedStreamingUnloadPaddingInChunks = StreamingUnloadPaddingInChunks;
    CachedSmoothStreamingChunksBelowSurface = SmoothStreamingChunksBelowSurface;
    CachedSmoothStreamingChunksAboveSurface = SmoothStreamingChunksAboveSurface;
    CachedStreamingTerrainSystem = TerrainSystem;
    CachedDesiredStreamingChunkCoords = MoveTemp(DesiredChunkCoords);
    bCachedStreamingChunkSetsValid = true;

    bRuntimeStreamingInitialized = true;
}

void ATerraforgeWorldActor::QueueMissingStreamingChunks(
    const FIntVector& ViewerChunkCoord,
    const TSet<FIntVector>& DesiredChunkCoords
)
{
    TArray<FIntVector> MissingChunkCoords;
    MissingChunkCoords.Reserve(DesiredChunkCoords.Num());

    for (const FIntVector& ChunkCoord : DesiredChunkCoords)
    {
        if (ActiveChunkComponentsByCoord.Contains(ChunkCoord))
        {
            continue;
        }

        if (PendingStreamingChunkCoordSet.Contains(ChunkCoord))
        {
            continue;
        }

        if (PendingStreamingMeshData.Contains(ChunkCoord))
        {
            continue;
        }

        if (AsyncStreamingChunkCoordSet.Contains(ChunkCoord))
        {
            continue;
        }

        if (ProcessedEmptyStreamingChunks.Contains(ChunkCoord))
        {
            continue;
        }

        MissingChunkCoords.Add(ChunkCoord);
    }

    MissingChunkCoords.Sort(
        [ViewerChunkCoord](const FIntVector& A, const FIntVector& B)
        {
            const int32 ADX = A.X - ViewerChunkCoord.X;
            const int32 ADY = A.Y - ViewerChunkCoord.Y;
            const int32 ADZ = A.Z - ViewerChunkCoord.Z;

            const int32 BDX = B.X - ViewerChunkCoord.X;
            const int32 BDY = B.Y - ViewerChunkCoord.Y;
            const int32 BDZ = B.Z - ViewerChunkCoord.Z;

            const int32 ADistanceSquared =
                ADX * ADX +
                ADY * ADY +
                ADZ * ADZ;

            const int32 BDistanceSquared =
                BDX * BDX +
                BDY * BDY +
                BDZ * BDZ;

            if (ADistanceSquared != BDistanceSquared)
            {
                return ADistanceSquared < BDistanceSquared;
            }

            const int32 AAbsZ = FMath::Abs(A.Z);
            const int32 BAbsZ = FMath::Abs(B.Z);

            if (AAbsZ != BAbsZ)
            {
                return AAbsZ < BAbsZ;
            }

            return A.Z < B.Z;
        }
    );

    for (const FIntVector& ChunkCoord : MissingChunkCoords)
    {
        PendingStreamingChunkCoords.Add(ChunkCoord);
        PendingStreamingChunkCoordSet.Add(ChunkCoord);
    }
}

void ATerraforgeWorldActor::QueueStreamingMeshApply(
    const FIntVector& ChunkCoord,
    FTerraforgeVoxelMeshData&& MeshData
)
{
    if (MeshData.IsEmpty())
    {
        ProcessedEmptyStreamingChunks.Add(ChunkCoord);
        return;
    }

    PendingStreamingMeshData.Add(
        ChunkCoord,
        MoveTemp(MeshData)
    );

    if (!PendingStreamingMeshCoordSet.Contains(ChunkCoord))
    {
        PendingStreamingMeshCoordSet.Add(ChunkCoord);
        PendingStreamingMeshCoords.Add(ChunkCoord);
    }

    if (PendingStreamingMeshApplyIndex >= PendingStreamingMeshCoords.Num())
    {
        PendingStreamingMeshApplyIndex = 0;
    }
}

void ATerraforgeWorldActor::ProcessStreamingGenerationQueue()
{
    if (PendingStreamingChunkCoordSet.Num() == 0)
    {
        if (PendingStreamingChunkQueueHead > 0)
        {
            PendingStreamingChunkCoords.Reset();
            PendingStreamingChunkQueueHead = 0;
        }

        return;
    }

    const bool bInitialStreamingFill =
        !bWorldReady;

    const int32 MaxDispatchesThisFrame =
        bInitialStreamingFill
        ? FMath::Max(1, InitialStreamingChunksGeneratedPerFrame)
        : FMath::Max(1, StreamingChunksGeneratedPerFrame);

    int32 DispatchedThisFrame = 0;

    while (
        PendingStreamingChunkQueueHead < PendingStreamingChunkCoords.Num() &&
        DispatchedThisFrame < MaxDispatchesThisFrame &&
        ActiveAsyncStreamingChunkTasks < FMath::Max(1, MaxAsyncStreamingChunkTasks)
        )
    {
        const FIntVector ChunkCoord =
            PendingStreamingChunkCoords[PendingStreamingChunkQueueHead];

        ++PendingStreamingChunkQueueHead;

        if (!PendingStreamingChunkCoordSet.Remove(ChunkCoord))
        {
            continue;
        }

        if (ActiveChunkComponentsByCoord.Contains(ChunkCoord))
        {
            continue;
        }

        if (PendingStreamingMeshData.Contains(ChunkCoord))
        {
            continue;
        }

        if (AsyncStreamingChunkCoordSet.Contains(ChunkCoord))
        {
            continue;
        }

        if (ProcessedEmptyStreamingChunks.Contains(ChunkCoord))
        {
            continue;
        }

        GenerateStreamingChunk(ChunkCoord);

        ++DispatchedThisFrame;
    }

    if (PendingStreamingChunkQueueHead >= PendingStreamingChunkCoords.Num())
    {
        PendingStreamingChunkCoords.Reset();
        PendingStreamingChunkQueueHead = 0;
    }
}

void ATerraforgeWorldActor::GenerateStreamingChunk(
    const FIntVector& ChunkCoord
)
{
    if (ActiveChunkComponentsByCoord.Contains(ChunkCoord))
    {
        return;
    }

    if (PendingStreamingMeshData.Contains(ChunkCoord))
    {
        return;
    }

    if (AsyncStreamingChunkCoordSet.Contains(ChunkCoord))
    {
        return;
    }

    if (ProcessedEmptyStreamingChunks.Contains(ChunkCoord))
    {
        return;
    }

    struct FTerraforgeStreamingChunkBuildResult
    {
        FIntVector ChunkCoord = FIntVector::ZeroValue;
        ETerraforgeTerrainSystem TerrainSystem = ETerraforgeTerrainSystem::Block;

        bool bHasRenderableMesh = false;

        FTerraforgeVoxelChunkData BlockChunkData;
        FTerraforgeDensityChunkData DensityChunkData;

        TMap<FIntVector, FTerraforgeDensityChunkData> AdditionalDensityChunks;

        FTerraforgeVoxelMeshData MeshData;

        FTerraforgeStreamingChunkBuildResult()
            : BlockChunkData(FIntVector::ZeroValue)
            , DensityChunkData(FIntVector::ZeroValue)
        {
        }

        explicit FTerraforgeStreamingChunkBuildResult(
            const FIntVector& InChunkCoord,
            const ETerraforgeTerrainSystem InTerrainSystem
        )
            : ChunkCoord(InChunkCoord)
            , TerrainSystem(InTerrainSystem)
            , BlockChunkData(InChunkCoord)
            , DensityChunkData(InChunkCoord)
        {
        }
    };

    const int32 StreamingGenerationId =
        ActiveStreamingGenerationId;

    const ETerraforgeTerrainSystem CapturedTerrainSystem =
        TerrainSystem;

    const int32 CapturedDensityMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    TMap<FIntVector, FTerraforgeDensityChunkData> DensitySnapshot;

    for (int32 DZ = -1; DZ <= 1; ++DZ)
    {
        for (int32 DY = -1; DY <= 1; ++DY)
        {
            for (int32 DX = -1; DX <= 1; ++DX)
            {
                const FIntVector SnapshotCoord =
                    ChunkCoord + FIntVector(DX, DY, DZ);

                const FTerraforgeDensityChunkData* ExistingDensityChunk =
                    DensityChunkDataByCoord.Find(SnapshotCoord);

                if (ExistingDensityChunk != nullptr)
                {
                    DensitySnapshot.Add(
                        SnapshotCoord,
                        *ExistingDensityChunk
                    );
                }
            }
        }
    }

    TMap<FIntVector, FTerraforgeVoxelChunkData> BlockSnapshot;

    for (int32 DZ = -1; DZ <= 1; ++DZ)
    {
        for (int32 DY = -1; DY <= 1; ++DY)
        {
            for (int32 DX = -1; DX <= 1; ++DX)
            {
                const FIntVector SnapshotCoord =
                    ChunkCoord + FIntVector(DX, DY, DZ);

                const FTerraforgeVoxelChunkData* ExistingBlockChunk =
                    BlockChunkDataByCoord.Find(SnapshotCoord);

                if (ExistingBlockChunk != nullptr)
                {
                    BlockSnapshot.Add(
                        SnapshotCoord,
                        *ExistingBlockChunk
                    );
                }
            }
        }
    }

    TMap<FIntVector, TMap<int32, uint16>> BlockOverrideSnapshot;

    for (int32 DZ = -1; DZ <= 1; ++DZ)
    {
        for (int32 DY = -1; DY <= 1; ++DY)
        {
            for (int32 DX = -1; DX <= 1; ++DX)
            {
                const FIntVector SnapshotCoord =
                    ChunkCoord + FIntVector(DX, DY, DZ);

                const TMap<int32, uint16>* ExistingOverrides =
                    BlockVoxelOverridesByChunk.Find(SnapshotCoord);

                if (ExistingOverrides != nullptr)
                {
                    BlockOverrideSnapshot.Add(
                        SnapshotCoord,
                        *ExistingOverrides
                    );
                }
            }
        }
    }

    AsyncStreamingChunkCoordSet.Add(ChunkCoord);
    ++ActiveAsyncStreamingChunkTasks;

    Async(
        EAsyncExecution::ThreadPool,
        [
            this,
            StreamingGenerationId,
            ChunkCoord,
            CapturedTerrainSystem,
            CapturedDensityMeshStep,
            DensitySnapshot = MoveTemp(DensitySnapshot),
            BlockSnapshot = MoveTemp(BlockSnapshot),
            BlockOverrideSnapshot = MoveTemp(BlockOverrideSnapshot)
        ]() mutable
        {
            FTerraforgeStreamingChunkBuildResult Result(
                ChunkCoord,
                CapturedTerrainSystem
            );

            if (CapturedTerrainSystem == ETerraforgeTerrainSystem::Block)
            {
                FTerraforgeVoxelChunkData* ExistingChunkData =
                    BlockSnapshot.Find(ChunkCoord);

                if (ExistingChunkData != nullptr)
                {
                    Result.BlockChunkData = *ExistingChunkData;
                }
                else
                {
                    GenerateBlockChunkData(Result.BlockChunkData);
                }

                const TMap<int32, uint16>* Overrides =
                    BlockOverrideSnapshot.Find(ChunkCoord);

                if (Overrides != nullptr)
                {
                    for (const TPair<int32, uint16>& Pair : *Overrides)
                    {
                        if (Result.BlockChunkData.Voxels.IsValidIndex(Pair.Key))
                        {
                            Result.BlockChunkData.Voxels[Pair.Key].MaterialId = Pair.Value;
                        }
                    }
                }

                if (!Result.BlockChunkData.HasAnySolidVoxel())
                {
                    Result.bHasRenderableMesh = false;
                }
                else
                {
                    auto BlockMaterialLookup =
                        [
                            &BlockSnapshot,
                            &BlockOverrideSnapshot,
                            this
                        ](const FIntVector& WorldVoxelCoord) -> uint16
                        {
                            const FIntVector LookupChunkCoord =
                                TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                            const FIntVector LocalCoord =
                                TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                            const int32 VoxelIndex =
                                LocalCoord.X +
                                LocalCoord.Y * TerraforgeVoxel::ChunkSize +
                                LocalCoord.Z * TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize;

                            const TMap<int32, uint16>* Overrides =
                                BlockOverrideSnapshot.Find(LookupChunkCoord);

                            if (Overrides != nullptr)
                            {
                                const uint16* OverrideMaterial =
                                    Overrides->Find(VoxelIndex);

                                if (OverrideMaterial != nullptr)
                                {
                                    return *OverrideMaterial;
                                }
                            }

                            const FTerraforgeVoxelChunkData* SnapshotChunk =
                                BlockSnapshot.Find(LookupChunkCoord);

                            if (SnapshotChunk != nullptr)
                            {
                                return SnapshotChunk->GetVoxel(
                                    LocalCoord.X,
                                    LocalCoord.Y,
                                    LocalCoord.Z
                                ).MaterialId;
                            }

                            const FTerraforgeTerrainSample Sample =
                                SampleTerrainAtWorldVoxelPosition(
                                    FVector(
                                        static_cast<double>(WorldVoxelCoord.X),
                                        static_cast<double>(WorldVoxelCoord.Y),
                                        static_cast<double>(WorldVoxelCoord.Z)
                                    )
                                );

                            return Sample.Density > 0.0f
                                ? static_cast<uint16>(FMath::Clamp(Sample.SolidMaterialId, 1, 65535))
                                : 0;
                        };

                    FTerraforgeVoxelMesher::GenerateGreedyMeshNeighbourAware(
                        Result.BlockChunkData,
                        BlockMaterialLookup,
                        Result.MeshData
                    );

                    Result.bHasRenderableMesh =
                        !Result.MeshData.IsEmpty();
                }
            }
            else
            {
                FTerraforgeDensityChunkData* ExistingDensityChunk =
                    DensitySnapshot.Find(ChunkCoord);

                if (ExistingDensityChunk != nullptr)
                {
                    Result.DensityChunkData = *ExistingDensityChunk;
                }
                else
                {
                    FillDensityChunkFromTerrainSampler(Result.DensityChunkData);
                }

                if (!Result.DensityChunkData.HasAnySolidVoxel())
                {
                    Result.bHasRenderableMesh = false;
                }
                else
                {
                    for (int32 DZ = -1; DZ <= 1; ++DZ)
                    {
                        for (int32 DY = -1; DY <= 1; ++DY)
                        {
                            for (int32 DX = -1; DX <= 1; ++DX)
                            {
                                if (DX == 0 && DY == 0 && DZ == 0)
                                {
                                    continue;
                                }

                                const FIntVector NeighbourChunkCoord =
                                    ChunkCoord + FIntVector(DX, DY, DZ);

                                if (DensitySnapshot.Contains(NeighbourChunkCoord))
                                {
                                    continue;
                                }

                                FTerraforgeDensityChunkData NeighbourDensityChunkData(
                                    NeighbourChunkCoord
                                );

                                FillDensityChunkFromTerrainSampler(
                                    NeighbourDensityChunkData
                                );

                                if (NeighbourDensityChunkData.HasAnySolidVoxel())
                                {
                                    Result.AdditionalDensityChunks.Add(
                                        NeighbourChunkCoord,
                                        MoveTemp(NeighbourDensityChunkData)
                                    );
                                }
                            }
                        }
                    }

                    DensitySnapshot.Add(
                        ChunkCoord,
                        Result.DensityChunkData
                    );

                    for (const TPair<FIntVector, FTerraforgeDensityChunkData>& Pair :
                        Result.AdditionalDensityChunks)
                    {
                        DensitySnapshot.Add(
                            Pair.Key,
                            Pair.Value
                        );
                    }

                    auto DensityLookup =
                        [
                            &DensitySnapshot,
                            this
                        ](const FIntVector& WorldVoxelCoord) -> float
                        {
                            const FIntVector LookupChunkCoord =
                                TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                            const FIntVector LocalCoord =
                                TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                            const FTerraforgeDensityChunkData* SnapshotChunk =
                                DensitySnapshot.Find(LookupChunkCoord);

                            if (SnapshotChunk != nullptr)
                            {
                                return SnapshotChunk->GetVoxel(
                                    LocalCoord.X,
                                    LocalCoord.Y,
                                    LocalCoord.Z
                                ).Density;
                            }

                            return GetDensityAtWorldVoxelPosition(
                                FVector(
                                    static_cast<double>(WorldVoxelCoord.X),
                                    static_cast<double>(WorldVoxelCoord.Y),
                                    static_cast<double>(WorldVoxelCoord.Z)
                                )
                            );
                        };

                    auto MaterialLookup =
                        [
                            &DensitySnapshot,
                            this
                        ](const FIntVector& WorldVoxelCoord) -> uint16
                        {
                            const FIntVector LookupChunkCoord =
                                TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                            const FIntVector LocalCoord =
                                TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                            const FTerraforgeDensityChunkData* SnapshotChunk =
                                DensitySnapshot.Find(LookupChunkCoord);

                            if (SnapshotChunk != nullptr)
                            {
                                return SnapshotChunk->GetVoxel(
                                    LocalCoord.X,
                                    LocalCoord.Y,
                                    LocalCoord.Z
                                ).MaterialId;
                            }

                            const FTerraforgeTerrainSample Sample =
                                SampleTerrainAtWorldVoxelPosition(
                                    FVector(
                                        static_cast<double>(WorldVoxelCoord.X),
                                        static_cast<double>(WorldVoxelCoord.Y),
                                        static_cast<double>(WorldVoxelCoord.Z)
                                    )
                                );

                            return Sample.Density > 0.0f
                                ? static_cast<uint16>(FMath::Clamp(Sample.SolidMaterialId, 1, 65535))
                                : 0;
                        };

                    FTerraforgeMarchingCubes::GenerateMesh(
                        ChunkCoord,
                        DensityLookup,
                        MaterialLookup,
                        Result.MeshData,
                        CapturedDensityMeshStep,
                        bSmoothNormalsAcrossMaterialBoundaries
                    );

                    Result.bHasRenderableMesh =
                        !Result.MeshData.IsEmpty();
                }
            }

            AsyncTask(
                ENamedThreads::GameThread,
                [
                    this,
                    StreamingGenerationId,
                    Result = MoveTemp(Result)
                ]() mutable
                {
                    AsyncStreamingChunkCoordSet.Remove(Result.ChunkCoord);
                    ActiveAsyncStreamingChunkTasks =
                        FMath::Max(0, ActiveAsyncStreamingChunkTasks - 1);

                    if (StreamingGenerationId != ActiveStreamingGenerationId)
                    {
                        return;
                    }

                    if (!IsChunkActiveOrStreamingDesired(Result.ChunkCoord))
                    {
                        return;
                    }

                    if (!Result.bHasRenderableMesh)
                    {
                        ProcessedEmptyStreamingChunks.Add(Result.ChunkCoord);
                        return;
                    }

                    if (Result.TerrainSystem == ETerraforgeTerrainSystem::Block)
                    {
                        BlockChunkDataByCoord.Add(
                            Result.ChunkCoord,
                            MoveTemp(Result.BlockChunkData)
                        );
                    }
                    else
                    {
                        DensityChunkDataByCoord.Add(
                            Result.ChunkCoord,
                            MoveTemp(Result.DensityChunkData)
                        );
                        bDensityChunkBoundsDirty = true;

                        for (TPair<FIntVector, FTerraforgeDensityChunkData>& Pair :
                            Result.AdditionalDensityChunks)
                        {
                            if (!DensityChunkDataByCoord.Contains(Pair.Key))
                            {
                                DensityChunkDataByCoord.Add(
                                    Pair.Key,
                                    MoveTemp(Pair.Value)
                                );
                                bDensityChunkBoundsDirty = true;
                            }
                        }
                    }

                    QueueStreamingMeshApply(
                        Result.ChunkCoord,
                        MoveTemp(Result.MeshData)
                    );
                }
            );
        }
    );
}

void ATerraforgeWorldActor::TickStreamingMeshApply()
{
    if (PendingStreamingMeshCoordSet.Num() == 0)
    {
        return;
    }

    int32 AppliedThisFrame = 0;

    while (
        PendingStreamingMeshApplyIndex < PendingStreamingMeshCoords.Num() &&
        AppliedThisFrame < FMath::Max(1, StreamingMeshAppliesPerFrame)
        )
    {
        const FIntVector ChunkCoord =
            PendingStreamingMeshCoords[PendingStreamingMeshApplyIndex];

        ++PendingStreamingMeshApplyIndex;
        ++AppliedThisFrame;

        if (!PendingStreamingMeshCoordSet.Remove(ChunkCoord))
        {
            continue;
        }

        FTerraforgeVoxelMeshData* MeshData =
            PendingStreamingMeshData.Find(ChunkCoord);

        if (MeshData == nullptr || MeshData->IsEmpty())
        {
            ProcessedEmptyStreamingChunks.Add(ChunkCoord);
            PendingStreamingMeshData.Remove(ChunkCoord);
            continue;
        }

        ProcessedEmptyStreamingChunks.Remove(ChunkCoord);

        ApplyMeshToChunk(
            ChunkCoord,
            *MeshData
        );

        PendingStreamingMeshData.Remove(ChunkCoord);
    }

    if (PendingStreamingMeshApplyIndex >= PendingStreamingMeshCoords.Num())
    {
        PendingStreamingMeshCoords.Reset();
        PendingStreamingMeshData.Reset();
        PendingStreamingMeshCoordSet.Reset();
        PendingStreamingMeshApplyIndex = 0;

        UpdateChunkCollisionStates();
    }
}

void ATerraforgeWorldActor::UnloadChunksOutsideStreamingSet(
    const TSet<FIntVector>& KeepChunkCoords
)
{
    TArray<FIntVector> ChunkCoordsToUnload;

    for (const TPair<FIntVector, TObjectPtr<UTerraforgeVoxelChunkComponent>>& Pair :
        ActiveChunkComponentsByCoord)
    {
        if (!KeepChunkCoords.Contains(Pair.Key))
        {
            ChunkCoordsToUnload.Add(Pair.Key);
        }
    }

    for (const FIntVector& ChunkCoord : ChunkCoordsToUnload)
    {
        TObjectPtr<UTerraforgeVoxelChunkComponent>* ChunkComponentPtr =
            ActiveChunkComponentsByCoord.Find(ChunkCoord);

        if (ChunkComponentPtr == nullptr || !IsValid(ChunkComponentPtr->Get()))
        {
            ActiveChunkComponentsByCoord.Remove(ChunkCoord);
            continue;
        }

        UTerraforgeVoxelChunkComponent* ChunkComponent =
            ChunkComponentPtr->Get();

        ChunkComponent->ClearAllMeshSections();
        ChunkComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        ChunkComponent->SetVisibility(false, true);
        ChunkComponent->SetHiddenInGame(true);
        ChunkComponent->Deactivate();

        ActiveChunkComponentsByCoord.Remove(ChunkCoord);
        PooledChunkComponents.Add(ChunkComponent);
    }
}

bool ATerraforgeWorldActor::IsChunkActiveOrStreamingDesired(
    const FIntVector& ChunkCoord
) const
{
    if (ActiveChunkComponentsByCoord.Contains(ChunkCoord))
    {
        return true;
    }

    if (!ShouldUseRuntimeStreaming())
    {
        return true;
    }

    if (bCachedStreamingChunkSetsValid)
    {
        return CachedDesiredStreamingChunkCoords.Contains(ChunkCoord);
    }

    FIntVector ViewerChunkCoord = FIntVector::ZeroValue;

    if (!GetStreamingViewerChunkCoord(ViewerChunkCoord))
    {
        return false;
    }

    TSet<FIntVector> DesiredChunkCoords;
    BuildStreamingChunkSet(
        ViewerChunkCoord,
        ViewDistanceInChunks,
        DesiredChunkCoords
    );

    return DesiredChunkCoords.Contains(ChunkCoord);
}

void ATerraforgeWorldActor::PlacePlayerOnTerrainAfterStreamingReady()
{
    UWorld* World = GetWorld();

    if (World == nullptr)
    {
        return;
    }

    ACharacter* PlayerCharacter =
        UGameplayStatics::GetPlayerCharacter(World, 0);

    if (PlayerCharacter == nullptr)
    {
        return;
    }

    UCapsuleComponent* Capsule =
        PlayerCharacter->GetCapsuleComponent();

    if (Capsule == nullptr)
    {
        return;
    }

    const FVector PlayerLocation =
        PlayerCharacter->GetActorLocation();

    double TerrainSurfaceWorldZ = 0.0;

    if (!FindTerrainSpawnSurfaceAtWorldXY(PlayerLocation, TerrainSurfaceWorldZ))
    {
        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Failed to place player on streamed terrain. No terrain surface found at player XY.")
        );

        return;
    }

    const double SpawnClearance =
        Capsule->GetScaledCapsuleHalfHeight() + 24.0;

    FVector NewPlayerLocation = PlayerLocation;
    NewPlayerLocation.Z =
        TerrainSurfaceWorldZ + SpawnClearance;

    if (UCharacterMovementComponent* MovementComponent =
        PlayerCharacter->GetCharacterMovement())
    {
        MovementComponent->StopMovementImmediately();
        MovementComponent->SetMovementMode(MOVE_Walking);
    }

    PlayerCharacter->SetActorLocation(
        NewPlayerLocation,
        false,
        nullptr,
        ETeleportType::TeleportPhysics
    );

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Placed player on streamed Terraforge terrain. OldZ=%.2f NewZ=%.2f SurfaceZ=%.2f"),
        PlayerLocation.Z,
        NewPlayerLocation.Z,
        TerrainSurfaceWorldZ
    );
}

void ATerraforgeWorldActor::InvalidateStreamingChunkForEdit(
    const FIntVector& ChunkCoord
)
{
    ProcessedEmptyStreamingChunks.Remove(ChunkCoord);
    PendingStreamingChunkCoordSet.Remove(ChunkCoord);
    bCachedStreamingChunkSetsValid = false;

    for (int32 DZ = -1; DZ <= 1; ++DZ)
    {
        for (int32 DY = -1; DY <= 1; ++DY)
        {
            for (int32 DX = -1; DX <= 1; ++DX)
            {
                const FIntVector NeighbourCoord =
                    ChunkCoord + FIntVector(DX, DY, DZ);

                ProcessedEmptyStreamingChunks.Remove(NeighbourCoord);
            }
        }
    }
}