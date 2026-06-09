using System.Collections.Generic;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing
{
  public static class MeshDataPool
  {
    private static readonly Stack<MeshData> pool = new();
    private static readonly object gate = new();
    private const int MaxPoolSize = 256;

    public static MeshData Rent(int minVertices = 0, int minIndices = 0)
    {
      MeshData mesh = null;
      lock (gate)
      {
        if (pool.Count > 0)
        {
          mesh = pool.Pop();
        }
      }

      mesh ??= new MeshData();

      if (minVertices > 0 || minIndices > 0)
      {
        mesh.Reserve(minVertices, minIndices);
      }

      mesh.Reset();
      return mesh;
    }

    public static void Return(MeshData mesh)
    {
      if (mesh == null)
      {
        return;
      }

      // Keep contents for capacity reuse but clear counts
      mesh.Reset();

      lock (gate)
      {
        if (pool.Count < MaxPoolSize)
        {
          pool.Push(mesh);
        }
      }
    }
  }
}
