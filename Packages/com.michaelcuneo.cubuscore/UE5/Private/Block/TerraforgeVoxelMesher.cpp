#include "Block/TerraforgeVoxelMesher.h"
#include "Core/TerraforgeVoxelMath.h"

bool FTerraforgeVoxelMesher::IsSolid(
    const FTerraforgeVoxelChunkData& ChunkData,
    const int32 X,
    const int32 Y,
    const int32 Z
)
{
    if (
        X < 0 || X >= TerraforgeVoxel::ChunkSize ||
        Y < 0 || Y >= TerraforgeVoxel::ChunkSize ||
        Z < 0 || Z >= TerraforgeVoxel::ChunkSize
        )
    {
        return false;
    }

    return ChunkData.IsSolid(X, Y, Z);
}

uint16 FTerraforgeVoxelMesher::GetMaterialOrAir(
    const FTerraforgeVoxelChunkData& ChunkData,
    const int32 X,
    const int32 Y,
    const int32 Z
)
{
    if (
        X < 0 || X >= TerraforgeVoxel::ChunkSize ||
        Y < 0 || Y >= TerraforgeVoxel::ChunkSize ||
        Z < 0 || Z >= TerraforgeVoxel::ChunkSize
        )
    {
        return 0;
    }

    return ChunkData.GetVoxel(X, Y, Z).MaterialId;
}

uint16 FTerraforgeVoxelMesher::GetMaterialNeighbourAware(
    const FTerraforgeVoxelChunkData& ChunkData,
    FMaterialLookup MaterialLookup,
    const int32 LocalX,
    const int32 LocalY,
    const int32 LocalZ
)
{
    if (
        LocalX >= 0 && LocalX < TerraforgeVoxel::ChunkSize &&
        LocalY >= 0 && LocalY < TerraforgeVoxel::ChunkSize &&
        LocalZ >= 0 && LocalZ < TerraforgeVoxel::ChunkSize
        )
    {
        return ChunkData.GetVoxel(LocalX, LocalY, LocalZ).MaterialId;
    }

    const FIntVector WorldVoxel = ChunkData.LocalToWorldVoxel(LocalX, LocalY, LocalZ);
    return MaterialLookup(WorldVoxel);
}

void FTerraforgeVoxelMesher::GenerateNaiveMesh(
    const FTerraforgeVoxelChunkData& ChunkData,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();

    for (int32 Z = 0; Z < TerraforgeVoxel::ChunkSize; ++Z)
    {
        for (int32 Y = 0; Y < TerraforgeVoxel::ChunkSize; ++Y)
        {
            for (int32 X = 0; X < TerraforgeVoxel::ChunkSize; ++X)
            {
                const FTerraforgeVoxel Voxel = ChunkData.GetVoxel(X, Y, Z);

                if (!Voxel.IsSolid())
                {
                    continue;
                }

                if (!IsSolid(ChunkData, X + 1, Y, Z))
                {
                    AddFace(OutMesh, FIntVector(X, Y, Z), ETerraforgeVoxelFace::XPositive, Voxel.MaterialId);
                }

                if (!IsSolid(ChunkData, X - 1, Y, Z))
                {
                    AddFace(OutMesh, FIntVector(X, Y, Z), ETerraforgeVoxelFace::XNegative, Voxel.MaterialId);
                }

                if (!IsSolid(ChunkData, X, Y + 1, Z))
                {
                    AddFace(OutMesh, FIntVector(X, Y, Z), ETerraforgeVoxelFace::YPositive, Voxel.MaterialId);
                }

                if (!IsSolid(ChunkData, X, Y - 1, Z))
                {
                    AddFace(OutMesh, FIntVector(X, Y, Z), ETerraforgeVoxelFace::YNegative, Voxel.MaterialId);
                }

                if (!IsSolid(ChunkData, X, Y, Z + 1))
                {
                    AddFace(OutMesh, FIntVector(X, Y, Z), ETerraforgeVoxelFace::ZPositive, Voxel.MaterialId);
                }

                if (!IsSolid(ChunkData, X, Y, Z - 1))
                {
                    AddFace(OutMesh, FIntVector(X, Y, Z), ETerraforgeVoxelFace::ZNegative, Voxel.MaterialId);
                }
            }
        }
    }
}

void FTerraforgeVoxelMesher::AddFace(
    FTerraforgeVoxelMeshData& Mesh,
    const FIntVector& VoxelCoord,
    const ETerraforgeVoxelFace Face,
    const uint16 MaterialId
)
{
    const double S = TerraforgeVoxel::VoxelSize;

    const FVector Base(
        static_cast<double>(VoxelCoord.X) * S,
        static_cast<double>(VoxelCoord.Y) * S,
        static_cast<double>(VoxelCoord.Z) * S
    );

    FVector V0;
    FVector V1;
    FVector V2;
    FVector V3;
    FVector Normal;
    FProcMeshTangent Tangent;

    switch (Face)
    {
    case ETerraforgeVoxelFace::XPositive:
        V0 = Base + FVector(S, 0, 0);
        V1 = Base + FVector(S, 0, S);
        V2 = Base + FVector(S, S, S);
        V3 = Base + FVector(S, S, 0);
        Normal = FVector(1, 0, 0);
        Tangent = FProcMeshTangent(0, 1, 0);
        break;

    case ETerraforgeVoxelFace::XNegative:
        V0 = Base + FVector(0, 0, 0);
        V1 = Base + FVector(0, S, 0);
        V2 = Base + FVector(0, S, S);
        V3 = Base + FVector(0, 0, S);
        Normal = FVector(-1, 0, 0);
        Tangent = FProcMeshTangent(0, -1, 0);
        break;

    case ETerraforgeVoxelFace::YPositive:
        V0 = Base + FVector(S, S, 0);
        V1 = Base + FVector(S, S, S);
        V2 = Base + FVector(0, S, S);
        V3 = Base + FVector(0, S, 0);
        Normal = FVector(0, 1, 0);
        Tangent = FProcMeshTangent(-1, 0, 0);
        break;

    case ETerraforgeVoxelFace::YNegative:
        V0 = Base + FVector(0, 0, 0);
        V1 = Base + FVector(0, 0, S);
        V2 = Base + FVector(S, 0, S);
        V3 = Base + FVector(S, 0, 0);
        Normal = FVector(0, -1, 0);
        Tangent = FProcMeshTangent(1, 0, 0);
        break;

    case ETerraforgeVoxelFace::ZPositive:
        V0 = Base + FVector(0, 0, S);
        V1 = Base + FVector(0, S, S);
        V2 = Base + FVector(S, S, S);
        V3 = Base + FVector(S, 0, S);
        Normal = FVector(0, 0, 1);
        Tangent = FProcMeshTangent(1, 0, 0);
        break;

    case ETerraforgeVoxelFace::ZNegative:
        V0 = Base + FVector(0, 0, 0);
        V1 = Base + FVector(S, 0, 0);
        V2 = Base + FVector(S, S, 0);
        V3 = Base + FVector(0, S, 0);
        Normal = FVector(0, 0, -1);
        Tangent = FProcMeshTangent(1, 0, 0);
        break;

    default:
        return;
    }

    const int32 StartIndex = Mesh.Vertices.Num();

    Mesh.Vertices.Add(V0);
    Mesh.Vertices.Add(V1);
    Mesh.Vertices.Add(V2);
    Mesh.Vertices.Add(V3);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 1);
    Mesh.Triangles.Add(StartIndex + 2);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 2);
    Mesh.Triangles.Add(StartIndex + 3);

    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);

    Mesh.UVs.Add(FVector2D(0.0, 0.0));
    Mesh.UVs.Add(FVector2D(0.0, 1.0));
    Mesh.UVs.Add(FVector2D(1.0, 1.0));
    Mesh.UVs.Add(FVector2D(1.0, 0.0));

    const uint8 ColorValue = static_cast<uint8>(
        FMath::Clamp<int32>(static_cast<int32>(MaterialId) * 60, 60, 255)
        );

    const FColor VertexColor(ColorValue, ColorValue, ColorValue, 255);

    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);

    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
}

