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

		[FormerlySerializedAs("_splatTexture")]
		public Texture2D _diffuseTexture;

		public Texture2D _maskTexture;
		public float _metallic = 0f;
		public Texture2D _normalTexture;
		public float _normalScale = 0.5f;
		public float _smoothness = 0f;
		public Color _specularColor = Color.gray;
		public Vector2 _tileSize = Vector2.zero;
		public Vector2 _tileOffset = Vector2.zero;

		public bool _uiExpanded;
		public int _tile = -1;

		// Flags to denote whether the above layer properties had been overriden by user
		public enum Overrides
		{
			None		= 0,
			Diffuse		= 1,
			Mask		= 2,
			Metallic	= 4,
			Normal		= 8,
			NormalScale	= 16,
			Smoothness	= 32,
			Specular	= 64,
			TileSize	= 128,
			TileOffset	= 256
		}

		public Overrides _overrides = Overrides.None;
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

		[SerializeField]
		private bool _isDirty;

		public bool IsDirty { get { return _isDirty; } set { _isDirty = value; } }

		[SerializeField]
		private string _geoName;

		[SerializeField]
		private string _objName;

		public string ObjectName { get { return _objName; } }

		public string GeoName { get { return _geoName; } }

		public bool _uiExpanded = true;

		public bool UIExpanded { get { return _uiExpanded; } set { _uiExpanded = value; } }


		//	LOGIC -----------------------------------------------------------------------------------------------------

		public void Initialize(HEU_GeoNode ownerNode)
		{
			_ownerNode = ownerNode;
			_geoName = ownerNode.GeoName;
			_objName = ownerNode.ObjectNode.ObjectName;
		}

		public void ResetParameters()
		{
			HEU_VolumeLayer defaultLayer = new HEU_VolumeLayer();

			foreach (HEU_VolumeLayer layer in _layers)
			{
				CopyLayer(defaultLayer, layer);
			}
		}

		public void GenerateFromParts(HEU_SessionBase session, HEU_HoudiniAsset houdiniAsset, List<HEU_PartData> volumeParts)
		{
			UpdateVolumeLayers(session, houdiniAsset, volumeParts);

			GenerateTerrainWithAlphamaps(session, houdiniAsset);
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

		private void GetPartLayerAttributes(HEU_SessionBase session, HEU_HoudiniAsset houdiniAsset, HAPI_NodeId geoID, HAPI_NodeId partID, HEU_VolumeLayer layer)
		{
			// Get the tile index, if it exists, for this part
			HAPI_AttributeInfo tileAttrInfo = new HAPI_AttributeInfo();
			int[] tileAttrData = new int[0];
			HEU_GeneralUtility.GetAttribute(session, geoID, partID, "tile", ref tileAttrInfo, ref tileAttrData, session.GetAttributeIntData);
			if (tileAttrData != null && tileAttrData.Length > 0)
			{
				layer._tile = tileAttrData[0];
				//Debug.LogFormat("Tile: {0}", tileAttrData[0]);
			}
			else
			{
				layer._tile = -1;
			}

			// Get the layer textures, and other layer values from attributes

			Texture2D defaultTexture = LoadDefaultSplatTexture();

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.Diffuse) && (layer._diffuseTexture == null || layer._diffuseTexture == defaultTexture))
			{
				layer._diffuseTexture = LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_DIFFUSE_ATTR);

				if (layer._diffuseTexture == null)
				{
					layer._diffuseTexture = defaultTexture;
				}
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.Mask) && layer._maskTexture == null)
			{
				layer._maskTexture = LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_MASK_ATTR);
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.Normal) && layer._normalTexture == null)
			{
				layer._normalTexture = LoadLayerTextureFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_NORMAL_ATTR);
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.NormalScale))
			{
				LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_NORMAL_SCALE_ATTR, ref layer._normalScale);
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.Metallic))
			{
				LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_METALLIC_ATTR, ref layer._metallic);
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.Smoothness))
			{
				LoadLayerFloatFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SMOOTHNESS_ATTR, ref layer._smoothness);
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.Specular))
			{
				LoadLayerColorFromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SPECULAR_ATTR, ref layer._specularColor);
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.TileOffset))
			{
				LoadLayerVector2FromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_OFFSET_ATTR, ref layer._tileOffset);
			}

			if (!IsLayerFieldOverriden(layer, HEU_VolumeLayer.Overrides.TileSize))
			{
				LoadLayerVector2FromAttribute(session, geoID, partID, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_SIZE_ATTR, ref layer._tileSize);
			}
		}

		private Texture2D LoadLayerTextureFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName)
		{
			// The texture path is stored as string primitive attribute. Only 1 string path per layer.
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			string[] texturePath = HEU_GeneralUtility.GetAttributeStringData(session, geoID, partID, attrName, ref attrInfo);
			if (texturePath != null && texturePath.Length > 0 && !string.IsNullOrEmpty(texturePath[0]))
			{
				return LoadAssetTexture(texturePath[0]);
			}
			return null;
		}

		private void LoadLayerFloatFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref float floatValue)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			float[] attrValues = new float[0];
			HEU_GeneralUtility.GetAttribute(session, geoID, partID, attrName, ref attrInfo, ref attrValues, session.GetAttributeFloatData);
			if (attrValues != null && attrValues.Length > 0)
			{
				floatValue = attrValues[0];
			}
		}

		private void LoadLayerColorFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref Color colorValue)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			float[] attrValues = new float[0];
			HEU_GeneralUtility.GetAttribute(session, geoID, partID, attrName, ref attrInfo, ref attrValues, session.GetAttributeFloatData);
			if (attrValues != null && attrValues.Length >= 3)
			{
				if (attrInfo.tupleSize >= 3)
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
				}
			}
		}

		private void LoadLayerVector2FromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref Vector2 vectorValue)
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
				}
			}
		}

		private void UpdateVolumeLayers(HEU_SessionBase session, HEU_HoudiniAsset houdiniAsset, List<HEU_PartData> volumeParts)
		{
			bool bResult;
			foreach (HEU_PartData part in volumeParts)
			{
				HEU_GeoNode geoNode = part.ParentGeoNode;

				HAPI_VolumeInfo volumeInfo = new HAPI_VolumeInfo();
				bResult = session.GetVolumeInfo(geoNode.GeoID, part.PartID, ref volumeInfo);
				if (!bResult || volumeInfo.tupleSize != 1 || volumeInfo.zLength != 1 || volumeInfo.storage != HAPI_StorageType.HAPI_STORAGETYPE_FLOAT)
				{
					continue;
				}

				string volumeName = HEU_SessionManager.GetString(volumeInfo.nameSH, session);
				part.SetVolumeLayerName(volumeName);

				//Debug.LogFormat("Part name: {0}, GeoName: {1}, Volume Name: {2}, Display: {3}", part.PartName, geoNode.GeoName, volumeName, geoNode.Displayable);

				bool bHeightPart = volumeName.Equals("height");

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

				GetPartLayerAttributes(session, houdiniAsset, geoNode.GeoID, part.PartID, layer);

				if (!bHeightPart)
				{
					part.DestroyAllData();
				}
			}
		}


		private void GenerateTerrainWithAlphamaps(HEU_SessionBase session, HEU_HoudiniAsset houdiniAsset)
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

			TerrainData terrainData = null;
			Vector3 terrainOffsetPosition = Vector3.zero;

			// Generate the terrain and terrain data from the heightmap's height layer
			bResult = HEU_GeometryUtility.GenerateTerrainFromVolume(session, ref baseVolumeInfo, baseLayer._part.ParentGeoNode.GeoID,
				baseLayer._part.PartID, baseLayer._part.OutputGameObject, out terrainData, out terrainOffsetPosition);
			if (!bResult)
			{
				return;
			}

			baseLayer._part.SetTerrainData(terrainData);
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
				float[] hf = GetHeightfield(session, _ownerNode.GeoID, _layers[i]._part.PartID, _layers[i]._part.PartName, terrainSize);
				if (hf != null && hf.Length > 0)
				{
					heightFields.Add(hf);
					validLayers.Add(_layers[i]);
				}
			}

			// Total maps is masks plus base height layer
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
						a = Mathf.Clamp01(a - f) * validLayers[m - 1]._strength;
						alphamap[x, y, m] = a;

						f += a;
					}

					// Base layer gets leftover value
					alphamap[x, y, 0] = Mathf.Clamp01(1.0f - f) * baseLayer._strength;
				}
			}

