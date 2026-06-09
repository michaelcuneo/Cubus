#include "World/TerraforgeWorldActor.h"

#include "Core/TerraforgeVoxelChunkComponent.h"
#include "Components/SceneComponent.h"
#include "Density/TerraforgeMarchingCubes.h"
#include "Core/TerraforgeVoxelMath.h"
#include "Async/Async.h"
#include "DrawDebugHelpers.h"
#include "GameFramework/PlayerController.h"
#include "Kismet/GameplayStatics.h"
#include "GameFramework/Character.h"
#include "Components/CapsuleComponent.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Core/TerraforgeVoxelLog.h"

namespace TerraforgeWorldActorLocal
{
    bool ShouldChunkHaveCollisionNearLocalPosition(
        const FIntVector& ChunkCoord,
        const FVector& PlayerLocalLocation,
        const double ChunkWorldSize,
        const double CollisionActivationRadiusSquared
    )
    {
        const double MinX =
            static_cast<double>(ChunkCoord.X) * ChunkWorldSize;

        const double MinY =
            static_cast<double>(ChunkCoord.Y) * ChunkWorldSize;

        const double MaxX =
            static_cast<double>(ChunkCoord.X + 1) * ChunkWorldSize;

        const double MaxY =
            static_cast<double>(ChunkCoord.Y + 1) * ChunkWorldSize;

        const double ClosestX =
            FMath::Clamp(
                static_cast<double>(PlayerLocalLocation.X),
                MinX,
                MaxX
            );

        const double ClosestY =
            FMath::Clamp(
                static_cast<double>(PlayerLocalLocation.Y),
                MinY,
                MaxY
            );

        const double DX =
            static_cast<double>(PlayerLocalLocation.X) - ClosestX;

        const double DY =
            static_cast<double>(PlayerLocalLocation.Y) - ClosestY;

        const double DistanceToChunkBoundsSquared2D =
            DX * DX + DY * DY;

        return DistanceToChunkBoundsSquared2D <=
            CollisionActivationRadiusSquared;
    }
}

ATerraforgeWorldActor::ATerraforgeWorldActor()
{
    PrimaryActorTick.bCanEverTick = true;

    SceneRoot = CreateDefaultSubobject<USceneComponent>(TEXT("SceneRoot"));
    SetRootComponent(SceneRoot);
}

void ATerraforgeWorldActor::BeginPlay()
{
    Super::BeginPlay();

    if (ShouldUseRuntimeStreaming())
    {
        if (bGenerateOnBeginPlay)
        {
            GenerateWorld();
        }

        return;
    }

    const bool bShouldAutoLoad =
        PersistenceMode == ETerraforgeWorldPersistenceMode::AutoLoadOnly ||
        PersistenceMode == ETerraforgeWorldPersistenceMode::AutoLoadAndSave;

    if (bShouldAutoLoad)
    {
        const bool bHasSave =
            UGameplayStatics::DoesSaveGameExist(
                DefaultSaveSlotName,
                DefaultSaveUserIndex
            );

        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Terraforge persistence auto-load check. Mode=%d Slot=%s UserIndex=%d HasSave=%s"),
            static_cast<int32>(PersistenceMode),
            *DefaultSaveSlotName,
            DefaultSaveUserIndex,
            bHasSave ? TEXT("true") : TEXT("false")
        );

        if (bHasSave)
        {
            if (LoadDefaultWorld())
            {
                return;
            }

            if (!bGenerateIfNoSaveExists)
            {
                UE_LOG(
                    LogTerraforgeVoxel,
                    Warning,
                    TEXT("Terraforge auto-load failed or was skipped, and generation fallback is disabled.")
                );

                return;
            }
        }

        if (!bGenerateIfNoSaveExists)
        {
            UE_LOG(
                LogTerraforgeVoxel,
                Warning,
                TEXT("Terraforge persistence found no save and procedural fallback is disabled.")
            );

            return;
        }
    }

    if (bGenerateOnBeginPlay)
    {
        GenerateWorld();
    }
}

void ATerraforgeWorldActor::OnConstruction(
    const FTransform& Transform
)
{
    Super::OnConstruction(Transform);

#if WITH_EDITOR
    UWorld* World = GetWorld();

    if (World != nullptr && World->IsGameWorld())
    {
        return;
    }

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Terraforge editor construction. GenerateInEditor=%s TerrainSystem=%d Radius=%d"),
        bGenerateInEditor ? TEXT("true") : TEXT("false"),
        static_cast<int32>(TerrainSystem),
        RadiusInChunks
    );

    if (!bGenerateInEditor)
    {
        ReleaseAllChunkComponents();
        return;
    }

    bGenerationInProgress = false;
    bIsApplyingGeneratedMeshes = false;
    bAsyncRebuildInProgress = false;
    PendingGeneratedMeshData.Reset();
    PendingGeneratedMeshCoords.Reset();
    PendingGeneratedMeshApplyIndex = 0;

    PendingDensityRebuildMeshData.Reset();
    PendingDensityRebuildMeshCoords.Reset();
    PendingDensityRebuildMeshApplyIndex = 0;

    if (TerrainSystem == ETerraforgeTerrainSystem::SmoothDensity)
    {
        GenerateSmoothDensityWorldEditorPreview();
        return;
    }

    GenerateWorld();
#endif
}

void ATerraforgeWorldActor::Tick(const float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);

    if (bIsEndingPlay)
    {
        return;
    }

    TickBatchedGeneratedMeshApply();
    TickDensityRebuildMeshApply();
    TickStreamingMeshApply();

    bool bCollisionUpdatedThisTick = false;

    if (ShouldUseRuntimeStreaming())
    {
        UpdateRuntimeStreaming();
        ProcessStreamingGenerationQueue();

        if (
            !bWorldReady &&
            PendingStreamingChunkCoordSet.Num() == 0 &&
            PendingStreamingMeshCoordSet.Num() == 0 &&
            PendingStreamingMeshData.Num() == 0 &&
            ActiveChunkComponentsByCoord.Num() > 0
            )
        {
            TimeSinceLastCollisionUpdate = 0.0f;
            UpdateChunkCollisionStates();
            bCollisionUpdatedThisTick = true;
            MarkGenerationComplete();

            if (bPendingInitialStreamingPlayerPlacement)
            {
                bPendingInitialStreamingPlayerPlacement = false;
                PlacePlayerOnTerrainAfterStreamingReady();
            }
        }
    }

    TimeSinceLastCollisionUpdate += DeltaSeconds;

    if (!bCollisionUpdatedThisTick && TimeSinceLastCollisionUpdate >= CollisionUpdateInterval)
    {
        TimeSinceLastCollisionUpdate = 0.0f;
        UpdateChunkCollisionStates();
    }

    if (!CanEditWorldNow() || bGenerationInProgress || bIsApplyingGeneratedMeshes)
    {
        return;
    }

    if (!bIsRemovingDensity && !bIsAddingDensity)
    {
        TimeSinceLastEdit = EditInterval;
        return;
    }

    if (
        bAsyncDensityEditInProgress ||
        bAsyncRebuildInProgress ||
        PendingDirtyChunks.Num() > 0 ||
        PendingDensityRebuildMeshCoords.Num() > 0 ||
        PendingDensityRebuildMeshData.Num() > 0
        )
    {
        return;
    }

    TimeSinceLastEdit += DeltaSeconds;

    if (TimeSinceLastEdit < EditInterval)
    {
        return;
    }

    TimeSinceLastEdit = 0.0f;

    if (bIsRemovingDensity)
    {
        PrimaryEditAtCameraTrace();
    }

    if (bIsAddingDensity)
    {
        SecondaryEditAtCameraTrace();
    }
}

void ATerraforgeWorldActor::EndPlay(
    const EEndPlayReason::Type EndPlayReason
)
{
    CancelPendingAsyncWorldWork();

    const bool bShouldAutoSave =
        PersistenceMode == ETerraforgeWorldPersistenceMode::AutoSaveOnly ||
        PersistenceMode == ETerraforgeWorldPersistenceMode::AutoLoadAndSave;

    if (bShouldAutoSave)
    {
        const bool bShouldWriteSave =
            !bAutoSaveOnlyWhenModified ||
            HasModifiedWorld();

        if (bShouldWriteSave)
        {
            UE_LOG(
                LogTerraforgeVoxel,
                Warning,
                TEXT("Terraforge persistence auto-saving world on EndPlay. Mode=%d Slot=%s Modified=%s"),
                static_cast<int32>(PersistenceMode),
                *DefaultSaveSlotName,
                HasModifiedWorld() ? TEXT("true") : TEXT("false")
            );

            SaveDefaultWorld();
        }
        else
        {
            UE_LOG(
                LogTerraforgeVoxel,
                Display,
                TEXT("Skipped Terraforge persistence auto-save. Mode=%d Modified=%s"),
                static_cast<int32>(PersistenceMode),
                HasModifiedWorld() ? TEXT("true") : TEXT("false")
            );
        }
    }

    DestroyAllChunkComponents();

    Super::EndPlay(EndPlayReason);
}
bool ATerraforgeWorldActor::IsWorldReady() const
{
    return bWorldReady;
}

bool ATerraforgeWorldActor::IsInitialTerrainReady() const
{
    return bInitialTerrainReady;
}

bool ATerraforgeWorldActor::GetSuggestedPlayerSpawnLocation(
    const FVector DesiredWorldLocation,
    const float SearchRadius,
    FVector& OutSpawnLocation
) const
{
    return FindSafeSpawnLocation(
        DesiredWorldLocation,
        SearchRadius,
        OutSpawnLocation
    );
}

void ATerraforgeWorldActor::GenerateWorld()
{
    if (bGenerationInProgress || bIsApplyingGeneratedMeshes)
    {
        return;
    }

    bIsEndingPlay = false;

    MarkGenerationStarted();

    ClearWorld();

    PendingStreamingChunkCoords.Reset();
    PendingStreamingChunkCoordSet.Reset();
    PendingStreamingChunkQueueHead = 0;
    PendingStreamingMeshData.Reset();
    PendingStreamingMeshCoords.Reset();
    PendingStreamingMeshCoordSet.Reset();
    PendingStreamingMeshApplyIndex = 0;

    AsyncStreamingChunkCoordSet.Reset();
    ActiveAsyncStreamingChunkTasks = 0;
    ++ActiveStreamingGenerationId;

    GeneratedEmptyStreamingChunks.Reset();
    ProcessedEmptyStreamingChunks.Reset();
    bRuntimeStreamingInitialized = false;
    bCachedStreamingChunkSetsValid = false;
    CachedDesiredStreamingChunkCoords.Reset();

    if (ShouldUseRuntimeStreaming())
    {
        bPendingInitialStreamingPlayerPlacement = true;

        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Starting Terraforge runtime streaming. TerrainSystem=%d ViewDistance=%d ChunksPerFrame=%d InitialChunksPerFrame=%d"),
            static_cast<int32>(TerrainSystem),
            ViewDistanceInChunks,
            StreamingChunksGeneratedPerFrame,
            InitialStreamingChunksGeneratedPerFrame 
        );

        UpdateRuntimeStreaming();
        return;
    }

    switch (TerrainSystem)
    {
    case ETerraforgeTerrainSystem::Block:
    {
        GenerateBlockWorld();
        UpdateChunkCollisionStates();
        MarkGenerationComplete();
        break;
    }

    case ETerraforgeTerrainSystem::SmoothDensity:
    default:
    {
        GenerateSmoothDensityWorld();
        break;
    }
    }
}

float ATerraforgeWorldActor::GetGenerationProgress() const
{
    return GenerationProgress;
}

void ATerraforgeWorldActor::ClearWorld()
{
    ReleaseAllChunkComponents();

    DensityChunkDataByCoord.Reset();
    bDensityChunkBoundsDirty = true;
    BlockChunkDataByCoord.Reset();

    ModifiedDensityChunks.Reset();
    ModifiedDensityVoxelIndicesByChunk.Reset();

    ModifiedBlockChunks.Reset();
    ModifiedBlockVoxelIndicesByChunk.Reset();
    BlockVoxelOverridesByChunk.Reset();

    PendingGeneratedMeshData.Reset();
    PendingGeneratedMeshCoords.Reset();
    PendingGeneratedMeshApplyIndex = 0;

    GeneratedEmptyStreamingChunks.Reset();
    ProcessedEmptyStreamingChunks.Reset();

    PendingStreamingMeshData.Reset();
    PendingStreamingMeshCoords.Reset();
    PendingStreamingMeshCoordSet.Reset();
    PendingStreamingMeshApplyIndex = 0;
    PendingStreamingChunkCoords.Reset();
    PendingStreamingChunkCoordSet.Reset();
    PendingStreamingChunkQueueHead = 0;

    AsyncStreamingChunkCoordSet.Reset();
    ActiveAsyncStreamingChunkTasks = 0;
    ++ActiveStreamingGenerationId;

    bIsApplyingGeneratedMeshes = false;
    bInitialTerrainReady = false;

    bAsyncDensityEditInProgress = false;
    ++ActiveAsyncDensityEditId;

    TotalMeshesToApply = 0;
    AppliedMeshes = 0;

    bWorldReady = false;
    GenerationProgress = 0.0f;
    bCachedStreamingChunkSetsValid = false;
    CachedDesiredStreamingChunkCoords.Reset();
}

void ATerraforgeWorldActor::SetGenerationProgress(
    const float NewProgress
)
{
    const float ClampedProgress =
        FMath::Clamp(NewProgress, 0.0f, 1.0f);

    if (FMath::IsNearlyEqual(GenerationProgress, ClampedProgress, 0.001f))
    {
        return;
    }

    GenerationProgress = ClampedProgress;
    OnGenerationProgress.Broadcast(GenerationProgress);
}

void ATerraforgeWorldActor::MarkGenerationStarted()
{
    bWorldReady = false;
    GenerationProgress = 0.0f;
    TotalMeshesToApply = 0;
    AppliedMeshes = 0;
    bInitialTerrainReady = false;

    OnGenerationStarted.Broadcast();
    OnGenerationProgress.Broadcast(GenerationProgress);

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Terraforge world generation started.")
    );
}

void ATerraforgeWorldActor::MarkGenerationComplete()
{
    bWorldReady = true;
    GenerationProgress = 1.0f;

    OnGenerationProgress.Broadcast(GenerationProgress);
    OnGenerationComplete.Broadcast();
    
    BroadcastInitialTerrainReady();

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Terraforge world generation complete.")
    );
}

bool ATerraforgeWorldActor::CanEditWorldNow() const
{
    if (bIsEndingPlay)
    {
        return false;
    }

    if (bWorldReady)
    {
        return true;
    }

    if (
        ShouldUseRuntimeStreaming() &&
        ActiveChunkComponentsByCoord.Num() > 0
        )
    {
        return true;
    }

    return false;
}

void ATerraforgeWorldActor::CancelPendingAsyncWorldWork()
{
    bIsEndingPlay = true;

    ++ActiveGenerationId;
    ++ActiveAsyncRebuildId;
    ++ActiveAsyncDensityEditId;
    bAsyncDensityEditInProgress = false;

    bGenerationInProgress = false;
    bIsApplyingGeneratedMeshes = false;
    bAsyncRebuildInProgress = false;

    PendingGeneratedMeshData.Reset();
    PendingGeneratedMeshCoords.Reset();
    PendingGeneratedMeshApplyIndex = 0;

    PendingDensityRebuildMeshData.Reset();
    PendingDensityRebuildMeshCoords.Reset();
    PendingDensityRebuildMeshApplyIndex = 0;

    PendingDirtyChunks.Reset();

    PendingStreamingChunkCoords.Reset();
    PendingStreamingChunkCoordSet.Reset();
    PendingStreamingChunkQueueHead = 0;
    PendingStreamingMeshData.Reset();
    PendingStreamingMeshCoords.Reset();
    PendingStreamingMeshCoordSet.Reset();
    PendingStreamingMeshApplyIndex = 0;
    bCachedStreamingChunkSetsValid = false;
    CachedDesiredStreamingChunkCoords.Reset();
}