void FTerraforgeVoxelMesher::AddQuad(
    FTerraforgeVoxelMeshData& Mesh,
    const FVector& V0,
    const FVector& V1,
    const FVector& V2,
    const FVector& V3,
    const FVector& Normal,
    const FProcMeshTangent& Tangent,
    const uint16 MaterialId,
    const FVector2D& UVScale
)
{
    const int32 StartIndex = Mesh.Vertices.Num();

    Mesh.Vertices.Add(V0);
    Mesh.Vertices.Add(V1);
    Mesh.Vertices.Add(V2);
    Mesh.Vertices.Add(V3);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 1);
    Mesh.Triangles.Add(StartIndex + 2);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 2);
    Mesh.Triangles.Add(StartIndex + 3);

    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);

    Mesh.UVs.Add(FVector2D(0.0, 0.0));
    Mesh.UVs.Add(FVector2D(0.0, UVScale.Y));
    Mesh.UVs.Add(FVector2D(UVScale.X, UVScale.Y));
    Mesh.UVs.Add(FVector2D(UVScale.X, 0.0));

    const uint8 ColorValue = static_cast<uint8>(
        FMath::Clamp<int32>(static_cast<int32>(MaterialId) * 60, 60, 255)
        );

    const FColor VertexColor(ColorValue, ColorValue, ColorValue, 255);

    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);

    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
}

void FTerraforgeVoxelMesher::GenerateGreedyMesh(
    const FTerraforgeVoxelChunkData& ChunkData,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    TArray<int32> Mask;
    Mask.SetNumZeroed(Size * Size);

    // Axis: 0 = X, 1 = Y, 2 = Z
    for (int32 Axis = 0; Axis < 3; ++Axis)
    {
        const int32 U = (Axis + 1) % 3;
        const int32 V = (Axis + 2) % 3;

        FIntVector X = FIntVector::ZeroValue;
        FIntVector Q = FIntVector::ZeroValue;

        Q[Axis] = 1;

        for (X[Axis] = -1; X[Axis] < Size;)
        {
            int32 N = 0;

            for (X[V] = 0; X[V] < Size; ++X[V])
            {
                for (X[U] = 0; X[U] < Size; ++X[U])
                {
                    const uint16 MaterialA = GetMaterialOrAir(
                        ChunkData,
                        X.X,
                        X.Y,
                        X.Z
                    );

                    const uint16 MaterialB = GetMaterialOrAir(
                        ChunkData,
                        X.X + Q.X,
                        X.Y + Q.Y,
                        X.Z + Q.Z
                    );

                    const bool SolidA = MaterialA != 0;
                    const bool SolidB = MaterialB != 0;

                    if (SolidA == SolidB)
                    {
                        Mask[N++] = 0;
                    }
                    else if (SolidA)
                    {
                        // Positive-facing face for material A.
                        Mask[N++] = static_cast<int32>(MaterialA);
                    }
                    else
                    {
                        // Negative-facing face for material B.
                        Mask[N++] = -static_cast<int32>(MaterialB);
                    }
                }
            }

            ++X[Axis];

            N = 0;

            for (int32 J = 0; J < Size; ++J)
            {
                for (int32 I = 0; I < Size;)
                {
                    const int32 CurrentMask = Mask[N];

                    if (CurrentMask == 0)
                    {
                        ++I;
                        ++N;
                        continue;
                    }

                    int32 Width = 1;

                    while (
                        I + Width < Size &&
                        Mask[N + Width] == CurrentMask
                        )
                    {
                        ++Width;
                    }

                    int32 Height = 1;
                    bool bDone = false;

                    while (J + Height < Size)
                    {
                        for (int32 K = 0; K < Width; ++K)
                        {
                            if (Mask[N + K + Height * Size] != CurrentMask)
                            {
                                bDone = true;
                                break;
                            }
                        }

                        if (bDone)
                        {
                            break;
                        }

                        ++Height;
                    }

                    FIntVector D1 = FIntVector::ZeroValue;
                    FIntVector D2 = FIntVector::ZeroValue;

                    D1[U] = Width;
                    D2[V] = Height;

                    FIntVector Start = X;
                    Start[U] = I;
                    Start[V] = J;

                    const bool bPositiveFace = CurrentMask > 0;
                    const uint16 MaterialId = static_cast<uint16>(FMath::Abs(CurrentMask));

                    FVector V0;
                    FVector V1;
                    FVector V2;
                    FVector V3;
                    FVector Normal;
                    FProcMeshTangent Tangent;

                    const FVector P0(
                        static_cast<double>(Start.X) * S,
                        static_cast<double>(Start.Y) * S,
                        static_cast<double>(Start.Z) * S
                    );

                    const FVector P1(
                        static_cast<double>(Start.X + D1.X) * S,
                        static_cast<double>(Start.Y + D1.Y) * S,
                        static_cast<double>(Start.Z + D1.Z) * S
                    );

                    const FVector P2(
                        static_cast<double>(Start.X + D1.X + D2.X) * S,
                        static_cast<double>(Start.Y + D1.Y + D2.Y) * S,
                        static_cast<double>(Start.Z + D1.Z + D2.Z) * S
                    );

                    const FVector P3(
                        static_cast<double>(Start.X + D2.X) * S,
                        static_cast<double>(Start.Y + D2.Y) * S,
                        static_cast<double>(Start.Z + D2.Z) * S
                    );

                    if (bPositiveFace)
                    {
                        V0 = P0;
                        V1 = P3;
                        V2 = P2;
                        V3 = P1;
                    }
                    else
                    {
                        V0 = P0;
                        V1 = P1;
                        V2 = P2;
                        V3 = P3;
                    }

                    Normal = FVector::ZeroVector;
                    Normal[Axis] = bPositiveFace ? 1.0 : -1.0;

                    if (Axis == 0)
                    {
                        Tangent = FProcMeshTangent(0.0f, 1.0f, 0.0f);
                    }
                    else
                    {
                        Tangent = FProcMeshTangent(1.0f, 0.0f, 0.0f);
                    }

                    AddQuad(
                        OutMesh,
                        V0,
                        V1,
                        V2,
                        V3,
                        Normal,
                        Tangent,
                        MaterialId,
                        FVector2D(static_cast<double>(Width), static_cast<double>(Height))
                    );

                    for (int32 Y = 0; Y < Height; ++Y)
                    {
                        for (int32 X2 = 0; X2 < Width; ++X2)
                        {
                            Mask[N + X2 + Y * Size] = 0;
                        }
                    }

                    I += Width;
                    N += Width;
                }
            }
        }
    }
}

