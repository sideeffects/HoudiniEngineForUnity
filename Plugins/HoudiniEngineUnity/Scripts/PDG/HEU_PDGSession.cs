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
	using HAPI_SessionId = System.Int64;


	public class HEU_PDGSession
	{
		public static HEU_PDGSession GetPDGSession()
		{
			if (_pdgSession == null)
			{
				_pdgSession = new HEU_PDGSession();
			}
			return _pdgSession;
		}

		public HEU_PDGSession()
		{
#if UNITY_EDITOR && HOUDINIENGINEUNITY_ENABLED
			EditorApplication.update += Update;
#endif
		}

		public void AddAsset(HEU_PDGAssetLink asset)
		{
#if UNITY_EDITOR && HOUDINIENGINEUNITY_ENABLED
			if (!_pdgAssets.Contains(asset))
			{
				_pdgAssets.Add(asset);
				//Debug.Log("Adding asset " + asset.AssetName + " with total " + _pdgAssets.Count);
			}
#endif
		}

		public void RemoveAsset(HEU_PDGAssetLink asset)
		{
#if UNITY_EDITOR && HOUDINIENGINEUNITY_ENABLED
			// Setting the asset reference to null and removing
			// later in Update in case of removing while iterating the list
			int index = _pdgAssets.IndexOf(asset);
			if (index >= 0)
			{
				_pdgAssets[index] = null;
			}
#endif
		}

		void Update()
		{
#if UNITY_EDITOR && HOUDINIENGINEUNITY_ENABLED
			CleanUp();

			UpdatePDGContext();
#endif
		}

		private void CleanUp()
		{
			for (int i = 0; i < _pdgAssets.Count; ++i)
			{
				if (_pdgAssets[i] == null)
				{
					_pdgAssets.RemoveAt(i);
					i--;
				}
			}
		}

		private void UpdatePDGContext()
		{
			HEU_SessionBase session = GetHAPIPDGSession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			ReinitializePDGContext();

			// Get next set of events for each graph context
			if (_pdgContextIDs != null)
			{
				foreach (HAPI_PDG_GraphContextId contextID in _pdgContextIDs)
				{
					int pdgStateInt;
					if (!session.GetPDGState(contextID, out pdgStateInt))
					{
						SetErrorState("Failed to get PDG state", true);
						continue;
					}

					_pdgState = (HAPI_PDG_State)pdgStateInt;


					if (_pdgQueryEvents == null || _pdgQueryEvents.Length != _pdgMaxProcessEvents)
					{
						_pdgQueryEvents = new HAPI_PDG_EventInfo[_pdgMaxProcessEvents];
					}

					for (int i = 0; i < _pdgQueryEvents.Length; ++i)
					{
						ResetPDGEventInfo(ref _pdgQueryEvents[i]);
					}

					int eventCount = 0;
					int remainingCount = 0;
					if (!session.GetPDGEvents(contextID, _pdgQueryEvents, _pdgMaxProcessEvents, out eventCount, out remainingCount))
					{
						SetErrorState("Failed to get PDG events", true);
						continue; 
					}

					for (int i = 0; i < eventCount; ++i)
					{
						ProcessPDGEvent(session, contextID, ref _pdgQueryEvents[i]);
					}
					
				}
			}
		}

		public void ReinitializePDGContext()
		{
			HEU_SessionBase session = GetHAPIPDGSession();
			if (session == null || !session.IsSessionValid())
			{
				_pdgContextIDs = null;
				return;
			}

			int numContexts = 0;
			HAPI_StringHandle[] contextNames = new HAPI_StringHandle[_pdgContextSize];
			HAPI_PDG_GraphContextId[] contextIDs = new HAPI_PDG_GraphContextId[_pdgContextSize];
			if (!session.GetPDGGraphContexts(out numContexts, contextNames, contextIDs, _pdgContextSize) || numContexts <= 0)
			{
				_pdgContextIDs = null;
				return;
			}

			if (_pdgContextIDs == null || numContexts != _pdgContextIDs.Length)
			{
				_pdgContextIDs = new HAPI_PDG_GraphContextId[numContexts];
			}

			for (int i = 0; i < numContexts; ++i)
			{
				_pdgContextIDs[i] = contextIDs[i];
				//Debug.LogFormat("PDG Context: {0} - {1}", HEU_SessionManager.GetString(contextNames[i], session), contextIDs[i]);
			}
		}

		private void ProcessPDGEvent(HEU_SessionBase session, HAPI_PDG_GraphContextId contextID, ref HAPI_PDG_EventInfo eventInfo)
		{
			HEU_PDGAssetLink assetLink = null;
			HEU_TOPNodeData topNode = null;

			HAPI_PDG_EventType evType = (HAPI_PDG_EventType)eventInfo.eventType;
			HAPI_PDG_WorkitemState currentState = (HAPI_PDG_WorkitemState)eventInfo.currentState;
			HAPI_PDG_WorkitemState lastState = (HAPI_PDG_WorkitemState)eventInfo.lastState;

			GetTOPAssetLinkAndNode(eventInfo.nodeId, out assetLink, out topNode);

			//string topNodeName = topNode != null ? string.Format("node={0}", topNode._nodeName) : string.Format("id={0}", eventInfo.nodeId);
			//Debug.LogFormat("PDG Event: {0}, type={1}, workitem={2}, curState={3}, lastState={4}", topNodeName, evType.ToString(), 
			//	eventInfo.workitemId, currentState, lastState);

			if (assetLink == null || topNode == null || topNode._nodeID != eventInfo.nodeId)
			{
				return;
			}

			if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_NULL)
			{
				SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.NONE);
			}
			else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_NODE_CLEAR)
			{
				NotifyTOPNodePDGStateClear(assetLink, topNode);
			}
			else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_DIRTY_START)
			{
				SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.DIRTYING);

				//HEU_PDGAssetLink.ClearTOPNodeWorkItemResults(topNode);
			}
			else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_DIRTY_STOP)
			{
				SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.DIRTIED);
			}
			else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_COOK_ERROR)
			{
				SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.COOK_FAILED);
			}
			else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_COOK_WARNING)
			{

			}
			else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_COOK_COMPLETE)
			{
				SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.COOK_COMPLETE);
			}
			else 
			{
				// Work item events

				HEU_TOPNodeData.PDGState currentTOPPDGState = topNode._pdgState;

				if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_WORKITEM_ADD)
				{
					NotifyTOPNodeTotalWorkItem(assetLink, topNode, 1);
				}
				else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_WORKITEM_REMOVE)
				{
					NotifyTOPNodeTotalWorkItem(assetLink, topNode, -1);
				}
				else if (evType == HAPI_PDG_EventType.HAPI_PDG_EVENT_WORKITEM_STATE_CHANGE)
				{
					// Last states
					if (lastState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_WAITING && currentState != HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_WAITING)
					{
						NotifyTOPNodeWaitingWorkItem(assetLink, topNode, -1);
					}
					else if (lastState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_COOKING && currentState != HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_COOKING)
					{
						NotifyTOPNodeCookingWorkItem(assetLink, topNode, -1);
					}
					else if (lastState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_SCHEDULED && currentState != HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_SCHEDULED)
					{
						NotifyTOPNodeScheduledWorkItem(assetLink, topNode, -1);
					}

					// New states
					if (currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_WAITING)
					{
						NotifyTOPNodeWaitingWorkItem(assetLink, topNode, 1);
					}
					else if(currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_UNCOOKED)
					{

					}
					else if (currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_DIRTY)
					{
						//Debug.LogFormat("Dirty: id={0}", eventInfo.workitemId);

						ClearWorkItemResult(session, contextID, eventInfo, topNode);
					}
					else if (currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_SCHEDULED)
					{
						NotifyTOPNodeScheduledWorkItem(assetLink, topNode, 1);
					}
					else if(currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_COOKING)
					{
						NotifyTOPNodeCookingWorkItem(assetLink, topNode, 1);
					}
					else if (currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_COOKED_SUCCESS)
					{
						NotifyTOPNodeCookedWorkItem(assetLink, topNode);

						if (topNode._tags._autoload)
						{
							HAPI_PDG_WorkitemInfo workItemInfo = new HAPI_PDG_WorkitemInfo();
							if (!session.GetWorkItemInfo(contextID, eventInfo.workitemId, ref workItemInfo))
							{
								Debug.LogErrorFormat("Failed to get work item {1} info for {0}", topNode._nodeName, eventInfo.workitemId);
								return;
							}

							if (workItemInfo.numResults > 0)
							{
								HAPI_PDG_WorkitemResultInfo[] resultInfos = new HAPI_PDG_WorkitemResultInfo[workItemInfo.numResults];
								int resultCount = workItemInfo.numResults;
								if (!session.GetWorkitemResultInfo(topNode._nodeID, eventInfo.workitemId, resultInfos, resultCount))
								{
									Debug.LogErrorFormat("Failed to get work item {1} result info for {0}", topNode._nodeName, eventInfo.workitemId);
									return;
								}

								assetLink.LoadResults(session, topNode, workItemInfo, resultInfos, eventInfo.workitemId);
							}
						}
					}
					else if(currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_COOKED_FAIL)
					{
						NotifyTOPNodeErrorWorkItem(assetLink, topNode);
					}
					else if(currentState == HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_COOKED_CANCEL)
					{
						// Ignore it because in-progress cooks can be cancelled when automatically recooking graph
					}
				}

				if (currentTOPPDGState == HEU_TOPNodeData.PDGState.COOKING)
				{
					if (topNode.AreAllWorkItemsComplete())
					{
						if (topNode.AnyWorkItemsFailed())
						{
							SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.COOK_FAILED);
						}
						else
						{
							SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.COOK_COMPLETE);
						}
					}
				}
				else if(topNode.AnyWorkItemsPending())
				{
					SetTOPNodePDGState(assetLink, topNode, HEU_TOPNodeData.PDGState.COOKING);
				}
			}
		}

		private bool GetTOPAssetLinkAndNode(HAPI_NodeId nodeID, out HEU_PDGAssetLink assetLink, out HEU_TOPNodeData topNode)
		{
			assetLink = null;
			topNode = null;
			int numAssets = _pdgAssets.Count;
			for (int i = 0; i < numAssets; ++i)
			{
				topNode = _pdgAssets[i].GetTOPNode(nodeID);
				if (topNode != null)
				{
					assetLink = _pdgAssets[i];
					return true;
				}
			}
			return false;
		}

		private void SetTOPNodePDGState(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode, HEU_TOPNodeData.PDGState pdgState)
		{
			topNode._pdgState = pdgState;
			assetLink.RepaintUI();
		}

		private void NotifyTOPNodePDGStateClear(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode)
		{
			//Debug.LogFormat("NotifyTOPNodePDGStateClear:: {0}", topNode._nodeName);
			topNode._pdgState = HEU_TOPNodeData.PDGState.NONE;
			topNode._workItemTally.ZeroAll();
			assetLink.RepaintUI();
		}

		private void NotifyTOPNodeTotalWorkItem(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode, int inc)
		{
			topNode._workItemTally._totalWorkItems = Mathf.Max(topNode._workItemTally._totalWorkItems + inc, 0);
			assetLink.RepaintUI();
		}

		private void NotifyTOPNodeCookedWorkItem(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode)
		{
			topNode._workItemTally._cookedWorkItems++;
			assetLink.RepaintUI();
		}

		private void NotifyTOPNodeErrorWorkItem(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode)
		{
			topNode._workItemTally._erroredWorkItems++;
			assetLink.RepaintUI();
		}

		private void NotifyTOPNodeWaitingWorkItem(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode, int inc)
		{
			topNode._workItemTally._waitingWorkItems = Mathf.Max(topNode._workItemTally._waitingWorkItems + inc, 0);
			assetLink.RepaintUI();
		}

		private void NotifyTOPNodeScheduledWorkItem(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode, int inc)
		{
			topNode._workItemTally._scheduledWorkItems = Mathf.Max(topNode._workItemTally._scheduledWorkItems + inc, 0);
			assetLink.RepaintUI();
		}

		private void NotifyTOPNodeCookingWorkItem(HEU_PDGAssetLink assetLink, HEU_TOPNodeData topNode, int inc)
		{
			topNode._workItemTally._cookingWorkItems = Mathf.Max(topNode._workItemTally._cookingWorkItems + inc, 0);
			assetLink.RepaintUI();
		}

		private static void ResetPDGEventInfo(ref HAPI_PDG_EventInfo eventInfo)
		{
			eventInfo.nodeId = HEU_Defines.HEU_INVALID_NODE_ID;
			eventInfo.workitemId = -1;
			eventInfo.dependencyId = -1;
			eventInfo.currentState = (int)HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_UNDEFINED;
			eventInfo.lastState = (int)HAPI_PDG_WorkitemState.HAPI_PDG_WORKITEM_UNDEFINED;
			eventInfo.eventType = (int)HAPI_PDG_EventType.HAPI_PDG_EVENT_NULL;
		}

		private void SetErrorState(string msg, bool bLogIt)
		{
			// Log first error
			if (!_errored && bLogIt)
			{
				Debug.LogError(msg);
			}

			_errored = true;
			_errorMsg = msg;
		}

		private void ClearErrorState()
		{
			_errored = false;
			_errorMsg = "";
		}

		public HEU_SessionBase GetHAPIPDGSession()
		{
			return HEU_SessionManager.GetOrCreateDefaultSession();
		}

		public void CookTOPNetworkOutputNode(HEU_TOPNetworkData topNetwork)
		{
			HEU_SessionBase session = GetHAPIPDGSession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			// Cancel all cooks. This is required as otherwise the graph gets into an infnite cooked
			// state (bug?)
			if (_pdgContextIDs != null)
			{
				foreach (HAPI_PDG_GraphContextId contextID in _pdgContextIDs)
				{
					session.CancelPDGCook(contextID);
				}
			}			

			if (!session.CookPDG(topNetwork._nodeID, 0, 0))
			{
				Debug.LogErrorFormat("Cook node failed!");
			}
		}

		public void PauseCook(HEU_TOPNetworkData topNetwork)
		{
			HEU_SessionBase session = GetHAPIPDGSession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			// Cancel all cooks.
			if (_pdgContextIDs != null)
			{
				foreach (HAPI_PDG_GraphContextId contextID in _pdgContextIDs)
				{
					session.PausePDGCook(contextID);
				}
			}
		}

		public void CancelCook(HEU_TOPNetworkData topNetwork)
		{
			HEU_SessionBase session = GetHAPIPDGSession();
			if (session == null || !session.IsSessionValid())
			{
				return;
			}

			// Cancel all cooks.
			if (_pdgContextIDs != null)
			{
				foreach (HAPI_PDG_GraphContextId contextID in _pdgContextIDs)
				{
					session.CancelPDGCook(contextID);
				}
			}
		}

		public void ClearWorkItemResult(HEU_SessionBase session, HAPI_PDG_GraphContextId contextID, HAPI_PDG_EventInfo eventInfo, HEU_TOPNodeData topNode)
		{
			session.LogErrorOverride = false;
			bool bCleared = false;

			HAPI_PDG_WorkitemInfo workItemInfo = new HAPI_PDG_WorkitemInfo();
			if (session.GetWorkItemInfo(contextID, eventInfo.workitemId, ref workItemInfo))
			{
				//Debug.LogFormat("Clear: index={0}, state={1}", workItemInfo.index, (HAPI_PDG_WorkitemState)eventInfo.currentState);

				if (workItemInfo.index >= 0)
				{
					HEU_PDGAssetLink.ClearWorkItemResultByIndex(topNode, workItemInfo.index);
					bCleared = true;
				}
			}

			if (!bCleared)
			{
				HEU_PDGAssetLink.ClearWorkItemResultByID(topNode, eventInfo.workitemId);
			}

			session.LogErrorOverride = true;
		}


		//	DATA ------------------------------------------------------------------------------------------------------


		private static HEU_PDGSession _pdgSession;

		private List<HEU_PDGAssetLink> _pdgAssets = new List<HEU_PDGAssetLink>();

		public int _pdgMaxProcessEvents = 100;
		public int _pdgContextSize = 20;
		public HAPI_PDG_GraphContextId[] _pdgContextIDs = null;

		public Stack<HAPI_PDG_EventInfo> _pdgEvents = new Stack<HAPI_PDG_EventInfo>();

		public HAPI_PDG_EventInfo[] _pdgQueryEvents;

		public bool _errored;
		public string _errorMsg;

		public HAPI_PDG_State _pdgState = HAPI_PDG_State.HAPI_PDG_STATE_READY;
	}


}   // namespace HoudiniEngineUnity