void ATerraforgeWorldActor::GenerateBlockWorld()
{
    BlockChunkDataByCoord.Reset();

    const int32 SafeRadiusInChunks =
        FMath::Max(0, RadiusInChunks);

    int32 EffectiveBlockMinChunkZ = BlockMinChunkZ;
    int32 EffectiveBlockMaxChunkZ = BlockMaxChunkZ;

    if (EffectiveBlockMinChunkZ > EffectiveBlockMaxChunkZ)
    {
        Swap(EffectiveBlockMinChunkZ, EffectiveBlockMaxChunkZ);
    }

    for (int32 Z = EffectiveBlockMinChunkZ; Z <= EffectiveBlockMaxChunkZ; ++Z)
    {
        for (int32 Y = -SafeRadiusInChunks; Y <= SafeRadiusInChunks; ++Y)
        {
            for (int32 X = -SafeRadiusInChunks; X <= SafeRadiusInChunks; ++X)
            {
                const FIntVector ChunkCoord(X, Y, Z);

                FTerraforgeVoxelChunkData ChunkData(ChunkCoord);

                GenerateBlockChunkData(ChunkData);

                if (ChunkData.HasAnySolidVoxel())
                {
                    BlockChunkDataByCoord.Add(
                        ChunkCoord,
                        MoveTemp(ChunkData)
                    );
                }
            }
        }
    }

    for (const TPair<FIntVector, FTerraforgeVoxelChunkData>& Pair : BlockChunkDataByCoord)
    {
        const FIntVector ChunkCoord = Pair.Key;
        const FTerraforgeVoxelChunkData& ChunkData = Pair.Value;

        FTerraforgeVoxelMeshData MeshData;

        FTerraforgeVoxelMesher::GenerateGreedyMeshNeighbourAware(
            ChunkData,
            [this](const FIntVector& WorldVoxelCoord) -> uint16
            {
                return GetBlockMaterialAtWorldVoxel(WorldVoxelCoord);
            },
            MeshData
        );

        ApplyMeshToChunk(
            ChunkCoord,
            MeshData
        );
    }

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Generated Terraforge block world. DataChunks=%d ActiveComponents=%d"),
        BlockChunkDataByCoord.Num(),
        ActiveChunkComponentsByCoord.Num()
    );
}

void ATerraforgeWorldActor::GenerateBlockChunkData(
    FTerraforgeVoxelChunkData& ChunkData
) const
{
    for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
    {
        for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
        {
            for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
            {
                const FIntVector WorldVoxel =
                    ChunkData.LocalToWorldVoxel(X, Y, Z);

                const FVector WorldVoxelPosition(
                    static_cast<double>(WorldVoxel.X),
                    static_cast<double>(WorldVoxel.Y),
                    static_cast<double>(WorldVoxel.Z)
                );

                const FTerraforgeTerrainSample Sample =
                    SampleTerrainAtWorldVoxelPosition(WorldVoxelPosition);

                FTerraforgeVoxel Voxel;
                Voxel.MaterialId =
                    Sample.Density > 0.0f
                    ? static_cast<uint16>(FMath::Clamp(Sample.SolidMaterialId, 1, 65535))
                    : 0;

                ChunkData.SetVoxel(
                    X,
                    Y,
                    Z,
                    Voxel
                );
            }
        }
    }
}

uint16 ATerraforgeWorldActor::GetBlockMaterialAtWorldVoxel(
    const FIntVector& WorldVoxelCoord
) const
{
    const FIntVector ChunkCoord =
        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

    const FIntVector LocalCoord =
        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

    const FTerraforgeVoxelChunkData* ChunkData =
        BlockChunkDataByCoord.Find(ChunkCoord);

    if (ChunkData == nullptr)
    {
        return 0;
    }

    return ChunkData->GetVoxel(
        LocalCoord.X,
        LocalCoord.Y,
        LocalCoord.Z
    ).MaterialId;
}

void ATerraforgeWorldActor::GenerateSmoothDensityWorld()
{
    GenerateSmoothDensityWorldAsync();
}

bool ATerraforgeWorldActor::ShouldUseRuntimeStreaming() const
{
    const UWorld* World = GetWorld();

    return
        bEnableRuntimeStreaming &&
        World != nullptr &&
        World->IsGameWorld();
}

void ATerraforgeWorldActor::GenerateSmoothDensityWorldAsync()
{
    if (bGenerationInProgress)
    {
        return;
    }

    bGenerationInProgress = true;
    ++ActiveGenerationId;

    const int32 GenerationId = ActiveGenerationId;

    DensityChunkDataByCoord.Reset();
    bDensityChunkBoundsDirty = true;

    PendingGeneratedMeshData.Reset();
    PendingGeneratedMeshCoords.Reset();
    PendingGeneratedMeshApplyIndex = 0;
    bIsApplyingGeneratedMeshes = false;

    const int32 CapturedRadiusInChunks =
        FMath::Max(0, RadiusInChunks);

    int32 CapturedMinChunkZ = DensityMinChunkZ;
    int32 CapturedMaxChunkZ = DensityMaxChunkZ;

    if (CapturedMinChunkZ > CapturedMaxChunkZ)
    {
        Swap(CapturedMinChunkZ, CapturedMaxChunkZ);
    }

    const int32 CapturedDensityMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    Async(
        EAsyncExecution::ThreadPool,
        [
            this,
            GenerationId,
            CapturedRadiusInChunks,
            CapturedMinChunkZ,
            CapturedMaxChunkZ,
            CapturedDensityMeshStep
        ]() mutable
        {
            TMap<FIntVector, FTerraforgeDensityChunkData> GeneratedDensityData;
            TMap<FIntVector, FTerraforgeVoxelMeshData> GeneratedMeshData;

            for (int32 Z = CapturedMinChunkZ; Z <= CapturedMaxChunkZ; ++Z)
            {
                for (int32 Y = -CapturedRadiusInChunks; Y <= CapturedRadiusInChunks; ++Y)
                {
                    for (int32 X = -CapturedRadiusInChunks; X <= CapturedRadiusInChunks; ++X)
                    {
                        const FIntVector ChunkCoord(X, Y, Z);

                        FTerraforgeDensityChunkData DensityChunkData(ChunkCoord);

                        FillDensityChunkFromTerrainSampler(DensityChunkData);

                        if (DensityChunkData.HasAnySolidVoxel())
                        {
                            GeneratedDensityData.Add(
                                ChunkCoord,
                                MoveTemp(DensityChunkData)
                            );
                        }
                    }
                }
            }

            auto DensityLookup =
                [&GeneratedDensityData, this](const FIntVector& WorldVoxelCoord) -> float
                {
                    const FIntVector ChunkCoord =
                        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                    const FIntVector LocalCoord =
                        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                    const FTerraforgeDensityChunkData* DensityChunkData =
                        GeneratedDensityData.Find(ChunkCoord);

                    if (DensityChunkData == nullptr)
                    {
                        const FTerraforgeTerrainSample Sample =
                            SampleTerrainAtWorldVoxelPosition(
                                FVector(
                                    static_cast<double>(WorldVoxelCoord.X),
                                    static_cast<double>(WorldVoxelCoord.Y),
                                    static_cast<double>(WorldVoxelCoord.Z)
                                )
                            );

                        return Sample.Density;
                    }

                    return DensityChunkData->GetVoxel(
                        LocalCoord.X,
                        LocalCoord.Y,
                        LocalCoord.Z
                    ).Density;
                };

            auto MaterialLookup =
                [&GeneratedDensityData, this](const FIntVector& WorldVoxelCoord) -> uint16
                {
                    const FIntVector ChunkCoord =
                        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                    const FIntVector LocalCoord =
                        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                    const FTerraforgeDensityChunkData* DensityChunkData =
                        GeneratedDensityData.Find(ChunkCoord);

                    if (DensityChunkData == nullptr)
                    {
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
                    }

                    return DensityChunkData->GetVoxel(
                        LocalCoord.X,
                        LocalCoord.Y,
                        LocalCoord.Z
                    ).MaterialId;
                };

            for (const TPair<FIntVector, FTerraforgeDensityChunkData>& Pair : GeneratedDensityData)
            {
                const FIntVector ChunkCoord = Pair.Key;

                FTerraforgeVoxelMeshData MeshData;

                FTerraforgeMarchingCubes::GenerateMesh(
                    ChunkCoord,
                    DensityLookup,
                    MaterialLookup,
                    MeshData,
                    CapturedDensityMeshStep,
                    bSmoothNormalsAcrossMaterialBoundaries
                );

                if (!MeshData.IsEmpty())
                {
                    GeneratedMeshData.Add(
                        ChunkCoord,
                        MoveTemp(MeshData)
                    );
                }
            }

            AsyncTask(
                ENamedThreads::GameThread,
                [
                    this,
                    GenerationId,
                    GeneratedDensityData = MoveTemp(GeneratedDensityData),
                    GeneratedMeshData = MoveTemp(GeneratedMeshData)
                ]() mutable
                {
                    ApplyGeneratedSmoothDensityWorld(
                        GenerationId,
                        MoveTemp(GeneratedDensityData),
                        MoveTemp(GeneratedMeshData)
                    );
                }
            );
        }
    );
}

void ATerraforgeWorldActor::ApplyGeneratedSmoothDensityWorld(
    const int32 GenerationId,
    TMap<FIntVector, FTerraforgeDensityChunkData> GeneratedDensityData,
    TMap<FIntVector, FTerraforgeVoxelMeshData> GeneratedMeshData
)
{
    if (bIsEndingPlay || GenerationId != ActiveGenerationId)
    {
        return;
    }

    bGenerationInProgress = false;

    DensityChunkDataByCoord = MoveTemp(GeneratedDensityData);
    bDensityChunkBoundsDirty = true;

    BeginBatchedGeneratedMeshApply(
        MoveTemp(GeneratedMeshData)
    );

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Queued Terraforge smooth density world apply. DataChunks=%d MeshChunks=%d"),
        DensityChunkDataByCoord.Num(),
        PendingGeneratedMeshCoords.Num()
    );
}

void ATerraforgeWorldActor::BeginBatchedGeneratedMeshApply(
    TMap<FIntVector, FTerraforgeVoxelMeshData> GeneratedMeshData
)
{
    PendingGeneratedMeshData = MoveTemp(GeneratedMeshData);

    PendingGeneratedMeshCoords.Reset();
    PendingGeneratedMeshData.GetKeys(PendingGeneratedMeshCoords);

    PendingGeneratedMeshApplyIndex = 0;
    bIsApplyingGeneratedMeshes = PendingGeneratedMeshCoords.Num() > 0;
    TotalMeshesToApply = PendingGeneratedMeshCoords.Num();
    AppliedMeshes = 0;
    SetGenerationProgress(0.05f);

    if (!bIsApplyingGeneratedMeshes)
    {
        UpdateChunkCollisionStates();
        MarkGenerationComplete();

        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Terraforge world ready. No meshes to apply.")
        );

        return;
    }
}

void ATerraforgeWorldActor::TickBatchedGeneratedMeshApply()
{
    if (!bIsApplyingGeneratedMeshes)
    {
        return;
    }

    int32 AppliedThisFrame = 0;

    while (
        PendingGeneratedMeshApplyIndex < PendingGeneratedMeshCoords.Num() &&
        AppliedThisFrame < MeshAppliesPerFrame
        )
    {
        const FIntVector ChunkCoord =
            PendingGeneratedMeshCoords[PendingGeneratedMeshApplyIndex];

        ++PendingGeneratedMeshApplyIndex;
        ++AppliedThisFrame;

        FTerraforgeVoxelMeshData* MeshData =
            PendingGeneratedMeshData.Find(ChunkCoord);

        if (MeshData == nullptr || MeshData->IsEmpty())
        {
            continue;
        }

        ApplySingleGeneratedMesh(
            ChunkCoord,
            *MeshData
        );

        ++AppliedMeshes;

        if (TotalMeshesToApply > 0)
        {
            const float ApplyProgress =
                static_cast<float>(AppliedMeshes) /
                static_cast<float>(TotalMeshesToApply);

            SetGenerationProgress(
                FMath::Lerp(0.05f, 1.0f, ApplyProgress)
            );
        }
    }

    if (PendingGeneratedMeshApplyIndex >= PendingGeneratedMeshCoords.Num())
    {
        PendingGeneratedMeshData.Reset();
        PendingGeneratedMeshCoords.Reset();
        PendingGeneratedMeshApplyIndex = 0;
        bIsApplyingGeneratedMeshes = false;

        UpdateChunkCollisionStates();
        MarkGenerationComplete();

        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Terraforge world ready. ActiveComponents=%d"),
            ActiveChunkComponentsByCoord.Num()
        );
    }
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

void ATerraforgeWorldActor::ApplySingleGeneratedMesh(
    const FIntVector& ChunkCoord,
    FTerraforgeVoxelMeshData& MeshData
)
{
    ApplyMeshToChunk(
        ChunkCoord,
        MeshData
    );
}

float ATerraforgeWorldActor::GetDensityAtWorldVoxelPosition(
    const FVector& WorldVoxelPosition
) const
{
    return SampleTerrainAtWorldVoxelPosition(
        WorldVoxelPosition
    ).Density;
}

float ATerraforgeWorldActor::GetStoredDensityAtWorldVoxel(
    const FIntVector& WorldVoxelCoord
) const
{
    const FIntVector ChunkCoord =
        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

    const FIntVector LocalCoord =
        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

    const FTerraforgeDensityChunkData* DensityChunkData =
        DensityChunkDataByCoord.Find(ChunkCoord);

    if (DensityChunkData == nullptr)
    {
        return GetDensityAtWorldVoxelPosition(
            FVector(
                static_cast<double>(WorldVoxelCoord.X),
                static_cast<double>(WorldVoxelCoord.Y),
                static_cast<double>(WorldVoxelCoord.Z)
            )
        );
    }

    return DensityChunkData->GetVoxel(
        LocalCoord.X,
        LocalCoord.Y,
        LocalCoord.Z
    ).Density;
}

FTerraforgeTerrainSample ATerraforgeWorldActor::SampleTerrainAtWorldVoxelPosition(
    const FVector& WorldVoxelPosition
) const
{
    FTerraforgeTerrainSample Sample;

    const FTerraforgeTerrainGenerationProfile& Profile =
        GetActiveTerrainGenerationProfile();

    const double WX = WorldVoxelPosition.X;
    const double WY = WorldVoxelPosition.Y;
    const double WZ = WorldVoxelPosition.Z;

    double EdgeT = 0.0;
    double EdgeFalloff = 1.0;

    if (Profile.bUseWorldEdgeFalloff)
    {
        const double Distance2D =
            FMath::Sqrt(WX * WX + WY * WY);

        const double SafeWorldEdgeRadius =
            FMath::Max(1.0, static_cast<double>(Profile.WorldEdgeRadius));

        EdgeT =
            FMath::Clamp(Distance2D / SafeWorldEdgeRadius, 0.0, 1.0);

        const double SmoothEdge =
            EdgeT * EdgeT * (3.0 - 2.0 * EdgeT);

        EdgeFalloff =
            1.0 - SmoothEdge;
    }

    const double BaseHeight =
        static_cast<double>(Profile.BaseHeight);

    const double MainHeight =
        static_cast<double>(Profile.HeightScale) * EdgeFalloff;

    const double Continental =
        FMath::Sin(WX * 0.0065 + WY * 0.0027) * 18.0 +
        FMath::Cos(WY * 0.0058 - WX * 0.0021) * 16.0 +
        FMath::Sin((WX + WY) * 0.0042) * 12.0;

    const double Hills =
        FMath::Sin(WX * 0.018 + WY * 0.011) * 10.0 +
        FMath::Cos(WY * 0.021 - WX * 0.009) * 9.0 +
        FMath::Sin((WX - WY) * 0.016) * 7.0;

    const double Detail =
        FMath::Sin(WX * 0.055 + WY * 0.037) * 3.5 +
        FMath::Cos(WY * 0.061 - WX * 0.024) * 3.0;

    const double RidgeBase =
        FMath::Abs(
            FMath::Sin(WX * 0.013 + WY * 0.019) +
            FMath::Cos(WX * 0.017 - WY * 0.011)
        );

    const double Ridges =
        FMath::Pow(
            FMath::Clamp(RidgeBase * 0.5, 0.0, 1.0),
            2.0
        ) * 24.0;

    const double EdgeDrop =
        Profile.bUseWorldEdgeFalloff
        ? FMath::Pow(EdgeT, 3.0) * 140.0
        : 0.0;

    const double ValleyMask =
        FMath::Clamp(
            FMath::Sin(WX * 0.007) * 0.5 +
            FMath::Cos(WY * 0.006) * 0.5,
            -1.0,
            1.0
        );

    const double ValleyCut =
        FMath::Max(0.0, ValleyMask) * 18.0 * EdgeFalloff;

    double SurfaceHeight =
        BaseHeight +
        MainHeight +
        Continental * EdgeFalloff +
        Hills * EdgeFalloff +
        Detail * EdgeFalloff +
        Ridges * EdgeFalloff -
        EdgeDrop -
        ValleyCut;

    if (Profile.bUseWorldEdgeFalloff && EdgeT > 0.78)
    {
        const double OuterT =
            FMath::Clamp((EdgeT - 0.78) / 0.22, 0.0, 1.0);

        const double OuterSmooth =
            OuterT * OuterT * (3.0 - 2.0 * OuterT);

        SurfaceHeight =
            FMath::Lerp(
                SurfaceHeight,
                static_cast<double>(Profile.WorldEdgeTargetHeight),
                OuterSmooth
            );
    }

    double Density =
        SurfaceHeight - WZ;

    double CaveAmount = 0.0;

    const bool bCanCarveCaves =
        Profile.CaveStrength > 0.0f &&
        WZ < SurfaceHeight - static_cast<double>(Profile.CaveStartDepth
            );

    if (bCanCarveCaves)
    {
        const double Frequency =
            FMath::Max(0.0001, static_cast<double>(Profile.CaveFrequency));

        const double CaveNoiseA =
            FMath::Sin(WX * Frequency + WY * Frequency * 0.37 + WZ * Frequency * 1.71);

        const double CaveNoiseB =
            FMath::Cos(WY * Frequency * 1.23 - WZ * Frequency * 0.89 + WX * Frequency * 0.53);

        const double CaveNoiseC =
            FMath::Sin((WX + WY - WZ) * Frequency * 0.61);

        const double CombinedCaveNoise =
            (CaveNoiseA + CaveNoiseB + CaveNoiseC) / 3.0;

        CaveAmount =
            FMath::Max(0.0, CombinedCaveNoise) *
            static_cast<double>(Profile.CaveStrength);

        Density -= CaveAmount;
    }

    Sample.SurfaceHeight =
        static_cast<float>(SurfaceHeight);

    Sample.CaveAmount =
        static_cast<float>(CaveAmount);

    Sample.Density =
        static_cast<float>(Density);

    Sample.SolidMaterialId =
        GetTerrainSolidMaterialAtWorldVoxelPosition(
            WorldVoxelPosition,
            Sample.Density,
            Sample.SurfaceHeight
        );

    Sample.LiquidMaterialId = 0;
    Sample.BiomeId = GetActiveBiomeId();
    Sample.bIsLiquid = false;

    return Sample;
}

