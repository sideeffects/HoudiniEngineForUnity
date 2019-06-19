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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace HoudiniEngineUnity
{
	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Typedefs (copy these from HEU_Common.cs)
	using HAPI_NodeId = System.Int32;
	using HAPI_PartId = System.Int32;
	using HAPI_StringHandle = System.Int32;

	/// <summary>
	/// Represents a volume-based terrain layer
	/// </summary>
	[System.Serializable]
	public class HEU_VolumeLayer
	{
		public string _layerName;
		public HEU_PartData _part;
		public float _strength = 1.0f;
		public bool _uiExpanded;
		public int _tile = 0;
	}


	/// <summary>
	/// Creates terrain out of volume parts.
	/// </summary>
	public class HEU_VolumeCache : ScriptableObject
	{
		//	DATA ------------------------------------------------------------------------------------------------------

		[SerializeField]
		private HEU_GeoNode _ownerNode;

		[SerializeField]
		private List<HEU_VolumeLayer> _layers = new List<HEU_VolumeLayer>();

		// Used for storing in use layers during update. This is temporary and does not need to be serialized.
		private List<HEU_VolumeLayer> _updatedLayers;

		[SerializeField]
		private int _tileIndex;

		[SerializeField]
		private bool _isDirty;

		public bool IsDirty { get { return _isDirty; } set { _isDirty = value; } }

		[SerializeField]
		private string _geoName;

		[SerializeField]
		private string _objName;

		public int TileIndex { get { return _tileIndex; } }

		public string ObjectName { get { return _objName; } }

		public string GeoName { get { return _geoName; } }

		public bool _uiExpanded = true;

		public bool UIExpanded { get { return _uiExpanded; } set { _uiExpanded = value; } }

		// Hold a reference to the TerrainData so that it can be serialized/deserialized when using presets (Rebuild/duplicate)
		[SerializeField]
		private TerrainData _terrainData;


		//	LOGIC -----------------------------------------------------------------------------------------------------

		public static List<HEU_VolumeCache> UpdateVolumeCachesFromParts(HEU_SessionBase session, HEU_GeoNode ownerNode, List<HEU_PartData> volumeParts, List<HEU_VolumeCache> volumeCaches)
		{
			HEU_HoudiniAsset parentAsset = ownerNode.ParentAsset;

			foreach (HEU_VolumeCache cache in volumeCaches)
			{
				// Remove current volume caches from parent asset.
				// These get added back in below.
				parentAsset.RemoveVolumeCache(cache);

				// Mark the cache for updating
				cache.StartUpdateLayers();
			}

			// This will keep track of volume caches still in use
			List<HEU_VolumeCache> updatedCaches = new List<HEU_VolumeCache>();

			int numParts = volumeParts.Count;
			for (int i = 0; i < numParts; ++i)
			{
				// Get the tile index, if it exists, for this part
				HAPI_AttributeInfo tileAttrInfo = new HAPI_AttributeInfo();
				int[] tileAttrData = new int[0];
				HEU_GeneralUtility.GetAttribute(session, ownerNode.GeoID, volumeParts[i].PartID, HEU_Defines.HAPI_HEIGHTFIELD_TILE_ATTR, ref tileAttrInfo, ref tileAttrData, session.GetAttributeIntData);
				if (tileAttrData != null && tileAttrData.Length > 0)
				{
					//Debug.LogFormat("Tile: {0}", tileAttrData[0]);

					int tile = tileAttrData[0];
					HEU_VolumeCache volumeCache = null;

					// Find cache in updated list
					for (int j = 0; j < updatedCaches.Count; ++j)
					{
						if (updatedCaches[j] != null && updatedCaches[j].TileIndex == tile)
						{
							volumeCache = updatedCaches[j];
							break;
						}
					}

					if (volumeCache != null)
					{
						volumeCache.UpdateLayerFromPart(session, volumeParts[i]);

						// Skip adding new cache since already found in updated list
						continue;
					}

					// Find existing cache in old list
					if (volumeCaches != null && volumeCaches.Count > 0)
					{
						for(int j = 0; j < volumeCaches.Count; ++j)
						{
							if (volumeCaches[j] != null && volumeCaches[j].TileIndex == tile)
							{
								volumeCache = volumeCaches[j];
								break;
							}
						}
					}

					// Create new cache for this tile if not found
					if (volumeCache == null)
					{
						volumeCache = ScriptableObject.CreateInstance<HEU_VolumeCache>();
						volumeCache.Initialize(ownerNode, tile);
						volumeCache.StartUpdateLayers();
					}

					volumeCache.UpdateLayerFromPart(session, volumeParts[i]);

					if (!updatedCaches.Contains(volumeCache))
					{
						updatedCaches.Add(volumeCache);
					}
				}
				else
				{
					// No tile index. Most likely a single terrain tile.

					HEU_VolumeCache volumeCache = null;

					if (updatedCaches.Count == 0)
					{
						// Create a single volume cache, or use existing if it was just 1.
						// If more than 1 volume cache exists, this will recreate a single one

						if (volumeCaches == null || volumeCaches.Count != 1)
						{
							volumeCache = ScriptableObject.CreateInstance<HEU_VolumeCache>();
							volumeCache.Initialize(ownerNode, 0);
							volumeCache.StartUpdateLayers();
						}
						else if (volumeCaches.Count == 1)
						{
							// Keep the single volumecache
							volumeCache = volumeCaches[0];
						}

						if (!updatedCaches.Contains(volumeCache))
						{
							updatedCaches.Add(volumeCache);
						}
					}
					else
					{
						// Reuse the updated cache
						volumeCache = updatedCaches[0];
					}

					volumeCache.UpdateLayerFromPart(session, volumeParts[i]);
				}
			}

			foreach (HEU_VolumeCache cache in updatedCaches)
			{
				// Add to parent for UI and preset
				parentAsset.AddVolumeCache(cache);

				// Finish update by keeping just the layers in use for each volume cache.
				cache.FinishUpdateLayers();
			}

			return updatedCaches;
		}

		public void Initialize(HEU_GeoNode ownerNode, int tileIndex)
		{
			_ownerNode = ownerNode;
			_geoName = ownerNode.GeoName;
			_objName = ownerNode.ObjectNode.ObjectName;
			_tileIndex = tileIndex;
			_terrainData = null;
		}

		public void ResetParameters()
		{
			_terrainData = null;

			HEU_VolumeLayer defaultLayer = new HEU_VolumeLayer();

			foreach (HEU_VolumeLayer layer in _layers)
			{
				CopyLayer(defaultLayer, layer);
			}
		}

		public HEU_VolumeLayer GetLayer(string layerName)
		{
			foreach(HEU_VolumeLayer layer in _layers)
			{
				if(layer._layerName.Equals(layerName))
				{
					return layer;
				}
			}
			return null;
		}

		public void StartUpdateLayers()
		{
			_updatedLayers = new List<HEU_VolumeLayer>(_layers);
		}

		public void FinishUpdateLayers()
		{
			_layers = _updatedLayers;
			_updatedLayers = null;
		}

		private void GetPartLayerAttributes(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, HEU_VolumeLayer layer)
		{
			// Get the tile index, if it exists, for this part
			HAPI_AttributeInfo tileAttrInfo = new HAPI_AttributeInfo();
			int[] tileAttrData = new int[0];
			HEU_GeneralUtility.GetAttribute(session, geoID, partID, HEU_Defines.HAPI_HEIGHTFIELD_TILE_ATTR, ref tileAttrInfo, ref tileAttrData, session.GetAttributeIntData);
			if (tileAttrData != null && tileAttrData.Length > 0)
			{
				layer._tile = tileAttrData[0];
				//Debug.LogFormat("Tile: {0}", tileAttrData[0]);
			}
			else
			{
				layer._tile = 0;
			}
		}

		private bool LoadLayerTextureFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, out Texture2D outTexture)
		{
			outTexture = null;
			// The texture path is stored as string primitive attribute. Only 1 string path per layer.
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			string[] texturePath = HEU_GeneralUtility.GetAttributeStringData(session, geoID, partID, attrName, ref attrInfo);
			if (texturePath != null && texturePath.Length > 0 && !string.IsNullOrEmpty(texturePath[0]))
			{
				outTexture = LoadAssetTexture(texturePath[0]);
			}
			return outTexture != null;
		}

		private bool LoadLayerFloatFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref float floatValue)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			float[] attrValues = new float[0];
			HEU_GeneralUtility.GetAttribute(session, geoID, partID, attrName, ref attrInfo, ref attrValues, session.GetAttributeFloatData);
			if (attrValues != null && attrValues.Length > 0)
			{
				floatValue = attrValues[0];
				return true;
			}
			return false;
		}

		private bool LoadLayerColorFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref Color colorValue)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			float[] attrValues = new float[0];
			HEU_GeneralUtility.GetAttribute(session, geoID, partID, attrName, ref attrInfo, ref attrValues, session.GetAttributeFloatData);
			if (attrValues != null && attrValues.Length >= 3 && attrInfo.tupleSize >= 3)
			{
				colorValue[0] = attrValues[0];
				colorValue[1] = attrValues[1];
				colorValue[2] = attrValues[2];

				if (attrInfo.tupleSize == 4 && attrValues.Length == 4)
				{
					colorValue[3] = attrValues[3];
				}
				else
				{
					colorValue[3] = 1f;
				}
				return true;
			}
			return false;
		}

		private bool LoadLayerVector2FromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref Vector2 vectorValue)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			float[] attrValues = new float[0];
			HEU_GeneralUtility.GetAttribute(session, geoID, partID, attrName, ref attrInfo, ref attrValues, session.GetAttributeFloatData);
			if (attrValues != null && attrValues.Length == 2)
			{
				if (attrInfo.tupleSize == 2)
				{
					vectorValue[0] = attrValues[0];
					vectorValue[1] = attrValues[1];
					return true;
				}
			}
			return false;
		}

		public void UpdateLayerFromPart(HEU_SessionBase session, HEU_PartData part)
		{
			HEU_GeoNode geoNode = part.ParentGeoNode;

			HAPI_VolumeInfo volumeInfo = new HAPI_VolumeInfo();
			bool bResult = session.GetVolumeInfo(geoNode.GeoID, part.PartID, ref volumeInfo);
			if (!bResult || volumeInfo.tupleSize != 1 || volumeInfo.zLength != 1 || volumeInfo.storage != HAPI_StorageType.HAPI_STORAGETYPE_FLOAT)
			{
				return;
			}

			string volumeName = HEU_SessionManager.GetString(volumeInfo.nameSH, session);
			part.SetVolumeLayerName(volumeName);

			//Debug.LogFormat("Part name: {0}, GeoName: {1}, Volume Name: {2}, Display: {3}", part.PartName, geoNode.GeoName, volumeName, geoNode.Displayable);

			bool bHeightPart = volumeName.Equals(HEU_Defines.HAPI_HEIGHTFIELD_LAYERNAME_HEIGHT);

			HEU_VolumeLayer layer = GetLayer(volumeName);
			if (layer == null)
			{
				layer = new HEU_VolumeLayer();
				layer._layerName = volumeName;

				if (bHeightPart)
				{
					_layers.Insert(0, layer);
				}
				else
				{
					_layers.Add(layer);
				}
			}

			layer._part = part;

			GetPartLayerAttributes(session, geoNode.GeoID, part.PartID, layer);

			if (!bHeightPart)
			{
				part.DestroyAllData();
			}

			if (!_updatedLayers.Contains(layer))
			{
				if (bHeightPart)
				{
					_updatedLayers.Insert(0, layer);
				}
				else
				{
					_updatedLayers.Add(layer);
				}
			}
		}

		public void GenerateTerrainWithAlphamaps(HEU_SessionBase session, HEU_HoudiniAsset houdiniAsset)
		{
			if(_layers == null || _layers.Count == 0)
			{
				Debug.LogError("Unable to generate terrain due to lack of heightfield layers!");
				return;
			}

			HEU_VolumeLayer baseLayer = _layers[0];

			HAPI_VolumeInfo baseVolumeInfo = new HAPI_VolumeInfo();
			bool bResult = session.GetVolumeInfo(_ownerNode.GeoID, baseLayer._part.PartID, ref baseVolumeInfo);
			if (!bResult)
			{
				Debug.LogErrorFormat("Unable to get volume info for layer {0}!", baseLayer._layerName);
				return;
			}

			// Special handling of volume cache presets. It is applied here (if exists) because it might pertain to TerrainData that exists
			// in the AssetDatabase. If we don't apply here but rather create a new one, the existing file will get overwritten.
			// Applying the preset here for terrain ensures the TerrainData is reused.
			// Get the volume preset for this part
			HEU_VolumeCachePreset volumeCachePreset = houdiniAsset.GetVolumeCachePreset(_ownerNode.ObjectNode.ObjectName, _ownerNode.GeoName, TileIndex);
			if (volumeCachePreset != null)
			{
				ApplyPreset(volumeCachePreset);

				// Remove it so that it doesn't get applied when doing the recook step
				houdiniAsset.RemoveVolumeCachePreset(volumeCachePreset);
			}

			// The TerrainData and TerrainLayer files needs to be saved out if we create them. This creates the relative folder
			// path from the Asset's cache folder: {assetCache}/{geo name}/Terrain/Tile{tileIndex}/...
			string relativeFolderPath = HEU_Platform.BuildPath(_ownerNode.GeoName, HEU_Defines.HEU_FOLDER_TERRAIN, HEU_Defines.HEU_FOLDER_TILE + TileIndex);

			//Debug.Log("Generating Terrain with AlphaMaps: " + (_terrainData != null ? _terrainData.name : "NONE"));
			TerrainData terrainData = _terrainData;
			Vector3 terrainOffsetPosition = Vector3.zero;

			// Look up TerrainData file via attribute if user has set it
			string terrainDataFile = HEU_GeneralUtility.GetAttributeStringValueSingle(session, _ownerNode.GeoID, baseLayer._part.PartID,
				HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TERRAINDATA_FILE_ATTR, HAPI_AttributeOwner.HAPI_ATTROWNER_PRIM);
			if (!string.IsNullOrEmpty(terrainDataFile))
			{
				TerrainData loadedTerrainData = HEU_AssetDatabase.LoadAssetAtPath(terrainDataFile, typeof(TerrainData)) as TerrainData;
				if (loadedTerrainData == null)
				{
					Debug.LogWarningFormat("TerrainData, set via attribute, not found at: {0}", terrainDataFile);
				}
				else
				{
					// In the case that the specified TerrainData belongs to another Terrain (i.e. input Terrain), 
					// make a copy of it and store it in our cache. Note that this overwrites existing TerrainData in our cache
					// because the workflow is such that attributes will always override local setting.
					string bakedTerrainPath = houdiniAsset.GetValidAssetCacheFolderPath();
					bakedTerrainPath = HEU_Platform.BuildPath(bakedTerrainPath, relativeFolderPath);
					terrainData = HEU_AssetDatabase.CopyAndLoadAssetAtAnyPath(loadedTerrainData, bakedTerrainPath, typeof(TerrainData), true) as TerrainData;
					if (terrainData == null)
					{
						Debug.LogErrorFormat("Unable to copy TerrainData from {0} for generating Terrain.", terrainDataFile);
					}
				}
			}

			// Generate the terrain and terrain data from the heightmap's height layer
			bResult = HEU_GeometryUtility.GenerateTerrainFromVolume(session, ref baseVolumeInfo, baseLayer._part.ParentGeoNode.GeoID,
				baseLayer._part.PartID, baseLayer._part.OutputGameObject, ref terrainData, out terrainOffsetPosition);
			if (!bResult || terrainData == null)
			{
				return;
			}

			if (_terrainData != terrainData)
			{
				_terrainData = terrainData;
				baseLayer._part.SetTerrainData(terrainData, relativeFolderPath);
			}

			baseLayer._part.SetTerrainOffsetPosition(terrainOffsetPosition);

			int terrainSize = terrainData.heightmapResolution;

			// Now set the alphamaps (textures with masks) for the other layers

			// First, preprocess all layers to get heightfield arrays, converted to proper size
			// Then, merge into a float[x,y,map]
			List<float[]> heightFields = new List<float[]>();
			List<HEU_VolumeLayer> validLayers = new List<HEU_VolumeLayer>();

			int numLayers = _layers.Count;
			for(int i = 1; i < numLayers; ++i)
			{
				float[] hf = HEU_GeometryUtility.GetHeightfieldFromPart(session, _ownerNode.GeoID, _layers[i]._part.PartID, _layers[i]._part.PartName, terrainSize);
				if (hf != null && hf.Length > 0)
				{
					heightFields.Add(hf);
					validLayers.Add(_layers[i]);
				}
			}

			// Total maps = all HF layers + base height layer
			int numMaps = heightFields.Count + 1;

			// Assign floats to alpha map
			float[,,] alphamap = new float[terrainSize, terrainSize, numMaps];
			for (int y = 0; y < terrainSize; ++y)
			{
				for (int x = 0; x < terrainSize; ++x)
				{
					float f = 0f;
					for (int m = numMaps - 1; m > 0; --m)
					{
						float a = heightFields[m - 1][y + terrainSize * x];
						a = Mathf.Clamp01(a) * validLayers[m - 1]._strength;
						alphamap[x, y, m] = a;

						f += a;
					}

					// Base layer gets leftover value
					alphamap[x, y, 0] = Mathf.Clamp01(1.0f - f) * baseLayer._strength;
				}
			}

			HAPI_NodeId geoID;
			HAPI_PartId partID;

			Texture2D defaultTexture = LoadDefaultSplatTexture();

#if UNITY_2018_3_OR_NEWER
			// Create or update the terrain layers based on heightfield layers.
			TerrainLayer[] previousLayers = terrainData.terrainLayers;
			TerrainLayer[] terrainLayers = new TerrainLayer[numMaps];
			bool bRequiresSave = false;
			TerrainLayer terrainLayerToUpdate = null;
			for (int m = 0; m < numMaps; ++m)
			{
				bRequiresSave = false;
				terrainLayerToUpdate = null;

				HEU_VolumeLayer layer = (m == 0) ? baseLayer : validLayers[m - 1];

				geoID = _ownerNode.GeoID;
				partID = layer._part.PartID;

				// Ideally want to reuse existing TerrainLayer unless it doesn't exist or provided.
				
				// Search existing layers by layer name to reuse it so that we can keep user changes
				if (previousLayers != null)
				{
					terrainLayerToUpdate = GetTerrainLayerByName(layer._layerName, previousLayers);
					//Debug.LogFormat("Found and reusing existing terrain layer {0}: {1}", layer._layerName, (terrainLayerToUpdate != null));

					if (terrainLayerToUpdate != null)
					{
						// Make copy of the TerrainLayer into this asset's cache
						string bakedTerrainPath = houdiniAsset.GetValidAssetCacheFolderPath();
						bakedTerrainPath = HEU_Platform.BuildPath(bakedTerrainPath, relativeFolderPath);
						terrainLayerToUpdate = HEU_AssetDatabase.CopyAndLoadAssetAtAnyPath(terrainLayerToUpdate, bakedTerrainPath, typeof(TerrainLayer), true) as TerrainLayer;
						if (terrainLayerToUpdate == null)
						{
							Debug.LogErrorFormat("Unable to copy TerrainLayer '{0}' for generating Terrain.", layer._layerName);
							continue;
						}
					}
				}

				if (terrainLayerToUpdate == null)
				{
					terrainLayerToUpdate = new TerrainLayer();
					terrainLayerToUpdate.name = layer._layerName;
					//Debug.LogFormat("Created new TerrainLayer with name: {0} ", terrainLayerToUpdate.name);
					bRequiresSave = true;
				}

				// Now override layer properties if they have been set via attributes

				Texture2D diffuseTexture = null;
				if (LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_DIFFUSE_ATTR, out diffuseTexture))
				{
					terrainLayerToUpdate.diffuseTexture = diffuseTexture;
				}

				if (terrainLayerToUpdate.diffuseTexture == null && bRequiresSave)
				{
					// Applying default texture if this layer was created newly and no texture was specified.
					// Unity always seems to require a default texture when creating a new layer normally.
					terrainLayerToUpdate.diffuseTexture = defaultTexture;
				}

				Texture2D maskTexture = null;
				if (LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_MASK_ATTR, out maskTexture))
				{
					terrainLayerToUpdate.maskMapTexture = maskTexture;
				}

				Texture2D normalTexture = null;
				if (LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_NORMAL_ATTR, out normalTexture))
				{
					terrainLayerToUpdate.normalMapTexture = normalTexture;
				}

				float normalScale = 0f;
				if (LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_NORMAL_SCALE_ATTR, ref normalScale))
				{
					terrainLayerToUpdate.normalScale = normalScale;
				}

				float metallic = 0f;
				if (LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_METALLIC_ATTR, ref metallic))
				{
					terrainLayerToUpdate.metallic = metallic;
				}

				float smoothness = 0f;
				if (LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SMOOTHNESS_ATTR, ref smoothness))
				{
					terrainLayerToUpdate.smoothness = smoothness;
				}

				Color specularColor = new Color();
				if (LoadLayerColorFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SPECULAR_ATTR, ref specularColor))
				{
					terrainLayerToUpdate.specular = specularColor;
				}

				Vector2 tileOffset = new Vector2();
				if (LoadLayerVector2FromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_OFFSET_ATTR, ref tileOffset))
				{
					terrainLayerToUpdate.tileOffset = tileOffset;
				}

				Vector2 tileSize = new Vector2();
				if (LoadLayerVector2FromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_SIZE_ATTR, ref tileSize))
				{
					terrainLayerToUpdate.tileSize = tileSize;
				}

				if (terrainLayerToUpdate.tileSize.magnitude == 0f)
				{
					// Use texture size if tile size is 0
					terrainLayerToUpdate.tileSize = new Vector2(terrainLayerToUpdate.diffuseTexture.width, terrainLayerToUpdate.diffuseTexture.height);
				}

				if (bRequiresSave)
				{
					// In order to retain the TerrainLayer, it must be saved to the AssetDatabase.
					Object savedObject = null;
					string layerFileNameWithExt = terrainLayerToUpdate.name;
					if (!layerFileNameWithExt.EndsWith(HEU_Defines.HEU_EXT_TERRAINLAYER))
					{
						layerFileNameWithExt += HEU_Defines.HEU_EXT_TERRAINLAYER;
					}
					houdiniAsset.AddToAssetDBCache(layerFileNameWithExt, terrainLayerToUpdate, relativeFolderPath, ref savedObject);
				}

				terrainLayers[m] = terrainLayerToUpdate;
			}

			terrainData.terrainLayers = terrainLayers;

			terrainData.SetAlphamaps(0, 0, alphamap);

			// If the layers were writen out, this saves the asset DB. Otherwise user has to save it themselves.
			// Not 100% sure this is needed, but without this the editor doesn't know the terrain asset has been updated
			// and therefore doesn't import and show the terrain layer.
			HEU_AssetDatabase.SaveAssetDatabase();
