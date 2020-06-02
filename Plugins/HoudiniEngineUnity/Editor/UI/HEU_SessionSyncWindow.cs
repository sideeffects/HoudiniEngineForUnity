/*
* Copyright (c) <2020> Side Effects Software Inc.
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

using System.Text;
using UnityEditor;
using UnityEngine;

namespace HoudiniEngineUnity
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Typedefs (copy these from HEU_Common.cs)
    using HAPI_NodeId = System.Int32;

    /// <summary>
    /// Handles the SessionSync UI and logic.
    /// Users can start Houdini with SessionSync or connect to SessionSync
    /// already running in Houdini.
    /// Allows to set connection settings, and SessionSync settings.
    /// Allows to create new nodes in Houdini.
    /// </summary>
    [InitializeOnLoad]
    public class HEU_SessionSyncWindow : EditorWindow
    {
	public static void ShowWindow()
	{
	    EditorWindow.GetWindow(typeof(HEU_SessionSyncWindow), false, "HEngine SessionSync");
	}

	private void OnEnable()
	{
	    ReInitialize();

	    EditorApplication.update += UpdateSync;
	}

	private void OnDisable()
	{
	    EditorApplication.update -= UpdateSync;
	}

	void ReInitialize()
	{
	    _sessionMode = HEU_PluginSettings.Session_Mode;

	    _port = HEU_PluginSettings.Session_Port;
	    _pipeName = HEU_PluginSettings.Session_PipeName;

	    _log = new StringBuilder();

	    if (_connectionSyncInfo != null && !_connectionSyncInfo._validForConnection)
	    {
		// The serializer creates a default _connectionSyncInfo which isn't
		// the correct SessionInfo. The real one is stored in SessionData.
		_connectionSyncInfo = null;
	    }
	}

	void OnGUI()
	{
	    SetupUI();

	    HEU_SessionSyncInfo syncInfo = GetSessionSync();

	    EditorGUI.BeginChangeCheck();

	    bool bSessionStarted = (syncInfo != null && syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped);
	    bool bSessionCanStart = !bSessionStarted;

	    if (bSessionCanStart)
	    {
		// Only able to start a session if no session exists.
		HEU_SessionBase session = HEU_SessionManager.GetDefaultSession();
		if (session != null && session.IsSessionValid())
		{
		    bSessionCanStart = false;
		}
	    }

	    HEU_HoudiniAssetUI.DrawHeaderSection();

	    if (syncInfo != null)
	    {
		if (syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Stopped)
		{
		    if (!bSessionCanStart)
		    {
			EditorGUILayout.LabelField("Another session already running. Disconnect it to start SessionSync.");
		    }
		    else
		    {
			EditorGUILayout.LabelField("Status: " + syncInfo.SyncStatus);
		    }
		}
		else
		{
		    EditorGUILayout.LabelField("Status: " + syncInfo.SyncStatus);
		}
	    }
	    else
	    {
		if (!bSessionCanStart)
		{
		    EditorGUILayout.LabelField("Another session already running. Disconnect it to start SessionSync.");
		}
		else
		{
		    EditorGUILayout.LabelField("No active session.");
		}
	    }

	    EditorGUILayout.Separator();
	    
	    EditorGUI.indentLevel++;

	    using (new EditorGUILayout.HorizontalScope())
	    {
		using (new EditorGUI.DisabledScope(bSessionStarted || !bSessionCanStart))
		{
		    if (GUILayout.Button("Start Houdini"))
		    {
			StartAndConnectToHoudini(syncInfo);
		    }
		    else if (GUILayout.Button("Connect to Houdini"))
		    {
			ConnectSessionSync(syncInfo);
		    }
		}
	    }

	    using (new EditorGUI.DisabledScope((syncInfo == null || !bSessionStarted) && bSessionCanStart))
	    {
		if (GUILayout.Button("Disconnect"))
		{
		    Disconnect(syncInfo);
		}
	    }

	    EditorGUILayout.Separator();

	    EditorGUILayout.LabelField("Connection Settings");

	    using (new EditorGUI.DisabledScope(bSessionStarted))
	    {
		SessionMode newSessionMode = (SessionMode)EditorGUILayout.EnumPopup("Type", _sessionMode);
		if (_sessionMode != newSessionMode)
		{
		    _sessionMode = newSessionMode;
		    HEU_PluginSettings.Session_Mode = newSessionMode;
		}

		EditorGUI.indentLevel++;
		if (_sessionMode == SessionMode.Pipe)
		{
		    string newPipeName = EditorGUILayout.DelayedTextField("Pipe Name", _pipeName);
		    if (_pipeName != newPipeName)
		    {
			HEU_PluginSettings.Session_PipeName = newPipeName;
			_pipeName = newPipeName;
		    }
		}
		else if (_sessionMode == SessionMode.Socket)
		{
		    int newPort = EditorGUILayout.DelayedIntField("Port", _port);
		    HEU_PluginSettings.Session_Port = newPort;
		    if (_port != newPort)
		    {
			HEU_PluginSettings.Session_Port = newPort;
			_port = newPort;
		    }
		}
		EditorGUI.indentLevel--;
	    }

	    EditorGUILayout.Separator();

	    // The rest requires syncInfo

	    if (syncInfo != null)
	    {
		using (new EditorGUI.DisabledScope(syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Connected))
		{
		    EditorGUILayout.LabelField("Synchronization Settings");

		    EditorGUI.indentLevel++;

		    HEU_PluginSettings.SessionSyncAutoCook = HEU_EditorUI.DrawToggleLeft(HEU_PluginSettings.SessionSyncAutoCook, "Sync With Houdini Cook");

		    bool enableHoudiniTime = HEU_EditorUI.DrawToggleLeft(syncInfo._useHoudiniTime, "Cook Using Houdini Time");
		    if (syncInfo._useHoudiniTime != enableHoudiniTime)
		    {
			syncInfo.SetHuseHoudiniTime(enableHoudiniTime);
		    }

		    syncInfo._syncViewport = HEU_EditorUI.DrawToggleLeft(syncInfo._syncViewport, "Sync Viewport");

		    EditorGUI.indentLevel--;
		}

		EditorGUILayout.Separator();

		EditorGUILayout.LabelField("New Node");

		using (new EditorGUI.DisabledScope(syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Connected))
		{
		    EditorGUI.indentLevel++;

		    syncInfo._newNodeName = EditorGUILayout.TextField("Name", syncInfo._newNodeName);

		    syncInfo._nodeTypeIndex = EditorGUILayout.Popup("Type", syncInfo._nodeTypeIndex, _nodeTypesLabels);

		    using (new EditorGUI.DisabledGroupScope(string.IsNullOrEmpty(syncInfo._newNodeName)))
		    {
			using (new EditorGUILayout.VerticalScope())
			{
			    if (GUILayout.Button("Create"))
			    {
				if (syncInfo._nodeTypeIndex >= 0 && syncInfo._nodeTypeIndex < 3)
				{
				    HEU_NodeSync.CreateNodeSync(null, _nodeTypes[syncInfo._nodeTypeIndex],
					syncInfo._newNodeName);
				}
				else if (syncInfo._nodeTypeIndex == 3)
				{
				    CreateCurve(syncInfo._newNodeName);
				}
				else if (syncInfo._nodeTypeIndex == 4)
				{
				    CreateInput(syncInfo._newNodeName);
				}
			    }

			    if (GUILayout.Button("Load NodeSync"))
			    {
				LoadNodeSyncDialog(syncInfo._newNodeName);
			    }
			}
		    }

		    EditorGUI.indentLevel--;

		    DrawSyncNodes();
		}

		EditorGUILayout.Separator();

		// Log
		using (new EditorGUILayout.VerticalScope(_backgroundStyle))
		{
		    using (new EditorGUILayout.HorizontalScope())
		    {
			EditorGUILayout.PrefixLabel(_eventMessageContent);

			if (GUILayout.Button("Clear"))
			{
			    ClearLog();
			}
		    }

		    string logMsg = GetLog();

		    using (var scrollViewScope = new EditorGUILayout.ScrollViewScope(_eventMessageScrollPos, GUILayout.Height(120)))
		    {
			_eventMessageScrollPos = scrollViewScope.scrollPosition;

			GUILayout.Label(logMsg, _eventMessageStyle);
		    }
		}
	    }

	    EditorGUI.indentLevel--;

	    if (EditorGUI.EndChangeCheck() && syncInfo != null)
	    {
		HEU_SessionBase sessionBase = HEU_SessionManager.GetDefaultSession();
		if (sessionBase != null)
		{
		    HEU_SessionManager.SaveAllSessionData();
		}
	    }
	} 

	void ConnectSessionSync(HEU_SessionSyncInfo syncInfo)
	{
	    if (syncInfo != null && syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped)
	    {
		return;
	    }

	    Log("Connecting To Houdini...");

	    HEU_SessionManager.RecreateDefaultSessionData();

	    if (syncInfo == null)
	    {
		HEU_SessionData sessionData = HEU_SessionManager.GetSessionData();
		if (sessionData != null)
		{
		    syncInfo = sessionData.GetOrCreateSessionSync();
		}
		else
		{
		    syncInfo = new HEU_SessionSyncInfo();
		}
	    }


	    bool result = InternalConnect(_sessionMode, _pipeName,
		HEU_PluginSettings.Session_Localhost, _port,
		HEU_PluginSettings.Session_AutoClose, HEU_PluginSettings.Session_Timeout,
		true);

	    if (result)
	    {
		try
		{
		    HEU_SessionManager.InitializeDefaultSession();

		    HEU_SessionManager.GetDefaultSession().GetSessionData().SetSessionSync(syncInfo);

		    syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Connected;
		    Log("Connected!");
		}
		catch (HEU_HoudiniEngineError ex)
		{
		    syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;

		    Log("Connection errored!");
		    Log(ex.ToString());
		}
	    }
	    else
	    {
		Log("Connection failed!");
	    }
	}

	bool InternalConnect(
	    SessionMode sessionType, string pipeName, 
	    string ip, int port, bool autoClose, float timeout, 
	    bool logError)
	{
	    if (sessionType == SessionMode.Pipe)
	    {
		return HEU_SessionManager.ConnectSessionSyncUsingThriftPipe(
		    pipeName,
		    autoClose,
		    timeout,
		    logError);
	    }
	    else
	    {
		return HEU_SessionManager.ConnectSessionSyncUsingThriftSocket(
		    ip,
		    port,
		    autoClose,
		    timeout,
		    logError);
	    }
	}

	void Disconnect(HEU_SessionSyncInfo syncInfo)
	{
	    if (syncInfo != null)
	    {
		syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;

		// Store the sync info as it gets cleared in the session below
		_connectionSyncInfo = syncInfo;
	    }

	    if (HEU_SessionManager.CloseDefaultSession())
	    {
		Log("Connection closed!");
	    }
	    else
	    {
		Log("Failed to close session! ");
	    }
	}


	bool OpenHoudini()
	{
	    string args = "";

	    // Form argument
	    if (_sessionMode == SessionMode.Pipe)
	    {
		args = string.Format("-hess=pipe:{0}", _pipeName);
	    }
	    else
	    {
		args = string.Format("-hess=port:{0}", _port);
	    }

	    Log("Opening Houdini...");

	    if (!HEU_SessionManager.OpenHoudini(args))
	    {
		Log("Failed to start Houdini!");
		return false;
	    }

	    Log("Houdini started!");

	    return true;
	}

	void StartAndConnectToHoudini(HEU_SessionSyncInfo syncInfo)
	{
	    if (syncInfo != null && syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped)
	    {
		return;
	    }

	    if (!OpenHoudini())
	    {
		return;
	    }

	    // Now start thread to connect to it

	    HEU_SessionManager.RecreateDefaultSessionData();

	    if (syncInfo == null)
	    {
		HEU_SessionData sessionData = HEU_SessionManager.GetSessionData();
		if (sessionData != null)
		{
		    syncInfo = sessionData.GetOrCreateSessionSync();
		}
		else
		{
		    syncInfo = new HEU_SessionSyncInfo();
		}

		syncInfo._validForConnection = true;
	    }

	    syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Connecting;

	    _connectionSyncInfo = syncInfo;
	    Log("Connecting...");

	    syncInfo._timeStartConnection = Time.realtimeSinceStartup;
	    syncInfo._timeLastUpdate = Time.realtimeSinceStartup;
	}

	void UpdateSync()
	{
	    HEU_SessionSyncInfo syncInfo = GetSessionSync();

	    if (syncInfo != null)
	    {
		if (syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Connecting)
		{
		    UpdateConnecting(syncInfo);
		}
		else if (syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Connected)
		{
		    UpdateConnected(syncInfo);
		}
	    }
	}

	void UpdateConnecting(HEU_SessionSyncInfo syncInfo)
	{
	    if (syncInfo == null || syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Connecting)
	    {
		return;
	    }

	    if (Time.realtimeSinceStartup - syncInfo._timeLastUpdate >= CONNECTION_ATTEMPT_RATE)
	    {
		if (InternalConnect(_sessionMode, _pipeName,
		    HEU_PluginSettings.Session_Localhost, _port,
		    HEU_PluginSettings.Session_AutoClose,
		    HEU_PluginSettings.Session_Timeout, false))
		{
		    Log("Initializing...");
		    syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Initializing;

		    try
		    {
			HEU_SessionManager.InitializeDefaultSession();

			HEU_SessionManager.GetDefaultSession().GetSessionData().SetSessionSync(syncInfo);

			syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Connected;

			Log("Connected!");
		    }
		    catch (System.Exception ex)
		    {
			syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;
			Log("Connection errored!");
			Log(ex.ToString());

			Debug.Log(ex.ToString());
		    }
		    finally
		    {
			// Clear this to get out of the connection state
			_connectionSyncInfo = null;
		    }
		}
		else if (Time.realtimeSinceStartup - syncInfo._timeStartConnection >= CONNECTION_TIME_OUT)
		{
		    syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;
		    Log("Timed out trying to connect to Houdini."
			+ "\nCheck if Houdini is running and SessionSync is enabled."
			+ "\nCheck port or pipe name are correct by comparing with Houdini SessionSync panel.");
		}
		else
		{
		    // Try again in a bit
		    syncInfo._timeLastUpdate = Time.realtimeSinceStartup;
		}
	    }
	}

	void UpdateConnected(HEU_SessionSyncInfo syncInfo)
	{
	    if (!HEU_PluginSettings.SessionSyncAutoCook)
	    {
		return;
	    }

	    HEU_SessionBase session = HEU_SessionManager.GetDefaultSession();
	    if (session == null || !session.IsSessionValid() || !session.IsSessionSync())
	    {
		return;
	    }

	    if (session.ConnectionState == SessionConnectionState.CONNECTED)
	    {
		// Get latest use time from HAPI
		syncInfo._useHoudiniTime = session.GetUseHoudiniTime();

		// Use the above call to check validity of the session.
		// Note that once HAPI_IsSessionValid is improved, we might just use that.
		if (session.LastCallResultCode == HAPI_Result.HAPI_RESULT_INVALID_SESSION)
		{
		    // Bad session
		    Log("Session is invalid. Disconnecting.");
		    Disconnect(syncInfo);
		    return;
		}

		if (syncInfo._syncViewport)
		{
		    UpdateViewport(session, syncInfo);
		}
	    }
	    else
	    {
		if (syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Connected)
		{
		    // Bad session
		    Log("Session is invalid. Disconnecting.");
		    Disconnect(syncInfo);
		}
	    }
	}

	/// <summary>
	/// Synchronize the viewport between HAPI and Unity.
	/// </summary>
	void UpdateViewport(HEU_SessionBase session, HEU_SessionSyncInfo syncInfo)
	{
	    SceneView sceneView = SceneView.lastActiveSceneView;
	    if (sceneView == null)
	    {
		return;
	    }

	    // Get the latest viewport from HAPI, and check it agianst last update.
	    HAPI_Viewport viewHAPI = new HAPI_Viewport(true);
	    session.GetViewport(ref viewHAPI);

	    if (!HEU_HAPIUtility.IsViewportEqual(ref viewHAPI, ref syncInfo._viewportHAPI))
	    {
		// HAPI has changed. Update local viewport.

		Transform target = sceneView.camera.transform;

		// Account for left-handed coordinate system
		Vector3 pivot = new Vector3(-viewHAPI.position[0], viewHAPI.position[1], viewHAPI.position[2]);

		Quaternion rotation = new Quaternion(viewHAPI.rotationQuaternion[0], 
		    viewHAPI.rotationQuaternion[1], viewHAPI.rotationQuaternion[2], 
		    viewHAPI.rotationQuaternion[3]);
		Vector3 euler = rotation.eulerAngles;
		euler.y = -euler.y;
		euler.z = -euler.z;
		// Flip the camera direction for Unity camera
		rotation = Quaternion.Euler(euler) * Quaternion.Euler(0, 180f, 0);

		// TODO: use viewHAPI.offset to set camera distance
		// Unfortuantely no direct API to set the camera distance in Unity

		sceneView.LookAtDirect(pivot, rotation);
		sceneView.Repaint();

		// Store HAPI viewport for comparison on next update
		syncInfo._viewportHAPI = viewHAPI;
		syncInfo._viewportLocal = viewHAPI;
		syncInfo._viewportJustUpdated = true;
	    }
	    else
	    {
		// HAPI hasn't changed, so let's see if local viewport has

		Vector3 pivot = sceneView.pivot;
		Quaternion rotation = sceneView.rotation;
		float localDistance = sceneView.cameraDistance;

		// Generate the local HAPI_Viewport
		HAPI_Viewport viewLocal = new HAPI_Viewport(true);

		// Account for left-handed coordinate system
		viewLocal.position[0] = -pivot.x;
		viewLocal.position[1] = pivot.y;
		viewLocal.position[2] = pivot.z;

		// Flip the camera direction for Unity camera
		rotation = rotation * Quaternion.Euler(0, 180f, 0);
		Vector3 euler = rotation.eulerAngles;
		euler.y = -euler.y;
		euler.z = -euler.z;
		rotation = Quaternion.Euler(euler);

		viewLocal.rotationQuaternion[0] = rotation.x;
		viewLocal.rotationQuaternion[1] = rotation.y;
		viewLocal.rotationQuaternion[2] = rotation.z;
		viewLocal.rotationQuaternion[3] = rotation.w;

		viewLocal.offset = syncInfo._viewportHAPI.offset;

		if (!HEU_HAPIUtility.IsViewportEqual(ref viewLocal, ref syncInfo._viewportLocal))
		{
		    // Always store local viewport for comparison on next update
		    syncInfo._viewportLocal = viewLocal;

		    if (syncInfo._viewportJustUpdated)
		    {
			// Unity's SceneView internally updates the
			// viewport after setting it, so this makes sure
			// to update and store the latest change locally,
			// and skip sending it to HAPI
			syncInfo._viewportJustUpdated = false;
		    }
		    else
		    {
			session.SetViewport(ref viewLocal);

			// Store HAPI viewport for comparison on next update
			syncInfo._viewportHAPI = viewLocal;
		    }

		    //Debug.Log("Setting HAPI (from local)");
		    //Debug.LogFormat("Pos: {0}, {1}, {2}", viewLocal.position[0], viewLocal.position[1], viewLocal.position[2]);
		    //Debug.LogFormat("Rot: {0}, {1}, {2}, {3}", viewLocal.rotationQuaternion[0], 
			//viewLocal.rotationQuaternion[1], viewLocal.rotationQuaternion[2], viewLocal.rotationQuaternion[3]);
		    //Debug.LogFormat("Dis: {0}, sceneView.camDist: {1}", viewLocal.offset, sceneView.cameraDistance);
		}
	    }
	}

	HEU_SessionSyncInfo GetSessionSync()
	{
	    HEU_SessionSyncInfo syncInfo = _connectionSyncInfo;
	    if (syncInfo == null)
	    {
		HEU_SessionData sessionData = HEU_SessionManager.GetSessionData();
		if (sessionData != null)
		{
		    // On domain reload, re-acquire serialized SessionSync
		    // if session exists 
		    syncInfo = sessionData.GetOrCreateSessionSync();  
		}
	    }
	    return syncInfo;
	}

	void CreateCurve(string name)
	{
	    GameObject newCurveGO = HEU_HAPIUtility.CreateNewCurveAsset(name: name);
	    if (newCurveGO != null)
	    {
		HEU_Curve.PreferredNextInteractionMode = HEU_Curve.Interaction.ADD;
		HEU_EditorUtility.SelectObject(newCurveGO);
	    }
	}

	void CreateInput(string name)
	{
	    GameObject newCurveGO = HEU_HAPIUtility.CreateNewInputAsset(name: name);
	    if (newCurveGO != null)
	    {
		HEU_EditorUtility.SelectObject(newCurveGO);
	    }
	}

	void SetupUI()
	{
	    _backgroundStyle = new GUIStyle(GUI.skin.box);
	    RectOffset br = _backgroundStyle.margin;
	    br.top = 10;
	    br.bottom = 6;
	    br.left = 4;
	    br.right = 4;
	    _backgroundStyle.margin = br;

	    br = _backgroundStyle.padding;
	    br.top = 8;
	    br.bottom = 8;
	    br.left = 8;
	    br.right = 8;
	    _backgroundStyle.padding = br;

	    _eventMessageContent = new GUIContent("Log", "Status messages logged here."); 

	    _eventMessageStyle = new GUIStyle(EditorStyles.textArea);
	    _eventMessageStyle.richText = true;
	    _eventMessageStyle.normal.background = HEU_GeneralUtility.MakeTexture(1, 1, new Color(0, 0, 0, 1f));
	}

	void LoadNodeSyncDialog(string name)
	{
	    string fileName = "Test.hess";
	    string filePattern = "hess";
	    string newPath = EditorUtility.OpenFilePanel("Load Node Sync", fileName + "." + filePattern, filePattern);
	    if (newPath != null && !string.IsNullOrEmpty(newPath))
	    {
		CreateNodeSyncFromFile(newPath, name);
	    }
	}

	void CreateNodeSyncFromFile(string filePath, string name)
	{
	    HEU_SessionBase session = HEU_SessionManager.GetDefaultSession();
	    if (session == null || !session.IsSessionValid())
	    {
		return;
	    }

	    HAPI_NodeId parentNodeID = -1;
	    string nodeName = name;
	    HAPI_NodeId newNodeID = -1;

	    // This loads the node network from file, and returns the node that was created
	    // with newNodeID. It is either a SOP object, or a subnet object.
	    // The actual loader (HEU_ThreadedTaskLoadGeo) will deal with either case.
	    if (!session.LoadNodeFromFile(filePath, parentNodeID, nodeName, true, out newNodeID))
	    {
		Log(string.Format("Failed to load node network from file: {0}.", filePath));
		return;
	    }

	    // Wait until finished
	    if (!HEU_HAPIUtility.ProcessHoudiniCookStatus(session, nodeName))
	    {
		Log(string.Format("Failed to cook loaded node with name: {0}.", nodeName));
		return;
	    }

	    GameObject newGO = new GameObject(nodeName);

	    HEU_NodeSync nodeSync = newGO.AddComponent<HEU_NodeSync>();
	    nodeSync.InitializeFromHoudini(session, newNodeID, nodeName, filePath);
	}

	void DrawSyncNodes()
	{
	    
	}

	public void Log(string msg)
	{
	    lock (_log)
	    {
		_log.AppendLine(msg);
	    }
	}

	public string GetLog()
	{
	    lock (_log)
	    {
		return _log.ToString();
	    }
	}

	public void ClearLog()
	{
	    lock (_log)
	    {
		_log.Length = 0;
	    }
	}

	// DATA ---------------------------------------------------------------

	GUIStyle _backgroundStyle;

	private GUIStyle _eventMessageStyle;
	private Vector2 _eventMessageScrollPos = new Vector2();

	private GUIContent _eventMessageContent;

	// Initial sync info while connecting
	[SerializeField]
	private HEU_SessionSyncInfo _connectionSyncInfo;

	public SessionMode _sessionMode = SessionMode.Socket;

	public int _port = 0;
	public string _pipeName = "";

	const float CONNECTION_ATTEMPT_RATE = 5f;
	const float CONNECTION_TIME_OUT = 60f;

	[SerializeField]
	private StringBuilder _log = new StringBuilder();

	// Operator names for creating new nodes
	private string[] _nodeTypes =
	{
	    "SOP/output",
	    "Object/subnet",
	    "SOP/subnet",
	    "curve",
	    "input",
	};

	// Labels for the operator names above
	private string[] _nodeTypesLabels =
	{
	    "Experimental/Object/Geometry",
	    "Experimental/Object/Subnet",
	    "Experimental/SOP/Subnet",
	    "Curve",
	    "Input",
	};
    }

}