void FTerraforgeVoxelMesher::GenerateGreedyMeshNeighbourAware(
    const FTerraforgeVoxelChunkData& ChunkData,
    FMaterialLookup MaterialLookup,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();
    OutMesh.Reserve(4096, 6144);

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    TArray<int32> Mask;
    Mask.SetNumZeroed(Size * Size);

    for (int32 Axis = 0; Axis < 3; ++Axis)
    {
        const int32 U = (Axis + 1) % 3;
        const int32 V = (Axis + 2) % 3;

        FIntVector X = FIntVector::ZeroValue;
        FIntVector Q = FIntVector::ZeroValue;

        Q[Axis] = 1;

        for (X[Axis] = -1; X[Axis] < Size;)
        {
            int32 N = 0;

            for (X[V] = 0; X[V] < Size; ++X[V])
            {
                for (X[U] = 0; X[U] < Size; ++X[U])
                {
                    const uint16 MaterialA = GetMaterialNeighbourAware(
                        ChunkData,
                        MaterialLookup,
                        X.X,
                        X.Y,
                        X.Z
                    );

                    const uint16 MaterialB = GetMaterialNeighbourAware(
                        ChunkData,
                        MaterialLookup,
                        X.X + Q.X,
                        X.Y + Q.Y,
                        X.Z + Q.Z
                    );

                    const bool bSolidA = MaterialA != 0;
                    const bool bSolidB = MaterialB != 0;

                    if (bSolidA == bSolidB)
                    {
                        Mask[N++] = 0;
                    }
                    else if (bSolidA)
                    {
                        Mask[N++] = static_cast<int32>(MaterialA);
                    }
                    else
                    {
                        Mask[N++] = -static_cast<int32>(MaterialB);
                    }
                }
            }

            ++X[Axis];

            N = 0;

            for (int32 J = 0; J < Size; ++J)
            {
                for (int32 I = 0; I < Size;)
                {
                    const int32 CurrentMask = Mask[N];

                    if (CurrentMask == 0)
                    {
                        ++I;
                        ++N;
                        continue;
                    }

                    int32 Width = 1;

                    while (
                        I + Width < Size &&
                        Mask[N + Width] == CurrentMask
                        )
                    {
                        ++Width;
                    }

                    int32 Height = 1;
                    bool bDone = false;

                    while (J + Height < Size)
                    {
                        for (int32 K = 0; K < Width; ++K)
                        {
                            if (Mask[N + K + Height * Size] != CurrentMask)
                            {
                                bDone = true;
                                break;
                            }
                        }

                        if (bDone)
                        {
                            break;
                        }

                        ++Height;
                    }

                    FIntVector D1 = FIntVector::ZeroValue;
                    FIntVector D2 = FIntVector::ZeroValue;

                    D1[U] = Width;
                    D2[V] = Height;

                    FIntVector Start = X;
                    Start[U] = I;
                    Start[V] = J;

                    const bool bPositiveFace = CurrentMask > 0;
                    const uint16 MaterialId = static_cast<uint16>(FMath::Abs(CurrentMask));

                    const FVector P0(
                        static_cast<double>(Start.X) * S,
                        static_cast<double>(Start.Y) * S,
                        static_cast<double>(Start.Z) * S
                    );

                    const FVector P1(
                        static_cast<double>(Start.X + D1.X) * S,
                        static_cast<double>(Start.Y + D1.Y) * S,
                        static_cast<double>(Start.Z + D1.Z) * S
                    );

                    const FVector P2(
                        static_cast<double>(Start.X + D1.X + D2.X) * S,
                        static_cast<double>(Start.Y + D1.Y + D2.Y) * S,
                        static_cast<double>(Start.Z + D1.Z + D2.Z) * S
                    );

                    const FVector P3(
                        static_cast<double>(Start.X + D2.X) * S,
                        static_cast<double>(Start.Y + D2.Y) * S,
                        static_cast<double>(Start.Z + D2.Z) * S
                    );

                    FVector V0;
                    FVector V1;
                    FVector V2;
                    FVector V3;

                    if (bPositiveFace)
                    {
                        V0 = P0;
                        V1 = P3;
                        V2 = P2;
                        V3 = P1;
                    }
                    else
                    {
                        V0 = P0;
                        V1 = P1;
                        V2 = P2;
                        V3 = P3;
                    }

                    FVector Normal = FVector::ZeroVector;
                    Normal[Axis] = bPositiveFace ? 1.0 : -1.0;

                    FProcMeshTangent Tangent;

                    if (Axis == 0)
                    {
                        Tangent = FProcMeshTangent(0.0f, 1.0f, 0.0f);
                    }
                    else
                    {
                        Tangent = FProcMeshTangent(1.0f, 0.0f, 0.0f);
                    }

                    AddQuad(
                        OutMesh,
                        V0,
                        V1,
                        V2,
                        V3,
                        Normal,
                        Tangent,
                        MaterialId,
                        FVector2D(static_cast<double>(Width), static_cast<double>(Height))
                    );

                    for (int32 Y = 0; Y < Height; ++Y)
                    {
                        for (int32 X2 = 0; X2 < Width; ++X2)
                        {
                            Mask[N + X2 + Y * Size] = 0;
                        }
                    }

                    I += Width;
                    N += Width;
                }
            }
        }
    }
}

void FTerraforgeVoxelMesher::AddSmoothQuad(
    FTerraforgeVoxelMeshData& Mesh,
    const FVector& A,
    const FVector& B,
    const FVector& C,
    const FVector& D,
    const FVector& DesiredNormal
)
{
    FVector V0 = A;
    FVector V1 = B;
    FVector V2 = C;
    FVector V3 = D;

    const FVector RawNormal = FVector::CrossProduct(V1 - V0, V2 - V0);
    const double RawNormalSizeSquared = RawNormal.SizeSquared();

    if (RawNormalSizeSquared < KINDA_SMALL_NUMBER)
    {
        return;
    }

    FVector Normal = RawNormal / FMath::Sqrt(RawNormalSizeSquared);

    const FVector Desired = DesiredNormal.GetSafeNormal();

    if (!Desired.IsNearlyZero() && FVector::DotProduct(Normal, Desired) < 0.0)
    {
        V0 = A;
        V1 = D;
        V2 = C;
        V3 = B;

        const FVector FlippedRawNormal = FVector::CrossProduct(V1 - V0, V2 - V0);
        const double FlippedSizeSquared = FlippedRawNormal.SizeSquared();

        if (FlippedSizeSquared < KINDA_SMALL_NUMBER)
        {
            return;
        }

        Normal = FlippedRawNormal / FMath::Sqrt(FlippedSizeSquared);
    }

    const int32 StartIndex = Mesh.Vertices.Num();

    Mesh.Vertices.Add(V0);
    Mesh.Vertices.Add(V1);
    Mesh.Vertices.Add(V2);
    Mesh.Vertices.Add(V3);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 1);
    Mesh.Triangles.Add(StartIndex + 2);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 2);
    Mesh.Triangles.Add(StartIndex + 3);

    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);
    Mesh.Normals.Add(Normal);

    const double UVScale = 0.0025;

    const FVector AbsNormal(
        FMath::Abs(Normal.X),
        FMath::Abs(Normal.Y),
        FMath::Abs(Normal.Z)
    );

    auto ProjectUV = [UVScale, AbsNormal](const FVector& P) -> FVector2D
        {
            if (AbsNormal.Z >= AbsNormal.X && AbsNormal.Z >= AbsNormal.Y)
            {
                return FVector2D(P.X * UVScale, P.Y * UVScale);
            }

            if (AbsNormal.X >= AbsNormal.Y)
            {
                return FVector2D(P.Y * UVScale, P.Z * UVScale);
            }

            return FVector2D(P.X * UVScale, P.Z * UVScale);
        };

    Mesh.UVs.Add(ProjectUV(V0));
    Mesh.UVs.Add(ProjectUV(V1));
    Mesh.UVs.Add(ProjectUV(V2));
    Mesh.UVs.Add(ProjectUV(V3));

    const FColor VertexColor(160, 160, 160, 255);

    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);

    const FProcMeshTangent Tangent(1.0f, 0.0f, 0.0f);

    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
}