uint16 ATerraforgeWorldActor::GetTerrainSolidMaterialAtWorldVoxelPosition(
    const FVector& WorldVoxelPosition,
    const float Density,
    const float SurfaceHeight
) const
{
    const FTerraforgeTerrainGenerationProfile& Profile =
        GetActiveTerrainGenerationProfile();

    if (Density <= 0.0f)
    {
        return 0;
    }

    const float DepthBelowSurface =
        SurfaceHeight - static_cast<float>(WorldVoxelPosition.Z);

    if (DepthBelowSurface <= 3.0f)
    {
        return static_cast<uint16>(
            FMath::Clamp(Profile.SurfaceMaterialId, 1, 65535)
            );
    }

    if (DepthBelowSurface <= 18.0f)
    {
        return static_cast<uint16>(
            FMath::Clamp(Profile.SubsurfaceMaterialId, 1, 65535)
            );
    }

    return static_cast<uint16>(
        FMath::Clamp(Profile.StoneMaterialId, 1, 65535)
        );
}

uint16 ATerraforgeWorldActor::GetStoredDensityMaterialAtWorldVoxel(
    const FIntVector& WorldVoxelCoord
) const
{
    const FIntVector ChunkCoord =
        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

    const FIntVector LocalCoord =
        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

    const FTerraforgeDensityChunkData* DensityChunkData =
        DensityChunkDataByCoord.Find(ChunkCoord);

    if (DensityChunkData == nullptr)
    {
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
    }

    return DensityChunkData->GetVoxel(
        LocalCoord.X,
        LocalCoord.Y,
        LocalCoord.Z
    ).MaterialId;
}

void ATerraforgeWorldActor::FillDensityChunkFromTerrainSampler(
    FTerraforgeDensityChunkData& DensityChunkData
) const
{
    for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
    {
        for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
        {
            for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
            {
                const FIntVector WorldVoxel =
                    DensityChunkData.LocalToWorldVoxel(X, Y, Z);

                const FVector WorldVoxelPosition(
                    static_cast<double>(WorldVoxel.X),
                    static_cast<double>(WorldVoxel.Y),
                    static_cast<double>(WorldVoxel.Z)
                );

                const FTerraforgeTerrainSample Sample =
                    SampleTerrainAtWorldVoxelPosition(WorldVoxelPosition);

                FTerraforgeDensityVoxel Voxel;
                Voxel.Density = Sample.Density;
                Voxel.MaterialId =
                    Sample.Density > 0.0f
                    ? static_cast<uint16>(FMath::Clamp(Sample.SolidMaterialId, 1, 65535))
                    : 0;

                DensityChunkData.SetVoxel(
                    X,
                    Y,
                    Z,
                    Voxel
                );
            }
        }
    }
}

void ATerraforgeWorldActor::ApplyMeshToChunk(
    const FIntVector& ChunkCoord,
    FTerraforgeVoxelMeshData& MeshData
)
{
    if (MeshData.IsEmpty())
    {
        return;
    }

    const bool bShouldBuildCollisionGeometry =
        bGenerateCollision &&
        CollisionMode != ETerraforgeWorldCollisionMode::None;

    UTerraforgeVoxelChunkComponent* ChunkComponent =
        AcquireChunkComponent(
            ChunkCoord,
            bShouldBuildCollisionGeometry
        );

    if (ChunkComponent == nullptr)
    {
        return;
    }

    ChunkComponent->ApplyMesh(MeshData);

    ChunkComponent->SetCollisionEnabled(
        ShouldChunkGenerateCollisionInitially(ChunkCoord)
        ? ECollisionEnabled::QueryAndPhysics
        : ECollisionEnabled::NoCollision
    );

    if (WorldMaterial != nullptr)
    {
        ChunkComponent->SetMaterial(0, WorldMaterial);
    }
}

UTerraforgeVoxelChunkComponent* ATerraforgeWorldActor::AcquireChunkComponent(
    const FIntVector& ChunkCoord,
    const bool bInGenerateCollision
)
{
    TObjectPtr<UTerraforgeVoxelChunkComponent>* ExistingComponentPtr =
        ActiveChunkComponentsByCoord.Find(ChunkCoord);

    if (
        ExistingComponentPtr != nullptr &&
        IsValid(ExistingComponentPtr->Get())
        )
    {
        return ExistingComponentPtr->Get();
    }

    UTerraforgeVoxelChunkComponent* ChunkComponent = nullptr;

    while (PooledChunkComponents.Num() > 0 && ChunkComponent == nullptr)
    {
        TObjectPtr<UTerraforgeVoxelChunkComponent> Candidate =
            PooledChunkComponents.Pop(EAllowShrinking::No);

        if (IsValid(Candidate.Get()))
        {
            ChunkComponent = Candidate.Get();
        }
    }

    if (ChunkComponent == nullptr)
    {
        const FName ComponentName = MakeUniqueObjectName(
            this,
            UTerraforgeVoxelChunkComponent::StaticClass(),
            *FString::Printf(
                TEXT("TerraforgeWorldChunk_%d_%d_%d"),
                ChunkCoord.X,
                ChunkCoord.Y,
                ChunkCoord.Z
            )
        );

        ChunkComponent =
            NewObject<UTerraforgeVoxelChunkComponent>(
                this,
                UTerraforgeVoxelChunkComponent::StaticClass(),
                ComponentName
            );

        if (ChunkComponent == nullptr)
        {
            return nullptr;
        }

        ChunkComponent->SetupAttachment(SceneRoot);
        AddInstanceComponent(ChunkComponent);
        ChunkComponent->RegisterComponent();
    }

    ChunkComponent->SetVisibility(true, true);
    ChunkComponent->SetHiddenInGame(false);
    ChunkComponent->Activate(true);

    ChunkComponent->InitializeChunk(
        ChunkCoord,
        bInGenerateCollision,
        GetBaseVoxelSize()
    );

    ActiveChunkComponentsByCoord.Add(
        ChunkCoord,
        ChunkComponent
    );

    return ChunkComponent;
}

void ATerraforgeWorldActor::ReleaseAllChunkComponents()
{
    TArray<UTerraforgeVoxelChunkComponent*> ExistingChunkComponents;
    GetComponents<UTerraforgeVoxelChunkComponent>(ExistingChunkComponents);

    ActiveChunkComponentsByCoord.Reset();

    for (UTerraforgeVoxelChunkComponent* ChunkComponent : ExistingChunkComponents)
    {
        if (!IsValid(ChunkComponent))
        {
            continue;
        }

        ChunkComponent->ClearAllMeshSections();
        ChunkComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        ChunkComponent->SetVisibility(false, true);
        ChunkComponent->SetHiddenInGame(true);
        ChunkComponent->Deactivate();

        PooledChunkComponents.Add(ChunkComponent);
    }
}

void ATerraforgeWorldActor::DestroyAllChunkComponents()
{
    TArray<UTerraforgeVoxelChunkComponent*> ExistingChunkComponents;
    GetComponents<UTerraforgeVoxelChunkComponent>(ExistingChunkComponents);

    for (UTerraforgeVoxelChunkComponent* ChunkComponent : ExistingChunkComponents)
    {
        if (!IsValid(ChunkComponent))
        {
            continue;
        }

        ChunkComponent->ClearAllMeshSections();
        ChunkComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        ChunkComponent->DestroyComponent();
    }

    ActiveChunkComponentsByCoord.Reset();
    PooledChunkComponents.Reset();
}

bool ATerraforgeWorldActor::FindSafeSpawnLocation(
    const FVector DesiredWorldLocation,
    const float SearchRadius,
    FVector& OutSpawnLocation
) const
{
    if (!bWorldReady)
    {
        return false;
    }

    const double Step =
        static_cast<double>(GetBaseVoxelSize());

    const int32 Rings =
        FMath::Max(0, FMath::CeilToInt(SearchRadius / Step));

    auto TryLocation =
        [this, &OutSpawnLocation](const FVector& CandidateWorldLocation) -> bool
        {
            double SurfaceWorldZ = 0.0;

            if (!FindTerrainSpawnSurfaceAtWorldXY(CandidateWorldLocation, SurfaceWorldZ))
            {
                return false;
            }

            OutSpawnLocation = CandidateWorldLocation;

            const double SpawnClearance =
                TerrainSystem == ETerraforgeTerrainSystem::Block
                ? 220.0
                : 120.0;

            OutSpawnLocation.Z = SurfaceWorldZ + SpawnClearance;

            return true;
        };

    if (TryLocation(DesiredWorldLocation))
    {
        return true;
    }

    for (int32 Ring = 1; Ring <= Rings; ++Ring)
    {
        const double Radius =
            static_cast<double>(Ring) * Step;

        const int32 Samples =
            FMath::Max(8, Ring * 8);

        for (int32 SampleIndex = 0; SampleIndex < Samples; ++SampleIndex)
        {
            const double Angle =
                (static_cast<double>(SampleIndex) / static_cast<double>(Samples)) *
                UE_DOUBLE_TWO_PI;

            FVector Candidate = DesiredWorldLocation;
            Candidate.X += FMath::Cos(Angle) * Radius;
            Candidate.Y += FMath::Sin(Angle) * Radius;

            if (TryLocation(Candidate))
            {
                return true;
            }
        }
    }

    return false;
}

bool ATerraforgeWorldActor::FindDensitySurfaceAtWorldXY(
    const FVector WorldLocation,
    double& OutSurfaceWorldZ
) const
{
    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    const int32 WorldVoxelX =
        FMath::FloorToInt(LocalLocation.X / SafeVoxelSize);

    const int32 WorldVoxelY =
        FMath::FloorToInt(LocalLocation.Y / SafeVoxelSize);

    RefreshCachedDensityChunkBounds();

    if (CachedDensityMinWorldZ > CachedDensityMaxWorldZ)
    {
        return false;
    }

    float PreviousDensity =
        GetStoredDensityAtWorldVoxel(
            FIntVector(WorldVoxelX, WorldVoxelY, CachedDensityMaxWorldZ)
        );

    for (int32 Z = CachedDensityMaxWorldZ - 1; Z >= CachedDensityMinWorldZ; --Z)
    {
        const FIntVector CurrentVoxel(
            WorldVoxelX,
            WorldVoxelY,
            Z
        );

        const float CurrentDensity =
            GetStoredDensityAtWorldVoxel(CurrentVoxel);

        if (PreviousDensity <= 0.0f && CurrentDensity > 0.0f)
        {
            const double Alpha =
                PreviousDensity / (PreviousDensity - CurrentDensity);

            const double SurfaceVoxelZ =
                static_cast<double>(Z + 1) -
                FMath::Clamp(Alpha, 0.0, 1.0);

            const FVector SurfaceLocalPosition(
                static_cast<double>(WorldVoxelX) * SafeVoxelSize,
                static_cast<double>(WorldVoxelY) * SafeVoxelSize,
                SurfaceVoxelZ * SafeVoxelSize
            );

            const FVector SurfaceWorldPosition =
                GetActorTransform().TransformPosition(SurfaceLocalPosition);

            OutSurfaceWorldZ = SurfaceWorldPosition.Z;
            return true;
        }

        PreviousDensity = CurrentDensity;
    }

    return false;
}

bool ATerraforgeWorldActor::GetCameraTraceHit(
    FHitResult& OutHit,
    const float TraceDistance
) const
{
    UWorld* World = GetWorld();

    if (World == nullptr)
    {
        return false;
    }

    APlayerController* PlayerController =
        UGameplayStatics::GetPlayerController(World, 0);

    if (PlayerController == nullptr)
    {
        return false;
    }

    FVector CameraLocation;
    FRotator CameraRotation;

    PlayerController->GetPlayerViewPoint(
        CameraLocation,
        CameraRotation
    );

    const FVector TraceStart = CameraLocation;
    const FVector TraceEnd =
        TraceStart + CameraRotation.Vector() * TraceDistance;

    FCollisionQueryParams QueryParams(
        SCENE_QUERY_STAT(TerraforgeWorldTrace),
        true
    );

    QueryParams.bTraceComplex = true;
    QueryParams.bReturnPhysicalMaterial = false;

    APawn* PlayerPawn = PlayerController->GetPawn();

    if (PlayerPawn != nullptr)
    {
        QueryParams.AddIgnoredActor(PlayerPawn);
    }

    const bool bHit =
        World->LineTraceSingleByChannel(
            OutHit,
            TraceStart,
            TraceEnd,
            ECC_Visibility,
            QueryParams
        );

    return bHit;
}

void ATerraforgeWorldActor::AddBlocksAtCameraTrace(
    const int32 InBlockEditRange,
    const float TraceDistance,
    const int32 MaterialId
)
{
    FHitResult Hit;

    if (!GetCameraTraceHit(Hit, TraceDistance))
    {
        UE_LOG(LogTerraforgeVoxel, VeryVerbose, TEXT("Terraforge add block trace missed."));
        return;
    }

    const FVector AddLocation =
        Hit.ImpactPoint + Hit.ImpactNormal * GetBaseVoxelSize() * 0.75f;

    AddBlocksInSphere(
        AddLocation,
        InBlockEditRange,
        MaterialId
    );
}

void ATerraforgeWorldActor::RemoveBlocksAtCameraTrace(
    const int32 InBlockEditRange,
    const float TraceDistance
)
{
    FHitResult Hit;

    if (!GetCameraTraceHit(Hit, TraceDistance))
    {
        UE_LOG(LogTerraforgeVoxel, VeryVerbose, TEXT("Terraforge remove block trace missed."));
        return;
    }

    const FVector TraceDirection =
        (Hit.TraceEnd - Hit.TraceStart).GetSafeNormal();

    const FVector RemoveLocation =
        Hit.ImpactPoint + TraceDirection * GetBaseVoxelSize() * 0.75f;

    RemoveBlocksInSphere(
        RemoveLocation,
        InBlockEditRange
    );
}

void ATerraforgeWorldActor::SetBlockMaterialAtWorldVoxel(
    const FIntVector& WorldVoxelCoord,
    const uint16 MaterialId
)
{
    const FIntVector ChunkCoord =
        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

    const FIntVector LocalCoord =
        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

    const int32 VoxelIndex =
        LocalCoord.X +
        LocalCoord.Y * TerraforgeVoxel::ChunkSize +
        LocalCoord.Z * TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize;

    TMap<int32, uint16>& ChunkOverrides =
        BlockVoxelOverridesByChunk.FindOrAdd(ChunkCoord);

    ChunkOverrides.Add(
        VoxelIndex,
        MaterialId
    );

    MarkBlockVoxelModified(WorldVoxelCoord);

    FTerraforgeVoxelChunkData* ChunkData =
        BlockChunkDataByCoord.Find(ChunkCoord);

    if (ChunkData == nullptr)
    {
        if (MaterialId == 0)
        {
            return;
        }

        FTerraforgeVoxelChunkData NewChunkData(ChunkCoord);

        GenerateBlockChunkData(NewChunkData);
        ApplyBlockVoxelOverridesToChunk(
            ChunkCoord,
            NewChunkData
        );

        BlockChunkDataByCoord.Add(
            ChunkCoord,
            MoveTemp(NewChunkData)
        );

        ChunkData = BlockChunkDataByCoord.Find(ChunkCoord);

        if (ChunkData == nullptr)
        {
            return;
        }
    }

    FTerraforgeVoxel Voxel;
    Voxel.MaterialId = MaterialId;

    ChunkData->SetVoxel(
        LocalCoord.X,
        LocalCoord.Y,
        LocalCoord.Z,
        Voxel
    );
}