#if UNITY_2018_3_OR_NEWER

			// Create TerrainLayer for each heightfield layer
			// Note that at time of this implementation the new Unity terrain
			// is still in beta. Therefore, the following layer creation is subject
			// to change.

			TerrainLayer[] terrainLayers = new TerrainLayer[numMaps];
			for (int m = 0; m < numMaps; ++m)
			{
				terrainLayers[m] = new TerrainLayer();

				HEU_VolumeLayer layer = (m == 0) ? baseLayer : validLayers[m - 1];

				terrainLayers[m].diffuseTexture = layer._diffuseTexture;
				terrainLayers[m].diffuseRemapMin = Vector4.zero;
				terrainLayers[m].diffuseRemapMax = Vector4.one;

				terrainLayers[m].maskMapTexture = layer._maskTexture;
				terrainLayers[m].maskMapRemapMin = Vector4.zero;
				terrainLayers[m].maskMapRemapMax = Vector4.one;

				terrainLayers[m].metallic = layer._metallic;

				terrainLayers[m].normalMapTexture = layer._normalTexture;
				terrainLayers[m].normalScale = layer._normalScale;

				terrainLayers[m].smoothness = layer._smoothness;
				terrainLayers[m].specular = layer._specularColor;
				terrainLayers[m].tileOffset = layer._tileOffset;

				if (layer._tileSize.magnitude == 0f)
				{
					// Use texture size if tile size is 0
					layer._tileSize = new Vector3(layer._diffuseTexture.width, layer._diffuseTexture.height);
				}
				terrainLayers[m].tileSize = layer._tileSize;
			}
			terrainData.terrainLayers = terrainLayers;