void FTerraforgeVoxelMesher::GenerateSmoothMeshSurfaceNets(
    const FIntVector& ChunkCoord,
    FDensityLookup DensityLookup,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();
    OutMesh.Reserve((TerraforgeVoxel::ChunkSize + 1) * (TerraforgeVoxel::ChunkSize + 1), TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize * 6);

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    auto CellIndex = [](const int32 X, const int32 Y, const int32 Z) -> int32
        {
            return X + TerraforgeVoxel::ChunkSize * (Y + TerraforgeVoxel::ChunkSize * Z);
        };

    static constexpr int32 CornerOffsets[8][3] =
    {
        {0, 0, 0},
        {1, 0, 0},
        {1, 1, 0},
        {0, 1, 0},
        {0, 0, 1},
        {1, 0, 1},
        {1, 1, 1},
        {0, 1, 1}
    };

    static constexpr int32 EdgeCorners[12][2] =
    {
        {0, 1},
        {1, 2},
        {2, 3},
        {3, 0},

        {4, 5},
        {5, 6},
        {6, 7},
        {7, 4},

        {0, 4},
        {1, 5},
        {2, 6},
        {3, 7}
    };

    TArray<uint8> bCellActive;
    TArray<FVector> CellPositions;

    bCellActive.Init(0, Size * Size * Size);
    CellPositions.Init(FVector::ZeroVector, Size * Size * Size);

    /*
     * Step 1:
     * For every voxel cell, find where the density surface crosses its edges.
     * Store one averaged surface point per active cell.
     */
    for (int32 Z = 0; Z < Size; ++Z)
    {
        for (int32 Y = 0; Y < Size; ++Y)
        {
            for (int32 X = 0; X < Size; ++X)
            {
                FVector CornerPositions[8];
                float CornerDensities[8];

                bool bHasInside = false;
                bool bHasOutside = false;

                for (int32 Corner = 0; Corner < 8; ++Corner)
                {
                    const int32 CX = CornerOffsets[Corner][0];
                    const int32 CY = CornerOffsets[Corner][1];
                    const int32 CZ = CornerOffsets[Corner][2];

                    const FVector WorldVoxelPosition(
                        static_cast<double>(ChunkCoord.X * Size + X + CX),
                        static_cast<double>(ChunkCoord.Y * Size + Y + CY),
                        static_cast<double>(ChunkCoord.Z * Size + Z + CZ)
                    );

                    CornerPositions[Corner] = FVector(
                        static_cast<double>(X + CX) * S,
                        static_cast<double>(Y + CY) * S,
                        static_cast<double>(Z + CZ) * S
                    );

                    const float Density = DensityLookup(WorldVoxelPosition);
                    CornerDensities[Corner] = Density;

                    if (Density >= 0.0f)
                    {
                        bHasInside = true;
                    }
                    else
                    {
                        bHasOutside = true;
                    }
                }

                if (!bHasInside || !bHasOutside)
                {
                    continue;
                }

                FVector AccumulatedPosition = FVector::ZeroVector;
                int32 CrossingCount = 0;

                for (int32 Edge = 0; Edge < 12; ++Edge)
                {
                    const int32 C0 = EdgeCorners[Edge][0];
                    const int32 C1 = EdgeCorners[Edge][1];

                    const float D0 = CornerDensities[C0];
                    const float D1 = CornerDensities[C1];

                    const bool bInside0 = D0 >= 0.0f;
                    const bool bInside1 = D1 >= 0.0f;

                    if (bInside0 == bInside1)
                    {
                        continue;
                    }

                    const float Denominator = D0 - D1;

                    float T = 0.5f;

                    if (FMath::Abs(Denominator) > KINDA_SMALL_NUMBER)
                    {
                        T = FMath::Clamp(D0 / Denominator, 0.0f, 1.0f);
                    }

                    const FVector P =
                        CornerPositions[C0] +
                        (CornerPositions[C1] - CornerPositions[C0]) * T;

                    AccumulatedPosition += P;
                    ++CrossingCount;
                }

                if (CrossingCount <= 0)
                {
                    continue;
                }

                const int32 Index = CellIndex(X, Y, Z);

                bCellActive[Index] = 1;
                CellPositions[Index] =
                    AccumulatedPosition / static_cast<double>(CrossingCount);
            }
        }
    }

    auto IsCellValid = [&bCellActive, CellIndex](const int32 X, const int32 Y, const int32 Z) -> bool
        {
            if (
                X < 0 || X >= TerraforgeVoxel::ChunkSize ||
                Y < 0 || Y >= TerraforgeVoxel::ChunkSize ||
                Z < 0 || Z >= TerraforgeVoxel::ChunkSize
                )
            {
                return false;
            }

            return bCellActive[CellIndex(X, Y, Z)] != 0;
        };

    auto GetCellPosition = [&CellPositions, CellIndex](const int32 X, const int32 Y, const int32 Z) -> FVector
        {
            return CellPositions[CellIndex(X, Y, Z)];
        };

    auto GetDensityAtGridPoint = [
        ChunkCoord,
        DensityLookup,
        Size
    ](
        const int32 X,
        const int32 Y,
        const int32 Z
        ) -> float
        {
            const int32 BaseWorldX = ChunkCoord.X * Size;
            const int32 BaseWorldY = ChunkCoord.Y * Size;
            const int32 BaseWorldZ = ChunkCoord.Z * Size;

            const FVector WorldVoxelPosition(
                static_cast<double>(BaseWorldX + X),
                static_cast<double>(BaseWorldY + Y),
                static_cast<double>(BaseWorldZ + Z)
            );

            return DensityLookup(WorldVoxelPosition);
        };

    /*
     * Step 2:
     * For every grid edge crossing, connect the four adjacent cell vertices.
     */

     // X-axis grid edges.
    for (int32 Z = 1; Z < Size; ++Z)
    {
        for (int32 Y = 1; Y < Size; ++Y)
        {
            for (int32 X = 0; X < Size; ++X)
            {
                const float D0 = GetDensityAtGridPoint(X, Y, Z);
                const float D1 = GetDensityAtGridPoint(X + 1, Y, Z);

                if ((D0 >= 0.0f) == (D1 >= 0.0f))
                {
                    continue;
                }

                const int32 C0X = X;
                const int32 C0Y = Y - 1;
                const int32 C0Z = Z - 1;

                const int32 C1X = X;
                const int32 C1Y = Y;
                const int32 C1Z = Z - 1;

                const int32 C2X = X;
                const int32 C2Y = Y;
                const int32 C2Z = Z;

                const int32 C3X = X;
                const int32 C3Y = Y - 1;
                const int32 C3Z = Z;

                if (
                    !IsCellValid(C0X, C0Y, C0Z) ||
                    !IsCellValid(C1X, C1Y, C1Z) ||
                    !IsCellValid(C2X, C2Y, C2Z) ||
                    !IsCellValid(C3X, C3Y, C3Z)
                    )
                {
                    continue;
                }

                const FVector DesiredNormal =
                    D0 >= 0.0f ? FVector(1.0, 0.0, 0.0) : FVector(-1.0, 0.0, 0.0);

                AddSmoothQuad(
                    OutMesh,
                    GetCellPosition(C0X, C0Y, C0Z),
                    GetCellPosition(C1X, C1Y, C1Z),
                    GetCellPosition(C2X, C2Y, C2Z),
                    GetCellPosition(C3X, C3Y, C3Z),
                    DesiredNormal
                );
            }
        }
    }

    // Y-axis grid edges.
    for (int32 Z = 1; Z < Size; ++Z)
    {
        for (int32 Y = 0; Y < Size; ++Y)
        {
            for (int32 X = 1; X < Size; ++X)
            {
                const float D0 = GetDensityAtGridPoint(X, Y, Z);
                const float D1 = GetDensityAtGridPoint(X, Y + 1, Z);

                if ((D0 >= 0.0f) == (D1 >= 0.0f))
                {
                    continue;
                }

                const int32 C0X = X - 1;
                const int32 C0Y = Y;
                const int32 C0Z = Z - 1;

                const int32 C1X = X;
                const int32 C1Y = Y;
                const int32 C1Z = Z - 1;

                const int32 C2X = X;
                const int32 C2Y = Y;
                const int32 C2Z = Z;

                const int32 C3X = X - 1;
                const int32 C3Y = Y;
                const int32 C3Z = Z;

                if (
                    !IsCellValid(C0X, C0Y, C0Z) ||
                    !IsCellValid(C1X, C1Y, C1Z) ||
                    !IsCellValid(C2X, C2Y, C2Z) ||
                    !IsCellValid(C3X, C3Y, C3Z)
                    )
                {
                    continue;
                }

                const FVector DesiredNormal =
                    D0 >= 0.0f ? FVector(0.0, 1.0, 0.0) : FVector(0.0, -1.0, 0.0);

                AddSmoothQuad(
                    OutMesh,
                    GetCellPosition(C0X, C0Y, C0Z),
                    GetCellPosition(C1X, C1Y, C1Z),
                    GetCellPosition(C2X, C2Y, C2Z),
                    GetCellPosition(C3X, C3Y, C3Z),
                    DesiredNormal
                );
            }
        }
    }

    // Z-axis grid edges.
    for (int32 Z = 0; Z < Size; ++Z)
    {
        for (int32 Y = 1; Y < Size; ++Y)
        {
            for (int32 X = 1; X < Size; ++X)
            {
                const float D0 = GetDensityAtGridPoint(X, Y, Z);
                const float D1 = GetDensityAtGridPoint(X, Y, Z + 1);

                if ((D0 >= 0.0f) == (D1 >= 0.0f))
                {
                    continue;
                }

                const int32 C0X = X - 1;
                const int32 C0Y = Y - 1;
                const int32 C0Z = Z;

                const int32 C1X = X;
                const int32 C1Y = Y - 1;
                const int32 C1Z = Z;

                const int32 C2X = X;
                const int32 C2Y = Y;
                const int32 C2Z = Z;

                const int32 C3X = X - 1;
                const int32 C3Y = Y;
                const int32 C3Z = Z;

                if (
                    !IsCellValid(C0X, C0Y, C0Z) ||
                    !IsCellValid(C1X, C1Y, C1Z) ||
                    !IsCellValid(C2X, C2Y, C2Z) ||
                    !IsCellValid(C3X, C3Y, C3Z)
                    )
                {
                    continue;
                }

                const FVector DesiredNormal =
                    D0 >= 0.0f ? FVector(0.0, 0.0, 1.0) : FVector(0.0, 0.0, -1.0);

                AddSmoothQuad(
                    OutMesh,
                    GetCellPosition(C0X, C0Y, C0Z),
                    GetCellPosition(C1X, C1Y, C1Z),
                    GetCellPosition(C2X, C2Y, C2Z),
                    GetCellPosition(C3X, C3Y, C3Z),
                    DesiredNormal
                );
            }
        }
    }

    UE_LOG(
        LogTemp,
        Display,
        TEXT("Surface Nets chunk (%d, %d, %d). Vertices: %d, Triangles: %d"),
        ChunkCoord.X,
        ChunkCoord.Y,
        ChunkCoord.Z,
        OutMesh.GetVertexCount(),
        OutMesh.GetTriangleCount()
    );
}