void ATerraforgeWorldActor::RemoveBlocksInSphere(
    const FVector WorldLocation,
    const int32 BlockRange
)
{
    const int32 SafeBlockRange =
        FMath::Clamp(BlockRange, 1, 16);

    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    const double CenterX = LocalLocation.X / SafeVoxelSize;
    const double CenterY = LocalLocation.Y / SafeVoxelSize;
    const double CenterZ = LocalLocation.Z / SafeVoxelSize;

    const FIntVector CenterVoxel(
        FMath::FloorToInt(CenterX),
        FMath::FloorToInt(CenterY),
        FMath::FloorToInt(CenterZ)
    );

    TSet<FIntVector> DirtyChunks;
    DirtyChunks.Reserve(32);

    int32 RemovedCount = 0;

    const double RadiusSquared =
        static_cast<double>(SafeBlockRange) *
        static_cast<double>(SafeBlockRange);

    for (int32 Z = CenterVoxel.Z - SafeBlockRange; Z <= CenterVoxel.Z + SafeBlockRange; ++Z)
    {
        const double DZ =
            static_cast<double>(Z) - static_cast<double>(CenterVoxel.Z);

        const double DZ2 =
            DZ * DZ;

        for (int32 Y = CenterVoxel.Y - SafeBlockRange; Y <= CenterVoxel.Y + SafeBlockRange; ++Y)
        {
            const double DY =
                static_cast<double>(Y) - static_cast<double>(CenterVoxel.Y);

            const double DYZ2 =
                DY * DY + DZ2;

            if (DYZ2 > RadiusSquared)
            {
                continue;
            }

            for (int32 X = CenterVoxel.X - SafeBlockRange; X <= CenterVoxel.X + SafeBlockRange; ++X)
            {
                const double DX =
                    static_cast<double>(X) - static_cast<double>(CenterVoxel.X);

                const double DistanceSquared =
                    DX * DX + DYZ2;

                if (DistanceSquared > RadiusSquared)
                {
                    continue;
                }

                const FIntVector WorldVoxelCoord(X, Y, Z);

                if (GetBlockMaterialAtWorldVoxel(WorldVoxelCoord) == 0)
                {
                    continue;
                }

                SetBlockMaterialAtWorldVoxel(
                    WorldVoxelCoord,
                    0
                );

                ++RemovedCount;

                const FIntVector ChunkCoord =
                    TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                DirtyChunks.Add(ChunkCoord);
                DirtyChunks.Add(ChunkCoord + FIntVector(1, 0, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(-1, 0, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, 1, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, -1, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, 0, 1));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, 0, -1));
            }
        }
    }

    if (DirtyChunks.Num() == 0)
    {
        return;
    }

    RebuildBlockChunks(DirtyChunks);

    KeepPlayerAboveTerrain(
        WorldLocation,
        static_cast<float>(SafeBlockRange) * GetBaseVoxelSize()
    );

    UE_LOG(
        LogTerraforgeVoxel,
        Display,
        TEXT("Removed Terraforge blocks. Range=%d Count=%d DirtyChunks=%d"),
        SafeBlockRange,
        RemovedCount,
        DirtyChunks.Num()
    );
}

void ATerraforgeWorldActor::AddBlocksInSphere(
    const FVector WorldLocation,
    const int32 BlockRange,
    const int32 MaterialId
)
{
    if (MaterialId <= 0)
    {
        return;
    }

    const int32 SafeBlockRange =
        FMath::Clamp(BlockRange, 1, 16);

    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    const double CenterX = LocalLocation.X / SafeVoxelSize;
    const double CenterY = LocalLocation.Y / SafeVoxelSize;
    const double CenterZ = LocalLocation.Z / SafeVoxelSize;

    const FIntVector CenterVoxel(
        FMath::FloorToInt(CenterX),
        FMath::FloorToInt(CenterY),
        FMath::FloorToInt(CenterZ)
    );

    TSet<FIntVector> DirtyChunks;
    DirtyChunks.Reserve(32);

    int32 AddedCount = 0;

    const double RadiusSquared =
        static_cast<double>(SafeBlockRange) *
        static_cast<double>(SafeBlockRange);

    for (int32 Z = CenterVoxel.Z - SafeBlockRange; Z <= CenterVoxel.Z + SafeBlockRange; ++Z)
    {
        const double DZ =
            static_cast<double>(Z) - static_cast<double>(CenterVoxel.Z);

        const double DZ2 =
            DZ * DZ;

        for (int32 Y = CenterVoxel.Y - SafeBlockRange; Y <= CenterVoxel.Y + SafeBlockRange; ++Y)
        {
            const double DY =
                static_cast<double>(Y) - static_cast<double>(CenterVoxel.Y);

            const double DYZ2 =
                DY * DY + DZ2;

            if (DYZ2 > RadiusSquared)
            {
                continue;
            }

            for (int32 X = CenterVoxel.X - SafeBlockRange; X <= CenterVoxel.X + SafeBlockRange; ++X)
            {
                const double DX =
                    static_cast<double>(X) - static_cast<double>(CenterVoxel.X);

                const double DistanceSquared =
                    DX * DX + DYZ2;

                if (DistanceSquared > RadiusSquared)
                {
                    continue;
                }

                const FIntVector WorldVoxelCoord(X, Y, Z);

                if (GetBlockMaterialAtWorldVoxel(WorldVoxelCoord) != 0)
                {
                    continue;
                }

                SetBlockMaterialAtWorldVoxel(
                    WorldVoxelCoord,
                    static_cast<uint16>(FMath::Clamp(MaterialId, 1, 65535))
                );

                ++AddedCount;

                const FIntVector ChunkCoord =
                    TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                DirtyChunks.Add(ChunkCoord);
                DirtyChunks.Add(ChunkCoord + FIntVector(1, 0, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(-1, 0, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, 1, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, -1, 0));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, 0, 1));
                DirtyChunks.Add(ChunkCoord + FIntVector(0, 0, -1));
            }
        }
    }

    if (DirtyChunks.Num() == 0)
    {
        return;
    }

    RebuildBlockChunks(DirtyChunks);

    KeepPlayerAboveTerrain(
        WorldLocation,
        static_cast<float>(SafeBlockRange) * GetBaseVoxelSize()
    );

    UE_LOG(
        LogTerraforgeVoxel,
        Display,
        TEXT("Added Terraforge blocks. Range=%d Count=%d DirtyChunks=%d"),
        SafeBlockRange,
        AddedCount,
        DirtyChunks.Num()
    );
}

void ATerraforgeWorldActor::RebuildBlockChunks(
    const TSet<FIntVector>& DirtyChunks
)
{
    if (DirtyChunks.Num() == 0)
    {
        return;
    }

    for (const FIntVector& ChunkCoord : DirtyChunks)
    {
        FTerraforgeVoxelChunkData* ChunkData =
            BlockChunkDataByCoord.Find(ChunkCoord);

        UTerraforgeVoxelChunkComponent* ExistingComponent = nullptr;

        TObjectPtr<UTerraforgeVoxelChunkComponent>* ExistingComponentPtr =
            ActiveChunkComponentsByCoord.Find(ChunkCoord);

        if (
            ExistingComponentPtr != nullptr &&
            IsValid(ExistingComponentPtr->Get())
            )
        {
            ExistingComponent = ExistingComponentPtr->Get();
        }

        if (ChunkData == nullptr || !ChunkData->HasAnySolidVoxel())
        {
            if (ExistingComponent != nullptr)
            {
                ExistingComponent->ClearAllMeshSections();
                ExistingComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);
                ExistingComponent->SetVisibility(false, true);
                ExistingComponent->SetHiddenInGame(true);
                ExistingComponent->Deactivate();

                ActiveChunkComponentsByCoord.Remove(ChunkCoord);
                PooledChunkComponents.Add(ExistingComponent);
            }

            BlockChunkDataByCoord.Remove(ChunkCoord);
            continue;
        }

        FTerraforgeVoxelMeshData MeshData;

        FTerraforgeVoxelMesher::GenerateGreedyMeshNeighbourAware(
            *ChunkData,
            [this](const FIntVector& WorldVoxelCoord) -> uint16
            {
                return GetBlockMaterialAtWorldVoxel(WorldVoxelCoord);
            },
            MeshData
        );

        if (MeshData.IsEmpty())
        {
            if (ExistingComponent != nullptr)
            {
                ExistingComponent->ClearAllMeshSections();
                ExistingComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);
                ExistingComponent->SetVisibility(false, true);
                ExistingComponent->SetHiddenInGame(true);
                ExistingComponent->Deactivate();

                ActiveChunkComponentsByCoord.Remove(ChunkCoord);
                PooledChunkComponents.Add(ExistingComponent);
            }

            continue;
        }

        if (!IsChunkActiveOrStreamingDesired(ChunkCoord))
        {
            continue;
        }

        ApplyMeshToChunk(
            ChunkCoord,
            MeshData
        );
    }
}

void ATerraforgeWorldActor::SetDensityAtWorldVoxel(
    const FIntVector& WorldVoxelCoord,
    const float Density,
    const uint16 MaterialId
)
{
    const FIntVector ChunkCoord =
        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

    const FIntVector LocalCoord =
        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

    FTerraforgeDensityChunkData* DensityChunkData =
        DensityChunkDataByCoord.Find(ChunkCoord);

    if (DensityChunkData == nullptr)
    {
        FTerraforgeDensityChunkData NewDensityChunkData(ChunkCoord);

        FillDensityChunkFromTerrainSampler(NewDensityChunkData);

        DensityChunkDataByCoord.Add(
            ChunkCoord,
            MoveTemp(NewDensityChunkData)
        );
        bDensityChunkBoundsDirty = true;

        DensityChunkData =
            DensityChunkDataByCoord.Find(ChunkCoord);

        if (DensityChunkData == nullptr)
        {
            return;
        }
    }

    FTerraforgeDensityVoxel Voxel;
    Voxel.Density = Density;
    Voxel.MaterialId = Density > 0.0f ? MaterialId : 0;

    DensityChunkData->SetVoxel(
        LocalCoord.X,
        LocalCoord.Y,
        LocalCoord.Z,
        Voxel
    );

    MarkDensityVoxelModified(WorldVoxelCoord);
}

void ATerraforgeWorldActor::AddDensityInSphere(
    const FVector WorldLocation,
    const float Radius,
    const float Strength,
    const int32 MaterialId
)
{
    if (Radius <= 0.0f || Strength <= 0.0f || MaterialId <= 0)
    {
        return;
    }

    if (bAsyncDensityEditInProgress)
    {
        return;
    }

    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    const double CenterX = LocalLocation.X / SafeVoxelSize;
    const double CenterY = LocalLocation.Y / SafeVoxelSize;
    const double CenterZ = LocalLocation.Z / SafeVoxelSize;

    const double RadiusVoxels =
        Radius / SafeVoxelSize;

    const int32 RadiusInt =
        FMath::CeilToInt(RadiusVoxels);

    const FIntVector CenterVoxel(
        FMath::FloorToInt(CenterX),
        FMath::FloorToInt(CenterY),
        FMath::FloorToInt(CenterZ)
    );

    TSet<FIntVector> TouchedChunks;

    for (int32 Z = CenterVoxel.Z - RadiusInt; Z <= CenterVoxel.Z + RadiusInt; ++Z)
    {
        for (int32 Y = CenterVoxel.Y - RadiusInt; Y <= CenterVoxel.Y + RadiusInt; ++Y)
        {
            for (int32 X = CenterVoxel.X - RadiusInt; X <= CenterVoxel.X + RadiusInt; ++X)
            {
                TouchedChunks.Add(
                    TerraforgeVoxel::WorldVoxelToChunkCoord(
                        FIntVector(X, Y, Z)
                    )
                );
            }
        }
    }

    TSet<FIntVector> RebuildChunks;

    for (const FIntVector& ChunkCoord : TouchedChunks)
    {
        AddDensityChunkAndNeighboursToDirtySet(
            ChunkCoord,
            RebuildChunks
        );
    }

    TSet<FIntVector> SnapshotChunks;

    for (const FIntVector& ChunkCoord : RebuildChunks)
    {
        AddDensityChunkAndNeighboursToDirtySet(
            ChunkCoord,
            SnapshotChunks
        );
    }

    TMap<FIntVector, FTerraforgeDensityChunkData> DensitySnapshot;

    for (const FIntVector& SnapshotChunkCoord : SnapshotChunks)
    {
        const FTerraforgeDensityChunkData* ExistingChunk =
            DensityChunkDataByCoord.Find(SnapshotChunkCoord);

        if (ExistingChunk != nullptr)
        {
            DensitySnapshot.Add(
                SnapshotChunkCoord,
                *ExistingChunk
            );

            continue;
        }

        FTerraforgeDensityChunkData GeneratedChunk(SnapshotChunkCoord);
        FillDensityChunkFromTerrainSampler(GeneratedChunk);

        DensitySnapshot.Add(
            SnapshotChunkCoord,
            MoveTemp(GeneratedChunk)
        );
    }

    bAsyncDensityEditInProgress = true;
    ++ActiveAsyncDensityEditId;

    const int32 EditId = ActiveAsyncDensityEditId;

    const int32 CapturedDensityMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    const uint16 SafeMaterialId =
        static_cast<uint16>(FMath::Clamp(MaterialId, 1, 65535));

    Async(
        EAsyncExecution::ThreadPool,
        [
            this,
            EditId,
            WorldLocation,
            Radius,
            Strength,
            CenterX,
            CenterY,
            CenterZ,
            RadiusVoxels,
            RadiusInt,
            CenterVoxel,
            SafeMaterialId,
            CapturedDensityMeshStep,
            TouchedChunks = MoveTemp(TouchedChunks),
            RebuildChunks = MoveTemp(RebuildChunks),
            DensitySnapshot = MoveTemp(DensitySnapshot)
        ]() mutable
    {
        const double RadiusVoxelsSquared =
            RadiusVoxels * RadiusVoxels;

        const float SafeStrength =
            FMath::Max(0.0f, Strength);

        TMap<FIntVector, FTerraforgeDensityChunkData> EditedDensityChunks;

        for (int32 Z = CenterVoxel.Z - RadiusInt; Z <= CenterVoxel.Z + RadiusInt; ++Z)
        {
            const double DZ =
                static_cast<double>(Z) + 0.5 - CenterZ;

            const double DZ2 =
                DZ * DZ;

            for (int32 Y = CenterVoxel.Y - RadiusInt; Y <= CenterVoxel.Y + RadiusInt; ++Y)
            {
                const double DY =
                    static_cast<double>(Y) + 0.5 - CenterY;

                const double DYZ2 =
                    DY * DY + DZ2;

                if (DYZ2 > RadiusVoxelsSquared)
                {
                    continue;
                }

                for (int32 X = CenterVoxel.X - RadiusInt; X <= CenterVoxel.X + RadiusInt; ++X)
                {
                    const double DX =
                        static_cast<double>(X) + 0.5 - CenterX;

                    const double DistanceSquared =
                        DX * DX + DYZ2;

                    if (DistanceSquared > RadiusVoxelsSquared)
                    {
                        continue;
                    }

                    const double Distance =
                        FMath::Sqrt(DistanceSquared);

                    const double NormalizedDistance =
                        FMath::Clamp(Distance / RadiusVoxels, 0.0, 1.0);

                    const double T =
                        1.0 - NormalizedDistance;

                    const double SmoothFalloff =
                        T * T * (3.0 - 2.0 * T);

                    const FIntVector WorldVoxelCoord(X, Y, Z);

                    const FIntVector ChunkCoord =
                        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                    FTerraforgeDensityChunkData* ChunkData =
                        DensitySnapshot.Find(ChunkCoord);

                    if (ChunkData == nullptr)
                    {
                        continue;
                    }

                    const FIntVector LocalCoord =
                        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                    FTerraforgeDensityVoxel Voxel =
                        ChunkData->GetVoxel(
                            LocalCoord.X,
                            LocalCoord.Y,
                            LocalCoord.Z
                        );

                    Voxel.Density +=
                        SafeStrength * static_cast<float>(SmoothFalloff);

                    Voxel.MaterialId =
                        Voxel.Density > 0.0f ? SafeMaterialId : 0;

                    ChunkData->SetVoxel(
                        LocalCoord.X,
                        LocalCoord.Y,
                        LocalCoord.Z,
                        Voxel
                    );

                    EditedDensityChunks.Add(
                        ChunkCoord,
                        *ChunkData
                    );
                }
            }
        }

        TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes;

        auto DensityLookup =
            [&DensitySnapshot, this](const FIntVector& WorldVoxelCoord) -> float
            {
                const FIntVector LookupChunkCoord =
                    TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                const FIntVector LocalCoord =
                    TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                const FTerraforgeDensityChunkData* ChunkData =
                    DensitySnapshot.Find(LookupChunkCoord);

                if (ChunkData == nullptr)
                {
                    return GetDensityAtWorldVoxelPosition(
                        FVector(
                            static_cast<double>(WorldVoxelCoord.X),
                            static_cast<double>(WorldVoxelCoord.Y),
                            static_cast<double>(WorldVoxelCoord.Z)
                        )
                    );
                }

                return ChunkData->GetVoxel(
                    LocalCoord.X,
                    LocalCoord.Y,
                    LocalCoord.Z
                ).Density;
            };

        auto MaterialLookup =
            [&DensitySnapshot, this](const FIntVector& WorldVoxelCoord) -> uint16
            {
                const FIntVector LookupChunkCoord =
                    TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                const FIntVector LocalCoord =
                    TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                const FTerraforgeDensityChunkData* ChunkData =
                    DensitySnapshot.Find(LookupChunkCoord);

                if (ChunkData == nullptr)
                {
                    return 0;
                }

                return ChunkData->GetVoxel(
                    LocalCoord.X,
                    LocalCoord.Y,
                    LocalCoord.Z
                ).MaterialId;
            };

        for (const FIntVector& ChunkCoord : RebuildChunks)
        {
            FTerraforgeVoxelMeshData MeshData;

            FTerraforgeMarchingCubes::GenerateMesh(
                ChunkCoord,
                DensityLookup,
                MaterialLookup,
                MeshData,
                CapturedDensityMeshStep,
                bSmoothNormalsAcrossMaterialBoundaries
            );

            RebuiltMeshes.Add(
                ChunkCoord,
                MoveTemp(MeshData)
            );
        }

        AsyncTask(
            ENamedThreads::GameThread,
            [
                this,
                EditId,
                EditedDensityChunks = MoveTemp(EditedDensityChunks),
                RebuiltMeshes = MoveTemp(RebuiltMeshes),
                RebuildChunks = MoveTemp(RebuildChunks),
                WorldLocation,
                Radius
            ]() mutable
            {
                ApplyAsyncDensityEditResult(
                    EditId,
                    MoveTemp(EditedDensityChunks),
                    MoveTemp(RebuiltMeshes),
                    MoveTemp(RebuildChunks),
                    WorldLocation,
                    Radius
                );
            }
        );
    }
    );
}

