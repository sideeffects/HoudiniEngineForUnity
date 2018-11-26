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

using UnityEngine;

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
	/// This class provides functionality for uploading Unity terrain data from a gameobject
	/// into Houdini through a heightfield node network.
	/// It derives from the HEU_InputInterface and registers with HEU_InputUtility so that it
	/// can be used automatically when uploading terrain data.
	/// </summary>
	public class HEU_InputInterfaceTerrain : HEU_InputInterface
	{
#if UNITY_EDITOR
		/// <summary>
		/// Registers this input inteface for Unity meshes on
		/// the callback after scripts are reloaded in Unity.
		/// </summary>
		[InitializeOnLoadMethod]
		[UnityEditor.Callbacks.DidReloadScripts]
		private static void OnScriptsReloaded()
		{
			HEU_InputInterfaceTerrain inputInterface = new HEU_InputInterfaceTerrain();
			HEU_InputUtility.RegisterInputInterface(inputInterface);
		}
#endif

		public HEU_InputInterfaceTerrain() : base(priority: DEFAULT_PRIORITY)
		{

		}

		/// <summary>
		/// Creates a heightfield network inside the same object as connectNodeID.
		/// Uploads the terrain data from inputObject into the new heightfield network, incuding
		/// all terrain layers/alphamaps.
		/// </summary>
		/// <param name="session">Session that connectNodeID exists in</param>
		/// <param name="connectNodeID">The node to connect the network to. Most likely a SOP/merge node</param>
		/// <param name="inputObject">The gameobject containing the Terrain components</param>
		/// <param name="inputNodeID">The created heightfield network node ID</param>
		/// <returns>True if created network and uploaded heightfield data.</returns>
		public override bool CreateInputNodeWithDataUpload(HEU_SessionBase session, HAPI_NodeId connectNodeID, GameObject inputObject, out HAPI_NodeId inputNodeID)
		{
			inputNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

			// Create input node, cook it, then upload the geometry data

			if (!HEU_HAPIUtility.IsNodeValidInHoudini(session, connectNodeID))
			{
				Debug.LogError("Connection node is invalid.");
				return false;
			}

			HEU_InputDataTerrain idt = GenerateTerrainDataFromGameObject(inputObject);
			if (idt == null)
			{
				return false;
			}

			HAPI_NodeId parentNodeID = HEU_HAPIUtility.GetParentNodeID(session, connectNodeID);
			idt._parentNodeID = parentNodeID;

			if (!CreateHeightFieldInputNode(session, idt))
			{
				return false;
			}

			if (!UploadHeightValuesWithTransform(session, idt))
			{
				return false;
			}

			inputNodeID = idt._heightfieldNodeID;

			if (!UploadAlphaMaps(session, idt))
			{
				return false;
			}

			if (!session.CookNode(inputNodeID, false))
			{
				Debug.LogError("New input node failed to cook!");
				return false;
			}

			return true;
		}

		public override bool IsThisInputObjectSupported(GameObject inputObject)
		{
			if (inputObject != null)
			{
				if (inputObject.GetComponentInChildren<Terrain>(true) != null)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Create the main heightfield network for input.
		/// </summary>
		/// <param name="session"></param>
		/// <param name="idt"></param>
		/// <returns>True if successfully created the network</returns>
		public bool CreateHeightFieldInputNode(HEU_SessionBase session, HEU_InputDataTerrain idt)
		{
			idt._heightfieldNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			idt._heightNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			idt._maskNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			idt._mergeNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

			// Create the HeightField node network
			bool bResult = session.CreateHeightfieldInputNode(idt._parentNodeID, idt._heightFieldName, idt._numPointsX, idt._numPointsY, idt._voxelSize,
				out idt._heightfieldNodeID, out idt._heightNodeID, out idt._maskNodeID, out idt._mergeNodeID);
			if (!bResult 
				|| idt._heightfieldNodeID == HEU_Defines.HEU_INVALID_NODE_ID 
				|| idt._heightNodeID == HEU_Defines.HEU_INVALID_NODE_ID
				|| idt._maskNodeID == HEU_Defines.HEU_INVALID_NODE_ID 
				|| idt._mergeNodeID == HEU_Defines.HEU_INVALID_NODE_ID)
			{
				Debug.LogError("Failed to create new heightfield node in Houdini session!");
				return false;
			}

			if (!session.CookNode(idt._heightNodeID, false))
			{
				Debug.LogError("New input node failed to cook!");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Upload the base height layer into heightfield network.
		/// </summary>
		/// <param name="session"></param>
		/// <param name="idt"></param>
		/// <returns></returns>
		public bool UploadHeightValuesWithTransform(HEU_SessionBase session, HEU_InputDataTerrain idt)
		{
			// Get Geo, Part, and Volume infos
			HAPI_GeoInfo geoInfo = new HAPI_GeoInfo();
			if (!session.GetGeoInfo(idt._heightNodeID, ref geoInfo))
			{
				Debug.LogError("Unable to get geo info from heightfield node!");
				return false;
			}

			HAPI_PartInfo partInfo = new HAPI_PartInfo();
			if (!session.GetPartInfo(geoInfo.nodeId, 0, ref partInfo))
			{
				Debug.LogError("Unable to get part info from heightfield node!");
				return false;
			}

			HAPI_VolumeInfo volumeInfo = new HAPI_VolumeInfo();
			if (!session.GetVolumeInfo(idt._heightNodeID, 0, ref volumeInfo))
			{
				Debug.LogError("Unable to get volume info from heightfield node!");
				return false;
			}

			if (volumeInfo.xLength != Mathf.RoundToInt(idt._numPointsX / idt._voxelSize)
				|| volumeInfo.yLength != Mathf.RoundToInt(idt._numPointsY / idt._voxelSize)
				|| idt._terrainData.heightmapResolution != volumeInfo.xLength
				|| idt._terrainData.heightmapResolution != volumeInfo.yLength)
			{
				Debug.LogError("Created heightfield in Houdini differs in voxel size from input terrain!");
				return false;
			}

			// Update volume infos, and set it. This is required.
			volumeInfo.tileSize = 1;
			volumeInfo.type = HAPI_VolumeType.HAPI_VOLUMETYPE_HOUDINI;
			volumeInfo.storage = HAPI_StorageType.HAPI_STORAGETYPE_FLOAT;
			volumeInfo.transform = idt._transform;

			volumeInfo.minX = 0;
			volumeInfo.minY = 0;
			volumeInfo.minZ = 0;

			volumeInfo.tupleSize = 1;
			volumeInfo.tileSize = 1;

			volumeInfo.hasTaper = false;
			volumeInfo.xTaper = 0f;
			volumeInfo.yTaper = 0f;

			if (!session.SetVolumeInfo(idt._heightNodeID, partInfo.id, ref volumeInfo))
			{
				Debug.LogError("Unable to set volume info on input heightfield node!");
				return false;
			}

			// Now set the height data
			float[,] heights = idt._terrainData.GetHeights(0, 0, volumeInfo.xLength, volumeInfo.yLength);
			int sizeX = heights.GetLength(0);
			int sizeY = heights.GetLength(1);
			int totalSize = sizeX * sizeY;

			// Convert to single array
			float[] heightsArr = new float[totalSize];
			for (int j = 0; j < sizeY; j++)
			{
				for (int i = 0; i < sizeX; i++)
				{
					// Flip for coordinate system change
					float h = heights[i, (sizeY - j - 1)];

					heightsArr[i + j * sizeX] = h * idt._heightScale;
				}
			}

			// Set the base height layer
			if (!session.SetHeightFieldData(idt._heightNodeID, 0, "height", heightsArr, 0, totalSize))
			{
				Debug.LogError("Unable to set height values on input heightfield node!");
				return false;
			}

			if (!session.CommitGeo(idt._heightNodeID))
			{
				Debug.LogError("Unable to commit geo on input heightfield node!");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Upload the alphamaps (terrain layers) into heightfield network.
		/// Note that this skips the base layer.
		/// </summary>
		/// <param name="session"></param>
		/// <param name="idt"></param>
		/// <returns></returns>
		public bool UploadAlphaMaps(HEU_SessionBase session, HEU_InputDataTerrain idt)
		{
			bool bResult = true;

			int alphaLayers = idt._terrainData.alphamapLayers;

			// Skip the base layer
			if (alphaLayers < 1)
			{
				return true;
			}

			int sizeX = idt._terrainData.alphamapWidth;
			int sizeY = idt._terrainData.alphamapHeight;
			int totalSize = sizeX * sizeY;

			float[,,] alphaMaps = idt._terrainData.GetAlphamaps(0, 0, sizeX, sizeY);

			float[][] alphaMapsConverted = new float[alphaLayers - 1][];

			// Convert the alphamap layers to double arrays.
			// Note that we're skipping the base alpha map.
			for (int m = 0; m < alphaLayers - 1; ++m)
			{
				alphaMapsConverted[m] = new float[totalSize];
				for (int j = 0; j < sizeY; j++)
				{
					for (int i = 0; i < sizeX; i++)
					{
						// Flip for coordinate system change
						float h = alphaMaps[i, (sizeY - j - 1), m + 1];

						alphaMapsConverted[m][i + j * sizeX] = h;
					}
				}
			}


			// Create volume layers for all non-base alpha maps and upload values.
			for (int m = 0; m < alphaLayers - 1; ++m)
			{
				string layerName = "unity_alphamap_" + m + 1;

				HAPI_NodeId alphaLayerID = HEU_Defines.HEU_INVALID_NODE_ID;
				if (!session.CreateHeightfieldInputVolumeNode(idt._heightfieldNodeID, out alphaLayerID, layerName,
					Mathf.RoundToInt(sizeX * idt._voxelSize), Mathf.RoundToInt(sizeY * idt._voxelSize), idt._voxelSize))
				{
					bResult = false;
					Debug.LogError("Failed to create input volume node for layer " + layerName);
					break;
				}

				if (!SetHeightFieldData(session, alphaLayerID, 0, alphaMapsConverted[m], layerName))
				{
					bResult = false;
					break;
				}

				if (!session.CommitGeo(alphaLayerID))
				{
					bResult = false;
					Debug.LogError("Failed to commit volume layer " + layerName);
					break;
				}

				// Connect to the merge node but starting from index 1 since index 0 is height layer
				if (!session.ConnectNodeInput(idt._mergeNodeID, m + 2, alphaLayerID, 0))
				{
					bResult = false;
					Debug.LogError("Unable to connect new volume node for layer " + layerName);
					break;
				}
			}

			return bResult;
		}

		/// <summary>
		/// Helper to set heightfield data for a specific volume node.
		/// Used for a specific terrain layer.
		/// </summary>
		/// <param name="session">Session that the volume node resides in.</param>
		/// <param name="volumeNodeID">ID of the target volume node</param>
		/// <param name="partID">Part ID</param>
		/// <param name="heightValues">Array of height or alpha values</param>
		/// <param name="heightFieldName">Name of the layer</param>
		/// <returns>True if successfully uploaded heightfield values</returns>
		public bool SetHeightFieldData(HEU_SessionBase session, HAPI_NodeId volumeNodeID, HAPI_PartId partID, float[] heightValues, string heightFieldName)
		{
			// Cook the node to get infos below
			if (!session.CookNode(volumeNodeID, false))
			{
				return false;
			}

			// Get Geo, Part, and Volume infos
			HAPI_GeoInfo geoInfo = new HAPI_GeoInfo();
			if (!session.GetGeoInfo(volumeNodeID, ref geoInfo))
			{
				return false;
			}

			HAPI_PartInfo partInfo = new HAPI_PartInfo();
			if (!session.GetPartInfo(geoInfo.nodeId, partID, ref partInfo))
			{
				return false;
			}

			HAPI_VolumeInfo volumeInfo = new HAPI_VolumeInfo();
			if (!session.GetVolumeInfo(volumeNodeID, partInfo.id, ref volumeInfo))
			{
				return false;
			}

			volumeInfo.tileSize = 1;

			if (!session.SetVolumeInfo(volumeNodeID, partInfo.id, ref volumeInfo))
			{
				Debug.LogError("Unable to set volume info on input heightfield node!");
				return false;
			}

			// Now set the height data
			if (!session.SetHeightFieldData(geoInfo.nodeId, partInfo.id, heightFieldName, heightValues, 0, heightValues.Length))
			{
				Debug.LogError("Unable to set height values on input heightfield node!");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Holds terrain data for uploading as heightfields
		/// </summary>
		public class HEU_InputDataTerrain : HEU_InputData
		{
			// Default values
			public string _heightFieldName = "input";
			public HAPI_NodeId _parentNodeID = -1;
			public float _voxelSize = 2;

			// Acquired from input object
			public Terrain _terrain;
			public TerrainData _terrainData;

			public int _numPointsX;
			public int _numPointsY;

			public HAPI_Transform _transform = new HAPI_Transform();

			public float _heightScale;

			// Retrieved from Houdini
			public HAPI_NodeId _heightfieldNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			public HAPI_NodeId _heightNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			public HAPI_NodeId _maskNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			public HAPI_NodeId _mergeNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
		}

		/// <summary>
		/// Generates heightfield/terrain data from the given object relevant for uploading to Houdini.
		/// </summary>
		/// <param name="inputObject"></param>
		/// <returns>Valid input object or null if given object is not supported</returns>
		public HEU_InputDataTerrain GenerateTerrainDataFromGameObject(GameObject inputObject)
		{
			HEU_InputDataTerrain inputData = null;

			Terrain terrain = inputObject.GetComponent<Terrain>();
			if (terrain != null)
			{
				TerrainData terrainData = terrain.terrainData;

				Vector3 terrainSize = terrainData.size;
				if (terrainSize.x != terrainSize.z)
				{
					Debug.LogError("Only square sized terrains are supported for input! Change to square size and try again.");
					return null;
				}

				inputData = new HEU_InputDataTerrain();
				inputData._inputObject = inputObject;
				inputData._terrain = terrain;
				inputData._terrainData = terrainData;

				// Height values in Unity are normalized between 0 and 1, so this height scale
				// will multiply them before uploading to Houdini.
				inputData._heightScale = terrainSize.y;

				// Terrain heightMapResolution is the pixel resolution, which we set to the number of voxels
				// by dividing the terrain size with it. In Houdini, this is the Grid Spacing.
				inputData._voxelSize = terrainSize.x / inputData._terrainData.heightmapResolution;

				// This is the number of heightfield voxels on each dimension.
				inputData._numPointsX = Mathf.RoundToInt(inputData._terrainData.heightmapResolution * inputData._voxelSize);
				inputData._numPointsY = Mathf.RoundToInt(inputData._terrainData.heightmapResolution * inputData._voxelSize);

				Matrix4x4 transformMatrix = inputObject.transform.localToWorldMatrix;
				HAPI_TransformEuler transformEuler = HEU_HAPIUtility.GetHAPITransformFromMatrix(ref transformMatrix);

				// Volume transform used for all heightfield layers
				inputData._transform = new HAPI_Transform(false);

				// Unity terrain pivots are at bottom left, but Houdini uses centered heightfields so
				// apply local position offset by half sizes and account for coordinate change
				inputData._transform.position[0] = terrainSize.z * 0.5f;
				inputData._transform.position[1] = -terrainSize.x * 0.5f;
				inputData._transform.position[2] = 0;

				// Volume scale controls final size, but requires to be divided by 2
				inputData._transform.scale[0] = terrainSize.x * 0.5f;
				inputData._transform.scale[1] = terrainSize.z * 0.5f;
				inputData._transform.scale[2] = 0.5f;

				inputData._transform.rotationQuaternion[0] = 0f;
				inputData._transform.rotationQuaternion[1] = 0f;
				inputData._transform.rotationQuaternion[2] = 0f;
				inputData._transform.rotationQuaternion[3] = 1f;
			}

			return inputData;
		}
	}

}   // HoudiniEngineUnity