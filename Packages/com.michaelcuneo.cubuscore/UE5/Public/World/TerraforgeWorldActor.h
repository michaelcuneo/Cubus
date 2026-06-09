#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "World/TerraforgeTerrainSystem.h"
#include "Terrain/TerraforgeTerrainGenerationProfile.h"
#include "Biome/TerraforgeBiomeDefinition.h"
#include "Density/TerraforgeDensityChunkData.h"
#include "Block/TerraforgeVoxelChunkData.h"
#include "Block/TerraforgeVoxelMesher.h"
#include "World/TerraforgeWorldSaveGame.h"
#include "Core/TerraforgeVoxelLog.h"
#include "TerraforgeWorldActor.generated.h"

class UTerraforgeVoxelChunkComponent;
class UMaterialInterface;
class USceneComponent;

USTRUCT(BlueprintType)
struct FTerraforgeTerrainSample
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain")
    float Density = -1.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain")
    int32 SolidMaterialId = 0;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain")
    int32 LiquidMaterialId = 0;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain")
    uint8 BiomeId = 0;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain")
    float SurfaceHeight = 0.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain")
    float CaveAmount = 0.0f;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terraforge|Terrain")
    bool bIsLiquid = false;
};

UENUM(BlueprintType)
enum class ETerraforgeWorldLoadTerrainPolicy : uint8
{
    UseSaveTerrainSystem UMETA(DisplayName = "Use Save Terrain System"),
    UseActorTerrainSystem UMETA(DisplayName = "Use Actor Terrain System"),
    SkipIfDifferent UMETA(DisplayName = "Skip If Different")
};


UENUM(BlueprintType)
enum class ETerraforgeWorldPersistenceMode : uint8
{
    ManualOnly UMETA(DisplayName = "Manual Only"),
    AutoLoadOnly UMETA(DisplayName = "Auto Load Only"),
    AutoSaveOnly UMETA(DisplayName = "Auto Save Only"),
    AutoLoadAndSave UMETA(DisplayName = "Auto Load and Save"),
    Disabled UMETA(DisplayName = "Disabled")
};

DECLARE_DYNAMIC_MULTICAST_DELEGATE(FTerraforgeWorldGenerationStarted);

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(
    FTerraforgeWorldGenerationProgress,
    float,
    Progress
);

DECLARE_DYNAMIC_MULTICAST_DELEGATE(FTerraforgeWorldGenerationComplete);

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(
    FTerraforgeInitialTerrainReady,
    FVector,
    SuggestedSpawnLocation
);

UENUM(BlueprintType)
enum class ETerraforgeWorldCollisionMode : uint8
{
    None UMETA(DisplayName = "None"),
    AllChunks UMETA(DisplayName = "All Chunks"),
    NearPlayerOnly UMETA(DisplayName = "Near Player Only")
};

UENUM(BlueprintType)
enum class ETerraforgeEditInputMode : uint8
{
    SingleClick UMETA(DisplayName = "Single Click"),
    ContinuousHold UMETA(DisplayName = "Continuous Hold")
};

UCLASS()
class TERRAFORGEVOXEL_API ATerraforgeWorldActor : public AActor
{
    GENERATED_BODY()

public:
    ATerraforgeWorldActor();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    void GenerateWorld();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    void ClearWorld();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    bool IsWorldReady() const;

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    float GetGenerationProgress() const;

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    bool FindSafeSpawnLocation(
        FVector DesiredWorldLocation,
        float SearchRadius,
        FVector& OutSpawnLocation
    ) const;

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    bool FindDensitySurfaceAtWorldXY(
        const FVector WorldLocation,
        double& OutSurfaceWorldZ
    ) const;

