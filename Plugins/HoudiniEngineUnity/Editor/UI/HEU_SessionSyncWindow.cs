
using UnityEngine;
using UnityEditor;
using System.Threading;
using System.Collections.Concurrent;

namespace HoudiniEngineUnity
{

    public class HEU_SessionSyncWindow : EditorWindow
    {
	public static void ShowWindow()
	{
	    EditorWindow.GetWindow(typeof(HEU_SessionSyncWindow), false, "HEngine SessionSync");
	}

	private void OnEnable()
	{
	    if (_syncInfo == null)
	    {
		HEU_SessionData sessionData = HEU_SessionManager.GetSessionData();
		if (sessionData != null)
		{
		    _syncInfo = sessionData.GetOrCreateSessionSync();
		}
		else
		{
		    // On code refresh without session, we have to manually create it
		    _syncInfo = new HEU_SessionSyncInfo();
		    _syncInfo.Reset();
		}
	    }
	}

	void OnGUI()
	{
	    if (_syncInfo == null)
	    {
		return;
	    }

	    SetupUI();
	    UpdateSync();

	    HEU_HoudiniAssetUI.DrawHeaderSection();

	    //EditorGUILayout.LabelField("Houdini Engine SessionSync");
	    EditorGUILayout.LabelField("Status: " + _syncInfo.SyncStatus);

	    EditorGUILayout.Separator();
	    
	    EditorGUI.indentLevel++;

	    using (new EditorGUILayout.HorizontalScope())
	    {
		using (new EditorGUI.DisabledScope(_syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped))
		{
		    if (GUILayout.Button("Start Houdini"))
		    {
			StartAndConnectToHoudini();
		    }
		    else if (GUILayout.Button("Connect To Houdini"))
		    {
			ConnectSessionSync();
		    }
		}
	    }

	    using (new EditorGUI.DisabledScope(_syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Stopped))
	    {
		if (GUILayout.Button("Stop"))
		{
		    Stop();
		}
	    }

	    ProcessConnectingToHoudini();

	    EditorGUILayout.Separator();

	    EditorGUILayout.LabelField("Connection");

	    using (new EditorGUI.DisabledScope(_syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped))
	    {
		_syncInfo._sessionType = (HEU_SessionSyncInfo.SessionType)EditorGUILayout.EnumPopup("Type", _syncInfo._sessionType);

		EditorGUI.indentLevel++;
		if (_syncInfo._sessionType == HEU_SessionSyncInfo.SessionType.Pipe)
		{
		    _syncInfo._pipeName = EditorGUILayout.DelayedTextField("Pipe Name", _syncInfo._pipeName);
		}
		else if (_syncInfo._sessionType == HEU_SessionSyncInfo.SessionType.Socket)
		{
		    //_syncInfo._ip = EditorGUILayout.DelayedTextField("Address", _syncInfo._ip);
		    _syncInfo._port = EditorGUILayout.DelayedIntField("Port", _syncInfo._port);
		}
		EditorGUI.indentLevel--;
	    }

	    EditorGUILayout.Separator();

	    using (new EditorGUI.DisabledScope(_syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Connected))
	    {
		EditorGUILayout.LabelField("Sync Settings");

		EditorGUI.indentLevel++;

		HEU_PluginSettings.SessionSyncAutoCook = HEU_EditorUI.DrawToggleLeft(HEU_PluginSettings.SessionSyncAutoCook, "Sync with Houdini cook");

		bool enableHoudiniTime = HEU_EditorUI.DrawToggleLeft(_syncInfo._useHoudiniTime, "Cook using Houdini time");
		if (_syncInfo._useHoudiniTime != enableHoudiniTime)
		{
		    _syncInfo.SetHuseHoudiniTime(enableHoudiniTime);
		}

		EditorGUI.indentLevel--;
	    }

	    EditorGUI.indentLevel--;

	    EditorGUILayout.Separator();

	    EditorGUILayout.LabelField("Nodes");

	    using (new EditorGUI.DisabledScope(_syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Stopped))
	    {
		if (GUILayout.Button("Create Curve"))
		{
		    CreateCurve();
		}

		if (GUILayout.Button("Create Input"))
		{
		    CreateInput();
		}

		if (GUILayout.Button("Create Geometry (experimental)"))
		{
		    CreateNode();
		}
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
			_syncInfo.ClearLog();
		    }
		}

		string logMsg = _syncInfo.GetLog();