#if WITH_EDITOR
void ATerraforgeWorldActor::GenerateSmoothDensityWorldEditorPreview()
{
    MarkGenerationStarted();

    ClearWorld();

    const int32 SafeRadiusInChunks =
        FMath::Max(0, RadiusInChunks);

    int32 EffectiveMinChunkZ = DensityMinChunkZ;
    int32 EffectiveMaxChunkZ = DensityMaxChunkZ;

    if (EffectiveMinChunkZ > EffectiveMaxChunkZ)
    {
        Swap(EffectiveMinChunkZ, EffectiveMaxChunkZ);
    }

    const int32 SafeMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    for (int32 Z = EffectiveMinChunkZ; Z <= EffectiveMaxChunkZ; ++Z)
    {
        for (int32 Y = -SafeRadiusInChunks; Y <= SafeRadiusInChunks; ++Y)
        {
            for (int32 X = -SafeRadiusInChunks; X <= SafeRadiusInChunks; ++X)
            {
                const FIntVector ChunkCoord(X, Y, Z);

                FTerraforgeDensityChunkData DensityChunkData(ChunkCoord);

                FillDensityChunkFromTerrainSampler(DensityChunkData);

                if (DensityChunkData.HasAnySolidVoxel())
                {
                    DensityChunkDataByCoord.Add(
                        ChunkCoord,
                        MoveTemp(DensityChunkData)
                    );
                    bDensityChunkBoundsDirty = true;
                }
            }
        }
    }

    for (const TPair<FIntVector, FTerraforgeDensityChunkData>& Pair : DensityChunkDataByCoord)
    {
        const FIntVector ChunkCoord = Pair.Key;

        FTerraforgeVoxelMeshData MeshData;

        FTerraforgeMarchingCubes::GenerateMesh(
            ChunkCoord,
            [this](const FIntVector& WorldVoxelCoord) -> float
            {
                return GetStoredDensityAtWorldVoxel(WorldVoxelCoord);
            },
            [this](const FIntVector& WorldVoxelCoord) -> uint16
            {
                return GetStoredDensityMaterialAtWorldVoxel(WorldVoxelCoord);
            },
            MeshData,
            SafeMeshStep,
            bSmoothNormalsAcrossMaterialBoundaries
        );

        if (!MeshData.IsEmpty())
        {
            ApplyMeshToChunk(
                ChunkCoord,
                MeshData
            );

            ++AppliedMeshes;
        }
    }

    TotalMeshesToApply = AppliedMeshes;

    UpdateChunkCollisionStates();
    MarkGenerationComplete();

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Generated Terraforge smooth density editor preview. DataChunks=%d ActiveComponents=%d"),
        DensityChunkDataByCoord.Num(),
        ActiveChunkComponentsByCoord.Num()
    );
}
#endif

void ATerraforgeWorldActor::RemoveDensityInSphere(
    const FVector WorldLocation,
    const float Radius,
    const float Strength
)
{
    if (Radius <= 0.0f || Strength <= 0.0f)
    {
        return;
    }

    if (bAsyncDensityEditInProgress)
    {
        return;
    }

    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    const double CenterX = LocalLocation.X / SafeVoxelSize;
    const double CenterY = LocalLocation.Y / SafeVoxelSize;
    const double CenterZ = LocalLocation.Z / SafeVoxelSize;

    const double RadiusVoxels =
        Radius / SafeVoxelSize;

    const int32 RadiusInt =
        FMath::CeilToInt(RadiusVoxels);

    const FIntVector CenterVoxel(
        FMath::FloorToInt(CenterX),
        FMath::FloorToInt(CenterY),
        FMath::FloorToInt(CenterZ)
    );

    TSet<FIntVector> TouchedChunks;

    for (int32 Z = CenterVoxel.Z - RadiusInt; Z <= CenterVoxel.Z + RadiusInt; ++Z)
    {
        for (int32 Y = CenterVoxel.Y - RadiusInt; Y <= CenterVoxel.Y + RadiusInt; ++Y)
        {
            for (int32 X = CenterVoxel.X - RadiusInt; X <= CenterVoxel.X + RadiusInt; ++X)
            {
                TouchedChunks.Add(
                    TerraforgeVoxel::WorldVoxelToChunkCoord(
                        FIntVector(X, Y, Z)
                    )
                );
            }
        }
    }

    TSet<FIntVector> RebuildChunks;

    for (const FIntVector& ChunkCoord : TouchedChunks)
    {
        AddDensityChunkAndNeighboursToDirtySet(
            ChunkCoord,
            RebuildChunks
        );
    }

    TSet<FIntVector> SnapshotChunks;

    for (const FIntVector& ChunkCoord : RebuildChunks)
    {
        AddDensityChunkAndNeighboursToDirtySet(
            ChunkCoord,
            SnapshotChunks
        );
    }

    TMap<FIntVector, FTerraforgeDensityChunkData> DensitySnapshot;

    for (const FIntVector& SnapshotChunkCoord : SnapshotChunks)
    {
        const FTerraforgeDensityChunkData* ExistingChunk =
            DensityChunkDataByCoord.Find(SnapshotChunkCoord);

        if (ExistingChunk != nullptr)
        {
            DensitySnapshot.Add(
                SnapshotChunkCoord,
                *ExistingChunk
            );

            continue;
        }

        FTerraforgeDensityChunkData GeneratedChunk(SnapshotChunkCoord);
        FillDensityChunkFromTerrainSampler(GeneratedChunk);

        DensitySnapshot.Add(
            SnapshotChunkCoord,
            MoveTemp(GeneratedChunk)
        );
    }

    bAsyncDensityEditInProgress = true;
    ++ActiveAsyncDensityEditId;

    const int32 EditId = ActiveAsyncDensityEditId;

    const int32 CapturedDensityMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    Async(
        EAsyncExecution::ThreadPool,
        [
            this,
            EditId,
            WorldLocation,
            Radius,
            Strength,
            CenterX,
            CenterY,
            CenterZ,
            RadiusVoxels,
            RadiusInt,
            CenterVoxel,
            CapturedDensityMeshStep,
            RebuildChunks = MoveTemp(RebuildChunks),
            DensitySnapshot = MoveTemp(DensitySnapshot)
        ]() mutable
    {
        const double RadiusVoxelsSquared =
            RadiusVoxels * RadiusVoxels;

        const float SafeStrength =
            FMath::Max(0.0f, Strength);

        TMap<FIntVector, FTerraforgeDensityChunkData> EditedDensityChunks;

        for (int32 Z = CenterVoxel.Z - RadiusInt; Z <= CenterVoxel.Z + RadiusInt; ++Z)
        {
            const double DZ =
                static_cast<double>(Z) + 0.5 - CenterZ;

            const double DZ2 =
                DZ * DZ;

            for (int32 Y = CenterVoxel.Y - RadiusInt; Y <= CenterVoxel.Y + RadiusInt; ++Y)
            {
                const double DY =
                    static_cast<double>(Y) + 0.5 - CenterY;

                const double DYZ2 =
                    DY * DY + DZ2;

                if (DYZ2 > RadiusVoxelsSquared)
                {
                    continue;
                }

                for (int32 X = CenterVoxel.X - RadiusInt; X <= CenterVoxel.X + RadiusInt; ++X)
                {
                    const double DX =
                        static_cast<double>(X) + 0.5 - CenterX;

                    const double DistanceSquared =
                        DX * DX + DYZ2;

                    if (DistanceSquared > RadiusVoxelsSquared)
                    {
                        continue;
                    }

                    const double Distance =
                        FMath::Sqrt(DistanceSquared);

                    const double NormalizedDistance =
                        FMath::Clamp(Distance / RadiusVoxels, 0.0, 1.0);

                    const double T =
                        1.0 - NormalizedDistance;

                    const double SmoothFalloff =
                        T * T * (3.0 - 2.0 * T);

                    const FIntVector WorldVoxelCoord(X, Y, Z);

                    const FIntVector ChunkCoord =
                        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                    FTerraforgeDensityChunkData* ChunkData =
                        DensitySnapshot.Find(ChunkCoord);

                    if (ChunkData == nullptr)
                    {
                        continue;
                    }

                    const FIntVector LocalCoord =
                        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                    FTerraforgeDensityVoxel Voxel =
                        ChunkData->GetVoxel(
                            LocalCoord.X,
                            LocalCoord.Y,
                            LocalCoord.Z
                        );

                    Voxel.Density -=
                        SafeStrength * static_cast<float>(SmoothFalloff);

                    Voxel.MaterialId =
                        Voxel.Density > 0.0f
                        ? (Voxel.MaterialId == 0 ? 1 : Voxel.MaterialId)
                        : 0;

                    ChunkData->SetVoxel(
                        LocalCoord.X,
                        LocalCoord.Y,
                        LocalCoord.Z,
                        Voxel
                    );

                    EditedDensityChunks.Add(
                        ChunkCoord,
                        *ChunkData
                    );
                }
            }
        }

        TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes;

        auto DensityLookup =
            [&DensitySnapshot, this](const FIntVector& WorldVoxelCoord) -> float
            {
                const FIntVector LookupChunkCoord =
                    TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                const FIntVector LocalCoord =
                    TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                const FTerraforgeDensityChunkData* ChunkData =
                    DensitySnapshot.Find(LookupChunkCoord);

                if (ChunkData == nullptr)
                {
                    return GetDensityAtWorldVoxelPosition(
                        FVector(
                            static_cast<double>(WorldVoxelCoord.X),
                            static_cast<double>(WorldVoxelCoord.Y),
                            static_cast<double>(WorldVoxelCoord.Z)
                        )
                    );
                }

                return ChunkData->GetVoxel(
                    LocalCoord.X,
                    LocalCoord.Y,
                    LocalCoord.Z
                ).Density;
            };

        auto MaterialLookup =
            [&DensitySnapshot](const FIntVector& WorldVoxelCoord) -> uint16
            {
                const FIntVector LookupChunkCoord =
                    TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                const FIntVector LocalCoord =
                    TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                const FTerraforgeDensityChunkData* ChunkData =
                    DensitySnapshot.Find(LookupChunkCoord);

                if (ChunkData == nullptr)
                {
                    return 0;
                }

                return ChunkData->GetVoxel(
                    LocalCoord.X,
                    LocalCoord.Y,
                    LocalCoord.Z
                ).MaterialId;
            };

        for (const FIntVector& ChunkCoord : RebuildChunks)
        {
            FTerraforgeVoxelMeshData MeshData;

            FTerraforgeMarchingCubes::GenerateMesh(
                ChunkCoord,
                DensityLookup,
                MaterialLookup,
                MeshData,
                CapturedDensityMeshStep,
                bSmoothNormalsAcrossMaterialBoundaries
            );

            RebuiltMeshes.Add(
                ChunkCoord,
                MoveTemp(MeshData)
            );
        }

        AsyncTask(
            ENamedThreads::GameThread,
            [
                this,
                EditId,
                EditedDensityChunks = MoveTemp(EditedDensityChunks),
                RebuiltMeshes = MoveTemp(RebuiltMeshes),
                RebuildChunks = MoveTemp(RebuildChunks),
                WorldLocation,
                Radius
            ]() mutable
            {
                ApplyAsyncDensityEditResult(
                    EditId,
                    MoveTemp(EditedDensityChunks),
                    MoveTemp(RebuiltMeshes),
                    MoveTemp(RebuildChunks),
                    WorldLocation,
                    Radius
                );
            }
        );
    }
    );
}

void ATerraforgeWorldActor::RebuildDensityChunks(
    const TSet<FIntVector>& DirtyChunks
)
{
    RebuildDensityChunksAsync(DirtyChunks);
}

void ATerraforgeWorldActor::RebuildDensityChunksAsync(
    const TSet<FIntVector>& DirtyChunks
)
{
    if (DirtyChunks.Num() == 0)
    {
        return;
    }

    TSet<FIntVector> ExpandedDirtyChunks;

    for (const FIntVector& ChunkCoord : DirtyChunks)
    {
        AddDensityChunkAndNeighboursToDirtySet(
            ChunkCoord,
            ExpandedDirtyChunks
        );
    }

    for (const FIntVector& ChunkCoord : ExpandedDirtyChunks)
    {
        PendingDirtyChunks.Add(ChunkCoord);
        InvalidateStreamingChunkForEdit(ChunkCoord);
    }

    if (bAsyncRebuildInProgress)
    {
        return;
    }

    if (PendingDirtyChunks.Num() == 0)
    {
        return;
    }

    bAsyncRebuildInProgress = true;
    ++ActiveAsyncRebuildId;

    const int32 RebuildId = ActiveAsyncRebuildId;

    TSet<FIntVector> ChunksToRebuild = MoveTemp(PendingDirtyChunks);
    PendingDirtyChunks.Reset();

    for (auto It = ChunksToRebuild.CreateIterator(); It; ++It)
    {
        if (!IsChunkActiveOrStreamingDesired(*It))
        {
            It.RemoveCurrent();
        }
    }

    if (ChunksToRebuild.Num() == 0)
    {
        bAsyncRebuildInProgress = false;
        return;
    }

    TSet<FIntVector> SnapshotChunkCoords;

    for (const FIntVector& ChunkCoord : ChunksToRebuild)
    {
        AddDensityChunkAndNeighboursToDirtySet(
            ChunkCoord,
            SnapshotChunkCoords
        );
    }

    TMap<FIntVector, FTerraforgeDensityChunkData> DensitySnapshot;

    for (const FIntVector& SnapshotChunkCoord : SnapshotChunkCoords)
    {
        const FTerraforgeDensityChunkData* ExistingChunkData =
            DensityChunkDataByCoord.Find(SnapshotChunkCoord);

        if (ExistingChunkData == nullptr)
        {
            continue;
        }

        DensitySnapshot.Add(
            SnapshotChunkCoord,
            *ExistingChunkData
        );
    }

    const int32 CapturedDensityMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    Async(
        EAsyncExecution::ThreadPool,
        [
            this,
            RebuildId,
            ChunksToRebuild = MoveTemp(ChunksToRebuild),
            DensitySnapshot = MoveTemp(DensitySnapshot),
            CapturedDensityMeshStep
        ]() mutable
        {
            TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes;

            auto DensityLookup =
                [&DensitySnapshot, this](const FIntVector& WorldVoxelCoord) -> float
                {
                    const FIntVector ChunkCoord =
                        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                    const FIntVector LocalCoord =
                        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                    const FTerraforgeDensityChunkData* DensityChunkData =
                        DensitySnapshot.Find(ChunkCoord);

                    if (DensityChunkData == nullptr)
                    {
                        return GetDensityAtWorldVoxelPosition(
                            FVector(
                                static_cast<double>(WorldVoxelCoord.X),
                                static_cast<double>(WorldVoxelCoord.Y),
                                static_cast<double>(WorldVoxelCoord.Z)
                            )
                        );
                    }

                    return DensityChunkData->GetVoxel(
                        LocalCoord.X,
                        LocalCoord.Y,
                        LocalCoord.Z
                    ).Density;
                };

            auto MaterialLookup =
                [&DensitySnapshot, this](const FIntVector& WorldVoxelCoord) -> uint16
                {
                    const FIntVector ChunkCoord =
                        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

                    const FIntVector LocalCoord =
                        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

                    const FTerraforgeDensityChunkData* DensityChunkData =
                        DensitySnapshot.Find(ChunkCoord);

                    if (DensityChunkData == nullptr)
                    {
                        const float Density =
                            GetDensityAtWorldVoxelPosition(
                                FVector(
                                    static_cast<double>(WorldVoxelCoord.X),
                                    static_cast<double>(WorldVoxelCoord.Y),
                                    static_cast<double>(WorldVoxelCoord.Z)
                                )
                            );

                        return Density > 0.0f ? 1 : 0;
                    }

                    return DensityChunkData->GetVoxel(
                        LocalCoord.X,
                        LocalCoord.Y,
                        LocalCoord.Z
                    ).MaterialId;
                };

            for (const FIntVector& ChunkCoord : ChunksToRebuild)
            {
                FTerraforgeVoxelMeshData MeshData;

                FTerraforgeMarchingCubes::GenerateMesh(
                    ChunkCoord,
                    DensityLookup,
                    MaterialLookup,
                    MeshData,
                    CapturedDensityMeshStep,
                    bSmoothNormalsAcrossMaterialBoundaries
                );

                RebuiltMeshes.Add(
                    ChunkCoord,
                    MoveTemp(MeshData)
                );
            }

            AsyncTask(
                ENamedThreads::GameThread,
                [
                    this,
                    RebuildId,
                    RebuiltMeshes = MoveTemp(RebuiltMeshes),
                    RebuiltChunkCoords = MoveTemp(ChunksToRebuild)
                ]() mutable
                {
                    ApplyAsyncRebuiltDensityChunks(
                        RebuildId,
                        MoveTemp(RebuiltMeshes),
                        MoveTemp(RebuiltChunkCoords)
                    );
                }
            );
        }
    );
}