    /*
     * Preferred generic edit API.
     */
    UFUNCTION(BlueprintCallable, Category = "Terraforge|Editing")
    void StartPrimaryEdit();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Editing")
    void StopPrimaryEdit();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Editing")
    void StartSecondaryEdit();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Editing")
    void StopSecondaryEdit();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Editing")
    void PrimaryEditAtCameraTrace();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Editing")
    void SecondaryEditAtCameraTrace();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Persistence")
    bool SaveWorldToSlot(
        const FString& SlotName,
        int32 UserIndex
    );

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Persistence")
    bool LoadWorldFromSlot(
        const FString& SlotName,
        int32 UserIndex
    );

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Persistence")
    bool ClearSavedWorldSlot(
        const FString& SlotName,
        int32 UserIndex
    );

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Persistence")
    bool SaveDefaultWorld();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Persistence")
    bool LoadDefaultWorld();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|Persistence")
    bool ClearDefaultSavedWorld();

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    bool IsInitialTerrainReady() const;

    UFUNCTION(BlueprintCallable, Category = "Terraforge|World")
    bool GetSuggestedPlayerSpawnLocation(
        FVector DesiredWorldLocation,
        float SearchRadius,
        FVector& OutSpawnLocation
    ) const;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Persistence")
    ETerraforgeWorldPersistenceMode PersistenceMode =
        ETerraforgeWorldPersistenceMode::ManualOnly;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Persistence")
    ETerraforgeWorldLoadTerrainPolicy LoadTerrainPolicy =
        ETerraforgeWorldLoadTerrainPolicy::SkipIfDifferent;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Editing")
    ETerraforgeEditInputMode EditInputMode =
        ETerraforgeEditInputMode::ContinuousHold;

    UPROPERTY(BlueprintAssignable, Category = "Terraforge|World")
    FTerraforgeWorldGenerationStarted OnGenerationStarted;

    UPROPERTY(BlueprintAssignable, Category = "Terraforge|World")
    FTerraforgeWorldGenerationProgress OnGenerationProgress;

    UPROPERTY(BlueprintAssignable, Category = "Terraforge|World")
    FTerraforgeWorldGenerationComplete OnGenerationComplete;

    UPROPERTY(BlueprintAssignable, Category = "Terraforge|World")
    FTerraforgeInitialTerrainReady OnInitialTerrainReady;


protected:
    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;
    virtual void OnConstruction(const FTransform& Transform) override;
    virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

private:
    UPROPERTY(VisibleAnywhere, Category = "Terraforge|World")
    TObjectPtr<USceneComponent> SceneRoot;

    UPROPERTY(EditAnywhere, Category = "Terraforge|World")
    ETerraforgeTerrainSystem TerrainSystem =
        ETerraforgeTerrainSystem::SmoothDensity;

