using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Storage;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Validation;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace CubusCore.Tests
{
  /// <summary>
  /// Roadmap item 1 - Fidelity-safe compression.
  /// Block payloads are lossless (exact round-trip). Density payloads are
  /// quantized to 1/4096 steps, so the round-trip must stay within that
  /// quantization tolerance and preserve material ids exactly.
  /// </summary>
  [TestFixture]
  public sealed class ChunkCompressionTests
  {
    private const string WorldId = "test-world";

    // CubusChunkPayloadCodec.DensityQuantizationScale = 4096. Round-trip error is
    // bounded by one quantization step; allow a small epsilon for float math.
    private const float DensityQuantizationStep = 1.0f / 4096.0f;
    private const float DensityTolerance = DensityQuantizationStep + 1e-5f;

    [Test]
    public void BlockChunk_RoundTrips_Losslessly()
    {
      var coord = new Vector3Int(3, -1, 5);
      BlockChunkData original = CubusTestFactory.BuildPatternedBlockChunk(coord);

      WorldChunkRecord record = CubusChunkPayloadCodec.EncodeBlockChunk(WorldId, coord, original);
      BlockChunkData decoded = CubusChunkPayloadCodec.DecodeBlockChunk(record);

      Assert.IsTrue(
        ChunkDeterminismHash.AreEqual(original, decoded, out string diff),
        $"Block chunk did not round-trip losslessly. {diff}");
      Assert.AreEqual(
        ChunkDeterminismHash.Hash(original),
        ChunkDeterminismHash.Hash(decoded),
        "Block chunk hash changed across encode/decode.");
    }

    [Test]
    public void BlockChunk_StampsCurrentPayloadVersion()
    {
      var coord = new Vector3Int(0, 0, 0);
      BlockChunkData original = CubusTestFactory.BuildPatternedBlockChunk(coord);

      WorldChunkRecord record = CubusChunkPayloadCodec.EncodeBlockChunk(WorldId, coord, original);

      Assert.AreEqual(CubusChunkPayloadCodec.PayloadVersion, record.PayloadVersion);
      Assert.AreEqual(WorldId, record.WorldId);
      Assert.AreEqual(coord, record.ChunkCoord);
      Assert.IsNotNull(record.PayloadBytes);
    }

    [Test]
    public void DensityChunk_RoundTrips_WithinQuantizationTolerance()
    {
      var coord = new Vector3Int(-2, 0, 4);
      DensityChunkData original = CubusTestFactory.BuildPatternedDensityChunk(coord);

      WorldChunkRecord record = CubusChunkPayloadCodec.EncodeDensityChunk(WorldId, coord, original);
      DensityChunkData decoded = CubusChunkPayloadCodec.DecodeDensityChunk(record);

      DensityVoxel[] originalVoxels = original.GetRawVoxelArray();
      DensityVoxel[] decodedVoxels = decoded.GetRawVoxelArray();

      Assert.AreEqual(originalVoxels.Length, decodedVoxels.Length, "Density voxel count changed.");

      float maxError = 0.0f;
      for (int i = 0; i < originalVoxels.Length; i++)
      {
        float error = Mathf.Abs(originalVoxels[i].Density - decodedVoxels[i].Density);
        maxError = Mathf.Max(maxError, error);

        Assert.AreEqual(
          originalVoxels[i].MaterialId,
          decodedVoxels[i].MaterialId,
          $"Material id changed at flat index {i}.");
      }

      Assert.LessOrEqual(
        maxError,
        DensityTolerance,
        $"Density round-trip error {maxError} exceeded quantization tolerance {DensityTolerance}.");
    }

    [Test]
    public void DensityChunk_StampsCurrentPayloadVersion()
    {
      var coord = new Vector3Int(0, 0, 0);
      DensityChunkData original = CubusTestFactory.BuildPatternedDensityChunk(coord);

      WorldChunkRecord record = CubusChunkPayloadCodec.EncodeDensityChunk(WorldId, coord, original);

      Assert.AreEqual(CubusChunkPayloadCodec.PayloadVersion, record.PayloadVersion);
      Assert.IsNotNull(record.PayloadBytes);
    }

    [Test]
    public void DensityChunk_Compresses_BelowRawSize()
    {
      // Fidelity-safe compression should still beat the raw float+material layout
      // for typical terrain data (long uniform runs compress under RLE).
      var coord = new Vector3Int(0, 0, 0);
      DensityChunkData original = CubusTestFactory.BuildLayeredDensityChunk(coord);

      WorldChunkRecord record = CubusChunkPayloadCodec.EncodeDensityChunk(WorldId, coord, original);

      int rawBytes = VoxelConstants.ChunkVolume * (sizeof(float) + sizeof(ushort));
      Assert.Less(
        record.PayloadBytes.Length,
        rawBytes,
        "Encoded density payload was not smaller than the raw representation.");
    }
  }
}
