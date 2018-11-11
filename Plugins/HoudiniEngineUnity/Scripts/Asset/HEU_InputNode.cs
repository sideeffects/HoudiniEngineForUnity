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


	// <summary>
	/// Represents a general node for sending data upstream to Houdini.
	/// Currently only supports sending geometry upstream.
	/// Specify input data as file (eg. bgeo), HDA, and Unity gameobjects.
	/// </summary>
	public class HEU_InputNode : ScriptableObject
	{
		// DATA -------------------------------------------------------------------------------------------------------

		// The type of input node based on how it was specified in the HDA
		public enum InputNodeType
		{
			CONNECTION,		// As an asset connection
			NODE,			// Pure input asset node
			PARAMETER,		// As an input parameter
		}

		[SerializeField]
		private InputNodeType _inputNodeType;

		public InputNodeType InputType { get { return _inputNodeType; } }

		// The type of input data set by user
		public enum InputObjectType
		{
			HDA,
			UNITY_MESH,
			//CURVE
		}

		[SerializeField]
		private InputObjectType _inputObjectType = InputObjectType.UNITY_MESH;

		public InputObjectType ThisInputObjectType { get { return _inputObjectType; } }

		[SerializeField]
		private InputObjectType _pendingInputObjectType = InputObjectType.UNITY_MESH;

		public InputObjectType PendingInputObjectType { get { return _pendingInputObjectType; } set { _pendingInputObjectType = value; } }

		// The IDs of the object merge created for the input objects
		[SerializeField]
		private List<HEU_InputObjectInfo> _inputObjects = new List<HEU_InputObjectInfo>();

		[SerializeField]
		private List<HAPI_NodeId> _inputObjectsConnectedAssetIDs = new List<HAPI_NodeId>();

		// Asset input: external reference used for UI
		[SerializeField]
		private GameObject _inputAsset;

		// Asset input: internal reference to the connected asset (valid if connected)
		[SerializeField]
		private GameObject _connectedInputAsset;

		[SerializeField]
		private HAPI_NodeId _nodeID;

		public HAPI_NodeId InputNodeID { get { return _nodeID; } }

		[SerializeField]
		private int _inputIndex;

		[SerializeField]
		private bool _requiresCook;

		public bool RequiresCook { get { return _requiresCook; } set { _requiresCook = value; } }

		[SerializeField]
		private bool _requiresUpload;

		public bool RequiresUpload { get { return _requiresUpload; } set { _requiresUpload = value; } }

		[SerializeField]
		private string _inputName;

		public string InputName { get { return _inputName; } }

		[SerializeField]
		private string _paramName;

		public string ParamName { get { return _paramName; } set { _paramName = value; } }

		[SerializeField]
		private HAPI_NodeId _connectedNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

		[SerializeField]
		// Enabling Keep World Transform by default to keep consistent with other plugins
		private bool _keepWorldTransform = true;

		// If true, sets the SOP/merge (object merge) node to use INTO_THIS_OBJECT transform type. Otherwise NONE.
		public bool KeepWorldTransform { get { return _keepWorldTransform; } set { _keepWorldTransform = value; } }

		[SerializeField]
		private bool _packGeometryBeforeMerging;

		// Acts same as SOP/merge (object merge) Pack Geometry Before Merging parameter value.
		public bool PackGeometryBeforeMerging { get { return _packGeometryBeforeMerging; } set { _packGeometryBeforeMerging = value; } }

		[SerializeField]
		private HEU_HoudiniAsset _parentAsset;

		public HEU_HoudiniAsset ParentAsset { get { return _parentAsset; } }

		public enum InputActions
		{
			ACTION,
			DELETE,
			INSERT
		}

		public bool IsAssetInput() { return _inputNodeType == InputNodeType.CONNECTION; }


		// LOGIC ------------------------------------------------------------------------------------------------------

		public static HEU_InputNode CreateSetupInput(HAPI_NodeId nodeID, int inputIndex, string inputName, InputNodeType inputNodeType, HEU_HoudiniAsset parentAsset)
		{
			HEU_InputNode newInput = ScriptableObject.CreateInstance<HEU_InputNode>();
			newInput._nodeID = nodeID;
			newInput._inputIndex = inputIndex;
			newInput._inputName = inputName;
			newInput._inputNodeType = inputNodeType;
			newInput._parentAsset = parentAsset;

			newInput._requiresUpload = false;
			newInput._requiresCook = false;

			return newInput;
		}

		public void SetInputNodeID(HAPI_NodeId nodeID)
		{
			_nodeID = nodeID;
		}

		public void DestroyAllData(HEU_SessionBase session)
		{
			ClearUICache();

			DisconnectAndDestroyInputAssets(session);
		}

		private void ResetInputObjectTransforms()
		{
			for(int i = 0; i < _inputObjects.Count; ++i)
			{
				_inputObjects[i]._syncdTransform = Matrix4x4.identity;
			}
		}

		public void ResetInputNode(HEU_SessionBase session)
		{
			ResetConnectionForForceUpdate(session);
			RemoveAllInputObjects();
			ClearUICache();

			ChangeInputType(session, InputObjectType.UNITY_MESH);
		}

		private HEU_InputObjectInfo CreateInputObjectInfo(GameObject inputGameObject)
		{
			HEU_InputObjectInfo newObjectInfo = new HEU_InputObjectInfo();
			newObjectInfo._gameObject = inputGameObject;

			return newObjectInfo;
		}

		public void InsertInputObject(int index, GameObject newInputGameObject)
		{
			if(index >= 0 && index < _inputObjects.Count)
			{
				_inputObjects.Insert(index, CreateInputObjectInfo(newInputGameObject));
			}
			else
			{
				Debug.LogErrorFormat("Insert index {0} out of range (number of items is {1})", index, _inputObjects.Count);
			}
		}

		public HEU_InputObjectInfo GetInputObject(int index)
		{
			if (index >= 0 && index < _inputObjects.Count)
			{
				return _inputObjects[index];
			}
			else
			{
				Debug.LogErrorFormat("Get index {0} out of range (number of items is {1})", index, _inputObjects.Count);
			}
			return null;
		}

		public HEU_InputObjectInfo AddInputObjectAtEnd(GameObject newInputGameObject)
		{
			HEU_InputObjectInfo inputObject = CreateInputObjectInfo(newInputGameObject);
			_inputObjects.Add(inputObject);
			return inputObject;
		}

		public void RemoveAllInputObjects()
		{
			_inputObjects.Clear();
		}

		public int NumInputObjects()
		{
			return _inputObjects.Count;
		}

		public void ChangeInputType(HEU_SessionBase session, InputObjectType newType)
		{
			if(newType == _inputObjectType)
			{
				return;
			}

			DisconnectAndDestroyInputAssets(session);

			_inputObjectType = newType;
			_pendingInputObjectType = _inputObjectType;
		}

		/// <summary>
		/// Reset the connected state so that any previous connection will be remade
		/// </summary>
		public void ResetConnectionForForceUpdate(HEU_SessionBase session)
		{
			if (_inputObjectType == InputObjectType.HDA)
			{
				if (IsInputAssetConnected())
				{
					// By disconnecting here, we can then properly reconnect again.
					// This is needed when loading a saved scene and recooking.
					DisconnectInputAssetActor(session);
				}
			}
		}

		public void UploadInput(HEU_SessionBase session)
		{
			if (_nodeID == HEU_Defines.HEU_INVALID_NODE_ID)
			{
				Debug.LogErrorFormat("Input Node ID is invalid. Unable to upload input. Try recooking.");
				return;
			}

			if(_pendingInputObjectType != _inputObjectType)
			{
				ChangeInputType(session, _pendingInputObjectType);
			}

			if(_inputObjectType == InputObjectType.UNITY_MESH)
			{
				if(_inputObjects == null || _inputObjects.Count == 0)
				{
					DisconnectAndDestroyInputAssets(session);
				}
				else
				{
					DisconnectAndDestroyInputAssets(session);

					bool bResult = HEU_InputUtility.CreateInputNodeWithMultiObjects(session, _nodeID, ref _connectedNodeID, ref _inputObjects, ref _inputObjectsConnectedAssetIDs, _keepWorldTransform);
					if(!bResult)
					{
						DisconnectAndDestroyInputAssets(session);
						return;
					}

					ConnectInputNode(session);

					if(!UploadObjectMergeTransformType(session))
					{
						Debug.LogErrorFormat("Failed to upload object merge transform type!");
						return;
					}

					if (!UploadObjectMergePackGeometry(session))
					{
						Debug.LogErrorFormat("Failed to upload object merge pack geometry value!");
						return;
					}
				}
			}
			else if(_inputObjectType == InputObjectType.HDA)
			{
				// Connect HDA. Note only 1 connection supported.

				if (IsInputAssetConnected())
				{
					DisconnectInputAssetActor(session);
				}

				if (_inputAsset != null)
				{
					HEU_HoudiniAssetRoot inputAssetRoot = _inputAsset.GetComponent<HEU_HoudiniAssetRoot>();
					if(inputAssetRoot != null && inputAssetRoot._houdiniAsset != null)
					{
						if (!inputAssetRoot._houdiniAsset.IsAssetValidInHoudini(session))
						{
							// Force a recook if its not valid (in case it hasn't been loaded into the session)
							inputAssetRoot._houdiniAsset.RequestCook(true, false, true, true);
						}

						ConnectInputAssetActor(session);
					}
					else
					{
						Debug.LogWarningFormat("The input GameObject {0} is not a valid HDA asset.", _inputAsset.name);
					}
				}
			}
			//else if (_inputObjectType == InputObjectType.CURVE)
			//{
				// TODO INPUT NODE - create new Curve SOP (add HEU_Curve here?)
			//}
			else
			{
				Debug.LogErrorFormat("Unsupported input type {0}. Unable to upload input.", _inputObjectType);
			}

			RequiresUpload = false;
			RequiresCook = true;

			ClearUICache();
		}

		public bool IsInputAssetConnected()
		{
			return _connectedInputAsset != null;
		}

		public void ReconnectToUpstreamAsset()
		{
			if (_inputObjectType == InputObjectType.HDA && IsInputAssetConnected())
			{
				HEU_HoudiniAssetRoot inputAssetRoot = _connectedInputAsset != null ? _connectedInputAsset.GetComponent<HEU_HoudiniAssetRoot>() : null;
				if (inputAssetRoot != null && inputAssetRoot._houdiniAsset != null)
				{
					_parentAsset.ConnectToUpstream(inputAssetRoot._houdiniAsset);
				}
			}
		}

		private void ConnectInputAssetActor(HEU_SessionBase session)
		{
			if(IsInputAssetConnected())
			{
				Debug.LogWarning("Input asset already connected!");
				return;
			}

			HEU_HoudiniAssetRoot inputAssetRoot = _inputAsset != null ? _inputAsset.GetComponent<HEU_HoudiniAssetRoot>() : null;
			if (inputAssetRoot != null && inputAssetRoot._houdiniAsset.IsAssetValidInHoudini(session))
			{
				_connectedNodeID = inputAssetRoot._houdiniAsset.AssetID;

				ConnectInputNode(session);

				_parentAsset.ConnectToUpstream(inputAssetRoot._houdiniAsset);

				_connectedInputAsset = _inputAsset;
			}
		}

		private void DisconnectInputAssetActor(HEU_SessionBase session)
		{
			if (!IsInputAssetConnected())
			{
				return;
			}

			if (session != null)
			{
				//Debug.LogWarningFormat("Disconnecting Node Input for _nodeID={0} with type={1}", _nodeID, _inputNodeType);

				if (_inputNodeType == InputNodeType.PARAMETER)
				{
					HEU_ParameterData paramData = _parentAsset.Parameters.GetParameter(_paramName);
					if (paramData == null)
					{
						Debug.LogErrorFormat("Unable to find parameter with name {0}!", _paramName);
					}
					else if (!session.SetParamStringValue(_nodeID, "", paramData.ParmID, 0))
					{
						Debug.LogErrorFormat("Unable to clear object path parameter for input node!");
					}
				}
				else if (_nodeID != HEU_Defines.HEU_INVALID_NODE_ID)
				{
					session.DisconnectNodeInput(_nodeID, _inputIndex, false);
				}
			}

			ClearConnectedInputAsset();

			// Must clear this and not delete as this points to existing asset node.
			// If _inputObjectType changes to MESH, node with this ID will get deleted. 
			_connectedNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
		}

		private void ClearConnectedInputAsset()
		{
			if (_connectedInputAsset != null)
			{
				HEU_HoudiniAssetRoot inputAssetRoot = _connectedInputAsset != null ? _connectedInputAsset.GetComponent<HEU_HoudiniAssetRoot>() : null;
				if (inputAssetRoot != null)
				{
					_parentAsset.DisconnectFromUpstream(inputAssetRoot._houdiniAsset);
				}
				_connectedInputAsset = null;
			}
		}

		private void ConnectInputNode(HEU_SessionBase session)
		{
			// Connect node input
			
			if (_inputNodeType == InputNodeType.PARAMETER)
			{
				if (string.IsNullOrEmpty(_paramName))
				{
					Debug.LogErrorFormat("Invalid parameter name for input node of parameter type!");
					return;
				}

				if (!session.SetParamNodeValue(_nodeID, _paramName, _connectedNodeID))
				{
					Debug.LogErrorFormat("Unable to connect to input node!");
					return;
				}

				//Debug.LogFormat("Setting input connection for parameter {0} with {1} connecting to {2}", _paramName, _nodeID, _connectedNodeID);
			}
			else
			{
				if(!session.ConnectNodeInput(_nodeID, _inputIndex, _connectedNodeID))
				{
					Debug.LogErrorFormat("Unable to connect to input node!");
					return;
				}
			}

			// This ensures that the input node's own object merge Keep World Transform
			// plays nicely with the connected object merge's Keep World Transform
			SetInputNodeObjectMergeKeepTransform(session, false);
		}

		/// <summary>
		/// Set the input node's object merge node's Keep World Transform parameter
		/// </summary>
		/// <param name="session"></param>
		/// <param name="bEnable"></param>
		private void SetInputNodeObjectMergeKeepTransform(HEU_SessionBase session, bool bEnable)
		{
			if (_inputNodeType != InputNodeType.PARAMETER)
			{
				HAPI_NodeId inputNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
				if (session.QueryNodeInput(_nodeID, _inputIndex, out inputNodeID, false))
				{
					session.SetParamIntValue(inputNodeID, HEU_Defines.HAPI_OBJMERGE_TRANSFORM_PARAM, 0, bEnable ? 1 : 0);
				}
			}
		}

		private void DisconnectAndDestroyInputAssets(HEU_SessionBase session)
		{
			DisconnectInputAssetActor(session);

			_inputAsset = null;

			if (session != null)
			{
				foreach (HAPI_NodeId nodeID in _inputObjectsConnectedAssetIDs)
				{
					session.DeleteNode(nodeID);
				}

				if (_connectedNodeID != HEU_Defines.HEU_INVALID_NODE_ID && HEU_HAPIUtility.IsNodeValidInHoudini(session, _connectedNodeID))
				{
					// We'll delete the parent Object because we presume to have created the SOP/merge ourselves.
					// If the parent Object doesn't get deleted, it sticks around unused.
					HAPI_NodeInfo parentNodeInfo = new HAPI_NodeInfo();
					if(session.GetNodeInfo(_connectedNodeID, ref parentNodeInfo))
					{
						if (parentNodeInfo.parentId != HEU_Defines.HEU_INVALID_NODE_ID)
						{
							session.DeleteNode(parentNodeInfo.parentId);
						}
					}
				}
			}

			_inputObjectsConnectedAssetIDs.Clear();
			_connectedNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
		}

		public bool UploadObjectMergeTransformType(HEU_SessionBase session)
		{
			if(_connectedNodeID == HEU_Defines.HAPI_INVALID_PARM_ID)
			{
				return false;
			}

			if (_inputObjectType != InputObjectType.UNITY_MESH)
			{
				return false;
			}

			int transformType = _keepWorldTransform ? 1 : 0;

			HAPI_NodeId inputNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

			// Use _connectedNodeID to find its connections, which should be
			// the object merge nodes. We set the pack parameter on those.
			// Presume that the number of connections to  _connectedNodeID is equal to 
			// size of _inputObjectsConnectedAssetIDs which contains the input nodes.
			int numConnected = _inputObjectsConnectedAssetIDs.Count;
			for (int i = 0; i < numConnected; ++i)
			{
				if (_inputObjectsConnectedAssetIDs[i] == HEU_Defines.HEU_INVALID_NODE_ID)
				{
					continue;
				}

				inputNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
				if (session.QueryNodeInput(_connectedNodeID, i, out inputNodeID, false))
				{
					session.SetParamIntValue(inputNodeID, HEU_Defines.HAPI_OBJMERGE_TRANSFORM_PARAM, 0, transformType);
				}
			}

			// This ensures that the input node's own object merge Keep World Transform
			// plays nicely with the connected object merge's Keep World Transform
			SetInputNodeObjectMergeKeepTransform(session, _keepWorldTransform);

			return true;
		}

		private bool UploadObjectMergePackGeometry(HEU_SessionBase session)
		{
			if (_connectedNodeID == HEU_Defines.HAPI_INVALID_PARM_ID)
			{
				return false;
			}

			if (_inputObjectType != InputObjectType.UNITY_MESH)
			{
				return false;
			}

			int packEnabled = _packGeometryBeforeMerging ? 1 : 0;

			HAPI_NodeId inputNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

			// Use _connectedNodeID to find its connections, which should be
			// the object merge nodes. We set the pack parameter on those.
			// Presume that the number of connections to  _connectedNodeID is equal to 
			// size of _inputObjectsConnectedAssetIDs which contains the input nodes.
			int numConnected = _inputObjectsConnectedAssetIDs.Count;
			for (int i = 0; i < numConnected; ++i)
			{
				if(_inputObjectsConnectedAssetIDs[i] == HEU_Defines.HEU_INVALID_NODE_ID)
				{
					continue;
				}

				inputNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
				if (session.QueryNodeInput(_connectedNodeID, i, out inputNodeID, false))
				{
					session.SetParamIntValue(inputNodeID, HEU_Defines.HAPI_OBJMERGE_PACK_GEOMETRY, 0, packEnabled);
				}
			}

			return true;
		}

		public bool HasInputNodeTransformChanged()
		{
			if (_inputObjectType == InputObjectType.UNITY_MESH)
			{
				for (int i = 0; i < _inputObjects.Count; ++i)
				{
					if (_inputObjects[i]._gameObject != null)
					{
						if (_inputObjects[i]._useTransformOffset)
						{
							if (!HEU_HAPIUtility.IsSameTransform(ref _inputObjects[i]._syncdTransform, ref _inputObjects[i]._translateOffset, ref _inputObjects[i]._rotateOffset, ref _inputObjects[i]._scaleOffset))
							{
								return true;
							}
						}
						else if (_inputObjects[i]._gameObject.transform.localToWorldMatrix != _inputObjects[i]._syncdTransform)
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		public bool UploadInputObjectTransforms(HEU_SessionBase session)
		{
			if (_nodeID == HEU_Defines.HAPI_INVALID_PARM_ID)
			{
				return false;
			}

			if (_inputObjectType != InputObjectType.UNITY_MESH)
			{
				return false;
			}

			for (int i = 0; i < _inputObjects.Count; ++i)
			{
				if(_inputObjects[i]._gameObject == null)
				{
					continue;
				}

				HEU_InputUtility.UploadInputObjectTransform(session, _inputObjects[i], _inputObjectsConnectedAssetIDs[i], _keepWorldTransform);
			}

			return false;
		}

		/// <summary>
		/// Force cook upstream connected asset if its not valid in given session.
		/// </summary>
		/// <param name="session"></param>
		public void CookUpstreamConnectedAsset(HEU_SessionBase session)
		{
			if(_inputObjectType == InputObjectType.HDA && IsInputAssetConnected())
			{
				HEU_HoudiniAssetRoot inputAssetRoot = _connectedInputAsset.GetComponent<HEU_HoudiniAssetRoot>();
				if (inputAssetRoot != null && !inputAssetRoot._houdiniAsset.IsAssetValidInHoudini(session))
				{
					inputAssetRoot._houdiniAsset.RequestCook(true, false, true, true);

					// Update the _connectNodeID to the connected asset's ID since it might have changed during cook
					if(_connectedNodeID != HEU_Defines.HEU_INVALID_NODE_ID)
					{
						HAPI_NodeId otherAssetID = inputAssetRoot._houdiniAsset != null ? inputAssetRoot._houdiniAsset.AssetID : HEU_Defines.HEU_INVALID_NODE_ID;
						if(otherAssetID != _connectedNodeID)
						{
							_connectedNodeID = otherAssetID;
						}
					}
				}
			}
		}

		/// <summary>
		/// Update the input connection based on the fact that the owner asset was recreated
		/// in the given session. Asset input connections will be updated, while mesh input
		/// connections will be invalidated without cleaning up because the IDs can't be trusted.
		/// </summary>
		/// <param name="session"></param>
		public void UpdateOnAssetRecreation(HEU_SessionBase session)
		{
			if (_inputObjectType == InputObjectType.HDA && IsInputAssetConnected())
			{
				// For asset inputs, cook the asset if its not valid in session, and update the ID
				HEU_HoudiniAssetRoot inputAssetRoot = _connectedInputAsset.GetComponent<HEU_HoudiniAssetRoot>();
				if (inputAssetRoot != null && !inputAssetRoot._houdiniAsset.IsAssetValidInHoudini(session))
				{
					inputAssetRoot._houdiniAsset.RequestCook(true, false, true, true);

					// Update the _connectNodeID to the connected asset's ID since it might have changed during cook
					if (_connectedNodeID != HEU_Defines.HEU_INVALID_NODE_ID)
					{
						HAPI_NodeId otherAssetID = inputAssetRoot._houdiniAsset != null ? inputAssetRoot._houdiniAsset.AssetID : HEU_Defines.HEU_INVALID_NODE_ID;
						if (otherAssetID != _connectedNodeID)
						{
							_connectedNodeID = otherAssetID;
						}

						if(_connectedNodeID == HEU_Defines.HEU_INVALID_NODE_ID)
						{
							ClearConnectedInputAsset();
						}
					}
				}
			}
			else if(_inputObjectType == InputObjectType.UNITY_MESH)
			{
				// For mesh input, invalidate _inputObjectsConnectedAssetIDs and _connectedNodeID as their
				// nodes most likely don't exist, and the IDs will not be correct since this asset got recreated
				// Note that _inputObjects don't need to be cleared as they will be used when recreating the connections.
				_inputObjectsConnectedAssetIDs.Clear();
				_connectedNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
			}
		}

		public void CopyInputValuesTo(HEU_SessionBase session, HEU_InputNode destInputNode)
		{
			destInputNode._pendingInputObjectType = _inputObjectType;

			if(destInputNode._inputObjectType == InputObjectType.HDA)
			{
				destInputNode.ResetConnectionForForceUpdate(session);
			}

			destInputNode._inputObjects.Clear();
			foreach(HEU_InputObjectInfo inputObj in _inputObjects)
			{
				HEU_InputObjectInfo newInputObject = new HEU_InputObjectInfo();
				inputObj.CopyTo(newInputObject);
				//newInputObject._syncdTransform = Matrix4x4.identity;

				destInputNode._inputObjects.Add(newInputObject);
			}

			destInputNode._inputAsset = _inputAsset;
			destInputNode._connectedInputAsset = _connectedInputAsset;

			destInputNode._keepWorldTransform = _keepWorldTransform;
			destInputNode._packGeometryBeforeMerging = _packGeometryBeforeMerging;
		}

		public void PopulateInputPreset(HEU_InputPreset inputPreset)
		{
			inputPreset._inputObjectType = _inputObjectType;

			inputPreset._inputAssetName = _inputAsset != null ? _inputAsset.name : "";

			inputPreset._inputIndex = _inputIndex;
			inputPreset._inputName = _inputName;

			inputPreset._keepWorldTransform = _keepWorldTransform;
			inputPreset._packGeometryBeforeMerging = _packGeometryBeforeMerging;

			foreach (HEU_InputObjectInfo inputObject in _inputObjects)
			{
				HEU_InputObjectPreset inputObjectPreset = new HEU_InputObjectPreset();

				if (inputObject._gameObject != null)
				{
					inputObjectPreset._gameObjectName = inputObject._gameObject.name;

					// Tag whether scene or project input object
					inputObjectPreset._isSceneObject = !HEU_GeneralUtility.IsGameObjectInProject(inputObject._gameObject);
					if (!inputObjectPreset._isSceneObject)
					{
						// For inputs in project, use the project path as name
						inputObjectPreset._gameObjectName = HEU_AssetDatabase.GetAssetOrScenePath(inputObject._gameObject);
					}
				}
				else
				{
					inputObjectPreset._gameObjectName = "";
				}

				inputObjectPreset._useTransformOffset = inputObject._useTransformOffset;
				inputObjectPreset._translateOffset = inputObject._translateOffset;
				inputObjectPreset._rotateOffset = inputObject._rotateOffset;
				inputObjectPreset._scaleOffset = inputObject._scaleOffset;

				inputPreset._inputObjectPresets.Add(inputObjectPreset);
			}

		}

		public void LoadPreset(HEU_SessionBase session, HEU_InputPreset inputPreset)
		{
			ResetInputNode(session);

			ChangeInputType(session, inputPreset._inputObjectType);

			if (inputPreset._inputObjectType == HEU_InputNode.InputObjectType.UNITY_MESH)
			{
				int numObjects = inputPreset._inputObjectPresets.Count;
				for (int i = 0; i < numObjects; ++i)
				{
					if (!string.IsNullOrEmpty(inputPreset._inputObjectPresets[i]._gameObjectName))
					{
						GameObject inputGO = null;
						if (inputPreset._inputObjectPresets[i]._isSceneObject)
						{
							inputGO = HEU_GeneralUtility.GetGameObjectByNameInScene(inputPreset._inputObjectPresets[i]._gameObjectName);
						}
						else
						{
							// Use the _gameObjectName as path to find in scene
							inputGO = HEU_AssetDatabase.LoadAssetAtPath(inputPreset._inputObjectPresets[i]._gameObjectName, typeof(GameObject)) as GameObject;
							if(inputGO == null)
							{
								Debug.LogErrorFormat("Unable to find input at {0}", inputPreset._inputObjectPresets[i]._gameObjectName);
							}
						}

						if (inputGO != null)
						{
							HEU_InputObjectInfo inputObject = AddInputObjectAtEnd(inputGO);
							inputObject._useTransformOffset = inputPreset._inputObjectPresets[i]._useTransformOffset;
							inputObject._translateOffset = inputPreset._inputObjectPresets[i]._translateOffset;
							inputObject._rotateOffset = inputPreset._inputObjectPresets[i]._rotateOffset;
							inputObject._scaleOffset = inputPreset._inputObjectPresets[i]._scaleOffset;
						}
						else
						{
							Debug.LogWarningFormat("Gameobject with name {0} not found. Unable to set input object.", inputPreset._inputAssetName);
						}
					}
				}
			}
			else if (inputPreset._inputObjectType == HEU_InputNode.InputObjectType.HDA)
			{
				if (!string.IsNullOrEmpty(inputPreset._inputAssetName))
				{
					GameObject inputAsset = GameObject.Find(inputPreset._inputAssetName);
					if (inputAsset != null)
					{
						HEU_HoudiniAssetRoot inputAssetRoot = inputAsset != null ? inputAsset.GetComponent<HEU_HoudiniAssetRoot>() : null;
						if (inputAssetRoot != null && inputAssetRoot._houdiniAsset != null)
						{
							// Need to reconnect and make sure connected asset is in this session

							_inputAsset = inputAsset;

							if (!inputAssetRoot._houdiniAsset.IsAssetValidInHoudini(session))
							{
								inputAssetRoot._houdiniAsset.RequestCook(true, false, true, true);
							}

							_connectedNodeID = inputAssetRoot._houdiniAsset.AssetID;

							_parentAsset.ConnectToUpstream(inputAssetRoot._houdiniAsset);

							_connectedInputAsset = _inputAsset;
						}
						else
						{
							Debug.LogErrorFormat("Input HDA with name {0} is not a valid asset (missing components). Unable to set input asset.", inputPreset._inputAssetName);
						}
					}
					else
					{
						Debug.LogWarningFormat("Game with name {0} not found. Unable to set input asset.", inputPreset._inputAssetName);
					}
				}
			}

			KeepWorldTransform = inputPreset._keepWorldTransform;
			PackGeometryBeforeMerging = inputPreset._packGeometryBeforeMerging;

			RequiresUpload = true;

			ClearUICache();
		}

		public void NotifyParentRemovedInput()
		{
			if(_parentAsset != null)
			{
				_parentAsset.RemoveInputNode(this);
			}
		}

		// UI CACHE ---------------------------------------------------------------------------------------------------

		public HEU_InputNodeUICache _uiCache;

		public void ClearUICache()
		{
			_uiCache = null;
		}
	}

	// Container for each input object in this node
	[System.Serializable]
	public class HEU_InputObjectInfo
	{
		// Gameobject containing mesh
		public GameObject _gameObject;

		// The last upload transform, for diff checks
		public Matrix4x4 _syncdTransform = Matrix4x4.identity;

		// Whether to use the transform offset
		[FormerlySerializedAs("_useTransformOverride")]
		public bool _useTransformOffset = false;

		// Transform offset
		[FormerlySerializedAs("_translateOverride")]
		public Vector3 _translateOffset = Vector3.zero;

		[FormerlySerializedAs("_rotateOverride")]
		public Vector3 _rotateOffset = Vector3.zero;

		[FormerlySerializedAs("_scaleOverride")]
		public Vector3 _scaleOffset = Vector3.one;

		public System.Type _inputInterfaceType;

		public void CopyTo(HEU_InputObjectInfo destObject)
		{
			destObject._gameObject = _gameObject;
			destObject._syncdTransform = _syncdTransform;
			destObject._useTransformOffset = _useTransformOffset;
			destObject._translateOffset = _translateOffset;
			destObject._rotateOffset = _rotateOffset;
			destObject._scaleOffset = _scaleOffset;
			destObject._inputInterfaceType = _inputInterfaceType;
		}
	}

	// UI cache container
	public class HEU_InputNodeUICache
	{
#if UNITY_EDITOR
		public UnityEditor.SerializedObject _inputNodeSerializedObject;

		public UnityEditor.SerializedProperty _inputObjectTypeProperty;

		public UnityEditor.SerializedProperty _keepWorldTransformProperty;
		public UnityEditor.SerializedProperty _packBeforeMergeProperty;

		public UnityEditor.SerializedProperty _inputObjectsProperty;

		public UnityEditor.SerializedProperty _inputAssetProperty;
#endif

		public class HEU_InputObjectUICache
		{
#if UNITY_EDITOR
			public UnityEditor.SerializedProperty _gameObjectProperty;
			public UnityEditor.SerializedProperty _transformOffsetProperty;
			public UnityEditor.SerializedProperty _translateProperty;
			public UnityEditor.SerializedProperty _rotateProperty;
			public UnityEditor.SerializedProperty _scaleProperty;
#endif
		}

		public List<HEU_InputObjectUICache> _inputObjectCache = new List<HEU_InputObjectUICache>();
	}

}   // HoudiniEngineUnity