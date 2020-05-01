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
using System.Threading;

using UnityEngine;

namespace HoudiniEngineUnity
{
    [System.Serializable]
    public class HEU_SessionSyncInfo
    {
	public enum Status
	{
	    Stopped,
	    Started,
	    Connecting,
	    Initializing,
	    Connected
	}

	private int _status = 0;

	// Thread-safe access
	public Status SyncStatus
	{
	    get
	    {
		int istatus = Interlocked.CompareExchange(ref _status, 0, 0);
		return (Status)istatus;
	    }
	    set
	    {
		int istatus = (int)value;
		Interlocked.Exchange(ref _status, istatus);
	    }
	}

	public enum SessionType
	{
	    Pipe,
	    Socket
	}

	public SessionType _sessionType = SessionType.Pipe;

	public int _port;
	public string _pipeName;

	[SerializeField]
	private StringBuilder _log = new StringBuilder();

	[SerializeField]
	public bool _useHoudiniTime;


	public void Reset()
	{
	    Debug.Log("HEU_SessionSyncInfo::Reset");

	    _port = HEU_PluginSettings.Session_Port;
	    _pipeName = HEU_PluginSettings.Session_PipeName;

	    _log = new StringBuilder();
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

	public void Update()
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

	    // TODO: integrate this check into session.IsSessionValid() above?
	    if (session.ConnectedState == HEU_SessionBase.SessionConnectionState.CONNECTED)
	    {
		// Get latest use time from HAPI
		_useHoudiniTime = session.GetUseHoudiniTime();
	    }
	}

	public void SetHuseHoudiniTime(bool enable)
	{
	    _useHoudiniTime = enable;

	    HEU_SessionBase session = HEU_SessionManager.GetDefaultSession();
	    if (session != null)
	    {
		session.SetUseHoudiniTime(enable);
	    }
	}
    }

}