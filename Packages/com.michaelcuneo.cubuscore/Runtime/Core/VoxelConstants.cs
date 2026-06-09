namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core
{
  public static class VoxelConstants
  {
    public const int ChunkSize = 32;
    public const int ChunkVolume = ChunkSize * ChunkSize * ChunkSize;

    // Unity uses meters by convention.
    // Your UE5 version used 100 Unreal units per voxel, which is roughly 1 meter.
    public const float DefaultVoxelSize = 1.0f;
  }
}