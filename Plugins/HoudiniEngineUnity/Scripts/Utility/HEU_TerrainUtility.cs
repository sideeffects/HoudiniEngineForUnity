/*
* Copyright (c) <2019> Side Effects Software Inc.
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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HoudiniEngineUnity
{
	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Typedefs (copy these from HEU_Common.cs)
	using HAPI_NodeId = System.Int32;
	using HAPI_PartId = System.Int32;

	public static class HEU_TerrainUtility
	{
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
				else if (terrainResizedDelta > 0)
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
				float[] normalizedHeights = GetNormalizedHeightmapFromPartWithMinMax(session, geoID, partID, 
					volumeInfo.xLength, volumeInfo.yLength, ref minHeight, ref maxHeight, ref heightRange);
				float[,] unityHeights = ConvertHeightMapHoudiniToUnity(heightMapResolution, heightMapResolution, normalizedHeights);

				// The terrainData.baseMapResolution is not set here, but rather left to whatever default Unity uses
				// The terrainData.alphamapResolution is set later when setting the alphamaps.

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
		public static float[] GetNormalizedHeightmapFromPartWithMinMax(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, 
			int heightMapWidth, int heightMapHeight, ref float minHeight, ref float maxHeight, ref float heightRange)
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
				// Always use a non-zero height range, otherwise user can't paint height on Terrain.
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
			int paddingWidth = heightMapWidth - volumeXLength;
			int paddingLeft = Mathf.CeilToInt(paddingWidth * 0.5f);
			int paddingRight = heightMapWidth - paddingLeft;
			//Debug.LogFormat("Padding: Width={0}, Left={1}, Right={2}", paddingWidth, paddingLeft, paddingRight);

			int paddingHeight = heightMapHeight - volumeYLength;
			int paddingTop = Mathf.CeilToInt(paddingHeight * 0.5f);
			int paddingBottom = heightMapHeight - paddingTop;
			//Debug.LogFormat("Padding: Height={0}, Top={1}, Bottom={2}", paddingHeight, paddingTop, paddingBottom);

			// Normalize the height values into the range between 0 and 1, inclusive.
			float inverseHeightRange = 1f / heightRange;
			float normalizeMinHeight = minHeight;
			if (minHeight >= 0f && minHeight <= 1f && maxHeight >= 0f && maxHeight <= 1f)
			{
				// Its important to leave the values alone if they are already normalized.
				// So these values don't actually do anything in the normalization calculation below.
				inverseHeightRange = 1f;
				normalizeMinHeight = 0f;
			}

			// Set height values at centre of the terrain, with padding on the sides if we resized
			float[] resizedHeightValues = new float[heightMapWidth * heightMapHeight];
			for (int y = 0; y < heightMapHeight; ++y)
			{
				for (int x = 0; x < heightMapWidth; ++x)
				{
					if (y >= paddingTop && y < (paddingBottom) && x >= paddingLeft && x < (paddingRight))
					{
						int ay = x - paddingLeft;
						int ax = y - paddingTop;

						float f = heightValues[ay + ax * volumeXLength] - normalizeMinHeight;
						f *= inverseHeightRange;

						// Flip for right-hand to left-handed coordinate system
						int ix = x;
						int iy = heightMapHeight - (y + 1);

						// Unity expects height array indexing to be [y, x].
						resizedHeightValues[iy + ix * heightMapWidth] = f;
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
		public static float[,] ConvertHeightMapHoudiniToUnity(int heightMapWidth, int heightMapHeight, float[] heightValues)
		{
			float[,] unityHeights = new float[heightMapWidth, heightMapHeight];
			for (int y = 0; y < heightMapHeight; ++y)
			{
				for (int x = 0; x < heightMapWidth; ++x)
				{
					float f = heightValues[y + x * heightMapWidth];
					unityHeights[x, y] = f;
				}
			}

			return unityHeights;
		}

		/// <summary>
		/// Converts the given heightfields (linear array) into a multi-dimensional array that Unity
		/// needs for uploading splatmap values for terrain.
		/// Assumes the values have already been converted to Unity coordinates, and normalized between 0 and 1.
		/// </summary>
		/// <param name="heightMapSize">Size of each dimension of the heightmap (assumes equal sides).</param>
		/// <param name="heightFields">List of height values that will be converted to alphamap.</param>
		/// <returns>Converted heightfields into Unity alphamap array.</returns>
		public static float[,,] ConvertHeightFieldToAlphaMap(int heightMapWidth, int heightMapHeight, List<float[]> heightFields)
		{
			int numMaps = heightFields.Count;

			// Assign height floats to alpha map, with strength applied.
			float[,,] alphamap = new float[heightMapWidth, heightMapHeight, numMaps];
			for (int y = 0; y < heightMapHeight; ++y)
			{
				for (int x = 0; x < heightMapWidth; ++x)
				{
					for (int m = numMaps - 1; m >= 0; --m)
					{
						float a = heightFields[m][y + heightMapWidth * x];
						a = Mathf.Clamp01(a);
						alphamap[x, y, m] = a;
					}
				}
			}

			return alphamap;
		}

		/// <summary>
		/// Returns a new alphamap for Unity terrain consisting of heightfield values that have
		/// already be converted to Unity format, with strengths multiplied.
		/// </summary>
		/// <param name="heightMapSize">Size of each dimension of the heightmap</param>
		/// <param name="existingAlphaMaps">Existing alphamaps to reuse (could be null)</param>
		/// <param name="heightFields">Converted heightfields to use for alphamaps</param>
		/// <param name="strengths">Strength values to multiply the alphamap by</param>
		/// <param name="alphaMapIndices">List of indices for each alphamap dictating whether to use existingAlphaMaps values or use heightFields values.
		/// Negative values signal existing indices, while positive (>0) values signal heightField indices. Indices are offset by -1, and +1, respectively.</param>
		/// <returns>Converted alphamap</returns>
		public static float[,,] AppendConvertedHeightFieldToAlphaMap(int heightMapWidth, int heightMapHeight, float[,,] existingAlphaMaps, List<float[]> heightFields, float[] strengths, List<int> alphaMapIndices)
		{
			// Assign height floats to alpha map, with strength applied.
			int numMaps = alphaMapIndices.Count;
			int index = 0;
			float[,,] alphaMap = new float[heightMapWidth, heightMapHeight, numMaps];
			for (int y = 0; y < heightMapHeight; ++y)
			{
				for (int x = 0; x < heightMapWidth; ++x)
				{
					for (int m = 0; m < numMaps; m++)
					{
						index = alphaMapIndices[m];

						if (index < 0)
						{
							// Use existing alphamap
							index = (index * -1) - 1;   // index is negative and off by -1
							alphaMap[x, y, m] = existingAlphaMaps[x, y, index];
						}
						else if (index > 0)
						{
							// Use heightfield
							index -= 1; // index is off by +1
							float a = heightFields[index][y + heightMapWidth * x];
							a = Mathf.Clamp01(a) * strengths[index];
							alphaMap[x, y, m] = a;
						}
					}
				}
			}

			return alphaMap;
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
		/// Returns list of TreePrototypes formed by querying data from given part.
		/// </summary>
		/// <param name="session">Houdini Engine session</param>
		/// <param name="geoID">Geometry object</param>
		/// <param name="partID">Part ID</param>
		/// <returns>Returns list of TreePrototypes or null if none found.</returns>
		public static List<TreePrototype> GetTreePrototypesFromPart(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID)
		{
			List<TreePrototype> treePrototypes = new List<TreePrototype>();

			// Each TreePrototype data is stored as a string attribute, under the 'HEU_Defines.HEIGHTFIELD_TREEPROTOTYPE + index'
			// name. So check and parse until no more valid attributes found.
			int index = 0;
			while (true)
			{
				// Does this attribute exist?
				string attrName = HEU_Defines.HEIGHTFIELD_TREEPROTOTYPE + index.ToString();
				if (!HEU_GeneralUtility.HasAttribute(session, geoID, partID, attrName, HAPI_AttributeOwner.HAPI_ATTROWNER_PRIM))
				{
					break;
				}

				index++;

				// Get the string value
				HAPI_AttributeInfo treeAttrInfo = new HAPI_AttributeInfo();
				string[] protoAttrString = HEU_GeneralUtility.GetAttributeStringData(session, geoID, partID,
					attrName, ref treeAttrInfo);
				if (protoAttrString == null || protoAttrString.Length == 0 || string.IsNullOrEmpty(protoAttrString[0]))
				{
					break;
				}

				// Parse the attribute string value:
				// Only expecting a single element here, comma-separated for the asset path and bend factor:
				// => asset_path,bend_factor
				float bend = 0f;
				GameObject prefabGO = null;
				string[] properties = protoAttrString[0].Split(',');
				if (properties.Length > 0 && !string.IsNullOrEmpty(properties[0]))
				{
					prefabGO = HEU_AssetDatabase.LoadAssetAtPath(properties[0], typeof(GameObject)) as GameObject;
					if (prefabGO != null)
					{
						bend = 0f;
						if (properties.Length >= 2)
						{
							float.TryParse(properties[1], out bend);
						}

						TreePrototype prototype = new TreePrototype();
						prototype.prefab = prefabGO;
						prototype.bendFactor = bend;
						treePrototypes.Add(prototype);

						Debug.LogFormat("Added Tree Prototype: {0} - {1}", properties[0], bend);
					}
				}
			}

			return treePrototypes.Count > 0 ? treePrototypes : null;
		}

		/// <summary>
		/// Grab the scatter data for the given part.
		/// This finds the properties of TreeInstances via attributes.
		/// </summary>
		/// <param name="session">Houdini session</param>
		/// <param name="geoID">Geometry ID</param>
		/// <param name="partID">Part (volume layer) ID</param>
		/// <param name="pointCount">Number of expected scatter points</param>
		public static void PopulateScatterInfo(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, int pointCount,
			ref HEU_VolumeScatterTrees scatterTrees)
		{
			// The HEU_VolumeScatterTrees might already have been created when the volumecache was queried.
			// The "height" layer might have had prototype data which is set in _scatterTrees.
			if (scatterTrees == null)
			{
				scatterTrees = new HEU_VolumeScatterTrees();
			}
			Debug.Log("Point count is : " + pointCount);

			// Get prototype indices. These indices refer to _scatterTrees._treePrototypes.
			HAPI_AttributeInfo indicesAttrInfo = new HAPI_AttributeInfo();
			int[] indices = new int[0];
			if (HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HEIGHTFIELD_TREEINSTANCE_PROTOTYPEINDEX, ref indicesAttrInfo, ref indices, session.GetAttributeIntData))
			{
				if (indices != null && indices.Length == pointCount)
				{
					scatterTrees._prototypeIndices = indices;
				}
				else
				{
					Debug.LogWarningFormat("Scatter instance index count for attribute {0} is not valid. Expected {1} but got {2}",
						HEU_Defines.HEIGHTFIELD_TREEINSTANCE_PROTOTYPEINDEX, pointCount, (indices != null ? indices.Length : 0));
				}
			}

			// Using the UVs as position of the instances, since they are properly mapped to the terrain tile.
			// Also getting other attributes for the TreeInstances, if they are set.
			HAPI_AttributeInfo uvAttrInfo = new HAPI_AttributeInfo();
			float[] uvs = new float[0];
			if (!HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HAPI_ATTRIB_UV, ref uvAttrInfo, ref uvs, session.GetAttributeFloatData))
			{
				Debug.LogWarning("UVs for scatter instances not found or valid.");
			}

			if (uvs != null && uvs.Length == (pointCount * uvAttrInfo.tupleSize))
			{
				// Get height scales
				HAPI_AttributeInfo heightAttrInfo = new HAPI_AttributeInfo();
				float[] heightscales = new float[0];
				HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HEIGHTFIELD_TREEINSTANCE_HEIGHTSCALE, ref heightAttrInfo, ref heightscales, session.GetAttributeFloatData);

				// Get width scales
				HAPI_AttributeInfo widthAttrInfo = new HAPI_AttributeInfo();
				float[] widthscales = new float[0];
				HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HEIGHTFIELD_TREEINSTANCE_WIDTHSCALE, ref widthAttrInfo, ref widthscales, session.GetAttributeFloatData);

				// Get orientation
				HAPI_AttributeInfo orientAttrInfo = new HAPI_AttributeInfo();
				float[] orients = new float[0];
				HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HAPI_ATTRIB_ORIENT, ref orientAttrInfo, ref orients, session.GetAttributeFloatData);

				// Get color
				HAPI_AttributeInfo colorAttrInfo = new HAPI_AttributeInfo();
				float[] colors = new float[0];
				HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HAPI_ATTRIB_COLOR, ref colorAttrInfo, ref colors, session.GetAttributeFloatData);

				// Get lightmap color
				HAPI_AttributeInfo lightmapColorAttrInfo = new HAPI_AttributeInfo();
				float[] lightmapColors = new float[0];
				HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HEIGHTFIELD_TREEINSTANCE_LIGHTMAPCOLOR, ref lightmapColorAttrInfo, ref lightmapColors, session.GetAttributeFloatData);

				scatterTrees._positions = new Vector3[pointCount];

				if (heightAttrInfo.exists && (heightscales.Length == pointCount))
				{
					scatterTrees._heightScales = heightscales;
				}

				if (widthAttrInfo.exists && (widthscales.Length == pointCount))
				{
					scatterTrees._widthScales = widthscales;
				}

				if (colorAttrInfo.exists && (colors.Length == colorAttrInfo.tupleSize * pointCount))
				{
					scatterTrees._colors = new Color32[pointCount];
				}

				if (lightmapColorAttrInfo.exists && (lightmapColors.Length == lightmapColorAttrInfo.tupleSize * pointCount))
				{
					scatterTrees._lightmapColors = new Color32[pointCount];
				}

				for (int i = 0; i < pointCount; ++i)
				{
					scatterTrees._positions[i] = new Vector3(1.0f - uvs[i * uvAttrInfo.tupleSize + 1],
																	 0,
																	 uvs[i * uvAttrInfo.tupleSize + 0]);

					if (scatterTrees._colors != null)
					{
						scatterTrees._colors[i] =
							new Color32((byte)(colors[i * colorAttrInfo.tupleSize + 0] * 255),
										(byte)(colors[i * colorAttrInfo.tupleSize + 1] * 255),
										(byte)(colors[i * colorAttrInfo.tupleSize + 2] * 255),
										(byte)(colors[i * colorAttrInfo.tupleSize + 3] * 255));
					}

					if (scatterTrees._lightmapColors != null)
					{
						scatterTrees._lightmapColors[i] =
							new Color32((byte)(lightmapColors[i * lightmapColorAttrInfo.tupleSize + 0] * 255),
										(byte)(lightmapColors[i * lightmapColorAttrInfo.tupleSize + 1] * 255),
										(byte)(lightmapColors[i * lightmapColorAttrInfo.tupleSize + 2] * 255),
										(byte)(lightmapColors[i * lightmapColorAttrInfo.tupleSize + 3] * 255));
					}
				}
			}
		}

		/// <summary>
		/// Apply the cached scatter prototypes and instances to the given TerrainData.
		/// </summary>
		public static void ApplyScatter(TerrainData terrainData, HEU_VolumeScatterTrees scatterTrees)
		{
#if UNITY_2018_3_OR_NEWER
			if (scatterTrees == null || scatterTrees._treePrototypes == null || scatterTrees._treePrototypes.Length == 0)
			{
				return;
			}

			terrainData.treePrototypes = scatterTrees._treePrototypes;
			terrainData.RefreshPrototypes();

			if (scatterTrees._positions != null && scatterTrees._positions.Length > 0
				&& scatterTrees._prototypeIndices != null && scatterTrees._prototypeIndices.Length == scatterTrees._positions.Length)
			{
				TreeInstance[] treeInstances = new TreeInstance[scatterTrees._positions.Length];

				for (int i = 0; i < scatterTrees._positions.Length; ++i)
				{
					treeInstances[i] = new TreeInstance();
					treeInstances[i].color = scatterTrees._colors != null ? scatterTrees._colors[i] : new Color32(255, 255, 255, 255);
					treeInstances[i].heightScale = scatterTrees._heightScales != null ? scatterTrees._heightScales[i] : 1f;
					treeInstances[i].lightmapColor = scatterTrees._lightmapColors != null ? scatterTrees._lightmapColors[i] : new Color32(255, 255, 255, 255);
					treeInstances[i].position = scatterTrees._positions[i];
					treeInstances[i].prototypeIndex = scatterTrees._prototypeIndices[i];
					treeInstances[i].widthScale = scatterTrees._widthScales != null ? scatterTrees._widthScales[i] : 1f;

					// Note that Unity's TreeInstance.rotation is read-only. 
					// So unable to use the orientation/rotation out of Houdini to set treeInstances[i].rotation.
				}

				terrainData.SetTreeInstances(treeInstances, true);
			}
#endif
		}

#if UNITY_2018_3_OR_NEWER
		private static int GetTerrainLayerIndexByName(string layerName, TerrainLayer[] terrainLayers)
		{
			string layerFileName = layerName;
			string layerFileNameWithSpaces = layerName.Replace('_', ' ');
			for (int i = 0; i < terrainLayers.Length; ++i)
			{
				if (terrainLayers[i] != null && terrainLayers[i].name != null
					&& (terrainLayers[i].name.Equals(layerFileName, System.StringComparison.CurrentCultureIgnoreCase)
					|| terrainLayers[i].name.Equals(layerFileNameWithSpaces, System.StringComparison.CurrentCultureIgnoreCase)))
				{
					return i;
				}
			}
			return -1;
		}

		public static int GetTerrainLayerIndex(TerrainLayer layer, TerrainLayer[] terrainLayers)
		{
			for (int i = 0; i < terrainLayers.Length; ++i)
			{
				if (layer == terrainLayers[i])
				{
					return i;
				}
			}
			return -1;
		}
#endif
	}

}   // HoudiniEngineUnity