#else
			// Need to create or reuse SplatPrototype for each layer in heightfield, representing the textures.
			SplatPrototype[] previousSplats = terrainData.splatPrototypes;
			SplatPrototype[] splatPrototypes = new SplatPrototype[numMaps];
			SplatPrototype splatPrototypeToUpdate = null;
			for (int m = 0; m < numMaps; ++m)
			{
				HEU_VolumeLayer layer = (m == 0) ? baseLayer : validLayers[m - 1];

				geoID = _ownerNode.GeoID;
				partID = layer._part.PartID;

				if (previousSplats != null && m < previousSplats.Length)
				{
					splatPrototypeToUpdate = previousSplats[m];
				}
				
				if (splatPrototypeToUpdate == null)
				{
					splatPrototypeToUpdate = new SplatPrototype();
				}

				// Now override splat properties if they have been set via attributes

				Texture2D diffuseTexture = null;
				if (LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_DIFFUSE_ATTR, out diffuseTexture))
				{
					splatPrototypeToUpdate.texture = diffuseTexture;
				}

				if (splatPrototypeToUpdate.texture == null)
				{
					splatPrototypeToUpdate.texture = defaultTexture;
				}

				Texture2D normalTexture = null;
				if (LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_NORMAL_ATTR, out normalTexture))
				{
					splatPrototypeToUpdate.normalMap = normalTexture;
				}

				float metallic = 0f;
				if (LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_METALLIC_ATTR, ref metallic))
				{
					splatPrototypeToUpdate.metallic = metallic;
				}

				float smoothness = 0f;
				if (LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SMOOTHNESS_ATTR, ref smoothness))
				{
					splatPrototypeToUpdate.smoothness = smoothness;
				}

				Color specularColor = new Color();
				if (LoadLayerColorFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SPECULAR_ATTR, ref specularColor))
				{
					splatPrototypeToUpdate.specular = specularColor;
				}

				Vector2 tileOffset = new Vector2();
				if (LoadLayerVector2FromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_OFFSET_ATTR, ref tileOffset))
				{
					splatPrototypeToUpdate.tileOffset = tileOffset;
				}

				Vector2 tileSize = new Vector2();
				if (LoadLayerVector2FromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_SIZE_ATTR, ref tileSize))
				{
					splatPrototypeToUpdate.tileSize = tileSize;
				}

				if (splatPrototypeToUpdate.tileSize.magnitude == 0f)
				{
					// Use texture size if tile size is 0
					splatPrototypeToUpdate.tileSize = new Vector2(splatPrototypeToUpdate.texture.width, splatPrototypeToUpdate.texture.height);
				}

				splatPrototypes[m] = splatPrototypeToUpdate;
			}
			terrainData.splatPrototypes = splatPrototypes;

			terrainData.SetAlphamaps(0, 0, alphamap);
