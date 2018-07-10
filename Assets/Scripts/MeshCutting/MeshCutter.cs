using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshCutter
{

    public TempMesh PositiveMesh { get; private set; }
    public TempMesh NegativeMesh { get; private set; }

    private List<Vector3> addedPairs;

    private readonly List<Vector3> ogVertices;
    private readonly List<int> ogTriangles;
    private readonly List<Vector3> ogNormals;

    private readonly Vector3[] intersectPair;
    private readonly Vector3[] tempTriangle;

    private Intersections intersect;

    private readonly float threshold = 1e-6f;

    public MeshCutter(int initialArraySize)
    {
        PositiveMesh = new TempMesh(initialArraySize);
        NegativeMesh = new TempMesh(initialArraySize);

        addedPairs = new List<Vector3>(initialArraySize);
        ogVertices = new List<Vector3>(initialArraySize);
        ogTriangles = new List<int>(initialArraySize * 3);
        ogNormals = new List<Vector3>(initialArraySize);

        intersectPair = new Vector3[2];
        tempTriangle = new Vector3[3];

        intersect = new Intersections();
    }

    /// <summary>
    /// Slice a mesh by the slice plane.
    /// We assume the plane is already in the mesh's local coordinate frame
    /// Returns posMesh and negMesh, which are the resuling meshes on both sides of the plane 
    /// (posMesh on the same side as the plane's normal, negMesh on the opposite side)
    /// </summary>
    public bool SliceMesh(Mesh mesh, ref Plane slice)
    {
        // 1. Verify if the bounds intersect first
        if (!Intersections.BoundPlaneIntersect(mesh, ref slice))
        {
            //Debug.Log("Object " + obj.name + " didn't intersect");
            return false;
        }

        mesh.GetVertices(ogVertices);
        mesh.GetTriangles(ogTriangles, 0);
        mesh.GetNormals(ogNormals);

        PositiveMesh.Clear();
        NegativeMesh.Clear();
        addedPairs.Clear();

        // 2. Separate old vertices in new meshes
        for(int i = 0; i < ogVertices.Count; ++i)
        {
            if (slice.GetDistanceToPoint(ogVertices[i]) >= 0)
                PositiveMesh.AddVertex(ogVertices, ogNormals, i);
            else
                NegativeMesh.AddVertex(ogVertices, ogNormals, i);
        }

        // 2.5 : If one of the mesh has no vertices, then it doesn't intersect
        if (NegativeMesh.vertices.Count == 0 || PositiveMesh.vertices.Count == 0)
            return false;

        // 3. Separate triangles and cut those that intersect the plane
        for (int i = 0; i < ogTriangles.Count; i += 3)
        {
            if (intersect.TrianglePlaneIntersect(ogVertices, ogTriangles, i, ref slice, PositiveMesh, NegativeMesh, intersectPair))
                addedPairs.AddRange(intersectPair);
        }

        if (addedPairs.Count > 0)
        {
            //FillBoundaryGeneral(addedPairs);
            FillBoundaryFace(addedPairs);
            return true;
        } else
        {
            throw new UnityException("Error: if added pairs is empty, we should have returned false earlier");
        }
    }

    #region Boundary fill method


    private void FillBoundaryGeneral(List<Vector3> added)
    {
        // 1. Reorder added so in order ot their occurence along the perimeter.
        ReorderList(added);

        Vector3 center = FindCenter(added);

        //Create triangle for each edge to the center
        tempTriangle[2] = center;

        for (int i = 0; i < added.Count; i += 2)
        {
            // Add fronface triangle in meshPositive
            tempTriangle[0] = added[i];
            tempTriangle[1] = added[i + 1];

            PositiveMesh.AddTriangle(tempTriangle);

            // Add backface triangle in meshNegative
            tempTriangle[0] = added[i + 1];
            tempTriangle[1] = added[i];

            NegativeMesh.AddTriangle(tempTriangle);
        }
    }


    private void FillBoundaryFace(List<Vector3> added)
    {
        // 1. Reorder added so in order ot their occurence along the perimeter.
        ReorderList(added);

        // 2. Find actual face vertices
        var face = FindRealPolygon(added);

        // 3. Create triangle fans
        int t_fwd = 0,
            t_bwd = face.Count - 1,
            t_new = 1;
        bool incr_fwd = true;

        while (t_new != t_fwd && t_new != t_bwd)
        {
            AddTriangle(face, t_bwd, t_fwd, t_new);

            if (incr_fwd) t_fwd = t_new;
            else t_bwd = t_new;

            incr_fwd = !incr_fwd;
            t_new = incr_fwd ? t_fwd + 1 : t_bwd - 1;
        }
    }

    /// <summary>
    /// Extract polygon from the pairs of vertices.
    /// Per example, two vectors that are colinear is redundant and only forms one side of the polygon
    /// </summary>
    private List<Vector3> FindRealPolygon(List<Vector3> pairs)
    {
        List<Vector3> vertices = new List<Vector3>();
        Vector3 edge1, edge2;

        // List should be ordered in the correct way
        for (int i = 0; i < pairs.Count; i += 2)
        {
            edge1 = (pairs[i + 1] - pairs[i]);
            if (i == pairs.Count - 2)
                edge2 = pairs[1] - pairs[0];
            else
                edge2 = pairs[i + 3] - pairs[i + 2];

            // Normalize edges
            edge1.Normalize();
            edge2.Normalize();

            if (Vector3.Angle(edge1, edge2) > threshold)
                // This is a corner
                vertices.Add(pairs[i + 1]);
        }

        return vertices;
    }

    private void AddTriangle(List<Vector3> face, int t1, int t2, int t3)
    {
        tempTriangle[0] = face[t1];
        tempTriangle[1] = face[t2];
        tempTriangle[2] = face[t3];
        PositiveMesh.AddTriangle(tempTriangle);

        tempTriangle[1] = face[t3];
        tempTriangle[2] = face[t2];
        NegativeMesh.AddTriangle(tempTriangle);
    }
    #endregion



    /// <summary>
    /// Find center of polygon by averaging vertices
    /// </summary>
    private static Vector3 FindCenter(List<Vector3> pairs)
    {
        Vector3 center = Vector3.zero;
        int count = 0;

        for (int i = 0; i < pairs.Count; i += 2)
        {
            center += pairs[i];
            count++;
        }

        return center / count;
    }

    /// <summary>
    /// Reorder a list of pairs of vectors, where the second vector of a pair matches the first vector of 
    /// </summary>
    private static void ReorderList(List<Vector3> pairs)
    {
        int nbFaces = 0;
        int faceStart = 0;
        int i = 0;
        Vector3 tempFirst, tempSecond;

        // 1. Remove edges where start and end vertices are equal
        // Seems to solve to problem of remaining vertices when connecting faces 
        for (i = pairs.Count - 2; i >= 0; i -= 2)
        {
            if (pairs[i] == pairs[i + 1])
            {
                pairs.RemoveAt(i + 1);
                pairs.RemoveAt(i);
            }
            else
            {
                // Look for equal pairs
                for (int j = i - 2; j >= 0; j -= 2)
                {
                    if (pairs[i] == pairs[j] && pairs[i + 1] == pairs[j + 1])
                    {
                        pairs.RemoveAt(i + 1);
                        pairs.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        i = 0;
        while (i < pairs.Count)
        {
            // Find next adjacent edge
            for (int j = i + 2; j < pairs.Count; j += 2)
            {
                if (pairs[j] == pairs[i + 1])
                {
                    // If j is already at the correct place we break the loop
                    if (j == i + 2) break;

                    // Put j at i+2
                    tempFirst = pairs[i + 2];
                    tempSecond = pairs[i + 3];
                    pairs[i + 2] = pairs[j];
                    pairs[i + 3] = pairs[j + 1];
                    pairs[j] = tempFirst;
                    pairs[j + 1] = tempSecond;
                    break;
                }
            }

            if (i + 3 >= pairs.Count) {
                // Why does this happen?
                /* This seems to happen because edges are so small that at a certain point
                 * some vertices are equal to eachother and one edge is being 
                 */

                Debug.Log("Huh?");
                break;
            }
            else if (pairs[i + 3] == pairs[faceStart])   //TODO: Index out of range error happens here!
            {
                // A face is complete.
                nbFaces++;
                i += 4;
                faceStart = i;
            } else
            {
                i += 2;
            }
        }
    }    
}

