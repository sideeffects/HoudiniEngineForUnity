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

namespace HoudiniEngineUnity
{
	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Typedefs (copy these from HEU_Common.cs)
	using HAPI_NodeId = System.Int32;
	using HAPI_PartId = System.Int32;

	[System.Serializable]
	public class HEU_VolumeLayer
	{
		public string _layerName;
		public HEU_PartData _part;
		public Texture2D _splatTexture;
		public Texture2D _normalTexture;
		public float _strength = 1.0f;
		public Vector2 _tileSize = Vector2.zero;
		public Vector2 _tileOffset = Vector2.zero;
		public float _metallic = 0f;
		public float _smoothness = 0f;
		public bool _uiExpanded;
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
			foreach(HEU_VolumeLayer layer in _layers)
			{
				layer._splatTexture = LoadDefaultSplatTexture();
				layer._normalTexture = null;
				layer._strength = 1.0f;
				layer._tileSize = Vector2.zero;
				layer._tileOffset = Vector2.zero;
				layer._metallic = 0f;
				layer._smoothness = 0f;
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

					layer._splatTexture = LoadDefaultSplatTexture();

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

			// Need to create SplatPrototype for each layer in heightfield, representing the textures.
			SplatPrototype[] splatPrototypes = new SplatPrototype[numMaps];
			for (int m = 0; m < numMaps; ++m)
			{
				splatPrototypes[m] = new SplatPrototype();

				HEU_VolumeLayer layer = (m == 0) ? baseLayer : validLayers[m - 1];

				splatPrototypes[m].texture = layer._splatTexture;
				splatPrototypes[m].tileOffset = layer._tileOffset;
				if(layer._tileSize.magnitude == 0f)
				{
					// Use texture size if tile size is 0
					layer._tileSize = new Vector3(layer._splatTexture.width, layer._splatTexture.height);
				}
				splatPrototypes[m].tileSize = layer._tileSize;

				splatPrototypes[m].metallic = layer._metallic;
				splatPrototypes[m].smoothness = layer._smoothness;
				splatPrototypes[m].normalMap = layer._normalTexture;
			}

			terrainData.splatPrototypes = splatPrototypes;
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

				if(layer._splatTexture != null)
				{
					layerPreset._splatTexturePath = HEU_AssetDatabase.GetAssetPath(layer._splatTexture);
				}

				if (layer._normalTexture != null)
				{
					layerPreset._normalTexturePath = HEU_AssetDatabase.GetAssetPath(layer._normalTexture);
				}

				layerPreset._tileSize = layer._tileSize;
				layerPreset._tileOffset = layer._tileOffset;
				layerPreset._metallic = layer._metallic;
				layerPreset._smoothness = layer._smoothness;
				layerPreset._uiExpanded = layer._uiExpanded;

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
					destLayer._strength = srcLayer._strength;
					destLayer._splatTexture = srcLayer._splatTexture;
					destLayer._normalTexture = srcLayer._normalTexture;
					destLayer._tileSize = srcLayer._tileSize;
					destLayer._tileOffset = srcLayer._tileOffset;
					destLayer._metallic = srcLayer._metallic;
					destLayer._smoothness = srcLayer._smoothness;
					destLayer._uiExpanded = srcLayer._uiExpanded;
				}
			}
		}

		public static Texture2D LoadDefaultSplatTexture()
		{
			string defaultSplatTexturePath = HEU_PluginSettings.TerrainSplatTextureDefault;
			Texture2D texture = HEU_MaterialFactory.LoadTexture(defaultSplatTexturePath);
			if (texture == null)
			{
				Debug.LogErrorFormat("Unable to find the default Terrain texture at {0}. Make sure this default texture exists. Using default white texture instead.", defaultSplatTexturePath);
				texture = HEU_MaterialFactory.WhiteTexture();
			}
			return texture;
		}
	}

}   // HoudiniEngineUnity