void ATerraforgeWorldActor::ApplyAsyncRebuiltDensityChunks(
    const int32 RebuildId,
    TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes,
    TSet<FIntVector> RebuiltChunkCoords
)
{
    if (bIsEndingPlay || RebuildId != ActiveAsyncRebuildId)
    {
        return;
    }

    QueueAsyncRebuiltDensityChunksForApply(
        MoveTemp(RebuiltMeshes),
        MoveTemp(RebuiltChunkCoords)
    );

    bAsyncRebuildInProgress = false;

    UE_LOG(
        LogTerraforgeVoxel,
        Display,
        TEXT("Queued async Terraforge density rebuild apply. Chunks=%d PendingDirty=%d"),
        PendingDensityRebuildMeshCoords.Num(),
        PendingDirtyChunks.Num()
    );

    if (PendingDirtyChunks.Num() > 0)
    {
        TSet<FIntVector> QueuedChunks = PendingDirtyChunks;
        PendingDirtyChunks.Reset();

        RebuildDensityChunksAsync(QueuedChunks);
    }
}

void ATerraforgeWorldActor::ApplyAsyncDensityEditResult(
    const int32 EditId,
    TMap<FIntVector, FTerraforgeDensityChunkData> EditedDensityChunks,
    TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes,
    TSet<FIntVector> RebuiltChunkCoords,
    const FVector EditWorldLocation,
    const float InEditRadius
)
{
    if (bIsEndingPlay || EditId != ActiveAsyncDensityEditId)
    {
        return;
    }

    for (TPair<FIntVector, FTerraforgeDensityChunkData>& Pair : EditedDensityChunks)
    {
        DensityChunkDataByCoord.Add(
            Pair.Key,
            MoveTemp(Pair.Value)
        );
        bDensityChunkBoundsDirty = true;

        ModifiedDensityChunks.Add(Pair.Key);

        TSet<int32>& ModifiedIndices =
            ModifiedDensityVoxelIndicesByChunk.FindOrAdd(Pair.Key);

        for (int32 VoxelIndex = 0; VoxelIndex < TerraforgeVoxel::ChunkVolume; ++VoxelIndex)
        {
            ModifiedIndices.Add(VoxelIndex);
        }
    }

    for (const FIntVector& ChunkCoord : RebuiltChunkCoords)
    {
        InvalidateStreamingChunkForEdit(ChunkCoord);
    }

    QueueAsyncRebuiltDensityChunksForApply(
        MoveTemp(RebuiltMeshes),
        MoveTemp(RebuiltChunkCoords)
    );

    bHasPendingPlayerSafetyCheck = true;
    PendingPlayerSafetyLocation = EditWorldLocation;
    PendingPlayerSafetyRadius = InEditRadius;

    bAsyncDensityEditInProgress = false;
}

void ATerraforgeWorldActor::QueueAsyncRebuiltDensityChunksForApply(
    TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes,
    TSet<FIntVector> RebuiltChunkCoords
)
{
    for (const FIntVector& ChunkCoord : RebuiltChunkCoords)
    {
        FTerraforgeVoxelMeshData* MeshData =
            RebuiltMeshes.Find(ChunkCoord);

        if (MeshData == nullptr)
        {
            PendingDensityRebuildMeshData.Remove(ChunkCoord);
        }
        else
        {
            PendingDensityRebuildMeshData.Add(
                ChunkCoord,
                MoveTemp(*MeshData)
            );
        }

        if (!PendingDensityRebuildMeshCoords.Contains(ChunkCoord))
        {
            PendingDensityRebuildMeshCoords.Add(ChunkCoord);
        }
    }

    if (PendingDensityRebuildMeshApplyIndex >= PendingDensityRebuildMeshCoords.Num())
    {
        PendingDensityRebuildMeshApplyIndex = 0;
    }
}

void ATerraforgeWorldActor::TickDensityRebuildMeshApply()
{
    if (PendingDensityRebuildMeshCoords.Num() == 0)
    {
        return;
    }

    int32 AppliedThisFrame = 0;

    while (
        PendingDensityRebuildMeshApplyIndex < PendingDensityRebuildMeshCoords.Num() &&
        AppliedThisFrame < FMath::Max(1, DensityRebuildMeshAppliesPerFrame)
        )
    {
        const FIntVector ChunkCoord =
            PendingDensityRebuildMeshCoords[PendingDensityRebuildMeshApplyIndex];

        ++PendingDensityRebuildMeshApplyIndex;
        ++AppliedThisFrame;

        FTerraforgeVoxelMeshData* MeshData =
            PendingDensityRebuildMeshData.Find(ChunkCoord);

        if (MeshData == nullptr || MeshData->IsEmpty())
        {
            ClearChunkComponentForCoord(ChunkCoord);
            ProcessedEmptyStreamingChunks.Add(ChunkCoord);
            PendingDensityRebuildMeshData.Remove(ChunkCoord);
            continue;
        }

        ProcessedEmptyStreamingChunks.Remove(ChunkCoord);

        if (IsChunkActiveOrStreamingDesired(ChunkCoord))
        {
            ApplyMeshToChunk(
                ChunkCoord,
                *MeshData
            );
        }

        PendingDensityRebuildMeshData.Remove(ChunkCoord);
    }

    if (PendingDensityRebuildMeshApplyIndex >= PendingDensityRebuildMeshCoords.Num())
    {
        PendingDensityRebuildMeshCoords.Reset();
        PendingDensityRebuildMeshData.Reset();
        PendingDensityRebuildMeshApplyIndex = 0;

        if (bHasPendingPlayerSafetyCheck)
        {
            bHasPendingPlayerSafetyCheck = false;

            KeepPlayerAboveDensityTerrain(
                PendingPlayerSafetyLocation,
                PendingPlayerSafetyRadius
            );
        }
    }
}

bool ATerraforgeWorldActor::ShouldChunkGenerateCollisionInitially(
    const FIntVector& ChunkCoord
) const
{
    if (!bGenerateCollision)
    {
        return false;
    }

    switch (CollisionMode)
    {
    case ETerraforgeWorldCollisionMode::None:
        return false;

    case ETerraforgeWorldCollisionMode::AllChunks:
        return true;

    case ETerraforgeWorldCollisionMode::NearPlayerOnly:
        return ShouldChunkHaveCollisionNearPlayer(ChunkCoord);

    default:
        return false;
    }
}

bool ATerraforgeWorldActor::ShouldChunkHaveCollisionNearPlayer(
    const FIntVector& ChunkCoord
) const
{
    UWorld* World = GetWorld();

    if (World == nullptr)
    {
        return false;
    }

    ACharacter* PlayerCharacter =
        UGameplayStatics::GetPlayerCharacter(World, 0);

    if (PlayerCharacter == nullptr)
    {
        return false;
    }

    const FVector PlayerWorldLocation =
        PlayerCharacter->GetActorLocation();

    const FVector PlayerLocalLocation =
        GetActorTransform().InverseTransformPosition(PlayerWorldLocation);

    return TerraforgeWorldActorLocal::ShouldChunkHaveCollisionNearLocalPosition(
        ChunkCoord,
        PlayerLocalLocation,
        GetChunkWorldSize(),
        FMath::Square(static_cast<double>(CollisionActivationRadius))
    );
}

void ATerraforgeWorldActor::UpdateChunkCollisionStates()
{
    if (!bGenerateCollision)
    {
        return;
    }

    ECollisionEnabled::Type DefaultCollisionMode =
        ECollisionEnabled::NoCollision;

    bool bUsePlayerProximity = false;
    FVector PlayerLocalLocation = FVector::ZeroVector;
    double ChunkWorldSize = 0.0;
    double CollisionActivationRadiusSquared = 0.0;

    switch (CollisionMode)
    {
    case ETerraforgeWorldCollisionMode::None:
        DefaultCollisionMode = ECollisionEnabled::NoCollision;
        break;

    case ETerraforgeWorldCollisionMode::AllChunks:
        DefaultCollisionMode = ECollisionEnabled::QueryAndPhysics;
        break;

    case ETerraforgeWorldCollisionMode::NearPlayerOnly:
    {
        UWorld* World = GetWorld();
        ACharacter* PlayerCharacter =
            World != nullptr
            ? UGameplayStatics::GetPlayerCharacter(World, 0)
            : nullptr;

        if (PlayerCharacter != nullptr)
        {
            PlayerLocalLocation =
                GetActorTransform().InverseTransformPosition(
                    PlayerCharacter->GetActorLocation()
                );

            ChunkWorldSize = GetChunkWorldSize();
            CollisionActivationRadiusSquared =
                FMath::Square(static_cast<double>(CollisionActivationRadius));
            bUsePlayerProximity = true;
        }

        break;
    }

    default:
        break;
    }

    for (const TPair<FIntVector, TObjectPtr<UTerraforgeVoxelChunkComponent>>& Pair :
        ActiveChunkComponentsByCoord)
    {
        UTerraforgeVoxelChunkComponent* ChunkComponent = Pair.Value.Get();

        if (!IsValid(ChunkComponent))
        {
            continue;
        }

        ECollisionEnabled::Type DesiredCollisionMode =
            DefaultCollisionMode;

        if (bUsePlayerProximity)
        {
            DesiredCollisionMode =
                TerraforgeWorldActorLocal::ShouldChunkHaveCollisionNearLocalPosition(
                    Pair.Key,
                    PlayerLocalLocation,
                    ChunkWorldSize,
                    CollisionActivationRadiusSquared
                )
                ? ECollisionEnabled::QueryAndPhysics
                : ECollisionEnabled::NoCollision;
        }

        if (ChunkComponent->GetCollisionEnabled() != DesiredCollisionMode)
        {
            ChunkComponent->SetCollisionEnabled(DesiredCollisionMode);
        }
    }
}

void ATerraforgeWorldActor::RefreshCachedDensityChunkBounds() const
{
    if (!bDensityChunkBoundsDirty)
    {
        return;
    }

    int32 MinWorldZ = TNumericLimits<int32>::Max();
    int32 MaxWorldZ = TNumericLimits<int32>::Lowest();

    for (const TPair<FIntVector, FTerraforgeDensityChunkData>& Pair : DensityChunkDataByCoord)
    {
        MinWorldZ = FMath::Min(
            MinWorldZ,
            Pair.Key.Z * TerraforgeVoxel::ChunkSize
        );

        MaxWorldZ = FMath::Max(
            MaxWorldZ,
            (Pair.Key.Z + 1) * TerraforgeVoxel::ChunkSize
        );
    }

    if (MinWorldZ > MaxWorldZ)
    {
        CachedDensityMinWorldZ = 0;
        CachedDensityMaxWorldZ = -1;
    }
    else
    {
        CachedDensityMinWorldZ = MinWorldZ;
        CachedDensityMaxWorldZ = MaxWorldZ;
    }

    bDensityChunkBoundsDirty = false;
}

bool ATerraforgeWorldActor::FindDensityTerrainTopAtWorldXY(
    const FVector WorldLocation,
    double& OutTopWorldZ
) const
{
    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    const int32 WorldVoxelX =
        FMath::FloorToInt(LocalLocation.X / SafeVoxelSize);

    const int32 WorldVoxelY =
        FMath::FloorToInt(LocalLocation.Y / SafeVoxelSize);

    RefreshCachedDensityChunkBounds();

    if (CachedDensityMinWorldZ > CachedDensityMaxWorldZ)
    {
        return false;
    }

    for (int32 Z = CachedDensityMaxWorldZ; Z >= CachedDensityMinWorldZ; --Z)
    {
        const FIntVector WorldVoxelCoord(
            WorldVoxelX,
            WorldVoxelY,
            Z
        );

        const float Density =
            GetStoredDensityAtWorldVoxel(WorldVoxelCoord);

        if (Density > 0.0f)
        {
            const FVector TopLocalPosition(
                static_cast<double>(WorldVoxelX) * SafeVoxelSize,
                static_cast<double>(WorldVoxelY) * SafeVoxelSize,
                static_cast<double>(Z + 1) * SafeVoxelSize
            );

            const FVector TopWorldPosition =
                GetActorTransform().TransformPosition(TopLocalPosition);

            OutTopWorldZ = TopWorldPosition.Z;
            return true;
        }
    }

    return false;
}

bool ATerraforgeWorldActor::FindBlockTerrainTopAtWorldXY(
    const FVector WorldLocation,
    double& OutTopWorldZ
) const
{
    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    const int32 WorldVoxelX =
        FMath::FloorToInt(LocalLocation.X / SafeVoxelSize);

    const int32 WorldVoxelY =
        FMath::FloorToInt(LocalLocation.Y / SafeVoxelSize);

    int32 MinWorldZ = TNumericLimits<int32>::Max();
    int32 MaxWorldZ = TNumericLimits<int32>::Lowest();

    for (const TPair<FIntVector, FTerraforgeVoxelChunkData>& Pair : BlockChunkDataByCoord)
    {
        MinWorldZ = FMath::Min(
            MinWorldZ,
            Pair.Key.Z * TerraforgeVoxel::ChunkSize
        );

        MaxWorldZ = FMath::Max(
            MaxWorldZ,
            (Pair.Key.Z + 1) * TerraforgeVoxel::ChunkSize
        );
    }

    if (MinWorldZ > MaxWorldZ)
    {
        return false;
    }

    for (int32 Z = MaxWorldZ; Z >= MinWorldZ; --Z)
    {
        const FIntVector WorldVoxelCoord(
            WorldVoxelX,
            WorldVoxelY,
            Z
        );

        if (GetBlockMaterialAtWorldVoxel(WorldVoxelCoord) == 0)
        {
            continue;
        }

        const FVector TopLocalPosition(
            static_cast<double>(WorldVoxelX) * SafeVoxelSize,
            static_cast<double>(WorldVoxelY) * SafeVoxelSize,
            static_cast<double>(Z + 1) * SafeVoxelSize
        );

        const FVector TopWorldPosition =
            GetActorTransform().TransformPosition(TopLocalPosition);

        OutTopWorldZ = TopWorldPosition.Z;
        return true;
    }

    return false;
}

