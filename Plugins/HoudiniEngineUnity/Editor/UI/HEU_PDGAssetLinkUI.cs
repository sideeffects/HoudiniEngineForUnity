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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;


namespace HoudiniEngineUnity
{

	[CustomEditor(typeof(HEU_PDGAssetLink))]
	public class HEU_PDGAssetLinkUI : Editor
	{
		private void OnEnable()
		{
			_assetLink = target as HEU_PDGAssetLink;
		}

		public override void OnInspectorGUI()
		{
			if (_assetLink == null)
			{
				DrawNoAssetLink();
				return;
			}

			// Always hook into asset UI callback. This could have got reset on code refresh.
			_assetLink._repaintUIDelegate = RefreshUI;

			serializedObject.Update();

			SetupUI();

			using (new EditorGUILayout.VerticalScope(_backgroundStyle))
			{
				SerializedProperty assetGOProp = HEU_EditorUtility.GetSerializedProperty(serializedObject, "_assetGO");
				if (assetGOProp != null)
				{
					EditorGUILayout.PropertyField(assetGOProp, _assetGOLabelContent, false);
				}

				//EditorGUILayout.PrefixLabel("Asset Name: " + _assetLink.AssetName);

				HEU_PDGAssetLink.LinkState validState = _assetLink.AssetLinkState;

				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUILayout.LabelField("Asset Link State: " + validState);

					GUILayout.Space(10);

					if (GUILayout.Button(_refreshContent, GUILayout.Width(100)))
					{
						_assetLink.Refresh();
					}

					if (GUILayout.Button(_resetContent, GUILayout.Width(100)))
					{
						_assetLink.Reset();
					}
				}

				if (validState == HEU_PDGAssetLink.LinkState.ERROR_NOT_LINKED)
				{
					EditorGUILayout.LabelField("Failed to link with HDA. Unable to proceed. Try rebuilding asset.");
				}
				else if(validState == HEU_PDGAssetLink.LinkState.INACTIVE)
				{
					_assetLink.Refresh();
				}
				else if (validState == HEU_PDGAssetLink.LinkState.LINKED)
				{
					EditorGUILayout.Space();

					// Dropdown list of TOP network names
					DrawSelectedTOPNetwork();

					EditorGUILayout.Space();

					// Dropdown list of TOP nodes
					DrawSelectedTOPNode();
				}
			}
		}

		private void DrawNoAssetLink()
		{
			HEU_EditorUI.DrawSeparator();

			GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
			labelStyle.fontStyle = FontStyle.Bold;
			labelStyle.normal.textColor = HEU_EditorUI.IsEditorDarkSkin() ? Color.yellow : Color.red;
			EditorGUILayout.LabelField("Houdini Engine Asset - no HEU_PDGAssetLink found!", labelStyle);

			HEU_EditorUI.DrawSeparator();
		}

		private void DrawSelectedTOPNetwork()
		{
			int numTopNodes = _assetLink._topNetworkNames.Length;
			if (numTopNodes > 0)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUILayout.PrefixLabel(_topNetworkChooseLabel);

					int numTOPs = _assetLink._topNetworkNames.Length;

					int selectedIndex = Mathf.Clamp(_assetLink.SelectedTOPNetwork, 0, numTopNodes - 1);
					int newSelectedIndex = EditorGUILayout.Popup(selectedIndex, _assetLink._topNetworkNames, GUILayout.Width(300), GUILayout.Height(40));
					if (newSelectedIndex != selectedIndex)
					{
						_assetLink.SelectTOPNetwork(newSelectedIndex);
					}
				}
			}
			else
			{
				EditorGUILayout.PrefixLabel(_topNetworkNoneLabel);
			}
		}

		private void DrawSelectedTOPNode()
		{
			HEU_TOPNetworkData topNetworkData = _assetLink.GetSelectedTOPNetwork();
			if (topNetworkData == null)
			{
				return;
			}

			//HEU_EditorUI.BeginSection();
			{
				int numTopNodes = topNetworkData._topNodeNames.Length;
				if (numTopNodes > 0)
				{
					using (new EditorGUILayout.HorizontalScope())
					{
						EditorGUILayout.PrefixLabel(_topNodeChooseLabel);

						int selectedIndex = Mathf.Clamp(topNetworkData._selectedTOPIndex, 0, numTopNodes);
						int newSelectedIndex = EditorGUILayout.Popup(selectedIndex, topNetworkData._topNodeNames, GUILayout.Width(300), GUILayout.Height(40));
						if (newSelectedIndex != selectedIndex)
						{
							_assetLink.SelectTOPNode(topNetworkData, newSelectedIndex);
						}
					}
				}
				else
				{
					EditorGUILayout.PrefixLabel(_topNodeNoneLabel);
				}

				HEU_TOPNodeData topNode = _assetLink.GetSelectedTOPNode();
				if (topNode != null)
				{
					bool autoLoad = topNode._autoLoad;
					autoLoad = EditorGUILayout.ToggleLeft("AutoLoad Results", autoLoad);
					if (autoLoad != topNode._autoLoad)
					{
						topNode._autoLoad = autoLoad;
					}

					EditorGUILayout.LabelField("PDG State: " + topNode._pdgState);
					EditorGUILayout.LabelField("Number of tasks: " + topNode._totalWorkItems);
					EditorGUILayout.LabelField("Cooked tasks: " + topNode._cookedWorkItems);
					EditorGUILayout.LabelField("Errored tasks: " + topNode._erroredWorkItems);

					using (new EditorGUILayout.HorizontalScope())
					{
						if (GUILayout.Button("Dirty", GUILayout.MaxWidth(_maxButtonWidth)))
						{
							_assetLink.DirtyTOPNode(topNode);
						}

						if (GUILayout.Button("Cook", GUILayout.MaxWidth(_maxButtonWidth)))
						{
							_assetLink.CookTOPNode(topNode);
						}
					}
				}
			}
			//HEU_EditorUI.EndSection();
		}

		private void SetupUI()
		{
			_backgroundStyle = new GUIStyle(GUI.skin.GetStyle("box"));
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
		}

		public void RefreshUI()
		{
			Repaint();
		}

		//	DATA ------------------------------------------------------------------------------------------------------

		public HEU_PDGAssetLink _assetLink;

		private GUIStyle _backgroundStyle;

		private GUIContent _assetGOLabelContent = new GUIContent("HDA Asset", "The HDA linked to this.");

		private GUIContent _resetContent = new GUIContent("Reset", "Reset the state and generated items. Updates from linked HDA.");
		private GUIContent _refreshContent = new GUIContent("Refresh", "Refresh the state and UI.");

		private GUIContent _topNetworkChooseLabel = new GUIContent("TOP Network");
		private GUIContent _topNetworkNoneLabel = new GUIContent("TOP Network: None");

		private GUIContent _topNodeChooseLabel = new GUIContent("TOP Node");
		private GUIContent _topNodeNoneLabel = new GUIContent("TOP Node: None");

		private const int _maxButtonWidth = 150;
	}

}   // HoudiniEngineUnity