using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public sealed class MeshData
  {
    private struct VertexData
    {
      public Vector3 Position;
      public Vector3 Normal;
      public Color32 Color;
      public Vector2 Uv0;

      public VertexData(Vector3 position, Vector3 normal, Color32 color, Vector2 uv0)
      {
        Position = position;
        Normal = normal;
        Color = color;
        Uv0 = uv0;
      }
    }

    public readonly List<Vector3> Vertices = new();
    public readonly List<int> Triangles = new();
    public readonly List<Vector3> Normals = new();
    public readonly List<Vector2> UVs = new();
    public readonly List<Color32> Colors = new();

    private bool completeWithoutGeometry;

    public bool IsEmpty => !completeWithoutGeometry && (Vertices.Count == 0 || Triangles.Count == 0);
    public bool IsCompleteWithoutGeometry => completeWithoutGeometry;

    public int VertexCount => Vertices.Count;
    public int TriangleCount => Triangles.Count / 3;

    public void Reset()
    {
      Vertices.Clear();
      Triangles.Clear();
      Normals.Clear();
      UVs.Clear();
      Colors.Clear();
      completeWithoutGeometry = false;
    }

    public void MarkCompleteWithoutGeometry()
    {
      if (Vertices.Count == 0 && Triangles.Count == 0)
      {
        completeWithoutGeometry = true;
      }
    }

    public void Reserve(int vertexCount, int indexCount)
    {
      if (Vertices.Capacity < vertexCount)
      {
        Vertices.Capacity = vertexCount;
      }

      if (Triangles.Capacity < indexCount)
      {
        Triangles.Capacity = indexCount;
      }

      if (Normals.Capacity < vertexCount)
      {
        Normals.Capacity = vertexCount;
      }

      if (UVs.Capacity < vertexCount)
      {
        UVs.Capacity = vertexCount;
      }

      if (Colors.Capacity < vertexCount)
      {
        Colors.Capacity = vertexCount;
      }
    }

    public Mesh ToUnityMesh()
    {
      Mesh mesh = new()
      {
        indexFormat = Vertices.Count > 65535
              ? IndexFormat.UInt32
              : IndexFormat.UInt16
      };

      mesh.SetVertices(Vertices);
      mesh.SetTriangles(Triangles, 0);
      mesh.SetNormals(Normals);
      mesh.SetUVs(0, UVs);
      mesh.SetColors(Colors);

      mesh.RecalculateBounds();

      if (Normals.Count != Vertices.Count)
      {
        mesh.RecalculateNormals();
      }

      return mesh;
    }

    private static Color32 EncodeMaterialId(ushort materialId)
    {
      byte low = (byte)(materialId & 0xFF);
      byte high = (byte)((materialId >> 8) & 0xFF);
      return new Color32(low, high, 0, 255);
    }

    public void AddQuad(
      Vector3 v0,
      Vector3 v1,
      Vector3 v2,
      Vector3 v3,
      Vector3 normal,
      ushort materialId,
      Vector2 uvScale)
    {
      int startIndex = Vertices.Count;

      Vertices.Add(v0);
      Vertices.Add(v1);
      Vertices.Add(v2);
      Vertices.Add(v3);

      Triangles.Add(startIndex);
      Triangles.Add(startIndex + 2);
      Triangles.Add(startIndex + 1);

      Triangles.Add(startIndex);
      Triangles.Add(startIndex + 3);
      Triangles.Add(startIndex + 2);

      Normals.Add(normal);
      Normals.Add(normal);
      Normals.Add(normal);
      Normals.Add(normal);

      UVs.Add(Vector2.zero);
      UVs.Add(new Vector2(0.0f, uvScale.y));
      UVs.Add(uvScale);
      UVs.Add(new Vector2(uvScale.x, 0.0f));

      Color32 vertexColor = EncodeMaterialId(materialId);

      Colors.Add(vertexColor);
      Colors.Add(vertexColor);
      Colors.Add(vertexColor);
      Colors.Add(vertexColor);
    }

    public Mesh ToUnityMeshFast()
    {
      int vertexCount = Vertices.Count;
      int indexCount = Triangles.Count;

      Mesh mesh = new Mesh();
      IndexFormat indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

      var meshDataArray = UnityEngine.Mesh.AllocateWritableMeshData(1);
      var meshData = meshDataArray[0];

      var layout = new VertexAttributeDescriptor[]
      {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
      };

      meshData.SetVertexBufferParams(vertexCount, layout);

      // Interleaved vertex data: write sequentially per-vertex
      var vb = meshData.GetVertexData<VertexData>();
      for (int i = 0; i < vertexCount; i++)
      {
        Vector3 p = Vertices[i];
        Vector3 n = (Normals.Count == vertexCount) ? Normals[i] : Vector3.up;
        Vector2 uv = (UVs.Count == vertexCount) ? UVs[i] : Vector2.zero;
        Color32 c = (Colors.Count == vertexCount) ? Colors[i] : new Color32(255, 255, 255, 255);
        vb[i] = new VertexData(p, n, c, uv);
      }

      meshData.SetIndexBufferParams(indexCount, indexFormat);

      if (indexFormat == IndexFormat.UInt32)
      {
        var ib = meshData.GetIndexData<int>();
        for (int i = 0; i < indexCount; i++) ib[i] = Triangles[i];
      }
      else
      {
        var ib = meshData.GetIndexData<ushort>();
        for (int i = 0; i < indexCount; i++) ib[i] = (ushort)Triangles[i];
      }

      var sub = new SubMeshDescriptor(0, indexCount)
      {
        vertexCount = vertexCount,
        topology = MeshTopology.Triangles
      };
      meshData.subMeshCount = 1;
      meshData.SetSubMesh(0, sub, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

      UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

      // Assign bounds
      if (vertexCount > 0)
      {
        // Quick bounds compute
        Vector3 min = Vertices[0], max = Vertices[0];
        for (int i = 1; i < vertexCount; i++)
        {
          Vector3 v = Vertices[i];
          if (v.x < min.x) min.x = v.x; if (v.y < min.y) min.y = v.y; if (v.z < min.z) min.z = v.z;
          if (v.x > max.x) max.x = v.x; if (v.y > max.y) max.y = v.y; if (v.z > max.z) max.z = v.z;
        }
        var b = new Bounds((min + max) * 0.5f, max - min);
        mesh.bounds = b;
      }
      else
      {
        mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);
      }

      return mesh;
    }
  }
}