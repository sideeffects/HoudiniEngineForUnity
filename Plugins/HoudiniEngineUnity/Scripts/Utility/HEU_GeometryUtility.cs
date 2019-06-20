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

		/// <summary>
		/// Creates terrain from given volumeInfo for the given gameObject.
		/// If gameObject has a valid Terrain component, then it is reused.
		/// Similarly, if the Terrain component has a valid TerrainData, or if the given terrainData is valid, then it is used.
		/// Otherwise a new TerrainData is created and set to the Terrain.
		/// Populates the volumePositionOffset with the heightfield offset position.
		/// Returns true if successfully created the terrain, otherwise false.
		/// </summary>
		/// <param name="session">Houdini Engine session to query heightfield data from</param>
		/// <param name="volumeInfo">Volume info pertaining to the heightfield to generate the Terrain from</param>
		/// <param name="geoID">The geometry ID</param>
		/// <param name="partID">The part ID (height layer)</param>
		/// <param name="gameObject">The target GameObject containing the Terrain component</param>
		/// <param name="terrainData">A valid TerrainData to use, or if empty, a new one is created and populated</param>
		/// <param name="volumePositionOffset">Heightfield offset</param>
		/// <returns>True if successfully popupated the terrain</returns>
		public static bool GenerateTerrainFromVolume(HEU_SessionBase session, ref HAPI_VolumeInfo volumeInfo, HAPI_NodeId geoID, HAPI_PartId partID, 
			GameObject gameObject, ref TerrainData terrainData, out Vector3 volumePositionOffset)
		{
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
				float terrainSizeX = Mathf.Round((volumeInfo.xLength - 1) * gridSpacingX);
				float terrainSizeY = Mathf.Round((volumeInfo.yLength - 1) * gridSpacingY);

				//Debug.LogFormat("GS = {0},{1},{2}. SX = {1}. SY = {2}", gridSpacingX, gridSpacingY, terrainSizeX, terrainSizeY);

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

				bool bNewTerrain = false;
				bool bNewTerrainData = false;
				Terrain terrain = gameObject.GetComponent<Terrain>();
				if (terrain == null)
				{
					terrain = gameObject.AddComponent<Terrain>();
					bNewTerrain = true;
				}

				TerrainCollider collider = HEU_GeneralUtility.GetOrCreateComponent<TerrainCollider>(gameObject);
				
				// This ensures to reuse existing terraindata, and only creates new if none exist or none provided
				if (terrain.terrainData == null)
				{
					if (terrainData == null)
					{
						terrainData = new TerrainData();
						bNewTerrainData = true;
					}

					terrain.terrainData = terrainData;
					SetTerrainMaterial(terrain);
				}

				terrainData = terrain.terrainData;
				collider.terrainData = terrainData;

				if (bNewTerrain)
				{
#if UNITY_2018_3_OR_NEWER
					terrain.allowAutoConnect = true;
					// This has to be set after setting material
					terrain.drawInstanced = true;
#endif
				}

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
					Debug.LogErrorFormat("Unsupported terrain size. Use terrain size of power of two plus 1, and grid spacing of 2. Given size is {0} but Unity resized it to {1}.", heightMapResolution, terrainData.heightmapResolution);
					return false;
				}

				int mapWidth = volumeInfo.xLength;
				int mapHeight = volumeInfo.yLength;

				// Get the converted height values from Houdini and find the min and max height range.
				float minHeight = 0;
				float maxHeight = 0;
				float heightRange = 0;
				float[] normalizedHeights = GetNormalizedHeightmapFromPartWithMinMax(session, geoID, partID, volumeInfo.xLength,
					ref minHeight, ref maxHeight, ref heightRange);
				float[,] unityHeights = ConvertHeightMapHoudiniToUnity(heightMapResolution, normalizedHeights);

				terrainData.baseMapResolution = heightMapResolution;
				terrainData.alphamapResolution = heightMapResolution;

				if (bNewTerrainData)
				{
					// 32 is the default for resolutionPerPatch
					const int detailResolution = 1024;
					const int resolutionPerPatch = 32;
					terrainData.SetDetailResolution(detailResolution, resolutionPerPatch);
				}

				// Note SetHeights must be called before setting size in next line, as otherwise
				// the internal terrain size will not change after setting the size.
				terrainData.SetHeights(0, 0, unityHeights);

				// Note that Unity uses a default height range of 600 when a flat terrain is created.
				// Without a non-zero value for the height range, user isn't able to draw heights.
				// Therefore, set 600 as the value if height range is currently 0 (due to flat heightfield).
				if (heightRange == 0)
				{
					heightRange = terrainData.size.y > 1 ? terrainData.size.y : 600;
				}

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
		/// Sets a material on the given Terrain object.
		/// Currently sets the default Terrain material from the plugin settings, if its valid.
		/// </summary>
		/// <param name="terrain">The terrain to set material for</param>
		public static void SetTerrainMaterial(Terrain terrain)
		{
			// Use material specified in Plugin settings.
			string terrainMaterialPath = HEU_PluginSettings.DefaultTerrainMaterial;
			if (!string.IsNullOrEmpty(terrainMaterialPath))
			{
				Material material = HEU_MaterialFactory.LoadUnityMaterial(terrainMaterialPath);
				if (material != null)
				{
					terrain.materialType = Terrain.MaterialType.Custom;
					terrain.materialTemplate = material;
				}
			}

			// TODO: If none specified, guess based on Render settings?
		}

		/// <summary>
		/// Retrieves the heightmap from Houdini for the given volume part, converts to Unity coordinates,
		/// normalizes to 0 and 1, along with min and max height values, as well as the range.
		/// </summary>
		/// <param name="session">Current Houdini session</param>
		/// <param name="geoID">Geometry object ID</param>
		/// <param name="partID">The volume part ID</param>
		/// <param name="heightMapSize">Size of each dimension of the heightmap (assumes equal sides).</param>
		/// <param name="minHeight">Found minimum height value in the heightmap.</param>
		/// <param name="maxHeight">Found maximum height value in the heightmap.</param>
		/// <param name="heightRange">Found height range in the heightmap.</param>
		/// <returns>The converted heightmap from Houdini.</returns>
		public static float[] GetNormalizedHeightmapFromPartWithMinMax(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, int heightMapSize,
			ref float minHeight, ref float maxHeight, ref float heightRange)
		{
			minHeight = float.MaxValue;
			maxHeight = float.MinValue;
			heightRange = 1;

			HAPI_VolumeInfo volumeInfo = new HAPI_VolumeInfo();
			bool bResult = session.GetVolumeInfo(geoID, partID, ref volumeInfo);
			if (!bResult)
			{
				return null;
			}

			int volumeXLength = volumeInfo.xLength;
			int volumeYLength = volumeInfo.yLength;

			// Number of heightfield values
			int totalHeightValues = volumeXLength * volumeYLength;

			float[] heightValues = new float[totalHeightValues];
			if (!GetHeightmapFromPart(session, volumeXLength, volumeYLength, geoID, partID, ref heightValues, ref minHeight, ref maxHeight))
			{
				return null;
			}

			heightRange = (maxHeight - minHeight);
			if (heightRange == 0f)
			{
				heightRange = 1f;
			}
			//Debug.LogFormat("{0} : {1}", HEU_SessionManager.GetString(volumeInfo.nameSH, session), heightRange);

			const int UNITY_MAX_HEIGHT_RANGE = 65536;
			if (Mathf.RoundToInt(heightRange) > UNITY_MAX_HEIGHT_RANGE)
			{
				Debug.LogWarningFormat("Unity Terrain has maximum height range of {0}. This HDA height range is {1}, so it will be maxed out at {0}.\nPlease resize to within valid range!",
					UNITY_MAX_HEIGHT_RANGE, Mathf.RoundToInt(heightRange));
				heightRange = UNITY_MAX_HEIGHT_RANGE;
			}

			// Remap height values to fit terrain size
			int paddingWidth = heightMapSize - volumeXLength;
			int paddingLeft = Mathf.CeilToInt(paddingWidth * 0.5f);
			int paddingRight = heightMapSize - paddingLeft;
			//Debug.LogFormat("Padding: Width={0}, Left={1}, Right={2}", paddingWidth, paddingLeft, paddingRight);

			int paddingHeight = heightMapSize - volumeYLength;
			int paddingTop = Mathf.CeilToInt(paddingHeight * 0.5f);
			int paddingBottom = heightMapSize - paddingTop;
			//Debug.LogFormat("Padding: Height={0}, Top={1}, Bottom={2}", paddingHeight, paddingTop, paddingBottom);

			// Set height values at centre of the terrain, with padding on the sides if we resized
			float[] resizedHeightValues = new float[heightMapSize * heightMapSize];
			for (int y = 0; y < heightMapSize; ++y)
			{
				for (int x = 0; x < heightMapSize; ++x)
				{
					if (y >= paddingTop && y < (paddingBottom) && x >= paddingLeft && x < (paddingRight))
					{
						int ay = x - paddingLeft;
						int ax = y - paddingTop;

						float f = heightValues[ay + ax * volumeXLength] - minHeight;
						f /= heightRange;

						// Flip for right-hand to left-handed coordinate system
						int ix = x;
						int iy = heightMapSize - (y + 1);

						// Unity expects height array indexing to be [y, x].
						resizedHeightValues[iy + ix * heightMapSize] = f;
					}
				}
			}

			return resizedHeightValues;
		}

		/// <summary>
		/// Retrieve the heightmap from Houdini for given part (volume), along with min and max height values.
		/// </summary>
		/// <param name="session">Current Houdini session.</param>
		/// <param name="xLength">Length of x dimension of the heightmap.</param>
		/// <param name="yLength">Length of y dimension of the heightmap.</param>
		/// <param name="geoID">Geometry object ID.</param>
		/// <param name="partID">The volume part ID.</param>
		/// <param name="heightValues">The raw heightmap from Houdini.</param>
		/// <param name="minHeight">Found minimum height value in the heightmap.</param>
		/// <param name="maxHeight">Found maximum height value in the heightmap.</param>
		/// <returns>The heightmap from Houdini.</returns>
		public static bool GetHeightmapFromPart(HEU_SessionBase session, int xLength, int yLength, HAPI_NodeId geoID, HAPI_PartId partID,
			ref float[] heightValues, ref float minHeight, ref float maxHeight)
		{
			// Get the height values from Houdini and find the min and max height range.
			int totalHeightValues = xLength * yLength;
			heightValues = new float[totalHeightValues];

			bool bResult = HEU_GeneralUtility.GetArray2Arg(geoID, partID, session.GetHeightFieldData, heightValues, 0, totalHeightValues);
			if (!bResult)
			{
				return false;
			}

			minHeight = heightValues[0];
			maxHeight = minHeight;
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

			return true;
		}

		/// <summary>
		/// Convert the given heightValues (linear array) into a multi-dimensional array that Unity
		/// needs for uploading as the heightmap for terrain.
		/// </summary>
		/// <param name="heightMapSize">Size of each dimension of the heightmap (assumes equal sides).</param>
		/// <param name="heightValues">List of height values that will be converted to the heightmap.</param>
		/// <returns>Converted heightmap for Unity terrain.</returns>
		public static float[,] ConvertHeightMapHoudiniToUnity(int heightMapSize, float[] heightValues)
		{
			float[,] unityHeights = new float[heightMapSize, heightMapSize];
			for (int y = 0; y < heightMapSize; ++y)
			{
				for (int x = 0; x < heightMapSize; ++x)
				{
					float f = heightValues[y + x * heightMapSize];
					unityHeights[x, y] = f;
				}
			}

			return unityHeights;
		}

		/// <summary>
		/// Converts the given heightfields (linear array) into a multi-dimensional array that Unity
		/// needs for uploading splatmap values for terrain. Each layer's values are clamped between 0 and 1,
		/// and multiplied by the given strengths array (at corresponding layer index).
		/// The base layer (located at alphamap[x,y,0]) is given the residual value after summing up the other layers
		/// and subtracting from 1. Therefore the sum of each layer at a particular index should sum up to 1.
		/// Converts from Houdini to Unity coordinate system conversion.
		/// </summary>
		/// <param name="heightMapSize">Size of each dimension of the heightmap (assumes equal sides).</param>
		/// <param name="heightFields">List of height values that will be converted to alphamap.</param>
		/// <param name="strengths">List of strength values to multiply corresponding layer in the array.</param>
		/// <returns>Converted heightfields into Unity alphamap array, multiplied by strength values.</returns>
		public static float[,,] ConvertHeightSplatMapHoudiniToUnity(int heightMapSize, List<float[]> heightFields, float[] strengths)
		{
			// Total maps is masks plus base height layer
			int numMaps = heightFields.Count + 1;

			// Assign height floats to alpha map, with strength applied.
			float[,,] alphamap = new float[heightMapSize, heightMapSize, numMaps];
			for (int y = 0; y < heightMapSize; ++y)
			{
				for (int x = 0; x < heightMapSize; ++x)
				{
					float f = 0f;
					for (int m = numMaps - 1; m > 0; --m)
					{
						float a = heightFields[m - 1][y + heightMapSize * x];
						a = Mathf.Clamp01(a) * strengths[m];
						alphamap[x, y, m] = a;

						f += a;
					}

					// Base layer gets leftover value
					alphamap[x, y, 0] = Mathf.Clamp01(1.0f - f) * strengths[0];
				}
			}

			return alphamap;
		}

		/// <summary>
		/// Get the volume position offset based on volume dimensions and bounds.
		/// </summary>
		public static Vector3 GetVolumePositionOffset(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, 
			Vector3 volumePosition, float terrainSizeX, float heightMapSize, int mapWidth, int mapHeight, float minHeight)
		{
			// Use volume bounds to set position offset when using split tiles
			float xmin, xmax, zmin, zmax, ymin, ymax, xcenter, ycenter, zcenter;
			session.GetVolumeBounds(geoID, partID, out xmin, out ymin, out zmin, out xmax, out ymax, out zmax, out xcenter,
				out ycenter, out zcenter);

			// Offset position is based on size of heightfield
			float offsetX = (float)heightMapSize / (float)mapWidth;
			float offsetZ = (float)heightMapSize / (float)mapHeight;

			return new Vector3((terrainSizeX + xmin) * offsetX, minHeight + volumePosition.y, zmin * offsetZ);
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

		/// <summary>
		/// Returns the output instance's name for given instance index. 
		/// The instance name convention is: PartName_Instance1
		/// User could override the prefix (PartName) with their own via given instancePrefixes array.
		/// </summary>
		/// <param name="partName"></param>
		/// <param name="userPrefix"></param>
		/// <param name="index"></param>
		/// <returns></returns>
		public static string GetInstanceOutputName(string partName, string[] userPrefix, int index)
		{
			string prefix = null;
			if (userPrefix == null || userPrefix.Length == 0)
			{
				prefix = partName;
			}
			else if (userPrefix.Length == 1)
			{
				prefix = userPrefix[0];
			}
			else if (index >= 0 && (index <= userPrefix.Length))
			{
				prefix = userPrefix[index - 1];
			}
			return prefix + HEU_Defines.HEU_INSTANCE + index;
		}
	}


}   // HoudiniEngineUnity