#pragma once

// Defines macro to inject streaming config/state/methods into ATerraforgeWorldActor.
// Include this header BEFORE the actor's generated.h, then place
// `TERRAFORGE_STREAMING_MEMBERS` inside the class body where desired.

#define TERRAFORGE_STREAMING_MEMBERS \
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming") \
	bool bEnableRuntimeStreaming = false; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "1", ClampMax = "64")) \
	int32 ViewDistanceInChunks = 4; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "0", ClampMax = "8")) \
	int32 StreamingUnloadPaddingInChunks = 1; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "1", ClampMax = "128")) \
	int32 StreamingChunksGeneratedPerFrame = 4; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming", meta = (ClampMin = "1", ClampMax = "512")) \
	int32 InitialStreamingChunksGeneratedPerFrame = 64; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Performance", meta = (ClampMin = "1", ClampMax = "32")) \
	int32 StreamingMeshAppliesPerFrame = 1; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Smooth Density", meta = (ClampMin = "0", ClampMax = "16")) \
	int32 SmoothStreamingChunksBelowSurface = 2; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Smooth Density", meta = (ClampMin = "0", ClampMax = "16")) \
	int32 SmoothStreamingChunksAboveSurface = 2; \
	\
	TArray<FIntVector> PendingStreamingChunkCoords; \
	TSet<FIntVector> PendingStreamingChunkCoordSet; \
	int32 PendingStreamingChunkQueueHead = 0; \
	\
	TMap<FIntVector, FTerraforgeVoxelMeshData> PendingStreamingMeshData; \
	TArray<FIntVector> PendingStreamingMeshCoords; \
	TSet<FIntVector> PendingStreamingMeshCoordSet; \
	int32 PendingStreamingMeshApplyIndex = 0; \
	\
	void QueueStreamingMeshApply(const FIntVector& ChunkCoord, FTerraforgeVoxelMeshData&& MeshData); \
	void TickStreamingMeshApply(); \
	\
	bool bRuntimeStreamingInitialized = false; \
	bool bCachedStreamingChunkSetsValid = false; \
	FIntVector CachedStreamingViewerChunkCoord = FIntVector::ZeroValue; \
	int32 CachedStreamingViewDistanceInChunks = INDEX_NONE; \
	int32 CachedStreamingUnloadPaddingInChunks = INDEX_NONE; \
	int32 CachedSmoothStreamingChunksBelowSurface = INDEX_NONE; \
	int32 CachedSmoothStreamingChunksAboveSurface = INDEX_NONE; \
	ETerraforgeTerrainSystem CachedStreamingTerrainSystem = ETerraforgeTerrainSystem::SmoothDensity; \
	TSet<FIntVector> CachedDesiredStreamingChunkCoords; \
	\
	UPROPERTY(EditAnywhere, Category = "Terraforge|Streaming|Performance", meta = (ClampMin = "1", ClampMax = "64")) \
	int32 MaxAsyncStreamingChunkTasks = 8; \
	\
	TSet<FIntVector> AsyncStreamingChunkCoordSet; \
	int32 ActiveAsyncStreamingChunkTasks = 0; \
	int32 ActiveStreamingGenerationId = 0; \
	\
	TSet<FIntVector> GeneratedEmptyStreamingChunks; \
	TSet<FIntVector> ProcessedEmptyStreamingChunks; \
	\
	bool bPendingInitialStreamingPlayerPlacement = false; \
	\
	bool ShouldUseRuntimeStreaming() const; \
	bool GetStreamingViewerChunkCoord(FIntVector& OutViewerChunkCoord) const; \
	void BuildStreamingChunkSet(const FIntVector& ViewerChunkCoord, int32 Radius, TSet<FIntVector>& OutChunkCoords) const; \
	void UpdateRuntimeStreaming(); \
	void QueueMissingStreamingChunks(const FIntVector& ViewerChunkCoord, const TSet<FIntVector>& DesiredChunkCoords); \
	void ProcessStreamingGenerationQueue(); \
	void GenerateStreamingChunk(const FIntVector& ChunkCoord); \
	void UnloadChunksOutsideStreamingSet(const TSet<FIntVector>& KeepChunkCoords); \
	bool IsChunkActiveOrStreamingDesired(const FIntVector& ChunkCoord) const; \
	void InvalidateStreamingChunkForEdit(const FIntVector& ChunkCoord); \
	void PlacePlayerOnTerrainAfterStreamingReady();