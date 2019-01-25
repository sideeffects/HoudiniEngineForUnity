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
	using HAPI_ParmId = System.Int32;

	/// <summary>
	/// Threaded class for loading bgeo files into Unity.
	/// The threaded work involves loading the bgeo into a Houdini Engine session, then retrieving the geometry
	/// into local buffers. 
	/// Finally, back in the main thread, the buffers are passed off to HEU_GeoSync to continue loading in Unity.
	/// </summary>
	public class HEU_ThreadedTaskLoadGeo : HEU_ThreadedTask
	{
		//	LOGIC ------------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Initial setup for loading the bgeo file.
		/// </summary>
		/// <param name="filePath">Path to the bgeo file</param>
		/// <param name="geoSync">HEU_GeoSync object that will do the actual Unity geometry creation</param>
		/// <param name="session">Houdini Engine session</param>
		/// <param name="fileNodeID">The file node's ID that was created in Houdini</param>
		public void Setup(string filePath, HEU_GeoSync geoSync, HEU_SessionBase session, HAPI_NodeId fileNodeID)
		{
			_filePath = filePath;
			_geoSync = geoSync;
			_session = session;
			_name = filePath;

			// Work data
			_loadData = new HEU_LoadData();
			_loadData._fileNodeID = fileNodeID;
			_loadData._loadStatus = HEU_LoadData.LoadStatus.NONE;
			_loadData._logStr = "";
		}

		/// <summary>
		/// Do the geometry loading in Houdini in a thread.
		/// Creates a file node, loads the bgeo, then retrives the geometry into local buffers.
		/// </summary>
		protected override void DoWork()
		{
			_loadData._loadStatus = HEU_LoadData.LoadStatus.STARTED;

			//Debug.LogFormat("DoWork: Loading {0}", _filePath);

			if (_session == null || !_session.IsSessionValid())
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, "Invalid session!");
				return;
			}

			// Check file path
			if (!HEU_Platform.DoesPathExist(_filePath))
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("File not found at {0}", _filePath));
				return;
			}

			// Make sure file type is supported
			if (!_filePath.EndsWith(".bgeo") && !_filePath.EndsWith(".bgeo.sc"))
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Only .bgeo or .bgeo.sc files are supported."));
				return;
			}

			// Create file SOP
			if (_loadData._fileNodeID == HEU_Defines.HEU_INVALID_NODE_ID)
			{
				if (!CreateFileNode(out _loadData._fileNodeID))
				{
					SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Unable to create file node in Houdini."));
					return;
				}
			}

			Sleep();

			HAPI_NodeId displayNodeID = GetDisplayNodeID(_loadData._fileNodeID);
			if (displayNodeID == HEU_Defines.HEU_INVALID_NODE_ID)
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Unable to get display node of file geo node."));
				return;
			}

			// Set the file parameter
			if (!SetFileParm(displayNodeID, _filePath))
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Unable to set file path parm."));
				return;
			}

			// Cooking it will load the bgeo
			if (!_session.CookNode(_loadData._fileNodeID, false))
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Unable to cook node."));
				return;
			}

			// Wait until cooking has finished
			bool bResult = true;
			HAPI_State statusCode = HAPI_State.HAPI_STATE_STARTING_LOAD;
			while (bResult && statusCode > HAPI_State.HAPI_STATE_MAX_READY_STATE)
			{
				bResult = _session.GetStatus(HAPI_StatusType.HAPI_STATUS_COOK_STATE, out statusCode);

				Sleep();
			}

			// Check cook results for any errors
			if (statusCode == HAPI_State.HAPI_STATE_READY_WITH_COOK_ERRORS || statusCode == HAPI_State.HAPI_STATE_READY_WITH_FATAL_ERRORS)
			{
				string statusString = _session.GetStatusString(HAPI_StatusType.HAPI_STATUS_COOK_RESULT, HAPI_StatusVerbosity.HAPI_STATUSVERBOSITY_ERRORS);
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Cook failed: {0}.", statusString));
				return;
			}			

			// Get parts
			List<HAPI_PartInfo> meshParts = new List<HAPI_PartInfo>();
			List<HAPI_PartInfo> volumeParts = new List<HAPI_PartInfo>();
			if (!QueryParts(displayNodeID, ref meshParts, ref volumeParts))
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Unable to query parts on node."));
				return;
			}

			Sleep();

			// Create Unity mesh buffers

			// Create Unity terrain buffers
			if (!GenerateTerrainBuffers(displayNodeID, volumeParts))
			{
				SetLog(HEU_LoadData.LoadStatus.ERROR, string.Format("Unable to generate terrain data from volume parts."));
				return;
			}

			SetLog(HEU_LoadData.LoadStatus.SUCCESS, "Completed!");
		}

		/// <summary>
		/// Once geometry buffers have been retrieved, load into Unity
		/// </summary>
		protected override void OnComplete()
		{
			//Debug.LogFormat("OnCompete: Loaded {0}", _filePath);

			if (_geoSync != null)
			{
				_geoSync.OnLoadComplete(_loadData);
			}
		}

		protected override void OnStopped()
		{
			//Debug.LogFormat("OnStopped: Loaded {0}", _filePath);

			if (_geoSync != null)
			{
				_geoSync.OnStopped(_loadData);
			}
		}

		protected override void CleanUp()
		{
			_loadData = null;

			base.CleanUp();
		}

		private void SetLog(HEU_LoadData.LoadStatus status, string logStr)
		{
			_loadData._loadStatus = status;
			_loadData._logStr = string.Format("{0} : {1}", _loadData._loadStatus.ToString(), logStr);
		}

		private bool CreateFileNode(out HAPI_NodeId fileNodeID)
		{
			fileNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

			if (!_session.CreateNode(-1, "SOP/file", "loadbgeo", true, out fileNodeID))
			{
				return false;
			}

			return true;
		}

		private HAPI_NodeId GetDisplayNodeID(HAPI_NodeId objNodeID)
		{
			HAPI_GeoInfo displayGeoInfo = new HAPI_GeoInfo();
			if (_session.GetDisplayGeoInfo(objNodeID, ref displayGeoInfo))
			{
				return displayGeoInfo.nodeId;
			}

			return HEU_Defines.HEU_INVALID_NODE_ID;
		}

		private bool SetFileParm(HAPI_NodeId fileNodeID, string filePath)
		{
			HAPI_ParmId parmID = -1;
			if (!_session.GetParmIDFromName(fileNodeID, "file", out parmID))
			{
				return false;
			}

			if (!_session.SetParamStringValue(fileNodeID, filePath, parmID, 0))
			{
				return false;
			}

			return true;
		}

		private bool QueryParts(HAPI_NodeId nodeID, ref List<HAPI_PartInfo> meshParts, ref List<HAPI_PartInfo> volumeParts)
		{
			// Get display geo info
			HAPI_GeoInfo geoInfo = new HAPI_GeoInfo();
			if (!_session.GetGeoInfo(nodeID, ref geoInfo))
			{
				return false;
			}

			//Debug.LogFormat("GeoNode name:{0}, type: {1}, isTemplated: {2}, isDisplayGeo: {3}, isEditable: {4}, parts: {5}",
			//	HEU_SessionManager.GetString(geoInfo.nameSH, _session),
			//	geoInfo.type, geoInfo.isTemplated,
			//	geoInfo.isDisplayGeo, geoInfo.isEditable, geoInfo.partCount);

			if (geoInfo.type == HAPI_GeoType.HAPI_GEOTYPE_DEFAULT)
			{
				int numParts = geoInfo.partCount;
				for(int i = 0; i < numParts; ++i)
				{
					HAPI_PartInfo partInfo = new HAPI_PartInfo();
					if (!_session.GetPartInfo(geoInfo.nodeId, i, ref partInfo))
					{
						return false;
					}

					if (partInfo.type == HAPI_PartType.HAPI_PARTTYPE_VOLUME)
					{
						volumeParts.Add(partInfo);
					}
					else if(partInfo.type == HAPI_PartType.HAPI_PARTTYPE_MESH)
					{
						meshParts.Add(partInfo);
					}
				}
			}

			return true;
		}

		private bool GenerateTerrainBuffers(HAPI_NodeId nodeID, List<HAPI_PartInfo> volumeParts)
		{
			if (volumeParts.Count == 0)
			{
				return true;
			}

			_loadData._terrainTiles = new List<HEU_LoadVolumeTerrainTile>();

			int numParts = volumeParts.Count;
			for (int i = 0; i < numParts; ++i)
			{
				HAPI_VolumeInfo volumeInfo = new HAPI_VolumeInfo();
				bool bResult = _session.GetVolumeInfo(nodeID, volumeParts[i].id, ref volumeInfo);
				if (!bResult || volumeInfo.tupleSize != 1 || volumeInfo.zLength != 1 || volumeInfo.storage != HAPI_StorageType.HAPI_STORAGETYPE_FLOAT)
				{
					SetLog(HEU_LoadData.LoadStatus.ERROR, "This heightfield is not supported. Please check documentation.");
					return false;
				}

				if (volumeInfo.xLength != volumeInfo.yLength)
				{
					SetLog(HEU_LoadData.LoadStatus.ERROR, "Non-square sized terrain not supported.");
					return false;
				}

				string volumeName = HEU_SessionManager.GetString(volumeInfo.nameSH, _session);
				bool bHeightPart = volumeName.Equals("height");

				//Debug.LogFormat("Part name: {0}, GeoName: {1}, Volume Name: {2}, Display: {3}", part.PartName, geoNode.GeoName, volumeName, geoNode.Displayable);

				HEU_LoadVolumeLayer layer = new HEU_LoadVolumeLayer();
				layer._layerName = volumeName;
				layer._partID = volumeParts[i].id;
				layer._heightMapSize = volumeInfo.xLength;

				Matrix4x4 volumeTransformMatrix = HEU_HAPIUtility.GetMatrixFromHAPITransform(ref volumeInfo.transform, false);
				layer._position = HEU_HAPIUtility.GetPosition(ref volumeTransformMatrix);
				Vector3 scale = HEU_HAPIUtility.GetScale(ref volumeTransformMatrix);

				// Calculate real terrain size in both Houdini and Unity.
				// The height values will be mapped over this terrain size.
				float gridSpacingX = scale.x * 2f;
				float gridSpacingY = scale.y * 2f;
				float multiplierOffsetX = Mathf.Round(scale.x);
				float multiplierOffsetY = Mathf.Round(scale.y);
				layer._terrainSizeX = Mathf.Round(volumeInfo.xLength * gridSpacingX - multiplierOffsetX);
				layer._terrainSizeY = Mathf.Round(volumeInfo.yLength * gridSpacingY - multiplierOffsetY);

				// Get volume bounds for calculating position offset
				_session.GetVolumeBounds(nodeID, volumeParts[i].id, 
					out layer._minBounds.x, out layer._minBounds.y, out layer._minBounds.z, 
					out layer._maxBounds.x, out layer._maxBounds.y, out layer._maxBounds.z, 
					out layer._center.x, out layer._center.y, out layer._center.z);

				LoadStringFromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_DIFFUSE_ATTR, ref layer._diffuseTexturePath);
				LoadStringFromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_MASK_ATTR, ref layer._maskTexturePath);
				LoadStringFromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TEXTURE_NORMAL_ATTR, ref layer._normalTexturePath);

				LoadFloatFromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_NORMAL_SCALE_ATTR, ref layer._normalScale);
				LoadFloatFromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_METALLIC_ATTR, ref layer._metallic);
				LoadFloatFromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SMOOTHNESS_ATTR, ref layer._smoothness);

				LoadLayerColorFromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_SPECULAR_ATTR, ref layer._specularColor);
				LoadLayerVector2FromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_OFFSET_ATTR, ref layer._tileOffset);
				LoadLayerVector2FromAttribute(_session, nodeID, volumeParts[i].id, HEU_Defines.DEFAULT_UNITY_HEIGHTFIELD_TILE_SIZE_ATTR, ref layer._tileSize);

				// Get the height values from Houdini and find the min and max height range.
				if (!HEU_GeometryUtility.GetHeightfieldValues(_session, volumeInfo.xLength, volumeInfo.yLength, nodeID, volumeParts[i].id, ref layer._rawHeights, ref layer._minHeight, ref layer._maxHeight))
				{
					return false;
				}

				// TODO: Tried to replace above with this, but it flattens the heights
				//layer._rawHeights = HEU_GeometryUtility.GetHeightfieldFromPart(_session, nodeID, volumeParts[i].id, "part", volumeInfo.xLength);

				// Get the tile index, if it exists, for this part
				HAPI_AttributeInfo tileAttrInfo = new HAPI_AttributeInfo();
				int[] tileAttrData = new int[0];
				HEU_GeneralUtility.GetAttribute(_session, nodeID, volumeParts[i].id, "tile", ref tileAttrInfo, ref tileAttrData, _session.GetAttributeIntData);

				int tileIndex = 0;
				if (tileAttrInfo.exists && tileAttrData.Length == 1)
				{
					tileIndex = tileAttrData[0];
				}

				// Add layer based on tile index
				if (tileIndex >= 0)
				{
					HEU_LoadVolumeTerrainTile terrainTile = null;
					for(int j = 0; j < _loadData._terrainTiles.Count; ++j)
					{
						if (_loadData._terrainTiles[j]._tileIndex == tileIndex)
						{
							terrainTile = _loadData._terrainTiles[j];
							break;
						}
					}

					if (terrainTile == null)
					{
						terrainTile = new HEU_LoadVolumeTerrainTile();
						terrainTile._tileIndex = tileIndex;
						_loadData._terrainTiles.Add(terrainTile);
					}

					if (bHeightPart)
					{
						// Height layer always first layer
						terrainTile._layers.Insert(0, layer);

						terrainTile._heightMapSize = layer._heightMapSize;
						terrainTile._terrainSizeX = layer._terrainSizeX;
						terrainTile._terrainSizeY = layer._terrainSizeY;
						terrainTile._heightRange = (layer._maxHeight - layer._minHeight);
					}
					else
					{
						terrainTile._layers.Add(layer);
					}
				}

				Sleep();
			}

			foreach(HEU_LoadVolumeTerrainTile tile in _loadData._terrainTiles)
			{
				List<HEU_LoadVolumeLayer> layers = tile._layers;
				//Debug.LogFormat("Heightfield: tile={0}, layers={1}", tile._tileIndex, layers.Count);

				int heightMapSize = tile._heightMapSize;

				int numLayers = layers.Count;
				if (numLayers > 0)
				{
					// Convert heightmap values from Houdini to Unity
					tile._heightMap = HEU_GeometryUtility.ConvertHeightMapHoudiniToUnity(heightMapSize, layers[0]._rawHeights, layers[0]._minHeight, layers[0]._maxHeight);

					Sleep();

					// Convert splatmap values from Houdini to Unity.
					List<float[]> heightFields = new List<float[]>();
					for(int m = 1; m < numLayers; ++m)
					{
						heightFields.Add(layers[m]._rawHeights);
					}
					tile._splatMaps = HEU_GeometryUtility.ConvertHeightSplatMapHoudiniToUnity(heightMapSize, heightFields);

					tile._position = new Vector3((tile._terrainSizeX + tile._layers[0]._minBounds.x), tile._layers[0]._minHeight + tile._layers[0]._position.y, tile._layers[0]._minBounds.z);
				}
			}

			return true;
		}

		private void LoadStringFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref string strValue)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			string[] strAttr = HEU_GeneralUtility.GetAttributeStringData(session, geoID, partID, attrName, ref attrInfo);
			if (strAttr != null && strAttr.Length > 0 && !string.IsNullOrEmpty(strAttr[0]))
			{
				strValue = strAttr[0];
			}
		}

		private void LoadFloatFromAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_NodeId partID, string attrName, ref float floatValue)
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

		private void Sleep()
		{
			System.Threading.Thread.Sleep(0);
		}


		//	DATA ------------------------------------------------------------------------------------------------------

		// Setup
		private string _filePath;
		private HEU_GeoSync _geoSync;
		private HEU_SessionBase _session;

		private HEU_LoadData _loadData;

		public class HEU_LoadData
		{
			public HAPI_NodeId _fileNodeID;

			public enum LoadStatus
			{
				NONE,
				STARTED,
				SUCCESS,
				ERROR,
			}
			public LoadStatus _loadStatus;

			public string _logStr;

			public HEU_GeoGroup _geoGroup;

			public HEU_SessionBase _session;

			public List<HEU_LoadVolumeTerrainTile> _terrainTiles;
		}

		public class HEU_LoadVolumeTerrainTile
		{
			public int _tileIndex;
			public List<HEU_LoadVolumeLayer> _layers = new List<HEU_LoadVolumeLayer>();

			public int _heightMapSize;
			public float[,] _heightMap;
			public float[,,] _splatMaps;

			public float _terrainSizeX;
			public float _terrainSizeY;
			public float _heightRange;

			public Vector3 _position;
		}

		public class HEU_LoadVolumeLayer
		{
			public string _layerName;
			public HAPI_PartId _partID;
			public int _heightMapSize;
			public float _strength = 1.0f;

			public string _diffuseTexturePath;
			public string _maskTexturePath;
			public float _metallic = 0f;
			public string _normalTexturePath;
			public float _normalScale = 0.5f;
			public float _smoothness = 0f;
			public Color _specularColor = Color.gray;
			public Vector2 _tileSize = Vector2.zero;
			public Vector2 _tileOffset = Vector2.zero;

			public bool _uiExpanded;
			public int _tile = 0;

			public float[] _rawHeights;
			public float _minHeight;
			public float _maxHeight;

			public float _terrainSizeX;
			public float _terrainSizeY;

			public Vector3 _position;
			public Vector3 _minBounds;
			public Vector3 _maxBounds;
			public Vector3 _center;
		}
	}

}   // namespace HoudiniEngineUnity