void ATerraforgeWorldActor::KeepPlayerAboveTerrain(
    const FVector EditWorldLocation,
    const float InEditRadius
)
{
    if (TerrainSystem == ETerraforgeTerrainSystem::Block)
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

        const FVector EditLocalLocation =
            GetActorTransform().InverseTransformPosition(EditWorldLocation);

        const FVector PlayerLocalLocation =
            GetActorTransform().InverseTransformPosition(PlayerLocation);

        const double Distance2D =
            FVector2D::Distance(
                FVector2D(EditLocalLocation.X, EditLocalLocation.Y),
                FVector2D(PlayerLocalLocation.X, PlayerLocalLocation.Y)
            );

        const double SafetyRadius =
            static_cast<double>(InEditRadius) +
            static_cast<double>(Capsule->GetScaledCapsuleRadius()) +
            static_cast<double>(GetBaseVoxelSize()) * 2.0;

        if (Distance2D > SafetyRadius)
        {
            return;
        }

        double TerrainTopWorldZ = 0.0;

        if (!FindBlockTerrainTopAtWorldXY(PlayerLocation, TerrainTopWorldZ))
        {
            return;
        }

        const double MinimumPlayerZ =
            TerrainTopWorldZ +
            Capsule->GetScaledCapsuleHalfHeight() +
            12.0;

        if (PlayerLocation.Z >= MinimumPlayerZ)
        {
            return;
        }

        FVector NewPlayerLocation = PlayerLocation;
        NewPlayerLocation.Z = MinimumPlayerZ;

        PlayerCharacter->SetActorLocation(
            NewPlayerLocation,
            false,
            nullptr,
            ETeleportType::TeleportPhysics
        );

        return;
    }

    KeepPlayerAboveDensityTerrain(
        EditWorldLocation,
        InEditRadius
    );
}

void ATerraforgeWorldActor::KeepPlayerAboveDensityTerrain(
    const FVector EditWorldLocation,
    const float InEditRadius
)
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

    const FVector EditLocalLocation =
        GetActorTransform().InverseTransformPosition(EditWorldLocation);

    const FVector PlayerLocalLocation =
        GetActorTransform().InverseTransformPosition(PlayerLocation);

    const double Distance2D =
        FVector2D::Distance(
            FVector2D(EditLocalLocation.X, EditLocalLocation.Y),
            FVector2D(PlayerLocalLocation.X, PlayerLocalLocation.Y)
        );

    const double SafetyRadius =
        static_cast<double>(InEditRadius) +
        static_cast<double>(Capsule->GetScaledCapsuleRadius()) +
        static_cast<double>(GetBaseVoxelSize()) * 2.0;

    if (Distance2D > SafetyRadius)
    {
        return;
    }

    double TerrainTopWorldZ = 0.0;

    if (!FindDensityTerrainTopAtWorldXY(PlayerLocation, TerrainTopWorldZ))
    {
        return;
    }

    const double MinimumPlayerZ =
        TerrainTopWorldZ +
        Capsule->GetScaledCapsuleHalfHeight() +
        12.0;

    if (PlayerLocation.Z >= MinimumPlayerZ)
    {
        return;
    }

    FVector NewPlayerLocation = PlayerLocation;
    NewPlayerLocation.Z = MinimumPlayerZ;

    PlayerCharacter->SetActorLocation(
        NewPlayerLocation,
        false,
        nullptr,
        ETeleportType::TeleportPhysics
    );
}

void ATerraforgeWorldActor::MarkDensityVoxelModified(
    const FIntVector& WorldVoxelCoord
)
{
    const FIntVector ChunkCoord =
        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

    const FIntVector LocalCoord =
        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

    if (
        LocalCoord.X < 0 ||
        LocalCoord.Y < 0 ||
        LocalCoord.Z < 0 ||
        LocalCoord.X >= TerraforgeVoxel::ChunkSize ||
        LocalCoord.Y >= TerraforgeVoxel::ChunkSize ||
        LocalCoord.Z >= TerraforgeVoxel::ChunkSize
        )
    {
        return;
    }

    const int32 VoxelIndex =
        LocalCoord.X +
        LocalCoord.Y * TerraforgeVoxel::ChunkSize +
        LocalCoord.Z * TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize;

    ModifiedDensityChunks.Add(ChunkCoord);

    TSet<int32>& ModifiedVoxelIndices =
        ModifiedDensityVoxelIndicesByChunk.FindOrAdd(ChunkCoord);

    ModifiedVoxelIndices.Add(VoxelIndex);
}

void ATerraforgeWorldActor::MarkBlockVoxelModified(
    const FIntVector& WorldVoxelCoord
)
{
    const FIntVector ChunkCoord =
        TerraforgeVoxel::WorldVoxelToChunkCoord(WorldVoxelCoord);

    const FIntVector LocalCoord =
        TerraforgeVoxel::WorldVoxelToLocalCoord(WorldVoxelCoord);

    const int32 VoxelIndex =
        LocalCoord.X +
        LocalCoord.Y * TerraforgeVoxel::ChunkSize +
        LocalCoord.Z * TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize;

    ModifiedBlockChunks.Add(ChunkCoord);

    TSet<int32>& ModifiedVoxelIndices =
        ModifiedBlockVoxelIndicesByChunk.FindOrAdd(ChunkCoord);

    ModifiedVoxelIndices.Add(VoxelIndex);
}

bool ATerraforgeWorldActor::HasModifiedBlockWorld() const
{
    if (BlockVoxelOverridesByChunk.Num() > 0)
    {
        return true;
    }

    for (const TPair<FIntVector, TSet<int32>>& Pair : ModifiedBlockVoxelIndicesByChunk)
    {
        if (Pair.Value.Num() > 0)
        {
            return true;
        }
    }

    return false;
}

bool ATerraforgeWorldActor::HasModifiedWorld() const
{
    switch (TerrainSystem)
    {
    case ETerraforgeTerrainSystem::Block:
    {
        return HasModifiedBlockWorld();
    }

    case ETerraforgeTerrainSystem::SmoothDensity:
    default:
    {
        return HasModifiedDensityWorld();
    }
    }
}

bool ATerraforgeWorldActor::HasModifiedDensityWorld() const
{
    for (const TPair<FIntVector, TSet<int32>>& Pair : ModifiedDensityVoxelIndicesByChunk)
    {
        if (Pair.Value.Num() > 0)
        {
            return true;
        }
    }

    return false;
}

bool ATerraforgeWorldActor::SaveWorldToSlot(
    const FString& SlotName,
    const int32 UserIndex
)
{
    if (bGenerationInProgress || bIsApplyingGeneratedMeshes || bAsyncRebuildInProgress)
    {
        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Cannot save Terraforge world while generation or rebuild is in progress.")
        );

        return false;
    }

    UTerraforgeWorldSaveGame* SaveGame =
        Cast<UTerraforgeWorldSaveGame>(
            UGameplayStatics::CreateSaveGameObject(
                UTerraforgeWorldSaveGame::StaticClass()
            )
        );

    if (SaveGame == nullptr)
    {
        return false;
    }

    SaveGame->SaveVersion = 1;
    SaveGame->TerrainSystem = TerrainSystem;
    SaveGame->ChunkSize = TerraforgeVoxel::ChunkSize;
    SaveGame->VoxelSize = GetBaseVoxelSize();
    SaveGame->RadiusInChunks = RadiusInChunks;
    SaveGame->DensityMinChunkZ = DensityMinChunkZ;
    SaveGame->DensityMaxChunkZ = DensityMaxChunkZ;
    SaveGame->BlockMinChunkZ = BlockMinChunkZ;
    SaveGame->BlockMaxChunkZ = BlockMaxChunkZ;

    int32 SavedVoxelCount = 0;

    if (TerrainSystem == ETerraforgeTerrainSystem::SmoothDensity)
    {
        for (const TPair<FIntVector, TSet<int32>>& Pair : ModifiedDensityVoxelIndicesByChunk)
        {
            const FIntVector ChunkCoord = Pair.Key;

            const FTerraforgeDensityChunkData* DensityChunkData =
                DensityChunkDataByCoord.Find(ChunkCoord);

            if (DensityChunkData == nullptr)
            {
                continue;
            }

            FTerraforgeSavedDensityChunkV2 SavedChunk;
            SavedChunk.ChunkCoord = ChunkCoord;

            for (const int32 VoxelIndex : Pair.Value)
            {
                if (!DensityChunkData->Voxels.IsValidIndex(VoxelIndex))
                {
                    continue;
                }

                const FTerraforgeDensityVoxel& Voxel =
                    DensityChunkData->Voxels[VoxelIndex];

                FTerraforgeSavedDensityVoxelV2 SavedVoxel;
                SavedVoxel.VoxelIndex = VoxelIndex;
                SavedVoxel.Density = Voxel.Density;
                SavedVoxel.MaterialId = static_cast<int32>(Voxel.MaterialId);

                SavedChunk.Voxels.Add(SavedVoxel);
                ++SavedVoxelCount;
            }

            if (SavedChunk.Voxels.Num() > 0)
            {
                SaveGame->SavedDensityChunks.Add(MoveTemp(SavedChunk));
            }
        }
    }
    else
    {
        for (const TPair<FIntVector, TMap<int32, uint16>>& ChunkPair : BlockVoxelOverridesByChunk)
        {
            FTerraforgeSavedBlockChunk SavedChunk;
            SavedChunk.ChunkCoord = ChunkPair.Key;

            for (const TPair<int32, uint16>& VoxelPair : ChunkPair.Value)
            {
                const int32 VoxelIndex = VoxelPair.Key;

                if (
                    VoxelIndex < 0 ||
                    VoxelIndex >= TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize
                    )
                {
                    continue;
                }

                FTerraforgeSavedBlockVoxel SavedVoxel;
                SavedVoxel.VoxelIndex = VoxelIndex;
                SavedVoxel.MaterialId = static_cast<int32>(VoxelPair.Value);

                SavedChunk.Voxels.Add(SavedVoxel);
                ++SavedVoxelCount;
            }

            if (SavedChunk.Voxels.Num() > 0)
            {
                SaveGame->SavedBlockChunks.Add(MoveTemp(SavedChunk));
            }
        }
    }

    const bool bSaved =
        UGameplayStatics::SaveGameToSlot(
            SaveGame,
            SlotName,
            UserIndex
        );

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Terraforge world save %s. Slot=%s TerrainSystem=%d DensityChunks=%d BlockChunks=%d Voxels=%d"),
        bSaved ? TEXT("succeeded") : TEXT("failed"),
        *SlotName,
        static_cast<int32>(TerrainSystem),
        SaveGame->SavedDensityChunks.Num(),
        SaveGame->SavedBlockChunks.Num(),
        SavedVoxelCount
    );

    return bSaved;
}

bool ATerraforgeWorldActor::LoadWorldFromSlot(
    const FString& SlotName,
    const int32 UserIndex
)
{
    if (!UGameplayStatics::DoesSaveGameExist(SlotName, UserIndex))
    {
        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("No Terraforge world save exists. Slot=%s"),
            *SlotName
        );

        return false;
    }

    UTerraforgeWorldSaveGame* SaveGame =
        Cast<UTerraforgeWorldSaveGame>(
            UGameplayStatics::LoadGameFromSlot(
                SlotName,
                UserIndex
            )
        );

    if (SaveGame == nullptr)
    {
        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Failed to load Terraforge world save. Slot=%s"),
            *SlotName
        );

        return false;
    }

    if (SaveGame->ChunkSize != TerraforgeVoxel::ChunkSize)
    {
        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Terraforge save chunk size mismatch. Save=%d Current=%d"),
            SaveGame->ChunkSize,
            TerraforgeVoxel::ChunkSize
        );

        return false;
    }

    const ETerraforgeTerrainSystem ActorTerrainSystem = TerrainSystem;
    const ETerraforgeTerrainSystem SavedTerrainSystem = SaveGame->TerrainSystem;

    if (
        LoadTerrainPolicy == ETerraforgeWorldLoadTerrainPolicy::SkipIfDifferent &&
        ActorTerrainSystem != SavedTerrainSystem
        )
    {
        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Skipped Terraforge world load because saved terrain system differs from actor setting. Actor=%d Save=%d Slot=%s"),
            static_cast<int32>(ActorTerrainSystem),
            static_cast<int32>(SavedTerrainSystem),
            *SlotName
        );

        return false;
    }

    ETerraforgeTerrainSystem TerrainSystemToLoad = ActorTerrainSystem;

    if (LoadTerrainPolicy == ETerraforgeWorldLoadTerrainPolicy::UseSaveTerrainSystem)
    {
        TerrainSystemToLoad = SavedTerrainSystem;
        TerrainSystem = SavedTerrainSystem;
    }

    RadiusInChunks = SaveGame->RadiusInChunks;
    DensityMinChunkZ = SaveGame->DensityMinChunkZ;
    DensityMaxChunkZ = SaveGame->DensityMaxChunkZ;
    BlockMinChunkZ = SaveGame->BlockMinChunkZ;
    BlockMaxChunkZ = SaveGame->BlockMaxChunkZ;

    switch (TerrainSystemToLoad)
    {
    case ETerraforgeTerrainSystem::Block:
    {
        return LoadBlockWorldFromSave(SaveGame);
    }

    case ETerraforgeTerrainSystem::SmoothDensity:
    default:
    {
        return LoadSmoothDensityWorldFromSave(SaveGame);
    }
    }
}

bool ATerraforgeWorldActor::LoadSmoothDensityWorldFromSave(
    const UTerraforgeWorldSaveGame* SaveGame
)
{
    if (SaveGame == nullptr)
    {
        return false;
    }

    MarkGenerationStarted();

    ClearWorld();

    RadiusInChunks = SaveGame->RadiusInChunks;
    DensityMinChunkZ = SaveGame->DensityMinChunkZ;
    DensityMaxChunkZ = SaveGame->DensityMaxChunkZ;

    const int32 SafeRadiusInChunks =
        FMath::Max(0, RadiusInChunks);

    int32 EffectiveMinChunkZ = DensityMinChunkZ;
    int32 EffectiveMaxChunkZ = DensityMaxChunkZ;

    if (EffectiveMinChunkZ > EffectiveMaxChunkZ)
    {
        Swap(EffectiveMinChunkZ, EffectiveMaxChunkZ);
    }

    /*
     * Rebuild procedural smooth-density base.
     */
    for (int32 Z = EffectiveMinChunkZ; Z <= EffectiveMaxChunkZ; ++Z)
    {
        for (int32 Y = -SafeRadiusInChunks; Y <= SafeRadiusInChunks; ++Y)
        {
            for (int32 X = -SafeRadiusInChunks; X <= SafeRadiusInChunks; ++X)
            {
                const FIntVector ChunkCoord(X, Y, Z);

                FTerraforgeDensityChunkData DensityChunkData(ChunkCoord);

                FillDensityChunkFromTerrainSampler(DensityChunkData);

                if (DensityChunkData.HasAnySolidVoxel())
                {
                    DensityChunkDataByCoord.Add(
                        ChunkCoord,
                        MoveTemp(DensityChunkData)
                    );
                    bDensityChunkBoundsDirty = true;
                }
            }
        }
    }

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Rebuilt Terraforge smooth density base for load. DataChunks=%d Radius=%d MinZ=%d MaxZ=%d"),
        DensityChunkDataByCoord.Num(),
        RadiusInChunks,
        EffectiveMinChunkZ,
        EffectiveMaxChunkZ
    );

    /*
     * Overlay saved density edits.
     */
    ModifiedDensityChunks.Reset();
    ModifiedDensityVoxelIndicesByChunk.Reset();

    for (const FTerraforgeSavedDensityChunkV2& SavedChunk : SaveGame->SavedDensityChunks)
    {
        FTerraforgeDensityChunkData* DensityChunkData =
            DensityChunkDataByCoord.Find(SavedChunk.ChunkCoord);

        if (DensityChunkData == nullptr)
        {
            FTerraforgeDensityChunkData NewDensityChunkData(SavedChunk.ChunkCoord);

            FillDensityChunkFromTerrainSampler(NewDensityChunkData);

            DensityChunkDataByCoord.Add(
                SavedChunk.ChunkCoord,
                MoveTemp(NewDensityChunkData)
            );
            bDensityChunkBoundsDirty = true;

            DensityChunkData =
                DensityChunkDataByCoord.Find(SavedChunk.ChunkCoord);
        }

        if (DensityChunkData == nullptr)
        {
            continue;
        }

        TSet<int32>& ModifiedIndices =
            ModifiedDensityVoxelIndicesByChunk.FindOrAdd(SavedChunk.ChunkCoord);

        for (const FTerraforgeSavedDensityVoxelV2& SavedVoxel : SavedChunk.Voxels)
        {
            if (!DensityChunkData->Voxels.IsValidIndex(SavedVoxel.VoxelIndex))
            {
                continue;
            }

            FTerraforgeDensityVoxel Voxel;
            Voxel.Density = SavedVoxel.Density;
            Voxel.MaterialId =
                SavedVoxel.Density > 0.0f
                ? static_cast<uint16>(FMath::Clamp(SavedVoxel.MaterialId, 1, 65535))
                : 0;

            DensityChunkData->Voxels[SavedVoxel.VoxelIndex] = Voxel;

            ModifiedIndices.Add(SavedVoxel.VoxelIndex);
            ModifiedDensityChunks.Add(SavedChunk.ChunkCoord);
        }
    }

    /*
     * Generate loaded smooth meshes and apply them batched.
     */
    TMap<FIntVector, FTerraforgeVoxelMeshData> LoadedMeshData;

    const int32 SafeMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    for (const TPair<FIntVector, FTerraforgeDensityChunkData>& Pair : DensityChunkDataByCoord)
    {
        const FIntVector ChunkCoord = Pair.Key;

        FTerraforgeVoxelMeshData MeshData;

        FTerraforgeMarchingCubes::GenerateMesh(
            ChunkCoord,
            [this](const FIntVector& WorldVoxelCoord) -> float
            {
                return GetStoredDensityAtWorldVoxel(WorldVoxelCoord);
            },
            [this](const FIntVector& WorldVoxelCoord) -> uint16
            {
                return GetStoredDensityMaterialAtWorldVoxel(WorldVoxelCoord);
            },
            MeshData,
            SafeMeshStep,
            bSmoothNormalsAcrossMaterialBoundaries
        );

        if (!MeshData.IsEmpty())
        {
            LoadedMeshData.Add(
                ChunkCoord,
                MoveTemp(MeshData)
            );
        }
    }

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Generated Terraforge smooth density load meshes. MeshChunks=%d"),
        LoadedMeshData.Num()
    );

    BeginBatchedGeneratedMeshApply(
        MoveTemp(LoadedMeshData)
    );

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Loaded Terraforge smooth density world save. SavedChunks=%d"),
        SaveGame->SavedDensityChunks.Num()
    );

    return true;
}

