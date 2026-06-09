#pragma once

#include "CoreMinimal.h"
#include "ProceduralMeshComponent.h"
#include "TerraforgeVoxelChunkData.h"

struct FTerraforgeVoxelMeshData
{
    TArray<FVector> Vertices;
    TArray<int32> Triangles;
    TArray<FVector> Normals;
    TArray<FVector2D> UVs;
    TArray<FColor> VertexColors;
    TArray<FProcMeshTangent> Tangents;

    void Reset()
    {
        Vertices.Reset();
        Triangles.Reset();
        Normals.Reset();
        UVs.Reset();
        VertexColors.Reset();
        Tangents.Reset();
    }

    void Reserve(const int32 VertexCount, const int32 IndexCount)
    {
        Vertices.Reserve(VertexCount);
        Triangles.Reserve(IndexCount);
        Normals.Reserve(VertexCount);
        UVs.Reserve(VertexCount);
        VertexColors.Reserve(VertexCount);
        Tangents.Reserve(VertexCount);
    }

    bool IsEmpty() const
    {
        return Vertices.Num() == 0 || Triangles.Num() == 0;
    }

    int32 GetTriangleCount() const
    {
        return Triangles.Num() / 3;
    }

    int32 GetVertexCount() const
    {
        return Vertices.Num();
    }
};

class TERRAFORGEVOXEL_API FTerraforgeVoxelMesher
{
public:
    using FMaterialLookup = TFunctionRef<uint16(const FIntVector& WorldVoxelCoord)>;
    using FDensityLookup = TFunctionRef<float(const FVector& WorldVoxelPosition)>;
    using FHeightLookup = TFunctionRef<double(int32 WorldX, int32 WorldY)>;

    using FStoredDensityLookup = TFunctionRef<float(const FIntVector& WorldVoxelCoord)>;
    using FStoredDensityMaterialLookup = TFunctionRef<uint16(const FIntVector& WorldVoxelCoord)>;

    static void GenerateNaiveMesh(
        const FTerraforgeVoxelChunkData& ChunkData,
        FTerraforgeVoxelMeshData& OutMesh
    );

    static void GenerateGreedyMesh(
        const FTerraforgeVoxelChunkData& ChunkData,
        FTerraforgeVoxelMeshData& OutMesh
    );

    static void GenerateGreedyMeshNeighbourAware(
        const FTerraforgeVoxelChunkData& ChunkData,
        FMaterialLookup MaterialLookup,
        FTerraforgeVoxelMeshData& OutMesh
    );

    static void GenerateSmoothMeshMarchingTetrahedra(
        const FIntVector& ChunkCoord,
        FDensityLookup DensityLookup,
        FTerraforgeVoxelMeshData& OutMesh
    );

    static void GenerateSmoothMeshSurfaceNets(
        const FIntVector& ChunkCoord,
        FDensityLookup DensityLookup,
        FTerraforgeVoxelMeshData& OutMesh
    );

    static void GenerateSmoothHeightfieldMesh(
        const FIntVector& ChunkCoord,
        FTerraforgeVoxelMeshData& OutMesh
    );

    static void GenerateSmoothHeightfieldMeshWithLookup(
        const FIntVector& ChunkCoord,
        FHeightLookup HeightLookup,
        FTerraforgeVoxelMeshData& OutMesh
    );

    static void GenerateSmoothDensityMesh(
        const FIntVector& ChunkCoord,
        FStoredDensityLookup DensityLookup,
        FStoredDensityMaterialLookup MaterialLookup,
        FTerraforgeVoxelMeshData& OutMesh
    );

private:
    static bool IsSolid(
        const FTerraforgeVoxelChunkData& ChunkData,
        int32 X,
        int32 Y,
        int32 Z
    );

    static uint16 GetMaterialOrAir(
        const FTerraforgeVoxelChunkData& ChunkData,
        int32 X,
        int32 Y,
        int32 Z
    );

    static uint16 GetMaterialNeighbourAware(
        const FTerraforgeVoxelChunkData& ChunkData,
        FMaterialLookup MaterialLookup,
        int32 LocalX,
        int32 LocalY,
        int32 LocalZ
    );

    static void AddFace(
        FTerraforgeVoxelMeshData& Mesh,
        const FIntVector& VoxelCoord,
        ETerraforgeVoxelFace Face,
        uint16 MaterialId
    );

    static void AddQuad(
        FTerraforgeVoxelMeshData& Mesh,
        const FVector& V0,
        const FVector& V1,
        const FVector& V2,
        const FVector& V3,
        const FVector& Normal,
        const FProcMeshTangent& Tangent,
        uint16 MaterialId,
        const FVector2D& UVScale
    );

    static void AddSmoothTriangle(
        FTerraforgeVoxelMeshData& Mesh,
        const FVector& A,
        const FVector& B,
        const FVector& C
    );

    static void PolygoniseTetrahedron(
        FTerraforgeVoxelMeshData& Mesh,
        const FVector Positions[4],
        const float Densities[4]
    );

    static void AddSmoothQuad(
        FTerraforgeVoxelMeshData& Mesh,
        const FVector& A,
        const FVector& B,
        const FVector& C,
        const FVector& D,
        const FVector& DesiredNormal
    );

    static FVector InterpolateDensityEdge(
        const FVector& P0,
        const FVector& P1,
        float D0,
        float D1
    );

    static void AddDensityQuad(
        FTerraforgeVoxelMeshData& Mesh,
        const FVector& A,
        const FVector& B,
        const FVector& C,
        const FVector& D,
        const FVector& Normal,
        uint16 MaterialId
    );

    static double GetIslandHeightAtWorldVoxel(double WX, double WY);
};