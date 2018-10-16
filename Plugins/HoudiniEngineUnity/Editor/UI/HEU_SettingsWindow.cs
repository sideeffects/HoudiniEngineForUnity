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
using UnityEditor;

namespace HoudiniEngineUnity
{
	/// <summary>
	/// Draws the plugin settings window.
	/// </summary>
	public class HEU_SettingsWindow : EditorWindow
	{
		private static bool _showGeneral = true;
		private static bool _showEnvironment = true;
		private static bool _showCooking = true;
		private static bool _showGeometry = true;
		private static bool _showSession = false;
		private static bool _showAdvanced = false;

		private static Vector2 _scrollPosition;

		private delegate bool DrawDetailsDelegate();

		private static Texture2D _refreshIcon;
		private static GUIContent _refreshContent;


		public static void ShowWindow()
		{
			bool bUtility = false;
			bool bFocus = true;
			string title = HEU_Defines.HEU_PRODUCT_NAME + " Plugin Settings";

			Rect rect = new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 600, 650);
			EditorWindow window = EditorWindow.GetWindowWithRect<HEU_SettingsWindow>(rect, bUtility, title, bFocus);
			window.autoRepaintOnSceneChange = true;
		}

		private void OnEnable()
		{
			_refreshIcon = Resources.Load("heu_reloadhdaIcon") as Texture2D;
			_refreshContent = new GUIContent("", _refreshIcon, "Reload the file.");
		}

		public void OnGUI()
		{
			bool guiEnabled = GUI.enabled;

			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

			using (var vs = new EditorGUILayout.VerticalScope(GUI.skin.box))
			{
				DrawSection(this, "GENERAL", this.DrawDetailsGeneral, ref _showGeneral);
				DrawSection(this, "ENVIRONMENT", this.DrawDetailsEnvironment, ref _showEnvironment);
				DrawSection(this, "COOKING", this.DrawDetailsCooking, ref _showCooking);
				DrawSection(this, "GEOMETRY", this.DrawDetailsGeometry, ref _showGeometry);
				DrawSection(this, "SESSION", this.DrawSessionSettings, ref _showSession);
				DrawSection(this, "ADVANCED", this.DrawAdvancedSettings, ref _showAdvanced);

				float buttonHeight = 25;
				float buttonWidth = 280;

				GUIStyle yellowButtonStyle = new GUIStyle(GUI.skin.button);
				yellowButtonStyle.normal.textColor = HEU_EditorUI.GetUISafeTextColorYellow();
				yellowButtonStyle.fontStyle = FontStyle.Bold;
				yellowButtonStyle.fontSize = 12;
				yellowButtonStyle.fixedHeight = buttonHeight;
				yellowButtonStyle.fixedWidth = buttonWidth;

				using (var hs = new EditorGUILayout.HorizontalScope())
				{
					GUILayout.FlexibleSpace();
					if (GUILayout.Button(HEU_EditorStrings.REVERT_SETTINGS, yellowButtonStyle))
					{
						if (HEU_EditorUtility.DisplayDialog(HEU_EditorStrings.REVERT_SETTINGS + "?", 
							"Are you sure you want to revert all " + HEU_Defines.HEU_PRODUCT_NAME + " plugin settings?",
							"Yes", "No"))
						{
							HEU_PluginStorage.ClearPluginData();
							this.Repaint();
						}
					}
					GUILayout.FlexibleSpace();
				}
			}

			EditorGUILayout.EndScrollView();

			GUI.enabled = guiEnabled;
		}