void FTerraforgeVoxelMesher::AddSmoothTriangle(
    FTerraforgeVoxelMeshData& Mesh,
    const FVector& A,
    const FVector& B,
    const FVector& C
)
{
    const FVector Normal = FVector::CrossProduct(B - A, C - A).GetSafeNormal();

    if (Normal.IsNearlyZero())
    {
        return;
    }

    const double UVScale = 0.0025;
    const FColor VertexColor(160, 160, 160, 255);
    const FProcMeshTangent Tangent(1.0f, 0.0f, 0.0f);

    // Front-facing triangle
    {
        const int32 StartIndex = Mesh.Vertices.Num();

        Mesh.Vertices.Add(A);
        Mesh.Vertices.Add(B);
        Mesh.Vertices.Add(C);

        Mesh.Triangles.Add(StartIndex + 0);
        Mesh.Triangles.Add(StartIndex + 1);
        Mesh.Triangles.Add(StartIndex + 2);

        Mesh.Normals.Add(Normal);
        Mesh.Normals.Add(Normal);
        Mesh.Normals.Add(Normal);

        Mesh.UVs.Add(FVector2D(A.X * UVScale, A.Y * UVScale));
        Mesh.UVs.Add(FVector2D(B.X * UVScale, B.Y * UVScale));
        Mesh.UVs.Add(FVector2D(C.X * UVScale, C.Y * UVScale));

        Mesh.VertexColors.Add(VertexColor);
        Mesh.VertexColors.Add(VertexColor);
        Mesh.VertexColors.Add(VertexColor);

        Mesh.Tangents.Add(Tangent);
        Mesh.Tangents.Add(Tangent);
        Mesh.Tangents.Add(Tangent);
    }

    // Reverse-facing triangle.
    // This makes the temporary smooth mesh visible/lit from both sides
    // even while the tetrahedra winding is still imperfect.
    {
        const int32 StartIndex = Mesh.Vertices.Num();

        const FVector ReverseNormal = -Normal;

        Mesh.Vertices.Add(A);
        Mesh.Vertices.Add(C);
        Mesh.Vertices.Add(B);

        Mesh.Triangles.Add(StartIndex + 0);
        Mesh.Triangles.Add(StartIndex + 1);
        Mesh.Triangles.Add(StartIndex + 2);

        Mesh.Normals.Add(ReverseNormal);
        Mesh.Normals.Add(ReverseNormal);
        Mesh.Normals.Add(ReverseNormal);

        Mesh.UVs.Add(FVector2D(A.X * UVScale, A.Y * UVScale));
        Mesh.UVs.Add(FVector2D(C.X * UVScale, C.Y * UVScale));
        Mesh.UVs.Add(FVector2D(B.X * UVScale, B.Y * UVScale));

        Mesh.VertexColors.Add(VertexColor);
        Mesh.VertexColors.Add(VertexColor);
        Mesh.VertexColors.Add(VertexColor);

        Mesh.Tangents.Add(Tangent);
        Mesh.Tangents.Add(Tangent);
        Mesh.Tangents.Add(Tangent);
    }
}

void FTerraforgeVoxelMesher::PolygoniseTetrahedron(
    FTerraforgeVoxelMeshData& Mesh,
    const FVector Positions[4],
    const float Densities[4]
)
{
    constexpr float IsoLevel = 0.0f;

    int32 InsideIndices[4];
    int32 OutsideIndices[4];

    int32 InsideCount = 0;
    int32 OutsideCount = 0;

    for (int32 I = 0; I < 4; ++I)
    {
        if (Densities[I] >= IsoLevel)
        {
            InsideIndices[InsideCount++] = I;
        }
        else
        {
            OutsideIndices[OutsideCount++] = I;
        }
    }

    if (InsideCount == 0 || InsideCount == 4)
    {
        return;
    }

    auto Interpolate = [](
        const FVector& P1,
        const FVector& P2,
        const float D1,
        const float D2
        ) -> FVector
        {
            const float Denominator = D1 - D2;

            if (FMath::Abs(Denominator) < KINDA_SMALL_NUMBER)
            {
                return (P1 + P2) * 0.5;
            }

            const float T = FMath::Clamp(D1 / Denominator, 0.0f, 1.0f);
            return P1 + (P2 - P1) * T;
        };

    auto EmitOrientedTriangle = [&Mesh](
        const FVector& A,
        const FVector& B,
        const FVector& C,
        const FVector& DesiredOutwardDirection
        )
        {
            FVector Normal = FVector::CrossProduct(B - A, C - A).GetSafeNormal();

            if (Normal.IsNearlyZero())
            {
                return;
            }

            FVector Desired = DesiredOutwardDirection.GetSafeNormal();

            if (!Desired.IsNearlyZero() && FVector::DotProduct(Normal, Desired) < 0.0)
            {
                FTerraforgeVoxelMesher::AddSmoothTriangle(Mesh, A, C, B);
            }
            else
            {
                FTerraforgeVoxelMesher::AddSmoothTriangle(Mesh, A, B, C);
            }
        };

    if (InsideCount == 1)
    {
        const int32 I0 = InsideIndices[0];

        const FVector P0 = Interpolate(
            Positions[I0],
            Positions[OutsideIndices[0]],
            Densities[I0],
            Densities[OutsideIndices[0]]
        );

        const FVector P1 = Interpolate(
            Positions[I0],
            Positions[OutsideIndices[1]],
            Densities[I0],
            Densities[OutsideIndices[1]]
        );

        const FVector P2 = Interpolate(
            Positions[I0],
            Positions[OutsideIndices[2]],
            Densities[I0],
            Densities[OutsideIndices[2]]
        );

        const FVector Center = (P0 + P1 + P2) / 3.0;
        const FVector DesiredOutward = Center - Positions[I0];

        EmitOrientedTriangle(P0, P1, P2, DesiredOutward);
    }
    else if (InsideCount == 3)
    {
        const int32 O0 = OutsideIndices[0];

        const FVector P0 = Interpolate(
            Positions[O0],
            Positions[InsideIndices[0]],
            Densities[O0],
            Densities[InsideIndices[0]]
        );

        const FVector P1 = Interpolate(
            Positions[O0],
            Positions[InsideIndices[1]],
            Densities[O0],
            Densities[InsideIndices[1]]
        );

        const FVector P2 = Interpolate(
            Positions[O0],
            Positions[InsideIndices[2]],
            Densities[O0],
            Densities[InsideIndices[2]]
        );

        const FVector Center = (P0 + P1 + P2) / 3.0;
        const FVector DesiredOutward = Positions[O0] - Center;

        EmitOrientedTriangle(P0, P1, P2, DesiredOutward);
    }
    else if (InsideCount == 2)
    {
        const int32 I0 = InsideIndices[0];
        const int32 I1 = InsideIndices[1];
        const int32 O0 = OutsideIndices[0];
        const int32 O1 = OutsideIndices[1];

        const FVector P0 = Interpolate(
            Positions[I0],
            Positions[O0],
            Densities[I0],
            Densities[O0]
        );

        const FVector P1 = Interpolate(
            Positions[I1],
            Positions[O0],
            Densities[I1],
            Densities[O0]
        );

        const FVector P2 = Interpolate(
            Positions[I1],
            Positions[O1],
            Densities[I1],
            Densities[O1]
        );

        const FVector P3 = Interpolate(
            Positions[I0],
            Positions[O1],
            Densities[I0],
            Densities[O1]
        );

        const FVector CenterA = (P0 + P1 + P2) / 3.0;
        const FVector CenterB = (P0 + P2 + P3) / 3.0;

        const FVector OutsideCenter =
            (Positions[O0] + Positions[O1]) * 0.5;

        EmitOrientedTriangle(P0, P1, P2, OutsideCenter - CenterA);
        EmitOrientedTriangle(P0, P2, P3, OutsideCenter - CenterB);
    }
}

