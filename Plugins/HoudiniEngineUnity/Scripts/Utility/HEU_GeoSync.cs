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

		public void StartSync()
		{
			if (_bSyncing)
			{
				return;
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
			DestroyGameObjects();

			_logStr = "Unloaded!";
		}

		public void OnLoadComplete(HEU_ThreadedTaskLoadGeo.HEU_LoadData loadData)
		{
			_bSyncing = false;

			_logStr = loadData._logStr;
			_fileNodeID = loadData._fileNodeID;

			if (loadData._loadStatus == HEU_ThreadedTaskLoadGeo.HEU_LoadData.LoadStatus.SUCCESS)
			{
				if (loadData._terrainTiles != null && loadData._terrainTiles.Count > 0)
				{
					GenerateTerrain(loadData._terrainTiles);
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

		private void GenerateTerrain(List<HEU_ThreadedTaskLoadGeo.HEU_LoadVolumeTerrainTile> terrainTiles)
		{
			if (_gameObjects == null)
			{
				_gameObjects = new List<GameObject>();
			}
			else
			{
				DestroyGameObjects();
			}

			Transform parent = this.gameObject.transform;

			int numTiles = terrainTiles.Count;
			for(int t = 0; t < numTiles; ++t)
			{
				if (terrainTiles[t]._heightMap != null)
				{
					GameObject go = new GameObject("heightfield_" + terrainTiles[t]._tileIndex);
					Transform goTransform = go.transform;
					goTransform.parent = parent;
					_gameObjects.Add(go);

					Terrain terrain = HEU_GeneralUtility.GetOrCreateComponent<Terrain>(go);
					TerrainCollider collider = HEU_GeneralUtility.GetOrCreateComponent<TerrainCollider>(go);

					terrain.terrainData = new TerrainData();
					TerrainData terrainData = terrain.terrainData;
					collider.terrainData = terrainData;

					int heightMapSize = terrainTiles[t]._heightMapSize;

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

					terrainData.SetHeights(0, 0, terrainTiles[t]._heightMap);

					terrainData.size = new Vector3(terrainTiles[t]._terrainSizeX, terrainTiles[t]._heightRange, terrainTiles[t]._terrainSizeY);

					terrain.Flush();

					// Set position
					HAPI_Transform hapiTransformVolume = new HAPI_Transform(true);
					hapiTransformVolume.position[0] += terrainTiles[t]._position[0];
					hapiTransformVolume.position[1] += terrainTiles[t]._position[1];
					hapiTransformVolume.position[2] += terrainTiles[t]._position[2];
					HEU_HAPIUtility.ApplyLocalTransfromFromHoudiniToUnity(ref hapiTransformVolume, goTransform);

					// Set layers
					Texture2D defaultTexture = HEU_VolumeCache.LoadDefaultSplatTexture();
					int numLayers = terrainTiles[t]._layers.Count;

#if UNITY_2018_3_OR_NEWER

					// Create TerrainLayer for each heightfield layer
					// Note that at time of this implementation the new Unity terrain
					// is still in beta. Therefore, the following layer creation is subject
					// to change.

					TerrainLayer[] terrainLayers = new TerrainLayer[numLayers];
					for (int m = 0; m < numLayers; ++m)
					{
						terrainLayers[m] = new TerrainLayer();

						HEU_ThreadedTaskLoadGeo.HEU_LoadVolumeLayer layer = terrainTiles[t]._layers[m];

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

						HEU_ThreadedTaskLoadGeo.HEU_LoadVolumeLayer layer = terrainTiles[t]._layers[m];

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

					terrainData.SetAlphamaps(0, 0, terrainTiles[t]._splatMaps);

					//string assetPath = HEU_AssetDatabase.CreateAssetCacheFolder("terrainData");
					//AssetDatabase.CreateAsset(terrainData, assetPath);
					//Debug.Log("Created asset data at " + assetPath);
				}
			}
		}

		private void DestroyGameObjects()
		{
			if (_gameObjects != null)
			{
				for(int i = 0; i < _gameObjects.Count; ++i)
				{
					HEU_GeneralUtility.DestroyImmediate(_gameObjects[i]);
				}
				_gameObjects.Clear();
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
		private List<GameObject> _gameObjects;
	}


}   // HoudiniEngineUnity