#else
			// Need to create SplatPrototype for each layer in heightfield, representing the textures.
			SplatPrototype[] splatPrototypes = new SplatPrototype[numMaps];
			for (int m = 0; m < numMaps; ++m)
			{
				splatPrototypes[m] = new SplatPrototype();

				HEU_VolumeLayer layer = (m == 0) ? baseLayer : validLayers[m - 1];

				splatPrototypes[m].texture = layer._diffuseTexture;
				splatPrototypes[m].tileOffset = layer._tileOffset;
				if(layer._tileSize.magnitude == 0f)
				{
					// Use texture size if tile size is 0
					layer._tileSize = new Vector3(layer._diffuseTexture.width, layer._diffuseTexture.height);
				}
				splatPrototypes[m].tileSize = layer._tileSize;

				splatPrototypes[m].metallic = layer._metallic;
				splatPrototypes[m].smoothness = layer._smoothness;
				splatPrototypes[m].normalMap = layer._normalTexture;
			}
			terrainData.splatPrototypes = splatPrototypes;
#endif

			terrainData.SetAlphamaps(0, 0, alphamap);
		}

		private float[] GetHeightfield(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, string partName, int terrainSize)
		{
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
			bResult = HEU_GeneralUtility.GetArray2Arg(geoID, partID, session.GetHeightFieldData, heightValues, 0, totalHeightValues);
			if (!bResult)
			{
				Debug.LogErrorFormat("Unable to get heightfield data from part {0}", partName);
				return null;
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

			float heightRange = (maxHeight - minHeight);
			if(heightRange == 0f)
			{
				heightRange = 1f;
			}
			//Debug.LogFormat("{0} : {1}", HEU_SessionManager.GetString(volumeInfo.nameSH, session), heightRange);

			// Remap height values to fit terrain size
			int paddingWidth = terrainSize - volumeXLength;
			int paddingLeft = Mathf.CeilToInt(paddingWidth * 0.5f);
			int paddingRight = terrainSize - paddingLeft;
			//Debug.LogFormat("Padding: Width={0}, Left={1}, Right={2}", paddingWidth, paddingLeft, paddingRight);

			int paddingHeight = terrainSize - volumeYLength;
			int paddingTop = Mathf.CeilToInt(paddingHeight * 0.5f);
			int paddingBottom = terrainSize - paddingTop;
			//Debug.LogFormat("Padding: Height={0}, Top={1}, Bottom={2}", paddingHeight, paddingTop, paddingBottom);

			// Set height values at centre of the terrain, with padding on the sides if we resized
			float[] resizedHeightValues = new float[terrainSize * terrainSize];
			for (int y = 0; y < terrainSize; ++y)
			{
				for (int x = 0; x < terrainSize; ++x)
				{
					if (y >= paddingTop && y < (paddingBottom) && x >= paddingLeft && x < (paddingRight))
					{
						int ay = x - paddingLeft;
						int ax = y - paddingTop;

						float f = heightValues[ay + ax * volumeXLength] - minHeight;
						f /= heightRange;

						// Flip for right-hand to left-handed coordinate system
						int ix = x;
						int iy = terrainSize - (y + 1);

						// Unity expects height array indexing to be [y, x].
						resizedHeightValues[iy + ix * terrainSize] = f;
					}
				}
			}

			return resizedHeightValues;
		}

		public void PopulatePreset(HEU_VolumeCachePreset cachePreset)
		{
			cachePreset._objName = ObjectName;
			cachePreset._geoName = GeoName;
			cachePreset._uiExpanded = UIExpanded;

			foreach (HEU_VolumeLayer layer in _layers)
			{
				HEU_VolumeLayerPreset layerPreset = new HEU_VolumeLayerPreset();

				layerPreset._layerName = layer._layerName;
				layerPreset._strength = layer._strength;

				if(layer._diffuseTexture != null)
				{
					layerPreset._diffuseTexturePath = HEU_AssetDatabase.GetAssetPath(layer._diffuseTexture);
				}

				if (layer._maskTexture != null)
				{
					layerPreset._maskTexturePath = HEU_AssetDatabase.GetAssetPath(layer._maskTexture);
				}

				layerPreset._metallic = layer._metallic;

				if (layer._normalTexture != null)
				{
					layerPreset._normalTexturePath = HEU_AssetDatabase.GetAssetPath(layer._normalTexture);
				}

				layerPreset._normalScale = layer._normalScale;
				layerPreset._smoothness = layer._smoothness;
				layerPreset._specularColor = layer._specularColor;

				layerPreset._tileSize = layer._tileSize;
				layerPreset._tileOffset = layer._tileOffset;

				layerPreset._uiExpanded = layer._uiExpanded;
				layerPreset._tile = layer._tile;

				layerPreset._overrides = layer._overrides;

				cachePreset._volumeLayersPresets.Add(layerPreset);
			}
		}

		public void CopyValuesTo(HEU_VolumeCache destCache)
		{
			destCache.UIExpanded = UIExpanded;

			foreach(HEU_VolumeLayer srcLayer in _layers)
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

			destLayer._diffuseTexture = srcLayer._diffuseTexture;
			destLayer._maskTexture = srcLayer._maskTexture;

			destLayer._metallic = srcLayer._metallic;

			destLayer._normalTexture = srcLayer._normalTexture;
			destLayer._normalScale = srcLayer._normalScale;
			destLayer._smoothness = srcLayer._smoothness;
			destLayer._specularColor = srcLayer._specularColor;

			destLayer._tileSize = srcLayer._tileSize;
			destLayer._tileOffset = srcLayer._tileOffset;

			destLayer._uiExpanded = srcLayer._uiExpanded;
			destLayer._tile = srcLayer._tile;

			destLayer._overrides = srcLayer._overrides;
		}

		public static Texture2D LoadDefaultSplatTexture()
		{
			return LoadAssetTexture(HEU_PluginSettings.TerrainSplatTextureDefault);
		}

		public static Texture2D LoadAssetTexture(string path)
		{
			Texture2D texture = HEU_MaterialFactory.LoadTexture(path);
			if (texture == null)
			{
				Debug.LogErrorFormat("Unable to find the default Terrain texture at {0}. Make sure this default texture exists. Using default white texture instead.", path);
				texture = HEU_MaterialFactory.WhiteTexture();
			}
			return texture;
		}

		public static bool IsLayerFieldOverriden(HEU_VolumeLayer layer, HEU_VolumeLayer.Overrides field)
		{
			return (layer._overrides & field) == field;
		}

		public static HEU_VolumeLayer.Overrides SetLayerFieldOverride(HEU_VolumeLayer.Overrides setOverride, HEU_VolumeLayer.Overrides field)
		{
			return setOverride | field;
		}
	}

}   // HoudiniEngineUnity