		private static bool DrawSection(HEU_SettingsWindow settingsWindow, string sectionLabel, DrawDetailsDelegate drawDetailsDelegate, ref bool foldoutState)
		{
			bool bChanged = false;

			HEU_EditorUI.BeginSection();
			{
				foldoutState = HEU_EditorUI.DrawFoldOut(foldoutState, sectionLabel);
				if (foldoutState)
				{
					HEU_EditorUI.DrawSeparator();
					EditorGUI.indentLevel++;

					using (var hs = new EditorGUILayout.HorizontalScope())
					{
						using (var vs = new EditorGUILayout.VerticalScope())
						{
							bChanged |= drawDetailsDelegate();
						}
					}

					EditorGUI.indentLevel--;
				}
			}
			HEU_EditorUI.EndSection();

			HEU_EditorUI.DrawSeparator();

			return bChanged;
		}

		private bool DrawDetailsGeneral()
		{
			bool bChanged = false;
			{
				float oldValue = HEU_PluginSettings.PinSize;
				float newValue = EditorGUILayout.DelayedFloatField("Pin Size", oldValue);
				if (newValue != oldValue)
				{
					HEU_PluginSettings.PinSize = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				Color oldValue = HEU_PluginSettings.PinColor;
				Color newValue = EditorGUILayout.ColorField("Pin Color", oldValue);
				if (newValue != oldValue)
				{
					HEU_PluginSettings.PinColor = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				float oldValue = HEU_PluginSettings.ImageGamma;
				float newValue = EditorGUILayout.DelayedFloatField("Texture Gamma", oldValue);
				if (newValue != oldValue)
				{
					HEU_PluginSettings.ImageGamma = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				Color oldValue = HEU_PluginSettings.LineColor;
				Color newValue = EditorGUILayout.ColorField("Line Color", oldValue);
				if (newValue != oldValue)
				{
					HEU_PluginSettings.LineColor = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldPath = HEU_PluginSettings.AssetCachePath;
				string newPath = EditorGUILayout.TextField("Houdini Asset Cache Path", oldPath);
				if (!newPath.Equals(oldPath))
				{
					HEU_PluginSettings.AssetCachePath = newPath;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.UseFullPathNamesForOutput;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Use Full Path Names For Output");
				if (!newValue.Equals(oldValue))
				{
					HEU_PluginSettings.UseFullPathNamesForOutput = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.SetCurrentThreadToInvariantCulture;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Set Current Thread To Invariant Culture", "Enabling this sets to use InvariantCutulre which fixes locale-specific parsing issues such as using comma instead of dot for decimals.");
				if (!newValue.Equals(oldValue))
				{
					HEU_PluginSettings.SetCurrentThreadToInvariantCulture = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldPath = HEU_PluginSettings.HoudiniDebugLaunchPath;
				string fileExt = "";

				EditorGUILayout.LabelField(new GUIContent("Houdini Debug Executable:", "Set Houdini executable to launch when opening debug scenes."));
				using (new EditorGUILayout.HorizontalScope())
				{
					string newPath = EditorGUILayout.DelayedTextField(oldPath);

					GUIStyle buttonStyle = HEU_EditorUI.GetNewButtonStyle_MarginPadding(0, 0);
					if (GUILayout.Button("...", buttonStyle, GUILayout.Width(30), GUILayout.Height(18)))
					{
						string panelMsg = "Select Houdini Executable";
#if UNITY_EDITOR_OSX
						panelMsg += " (.app)";
#endif

						string openFilePath = UnityEditor.EditorUtility.OpenFilePanel(panelMsg, newPath, fileExt);
						if (!string.IsNullOrEmpty(openFilePath))
						{
							newPath = openFilePath;
						}
					}

					if (!newPath.Equals(oldPath))
					{
						HEU_PluginSettings.HoudiniDebugLaunchPath = newPath;
						bChanged = true;
					}
				}
#if UNITY_EDITOR_OSX
				GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
				labelStyle.wordWrap = true;
				EditorGUILayout.LabelField("  On macOS, you'll need to select the path to the .app folder.\n  E.g. /Applications/Houdini/Houdini16.5.616/Houdini Core 16.5.616.app", labelStyle);
#endif
			}

			return bChanged;
		}

		private bool DrawDetailsEnvironment()
		{
			bool bChanged = false;

			{
				string oldPath = HEU_PluginSettings.HoudiniEngineEnvFilePath;
				string fileExt = "env";

				using (new EditorGUILayout.HorizontalScope())
				{
					string newPath = EditorGUILayout.DelayedTextField(new GUIContent("Houdini Env File", "Assets/ relative path to unity_houdini.env file containing environment paths"), oldPath);

					GUIStyle buttonStyle = HEU_EditorUI.GetNewButtonStyle_MarginPadding(0, 0);
					if (GUILayout.Button("...", buttonStyle, GUILayout.Width(30), GUILayout.Height(18)))
					{
						string openFilePath = UnityEditor.EditorUtility.OpenFilePanel("Select Houdini Env file", newPath, fileExt);
						if (!string.IsNullOrEmpty(openFilePath))
						{
							newPath = openFilePath;
						}
					}

					if (!newPath.Equals(oldPath))
					{
						HEU_PluginSettings.HoudiniEngineEnvFilePath = newPath;
						bChanged = true;
					}

					GUILayout.Space(5);

					if (GUILayout.Button(_refreshContent, buttonStyle, GUILayout.Width(40), GUILayout.Height(18)))
					{
						HEU_PluginStorage.Instance.LoadAssetEnvironmentPaths();
					}
				}
			}
			HEU_EditorUI.DrawSeparator();

			Dictionary<string, string> envMap = HEU_PluginStorage.Instance.GetEnvironmentPathMap();
			if (envMap == null)
			{
				HEU_EditorUI.DrawHeadingLabel("No environment mapped paths found!");
			}
			else
			{
				HEU_EditorUI.DrawHeadingLabel("Enviornment Mapped Paths:");
				EditorGUILayout.LabelField("The following mappings will be applied to assets loaded from outside the Assets/ folder.");

				foreach (KeyValuePair<string, string> pair in envMap)
				{
					EditorGUILayout.LabelField(string.Format("{0} = {1}", pair.Key, pair.Value));
				}

				HEU_EditorUI.DrawSeparator();
			}

			return bChanged;
		}

		private bool DrawDetailsCooking()
		{
			bool bChanged = false;

			{
				bool oldValue = HEU_PluginSettings.CookingEnabled;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Enable Cooking");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.CookingEnabled = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.CookingTriggersDownstreamCooks;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Cooking Triggers Downstream Cooks");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.CookingTriggersDownstreamCooks = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.PushUnityTransformToHoudini;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Push Unity Transform To Houdini");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.PushUnityTransformToHoudini = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.TransformChangeTriggersCooks;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Transform Change Triggers Cooks");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.TransformChangeTriggersCooks = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.CookTemplatedGeos;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Import Templated Geos");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.CookTemplatedGeos = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.SupportHoudiniBoxType;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Support Houdini Box Type");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.SupportHoudiniBoxType = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.SupportHoudiniSphereType;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Support Houdini Sphere Type");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.SupportHoudiniSphereType = newValue;
					bChanged = true;
				}
			}

			return bChanged;
		}

		private bool DrawDetailsGeometry()
		{
			bool bChanged = false;

			EditorGUIUtility.labelWidth = 250;

			{
				bool oldValue = HEU_PluginSettings.Curves_ShowInSceneView;
				bool newValue = HEU_EditorUI.DrawToggleLeft(oldValue, "Show Curves in Scene View");
				if (newValue != oldValue)
				{
					HEU_PluginSettings.Curves_ShowInSceneView = newValue;
					HEU_HoudiniAsset.SetCurvesVisibilityInScene(newValue);
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				float oldValue = HEU_PluginSettings.NormalGenerationThresholdAngle;
				float newValue = EditorGUILayout.DelayedFloatField("Normal Generation Threshold Angle", oldValue);
				if (newValue != oldValue)
				{
					HEU_PluginSettings.NormalGenerationThresholdAngle = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.TerrainSplatTextureDefault;
				string newValue = EditorGUILayout.DelayedTextField("Terrain Default Splat Texture", oldValue);
				if (!newValue.Equals(oldValue))
				{
					HEU_PluginSettings.TerrainSplatTextureDefault = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();

			EditorGUIUtility.labelWidth = 0;

			return bChanged;
		}

		private bool DrawSessionSettings()
		{
			bool bChanged = false;

			HEU_EditorUI.DrawSeparator();

			EditorGUIUtility.labelWidth = 250;
			{
				string oldValue = HEU_PluginSettings.Session_PipeName;
				string newValue = EditorGUILayout.DelayedTextField("Pipe Session Name", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.Session_PipeName = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.Session_Localhost;
				string newValue = EditorGUILayout.DelayedTextField("Socket Session Host Name", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.Session_Localhost = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				int oldValue = HEU_PluginSettings.Session_Port;
				int newValue = EditorGUILayout.DelayedIntField("Socket Session Port", oldValue);
				if (oldValue != newValue)
				{
					HEU_PluginSettings.Session_Port = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				float oldValue = HEU_PluginSettings.Session_Timeout;
				float newValue = EditorGUILayout.DelayedFloatField("Session Timeout", oldValue);
				if (oldValue != newValue)
				{
					HEU_PluginSettings.Session_Timeout = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				bool oldValue = HEU_PluginSettings.Session_AutoClose;
				bool newValue = EditorGUILayout.Toggle("Session Auto Close", oldValue);
				if (oldValue != newValue)
				{
					HEU_PluginSettings.Session_AutoClose = newValue;
					bChanged = true;
				}
			}

			EditorGUIUtility.labelWidth = 0;

			return bChanged;
		}

		private bool DrawAdvancedSettings()
		{
			bool bChanged = false;

			GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
			labelStyle.normal.textColor = HEU_EditorUI.GetUISafeTextColorYellow();
			EditorGUILayout.LabelField("Warning: Changing these values from default might result in HDAs not loading properly!", labelStyle, GUILayout.MinHeight(30));
			HEU_EditorUI.DrawSeparator();

			EditorGUIUtility.labelWidth = 250;
			{
				string oldValue = HEU_PluginSettings.HDAData_Name;
				string newValue = EditorGUILayout.DelayedTextField("HDA Data GameObject Name", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.HDAData_Name = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.EditorOnly_Tag;
				string newValue = EditorGUILayout.DelayedTextField("HDA Data GameObject Tag", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.EditorOnly_Tag = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.CollisionGroupName;
				string newValue = EditorGUILayout.DelayedTextField("Collision Group", oldValue);
				if(oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.CollisionGroupName = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.RenderedCollisionGroupName;
				string newValue = EditorGUILayout.DelayedTextField("Rendered Collision Group", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.RenderedCollisionGroupName = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.UnityMaterialAttribName;
				string newValue = EditorGUILayout.DelayedTextField("Unity Material Attribute", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.UnityMaterialAttribName = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.UnitySubMaterialAttribName;
				string newValue = EditorGUILayout.DelayedTextField("Unity Substance Material Attribute", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.UnitySubMaterialAttribName = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.UnityTagAttributeName;
				string newValue = EditorGUILayout.DelayedTextField("Unity Tag Attribute", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.UnityTagAttributeName = newValue;
					bChanged = true;
				}
			}
			HEU_EditorUI.DrawSeparator();
			{
				string oldValue = HEU_PluginSettings.UnityScriptAttributeName;
				string newValue = EditorGUILayout.DelayedTextField("Unity Script Attribute", oldValue);
				if (oldValue != newValue && !string.IsNullOrEmpty(newValue))
				{
					HEU_PluginSettings.UnityScriptAttributeName = newValue;
					bChanged = true;
				}
			}

			EditorGUIUtility.labelWidth = 0;

			return bChanged;
		}
	}

}   // HoudiniEngineUnity