		using (var scrollViewScope = new EditorGUILayout.ScrollViewScope(_eventMessageScrollPos, GUILayout.Height(120)))
		{
		    _eventMessageScrollPos = scrollViewScope.scrollPosition;

		    GUILayout.Label(logMsg, _eventMessageStyle);
		}
	    }
	}

	void ConnectSessionSync()
	{
	    if (_syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped)
	    {
		return;
	    }

	    _syncInfo.Log("Connecting To Houdini...");

	    HEU_SessionManager.RecreateDefaultSessionData();

	    bool result = InternalConnect(_syncInfo._sessionType, _syncInfo._pipeName,
		HEU_PluginSettings.Session_Localhost, _syncInfo._port,
		HEU_PluginSettings.Session_AutoClose, HEU_PluginSettings.Session_Timeout,
		_syncInfo);

	    if (result)
	    {
		try
		{
		    HEU_SessionManager.InitializeDefaultSession();

		    _syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Connected;
		    _syncInfo.Log("Connected!");
		}
		catch (HEU_HoudiniEngineError ex)
		{
		    _syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;

		    _syncInfo.Log("Connection errored!");
		    _syncInfo.Log(ex.ToString());
		}
	    }
	    else
	    {
		_syncInfo.Log("Connection failed!");
	    }
	}

	bool InternalConnect(
	    HEU_SessionSyncInfo.SessionType sessionType, string pipeName, 
	    string ip, int port, bool autoClose, float timeout, 
	    HEU_SessionSyncInfo syncInfo)
	{
	    if (sessionType == HEU_SessionSyncInfo.SessionType.Pipe)
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

	void Stop()
	{
	    _syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;

	    if (_taskThread != null)
	    {
		_syncInfo.Log("Stopping connection thread...");
		_taskThread.Join();
		_taskThread = null;
	    }

	    if (HEU_SessionManager.CloseDefaultSession())
	    {
		_syncInfo.Log("Connection closed!");
	    }
	    else
	    {
		_syncInfo.Log("Failed to close session! ");
	    }
	}

	bool OpenHoudini()
	{
	    string args = "";

	    // Form argument
	    if (_syncInfo._sessionType == HEU_SessionSyncInfo.SessionType.Pipe)
	    {
		args = string.Format("-hess=pipe:{0}", _syncInfo._pipeName);
	    }
	    else
	    {
		args = string.Format("-hess=port:{0}", _syncInfo._port);
	    }

	    _syncInfo.Log("Opening Houdini...");

	    if (!HEU_SessionManager.OpenHoudini(args))
	    {
		_syncInfo.Log("Failed to start Houdini!");
		return false;
	    }

	    _syncInfo.Log("Houdini started!");

	    return true;
	}

	void StartAndConnectToHoudini()
	{
	    if (_syncInfo.SyncStatus != HEU_SessionSyncInfo.Status.Stopped)
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

	    _syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Connecting;

	    // Now start thread to connect to it

	    HEU_SessionManager.RecreateDefaultSessionData();

	    _taskThread = new Thread(() => ConnectToHoudini(_syncInfo._sessionType, _syncInfo._pipeName, 
		HEU_PluginSettings.Session_Localhost, _syncInfo._port,
		HEU_PluginSettings.Session_AutoClose, HEU_PluginSettings.Session_Timeout,
		_syncInfo));
	    _taskThread.Priority = System.Threading.ThreadPriority.Lowest;
	    _taskThread.IsBackground = true;
	    _taskThread.Start();
	}

	void ProcessConnectingToHoudini()
	{
	    if (_syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Initializing)
	    {
		try
		{
		    HEU_SessionManager.InitializeDefaultSession();

		    _syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Connected;
		    _syncInfo.Log("Connected!");
		}
		catch (HEU_HoudiniEngineError ex)
		{
		    _syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Stopped;
		    _syncInfo.Log("Connection errored!");
		    _syncInfo.Log(ex.ToString());
		}

		if (_taskThread != null)
		{
		    _taskThread.Join();
		    _taskThread = null;
		}
	    }
	}

	void ConnectToHoudini(HEU_SessionSyncInfo.SessionType sessionType, 
	    string pipeName, string ip, int port, bool autoClose, float timeout, 
	    HEU_SessionSyncInfo syncInfo)
	{
	    _syncInfo.Log("Connecting...");
	    while (_syncInfo.SyncStatus == HEU_SessionSyncInfo.Status.Connecting)
	    {
		if (InternalConnect(sessionType, pipeName, ip, port, autoClose, timeout, syncInfo))
		{
		    _syncInfo.Log("Initializing...");
		    _syncInfo.SyncStatus = HEU_SessionSyncInfo.Status.Initializing;
		}
		else
		{
		    Thread.Sleep(1000);
		}
	    }
	}

	void CreateNode()
	{
	    HEU_SessionBase session = HEU_SessionManager.GetDefaultSession();
	    if (session == null || !session.IsSessionValid())
	    {
		return;
	    }

	    int outNodeID = -1;

	    GameObject go = HEU_HAPIUtility.CreateEmptyGeoNode(session, "test", out outNodeID);
	    if (go == null)
	    {
		//Debug.LogErrorFormat("Unable to create merge SOP node for connecting input assets.");
		return;
	    }

	    Debug.Log("Created node!");
	}

	void CreateCurve()
	{
	    GameObject newCurveGO = HEU_HAPIUtility.CreateNewCurveAsset();
	    if (newCurveGO != null)
	    {
		HEU_Curve.PreferredNextInteractionMode = HEU_Curve.Interaction.ADD;
		HEU_EditorUtility.SelectObject(newCurveGO);
	    }
	}

	void CreateInput()
	{
	    GameObject newCurveGO = HEU_HAPIUtility.CreateNewInputAsset();
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

	void UpdateSync()
	{
	    if(_syncInfo != null)
	    {
		_syncInfo.Update();
	    }
	}

	// DATA ---------------------------------------------------------------

	[SerializeField]
	HEU_SessionSyncInfo _syncInfo;

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
	    public HEU_SessionSyncInfo.SessionType _sessionType;
	    public string _pipeName;
	    public int _port;
	}

	private Thread _taskThread;
	
    }

}