    UPROPERTY(EditAnywhere, Category = "Terraforge|World", meta = (ClampMin = "0", ClampMax = "32"))
    int32 RadiusInChunks = 3;

    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Terraforge|World", meta = (ClampMin = "10.0", ClampMax = "1000.0", AllowPrivateAccess = "true"))
    float BaseVoxelSize = 100.0f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming")
    bool bEnableRuntimeStreaming = false;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "1", ClampMax = "64"))
    int32 ViewDistanceInChunks = 4;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "0", ClampMax = "8"))
    int32 StreamingUnloadPaddingInChunks = 1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "1", ClampMax = "128"))
    int32 StreamingChunksGeneratedPerFrame = 4;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "1", ClampMax = "512"))
    int32 InitialStreamingChunksGeneratedPerFrame = 64;

    UPROPERTY(EditAnywhere, Category = "Terraforge|World")
    bool bGenerateOnBeginPlay = true;

    UPROPERTY(EditAnywhere, Category = "Terraforge|World")
    bool bGenerateInEditor = false;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Density", meta = (ClampMin = "-8", ClampMax = "0"))
    int32 DensityMinChunkZ = -1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Density", meta = (ClampMin = "0", ClampMax = "8"))
    int32 DensityMaxChunkZ = 2;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Density", meta = (ClampMin = "1", ClampMax = "8"))
    int32 DensityMeshStep = 2;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Density")
    bool bSmoothNormalsAcrossMaterialBoundaries = false;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Block", meta = (ClampMin = "-16", ClampMax = "0"))
    int32 BlockMinChunkZ = -1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Block", meta = (ClampMin = "0", ClampMax = "16"))
    int32 BlockMaxChunkZ = 2;

    TArray<FIntVector> PendingStreamingChunkCoords;
    TSet<FIntVector> PendingStreamingChunkCoordSet;
    int32 PendingStreamingChunkQueueHead = 0;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Performance", meta = (ClampMin = "1", ClampMax = "32"))
    int32 StreamingMeshAppliesPerFrame = 1;

    TMap<FIntVector, FTerraforgeVoxelMeshData> PendingStreamingMeshData;
    TArray<FIntVector> PendingStreamingMeshCoords;
    TSet<FIntVector> PendingStreamingMeshCoordSet;
    int32 PendingStreamingMeshApplyIndex = 0;

    void QueueStreamingMeshApply(
        const FIntVector& ChunkCoord,
        FTerraforgeVoxelMeshData&& MeshData
    );

    void TickStreamingMeshApply();

    bool bRuntimeStreamingInitialized = false;
    bool bCachedStreamingChunkSetsValid = false;
    FIntVector CachedStreamingViewerChunkCoord = FIntVector::ZeroValue;
    int32 CachedStreamingViewDistanceInChunks = INDEX_NONE;
    int32 CachedStreamingUnloadPaddingInChunks = INDEX_NONE;
    int32 CachedSmoothStreamingChunksBelowSurface = INDEX_NONE;
    int32 CachedSmoothStreamingChunksAboveSurface = INDEX_NONE;
    ETerraforgeTerrainSystem CachedStreamingTerrainSystem =
        ETerraforgeTerrainSystem::SmoothDensity;
    TSet<FIntVector> CachedDesiredStreamingChunkCoords;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Performance", meta = (ClampMin = "1", ClampMax = "64"))
    int32 MaxAsyncStreamingChunkTasks = 8;

    TSet<FIntVector> AsyncStreamingChunkCoordSet;
    int32 ActiveAsyncStreamingChunkTasks = 0;
    int32 ActiveStreamingGenerationId = 0;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Biome")
    TObjectPtr<UTerraforgeBiomeDefinition> ActiveBiomeDefinition;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Biome")
    FTerraforgeTerrainGenerationProfile FallbackGenerationProfile;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Collision")
    bool bGenerateCollision = true;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Collision")
    ETerraforgeWorldCollisionMode CollisionMode =
        ETerraforgeWorldCollisionMode::NearPlayerOnly;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Collision", meta = (ClampMin = "500.0", ClampMax = "50000.0"))
    float CollisionActivationRadius = 5000.0f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Collision", meta = (ClampMin = "0.05", ClampMax = "5.0"))
    float CollisionUpdateInterval = 0.25f;

    float TimeSinceLastCollisionUpdate = 0.0f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Rendering")
    TObjectPtr<UMaterialInterface> WorldMaterial;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Performance", meta = (ClampMin = "1", ClampMax = "64"))
    int32 MeshAppliesPerFrame = 1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Smooth Density|Performance", meta = (ClampMin = "1", ClampMax = "32"))
    int32 DensityRebuildMeshAppliesPerFrame = 1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Editing", meta = (ClampMin = "50.0", ClampMax = "5000.0"))
    float EditRadius = 500.0f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Editing", meta = (ClampMin = "0.05", ClampMax = "100.0"))
    float EditStrength = 1.5f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Editing", meta = (ClampMin = "1000.0", ClampMax = "100000.0"))
    float EditTraceDistance = 30000.0f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Editing", meta = (ClampMin = "0.01", ClampMax = "2.0"))
    float EditInterval = 0.08f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Editing", meta = (ClampMin = "1", ClampMax = "65535"))
    int32 AddMaterialId = 1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Block Editing", meta = (ClampMin = "1", ClampMax = "16"))
    int32 BlockEditRange = 1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Block Editing", meta = (ClampMin = "1000.0", ClampMax = "100000.0"))
    float BlockEditTraceDistance = 30000.0f;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Block Editing", meta = (ClampMin = "1", ClampMax = "65535"))
    int32 BlockAddMaterialId = 1;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Persistence")
    FString DefaultSaveSlotName = TEXT("TerraforgeWorld");

    UPROPERTY(EditAnywhere, Category = "Terraforge|Persistence")
    int32 DefaultSaveUserIndex = 0;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Persistence")
    bool bGenerateIfNoSaveExists = true;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Persistence")
    bool bAutoSaveOnlyWhenModified = true;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Smooth Density", meta = (ClampMin = "0", ClampMax = "16"))
    int32 SmoothStreamingChunksBelowSurface = 2;

    UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Smooth Density", meta = (ClampMin = "0", ClampMax = "16"))
    int32 SmoothStreamingChunksAboveSurface = 2;

    bool bIsRemovingDensity = false;
    bool bIsAddingDensity = false;
    float TimeSinceLastEdit = 0.0f;

    UPROPERTY(Transient)
    TMap<FIntVector, TObjectPtr<UTerraforgeVoxelChunkComponent>> ActiveChunkComponentsByCoord;

    UPROPERTY(Transient)
    TArray<TObjectPtr<UTerraforgeVoxelChunkComponent>> PooledChunkComponents;

    bool bWorldReady = false;

    bool bGenerationInProgress = false;
    int32 ActiveGenerationId = 0;

    TMap<FIntVector, FTerraforgeVoxelMeshData> PendingGeneratedMeshData;
    TArray<FIntVector> PendingGeneratedMeshCoords;

    int32 PendingGeneratedMeshApplyIndex = 0;
    bool bIsApplyingGeneratedMeshes = false;

    float GenerationProgress = 0.0f;
    int32 TotalMeshesToApply = 0;
    int32 AppliedMeshes = 0;

    bool bAsyncRebuildInProgress = false;
    int32 ActiveAsyncRebuildId = 0;

    TSet<FIntVector> PendingDirtyChunks;

    TMap<FIntVector, FTerraforgeVoxelMeshData> PendingDensityRebuildMeshData;
    TArray<FIntVector> PendingDensityRebuildMeshCoords;
    int32 PendingDensityRebuildMeshApplyIndex = 0;

    bool bIsEndingPlay = false;

    bool bHasPendingPlayerSafetyCheck = false;
    FVector PendingPlayerSafetyLocation = FVector::ZeroVector;
    float PendingPlayerSafetyRadius = 0.0f;

