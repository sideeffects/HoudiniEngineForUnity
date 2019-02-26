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
	using HAPI_StringHandle = System.Int32;
	using HAPI_NodeId = System.Int32;
	using HAPI_PDG_WorkitemId = System.Int32;
	using HAPI_PDG_GraphContextId = System.Int32;
	using HAPI_AssetLibraryId = System.Int32;


	/// <summary>
	/// Connects to an HDA containing TOP networks, queries TOP nodes, and keeps in sync.
	/// Also triggers automatically loading of generated geometry.
	/// </summary>
	[ExecuteInEditMode]
	public class HEU_PDGAssetLink : MonoBehaviour, ISerializationCallbackReceiver
	{
		private void Awake()
		{
			//Debug.Log("Awake");

			HandleInitialLoad();
		}

		public void OnBeforeSerialize()
		{
			
		}

		public void OnAfterDeserialize()
		{
			//Debug.Log("OnAfterDeserialize");

			HandleInitialLoad();
		}

		private void HandleInitialLoad()
		{
#if HOUDINIENGINEUNITY_ENABLED
			HEU_PDGSession pdgSession = HEU_PDGSession.GetPDGSession();
			if (pdgSession != null)
			{
				pdgSession.AddAsset(this);
			}

			if (_linkState != LinkState.INACTIVE)
			{
				// On load this, need to relink
				_assetID = HEU_Defines.HEU_INVALID_NODE_ID;
				_linkState = LinkState.INACTIVE;

				// UI will take care of refreshing
				//Refresh();
			}
#endif
		}

		private void OnDestroy()
		{
			HEU_PDGSession pdgSession = HEU_PDGSession.GetPDGSession();
			if (pdgSession != null)
			{
				pdgSession.RemoveAsset(this);
			}
		}

		public void Setup(HEU_HoudiniAsset hdaAsset)
		{
			_heu = hdaAsset;
			_assetGO = _heu.RootGameObject;
			_assetPath = _heu.AssetPath;
			_assetName = _heu.AssetName;

			Reset();
			Refresh();
		}

		private void NotifyAssetCooked(HEU_HoudiniAsset asset, bool bSuccess, List<GameObject> generatedOutputs)
		{
			//Debug.LogFormat("NotifyAssetCooked: {0} - {1} - {2}", asset.AssetName, bSuccess, _linkState);
			if (bSuccess)
			{
				if (_linkState == LinkState.LINKED)
				{
					if (_autoCook)
					{
						CookOutput();
					}
				}
				else
				{
					PopulateFromHDA();
				}
			}
			else
			{
				_linkState = LinkState.ERROR_NOT_LINKED;
			}
		}

		public void Reset()
		{
			ClearAllTOPData();
		}

		public void Refresh()
		{
			if (_heu == null)
			{
				_linkState = LinkState.ERROR_NOT_LINKED;
				_assetID = HEU_Defines.HEU_INVALID_NODE_ID;
				return;
			}

			if (!_heu.IsAssetValid())
			{
				_linkState = LinkState.INACTIVE;
				_assetID = HEU_Defines.HEU_INVALID_NODE_ID;
			}

			if (_linkState == LinkState.INACTIVE || _assetID == HEU_Defines.HEU_INVALID_NODE_ID)
			{
				_linkState = LinkState.LINKING;

				_heu._cookedEvent.RemoveListener(NotifyAssetCooked);
				_heu._cookedEvent.AddListener(NotifyAssetCooked);

				_heu._reloadEvent.RemoveListener(NotifyAssetCooked);
				_heu._reloadEvent.AddListener(NotifyAssetCooked);

				_heu.RequestCook(true, true, true, true);

				RepaintUI();
			}
			else
			{
				PopulateFromHDA();
			}
		}

		private void PopulateFromHDA()
		{
			if (!_heu.IsAssetValid())
			{
				_linkState = LinkState.ERROR_NOT_LINKED;
				return;
			}

			if (_heu != null)
			{
				_assetID = _heu.AssetID;
				_assetName = _heu.AssetName;
			}

			if (PopulateTOPNetworks())
			{
				_linkState = LinkState.LINKED;
			}
			else
			{
				_linkState = LinkState.ERROR_NOT_LINKED;
				Debug.LogErrorFormat("Failed to populate TOP network info for asset {0}!", _assetName);
			}

			RepaintUI();
		}

		public bool PopulateTOPNetworks()
		{
			HEU_SessionBase session = GetHAPISession();

			HAPI_NodeInfo assetInfo = new HAPI_NodeInfo();
			if (!session.GetNodeInfo(_assetID, ref assetInfo, true))
			{
				return false;
			}

			// Get all networks within the asset, recursively
			int nodeCount = 0;
			if (!session.ComposeChildNodeList(_assetID, (int)(HAPI_NodeType.HAPI_NODETYPE_ANY), (int)HAPI_NodeFlags.HAPI_NODEFLAGS_NETWORK, true, ref nodeCount))
			{
				return false;
			}

			HAPI_NodeId[] nodeIDs = new HAPI_NodeId[nodeCount];
			if (!session.GetComposedChildNodeList(_assetID, nodeIDs, nodeCount))
			{
				return false;
			}

			// Holds TOP networks in use
			List<HEU_TOPNetworkData> newNetworks = new List<HEU_TOPNetworkData>();

			// For each network, only add those with TOP child nodes (therefore guaranteeing only TOP networks are added).
			for (int t = 0; t < nodeCount; ++t)
			{
				HAPI_NodeInfo topNodeInfo = new HAPI_NodeInfo();
				if (!session.GetNodeInfo(nodeIDs[t], ref topNodeInfo))
				{
					return false;
				}

				string nodeName = HEU_SessionManager.GetString(topNodeInfo.nameSH, session);
				//Debug.LogFormat("Top node: {0} - {1}", nodeName, topNodeInfo.type);

				// Skip any non TOP or SOP networks
				if (topNodeInfo.type != HAPI_NodeType.HAPI_NODETYPE_TOP && topNodeInfo.type != HAPI_NodeType.HAPI_NODETYPE_SOP)
				{
					continue;
				}

				// Get list of all TOP nodes within this network.
				HAPI_NodeId[] topNodeIDs = null;
				if (!HEU_SessionManager.GetComposedChildNodeList(session, nodeIDs[t], (int)(HAPI_NodeType.HAPI_NODETYPE_TOP), (int)HAPI_NodeFlags.HAPI_NODEFLAGS_TOP_NONSCHEDULER, true, out topNodeIDs))
				{
					continue;
				}

				// Skip networks without TOP nodes
				if (topNodeIDs == null || topNodeIDs.Length == 0)
				{
					continue;
				}

				TOPNodeTags tags = new TOPNodeTags();
				if (_useHEngineData)
				{
					ParseHEngineData(session, nodeIDs[t], ref topNodeInfo, ref tags);

					if (!tags._show)
					{
						continue;
					}
				}
				else
				{
					tags._show = true;
				}

				HEU_TOPNetworkData topNetworkData = GetTOPNetworkByName(nodeName, _topNetworks);
				if (topNetworkData == null)
				{
					topNetworkData = new HEU_TOPNetworkData();
				}
				else
				{
					// Found previous TOP network, so remove it from old list. This makes
					// sure to not remove it when cleaning up old nodes.
					_topNetworks.Remove(topNetworkData);
				}

				newNetworks.Add(topNetworkData);

				topNetworkData._nodeID = nodeIDs[t];
				topNetworkData._nodeName = nodeName;
				topNetworkData._parentName = _assetName;
				topNetworkData._tags = tags;

				PopulateTOPNodes(session, topNetworkData._nodeID, topNetworkData, topNodeIDs, _useHEngineData);
			}

			// Clear old TOP networks and nodes
			ClearAllTOPData();
			_topNetworks = newNetworks;

			// Update latest TOP network names
			_topNetworkNames = new string[_topNetworks.Count];
			for(int i = 0; i < _topNetworks.Count; ++i)
			{
				_topNetworkNames[i] = _topNetworks[i]._nodeName;
			}

			return true;
		}

		public static bool PopulateTOPNodes(HEU_SessionBase session, HAPI_NodeId parentNodeID, HEU_TOPNetworkData topNetwork, HAPI_NodeId[] topNodeIDs, bool useHEngineData)
		{
			// Holds list of found TOP nodes
			List<HEU_TOPNodeData> newNodes = new List<HEU_TOPNodeData>();

			foreach(HAPI_NodeId topNodeID in topNodeIDs)
			{
				// Not necessary. Blocks main thread.
				//session.CookNode(childNodeID, HEU_PluginSettings.CookTemplatedGeos);

				HAPI_NodeInfo childNodeInfo = new HAPI_NodeInfo();
				if (!session.GetNodeInfo(topNodeID, ref childNodeInfo))
				{
					return false;
				}

				string nodeName = HEU_SessionManager.GetString(childNodeInfo.nameSH, session);
				//Debug.LogFormat("TOP Node: name={0}, type={1}", nodeName, childNodeInfo.type);

				TOPNodeTags tags = new TOPNodeTags();
				if (useHEngineData)
				{
					ParseHEngineData(session, topNodeID, ref childNodeInfo, ref tags);

					if (!tags._show)
					{
						continue;
					}
				}
				else
				{
					tags._show = true;
				}

				HEU_TOPNodeData topNodeData = GetTOPNodeByName(nodeName, topNetwork._topNodes);
				if (topNodeData == null)
				{
					topNodeData = new HEU_TOPNodeData();
				}
				else
				{
					topNetwork._topNodes.Remove(topNodeData);
				}

				newNodes.Add(topNodeData);

				//topNodeData.Reset();
				topNodeData._nodeID = topNodeID;
				topNodeData._nodeName = nodeName;
				topNodeData._parentName = topNetwork._parentName + "_" + topNetwork._nodeName;
				topNodeData._tags = tags;
			}

			// Clear old unused TOP nodes
			for (int i = 0; i < topNetwork._topNodes.Count; ++i)
			{
				ClearTOPNodeWorkItemResults(topNetwork._topNodes[i]);
			}
			topNetwork._topNodes = newNodes;

			// Get list of updated TOP node names
			topNetwork._topNodeNames = new string[topNetwork._topNodes.Count];
			for (int i = 0; i < topNetwork._topNodes.Count; ++i)
			{
				topNetwork._topNodeNames[i] = topNetwork._topNodes[i]._nodeName;
			}

			return true;
		}

		public void SelectTOPNetwork(int newIndex)
		{
			if (newIndex < 0 || newIndex >= _topNetworks.Count)
			{
				return;
			}

			_selectedTOPNetwork = newIndex;
		}

		public void SelectTOPNode(HEU_TOPNetworkData network, int newIndex)
		{
			if (newIndex < 0 || newIndex >= network._topNodes.Count)
			{
				return;
			}

			network._selectedTOPIndex = newIndex;
		}

		public HEU_TOPNetworkData GetSelectedTOPNetwork()
		{
			return GetTOPNetwork(_selectedTOPNetwork);
		}

		public HEU_TOPNodeData GetSelectedTOPNode()
		{
			HEU_TOPNetworkData topNetwork = GetTOPNetwork(_selectedTOPNetwork);
			if (topNetwork != null)
			{
				if (topNetwork._selectedTOPIndex >= 0 && topNetwork._selectedTOPIndex < topNetwork._topNodes.Count)
				{
					return topNetwork._topNodes[topNetwork._selectedTOPIndex];
				}
			}
			return null;
		}

		public HEU_TOPNetworkData GetTOPNetwork(int index)
		{
			if (index >= 0 && index < _topNetworks.Count)
			{
				return _topNetworks[index];
			}
			return null;
		}

		public static HEU_TOPNetworkData GetTOPNetworkByName(string name, List<HEU_TOPNetworkData> topNetworks)
		{
			for(int i = 0; i < topNetworks.Count; ++i)
			{
				if (topNetworks[i]._nodeName.Equals(name))
				{
					return topNetworks[i];
				}
			}
			return null;
		}

		public static HEU_TOPNodeData GetTOPNodeByName(string name, List<HEU_TOPNodeData> topNodes)
		{
			for (int i = 0; i < topNodes.Count; ++i)
			{
				if (topNodes[i]._nodeName.Equals(name))
				{
					return topNodes[i];
				}
			}
			return null;
		}

		private void ClearAllTOPData()
		{
			// Clears all TOP data

			foreach(HEU_TOPNetworkData network in _topNetworks)
			{
				foreach(HEU_TOPNodeData node in network._topNodes)
				{
					ClearTOPNodeWorkItemResults(node);
				}
			}

			_topNetworks.Clear();
			_topNetworkNames = new string[0];
		}

		private static void ClearTOPNetworkWorkItemResults(HEU_TOPNetworkData topNetwork)
		{
			foreach (HEU_TOPNodeData node in topNetwork._topNodes)
			{
				ClearTOPNodeWorkItemResults(node);
			}
		}

		private static void ClearTOPNodeWorkItemResults(HEU_TOPNodeData topNode)
		{
			int numResults = topNode._workResults.Count;
			for (int i = 0; i < numResults; ++i)
			{
				DestroyWorkItemResultData(topNode, topNode._workResults[i]);
			}
			topNode._workResults.Clear();

			if (topNode._workResultParentGO != null)
			{
				HEU_GeneralUtility.DestroyImmediate(topNode._workResultParentGO);
			}
		}

		private static void ClearWorkItemResult(HEU_TOPNodeData topNode, int workItemIndex)
		{
			HEU_TOPWorkResult result = GetWorkResult(topNode, workItemIndex);
			if (result != null)
			{
				DestroyWorkItemResultData(topNode, result);

				topNode._workResults.Remove(result);
			}
		}

		public void UpdateTOPNodeResultsVisibility(HEU_TOPNodeData topNode)
		{
			if (topNode._workResultParentGO != null)
			{
				topNode._workResultParentGO.SetActive(topNode._showResults);
			}
		}

		private static HEU_TOPWorkResult GetWorkResult(HEU_TOPNodeData topNode, int workItemIndex)
		{
			HEU_TOPWorkResult result = null;
			foreach (HEU_TOPWorkResult res in topNode._workResults)
			{
				if (res._workItemIndex == workItemIndex)
				{
					result = res;
					break;
				}
			}
			return result;
		}

		private static void DestroyWorkItemResultData(HEU_TOPNodeData topNode, HEU_TOPWorkResult result)
		{
			if (result._generatedGOs != null)
			{
				int numGOs = result._generatedGOs.Count;
				for (int i = 0; i < numGOs; ++i)
				{
					HEU_GeoSync geoSync = result._generatedGOs[i].GetComponent<HEU_GeoSync>();
					if (geoSync != null)
					{
						geoSync.Unload();
					}

					HEU_GeneralUtility.DestroyImmediate(result._generatedGOs[i]);
					result._generatedGOs[i] = null;
				}

				result._generatedGOs.Clear();
			}
		}

		public void DirtyTOPNode(HEU_TOPNodeData topNode)
		{
			HEU_SessionBase session = GetHAPISession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			if (!session.DirtyPDGNode(topNode._nodeID, true))
			{
				Debug.LogErrorFormat("Dirty node failed!");
				return;
			}

			ClearTOPNodeWorkItemResults(topNode);
		}

		public void CookTOPNode(HEU_TOPNodeData topNode)
		{
			HEU_SessionBase session = GetHAPISession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			if (!session.CookPDG(topNode._nodeID, 0, 0))
			{
				Debug.LogErrorFormat("Cook node failed!");
			}
		}

		public void DirtyAll()
		{
			HEU_SessionBase session = GetHAPISession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			HEU_TOPNetworkData topNetwork = GetSelectedTOPNetwork();
			if (topNetwork != null)
			{
				if (!session.DirtyPDGNode(topNetwork._nodeID, true))
				{
					Debug.LogErrorFormat("Dirty node failed!");
					return;
				}

				ClearTOPNetworkWorkItemResults(topNetwork);
			}
		}

		public void CookOutput()
		{
			HEU_SessionBase session = GetHAPISession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			HEU_TOPNetworkData topNetwork = GetSelectedTOPNetwork();
			if (topNetwork != null)
			{
				//Debug.Log("Cooking output!");

				_workItemTally.ZeroAll();
				ResetTOPNetworkWorkItemTally(topNetwork);

				HEU_PDGSession pdgSession = HEU_PDGSession.GetPDGSession();
				if (pdgSession != null)
				{
					pdgSession.CookTOPNetworkOutputNode(topNetwork);
				}
			}
		}

		public void PauseCook()
		{
			HEU_SessionBase session = GetHAPISession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			HEU_TOPNetworkData topNetwork = GetSelectedTOPNetwork();
			if (topNetwork != null)
			{
				//Debug.Log("Cooking output!");

				_workItemTally.ZeroAll();
				ResetTOPNetworkWorkItemTally(topNetwork);

				HEU_PDGSession pdgSession = HEU_PDGSession.GetPDGSession();
				if (pdgSession != null)
				{
					pdgSession.PauseCook(topNetwork);
				}
			}
		}

		public void CancelCook()
		{
			HEU_SessionBase session = GetHAPISession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			HEU_TOPNetworkData topNetwork = GetSelectedTOPNetwork();
			if (topNetwork != null)
			{
				//Debug.Log("Cooking output!");

				_workItemTally.ZeroAll();
				ResetTOPNetworkWorkItemTally(topNetwork);

				HEU_PDGSession pdgSession = HEU_PDGSession.GetPDGSession();
				if (pdgSession != null)
				{
					pdgSession.CancelCook(topNetwork);
				}
			}
		}

		public HEU_SessionBase GetHAPISession()
		{
			return _heu != null ? _heu.GetAssetSession(true) : null;
		}

		public void LoadResults(HEU_SessionBase session, HEU_TOPNodeData topNode, HAPI_PDG_WorkitemInfo workItemInfo, HAPI_PDG_WorkitemResultInfo[] resultInfos)
		{
			// Create HEU_GeoSync objects, set results, and sync it

			string workItemName = HEU_SessionManager.GetString(workItemInfo.nameSH, session);
			//Debug.LogFormat("Work item: {0}:: name={1}, results={2}", workItemInfo.index, workItemName, workItemInfo.numResults);

			// Clear previously generated result
			ClearWorkItemResult(topNode, workItemInfo.index);

			if (resultInfos == null || resultInfos.Length == 0)
			{
				return;
			}

			HEU_TOPWorkResult result = GetWorkResult(topNode, workItemInfo.index);
			if (result == null)
			{
				result = new HEU_TOPWorkResult();
				result._workItemIndex = workItemInfo.index;

				topNode._workResults.Add(result);
			}

			int numResults = resultInfos.Length;
			for (int i = 0; i < numResults; ++i)
			{
				if (resultInfos[i].resultTagSH <= 0 || resultInfos[i].resultSH <= 0)
				{
					continue;
				}

				string tag = HEU_SessionManager.GetString(resultInfos[i].resultTagSH, session);
				string path = HEU_SessionManager.GetString(resultInfos[i].resultSH, session);

				//Debug.LogFormat("Result for work item {0}: result={1}, tag={2}, path={3}", result._workItemIndex, i, tag, path);

				if (string.IsNullOrEmpty(tag) || !tag.StartsWith("file"))
				{
					continue;
				}

				string name = string.Format("{0}_{1}_{2}",
					topNode._parentName,
					workItemName,
					workItemInfo.index);

				// Get or create parent GO
				if (topNode._workResultParentGO == null)
				{
					topNode._workResultParentGO = new GameObject(topNode._nodeName);
					HEU_GeneralUtility.SetParentWithCleanTransform(GetLoadRootTransform(), topNode._workResultParentGO.transform);
					topNode._workResultParentGO.SetActive(topNode._showResults);
				}

				GameObject newGO = new GameObject(name);
				HEU_GeneralUtility.SetParentWithCleanTransform(topNode._workResultParentGO.transform, newGO.transform);

				result._generatedGOs.Add(newGO);

				HEU_GeoSync geoSync = newGO.AddComponent<HEU_GeoSync>();
				if (geoSync != null)
				{
					geoSync._filePath = path;
					geoSync.StartSync();
				}
			}
		}

		private Transform GetLoadRootTransform()
		{
			if (_loadRootGameObject == null)
			{
				_loadRootGameObject = new GameObject(_assetName + "_OUTPUTS");
			}
			return _loadRootGameObject.transform;
		}

		public HEU_TOPNodeData GetTOPNode(HAPI_NodeId nodeID)
		{
			int numNetworks = _topNetworks.Count;
			for(int i = 0; i < numNetworks; ++i)
			{
				int numNodes = _topNetworks[i]._topNodes.Count;
				for (int j = 0; j < numNodes; ++j)
				{
					if (_topNetworks[i]._topNodes[j]._nodeID == nodeID)
					{
						return _topNetworks[i]._topNodes[j];
					}
				}
			} 
			return null;
		}

		public void RepaintUI()
		{
			if (_repaintUIDelegate != null)
			{
				_repaintUIDelegate();
			}
		}

		public void UpdateWorkItemTally()
		{
			_workItemTally.ZeroAll();

			int numNetworks = _topNetworks.Count;
			for (int i = 0; i < numNetworks; ++i)
			{
				int numNodes = _topNetworks[i]._topNodes.Count;
				for (int j = 0; j < numNodes; ++j)
				{
					_workItemTally._totalWorkItems += _topNetworks[i]._topNodes[j]._workItemTally._totalWorkItems;
					_workItemTally._waitingWorkItems += _topNetworks[i]._topNodes[j]._workItemTally._waitingWorkItems;
					_workItemTally._scheduledWorkItems += _topNetworks[i]._topNodes[j]._workItemTally._scheduledWorkItems;
					_workItemTally._cookingWorkItems += _topNetworks[i]._topNodes[j]._workItemTally._cookingWorkItems;
					_workItemTally._cookedWorkItems += _topNetworks[i]._topNodes[j]._workItemTally._cookedWorkItems;
					_workItemTally._erroredWorkItems += _topNetworks[i]._topNodes[j]._workItemTally._erroredWorkItems;
				}
			}
		}

		public void ResetTOPNetworkWorkItemTally(HEU_TOPNetworkData topNetwork)
		{
			if (topNetwork != null)
			{
				int numNodes = topNetwork._topNodes.Count;
				for(int i = 0; i < numNodes; ++i)
				{
					topNetwork._topNodes[i]._workItemTally.ZeroAll();
				}
			}
		}

		public string GetTOPNodeStatus(HEU_TOPNodeData topNode)
		{
			if (topNode._pdgState == HEU_TOPNodeData.PDGState.COOK_FAILED || topNode.AnyWorkItemsFailed())
			{
				return "Cook Failed";
			}
			else if(topNode._pdgState == HEU_TOPNodeData.PDGState.COOK_COMPLETE)
			{
				return "Cook Completed";
			}
			else if (topNode._pdgState == HEU_TOPNodeData.PDGState.COOKING)
			{
				return "Cook In Progress";
			}
			else if (topNode._pdgState == HEU_TOPNodeData.PDGState.DIRTIED)
			{
				return "Dirtied";
			}
			else if (topNode._pdgState == HEU_TOPNodeData.PDGState.DIRTYING)
			{
				return "Dirtying";
			}

			return "";
		}

		//	MENU ----------------------------------------------------------------------------------------------------
#if UNITY_EDITOR
		//[MenuItem("PDG/Create PDG Asset Link", false, 0)]
		[MenuItem(HEU_Defines.HEU_PRODUCT_NAME + "/PDG/Create PDG Asset Link", false, 100)]
		public static void CreatePDGAssetLink()
		{
			GameObject selectedGO = Selection.activeGameObject;
			if (selectedGO != null)
			{
				HEU_HoudiniAssetRoot assetRoot = selectedGO.GetComponent<HEU_HoudiniAssetRoot>();
				if (assetRoot != null)
				{
					if (assetRoot._houdiniAsset != null)
					{
						string name = string.Format("{0}_PDGLink", assetRoot._houdiniAsset.AssetName);

						GameObject go = new GameObject(name);
						HEU_PDGAssetLink assetLink = go.AddComponent<HEU_PDGAssetLink>();
						assetLink.Setup(assetRoot._houdiniAsset);

						Selection.activeGameObject = go;
					}
					else
					{
						Debug.LogError("Selected gameobject is not an instantiated HDA. Failed to create PDG Asset Link.");
					}
				}
				else
				{
					Debug.LogError("Selected gameobject is not an instantiated HDA. Failed to create PDG Asset Link.");
				}
			}
			else
			{
				//Debug.LogError("Nothing selected. Select an instantiated HDA first.");
				HEU_EditorUtility.DisplayErrorDialog("PDG Asset Link", "No HDA selected. You must select an instantiated HDA first.", "OK");
			}
		}

		private static void ParseHEngineData(HEU_SessionBase session, HAPI_NodeId topNodeID, ref HAPI_NodeInfo nodeInfo, ref TOPNodeTags nodeTags)
		{
			// Turn off session logging error when querying string parm that might not be there
			bool bLogError = session.LogErrorOverride;
			session.LogErrorOverride = false;

			int numStrings = nodeInfo.parmStringValueCount;
			HAPI_StringHandle henginedatash = 0;
			if (numStrings > 0 && session.GetParamStringValue(topNodeID, "henginedata", 0, out henginedatash))
			{
				string henginedatastr = HEU_SessionManager.GetString(henginedatash, session);
				//Debug.Log("HEngine data: " + henginedatastr);

				if (!string.IsNullOrEmpty(henginedatastr))
				{
					string[] tags = henginedatastr.Split(',');
					if (tags != null && tags.Length > 0)
					{
						foreach (string t in tags)
						{
							if (t.Equals("show"))
							{
								nodeTags._show = true;
							}
							else if (t.Equals("autoload"))
							{
								nodeTags._autoload = true;
							}
						}
					}
				}
			}

			// Logging error back on
			session.LogErrorOverride = bLogError;
		}


#endif

		//	DATA ------------------------------------------------------------------------------------------------------

#pragma warning disable 0414
		[SerializeField]
		private string _assetPath;

		[SerializeField]
		private GameObject _assetGO;
#pragma warning restore 0414

		[SerializeField]
		private string _assetName;

		public string AssetName { get { return _assetName; } }

		[SerializeField]
		private HAPI_NodeId _assetID = HEU_Defines.HEU_INVALID_NODE_ID;

		[SerializeField]
		private HEU_HoudiniAsset _heu;

		[SerializeField]
		private List<HEU_TOPNetworkData> _topNetworks = new List<HEU_TOPNetworkData>();

		public string[] _topNetworkNames = new string[0];

		[SerializeField]
		private int _selectedTOPNetwork;

		public int SelectedTOPNetwork { get { return _selectedTOPNetwork; } }

		[SerializeField]
		private LinkState _linkState = LinkState.INACTIVE;

		public LinkState AssetLinkState { get { return _linkState; } }

		public enum LinkState
		{
			INACTIVE,
			LINKING,
			LINKED,
			ERROR_NOT_LINKED
		}

		public bool _autoCook;

		public bool _useHEngineData = false;

		// Delegate for Editor window to hook into for callback when needing updating
		public delegate void UpdateUIDelegate();
		public UpdateUIDelegate _repaintUIDelegate;

		private int _numWorkItems;

		public HEU_WorkItemTally _workItemTally = new HEU_WorkItemTally();

		// The root gameobject to place all loaded geometry under
		public GameObject _loadRootGameObject;
	}

}   // HoudiniEngineUnity