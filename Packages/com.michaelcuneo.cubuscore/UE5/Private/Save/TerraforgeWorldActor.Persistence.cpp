#include "World/TerraforgeWorldActor.h"

#include "Kismet/GameplayStatics.h"
#include "Save/TerraforgeWorldSaveGame.h"
#include "Density/TerraforgeMarchingCubes.h"
#include "Block/TerraforgeVoxelMesher.h"
#include "Core/TerraforgeVoxelLog.h"

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