private:
    void GenerateBlockWorld();
    void GenerateSmoothDensityWorld();

    UTerraforgeVoxelChunkComponent* AcquireChunkComponent(
        const FIntVector& ChunkCoord,
        bool bInGenerateCollision
    );

    void ReleaseAllChunkComponents();
    void DestroyAllChunkComponents();

    float GetDensityAtWorldVoxelPosition(
        const FVector& WorldVoxelPosition
    ) const;

    float GetStoredDensityAtWorldVoxel(
        const FIntVector& WorldVoxelCoord
    ) const;

    uint16 GetStoredDensityMaterialAtWorldVoxel(
        const FIntVector& WorldVoxelCoord
    ) const;

    void ApplyMeshToChunk(
        const FIntVector& ChunkCoord,
        FTerraforgeVoxelMeshData& MeshData
    );

    TMap<FIntVector, FTerraforgeDensityChunkData> DensityChunkDataByCoord;
    mutable bool bDensityChunkBoundsDirty = true;
    mutable int32 CachedDensityMinWorldZ = 0;
    mutable int32 CachedDensityMaxWorldZ = -1;

    TMap<FIntVector, FTerraforgeVoxelChunkData> BlockChunkDataByCoord;

    uint16 GetBlockMaterialAtWorldVoxel(
        const FIntVector& WorldVoxelCoord
    ) const;

    void GenerateBlockChunkData(
        FTerraforgeVoxelChunkData& ChunkData
    ) const;

    TSet<FIntVector> ModifiedDensityChunks;
    TMap<FIntVector, TSet<int32>> ModifiedDensityVoxelIndicesByChunk;

    TSet<FIntVector> ModifiedBlockChunks;
    TMap<FIntVector, TSet<int32>> ModifiedBlockVoxelIndicesByChunk;

    void MarkDensityVoxelModified(
        const FIntVector& WorldVoxelCoord
    );

    void MarkBlockVoxelModified(
        const FIntVector& WorldVoxelCoord
    );

    bool HasModifiedDensityWorld() const;
    bool HasModifiedBlockWorld() const;
    bool HasModifiedWorld() const;

    bool LoadSmoothDensityWorldFromSave(
        const UTerraforgeWorldSaveGame* SaveGame
    );

    bool LoadBlockWorldFromSave(
        const UTerraforgeWorldSaveGame* SaveGame
    );

    void GenerateSmoothDensityWorldAsync();

