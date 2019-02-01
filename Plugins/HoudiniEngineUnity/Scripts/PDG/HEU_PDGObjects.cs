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
	using HAPI_SessionId = System.Int64;


	[System.Serializable]
	public class HEU_TOPNetworkData
	{
		public HAPI_NodeId _nodeID;

		public string _nodeName;

		public List<HEU_TOPNodeData> _topNodes = new List<HEU_TOPNodeData>();

		public string[] _topNodeNames = new string[0];

		public int _selectedTOPIndex;

		public string _parentName;
	}

	[System.Serializable]
	public class HEU_TOPNodeData
	{
		public HAPI_NodeId _nodeID;

		public string _nodeName;

		public string _parentName;

		public bool _autoLoad;

		public List<HEU_TOPWorkResult> _workResults = new List<HEU_TOPWorkResult>();

		public enum PDGState
		{
			NONE,
			DIRTIED,
			DIRTYING,
			COOKING,
			COOK_SUCCESS,
			COOK_FAILED
		}
		public PDGState _pdgState;

		public int _totalWorkItems;
		public int _waitingWorkItems;
		public int _scheduledWorkItems;
		public int _cookingWorkItems;
		public int _cookedWorkItems;
		public int _erroredWorkItems;
		


		public bool AreAllWorkItemsComplete()
		{
			return (_waitingWorkItems == 0 && _cookingWorkItems == 0 && _scheduledWorkItems == 0 && (_totalWorkItems == (_cookedWorkItems + _erroredWorkItems)));
		}

		public bool AnyWorkItemsFailed()
		{
			return _erroredWorkItems > 0;
		}

		public bool AnyWorkItemsPending()
		{
			return (_totalWorkItems > 0 && (_waitingWorkItems > 0 || _cookingWorkItems > 0 || _scheduledWorkItems > 0));
		}
	}

	[System.Serializable]
	public class HEU_TOPWorkResult
	{
		public int _workItemIndex = -1;
		public List<GameObject> _generatedGOs = new List<GameObject>();
	}

}   // HoudiniEngineUnity