#endif
		}

		public void PopulatePreset(HEU_VolumeCachePreset cachePreset)
		{
			cachePreset._objName = ObjectName;
			cachePreset._geoName = GeoName;
			cachePreset._uiExpanded = UIExpanded;
			cachePreset._tile = TileIndex;

			if (_terrainData != null)
			{
				cachePreset._terrainDataPath = HEU_AssetDatabase.GetAssetPath(_terrainData);
			}
			else
			{
				cachePreset._terrainDataPath = "";
			}
			//Debug.Log("Set terraindata path: " + cachePreset._terrainDataPath);

			foreach (HEU_VolumeLayer layer in _layers)
			{
				HEU_VolumeLayerPreset layerPreset = new HEU_VolumeLayerPreset();

				layerPreset._layerName = layer._layerName;
				layerPreset._strength = layer._strength;
				layerPreset._uiExpanded = layer._uiExpanded;
				layerPreset._tile = layer._tile;

				cachePreset._volumeLayersPresets.Add(layerPreset);
			}
		}

		public bool ApplyPreset(HEU_VolumeCachePreset volumeCachePreset)
		{
			UIExpanded = volumeCachePreset._uiExpanded;

			// Load the TerrainData if the path is given
			//Debug.Log("Get terraindata path: " + volumeCachePreset._terrainDataPath);
			if (!string.IsNullOrEmpty(volumeCachePreset._terrainDataPath))
			{
				_terrainData = HEU_AssetDatabase.LoadAssetAtPath(volumeCachePreset._terrainDataPath, typeof(TerrainData)) as TerrainData;
				//Debug.Log("Loaded terrain? " + (_terrainData != null ? "yes" : "no"));
			}

			foreach (HEU_VolumeLayerPreset layerPreset in volumeCachePreset._volumeLayersPresets)
			{
				HEU_VolumeLayer layer = GetLayer(layerPreset._layerName);
				if (layer == null)
				{
					Debug.LogWarningFormat("Volume layer with name {0} not found! Unable to set heightfield layer preset.", layerPreset._layerName);
					return false;
				}

				layer._strength = layerPreset._strength;
				layer._tile = layerPreset._tile;
				layer._uiExpanded = layerPreset._uiExpanded;
			}
			
			IsDirty = true;

			return true;
		}

		public void CopyValuesTo(HEU_VolumeCache destCache)
		{
			destCache.UIExpanded = UIExpanded;

			destCache._terrainData = Object.Instantiate(_terrainData);

			foreach (HEU_VolumeLayer srcLayer in _layers)
			{
				HEU_VolumeLayer destLayer = destCache.GetLayer(srcLayer._layerName);
				if(destLayer != null)
				{
					CopyLayer(srcLayer, destLayer);
				}
			}
		}

		public static void CopyLayer(HEU_VolumeLayer srcLayer, HEU_VolumeLayer destLayer)
		{
			destLayer._strength = srcLayer._strength;
			destLayer._uiExpanded = srcLayer._uiExpanded;
			destLayer._tile = srcLayer._tile;
		}

		public static Texture2D LoadDefaultSplatTexture()
		{
			Texture2D texture = LoadAssetTexture(HEU_PluginSettings.TerrainSplatTextureDefault);
			if (texture == null)
			{
				texture = HEU_MaterialFactory.WhiteTexture();
			}
			return texture;
		}

		public static Texture2D LoadAssetTexture(string path)
		{
			Texture2D texture = HEU_MaterialFactory.LoadTexture(path);
			if (texture == null)
			{
				Debug.LogErrorFormat("Unable to find the default Terrain texture at {0}. Make sure this default texture exists.", path);
			}
			return texture;
		}

#if UNITY_2018_3_OR_NEWER
		private static TerrainLayer GetTerrainLayerByName(string layerName, TerrainLayer[] terrainLayers)
		{
			string layerFileName = layerName;
			string layerFileNameWithSpaces = layerName.Replace('_', ' ');
			foreach (TerrainLayer layer in terrainLayers)
			{
				if (layer != null && layer.name != null && (layer.name.Equals(layerFileName) || layer.name.Equals(layerFileNameWithSpaces)))
				{
					return layer;
				}
			}
			return null;
		}
#endif
	}

}   // HoudiniEngineUnity