void FTerraforgeVoxelMesher::GenerateSmoothMeshMarchingTetrahedra(
    const FIntVector& ChunkCoord,
    FDensityLookup DensityLookup,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();
    OutMesh.Reserve((TerraforgeVoxel::ChunkSize + 1) * (TerraforgeVoxel::ChunkSize + 1), TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize * 6);

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    /*
     * Cube corner layout:
     *
     * 0: (0,0,0)
     * 1: (1,0,0)
     * 2: (1,1,0)
     * 3: (0,1,0)
     * 4: (0,0,1)
     * 5: (1,0,1)
     * 6: (1,1,1)
     * 7: (0,1,1)
     */
    static constexpr int32 Tetrahedra[6][4] =
    {
        {0, 5, 1, 6},
        {0, 1, 2, 6},
        {0, 2, 3, 6},
        {0, 3, 7, 6},
        {0, 7, 4, 6},
        {0, 4, 5, 6}
    };

    for (int32 Z = 0; Z < Size; ++Z)
    {
        for (int32 Y = 0; Y < Size; ++Y)
        {
            for (int32 X = 0; X < Size; ++X)
            {
                const FIntVector WorldBaseVoxel(
                    ChunkCoord.X * Size + X,
                    ChunkCoord.Y * Size + Y,
                    ChunkCoord.Z * Size + Z
                );

                FVector CornerPositions[8];
                float CornerDensities[8];

                for (int32 Corner = 0; Corner < 8; ++Corner)
                {
                    const int32 CX = (Corner == 1 || Corner == 2 || Corner == 5 || Corner == 6) ? 1 : 0;
                    const int32 CY = (Corner == 2 || Corner == 3 || Corner == 6 || Corner == 7) ? 1 : 0;
                    const int32 CZ = (Corner >= 4) ? 1 : 0;

                    const FVector WorldVoxelPosition(
                        static_cast<double>(WorldBaseVoxel.X + CX),
                        static_cast<double>(WorldBaseVoxel.Y + CY),
                        static_cast<double>(WorldBaseVoxel.Z + CZ)
                    );

                    CornerPositions[Corner] = FVector(
                        static_cast<double>(X + CX) * S,
                        static_cast<double>(Y + CY) * S,
                        static_cast<double>(Z + CZ) * S
                    );

                    CornerDensities[Corner] = DensityLookup(WorldVoxelPosition);
                }

                for (int32 T = 0; T < 6; ++T)
                {
                    FVector TetPositions[4];
                    float TetDensities[4];

                    for (int32 I = 0; I < 4; ++I)
                    {
                        const int32 CornerIndex = Tetrahedra[T][I];

                        TetPositions[I] = CornerPositions[CornerIndex];
                        TetDensities[I] = CornerDensities[CornerIndex];
                    }

                    PolygoniseTetrahedron(
                        OutMesh,
                        TetPositions,
                        TetDensities
                    );
                }
            }
        }
    }
}

double FTerraforgeVoxelMesher::GetIslandHeightAtWorldVoxel(
    const double WX,
    const double WY
)
{
    const double IslandRadius = 95.0;
    const double BaseHeight = 8.0;
    const double HeightAmplitude = 34.0;

    const double Distance2D = FMath::Sqrt(WX * WX + WY * WY);

    const double EdgeT = FMath::Clamp(Distance2D / IslandRadius, 0.0, 1.0);
    const double EdgeFalloff = 1.0 - EdgeT * EdgeT * (3.0 - 2.0 * EdgeT);

    const double LargeNoise =
        FMath::Sin(WX * 0.035) * 0.45 +
        FMath::Cos(WY * 0.031) * 0.40 +
        FMath::Sin((WX + WY) * 0.022) * 0.50;

    const double MediumNoise =
        FMath::Sin(WX * 0.091 + WY * 0.037) * 0.35 +
        FMath::Cos(WY * 0.083 - WX * 0.029) * 0.30;

    const double RidgeNoise =
        FMath::Abs(FMath::Sin(WX * 0.047 + WY * 0.061)) * 0.65;

    return
        BaseHeight +
        EdgeFalloff * HeightAmplitude +
        LargeNoise * 12.0 +
        MediumNoise * 6.0 +
        RidgeNoise * 5.0;
}