#if WITH_EDITOR
    void GenerateSmoothDensityWorldEditorPreview();
#endif

    void ApplyGeneratedSmoothDensityWorld(
        int32 GenerationId,
        TMap<FIntVector, FTerraforgeDensityChunkData> GeneratedDensityData,
        TMap<FIntVector, FTerraforgeVoxelMeshData> GeneratedMeshData
    );

    void BeginBatchedGeneratedMeshApply(
        TMap<FIntVector, FTerraforgeVoxelMeshData> GeneratedMeshData
    );

    void TickBatchedGeneratedMeshApply();

    void ApplySingleGeneratedMesh(
        const FIntVector& ChunkCoord,
        FTerraforgeVoxelMeshData& MeshData
    );

    void TickDensityRebuildMeshApply();

    void QueueAsyncRebuiltDensityChunksForApply(
        TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes,
        TSet<FIntVector> RebuiltChunkCoords
    );

    void SetGenerationProgress(float NewProgress);
    void MarkGenerationStarted();
    void MarkGenerationComplete();

    bool CanEditWorldNow() const;
    void CancelPendingAsyncWorldWork();

    bool GetCameraTraceHit(
        FHitResult& OutHit,
        float TraceDistance
    ) const;

    void AddDensityInSphere(
        FVector WorldLocation,
        float Radius,
        float Strength,
        int32 MaterialId
    );

    void RemoveDensityInSphere(
        FVector WorldLocation,
        float Radius,
        float Strength
    );

    void SetDensityAtWorldVoxel(
        const FIntVector& WorldVoxelCoord,
        float Density,
        uint16 MaterialId
    );

    void RebuildDensityChunks(
        const TSet<FIntVector>& DirtyChunks
    );

    void RebuildDensityChunksAsync(
        const TSet<FIntVector>& DirtyChunks
    );

    void ApplyAsyncRebuiltDensityChunks(
        int32 RebuildId,
        TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes,
        TSet<FIntVector> RebuiltChunkCoords
    );

    FTerraforgeTerrainSample SampleTerrainAtWorldVoxelPosition(
        const FVector& WorldVoxelPosition
    ) const;

    void FillDensityChunkFromTerrainSampler(
        FTerraforgeDensityChunkData& DensityChunkData
    ) const;

    uint16 GetTerrainSolidMaterialAtWorldVoxelPosition(
        const FVector& WorldVoxelPosition,
        float Density,
        float SurfaceHeight
    ) const;

    void AddBlocksAtCameraTrace(
        int32 InBlockEditRange,
        float TraceDistance,
        int32 MaterialId
    );

    void RemoveBlocksAtCameraTrace(
        int32 InBlockEditRange,
        float TraceDistance
    );

    void AddBlocksInSphere(
        FVector WorldLocation,
        int32 BlockRange,
        int32 MaterialId
    );

    void RemoveBlocksInSphere(
        FVector WorldLocation,
        int32 BlockRange
    );

    void SetBlockMaterialAtWorldVoxel(
        const FIntVector& WorldVoxelCoord,
        uint16 MaterialId
    );

    void RebuildBlockChunks(
        const TSet<FIntVector>& DirtyChunks
    );

    bool ShouldChunkGenerateCollisionInitially(
        const FIntVector& ChunkCoord
    ) const;

    bool ShouldChunkHaveCollisionNearPlayer(
        const FIntVector& ChunkCoord
    ) const;

    void UpdateChunkCollisionStates();

    bool FindDensityTerrainTopAtWorldXY(
        FVector WorldLocation,
        double& OutTopWorldZ
    ) const;

    bool FindBlockTerrainTopAtWorldXY(
        FVector WorldLocation,
        double& OutTopWorldZ
    ) const;

    bool FindTerrainSpawnSurfaceAtWorldXY(
        FVector WorldLocation,
        double& OutSurfaceWorldZ
    ) const;

    void KeepPlayerAboveDensityTerrain(
        FVector EditWorldLocation,
        float InEditRadius
    );

    void KeepPlayerAboveTerrain(
        FVector EditWorldLocation,
        float InEditRadius
    );

    bool ShouldUseRuntimeStreaming() const;

    bool GetStreamingViewerChunkCoord(
        FIntVector& OutViewerChunkCoord
    ) const;

    float GetBaseVoxelSize() const;

    double GetChunkWorldSize() const;

    FIntVector WorldLocationToVoxelCoord(
        const FVector& WorldLocation
    ) const;

    FVector WorldVoxelCoordToLocalPosition(
        const FIntVector& WorldVoxelCoord
    ) const;

    void UpdateRuntimeStreaming();

    void BuildStreamingChunkSet(
        const FIntVector& ViewerChunkCoord,
        int32 Radius,
        TSet<FIntVector>& OutChunkCoords
    ) const;

    void QueueMissingStreamingChunks(
        const FIntVector& ViewerChunkCoord,
        const TSet<FIntVector>& DesiredChunkCoords
    );

    void ProcessStreamingGenerationQueue();

    void GenerateStreamingChunk(
        const FIntVector& ChunkCoord
    );

    void UnloadChunksOutsideStreamingSet(
        const TSet<FIntVector>& KeepChunkCoords
    );

    const FTerraforgeTerrainGenerationProfile& GetActiveTerrainGenerationProfile() const;

    uint8 GetActiveBiomeId() const;

    TSet<FIntVector> GeneratedEmptyStreamingChunks;

    bool IsChunkActiveOrStreamingDesired(
        const FIntVector& ChunkCoord
    ) const;

    TMap<FIntVector, TMap<int32, uint16>> BlockVoxelOverridesByChunk;

    void ApplyBlockVoxelOverridesToChunk(
        const FIntVector& ChunkCoord,
        FTerraforgeVoxelChunkData& ChunkData
    ) const;

    bool bPendingInitialStreamingPlayerPlacement = false;

    void PlacePlayerOnTerrainAfterStreamingReady();

    TSet<FIntVector> ProcessedEmptyStreamingChunks;

    void InvalidateStreamingChunkForEdit(
        const FIntVector& ChunkCoord
    );

    void AddDensityChunkAndNeighboursToDirtySet(
        const FIntVector& ChunkCoord,
        TSet<FIntVector>& DirtyChunks
    ) const;

    void ClearChunkComponentForCoord(
        const FIntVector& ChunkCoord
    );

    void GenerateSmoothDensityMeshForChunk(
        const FIntVector& ChunkCoord,
        FTerraforgeVoxelMeshData& OutMesh
    ) const;

    void RefreshCachedDensityChunkBounds() const;

    bool bInitialTerrainReady = false;

    void BroadcastInitialTerrainReady();

    bool bAsyncDensityEditInProgress = false;
    int32 ActiveAsyncDensityEditId = 0;

    void ApplyAsyncDensityEditResult(
        int32 EditId,
        TMap<FIntVector, FTerraforgeDensityChunkData> EditedDensityChunks,
        TMap<FIntVector, FTerraforgeVoxelMeshData> RebuiltMeshes,
        TSet<FIntVector> RebuiltChunkCoords,
        FVector EditWorldLocation,
        float InEditRadius
    );
};