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

#define EXPERIMENTAL

using UnityEngine;
using UnityEditor;
using System.Threading;
using System.Collections.Concurrent;
using System.Text;

namespace HoudiniEngineUnity
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Typedefs (copy these from HEU_Common.cs)
    using HAPI_NodeId = System.Int32;

#if EXPERIMENTAL

    public class HEU_SessionSyncWindow : EditorWindow
    {
	public static void ShowWindow()
	{
	    EditorWindow.GetWindow(typeof(HEU_SessionSyncWindow), false, "HEngine SessionSync");
	}

	private void OnEnable()
	{
	    ReInitialize();
	}

	void ReInitialize()
	{
	    _sessionType = SessionType.Pipe;

	    _port = HEU_PluginSettings.Session_Port;
	    _pipeName = HEU_PluginSettings.Session_PipeName;

	    _log = new StringBuilder();
	}

	void OnGUI()
	{
	    SetupUI();

	    HEU_SessionSyncInfo syncInfo = null;
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

	    if (syncInfo != null)
	    {
		syncInfo.Update();
	    }

	    EditorGUI.BeginChangeCheck();

	    bool bSessionStarted = (syncInfo != null && syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped);

	    HEU_HoudiniAssetUI.DrawHeaderSection();

	    if (syncInfo != null)
	    {
		EditorGUILayout.LabelField("Status: " + syncInfo.SyncStatus);
	    }
	    else
	    {
		EditorGUILayout.LabelField("No active session");
	    }

	    EditorGUILayout.Separator();
	    
	    EditorGUI.indentLevel++;

	    using (new EditorGUILayout.HorizontalScope())
	    {
		using (new EditorGUI.DisabledScope(bSessionStarted))
		{
		    if (GUILayout.Button("Start Houdini"))
		    {
			StartAndConnectToHoudini(syncInfo);
		    }
		    else if (GUILayout.Button("Connect To Houdini"))
		    {
			ConnectSessionSync(syncInfo);
		    }
		}
	    }

	    using (new EditorGUI.DisabledScope(syncInfo == null || !bSessionStarted))
	    {
		if (GUILayout.Button("Disconnect"))
		{
		    Disconnect(syncInfo);
		}
	    }

	    ProcessConnectingToHoudini(syncInfo);

	    EditorGUILayout.Separator();

	    EditorGUILayout.LabelField("Connection");

	    using (new EditorGUI.DisabledScope(bSessionStarted))
	    {
		_sessionType = (SessionType)EditorGUILayout.EnumPopup("Type", _sessionType);

		EditorGUI.indentLevel++;
		if (_sessionType == SessionType.Pipe)
		{
		    string newPipeName = EditorGUILayout.DelayedTextField("Pipe Name", _pipeName);
		    if (_pipeName != newPipeName)
		    {
			HEU_PluginSettings.Session_PipeName = newPipeName;
			_pipeName = newPipeName;
		    }
		}
		else if (_sessionType == SessionType.Socket)
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


	    bool result = InternalConnect(_sessionType, _pipeName,
		HEU_PluginSettings.Session_Localhost, _port,
		HEU_PluginSettings.Session_AutoClose, HEU_PluginSettings.Session_Timeout,
		syncInfo);

	    if (result)
	    {
		try
		{
		    HEU_SessionManager.InitializeDefaultSession();

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
	    SessionType sessionType, string pipeName, 
	    string ip, int port, bool autoClose, float timeout, 
	    HEU_SessionSyncInfo syncInfo)
	{
	    if (sessionType == SessionType.Pipe)
	    {
		return HEU_SessionManager.ConnectSessionSyncUsingThriftPipe(
		    pipeName,
		    autoClose,
		    timeout,
		    sessionSync: syncInfo);
	    }
	    else
	    {
		return HEU_SessionManager.ConnectSessionSyncUsingThriftSocket(
		    ip,
		    port,
		    autoClose,
		    timeout,
		    sessionSync: syncInfo);
	    }
	}

	void Disconnect(HEU_SessionSyncInfo syncInfo)
	{
	    if (syncInfo != null)
	    {
		syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;
	    }

	    if (_taskThread != null)
	    {
		Log("Stopping connection thread...");
		_taskThread.Join();
		_taskThread = null;
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
	    if (_sessionType == SessionType.Pipe)
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

	    if (_taskThread != null)
	    {
		// Task already in process. Wait
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
	    }

	    syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Connecting;

	    _taskThread = new Thread(() => ConnectToHoudini(_sessionType, _pipeName, 
		HEU_PluginSettings.Session_Localhost, _port,
		HEU_PluginSettings.Session_AutoClose, HEU_PluginSettings.Session_Timeout,
		syncInfo));
	    _taskThread.Priority = System.Threading.ThreadPriority.Lowest;
	    _taskThread.IsBackground = true;
	    _taskThread.Start();
	}

	void ProcessConnectingToHoudini(HEU_SessionSyncInfo syncInfo)
	{
	    if (syncInfo != null &&
		syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Initializing)
	    {
		try
		{
		    HEU_SessionManager.InitializeDefaultSession();

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

		if (_taskThread != null)
		{
		    _taskThread.Join();
		    _taskThread = null;
		}
	    }
	}

	void ConnectToHoudini(SessionType sessionType, 
	    string pipeName, string ip, int port, bool autoClose, float timeout, 
	    HEU_SessionSyncInfo syncInfo)
	{
	    Log("Connecting...");

	    while (syncInfo != null &&
		syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Connecting)
	    {
		if (InternalConnect(sessionType, pipeName, ip, port, autoClose, timeout, syncInfo))
		{
		    Log("Initializing...");
		    syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Initializing;
		}
		else
		{
		    Thread.Sleep(1000);
		}
	    }
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

	public class HEU_SessionSyncTask
	{
	    public enum TaskAction
	    {
		Start,
		Connect,
		Stop
	    }

	    public TaskAction _action;
	    public SessionType _sessionType;
	    public string _pipeName;
	    public int _port;
	}

	private Thread _taskThread;

	public enum SessionType
	{
	    Pipe,
	    Socket
	}

	public SessionType _sessionType = SessionType.Pipe;

	public int _port = 0;
	public string _pipeName = "";

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

#endif

}