void FTerraforgeVoxelMesher::GenerateSmoothHeightfieldMesh(
    const FIntVector& ChunkCoord,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();
    OutMesh.Reserve((TerraforgeVoxel::ChunkSize + 1) * (TerraforgeVoxel::ChunkSize + 1), TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize * 6);

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    /*
     * We generate a regular heightfield surface with one extra border vertex.
     * This gives a clean smooth surface without broken tetra/surface-net topology.
     */
    TArray<FVector> GridVertices;
    GridVertices.SetNumUninitialized((Size + 1) * (Size + 1));

    const int32 GridSize = Size + 1;

    auto GridIndex = [GridSize](const int32 X, const int32 Y) -> int32
        {
            return X + GridSize * Y;
        };

    for (int32 Y = 0; Y <= Size; ++Y)
    {
        for (int32 X = 0; X <= Size; ++X)
        {
            const int32 WorldX = ChunkCoord.X * Size + X;
            const int32 WorldY = ChunkCoord.Y * Size + Y;

            const double HeightVoxel =
                GetIslandHeightAtWorldVoxel(
                    static_cast<double>(WorldX),
                    static_cast<double>(WorldY)
                );

            const FVector LocalPosition(
                static_cast<double>(X) * S,
                static_cast<double>(Y) * S,
                HeightVoxel * S - static_cast<double>(ChunkCoord.Z * Size) * S
            );

            GridVertices[GridIndex(X, Y)] = LocalPosition;
        }
    }

    /*
     * Add vertices.
     */
    const int32 BaseVertexIndex = OutMesh.Vertices.Num();

    for (const FVector& V : GridVertices)
    {
        OutMesh.Vertices.Add(V);
        OutMesh.Normals.Add(FVector::UpVector);

        constexpr double UVScale = 0.0025;
        OutMesh.UVs.Add(FVector2D(V.X * UVScale, V.Y * UVScale));

        OutMesh.VertexColors.Add(FColor(150, 150, 150, 255));
        OutMesh.Tangents.Add(FProcMeshTangent(1.0f, 0.0f, 0.0f));
    }

    /*
     * Add triangles with consistent UE winding.
     */
    for (int32 Y = 0; Y < Size; ++Y)
    {
        for (int32 X = 0; X < Size; ++X)
        {
            const int32 V00 = BaseVertexIndex + GridIndex(X, Y);
            const int32 V10 = BaseVertexIndex + GridIndex(X + 1, Y);
            const int32 V01 = BaseVertexIndex + GridIndex(X, Y + 1);
            const int32 V11 = BaseVertexIndex + GridIndex(X + 1, Y + 1);

            OutMesh.Triangles.Add(V00);
            OutMesh.Triangles.Add(V01);
            OutMesh.Triangles.Add(V11);

            OutMesh.Triangles.Add(V00);
            OutMesh.Triangles.Add(V11);
            OutMesh.Triangles.Add(V10);
        }
    }

    /*
     * Recalculate vertex normals from triangles.
     */
    for (FVector& Normal : OutMesh.Normals)
    {
        Normal = FVector::ZeroVector;
    }

    for (int32 TriIndex = 0; TriIndex < OutMesh.Triangles.Num(); TriIndex += 3)
    {
        const int32 I0 = OutMesh.Triangles[TriIndex + 0];
        const int32 I1 = OutMesh.Triangles[TriIndex + 1];
        const int32 I2 = OutMesh.Triangles[TriIndex + 2];

        const FVector& A = OutMesh.Vertices[I0];
        const FVector& B = OutMesh.Vertices[I1];
        const FVector& C = OutMesh.Vertices[I2];

        FVector FaceNormal =
            FVector::CrossProduct(B - A, C - A).GetSafeNormal();

        if (FaceNormal.Z < 0.0)
        {
            FaceNormal *= -1.0;
        }

        OutMesh.Normals[I0] += FaceNormal;
        OutMesh.Normals[I1] += FaceNormal;
        OutMesh.Normals[I2] += FaceNormal;
    }

    for (FVector& Normal : OutMesh.Normals)
    {
        if (!Normal.Normalize())
        {
            Normal = FVector::UpVector;
        }
    }

    UE_LOG(
        LogTemp,
        Display,
        TEXT("Smooth heightfield chunk (%d, %d, %d). Vertices: %d, Triangles: %d"),
        ChunkCoord.X,
        ChunkCoord.Y,
        ChunkCoord.Z,
        OutMesh.GetVertexCount(),
        OutMesh.GetTriangleCount()
    );
}

void FTerraforgeVoxelMesher::GenerateSmoothHeightfieldMeshWithLookup(
    const FIntVector& ChunkCoord,
    FHeightLookup HeightLookup,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();
    OutMesh.Reserve((TerraforgeVoxel::ChunkSize + 1) * (TerraforgeVoxel::ChunkSize + 1), TerraforgeVoxel::ChunkSize * TerraforgeVoxel::ChunkSize * 6);

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    TArray<FVector> GridVertices;
    GridVertices.SetNumUninitialized((Size + 1) * (Size + 1));

    const int32 GridSize = Size + 1;

    auto GridIndex = [GridSize](const int32 X, const int32 Y) -> int32
        {
            return X + GridSize * Y;
        };

    for (int32 Y = 0; Y <= Size; ++Y)
    {
        for (int32 X = 0; X <= Size; ++X)
        {
            const int32 WorldX = ChunkCoord.X * Size + X;
            const int32 WorldY = ChunkCoord.Y * Size + Y;

            const double HeightVoxel = HeightLookup(WorldX, WorldY);

            const FVector LocalPosition(
                static_cast<double>(X) * S,
                static_cast<double>(Y) * S,
                HeightVoxel * S - static_cast<double>(ChunkCoord.Z * Size) * S
            );

            GridVertices[GridIndex(X, Y)] = LocalPosition;
        }
    }

    const int32 BaseVertexIndex = OutMesh.Vertices.Num();

    for (const FVector& V : GridVertices)
    {
        OutMesh.Vertices.Add(V);
        OutMesh.Normals.Add(FVector::UpVector);

        constexpr double UVScale = 0.0025;
        OutMesh.UVs.Add(FVector2D(V.X * UVScale, V.Y * UVScale));

        OutMesh.VertexColors.Add(FColor(150, 150, 150, 255));
        OutMesh.Tangents.Add(FProcMeshTangent(1.0f, 0.0f, 0.0f));
    }

    for (int32 Y = 0; Y < Size; ++Y)
    {
        for (int32 X = 0; X < Size; ++X)
        {
            const int32 V00 = BaseVertexIndex + GridIndex(X, Y);
            const int32 V10 = BaseVertexIndex + GridIndex(X + 1, Y);
            const int32 V01 = BaseVertexIndex + GridIndex(X, Y + 1);
            const int32 V11 = BaseVertexIndex + GridIndex(X + 1, Y + 1);

            OutMesh.Triangles.Add(V00);
            OutMesh.Triangles.Add(V01);
            OutMesh.Triangles.Add(V11);

            OutMesh.Triangles.Add(V00);
            OutMesh.Triangles.Add(V11);
            OutMesh.Triangles.Add(V10);
        }
    }

    for (FVector& Normal : OutMesh.Normals)
    {
        Normal = FVector::ZeroVector;
    }

    for (int32 TriIndex = 0; TriIndex < OutMesh.Triangles.Num(); TriIndex += 3)
    {
        const int32 I0 = OutMesh.Triangles[TriIndex + 0];
        const int32 I1 = OutMesh.Triangles[TriIndex + 1];
        const int32 I2 = OutMesh.Triangles[TriIndex + 2];

        const FVector& A = OutMesh.Vertices[I0];
        const FVector& B = OutMesh.Vertices[I1];
        const FVector& C = OutMesh.Vertices[I2];

        FVector FaceNormal = FVector::CrossProduct(B - A, C - A).GetSafeNormal();

        if (FaceNormal.Z < 0.0)
        {
            FaceNormal *= -1.0;
        }

        OutMesh.Normals[I0] += FaceNormal;
        OutMesh.Normals[I1] += FaceNormal;
        OutMesh.Normals[I2] += FaceNormal;
    }

    for (FVector& Normal : OutMesh.Normals)
    {
        if (!Normal.Normalize())
        {
            Normal = FVector::UpVector;
        }
    }
}

FVector FTerraforgeVoxelMesher::InterpolateDensityEdge(
    const FVector& P0,
    const FVector& P1,
    const float D0,
    const float D1
)
{
    const float Denominator = D0 - D1;

    if (FMath::Abs(Denominator) < KINDA_SMALL_NUMBER)
    {
        return (P0 + P1) * 0.5;
    }

    const float T = FMath::Clamp(D0 / Denominator, 0.0f, 1.0f);

    return P0 + (P1 - P0) * static_cast<double>(T);
}

