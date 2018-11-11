/*
* Copyright (c) <2018> Side Effects Software Inc.
* All rights reserved.
*
* Redistribution and use in source and binary forms, with or without
* modification, are permitted provided that the following conditions are met:
*
* 1. Redistributions of source code must retain the above copyright notice,
*    this list of conditions and the following disclaimer.
*
* 2. The name of Side Effects Software may not be used to endorse or
*    promote products derived from this software without specific prior
*    written permission.
*
* THIS SOFTWARE IS PROVIDED BY SIDE EFFECTS SOFTWARE "AS IS" AND ANY EXPRESS
* OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
* OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  IN
* NO EVENT SHALL SIDE EFFECTS SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT,
* INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
* LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
* OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
* LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
* NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
* EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

#if (UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_LINUX)
#define HOUDINIENGINEUNITY_ENABLED
#endif

using System.Text;
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace HoudiniEngineUnity
{
	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Typedefs (copy these from HEU_Common.cs)
	using HAPI_NodeId = System.Int32;
	using HAPI_PartId = System.Int32;

	/// <summary>
	/// Geometry-specific utility functions.
	/// </summary>
	public static class HEU_GeometryUtility
	{

		public static Vector2[] GeneratePerTriangle(Mesh meshSrc)
		{
#if UNITY_EDITOR
			return Unwrapping.GeneratePerTriangleUV(meshSrc);
#else
			Debug.LogWarning("GeneratePerTriangle is unavailable at runtime!");
			return null;
#endif
		}

		public static void GenerateSecondaryUVSet(Mesh meshsrc)
		{
#if UNITY_EDITOR
			UnwrapParam param;
			UnwrapParam.SetDefaults(out param);
			Unwrapping.GenerateSecondaryUVSet(meshsrc, param);
#else
			Debug.LogWarning("GenerateSecondaryUVSet is unavailable at runtime!");
#endif
		}


		/// <summary>
		/// Calculate the tangents for the given mesh.
		/// Does nothing if the mesh has no geometry, UVs, or normals.
		/// </summary>
		/// <param name="mesh">Source mesh to calculate tangents for.</param>
		public static void CalculateMeshTangents(Mesh mesh)
		{
			// Copy to local arrays
			int[] triangles = mesh.triangles;
			Vector3[] vertices = mesh.vertices;
			Vector2[] uv = mesh.uv;
			Vector3[] normals = mesh.normals;

			if (triangles == null || vertices == null || uv == null || normals == null 
				|| triangles.Length == 0 || vertices.Length == 0 || uv.Length == 0 || normals.Length == 0)
			{
				return;
			}

			int triangleCount = triangles.Length;
			int vertexCount = vertices.Length;

			Vector3[] tan1 = new Vector3[vertexCount];
			Vector3[] tan2 = new Vector3[vertexCount];
			Vector4[] tangents = new Vector4[vertexCount];

			for (long a = 0; a < triangleCount; a += 3)
			{
				long i1 = triangles[a + 0];
				long i2 = triangles[a + 1];
				long i3 = triangles[a + 2];

				Vector3 v1 = vertices[i1];
				Vector3 v2 = vertices[i2];
				Vector3 v3 = vertices[i3];

				Vector2 w1 = uv[i1];
				Vector2 w2 = uv[i2];
				Vector2 w3 = uv[i3];

				float x1 = v2.x - v1.x;
				float x2 = v3.x - v1.x;
				float y1 = v2.y - v1.y;
				float y2 = v3.y - v1.y;
				float z1 = v2.z - v1.z;
				float z2 = v3.z - v1.z;

				float s1 = w2.x - w1.x;
				float s2 = w3.x - w1.x;
				float t1 = w2.y - w1.y;
				float t2 = w3.y - w1.y;

				float div = s1 * t2 - s2 * t1;
				float r = div == 0.0f ? 0.0f : 1.0f / div;

				Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * r,
											(t2 * y1 - t1 * y2) * r,
											(t2 * z1 - t1 * z2) * r);
				Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * r,
											(s1 * y2 - s2 * y1) * r,
											(s1 * z2 - s2 * z1) * r);
				tan1[i1] += sdir;
				tan1[i2] += sdir;
				tan1[i3] += sdir;

				tan2[i1] += tdir;
				tan2[i2] += tdir;
				tan2[i3] += tdir;
			}

			for (long a = 0; a < vertexCount; ++a)
			{
				Vector3 n = normals[a];
				Vector3 t = tan1[a];

				Vector3.OrthoNormalize(ref n, ref t);
				tangents[a].x = t.x;
				tangents[a].y = t.y;
				tangents[a].z = t.z;

				tangents[a].w = (Vector3.Dot(Vector3.Cross(n, t), tan2[a]) < 0.0f) ? -1.0f : 1.0f;
			}

			mesh.tangents = tangents;
		}

		public static bool GenerateTerrainFromVolume(HEU_SessionBase session, ref HAPI_VolumeInfo volumeInfo, HAPI_NodeId geoID, HAPI_PartId partID, 
			GameObject gameObject, out TerrainData terrainData, out Vector3 volumePositionOffset)
		{
			terrainData = null;
			volumePositionOffset = Vector3.zero;

			if (volumeInfo.zLength == 1 && volumeInfo.tupleSize == 1)
			{
				// Heightfields will be converted to terrain in Unity.
				// Unity requires terrainData.heightmapResolution to be square power of two plus 1 (eg. 513, 257, 129, 65).
				// Houdini gives volumeInfo.xLength and volumeInfo.yLength which are the number of height values per dimension.
				// Note that volumeInfo.xLength and volumeInfo.yLength is equal to Houdini heightfield size / grid spacing.
				// The heightfield grid spacing is given as volumeTransformMatrix.scale but divided by 2 (grid spacing / 2 = volumeTransformMatrix.scale).
				// It is recommended to use grid spacing of 2.

				// Use the volumeInfo.transform to get the actual heightfield position and size.
				Matrix4x4 volumeTransformMatrix = HEU_HAPIUtility.GetMatrixFromHAPITransform(ref volumeInfo.transform, false);
				Vector3 position = HEU_HAPIUtility.GetPosition(ref volumeTransformMatrix);
				Vector3 scale = HEU_HAPIUtility.GetScale(ref volumeTransformMatrix);

				// Calculate real terrain size in both Houdini and Unity.
				// The height values will be mapped over this terrain size.
				float gridSpacingX = scale.x * 2f;
				float gridSpacingY = scale.y * 2f;
				//float gridSpacingZ = scale.z * 2f;
				float multiplierOffsetX = Mathf.Round(scale.x);
				float multiplierOffsetY = Mathf.Round(scale.y);
				//float multiplierOffsetZ = Mathf.Round(scale.z);
				float terrainSizeX = Mathf.Round(volumeInfo.xLength * gridSpacingX - multiplierOffsetX);
				float terrainSizeY = Mathf.Round(volumeInfo.yLength * gridSpacingY - multiplierOffsetY);

				//Debug.LogFormat("GS = {0},{1},{2}. SX = {1}. SY = {2}", gridSpacingX, gridSpacingY, gridSpacingZ, terrainSizeX, terrainSizeY);

				//Debug.LogFormat("HeightField Pos:{0}, Scale:{1}", position, scale.ToString("{0.00}"));
				//Debug.LogFormat("HeightField tileSize:{0}, xLength:{1}, yLength:{2}", volumeInfo.tileSize.ToString("{0.00}"), volumeInfo.xLength.ToString("{0.00}"), volumeInfo.yLength.ToString("{0.00}"));
				//Debug.LogFormat("HeightField Terrain Size x:{0}, y:{1}", terrainSizeX.ToString("{0.00}"), terrainSizeY.ToString("{0.00}"));
				//Debug.LogFormat("HeightField minX={0}, minY={1}, minZ={2}", volumeInfo.minX.ToString("{0.00}"), volumeInfo.minY.ToString("{0.00}"), volumeInfo.minZ.ToString("{0.00}"));

				const int UNITY_MINIMUM_HEIGHTMAP_RESOLUTION = 33;
				if (terrainSizeX < UNITY_MINIMUM_HEIGHTMAP_RESOLUTION || terrainSizeY < UNITY_MINIMUM_HEIGHTMAP_RESOLUTION)
				{
					Debug.LogWarningFormat("Unity Terrain has a minimum heightmap resolution of {0}. This HDA heightmap size is {1}x{2}."
						+ "\nPlease resize the terrain to a value higher than this.",
						UNITY_MINIMUM_HEIGHTMAP_RESOLUTION, terrainSizeX, terrainSizeY);
					return false;
				}

				Terrain terrain = HEU_GeneralUtility.GetOrCreateComponent<Terrain>(gameObject);
				TerrainCollider collider = HEU_GeneralUtility.GetOrCreateComponent<TerrainCollider>(gameObject);
				
				if (terrain.terrainData == null)
				{
					terrain.terrainData = new TerrainData();
				}

				terrainData = terrain.terrainData;
				collider.terrainData = terrainData;

				// Heightmap resolution must be square power-of-two plus 1. 
				// Unity will automatically resize terrainData.heightmapResolution so need to handle the changed size (if Unity changed it).
				int heightMapResolution = volumeInfo.xLength;
				terrainData.heightmapResolution = heightMapResolution;
				int terrainResizedDelta = terrainData.heightmapResolution - heightMapResolution;
				if (terrainResizedDelta < 0)
				{
					Debug.LogWarningFormat("Note that Unity automatically resized terrain resolution to {0} from {1}. Use terrain size of power of two plus 1, and grid spacing of 2.", heightMapResolution, terrainData.heightmapResolution);
					heightMapResolution = terrainData.heightmapResolution;
				}
				else if(terrainResizedDelta > 0)
				{
					Debug.LogErrorFormat("Unsupported terrain size. Use terrain size of power of two plus 1, and grid spacing of 2. (delta = {0})", terrainResizedDelta);
					return false;
				}

				// Get the height values from Houdini and find the min and max height range.
				int totalHeightValues = volumeInfo.xLength * volumeInfo.yLength;
				float[] heightValues = new float[totalHeightValues];

				bool bResult = HEU_GeneralUtility.GetArray2Arg(geoID, partID, session.GetHeightFieldData, heightValues, 0, totalHeightValues);
				if (!bResult)
				{
					return false;
				}

				float minHeight = heightValues[0];
				float maxHeight = minHeight;
				for (int i = 0; i < totalHeightValues; ++i)
				{
					float f = heightValues[i];
					if (f > maxHeight)
					{
						maxHeight = f;
					}
					else if (f < minHeight)
					{
						minHeight = f;
					}
				} 

				const int UNITY_MAX_HEIGHT_RANGE = 65536;
				float heightRange = (maxHeight - minHeight);
				if (Mathf.RoundToInt(heightRange) > UNITY_MAX_HEIGHT_RANGE)
				{
					Debug.LogWarningFormat("Unity Terrain has maximum height range of {0}. This HDA height range is {1}, so it will be maxed out at {0}.\nPlease resize to within valid range!",
						UNITY_MAX_HEIGHT_RANGE, Mathf.RoundToInt(heightRange));
					heightRange = UNITY_MAX_HEIGHT_RANGE;
				}

				int mapWidth = volumeInfo.xLength;
				int mapHeight = volumeInfo.yLength;

				int paddingWidth = heightMapResolution - mapWidth;
				int paddingLeft = Mathf.CeilToInt(paddingWidth * 0.5f);
				int paddingRight = heightMapResolution - paddingLeft;
				//Debug.LogFormat("Padding: Width={0}, Left={1}, Right={2}", paddingWidth, paddingLeft, paddingRight);

				int paddingHeight = heightMapResolution - mapHeight;
				int paddingTop = Mathf.CeilToInt(paddingHeight * 0.5f);
				int paddingBottom = heightMapResolution - paddingTop;
				//Debug.LogFormat("Padding: Height={0}, Top={1}, Bottom={2}", paddingHeight, paddingTop, paddingBottom);

				// Set height values at centre of the terrain, with padding on the sides if we resized
				float[,] unityHeights = new float[heightMapResolution, heightMapResolution];
				for (int y = 0; y < heightMapResolution; ++y)
				{
					for (int x = 0; x < heightMapResolution; ++x)
					{	
						if (y >= paddingTop && y < (paddingBottom) && x >= paddingLeft && x < (paddingRight))
						{
							int ay = x - paddingLeft;
							int ax = y - paddingTop;

							// Unity expects normalized height values
							float h = heightValues[ay + ax * mapWidth] - minHeight;
							float f = h / heightRange;

							// Flip for right-hand to left-handed coordinate system
							int ix = x;
							int iy = heightMapResolution - (y + 1);

							// Unity expects height array indexing to be [y, x].
							unityHeights[ix, iy] = f;
						}
					}
				}

				terrainData.baseMapResolution = heightMapResolution;
				terrainData.alphamapResolution = heightMapResolution;

				//int detailResolution = heightMapResolution;
				// 128 is the maximum for resolutionPerPatch
				const int resolutionPerPatch = 128;
				terrainData.SetDetailResolution(resolutionPerPatch, resolutionPerPatch);

				// Note SetHeights must be called before setting size in next line, as otherwise
				// the internal terrain size will not change after setting the size.
				terrainData.SetHeights(0, 0, unityHeights);

				terrainData.size = new Vector3(terrainSizeX, heightRange, terrainSizeY);

				terrain.Flush();

				// Unity Terrain has origin at bottom left, whereas Houdini uses centre of terrain. 

				// Use volume bounds to set position offset when using split tiles
				float xmin, xmax, zmin, zmax, ymin, ymax, xcenter, ycenter, zcenter;
				session.GetVolumeBounds(geoID, partID, out xmin, out ymin, out zmin, out xmax, out ymax, out zmax, out xcenter,
					out ycenter, out zcenter);
				//Debug.LogFormat("xmin: {0}, xmax: {1}, ymin: {2}, ymax: {3}, zmin: {4}, zmax: {5}, xc: {6}, yc: {7}, zc: {8}",
				//	xmin, xmax, ymin, ymax, zmin, zmax, xcenter, ycenter, zcenter);

				// Offset position is based on size of heightfield
				float offsetX = (float)heightMapResolution / (float)mapWidth;
				float offsetZ = (float)heightMapResolution / (float)mapHeight;
				//Debug.LogFormat("offsetX: {0}, offsetZ: {1}", offsetX, offsetZ);

				//Debug.LogFormat("position.x: {0}, position.z: {1}", position.x, position.z);

				//volumePositionOffset = new Vector3(-position.x * offsetX, minHeight + position.y, position.z * offsetZ);
				volumePositionOffset = new Vector3((terrainSizeX + xmin) * offsetX, minHeight + position.y, zmin * offsetZ);

				return true;
			}
			else
			{
				Debug.LogWarning("Non-heightfield volume type not supported!");
			}

			return false;
		}

		/// <summary>
		/// Generates a cube mesh using quad faces from given points, with vertex colours on selected and non-selected points.
		/// </summary>
		/// <param name="points">A cube will be created for each point in this list</param>
		/// <param name="selectedPtsFlag">Indices of selected points</param>
		/// <param name="defaultColor">Non-selected vertex color of cubes</param>
		/// <param name="selectedColor">Selected vertex color of cubes</param>
		/// <param name="size">Length of one side of cube</param>
		/// <returns>The generated mesh</returns>
		public static Mesh GenerateCubeMeshFromPoints(Vector3[] points, Color[] pointsColor, float size = 1f)
		{
			float halfSize = size * 0.5f;

			int totalPoints = points.Length;

			// Each cube face will get unique vertices due to splitting the normals
			int totalVertices = 24 * totalPoints;

			Vector3[] vertices = new Vector3[totalVertices];
			Color[] colors = new Color[totalVertices];
			Vector3[] normals = new Vector3[totalVertices];
			int[] indices = new int[totalVertices];

			int ptIndex = 0;
			int vertsPerPt = 24;

			foreach (Vector3 pt in points)
			{
				Vector3 v0 = new Vector3(pt.x - halfSize, pt.y + halfSize, pt.z + halfSize);
				Vector3 v1 = new Vector3(pt.x - halfSize, pt.y + halfSize, pt.z - halfSize);
				Vector3 v2 = new Vector3(pt.x + halfSize, pt.y + halfSize, pt.z - halfSize);
				Vector3 v3 = new Vector3(pt.x + halfSize, pt.y + halfSize, pt.z + halfSize);

				Vector3 v4 = new Vector3(pt.x - halfSize, pt.y - halfSize, pt.z + halfSize);
				Vector3 v5 = new Vector3(pt.x - halfSize, pt.y - halfSize, pt.z - halfSize);
				Vector3 v6 = new Vector3(pt.x + halfSize, pt.y - halfSize, pt.z - halfSize);
				Vector3 v7 = new Vector3(pt.x + halfSize, pt.y - halfSize, pt.z + halfSize);

				int vertIndex = ptIndex * vertsPerPt;

				// Top
				vertices[vertIndex + 0] = v0;
				vertices[vertIndex + 1] = v3;
				vertices[vertIndex + 2] = v2;
				vertices[vertIndex + 3] = v1;

				normals[vertIndex + 0] = Vector3.up;
				normals[vertIndex + 1] = Vector3.up;
				normals[vertIndex + 2] = Vector3.up;
				normals[vertIndex + 3] = Vector3.up;

				// Bottom
				vertices[vertIndex + 4] = v4;
				vertices[vertIndex + 5] = v5;
				vertices[vertIndex + 6] = v6;
				vertices[vertIndex + 7] = v7;

				normals[vertIndex + 4] = Vector3.down;
				normals[vertIndex + 5] = Vector3.down;
				normals[vertIndex + 6] = Vector3.down;
				normals[vertIndex + 7] = Vector3.down;

				// Front
				vertices[vertIndex + 8] = v0;
				vertices[vertIndex + 9] = v4;
				vertices[vertIndex + 10] = v7;
				vertices[vertIndex + 11] = v3;

				normals[vertIndex + 8] = Vector3.forward;
				normals[vertIndex + 9] = Vector3.forward;
				normals[vertIndex + 10] = Vector3.forward;
				normals[vertIndex + 11] = Vector3.forward;

				// Back
				vertices[vertIndex + 12] = v1;
				vertices[vertIndex + 13] = v2;
				vertices[vertIndex + 14] = v6;
				vertices[vertIndex + 15] = v5;

				normals[vertIndex + 12] = Vector3.back;
				normals[vertIndex + 13] = Vector3.back;
				normals[vertIndex + 14] = Vector3.back;
				normals[vertIndex + 15] = Vector3.back;

				// Left
				vertices[vertIndex + 16] = v0;
				vertices[vertIndex + 17] = v1;
				vertices[vertIndex + 18] = v5;
				vertices[vertIndex + 19] = v4;

				normals[vertIndex + 16] = Vector3.left;
				normals[vertIndex + 17] = Vector3.left;
				normals[vertIndex + 18] = Vector3.left;
				normals[vertIndex + 19] = Vector3.left;

				// Right
				vertices[vertIndex + 20] = v2;
				vertices[vertIndex + 21] = v3;
				vertices[vertIndex + 22] = v7;
				vertices[vertIndex + 23] = v6;

				normals[vertIndex + 20] = Vector3.right;
				normals[vertIndex + 21] = Vector3.right;
				normals[vertIndex + 22] = Vector3.right;
				normals[vertIndex + 23] = Vector3.right;

				// Vertex colors
				for (int i = 0; i < vertsPerPt; ++i)
				{
					colors[ptIndex * vertsPerPt + i] = pointsColor[ptIndex];
				}

				// Indices
				for (int i = 0; i < vertsPerPt; ++i)
				{
					indices[ptIndex * vertsPerPt + i] = ptIndex * vertsPerPt + i;
				}

				ptIndex++;
			}

			Mesh mesh = new Mesh();

			if (indices.Length > ushort.MaxValue)
			{
#if UNITY_2017_3_OR_NEWER
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#else
				Debug.LogErrorFormat("Unable to generate mesh from points due to larger than supported geometry (> {0} vertices). Use Unity 2017.3+ for large geometry.", ushort.MaxValue);
				return mesh;
#endif
			}

			mesh.vertices = vertices;
			mesh.colors = colors;
			mesh.normals = normals;
			mesh.SetIndices(indices, MeshTopology.Quads, 0);

			return mesh;
		}
	}


}   // HoudiniEngineUnity