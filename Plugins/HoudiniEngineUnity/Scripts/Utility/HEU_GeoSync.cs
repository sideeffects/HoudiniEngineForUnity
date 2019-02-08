#if (UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_LINUX)
#define HOUDINIENGINEUNITY_ENABLED
#endif

using System.Collections;
using System.Collections.Generic;
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
	/// Lightweight Unity geometry generator for Houdini geometry.
	/// Given already loaded geometry buffers, creates corresponding Unity geometry.
	/// </summary>
	public class HEU_GeoSync : MonoBehaviour
	{
		//	LOGIC -----------------------------------------------------------------------------------------------------

		private void Awake()
		{
#if HOUDINIENGINEUNITY_ENABLED
			if (_sessionID != HEU_SessionData.INVALID_SESSION_ID)
			{
				HEU_SessionBase session = HEU_SessionManager.GetSessionWithID(_sessionID);
				if (session == null || !HEU_HAPIUtility.IsNodeValidInHoudini(session, _fileNodeID))
				{
					// Reset session and file node IDs if these don't exist (could be from scene load).
					_sessionID = HEU_SessionData.INVALID_SESSION_ID;
					_fileNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
				}
			}
#endif
		}



		public void Initialize()
		{
			_generateOptions._generateNormals = true;
			_generateOptions._generateTangents = true;
			_generateOptions._generateUVs = false;
			_generateOptions._useLODGroups = true;
			_generateOptions._splitPoints = false;

			_initialized = true;
		}

		public void StartSync()
		{
			if (_bSyncing)
			{
				return;
			}

			if (!_initialized)
			{
				Initialize();
			}

			HEU_SessionBase session = GetHoudiniSession(true);
			if (session == null)
			{
				_logStr = "ERROR: No session found!";
				return;
			}

			if (_loadGeo == null)
			{
				_loadGeo = new HEU_ThreadedTaskLoadGeo();
			}

			_logStr = "Starting";
			_bSyncing = true;
			_sessionID = session.GetSessionData().SessionID;

			_loadGeo.Setup(_filePath, this, session, _fileNodeID);
			_loadGeo.Start();
		}

		public void StopSync()
		{
			if (!_bSyncing)
			{
				return;
			}

			if (_loadGeo != null)
			{
				_loadGeo.Stop();
			}

			_logStr = "Stopped";
			_bSyncing = false;
		}

		public void Unload()
		{
			if (_bSyncing)
			{
				StopSync();

				if (_loadGeo != null)
				{
					_loadGeo.Stop();
				}
			}

			DeleteSessionData();
			DestroyOutputs();

			_logStr = "Unloaded!";
		}

		public void OnLoadComplete(HEU_ThreadedTaskLoadGeo.HEU_LoadData loadData)
		{
			_bSyncing = false;

			_logStr = loadData._logStr;
			_fileNodeID = loadData._fileNodeID;

			if (loadData._loadStatus == HEU_ThreadedTaskLoadGeo.HEU_LoadData.LoadStatus.SUCCESS)
			{
				DestroyOutputs();

				if (loadData._meshBuffers != null && loadData._meshBuffers.Count > 0)
				{
					GenerateMesh(loadData._meshBuffers);
				}

				if (loadData._terrainBuffers != null && loadData._terrainBuffers.Count > 0)
				{
					GenerateTerrain(loadData._terrainBuffers);
				}

				if (loadData._instancerBuffers != null && loadData._instancerBuffers.Count > 0)
				{
					GenerateAllInstancers(loadData._instancerBuffers, loadData);
				}
			}
		}

		public void OnStopped(HEU_ThreadedTaskLoadGeo.HEU_LoadData loadData)
		{
			_bSyncing = false;

			_logStr = loadData._logStr;
			_fileNodeID = loadData._fileNodeID;
		}

		private void DeleteSessionData()
		{
			if (_fileNodeID != HEU_Defines.HEU_INVALID_NODE_ID)
			{
				HEU_SessionBase session = GetHoudiniSession(false);
				if (session != null)
				{
					session.DeleteNode(_fileNodeID);
				}

				_fileNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			}
		}

		private void GenerateTerrain(List<HEU_LoadBufferVolume> terrainBuffers)
		{
			Transform parent = this.gameObject.transform;

			int numVolues = terrainBuffers.Count;
			for(int t = 0; t < numVolues; ++t)
			{
				if (terrainBuffers[t]._heightMap != null)
				{
					GameObject newGameObject = new GameObject("heightfield_" + terrainBuffers[t]._tileIndex);
					Transform newTransform = newGameObject.transform;
					newTransform.parent = parent;

					HEU_GeneratedOutput generatedOutput = new HEU_GeneratedOutput();
					generatedOutput._outputData._gameObject = newGameObject;

					Terrain terrain = HEU_GeneralUtility.GetOrCreateComponent<Terrain>(newGameObject);
					TerrainCollider collider = HEU_GeneralUtility.GetOrCreateComponent<TerrainCollider>(newGameObject);

					terrain.terrainData = new TerrainData();
					TerrainData terrainData = terrain.terrainData;
					collider.terrainData = terrainData;

					int heightMapSize = terrainBuffers[t]._heightMapSize;

					terrainData.heightmapResolution = heightMapSize;
					if (terrainData.heightmapResolution != heightMapSize)
					{
						Debug.LogErrorFormat("Unsupported terrain size: {0}", heightMapSize);
						continue;
					}

					terrainData.baseMapResolution = heightMapSize;
					terrainData.alphamapResolution = heightMapSize;

					const int resolutionPerPatch = 128;
					terrainData.SetDetailResolution(resolutionPerPatch, resolutionPerPatch);

					terrainData.SetHeights(0, 0, terrainBuffers[t]._heightMap);

					terrainData.size = new Vector3(terrainBuffers[t]._terrainSizeX, terrainBuffers[t]._heightRange, terrainBuffers[t]._terrainSizeY);

					terrain.Flush();

					// Set position
					HAPI_Transform hapiTransformVolume = new HAPI_Transform(true);
					hapiTransformVolume.position[0] += terrainBuffers[t]._position[0];
					hapiTransformVolume.position[1] += terrainBuffers[t]._position[1];
					hapiTransformVolume.position[2] += terrainBuffers[t]._position[2];
					HEU_HAPIUtility.ApplyLocalTransfromFromHoudiniToUnity(ref hapiTransformVolume, newTransform);

					// Set layers
					Texture2D defaultTexture = HEU_VolumeCache.LoadDefaultSplatTexture();
					int numLayers = terrainBuffers[t]._layers.Count;

#if UNITY_2018_3_OR_NEWER

					// Create TerrainLayer for each heightfield layer
					// Note that at time of this implementation the new Unity terrain
					// is still in beta. Therefore, the following layer creation is subject
					// to change.

					TerrainLayer[] terrainLayers = new TerrainLayer[numLayers];
					for (int m = 0; m < numLayers; ++m)
					{
						terrainLayers[m] = new TerrainLayer();

						HEU_LoadBufferVolumeLayer layer = terrainBuffers[t]._layers[m];

						if (!string.IsNullOrEmpty(layer._diffuseTexturePath))
						{
							// Using Resources.Load is much faster than AssetDatabase.Load
							//terrainLayers[m].diffuseTexture = HEU_MaterialFactory.LoadTexture(layer._diffuseTexturePath);
							terrainLayers[m].diffuseTexture = Resources.Load<Texture2D>(layer._diffuseTexturePath);
						}
						if (terrainLayers[m].diffuseTexture == null)
						{
							terrainLayers[m].diffuseTexture = defaultTexture;
						}

						terrainLayers[m].diffuseRemapMin = Vector4.zero;
						terrainLayers[m].diffuseRemapMax = Vector4.one;

						if (!string.IsNullOrEmpty(layer._maskTexturePath))
						{
							// Using Resources.Load is much faster than AssetDatabase.Load
							//terrainLayers[m].maskMapTexture = HEU_MaterialFactory.LoadTexture(layer._maskTexturePath);
							terrainLayers[m].maskMapTexture = Resources.Load<Texture2D>(layer._maskTexturePath);
						}

						terrainLayers[m].maskMapRemapMin = Vector4.zero;
						terrainLayers[m].maskMapRemapMax = Vector4.one;

						terrainLayers[m].metallic = layer._metallic;

						if (!string.IsNullOrEmpty(layer._normalTexturePath))
						{
							terrainLayers[m].normalMapTexture = HEU_MaterialFactory.LoadTexture(layer._normalTexturePath);
						}

						terrainLayers[m].normalScale = layer._normalScale;

						terrainLayers[m].smoothness = layer._smoothness;
						terrainLayers[m].specular = layer._specularColor;
						terrainLayers[m].tileOffset = layer._tileOffset;

						if (layer._tileSize.magnitude == 0f && terrainLayers[m].diffuseTexture != null)
						{
							// Use texture size if tile size is 0
							layer._tileSize = new Vector2(terrainLayers[m].diffuseTexture.width, terrainLayers[m].diffuseTexture.height);
						}
						terrainLayers[m].tileSize = layer._tileSize;
					}
					terrainData.terrainLayers = terrainLayers;

#else
					// Need to create SplatPrototype for each layer in heightfield, representing the textures.
					SplatPrototype[] splatPrototypes = new SplatPrototype[numLayers];
					for (int m = 0; m < numLayers; ++m)
					{
						splatPrototypes[m] = new SplatPrototype();

						HEU_LoadBufferVolumeLayer layer = terrainBuffers[t]._layers[m];

						Texture2D diffuseTexture = null;
						if (!string.IsNullOrEmpty(layer._diffuseTexturePath))
						{
							diffuseTexture = HEU_MaterialFactory.LoadTexture(layer._diffuseTexturePath);
						}
						if (diffuseTexture == null)
						{
							diffuseTexture = defaultTexture;
						}
						splatPrototypes[m].texture = diffuseTexture;

						splatPrototypes[m].tileOffset = layer._tileOffset;
						if (layer._tileSize.magnitude == 0f && diffuseTexture != null)
						{
							// Use texture size if tile size is 0
							layer._tileSize = new Vector2(diffuseTexture.width, diffuseTexture.height);
						}
						splatPrototypes[m].tileSize = layer._tileSize;

						splatPrototypes[m].metallic = layer._metallic;
						splatPrototypes[m].smoothness = layer._smoothness;

						if (!string.IsNullOrEmpty(layer._normalTexturePath))
						{
							splatPrototypes[m].normalMap = HEU_MaterialFactory.LoadTexture(layer._normalTexturePath);
						}
					}
					terrainData.splatPrototypes = splatPrototypes;
#endif

					terrainData.SetAlphamaps(0, 0, terrainBuffers[t]._splatMaps);

					//string assetPath = HEU_AssetDatabase.CreateAssetCacheFolder("terrainData");
					//AssetDatabase.CreateAsset(terrainData, assetPath);
					//Debug.Log("Created asset data at " + assetPath);

					terrainBuffers[t]._generatedOutput = generatedOutput;
					_generatedOutputs.Add(generatedOutput);

					SetOutputVisiblity(terrainBuffers[t]);
				}
			}
		}

		private void GenerateMesh(List<HEU_LoadBufferMesh> meshBuffers)
		{
			HEU_SessionBase session = GetHoudiniSession(true);

			Transform parent = this.gameObject.transform;

			int numBuffers = meshBuffers.Count;
			for (int m = 0; m < numBuffers; ++m)
			{
				if (meshBuffers[m]._geoCache != null)
				{
					GameObject newGameObject = new GameObject("mesh_" + meshBuffers[m]._geoCache._partName);
					Transform newTransform = newGameObject.transform;
					newTransform.parent = parent;

					HEU_GeneratedOutput generatedOutput = new HEU_GeneratedOutput();
					generatedOutput._outputData._gameObject = newGameObject;

					bool bResult = false;
					int numLODs = meshBuffers[m]._LODGroupMeshes != null ? meshBuffers[m]._LODGroupMeshes.Count : 0;
					if (numLODs > 1)
					{
						bResult = HEU_GenerateGeoCache.GenerateLODMeshesFromGeoGroups(session, meshBuffers[m]._LODGroupMeshes, 
							meshBuffers[m]._geoCache, generatedOutput, meshBuffers[m]._defaultMaterialKey, 
							meshBuffers[m]._bGenerateUVs, meshBuffers[m]._bGenerateTangents, meshBuffers[m]._bGenerateNormals, meshBuffers[m]._bPartInstanced);
					}
					else if (numLODs == 1)
					{
						bResult = HEU_GenerateGeoCache.GenerateMeshFromSingleGroup(session, meshBuffers[m]._LODGroupMeshes[0], 
							meshBuffers[m]._geoCache, generatedOutput, meshBuffers[m]._defaultMaterialKey, 
							meshBuffers[m]._bGenerateUVs, meshBuffers[m]._bGenerateTangents, meshBuffers[m]._bGenerateNormals, meshBuffers[m]._bPartInstanced);
					}
					else
					{
						// Set return state to false if no mesh and not a collider type
						bResult = (meshBuffers[m]._geoCache._colliderType != HEU_GenerateGeoCache.ColliderType.NONE);
					}

					if (bResult)
					{
						HEU_GenerateGeoCache.UpdateCollider(meshBuffers[m]._geoCache, generatedOutput._outputData._gameObject);

						meshBuffers[m]._generatedOutput = generatedOutput;
						_generatedOutputs.Add(generatedOutput);

						SetOutputVisiblity(meshBuffers[m]);
					}
					else
					{
						HEU_GeneratedOutput.DestroyGeneratedOutput(generatedOutput);
					}
				}
			}
		}

		private HEU_LoadBufferBase GetLoadBufferFromID(HEU_ThreadedTaskLoadGeo.HEU_LoadData loadData, HAPI_NodeId id)
		{
			// Check each buffer array

			foreach(HEU_LoadBufferBase buffer in loadData._meshBuffers)
			{
				if(buffer._id == id)
				{
					return buffer;
				}
			}

			foreach (HEU_LoadBufferBase buffer in loadData._terrainBuffers)
			{
				if (buffer._id == id)
				{
					return buffer;
				}
			}

			foreach (HEU_LoadBufferBase buffer in loadData._instancerBuffers)
			{
				if (buffer._id == id)
				{
					return buffer;
				}
			}

			return null;
		}

		
		private void GenerateAllInstancers(List<HEU_LoadBufferInstancer> instancerBuffers, HEU_ThreadedTaskLoadGeo.HEU_LoadData loadData)
		{
			// Create a dictionary of load buffers to their IDs. This speeds up the instancer look up.
			Dictionary<HAPI_NodeId, HEU_LoadBufferBase> idBuffersMap = new Dictionary<HAPI_NodeId, HEU_LoadBufferBase>();

			if (loadData._meshBuffers != null)
			{
				foreach (HEU_LoadBufferBase buffer in loadData._meshBuffers)
				{
					idBuffersMap[buffer._id] = buffer;
				}
			}

			if (loadData._terrainBuffers != null)
			{
				foreach (HEU_LoadBufferBase buffer in loadData._terrainBuffers)
				{
					idBuffersMap[buffer._id] = buffer;
				}
			}

			if (loadData._instancerBuffers != null)
			{
				foreach (HEU_LoadBufferBase buffer in loadData._instancerBuffers)
				{
					idBuffersMap[buffer._id] = buffer;
				}
			}

			int numBuffers = instancerBuffers.Count;
			for (int m = 0; m < numBuffers; ++m)
			{
				GenerateInstancer(instancerBuffers[m], idBuffersMap);
			}
		}

		private void GenerateInstancer(HEU_LoadBufferInstancer instancerBuffer, Dictionary<HAPI_NodeId, HEU_LoadBufferBase> idBuffersMap)
		{
			if (instancerBuffer._generatedOutput != null)
			{
				// Already generated
				return;
			}

			Transform parent = this.gameObject.transform;

			GameObject instanceRootGO = new GameObject("instance_" + instancerBuffer._name);
			Transform instanceRootTransform = instanceRootGO.transform;
			instanceRootTransform.parent = parent;
			instanceRootTransform.localPosition = Vector3.zero;
			instanceRootTransform.localRotation = Quaternion.identity;
			instanceRootTransform.localScale = Vector3.one;

			instancerBuffer._generatedOutput = new HEU_GeneratedOutput();
			instancerBuffer._generatedOutput._outputData._gameObject = instanceRootGO;
			_generatedOutputs.Add(instancerBuffer._generatedOutput);

			if (instancerBuffer._instanceNodeIDs != null && instancerBuffer._instanceNodeIDs.Length > 0)
			{
				GenerateInstancesFromNodeIDs(instancerBuffer, idBuffersMap, instanceRootTransform);
			}
			else if (instancerBuffer._assetPaths != null && instancerBuffer._assetPaths.Length > 0)
			{
				GenerateInstancesFromAssetPaths(instancerBuffer, instanceRootTransform);
			}

			SetOutputVisiblity(instancerBuffer);
		}

		private void GenerateInstancesFromNodeIDs(HEU_LoadBufferInstancer instancerBuffer, Dictionary<HAPI_NodeId, HEU_LoadBufferBase> idBuffersMap,
			Transform instanceRootTransform)
		{
			int numInstances = instancerBuffer._instanceNodeIDs.Length;
			for (int i = 0; i < numInstances; ++i)
			{
				HEU_LoadBufferBase sourceBuffer = null;
				if (!idBuffersMap.TryGetValue(instancerBuffer._instanceNodeIDs[i], out sourceBuffer) || sourceBuffer == null)
				{
					Debug.LogErrorFormat("Part with id {0} is missing. Unable to setup instancer!", instancerBuffer._instanceNodeIDs[i]);
					return;
				}

				// If the part we're instancing is itself an instancer, make sure it has generated its instances
				if (sourceBuffer._bInstanced && sourceBuffer._generatedOutput == null)
				{
					HEU_LoadBufferInstancer sourceBufferInstancer = instancerBuffer as HEU_LoadBufferInstancer;
					if (sourceBufferInstancer != null)
					{
						GenerateInstancer(sourceBufferInstancer, idBuffersMap);
					}
				}

				GameObject sourceGameObject = sourceBuffer._generatedOutput._outputData._gameObject;
				if (sourceGameObject == null)
				{
					Debug.LogErrorFormat("Output gameobject is null for source {0}. Unable to instance for {1}.", sourceBuffer._name, instancerBuffer._name);
					continue;
				}

				int numTransforms = instancerBuffer._instanceTransforms.Length;
				for (int j = 0; j < numTransforms; ++j)
				{
					CreateNewInstanceFromObject(sourceGameObject, (j + 1), instanceRootTransform, ref instancerBuffer._instanceTransforms[i],
						instancerBuffer._instancePrefixes, instancerBuffer._name);
				}
			}
		}

		private void GenerateInstancesFromAssetPaths(HEU_LoadBufferInstancer instancerBuffer, Transform instanceRootTransform)
	{
			// For single asset, this is set when its impoted
			GameObject singleAssetGO = null;

			// For multi assets, keep track of loaded objects so we only need to load once for each object
			Dictionary<string, GameObject> loadedUnityObjectMap = new Dictionary<string, GameObject>();

			// Temporary empty gameobject in case the specified Unity asset is not found
			GameObject tempGO = null;

			if (instancerBuffer._assetPaths.Length == 1)
			{
				// Single asset path
				if (!string.IsNullOrEmpty(instancerBuffer._assetPaths[0]))
				{
					HEU_AssetDatabase.ImportAsset(instancerBuffer._assetPaths[0], HEU_AssetDatabase.HEU_ImportAssetOptions.Default);
					singleAssetGO = HEU_AssetDatabase.LoadAssetAtPath(instancerBuffer._assetPaths[0], typeof(GameObject)) as GameObject;
				}

				if (singleAssetGO == null)
				{
					Debug.LogErrorFormat("Asset at path {0} not found. Unable to create instances for {1}.", instancerBuffer._assetPaths[0], instancerBuffer._name);
					return;
				}
			}

			int numInstancesCreated = 0;
			int numInstances = instancerBuffer._instanceTransforms.Length;
			for (int i = 0; i < numInstances; ++i)
			{
				// Reset to the single asset for each instance allows which is null if using multi asset
				// therefore forcing the instance asset to be found
				GameObject unitySrcGO = singleAssetGO;

				if (unitySrcGO == null)
				{
					// If not using single asset, then there must be an asset path for each instance

					if (string.IsNullOrEmpty(instancerBuffer._assetPaths[i]))
					{
						continue;
					}

					if (!loadedUnityObjectMap.TryGetValue(instancerBuffer._assetPaths[i], out unitySrcGO))
					{
						// Try loading it
						//HEU_AssetDatabase.ImportAsset(instancerBuffer._assetPaths[i], HEU_AssetDatabase.HEU_ImportAssetOptions.Default);
						unitySrcGO = HEU_AssetDatabase.LoadAssetAtPath(instancerBuffer._assetPaths[i], typeof(GameObject)) as GameObject;

						if (unitySrcGO == null)
						{
							Debug.LogErrorFormat("Unable to load asset at {0} for instancing!", instancerBuffer._assetPaths[i]);

							// Even though the source Unity object is not found, we should create an object instance info to track it
							if (tempGO == null)
							{
								tempGO = new GameObject();
							}
							unitySrcGO = tempGO;
						}

						// Adding to map even if not found so we don't flood the log with the same error message
						loadedUnityObjectMap.Add(instancerBuffer._assetPaths[i], unitySrcGO);
					}
				}

				CreateNewInstanceFromObject(unitySrcGO, (numInstancesCreated + 1), instanceRootTransform, ref instancerBuffer._instanceTransforms[i],
					instancerBuffer._instancePrefixes, instancerBuffer._name);

				numInstancesCreated++;
			}

			if (tempGO != null)
			{
				HEU_GeneralUtility.DestroyImmediate(tempGO, bRegisterUndo: false);
			}
		}

		private void CreateNewInstanceFromObject(GameObject sourceObject, int instanceIndex, Transform parentTransform, 
			ref HAPI_Transform hapiTransform, string[] instancePrefixes, string instanceName)
		{
			GameObject newInstanceGO = null;

			if (HEU_EditorUtility.IsPrefabAsset(sourceObject))
			{
				newInstanceGO = HEU_EditorUtility.InstantiatePrefab(sourceObject) as GameObject;
				newInstanceGO.transform.parent = parentTransform;
			}
			else
			{
				newInstanceGO = HEU_EditorUtility.InstantiateGameObject(sourceObject, parentTransform, false, false);
			}

			// To get the instance output name, we pass in the instance index. The actual name will be +1 from this.
			newInstanceGO.name = HEU_GeometryUtility.GetInstanceOutputName(instanceName, instancePrefixes, instanceIndex);
			newInstanceGO.isStatic = sourceObject.isStatic;

			Transform instanceTransform = newInstanceGO.transform;
			HEU_HAPIUtility.ApplyLocalTransfromFromHoudiniToUnityForInstance(ref hapiTransform, instanceTransform);

			// When cloning, the instanced part might have been made invisible, so re-enable renderer to have the cloned instance display it.
			HEU_GeneralUtility.SetGameObjectRenderVisiblity(newInstanceGO, true);
			HEU_GeneralUtility.SetGameObjectChildrenRenderVisibility(newInstanceGO, true);
			HEU_GeneralUtility.SetGameObjectColliderState(newInstanceGO, true);
			HEU_GeneralUtility.SetGameObjectChildrenColliderState(newInstanceGO, true);
		}

		private void DestroyOutputs()
		{
			if (_generatedOutputs != null)
			{
				for (int i = 0; i < _generatedOutputs.Count; ++i)
				{
					HEU_GeneratedOutput.DestroyGeneratedOutput(_generatedOutputs[i]);
					_generatedOutputs[i] = null;
				}
				_generatedOutputs.Clear();
			}
		}

		private void SetOutputVisiblity(HEU_LoadBufferBase buffer)
		{
			bool bVisibility = !buffer._bInstanced;

			if (HEU_GeneratedOutput.HasLODGroup(buffer._generatedOutput))
			{
				foreach (HEU_GeneratedOutputData childOutput in buffer._generatedOutput._childOutputs)
				{
					HEU_GeneralUtility.SetGameObjectRenderVisiblity(childOutput._gameObject, bVisibility);
					HEU_GeneralUtility.SetGameObjectColliderState(childOutput._gameObject, bVisibility);
				}
			}
			else
			{
				HEU_GeneralUtility.SetGameObjectRenderVisiblity(buffer._generatedOutput._outputData._gameObject, bVisibility);
				HEU_GeneralUtility.SetGameObjectColliderState(buffer._generatedOutput._outputData._gameObject, bVisibility);
			}
		}

		public HEU_SessionBase GetHoudiniSession(bool bCreateIfNotFound)
		{
			HEU_SessionBase session = (_sessionID != HEU_SessionData.INVALID_SESSION_ID) ? HEU_SessionManager.GetSessionWithID(_sessionID) : null;
			
			if (session == null || !session.IsSessionValid())
			{
				if (bCreateIfNotFound)
				{
					session = HEU_SessionManager.GetOrCreateDefaultSession();
					if (session != null && session.IsSessionValid())
					{
						_sessionID = session.GetSessionData().SessionID;
					}
				}
			}

			return session;
		}

		public bool IsLoaded() { return _fileNodeID != HEU_Defines.HEU_INVALID_NODE_ID; }

		public HEU_GenerateOptions GenerateOptions { get { return _generateOptions; } }


		//	DATA ------------------------------------------------------------------------------------------------------

		public string _filePath = "";

		public string _logStr;

		private HEU_ThreadedTaskLoadGeo _loadGeo;

		protected bool _bSyncing;
		public bool IsSyncing { get { return _bSyncing; } }

		private HAPI_NodeId _fileNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

		[SerializeField]
		private long _sessionID = HEU_SessionData.INVALID_SESSION_ID;

		[SerializeField]
		private List<HEU_GeneratedOutput> _generatedOutputs = new List<HEU_GeneratedOutput>();

		// Asset Options
		[SerializeField]
		private HEU_GenerateOptions _generateOptions = new HEU_GenerateOptions();

		[SerializeField]
		private bool _initialized;
	}

	[System.Serializable]
	public struct HEU_GenerateOptions
	{
		public bool _generateUVs;
		public bool _generateTangents;
		public bool _generateNormals;
		public bool _useLODGroups;
		public bool _splitPoints;
	}

}   // HoudiniEngineUnity