void FTerraforgeVoxelMesher::AddDensityQuad(
    FTerraforgeVoxelMeshData& Mesh,
    const FVector& A,
    const FVector& B,
    const FVector& C,
    const FVector& D,
    const FVector& Normal,
    const uint16 MaterialId
)
{
    FVector V0 = A;
    FVector V1 = B;
    FVector V2 = C;
    FVector V3 = D;

    const FVector RawFaceNormal = FVector::CrossProduct(V1 - V0, V2 - V0);
    const double RawFaceNormalSizeSquared = RawFaceNormal.SizeSquared();

    if (RawFaceNormalSizeSquared < KINDA_SMALL_NUMBER)
    {
        return;
    }

    FVector FinalNormal = Normal.GetSafeNormal();

    if (FinalNormal.IsNearlyZero())
    {
        FinalNormal = FVector::UpVector;
    }

    const FVector FaceNormal = RawFaceNormal / FMath::Sqrt(RawFaceNormalSizeSquared);

    if (FVector::DotProduct(FaceNormal, FinalNormal) < 0.0)
    {
        V0 = A;
        V1 = D;
        V2 = C;
        V3 = B;
    }

    const int32 StartIndex = Mesh.Vertices.Num();

    Mesh.Vertices.Add(V0);
    Mesh.Vertices.Add(V1);
    Mesh.Vertices.Add(V2);
    Mesh.Vertices.Add(V3);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 1);
    Mesh.Triangles.Add(StartIndex + 2);

    Mesh.Triangles.Add(StartIndex + 0);
    Mesh.Triangles.Add(StartIndex + 2);
    Mesh.Triangles.Add(StartIndex + 3);

    Mesh.Normals.Add(FinalNormal);
    Mesh.Normals.Add(FinalNormal);
    Mesh.Normals.Add(FinalNormal);
    Mesh.Normals.Add(FinalNormal);

    const double UVScale = 0.0025;

    const FVector AbsNormal(
        FMath::Abs(FinalNormal.X),
        FMath::Abs(FinalNormal.Y),
        FMath::Abs(FinalNormal.Z)
    );

    auto ProjectUV = [UVScale, AbsNormal](const FVector& P) -> FVector2D
        {
            if (AbsNormal.Z >= AbsNormal.X && AbsNormal.Z >= AbsNormal.Y)
            {
                return FVector2D(P.X * UVScale, P.Y * UVScale);
            }

            if (AbsNormal.X >= AbsNormal.Y)
            {
                return FVector2D(P.Y * UVScale, P.Z * UVScale);
            }

            return FVector2D(P.X * UVScale, P.Z * UVScale);
        };

    Mesh.UVs.Add(ProjectUV(V0));
    Mesh.UVs.Add(ProjectUV(V1));
    Mesh.UVs.Add(ProjectUV(V2));
    Mesh.UVs.Add(ProjectUV(V3));

    const uint8 Shade =
        static_cast<uint8>(FMath::Clamp<int32>(120 + static_cast<int32>(MaterialId) * 20, 80, 220));

    const FColor VertexColor(Shade, Shade, Shade, 255);

    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);
    Mesh.VertexColors.Add(VertexColor);

    const FProcMeshTangent Tangent(1.0f, 0.0f, 0.0f);

    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
    Mesh.Tangents.Add(Tangent);
}

void FTerraforgeVoxelMesher::GenerateSmoothDensityMesh(
    const FIntVector& ChunkCoord,
    FStoredDensityLookup DensityLookup,
    FStoredDensityMaterialLookup MaterialLookup,
    FTerraforgeVoxelMeshData& OutMesh
)
{
    OutMesh.Reset();
    OutMesh.Reserve(4096, 6144);

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    auto LocalToWorld = [ChunkCoord](const int32 X, const int32 Y, const int32 Z) -> FIntVector
        {
            return FIntVector(
                ChunkCoord.X * TerraforgeVoxel::ChunkSize + X,
                ChunkCoord.Y * TerraforgeVoxel::ChunkSize + Y,
                ChunkCoord.Z * TerraforgeVoxel::ChunkSize + Z
            );
        };

    auto LocalCenter = [S](const int32 X, const int32 Y, const int32 Z) -> FVector
        {
            return FVector(
                (static_cast<double>(X) + 0.5) * S,
                (static_cast<double>(Y) + 0.5) * S,
                (static_cast<double>(Z) + 0.5) * S
            );
        };

    auto EmitFace = [&](
        const int32 X,
        const int32 Y,
        const int32 Z,
        const FIntVector& Direction
        )
        {
            const FIntVector WorldVoxel = LocalToWorld(X, Y, Z);
            const FIntVector NeighborWorldVoxel = WorldVoxel + Direction;

            const float D0 = DensityLookup(WorldVoxel);
            const float D1 = DensityLookup(NeighborWorldVoxel);

            if (D0 <= 0.0f || D1 > 0.0f)
            {
                return;
            }

            const uint16 MaterialId = MaterialLookup(WorldVoxel);

            if (MaterialId == 0)
            {
                return;
            }

            const FVector P0 = LocalCenter(X, Y, Z);

            const FVector P1 =
                P0 +
                FVector(
                    static_cast<double>(Direction.X) * S,
                    static_cast<double>(Direction.Y) * S,
                    static_cast<double>(Direction.Z) * S
                );

            const FVector FaceCenter =
                InterpolateDensityEdge(P0, P1, D0, D1);

            FVector Normal(
                static_cast<double>(Direction.X),
                static_cast<double>(Direction.Y),
                static_cast<double>(Direction.Z)
            );

            if (!Normal.Normalize())
            {
                Normal = FVector::UpVector;
            }

            /*
             * Axis-aligned conservative density face.
             * This is intentionally stable before we move to real Marching Cubes.
             */
            FVector A;
            FVector B;
            FVector C;
            FVector D;

            if (Direction.X != 0)
            {
                const double FX = FaceCenter.X;

                const double Y0 = static_cast<double>(Y) * S;
                const double Y1 = static_cast<double>(Y + 1) * S;
                const double Z0 = static_cast<double>(Z) * S;
                const double Z1 = static_cast<double>(Z + 1) * S;

                A = FVector(FX, Y0, Z0);
                B = FVector(FX, Y1, Z0);
                C = FVector(FX, Y1, Z1);
                D = FVector(FX, Y0, Z1);
            }
            else if (Direction.Y != 0)
            {
                const double FY = FaceCenter.Y;

                const double X0 = static_cast<double>(X) * S;
                const double X1 = static_cast<double>(X + 1) * S;
                const double Z0 = static_cast<double>(Z) * S;
                const double Z1 = static_cast<double>(Z + 1) * S;

                A = FVector(X0, FY, Z0);
                B = FVector(X0, FY, Z1);
                C = FVector(X1, FY, Z1);
                D = FVector(X1, FY, Z0);
            }
            else
            {
                const double FZ = FaceCenter.Z;

                const double X0 = static_cast<double>(X) * S;
                const double X1 = static_cast<double>(X + 1) * S;
                const double Y0 = static_cast<double>(Y) * S;
                const double Y1 = static_cast<double>(Y + 1) * S;

                A = FVector(X0, Y0, FZ);
                B = FVector(X1, Y0, FZ);
                C = FVector(X1, Y1, FZ);
                D = FVector(X0, Y1, FZ);
            }

            AddDensityQuad(
                OutMesh,
                A,
                B,
                C,
                D,
                Normal,
                MaterialId
            );
        };

    for (int32 Z = 0; Z < Size; ++Z)
    {
        for (int32 Y = 0; Y < Size; ++Y)
        {
            for (int32 X = 0; X < Size; ++X)
            {
                const FIntVector WorldVoxel = LocalToWorld(X, Y, Z);

                if (DensityLookup(WorldVoxel) <= 0.0f)
                {
                    continue;
                }

                EmitFace(X, Y, Z, FIntVector(1, 0, 0));
                EmitFace(X, Y, Z, FIntVector(-1, 0, 0));

                EmitFace(X, Y, Z, FIntVector(0, 1, 0));
                EmitFace(X, Y, Z, FIntVector(0, -1, 0));

                EmitFace(X, Y, Z, FIntVector(0, 0, 1));
                EmitFace(X, Y, Z, FIntVector(0, 0, -1));
            }
        }
    }

    UE_LOG(
        LogTemp,
        Display,
        TEXT("Generated smooth density mesh chunk (%d, %d, %d). Vertices: %d, Triangles: %d"),
        ChunkCoord.X,
        ChunkCoord.Y,
        ChunkCoord.Z,
        OutMesh.GetVertexCount(),
        OutMesh.GetTriangleCount()
    );
}