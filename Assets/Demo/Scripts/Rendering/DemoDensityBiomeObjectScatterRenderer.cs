using System.Collections.Generic;
using System.Reflection;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using UnityEngine;

namespace Assets.Demo.Scripts.Rendering
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldRenderer))]
    public sealed class DemoDensityBiomeObjectScatterRenderer : MonoBehaviour
    {
        [Header("Scatter Object")]
        [SerializeField]
        private GameObject scatterObject;

        [SerializeField]
        private bool disableSpawnedColliders = true;

        [Header("Placement")]
        [SerializeField]
        private bool enableTestScatter = true;

        [SerializeField]
        [Min(1)]
        private int maximumTrianglesCheckedPerChunk = 256;

        [SerializeField]
        [Range(-1f, 1f)]
        private float minimumUpwardNormal = 0.35f;

        [SerializeField]
        [Min(0.01f)]
        private float objectScale = 1f;

        [SerializeField]
        private float surfaceOffset;

        [SerializeField]
        private bool alignToSurface = true;

        [SerializeField]
        private bool randomiseYaw = true;

        [SerializeField]
        private bool logDiagnostics = true;
        private readonly Queue<Vector3Int> pendingChunks = new();
        private readonly HashSet<Vector3Int> queuedChunks = new();
        private readonly Dictionary<Vector3Int, GameObject> spawnedByChunk = new();

        private WorldRenderer worldRenderer;

        private FieldInfo densityChunkViewsField;

        private void Awake()
        {
            worldRenderer = GetComponent<WorldRenderer>();

            densityChunkViewsField = typeof(WorldRenderer).GetField(
                "activeDensityChunkViews",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private void OnEnable()
        {
            if (worldRenderer == null)
            {
                worldRenderer = GetComponent<WorldRenderer>();
            }

            if (worldRenderer == null)
            {
                Debug.LogError(
                    "Density scatter test requires a WorldRenderer component.",
                    this);

                enabled = false;
                return;
            }

            worldRenderer.DensityChunkRendered -= QueueChunk;
            worldRenderer.DensityChunkRemoved -= RemoveChunk;
            worldRenderer.ChunksCleared -= ClearAll;

            worldRenderer.DensityChunkRendered += QueueChunk;
            worldRenderer.DensityChunkRemoved += RemoveChunk;
            worldRenderer.ChunksCleared += ClearAll;

            QueueExistingChunks();
        }

        private void OnDisable()
        {
            if (worldRenderer == null)
            {
                return;
            }

            worldRenderer.DensityChunkRendered -= QueueChunk;
            worldRenderer.DensityChunkRemoved -= RemoveChunk;
            worldRenderer.ChunksCleared -= ClearAll;
        }

        private void Update()
        {
            if (!enableTestScatter || pendingChunks.Count == 0)
            {
                return;
            }

            Vector3Int chunkCoord = pendingChunks.Dequeue();
            queuedChunks.Remove(chunkCoord);

            BuildTestObject(chunkCoord);
        }

        private void QueueChunk(Vector3Int chunkCoord)
        {
            if (!enableTestScatter)
            {
                return;
            }

            if (queuedChunks.Add(chunkCoord))
            {
                pendingChunks.Enqueue(chunkCoord);
            }
        }

        private void QueueExistingChunks()
        {
            Dictionary<Vector3Int, ChunkView> densityViews = GetDensityChunkViews();

            if (densityViews == null)
            {
                return;
            }

            foreach (Vector3Int chunkCoord in densityViews.Keys)
            {
                QueueChunk(chunkCoord);
            }
        }

        private void BuildTestObject(Vector3Int chunkCoord)
        {
            RemoveChunk(chunkCoord);

            Dictionary<Vector3Int, ChunkView> densityViews = GetDensityChunkViews();

            if (densityViews == null)
            {
                LogFailure(chunkCoord, "density chunk dictionary unavailable");
                return;
            }

            if (!densityViews.TryGetValue(chunkCoord, out ChunkView chunkView) ||
                chunkView == null)
            {
                LogFailure(chunkCoord, "density chunk view unavailable");
                return;
            }

            Mesh mesh = chunkView.CurrentMesh;

            if (mesh == null)
            {
                LogFailure(chunkCoord, "mesh unavailable");
                return;
            }

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;

            if (vertices == null ||
                vertices.Length == 0 ||
                triangles == null ||
                triangles.Length < 3)
            {
                LogFailure(chunkCoord, "mesh contains no triangles");
                return;
            }

            int triangleCount = triangles.Length / 3;
            int checkedTriangleCount = Mathf.Min(
                triangleCount,
                maximumTrianglesCheckedPerChunk);

            for (int triangleIndex = 0;
                 triangleIndex < checkedTriangleCount;
                 triangleIndex++)
            {
                int index = triangleIndex * 3;

                int indexA = triangles[index];
                int indexB = triangles[index + 1];
                int indexC = triangles[index + 2];

                if (!IsValidVertexIndex(indexA, vertices.Length) ||
                    !IsValidVertexIndex(indexB, vertices.Length) ||
                    !IsValidVertexIndex(indexC, vertices.Length))
                {
                    continue;
                }

                Vector3 vertexA = vertices[indexA];
                Vector3 vertexB = vertices[indexB];
                Vector3 vertexC = vertices[indexC];

                Vector3 localNormal = CalculateTriangleNormal(
                    vertices,
                    normals,
                    indexA,
                    indexB,
                    indexC);

                Vector3 worldNormal = chunkView.transform
                    .TransformDirection(localNormal)
                    .normalized;

                if (worldNormal.y < minimumUpwardNormal)
                {
                    continue;
                }

                Vector3 localPosition =
                    (vertexA + vertexB + vertexC) / 3f;

                Vector3 worldPosition = chunkView.transform
                    .TransformPoint(localPosition);

                if (!SpawnObject(
                        chunkCoord,
                        worldPosition,
                        worldNormal))
                {
                    return;
                }

                if (logDiagnostics)
                {
                    Debug.Log(
                        $"Density scatter test spawned cube for chunk " +
                        $"{chunkCoord} after checking " +
                        $"{triangleIndex + 1} triangles.",
                        this);
                }

                return;
            }

            LogFailure(
                chunkCoord,
                $"no upward-facing triangle found in first " +
                $"{checkedTriangleCount} triangles");
        }

        private bool SpawnObject(
            Vector3Int chunkCoord,
            Vector3 position,
            Vector3 normal)
        {
            if (scatterObject == null)
            {
                LogFailure(
                    chunkCoord,
                    "no scatter object has been assigned");

                return false;
            }

            Quaternion surfaceRotation = alignToSurface
                ? Quaternion.FromToRotation(Vector3.up, normal)
                : Quaternion.identity;

            Quaternion yawRotation = randomiseYaw
                ? Quaternion.AngleAxis(
                    GetDeterministicYaw(chunkCoord),
                    Vector3.up)
                : Quaternion.identity;

            GameObject spawnedObject = Instantiate(
                scatterObject,
                position + normal * surfaceOffset,
                surfaceRotation * yawRotation,
                transform);

            spawnedObject.name =
                $"{scatterObject.name} {chunkCoord}";

            spawnedObject.transform.localScale =
                scatterObject.transform.localScale * objectScale;

            if (disableSpawnedColliders)
            {
                Collider[] colliders =
                    spawnedObject.GetComponentsInChildren<Collider>(true);

                foreach (Collider spawnedCollider in colliders)
                {
                    Destroy(spawnedCollider);
                }
            }

            spawnedByChunk[chunkCoord] = spawnedObject;

            return true;
        }

        private Dictionary<Vector3Int, ChunkView>
            GetDensityChunkViews()
        {
            if (worldRenderer == null ||
                densityChunkViewsField == null)
            {
                return null;
            }

            return densityChunkViewsField.GetValue(worldRenderer)
                as Dictionary<Vector3Int, ChunkView>;
        }

        private void RemoveChunk(Vector3Int chunkCoord)
        {
            queuedChunks.Remove(chunkCoord);

            if (!spawnedByChunk.TryGetValue(
                    chunkCoord,
                    out GameObject spawnedObject))
            {
                return;
            }

            spawnedByChunk.Remove(chunkCoord);

            if (spawnedObject != null)
            {
                Destroy(spawnedObject);
            }
        }

        private void ClearAll()
        {
            pendingChunks.Clear();
            queuedChunks.Clear();

            foreach (GameObject spawnedObject in
                     spawnedByChunk.Values)
            {
                if (spawnedObject != null)
                {
                    Destroy(spawnedObject);
                }
            }

            spawnedByChunk.Clear();
        }

        private void OnDestroy()
        {
            ClearAll();
        }

        private void LogFailure(
            Vector3Int chunkCoord,
            string reason)
        {
            if (!logDiagnostics)
            {
                return;
            }

            Debug.Log(
                $"Density scatter test skipped chunk " +
                $"{chunkCoord}: {reason}.",
                this);
        }

        private static Vector3 CalculateTriangleNormal(
            Vector3[] vertices,
            Vector3[] normals,
            int indexA,
            int indexB,
            int indexC)
        {
            if (normals != null &&
                normals.Length == vertices.Length)
            {
                Vector3 averagedNormal =
                    normals[indexA] +
                    normals[indexB] +
                    normals[indexC];

                if (averagedNormal.sqrMagnitude > 0.000001f)
                {
                    return averagedNormal.normalized;
                }
            }

            Vector3 edgeA =
                vertices[indexB] - vertices[indexA];

            Vector3 edgeB =
                vertices[indexC] - vertices[indexA];

            return Vector3.Cross(edgeA, edgeB).normalized;
        }

        private static bool IsValidVertexIndex(
            int index,
            int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        private static float GetDeterministicYaw(
            Vector3Int chunkCoord)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + chunkCoord.x;
                hash = hash * 31 + chunkCoord.y;
                hash = hash * 31 + chunkCoord.z;

                uint positiveHash = (uint)hash;

                return positiveHash % 360u;
            }
        }
    }
}