bool ATerraforgeWorldActor::LoadBlockWorldFromSave(
    const UTerraforgeWorldSaveGame* SaveGame
)
{
    if (SaveGame == nullptr)
    {
        return false;
    }

    MarkGenerationStarted();

    ClearWorld();

    RadiusInChunks = SaveGame->RadiusInChunks;
    BlockMinChunkZ = SaveGame->BlockMinChunkZ;
    BlockMaxChunkZ = SaveGame->BlockMaxChunkZ;

    BlockVoxelOverridesByChunk.Reset();
    ModifiedBlockChunks.Reset();
    ModifiedBlockVoxelIndicesByChunk.Reset();
    ProcessedEmptyStreamingChunks.Reset();

    for (const FTerraforgeSavedBlockChunk& SavedChunk : SaveGame->SavedBlockChunks)
    {
        TMap<int32, uint16>& ChunkOverrides =
            BlockVoxelOverridesByChunk.FindOrAdd(SavedChunk.ChunkCoord);

        TSet<int32>& ModifiedIndices =
            ModifiedBlockVoxelIndicesByChunk.FindOrAdd(SavedChunk.ChunkCoord);

        for (const FTerraforgeSavedBlockVoxel& SavedVoxel : SavedChunk.Voxels)
        {
            const int32 VoxelIndex = SavedVoxel.VoxelIndex;

            if (
                VoxelIndex < 0 ||
                VoxelIndex >= TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize
                )
            {
                continue;
            }

            const uint16 MaterialId =
                static_cast<uint16>(
                    FMath::Clamp(SavedVoxel.MaterialId, 0, 65535)
                    );

            ChunkOverrides.Add(
                VoxelIndex,
                MaterialId
            );

            ModifiedIndices.Add(VoxelIndex);
            ModifiedBlockChunks.Add(SavedChunk.ChunkCoord);
        }
    }

    if (ShouldUseRuntimeStreaming())
    {
        PendingStreamingChunkCoords.Reset();
        PendingStreamingChunkCoordSet.Reset();
        GeneratedEmptyStreamingChunks.Reset();
        bRuntimeStreamingInitialized = false;

        UpdateRuntimeStreaming();

        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("Loaded Terraforge streamed block overlay. SavedChunks=%d Overrides=%d"),
            SaveGame->SavedBlockChunks.Num(),
            BlockVoxelOverridesByChunk.Num()
        );

        return true;
    }

    BlockChunkDataByCoord.Reset();

    const int32 SafeRadiusInChunks =
        FMath::Max(0, RadiusInChunks);

    int32 EffectiveBlockMinChunkZ = BlockMinChunkZ;
    int32 EffectiveBlockMaxChunkZ = BlockMaxChunkZ;

    if (EffectiveBlockMinChunkZ > EffectiveBlockMaxChunkZ)
    {
        Swap(EffectiveBlockMinChunkZ, EffectiveBlockMaxChunkZ);
    }

    for (int32 Z = EffectiveBlockMinChunkZ; Z <= EffectiveBlockMaxChunkZ; ++Z)
    {
        for (int32 Y = -SafeRadiusInChunks; Y <= SafeRadiusInChunks; ++Y)
        {
            for (int32 X = -SafeRadiusInChunks; X <= SafeRadiusInChunks; ++X)
            {
                const FIntVector ChunkCoord(X, Y, Z);

                FTerraforgeVoxelChunkData ChunkData(ChunkCoord);

                GenerateBlockChunkData(ChunkData);

                ApplyBlockVoxelOverridesToChunk(
                    ChunkCoord,
                    ChunkData
                );

                if (ChunkData.HasAnySolidVoxel())
                {
                    BlockChunkDataByCoord.Add(
                        ChunkCoord,
                        MoveTemp(ChunkData)
                    );
                }
            }
        }
    }

    for (const TPair<FIntVector, TMap<int32, uint16>>& OverridePair : BlockVoxelOverridesByChunk)
    {
        if (BlockChunkDataByCoord.Contains(OverridePair.Key))
        {
            continue;
        }

        FTerraforgeVoxelChunkData ChunkData(OverridePair.Key);

        GenerateBlockChunkData(ChunkData);

        ApplyBlockVoxelOverridesToChunk(
            OverridePair.Key,
            ChunkData
        );

        if (ChunkData.HasAnySolidVoxel())
        {
            BlockChunkDataByCoord.Add(
                OverridePair.Key,
                MoveTemp(ChunkData)
            );
        }
    }

    TMap<FIntVector, FTerraforgeVoxelMeshData> LoadedMeshData;

    for (const TPair<FIntVector, FTerraforgeVoxelChunkData>& Pair : BlockChunkDataByCoord)
    {
        const FIntVector ChunkCoord = Pair.Key;
        const FTerraforgeVoxelChunkData& ChunkData = Pair.Value;

        FTerraforgeVoxelMeshData MeshData;

        FTerraforgeVoxelMesher::GenerateGreedyMeshNeighbourAware(
            ChunkData,
            [this](const FIntVector& WorldVoxelCoord) -> uint16
            {
                return GetBlockMaterialAtWorldVoxel(WorldVoxelCoord);
            },
            MeshData
        );

        if (!MeshData.IsEmpty())
        {
            LoadedMeshData.Add(
                ChunkCoord,
                MoveTemp(MeshData)
            );
        }
    }

    BeginBatchedGeneratedMeshApply(
        MoveTemp(LoadedMeshData)
    );

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Loaded Terraforge block world save. SavedChunks=%d Overrides=%d"),
        SaveGame->SavedBlockChunks.Num(),
        BlockVoxelOverridesByChunk.Num()
    );

    return true;
}

bool ATerraforgeWorldActor::ClearSavedWorldSlot(
    const FString& SlotName,
    const int32 UserIndex
)
{
    if (!UGameplayStatics::DoesSaveGameExist(SlotName, UserIndex))
    {
        UE_LOG(
            LogTerraforgeVoxel,
            Warning,
            TEXT("No Terraforge world save to clear. Slot=%s"),
            *SlotName
        );

        return false;
    }

    const bool bDeleted =
        UGameplayStatics::DeleteGameInSlot(
            SlotName,
            UserIndex
        );

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Terraforge world save clear %s. Slot=%s"),
        bDeleted ? TEXT("succeeded") : TEXT("failed"),
        *SlotName
    );

    return bDeleted;
}

bool ATerraforgeWorldActor::SaveDefaultWorld()
{
    return SaveWorldToSlot(
        DefaultSaveSlotName,
        DefaultSaveUserIndex
    );
}

bool ATerraforgeWorldActor::LoadDefaultWorld()
{
    return LoadWorldFromSlot(
        DefaultSaveSlotName,
        DefaultSaveUserIndex
    );
}

bool ATerraforgeWorldActor::ClearDefaultSavedWorld()
{
    return ClearSavedWorldSlot(
        DefaultSaveSlotName,
        DefaultSaveUserIndex
    );
}

void ATerraforgeWorldActor::PrimaryEditAtCameraTrace()
{
    if (!CanEditWorldNow())
    {
        return;
    }

    if (TerrainSystem == ETerraforgeTerrainSystem::Block)
    {
        RemoveBlocksAtCameraTrace(
            BlockEditRange,
            BlockEditTraceDistance
        );

        return;
    }

    FHitResult Hit;

    if (!GetCameraTraceHit(Hit, EditTraceDistance))
    {
        return;
    }

    const FVector TraceDirection =
        (Hit.TraceEnd - Hit.TraceStart).GetSafeNormal();

    const FVector RemoveLocation =
        Hit.ImpactPoint + TraceDirection * EditRadius * 0.20f;

    RemoveDensityInSphere(
        RemoveLocation,
        EditRadius,
        EditStrength
    );
}

void ATerraforgeWorldActor::SecondaryEditAtCameraTrace()
{
    if (!CanEditWorldNow())
    {
        return;
    }

    if (TerrainSystem == ETerraforgeTerrainSystem::Block)
    {
        AddBlocksAtCameraTrace(
            BlockEditRange,
            BlockEditTraceDistance,
            BlockAddMaterialId
        );

        return;
    }

    FHitResult Hit;

    if (!GetCameraTraceHit(Hit, EditTraceDistance))
    {
        return;
    }

    const FVector AddLocation =
        Hit.ImpactPoint + Hit.ImpactNormal * EditRadius * 0.20f;

    AddDensityInSphere(
        AddLocation,
        EditRadius,
        EditStrength,
        AddMaterialId
    );
}

void ATerraforgeWorldActor::StartPrimaryEdit()
{
    if (EditInputMode == ETerraforgeEditInputMode::SingleClick)
    {
        PrimaryEditAtCameraTrace();
        return;
    }

    bIsRemovingDensity = true;
    TimeSinceLastEdit = EditInterval;
}

void ATerraforgeWorldActor::StopPrimaryEdit()
{
    bIsRemovingDensity = false;
}

void ATerraforgeWorldActor::StartSecondaryEdit()
{
    if (EditInputMode == ETerraforgeEditInputMode::SingleClick)
    {
        SecondaryEditAtCameraTrace();
        return;
    }

    bIsAddingDensity = true;
    TimeSinceLastEdit = EditInterval;
}

void ATerraforgeWorldActor::StopSecondaryEdit()
{
    bIsAddingDensity = false;
}

bool ATerraforgeWorldActor::FindTerrainSpawnSurfaceAtWorldXY(
    const FVector WorldLocation,
    double& OutSurfaceWorldZ
) const
{
    if (TerrainSystem == ETerraforgeTerrainSystem::Block)
    {
        return FindBlockTerrainTopAtWorldXY(
            WorldLocation,
            OutSurfaceWorldZ
        );
    }

    return FindDensitySurfaceAtWorldXY(
        WorldLocation,
        OutSurfaceWorldZ
    );
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

float ATerraforgeWorldActor::GetBaseVoxelSize() const
{
    return FMath::Max(1.0f, BaseVoxelSize);
}

double ATerraforgeWorldActor::GetChunkWorldSize() const
{
    return
        static_cast<double>(TerraforgeVoxel::ChunkSize) *
        static_cast<double>(GetBaseVoxelSize());
}

FIntVector ATerraforgeWorldActor::WorldLocationToVoxelCoord(
    const FVector& WorldLocation
) const
{
    const FVector LocalLocation =
        GetActorTransform().InverseTransformPosition(WorldLocation);

    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    return FIntVector(
        FMath::FloorToInt(LocalLocation.X / SafeVoxelSize),
        FMath::FloorToInt(LocalLocation.Y / SafeVoxelSize),
        FMath::FloorToInt(LocalLocation.Z / SafeVoxelSize)
    );
}

FVector ATerraforgeWorldActor::WorldVoxelCoordToLocalPosition(
    const FIntVector& WorldVoxelCoord
) const
{
    const double SafeVoxelSize =
        static_cast<double>(GetBaseVoxelSize());

    return FVector(
        static_cast<double>(WorldVoxelCoord.X) * SafeVoxelSize,
        static_cast<double>(WorldVoxelCoord.Y) * SafeVoxelSize,
        static_cast<double>(WorldVoxelCoord.Z) * SafeVoxelSize
    );
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

const FTerraforgeTerrainGenerationProfile&
ATerraforgeWorldActor::GetActiveTerrainGenerationProfile() const
{
    if (ActiveBiomeDefinition != nullptr)
    {
        return ActiveBiomeDefinition->GenerationProfile;
    }

    return FallbackGenerationProfile;
}

uint8 ATerraforgeWorldActor::GetActiveBiomeId() const
{
    if (ActiveBiomeDefinition != nullptr)
    {
        return static_cast<uint8>(
            FMath::Clamp(ActiveBiomeDefinition->BiomeId, 0, 255)
            );
    }

    return 0;
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

void ATerraforgeWorldActor::ApplyBlockVoxelOverridesToChunk(
    const FIntVector& ChunkCoord,
    FTerraforgeVoxelChunkData& ChunkData
) const
{
    const TMap<int32, uint16>* Overrides =
        BlockVoxelOverridesByChunk.Find(ChunkCoord);

    if (Overrides == nullptr)
    {
        return;
    }

    for (const TPair<int32, uint16>& Pair : *Overrides)
    {
        const int32 VoxelIndex = Pair.Key;

        if (!ChunkData.Voxels.IsValidIndex(VoxelIndex))
        {
            continue;
        }

        ChunkData.Voxels[VoxelIndex].MaterialId = Pair.Value;
    }
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
void ATerraforgeWorldActor::ClearChunkComponentForCoord(
    const FIntVector& ChunkCoord
)
{
    TObjectPtr<UTerraforgeVoxelChunkComponent>* ExistingComponentPtr =
        ActiveChunkComponentsByCoord.Find(ChunkCoord);

    if (ExistingComponentPtr == nullptr)
    {
        return;
    }

    UTerraforgeVoxelChunkComponent* ExistingComponent =
        ExistingComponentPtr->Get();

    if (!IsValid(ExistingComponent))
    {
        ActiveChunkComponentsByCoord.Remove(ChunkCoord);
        return;
    }

    ExistingComponent->ClearAllMeshSections();
    ExistingComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    ExistingComponent->SetVisibility(false, true);
    ExistingComponent->SetHiddenInGame(true);
    ExistingComponent->Deactivate();

    ActiveChunkComponentsByCoord.Remove(ChunkCoord);
    PooledChunkComponents.Add(ExistingComponent);
}

void ATerraforgeWorldActor::GenerateSmoothDensityMeshForChunk(
    const FIntVector& ChunkCoord,
    FTerraforgeVoxelMeshData& OutMesh
) const
{
    const int32 SafeMeshStep =
        FMath::Clamp(DensityMeshStep, 1, 8);

    FTerraforgeMarchingCubes::GenerateMesh(
        ChunkCoord,
        [this](const FIntVector& WorldVoxelCoord) -> float
        {
            return GetStoredDensityAtWorldVoxel(WorldVoxelCoord);
        },
        [this](const FIntVector& WorldVoxelCoord) -> uint16
        {
            return GetStoredDensityMaterialAtWorldVoxel(WorldVoxelCoord);
        },
        OutMesh,
        SafeMeshStep,
        bSmoothNormalsAcrossMaterialBoundaries
    );
}

void ATerraforgeWorldActor::AddDensityChunkAndNeighboursToDirtySet(
    const FIntVector& ChunkCoord,
    TSet<FIntVector>& DirtyChunks
) const
{
    for (int32 DZ = -1; DZ <= 1; ++DZ)
    {
        for (int32 DY = -1; DY <= 1; ++DY)
        {
            for (int32 DX = -1; DX <= 1; ++DX)
            {
                DirtyChunks.Add(
                    ChunkCoord + FIntVector(DX, DY, DZ)
                );
            }
        }
    }
}

void ATerraforgeWorldActor::BroadcastInitialTerrainReady()
{
    if (bInitialTerrainReady)
    {
        return;
    }

    FVector SuggestedSpawnLocation = GetActorLocation();

    UWorld* World = GetWorld();

    if (World != nullptr)
    {
        if (APlayerController* PlayerController = UGameplayStatics::GetPlayerController(World, 0))
        {
            if (APawn* ExistingPawn = PlayerController->GetPawn())
            {
                SuggestedSpawnLocation = ExistingPawn->GetActorLocation();
            }
        }
    }

    FVector SafeSpawnLocation = SuggestedSpawnLocation;

    FindSafeSpawnLocation(
        SuggestedSpawnLocation,
        5000.0f,
        SafeSpawnLocation
    );

    bInitialTerrainReady = true;

    OnInitialTerrainReady.Broadcast(SafeSpawnLocation);

    UE_LOG(
        LogTerraforgeVoxel,
        Warning,
        TEXT("Terraforge initial terrain ready. SuggestedSpawn=%s"),
        *SafeSpawnLocation.ToString()
    );
}
