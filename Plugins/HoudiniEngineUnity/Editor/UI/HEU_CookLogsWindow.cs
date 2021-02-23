using UnityEditor;
using UnityEngine;
using System.Collections;

namespace HoudiniEngineUnity
{
    public class HEU_CookLogsWindow : EditorWindow
    {

	private HEU_OutputLogUIComponent _outputLogUIComponent = null;

	private GUIContent _titleContent = new GUIContent("Cook Log", "Cook logs displayed here"); 
    
	[MenuItem("HoudiniEngine/Cook Progress Logs")]
	static void Init()
	{
	    bool bUtility = false;
	    bool bFocus = false;
	    string title = "Houdini Cook Logs";

	    HEU_CookLogsWindow window = EditorWindow.GetWindow<HEU_CookLogsWindow>(bUtility, title, bFocus);
	    InitSize(window);
	}

	public static void InitSize(HEU_CookLogsWindow window)
	{
	     window.minSize = new Vector2(300, 150);
	}
	
	private void SetupUI()
	{
	    if (_outputLogUIComponent == null)
	    {
		_outputLogUIComponent = new HEU_OutputLogUIComponent(_titleContent, OnClearLog);
	    }

	    _outputLogUIComponent.SetupUI();
	}
	
	void OnGUI()
	{
	    HEU_SessionBase sessionBase = HEU_SessionManager.GetDefaultSession();

	    if (sessionBase == null)
	    {
		return;
	    }

	    SetupUI();

	    if (_outputLogUIComponent != null)
	    {
		float setHeight = this.position.size.y - 60;
		_outputLogUIComponent.SetHeight(setHeight);
		_outputLogUIComponent.OnGUI(sessionBase.GetCookLogString());
	    }
	}

	private void OnClearLog()
	{
	    HEU_SessionBase sessionBase = HEU_SessionManager.GetDefaultSession();
	    sessionBase.ClearCookLog();
	}

	void OnInspectorUpdate()
	{
	    if (HEU_PluginSettings.WriteCookLogs)
	    {
	        Repaint();
	    }
	}
    }
} // HoudiniEngineUnity