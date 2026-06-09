#include "Core/TerraforgeVoxelChunkComponent.h"

#include "Core/TerraforgeVoxelMath.h"

UTerraforgeVoxelChunkComponent::UTerraforgeVoxelChunkComponent(
    const FObjectInitializer& ObjectInitializer
)
    : Super(ObjectInitializer)
{
    bUseAsyncCooking = true;
    PrimaryComponentTick.bCanEverTick = false;
}

void UTerraforgeVoxelChunkComponent::InitializeChunk(
    const FIntVector& InChunkCoord,
    const bool bInGenerateCollision,
    const float InBaseVoxelSize
)
{
    ChunkCoord = InChunkCoord;
    bGenerateCollision = bInGenerateCollision;

    const double SafeBaseVoxelSize =
        FMath::Max(1.0, static_cast<double>(InBaseVoxelSize));

    const double ChunkWorldSize =
        static_cast<double>(TerraforgeVoxel::ChunkSize) *
        SafeBaseVoxelSize;

    const double ComponentScale =
        SafeBaseVoxelSize /
        static_cast<double>(TerraforgeVoxel::VoxelSize);

    SetRelativeLocation(FVector(
        static_cast<double>(ChunkCoord.X) * ChunkWorldSize,
        static_cast<double>(ChunkCoord.Y) * ChunkWorldSize,
        static_cast<double>(ChunkCoord.Z) * ChunkWorldSize
    ));

    SetRelativeScale3D(FVector(ComponentScale));
}

void UTerraforgeVoxelChunkComponent::ApplyMesh(
    const FTerraforgeVoxelMeshData& MeshData
)
{
    ClearAllMeshSections();

    if (MeshData.IsEmpty())
    {
        SetCollisionEnabled(ECollisionEnabled::NoCollision);
        MarkRenderStateDirty();
        UpdateBounds();

        UE_LOG(
            LogTemp,
            Display,
            TEXT("Cleared empty chunk mesh (%d, %d, %d)."),
            ChunkCoord.X,
            ChunkCoord.Y,
            ChunkCoord.Z
        );

        return;
    }

    SetCollisionEnabled(
        bGenerateCollision
        ? ECollisionEnabled::QueryAndPhysics
        : ECollisionEnabled::NoCollision
    );

    CreateMeshSection(
        0,
        MeshData.Vertices,
        MeshData.Triangles,
        MeshData.Normals,
        MeshData.UVs,
        MeshData.VertexColors,
        MeshData.Tangents,
        bGenerateCollision
    );

    MarkRenderStateDirty();
    UpdateBounds();

    UE_LOG(
        LogTemp,
        Display,
        TEXT("Applied chunk mesh (%d, %d, %d). Vertices: %d, Triangles: %d, Collision: %s"),
        ChunkCoord.X,
        ChunkCoord.Y,
        ChunkCoord.Z,
        MeshData.GetVertexCount(),
        MeshData.GetTriangleCount(),
        bGenerateCollision ? TEXT("true") : TEXT("false")
    );
}