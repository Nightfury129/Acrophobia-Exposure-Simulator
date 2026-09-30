using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRSlashGame.Gameplay
{
    /// <summary>
    /// Provides zero-allocation runtime 3D geometric mesh slicing along an arbitrary plane.
    /// Slices triangles, interpolates vertex data (position, normal, UV),
    /// generates planar cap faces for solid interior fruit volume, and writes directly
    /// into pre-allocated pooled Mesh instances without triggering garbage collection.
    /// </summary>
    public static class MeshSlicer
    {
        private const float Epsilon = 1e-6f;
        private const int InitialVertexCapacity = 2048;
        private const int InitialTriangleCapacity = 4096;
        private const int InitialEdgeCapacity = 512;

        public class SlicedMeshBuilder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(InitialVertexCapacity);
            public readonly List<Vector3> Normals = new List<Vector3>(InitialVertexCapacity);
            public readonly List<Vector2> UVs = new List<Vector2>(InitialVertexCapacity);
            public readonly List<int> Triangles = new List<int>(InitialTriangleCapacity);

            public void Clear()
            {
                Vertices.Clear();
                Normals.Clear();
                UVs.Clear();
                Triangles.Clear();
            }

            public int AddVertex(Vector3 pos, Vector3 norm, Vector2 uv)
            {
                int index = Vertices.Count;
                Vertices.Add(pos);
                Normals.Add(norm);
                UVs.Add(uv);
                return index;
            }

            public void AddTriangle(int i0, int i1, int i2)
            {
                Triangles.Add(i0);
                Triangles.Add(i1);
                Triangles.Add(i2);
            }

            /// <summary>
            /// Writes vertex/index buffers into an existing pre-allocated Mesh instance with 0 B GC.
            /// </summary>
            public void ApplyToMesh(Mesh targetMesh, string name)
            {
                if (targetMesh == null) return;

                targetMesh.Clear(false);
                targetMesh.name = name;
                targetMesh.indexFormat = Vertices.Count > 65000 
                    ? IndexFormat.UInt32 
                    : IndexFormat.UInt16;

                targetMesh.SetVertices(Vertices);
                targetMesh.SetNormals(Normals);
                targetMesh.SetUVs(0, UVs);
                targetMesh.SetTriangles(Triangles, 0);
                targetMesh.RecalculateBounds();
                // Mobile VR Optimization: RecalculateTangents() intentionally omitted.
                // Tangent evaluation invokes expensive CPU Mikktspace / iterative neighborhood search
                // that stalls mobile ARM cores and generates garbage. Dissolve & fruit shaders do not use tangents.
            }

            /// <summary>
            /// Allocates a new Mesh (backward compatibility fallback).
            /// </summary>
            public Mesh ToMesh(string name)
            {
                Mesh mesh = new Mesh();
                ApplyToMesh(mesh, name);
                return mesh;
            }
        }

        private struct CutEdge
        {
            public Vector3 PointA;
            public Vector3 PointB;

            public CutEdge(Vector3 a, Vector3 b)
            {
                PointA = a;
                PointB = b;
            }
        }

        #region Pre-allocated Static Scratch Buffers (Zero-Allocation)

        private static readonly List<Vector3> s_SrcVertices = new List<Vector3>(InitialVertexCapacity);
        private static readonly List<Vector3> s_SrcNormals = new List<Vector3>(InitialVertexCapacity);
        private static readonly List<Vector2> s_SrcUVs = new List<Vector2>(InitialVertexCapacity);
        private static readonly List<int> s_SrcTriangles = new List<int>(InitialTriangleCapacity);

        private static readonly SlicedMeshBuilder s_PosBuilder = new SlicedMeshBuilder();
        private static readonly SlicedMeshBuilder s_NegBuilder = new SlicedMeshBuilder();

        private static readonly List<CutEdge> s_CutEdges = new List<CutEdge>(InitialEdgeCapacity);
        private static readonly List<CutEdge> s_RemainingEdges = new List<CutEdge>(InitialEdgeCapacity);

        private static readonly List<List<Vector3>> s_ActiveLoops = new List<List<Vector3>>(32);
        private static readonly List<List<Vector3>> s_LoopPool = new List<List<Vector3>>(32);

        private static readonly List<int> s_PosIndices = new List<int>(256);
        private static readonly List<int> s_NegIndices = new List<int>(256);

        private static List<Vector3> GetPooledLoop()
        {
            if (s_LoopPool.Count > 0)
            {
                int lastIdx = s_LoopPool.Count - 1;
                List<Vector3> loop = s_LoopPool[lastIdx];
                s_LoopPool.RemoveAt(lastIdx);
                loop.Clear();
                return loop;
            }
            return new List<Vector3>(64);
        }

        private static void ReleaseLoops()
        {
            for (int i = 0; i < s_ActiveLoops.Count; i++)
            {
                s_ActiveLoops[i].Clear();
                s_LoopPool.Add(s_ActiveLoops[i]);
            }
            s_ActiveLoops.Clear();
        }

        #endregion

        /// <summary>
        /// Slices a mesh by a plane defined in the mesh's local coordinate system, writing directly
        /// into pre-allocated target meshes with 0 B GC heap allocation.
        /// Returns true if the mesh was successfully bisected into two non-empty halves.
        /// </summary>
        public static bool Slice(
            Mesh sourceMesh, 
            Plane localPlane, 
            Mesh targetPosMesh, 
            Mesh targetNegMesh)
        {
            if (sourceMesh == null || sourceMesh.vertexCount == 0 || targetPosMesh == null || targetNegMesh == null)
            {
                return false;
            }

            // Extract source mesh data into pre-allocated reusable lists (0 B GC)
            s_SrcVertices.Clear();
            s_SrcNormals.Clear();
            s_SrcUVs.Clear();
            s_SrcTriangles.Clear();

            sourceMesh.GetVertices(s_SrcVertices);
            sourceMesh.GetNormals(s_SrcNormals);
            sourceMesh.GetUVs(0, s_SrcUVs);
            sourceMesh.GetTriangles(s_SrcTriangles, 0);

            bool hasNormals = s_SrcNormals.Count == s_SrcVertices.Count;
            bool hasUVs = s_SrcUVs.Count == s_SrcVertices.Count;

            s_PosBuilder.Clear();
            s_NegBuilder.Clear();
            s_CutEdges.Clear();

            // 1. Bisect triangles across the slice plane
            int triCount = s_SrcTriangles.Count / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = s_SrcTriangles[t * 3 + 0];
                int i1 = s_SrcTriangles[t * 3 + 1];
                int i2 = s_SrcTriangles[t * 3 + 2];

                Vector3 v0 = s_SrcVertices[i0];
                Vector3 v1 = s_SrcVertices[i1];
                Vector3 v2 = s_SrcVertices[i2];

                Vector3 n0 = hasNormals ? s_SrcNormals[i0] : Vector3.up;
                Vector3 n1 = hasNormals ? s_SrcNormals[i1] : Vector3.up;
                Vector3 n2 = hasNormals ? s_SrcNormals[i2] : Vector3.up;

                Vector2 u0 = hasUVs ? s_SrcUVs[i0] : Vector2.zero;
                Vector2 u1 = hasUVs ? s_SrcUVs[i1] : Vector2.zero;
                Vector2 u2 = hasUVs ? s_SrcUVs[i2] : Vector2.zero;

                float d0 = localPlane.GetDistanceToPoint(v0);
                float d1 = localPlane.GetDistanceToPoint(v1);
                float d2 = localPlane.GetDistanceToPoint(v2);

                bool side0 = d0 >= 0f;
                bool side1 = d1 >= 0f;
                bool side2 = d2 >= 0f;

                if (side0 == side1 && side1 == side2)
                {
                    // Entire triangle lies on one side
                    SlicedMeshBuilder target = side0 ? s_PosBuilder : s_NegBuilder;
                    int new0 = target.AddVertex(v0, n0, u0);
                    int new1 = target.AddVertex(v1, n1, u1);
                    int new2 = target.AddVertex(v2, n2, u2);
                    target.AddTriangle(new0, new1, new2);
                }
                else
                {
                    // Triangle is intersected by the plane: 1 vertex on one side, 2 on the other
                    if (side0 != side1 && side0 != side2)
                    {
                        SplitTriangle(v0, n0, u0, d0, side0,
                                      v1, n1, u1, d1,
                                      v2, n2, u2, d2,
                                      s_PosBuilder, s_NegBuilder, s_CutEdges);
                    }
                    else if (side1 != side0 && side1 != side2)
                    {
                        SplitTriangle(v1, n1, u1, d1, side1,
                                      v2, n2, u2, d2,
                                      v0, n0, u0, d0,
                                      s_PosBuilder, s_NegBuilder, s_CutEdges);
                    }
                    else
                    {
                        SplitTriangle(v2, n2, u2, d2, side2,
                                      v0, n0, u0, d0,
                                      v1, n1, u1, d1,
                                      s_PosBuilder, s_NegBuilder, s_CutEdges);
                    }
                }
            }

            // If no triangles crossed or either side is empty, slicing did not bisect the mesh
            if (s_PosBuilder.Vertices.Count == 0 || s_NegBuilder.Vertices.Count == 0 || s_CutEdges.Count == 0)
            {
                return false;
            }

            // 2. Cap the cut faces by triangulating the cross-section polygon loops
            CapCutFaces(s_CutEdges, localPlane, s_PosBuilder, s_NegBuilder);

            // 3. Write final geometry into target meshes without GC allocation
            s_PosBuilder.ApplyToMesh(targetPosMesh, $"{sourceMesh.name}_Positive");
            s_NegBuilder.ApplyToMesh(targetNegMesh, $"{sourceMesh.name}_Negative");

            return true;
        }

        /// <summary>
        /// Backward-compatible overload that creates new Mesh instances if pooled meshes are not supplied.
        /// </summary>
        public static bool Slice(
            Mesh sourceMesh, 
            Plane localPlane, 
            out Mesh positiveMesh, 
            out Mesh negativeMesh)
        {
            Mesh pos = new Mesh();
            Mesh neg = new Mesh();

            if (Slice(sourceMesh, localPlane, pos, neg))
            {
                positiveMesh = pos;
                negativeMesh = neg;
                return true;
            }

            UnityEngine.Object.Destroy(pos);
            UnityEngine.Object.Destroy(neg);
            positiveMesh = null;
            negativeMesh = null;
            return false;
        }

        private static void SplitTriangle(
            Vector3 vAlone, Vector3 nAlone, Vector2 uAlone, float dAlone, bool sideAlone,
            Vector3 vA, Vector3 nA, Vector2 uA, float dA,
            Vector3 vB, Vector3 nB, Vector2 uB, float dB,
            SlicedMeshBuilder posBuilder, 
            SlicedMeshBuilder negBuilder,
            List<CutEdge> cutEdges)
        {
            float tA = Mathf.Clamp01(Mathf.Abs(dAlone) / (Mathf.Abs(dAlone) + Mathf.Abs(dA) + Epsilon));
            float tB = Mathf.Clamp01(Mathf.Abs(dAlone) / (Mathf.Abs(dAlone) + Mathf.Abs(dB) + Epsilon));

            Vector3 ptA = Vector3.Lerp(vAlone, vA, tA);
            Vector3 ptB = Vector3.Lerp(vAlone, vB, tB);

            Vector3 normA = Vector3.Lerp(nAlone, nA, tA).normalized;
            Vector3 normB = Vector3.Lerp(nAlone, nB, tB).normalized;

            Vector2 uvA = Vector2.Lerp(uAlone, uA, tA);
            Vector2 uvB = Vector2.Lerp(uAlone, uB, tB);

            SlicedMeshBuilder aloneBuilder = sideAlone ? posBuilder : negBuilder;
            SlicedMeshBuilder pairBuilder = sideAlone ? negBuilder : posBuilder;

            // 1 triangle on alone side: (vAlone, ptA, ptB)
            int iAlone = aloneBuilder.AddVertex(vAlone, nAlone, uAlone);
            int iPtA_alone = aloneBuilder.AddVertex(ptA, normA, uvA);
            int iPtB_alone = aloneBuilder.AddVertex(ptB, normB, uvB);
            aloneBuilder.AddTriangle(iAlone, iPtA_alone, iPtB_alone);

            // 2 triangles (quad) on pair side: (ptA, vA, ptB) and (vA, vB, ptB)
            int iPtA_pair = pairBuilder.AddVertex(ptA, normA, uvA);
            int iVA = pairBuilder.AddVertex(vA, nA, uA);
            int iPtB_pair = pairBuilder.AddVertex(ptB, normB, uvB);
            int iVB = pairBuilder.AddVertex(vB, nB, uB);

            pairBuilder.AddTriangle(iPtA_pair, iVA, iPtB_pair);
            pairBuilder.AddTriangle(iVA, iVB, iPtB_pair);

            // Record the cut edge along the intersection plane
            if (sideAlone)
            {
                cutEdges.Add(new CutEdge(ptA, ptB));
            }
            else
            {
                cutEdges.Add(new CutEdge(ptB, ptA));
            }
        }

        private static void CapCutFaces(
            List<CutEdge> edges, 
            Plane plane, 
            SlicedMeshBuilder posBuilder, 
            SlicedMeshBuilder negBuilder)
        {
            if (edges.Count < 3) return;

            // Compute local 2D projection basis on slice plane
            Vector3 planeNormal = plane.normal.normalized;
            Vector3 tangent = Vector3.Cross(planeNormal, Vector3.up);
            if (tangent.sqrMagnitude < 0.01f)
            {
                tangent = Vector3.Cross(planeNormal, Vector3.right);
            }
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(planeNormal, tangent).normalized;

            // Chain edges into ordered loops (polygons) without GC allocation
            JoinEdgesIntoLoops(edges);

            for (int l = 0; l < s_ActiveLoops.Count; l++)
            {
                List<Vector3> loop = s_ActiveLoops[l];
                if (loop.Count < 3) continue;

                // Calculate polygon centroid for fan triangulation
                Vector3 center = Vector3.zero;
                for (int i = 0; i < loop.Count; i++)
                {
                    center += loop[i];
                }
                center /= loop.Count;

                // Calculate planar UV for center
                Vector2 centerUV = new Vector2(
                    Vector3.Dot(center, tangent) + 0.5f,
                    Vector3.Dot(center, bitangent) + 0.5f
                );

                // Add centroid to builders
                int posCenterIdx = posBuilder.AddVertex(center, -planeNormal, centerUV);
                int negCenterIdx = negBuilder.AddVertex(center, planeNormal, centerUV);

                s_PosIndices.Clear();
                s_NegIndices.Clear();

                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 pt = loop[i];
                    Vector2 uv = new Vector2(
                        Vector3.Dot(pt, tangent) + 0.5f,
                        Vector3.Dot(pt, bitangent) + 0.5f
                    );

                    s_PosIndices.Add(posBuilder.AddVertex(pt, -planeNormal, uv));
                    s_NegIndices.Add(negBuilder.AddVertex(pt, planeNormal, uv));
                }

                // Triangulate fan: center -> loop[i] -> loop[i+1]
                for (int i = 0; i < loop.Count; i++)
                {
                    int next = (i + 1) % loop.Count;

                    // Positive half (winding order facing outward)
                    posBuilder.AddTriangle(posCenterIdx, s_PosIndices[next], s_PosIndices[i]);

                    // Negative half (opposite winding)
                    negBuilder.AddTriangle(negCenterIdx, s_NegIndices[i], s_NegIndices[next]);
                }
            }

            ReleaseLoops();
        }

        private static void JoinEdgesIntoLoops(List<CutEdge> edges)
        {
            s_ActiveLoops.Clear();
            s_RemainingEdges.Clear();
            s_RemainingEdges.AddRange(edges);
            float mergeToleranceSq = 0.0004f;

            while (s_RemainingEdges.Count > 0)
            {
                List<Vector3> loop = GetPooledLoop();
                CutEdge current = s_RemainingEdges[0];
                s_RemainingEdges.RemoveAt(0);

                loop.Add(current.PointA);
                loop.Add(current.PointB);

                bool isClosed = false;
                bool foundNext = true;
                while (foundNext && s_RemainingEdges.Count > 0)
                {
                    foundNext = false;
                    Vector3 tip = loop[loop.Count - 1];

                    for (int i = 0; i < s_RemainingEdges.Count; i++)
                    {
                        if ((s_RemainingEdges[i].PointA - tip).sqrMagnitude <= mergeToleranceSq)
                        {
                            loop.Add(s_RemainingEdges[i].PointB);
                            s_RemainingEdges.RemoveAt(i);
                            foundNext = true;
                            break;
                        }
                        else if ((s_RemainingEdges[i].PointB - tip).sqrMagnitude <= mergeToleranceSq)
                        {
                            loop.Add(s_RemainingEdges[i].PointA);
                            s_RemainingEdges.RemoveAt(i);
                            foundNext = true;
                            break;
                        }
                    }

                    // Check if loop has closed back to start
                    if ((loop[loop.Count - 1] - loop[0]).sqrMagnitude <= mergeToleranceSq)
                    {
                        loop.RemoveAt(loop.Count - 1);
                        isClosed = true;
                        break;
                    }
                }

                if (isClosed && loop.Count >= 3)
                {
                    s_ActiveLoops.Add(loop);
                }
                else
                {
                    loop.Clear();
                    s_LoopPool.Add(loop);
                }
            }
        }
    }
}