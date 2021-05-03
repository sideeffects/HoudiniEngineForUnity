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

using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System;

namespace HoudiniEngineUnity
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Typedefs (copy these from HEU_Common.cs)
    using HAPI_NodeId = System.Int32;
    using HAPI_PartId = System.Int32;
    using HAPI_ParmId = System.Int32;
    using HAPI_StringHandle = System.Int32;

    [System.Serializable]
    public class CurveNodeData : IEquivable<CurveNodeData>
    {
	[SerializeField]
	public Vector3 position = Vector3.zero;

	[SerializeField]
	public Vector3 rotation = Vector3.zero;

	[SerializeField]
	public Vector3 scale = Vector3.one;

	public CurveNodeData()
	{
	}

	public CurveNodeData(Vector3 position)
	{
	    this.position = position;
	}

	public CurveNodeData(Vector3 position, Quaternion rotation)
	{
	    this.position = position;
	    this.rotation = rotation.eulerAngles;
	}

	public CurveNodeData(Vector3 position, Quaternion rotation, Vector3 scale)
	{ 
	    this.position = position;
	    this.rotation = rotation.eulerAngles;
	    this.scale = scale;
	}

	public CurveNodeData(CurveNodeData other)
	{
	    this.position = other.position;
	    this.rotation = other.rotation;
	    this.scale = other.scale;
	}

	public Quaternion GetRotation()
	{
	    return Quaternion.Euler(this.rotation);
	}

	public bool IsEquivalentTo(CurveNodeData other)
	{
	    bool bResult = true;

	    string header = "CurveNodeData";

	    if (other == null)
	    {
		HEU_Logger.LogError(header + " Not equivalent");
		return false;
	    }

	    HEU_TestHelpers.AssertTrueLogEquivalent(this.position, other.position, ref bResult, header, "position");
	    HEU_TestHelpers.AssertTrueLogEquivalent(this.rotation, other.rotation, ref bResult, header, "rotation");
	    HEU_TestHelpers.AssertTrueLogEquivalent(this.scale, other.scale, ref bResult, header, "scale");

	    return bResult;
	    
	}
    };


    /// <summary>
    /// Contains data and logic for curve node drawing and editing.
    /// </summary>
    public class HEU_Curve : ScriptableObject, IEquivable<HEU_Curve>
    {
	// DATA -------------------------------------------------------------------------------------------------------

	[SerializeField]
	private HAPI_NodeId _geoID;

	public HAPI_NodeId GeoID { get { return _geoID; } }

	[SerializeField]
	private List<CurveNodeData> _curveNodeData = new List<CurveNodeData>();

	public List<CurveNodeData> CurveNodeData { get { return _curveNodeData; }}

	[SerializeField]
	private Vector3[] _vertices;

	[SerializeField]
	private bool _isEditable;

	public bool IsEditable() { return _isEditable; }

	[SerializeField]
	private HEU_Parameters _parameters;

	public HEU_Parameters Parameters { get { return _parameters; } }

	[SerializeField]
	private bool _bUploadParameterPreset;

	public void SetUploadParameterPreset(bool bValue) { _bUploadParameterPreset = bValue; }

	[SerializeField]
	private string _curveName;

	public string CurveName { get { return _curveName; } }

	public GameObject _targetGameObject;

	[SerializeField]
	private bool _isGeoCurve;

	public bool IsGeoCurve() { return _isGeoCurve; }

	public enum CurveEditState
	{
	    INVALID,
	    GENERATED,
	    EDITING,
	    REQUIRES_GENERATION
	}
	[SerializeField]
	private CurveEditState _editState;

	public CurveEditState EditState { get { return _editState; } }

	// Types of interaction with this curve. Used by Editor.
	public enum Interaction
	{
	    VIEW,
	    ADD,
	    EDIT
	}

	// Preferred interaction mode when this a curve selected. Allows for quick access for curve editing.
	public static Interaction PreferredNextInteractionMode = Interaction.VIEW;

	public enum CurveDrawCollision
	{
	    COLLIDERS,
	    LAYERMASK
	}

	[SerializeField]
	private HEU_HoudiniAsset _parentAsset;

	public HEU_HoudiniAsset ParentAsset { get { return _parentAsset; }}

	// LOGIC ------------------------------------------------------------------------------------------------------

	public static HEU_Curve CreateSetupCurve(HEU_HoudiniAsset parentAsset, bool isEditable, string curveName, HAPI_NodeId geoID, bool bGeoCurve)
	{
	    HEU_Curve newCurve = ScriptableObject.CreateInstance<HEU_Curve>();
	    newCurve._isEditable = isEditable;
	    newCurve._curveName = curveName;
	    newCurve._geoID = geoID;
	    newCurve.SetEditState(CurveEditState.INVALID);
	    newCurve._isGeoCurve = bGeoCurve;
	    newCurve._parentAsset = parentAsset;

	    if (parentAsset.SerializedMetaData != null && parentAsset.SerializedMetaData.SavedCurveNodeData != null && parentAsset.SerializedMetaData.SavedCurveNodeData.ContainsKey(curveName) && !parentAsset.CurveDisableScaleRotation)
	    {
		newCurve._curveNodeData = parentAsset.SerializedMetaData.SavedCurveNodeData[curveName];
		parentAsset.SerializedMetaData.SavedCurveNodeData.Remove(curveName);
	    }

	    parentAsset.AddCurve(newCurve);
	    return newCurve;
	}

	public void DestroyAllData(bool bIsRebuild = false)
	{
	    if (_parameters != null)
	    {
		_parameters.CleanUp();
		_parameters = null;
	    }

	    if (_isGeoCurve && _targetGameObject != null)
	    {
		HEU_HAPIUtility.DestroyGameObject(_targetGameObject);
		_targetGameObject = null;
	    }

	    if (bIsRebuild && _parentAsset != null && _parentAsset.SerializedMetaData.SavedCurveNodeData != null && !_parentAsset.CurveDisableScaleRotation)
	    {
		_parentAsset.SerializedMetaData.SavedCurveNodeData.Add(_curveName, _curveNodeData);
	    }
	}

	public void SetCurveName(string name)
	{
	    _curveName = name;
	    if (_targetGameObject != null)
	    {
		_targetGameObject.name = name;
	    }
	}

	public void UploadParameterPreset(HEU_SessionBase session, HAPI_NodeId geoID, HEU_HoudiniAsset parentAsset)
	{
	    // TODO FIXME
	    // This fixes up the geo IDs for curves, and upload parameter values to Houdini.
	    // This is required for curves in saved scenes, as its parameter data is not part of the parent asset's
	    // parameter preset. Also the _geoID and parameters._nodeID could be different so uploading the
	    // parameter values before cooking would not be valid for those IDs. This waits until after cooking
	    // to then upload and cook just the curve.
	    // Admittedly this is a temporary solution until a proper workaround is in place. Ideally for an asset reload
	    // the object node and geo node names can be used to match up the IDs and then parameter upload can happen
	    // before cooking.

	    _geoID = geoID;

	    if (_parameters != null)
	    {
		_parameters._nodeID = geoID;

		if (_bUploadParameterPreset)
		{
		    _parameters.UploadPresetData(session);
		    _parameters.UploadValuesToHoudini(session, parentAsset);

		    HEU_HAPIUtility.CookNodeInHoudini(session, geoID, false, _curveName);

		    _bUploadParameterPreset = false;
		}
	    }
	}

	public void ResetCurveParameters(HEU_SessionBase session, HEU_HoudiniAsset parentAsset)
	{
	    if (_parameters != null)
	    {
		_parameters.ResetAllToDefault(session);

		// Force an upload here so that when the parent asset recooks, it will have updated parameter values.
		_parameters.UploadPresetData(session);
		_parameters.UploadValuesToHoudini(session, parentAsset);
	    }
	}

	public void SetCurveParameterPreset(HEU_SessionBase session, HEU_HoudiniAsset parentAsset, byte[] parameterPreset)
	{
	    if (_parameters != null)
	    {
		_parameters.SetPresetData(parameterPreset);

		// Force an upload here so that when the parent asset recooks, it will have updated parameter values.
		_parameters.UploadPresetData(session);
		_parameters.UploadValuesToHoudini(session, parentAsset);
	    }
	}

	public void UpdateCurve(HEU_SessionBase session, HAPI_PartId partID)
	{
	    int vertexCount = 0;
	    float[] posAttr = new float[0];

	    if (partID != HEU_Defines.HEU_INVALID_NODE_ID)
	    {
		// Get position attributes.
		// Note that for an empty curve (ie. no position attributes) this query will fail, 
		// but the curve is still valid, so we simply set to null vertices. This allows 
		// user to add points later on.
		HAPI_AttributeInfo posAttrInfo = new HAPI_AttributeInfo();
		HEU_GeneralUtility.GetAttribute(session, _geoID, partID, HEU_HAPIConstants.HAPI_ATTRIB_POSITION, ref posAttrInfo, ref posAttr, session.GetAttributeFloatData);
		if (posAttrInfo.exists)
		{
		    vertexCount = posAttrInfo.count;
		}
	    }

	    // Curve guides from position attributes
	    _vertices = new Vector3[vertexCount];
	    for (int i = 0; i < vertexCount; ++i)
	    {
		_vertices[i][0] = -posAttr[i * 3 + 0];
		_vertices[i][1] = posAttr[i * 3 + 1];
		_vertices[i][2] = posAttr[i * 3 + 2];
	    }
	}

	public void GenerateMesh(GameObject inGameObject)
	{
	    _targetGameObject = inGameObject;

	    MeshFilter meshFilter = _targetGameObject.GetComponent<MeshFilter>();
	    if (meshFilter == null)
	    {
		meshFilter = _targetGameObject.AddComponent<MeshFilter>();
	    }

	    MeshRenderer meshRenderer = _targetGameObject.GetComponent<MeshRenderer>();
	    if (meshRenderer == null)
	    {
		meshRenderer = _targetGameObject.AddComponent<MeshRenderer>();

		Shader shader = HEU_MaterialFactory.FindPluginShader(HEU_PluginSettings.DefaultCurveShader);
		meshRenderer.sharedMaterial = new Material(shader);
		meshRenderer.sharedMaterial.SetColor("_Color", HEU_PluginSettings.LineColor);
	    }

	    Mesh mesh = meshFilter.sharedMesh;

	    if (_curveNodeData.Count <= 1)
	    {
		if (mesh != null)
		{
		    mesh.Clear();
		    mesh = null;
		}
	    }
	    else
	    {
		if (mesh == null)
		{
		    mesh = new Mesh();
		    mesh.name = "Curve";
		}

		int[] indices = new int[_vertices.Length];
		for (int i = 0; i < _vertices.Length; ++i)
		{
		    indices[i] = i;
		}

		mesh.Clear();
		mesh.vertices = _vertices;
		mesh.SetIndices(indices, MeshTopology.LineStrip, 0);
		mesh.RecalculateBounds();

		mesh.UploadMeshData(false);
	    }

	    meshFilter.sharedMesh = mesh;
	    meshRenderer.enabled = HEU_PluginSettings.Curves_ShowInSceneView;

	    SetEditState(CurveEditState.GENERATED);
	}



	public bool UpdateCurveInputForCustomAttributes(HEU_SessionBase session, HEU_HoudiniAsset parentAsset)
	{


	    // Stop now just to be safe (Everything will be done Houdini-side) and we just fetch from there
	    // If I add the option to add custom attributes, this might be moved to one level up in the future.
	    if (parentAsset.CurveDisableScaleRotation)
	    {
		session.RevertGeo(GeoID);
		return true;
	    }

	    // Curve code mostly copied from Unreal-v2s FHoudiniSplineTranslator::HapiCreateCurveInputNodeForData

	    // In order to be able to add rotations and scale attributes to the curve SOP, we need to cook it twice:
	    // 
	    // - First, we send the positions string to it, and cook it without refinement.
	    //   this will allow us to get the proper curve CVs, part attributes and curve info to create the desired curve.
	    //
	    // - We then need to send back all the info extracted from the curve SOP to it, and add the rotation 
	    //   and scale attributes to it. This will lock the curve SOP, and prevent the curve type and method 
	    //   parameters from functioning properly (hence why we needed the first cook to set that up)

	    int numberOfCVs = _curveNodeData.Count;

	    if (numberOfCVs >= 2)
	    {
		// Re-create the curve attributes from scratch in order to modify the curve/rotation values
		// Additional CVs may be added or removed so we need to recreate the translation/rotation/scale lists
		// i.e. It is not guaranteed that positions.Count == rotations.Count == scales.Count so we have to do this
		List<Vector3> positions = new List<Vector3>();
		List<Quaternion> rotations = new List<Quaternion>();
		List<Vector3> scales = new List<Vector3>();

		_curveNodeData.ForEach((CurveNodeData data) =>
		{
		    positions.Add(data.position);
		    rotations.Add(data.GetRotation());
		    scales.Add(data.scale);
		});

		const string warningMessage = "\nRotation/Scale may not work properly.";

	    	if (!session.RevertGeo(GeoID))
		{
		    HEU_Logger.LogWarning("Unable to revert Geo!" + warningMessage);
		    return false;
		}

 		HAPI_NodeId curveIdNode = GeoID;

		// Set the type, method, close, and reverse parameters
		HEU_ParameterData typeParameter = _parameters.GetParameter(HEU_Defines.CURVE_TYPE_PARAM);
		int curveTypeValue = typeParameter._intValues[0];
		if (!session.SetParamIntValue(curveIdNode, HEU_Defines.CURVE_TYPE_PARAM, 0, curveTypeValue))
		{
		    HEU_Logger.LogWarning("Unable to get 'type' parameter"  + warningMessage);
		    return false;
		}

		HEU_ParameterData methodParameter = _parameters.GetParameter(HEU_Defines.CURVE_METHOD_PARAM);
		int curveMethodValue = methodParameter._intValues[0];
		if (!session.SetParamIntValue(curveIdNode, HEU_Defines.CURVE_METHOD_PARAM, 0, curveMethodValue))
		{
		    HEU_Logger.LogWarning("Unable to get 'method' parameter"  + warningMessage);
		    return false;
		}

		HEU_ParameterData closeParameter = _parameters.GetParameter(HEU_Defines.CURVE_CLOSE_PARAM);
		int curveCloseValue = System.Convert.ToInt32(closeParameter._toggle);
		if (!session.SetParamIntValue(curveIdNode, HEU_Defines.CURVE_CLOSE_PARAM, 0, curveCloseValue))
		{
		    HEU_Logger.LogWarning("Unable to get 'close' parameter"  + warningMessage);
		    return false;
		}

		HEU_ParameterData reverseParameter = _parameters.GetParameter(HEU_Defines.CURVE_REVERSE_PARAM);
		int curveReverseValue = System.Convert.ToInt32(reverseParameter._toggle);
		if (!session.SetParamIntValue(curveIdNode, HEU_Defines.CURVE_REVERSE_PARAM, 0, curveReverseValue))
		{
		    HEU_Logger.LogWarning("Unable to get 'reverse' parameter"  + warningMessage);
		    return false;
		}

		// Reading the curve values
		session.GetParamIntValue(curveIdNode, HEU_Defines.CURVE_TYPE_PARAM, 0, out curveTypeValue);
		session.GetParamIntValue(curveIdNode, HEU_Defines.CURVE_METHOD_PARAM, 0, out curveMethodValue);
		session.GetParamIntValue(curveIdNode, HEU_Defines.CURVE_CLOSE_PARAM, 0, out curveCloseValue);
		session.GetParamIntValue(curveIdNode, HEU_Defines.CURVE_REVERSE_PARAM, 0, out curveReverseValue);


		// For closed NURBs (Cvs and Breakpoints), we have to close the curve manually, but duplicating its last point in order to be
		// able to set the rotation and scale propertly
		bool bCloseCurveManually = false;
		
		if (curveCloseValue == 1 && curveTypeValue == (int)(HAPI_CurveType.HAPI_CURVETYPE_NURBS) && curveMethodValue != 2)
		{
		     // The curve is not closed anymore
		     session.SetParamIntValue(curveIdNode, HEU_Defines.CURVE_CLOSE_PARAM, 0, 0);
		     bCloseCurveManually = true;

		     // Duplicating the first point to the end point
		     // This needs to be done before sending the position string
		     positions.Add(positions[0]);
		     curveCloseValue = 0;
		}

		// Set updated coordinates string
		string positionsString = GetPointsString(positions);

		int parmId = -1;
		if (!session.GetParmIDFromName(curveIdNode, HEU_Defines.CURVE_COORDS_PARAM, out parmId))
		{
		    HEU_Logger.LogWarning("Unable to get curve 'coords' parameter." + warningMessage);
		    return false;
		}

		session.SetParamStringValue(_geoID, positionsString, parmId, 0);

		// Setting up first first cook for refinement
		HAPI_CookOptions cookOptions = HEU_HAPIUtility.GetDefaultCookOptions(session);
		cookOptions.maxVerticesPerPrimitive = -1;
		cookOptions.refineCurveToLinear = false;

		if (!HEU_HAPIUtility.CookNodeInHoudiniWithOptions(session, curveIdNode, cookOptions, CurveName))
		{
		    HEU_Logger.LogWarning("Unable to cook curve part!" + warningMessage);
		    return false;
		}

		HAPI_PartInfo partInfos = new HAPI_PartInfo();
		session.GetPartInfo(GeoID, 0, ref partInfos);

		// Depending on the curve type and method, additional control points might have been created.
		// We now have to interpolate the rotations and scale attributes for these.

		// Lambda function that interpolates rotation, scale, and uniform scale values
		// Between two points using fCoeff as a weight, and insert the interpolated value at nInsertIndex
		Action<int, int, float, int> InterpolateRotScaleUScale = (int nIndex1, int nIndex2, float fCoeff, int nInsertIndex) =>
		{
		    if (rotations != null && rotations.IsValidIndex(nIndex1) && rotations.IsValidIndex(nIndex2))
		    {
			Quaternion interpolation = Quaternion.Slerp(rotations[nIndex1], rotations[nIndex2], fCoeff);
			if (rotations.IsValidIndex(nInsertIndex))
			    rotations.Insert(nInsertIndex, interpolation);
			else
			    rotations.Add(interpolation);
		    }

		    if (scales != null && scales.IsValidIndex(nIndex1) && scales.IsValidIndex(nIndex2))
		    {
			Vector3 interpolation = Vector3.Slerp(scales[nIndex1], scales[nIndex2], fCoeff);
			if (scales.IsValidIndex(nInsertIndex))
			    scales.Insert(nInsertIndex, interpolation);
			else
			    scales.Add(interpolation);
		    }
		};

		// Lambda function that duplicates rotation and scale values at nIndex, and inserts/adds it at nInsertIndex
		Action<int, int> DuplicateRotScale = (int nIndex, int nInsertIndex) =>
		{
		    if (rotations != null && rotations.IsValidIndex(nIndex))
		    {
			Quaternion value = rotations[nIndex];
			if (rotations.IsValidIndex(nInsertIndex))
			    rotations.Insert(nInsertIndex, value);
			else
			    rotations.Add(value);
		    }

		    if (scales != null && scales.IsValidIndex(nIndex))
		    {
			Vector3 value = scales[nIndex];
			if (scales.IsValidIndex(nInsertIndex))
			    scales.Insert(nInsertIndex, value);
			else
			    scales.Add(value);
		    }
		};

		// Do we want to close the curve by ourselves?
		if (bCloseCurveManually)
		{
		    DuplicateRotScale(0, numberOfCVs++);
		    session.SetParamIntValue(curveIdNode, HEU_Defines.CURVE_CLOSE_PARAM, 0, 1);
		}

		// INTERPOLATION
		if (curveTypeValue == (int)HAPI_CurveType.HAPI_CURVETYPE_NURBS)
		{
		    // Closed NURBS have additional points  reproducing the first ones
		    if (curveCloseValue == 1)
		    {
			// Only the first one if the method if freehand ...
			DuplicateRotScale(0, numberOfCVs++);
			if (curveMethodValue != 2)
			{
			    // ... but also the 2nd and 3rd if the method is CVs or Breakpoints
			     DuplicateRotScale(1, numberOfCVs++);
			     DuplicateRotScale(2, numberOfCVs++);
			}
		    }
		    else if (curveMethodValue == 1)
		    {
			// Open NURBs have 2 new points if t he method is breakpoint:
			// One between the 1st and 2nd ...
			InterpolateRotScaleUScale(0, 1, 0.5f, 1);

			// ... and one before the last one.
			InterpolateRotScaleUScale(numberOfCVs, numberOfCVs - 1, 0.5f, numberOfCVs);
			numberOfCVs += 2;
		    }
		}
		else if (curveTypeValue == (int)HAPI_CurveType.HAPI_CURVETYPE_BEZIER)
		{
		    // Bezier curves requires additional point if the method is breakpoints
		    if (curveMethodValue == 1)
		    {
			// 2 interpolated control points are added per points (except the last one)
			int nOffset = 0;
			for (int n = 0; n < numberOfCVs - 1; n++)
			{
			    int nIndex1 = n + nOffset;
			    int nIndex2 = n + nOffset + 1;

			    InterpolateRotScaleUScale(nIndex1, nIndex2, 0.33f, nIndex2);
			    nIndex2++;
			    InterpolateRotScaleUScale(nIndex1, nIndex2, 0.66f, nIndex2);

			    nOffset += 2;
			}

			numberOfCVs += nOffset;

			if (curveCloseValue == 1)
			{
			    // If the curve is closed, we need to add 2 points after the last
			    // interpolated between the last and the first one
			    int nIndex = numberOfCVs - 1;
			    InterpolateRotScaleUScale(nIndex, 0, 0.33f, numberOfCVs++);
			    InterpolateRotScaleUScale(nIndex, 0, 0.66f, numberOfCVs++);

			    // and finally, the last point is the first.
			    DuplicateRotScale(0, numberOfCVs++);
			}
		    }
		    else if (curveCloseValue == 1)
		    {
			// For the other methods, if the bezier curve is closed, the last point is the 1st
			DuplicateRotScale(0, numberOfCVs++);
		    }
		}

		// Reset all other attributes

		// Even after interpolation, additional points might still be missing
		// Bezier curves require a certain number of points regarding their order
		// if points are lacking then HAPI duplicates the last one
		if (numberOfCVs < partInfos.pointCount)
		{
		    int nToAdd = partInfos.pointCount - numberOfCVs;
		    for (int n = 0; n < nToAdd; n++)
		    {
			DuplicateRotScale(numberOfCVs - 1, numberOfCVs);
			numberOfCVs++;
		    }
		}

		bool bAddRotations = !parentAsset.CurveDisableScaleRotation && (rotations.Count == partInfos.pointCount);
		bool bAddScales = !parentAsset.CurveDisableScaleRotation && (scales.Count == partInfos.pointCount);

		if (!bAddRotations)
		{
		    HEU_Logger.LogWarning("Point count malformed! Skipping adding rotations to curve");
		}
		
		if (!bAddScales)
		{
		    HEU_Logger.LogWarning("Point count malformed! Skipping adding scales to curve");
		}


		// We need to increase the point attributes count for points in the part infos
		HAPI_AttributeOwner newAttributesOwner = HAPI_AttributeOwner.HAPI_ATTROWNER_POINT;
		HAPI_AttributeOwner originalAttributesOwner = HAPI_AttributeOwner.HAPI_ATTROWNER_POINT;

		int originalPointParametersCount = partInfos.attributeCounts[(int)newAttributesOwner];
		if (bAddRotations)
		    partInfos.attributeCounts[(int)newAttributesOwner] += 1;
		
		if (bAddScales)
		    partInfos.attributeCounts[(int)newAttributesOwner] += 1;
		
		// Sending the updated PartInfos
		if (!session.SetPartInfo(curveIdNode, 0, ref partInfos))
		{
		    HEU_Logger.LogWarning("Unable to set part info!" + warningMessage);
		    return false;
		}

		// We need now to reproduce ALL the curves atttributes for ALL the owners
		for (int nOwner = 0; nOwner < (int)HAPI_AttributeOwner.HAPI_ATTROWNER_MAX;  nOwner++)
		{
		    int nOwnerAttributeCount = nOwner == (int)newAttributesOwner ? originalPointParametersCount : partInfos.attributeCounts[nOwner];
		    if (nOwnerAttributeCount == 0)
			continue;

			string[] AttributeNamesSH = new string[nOwnerAttributeCount];
			if (!session.GetAttributeNames(curveIdNode, 0, (HAPI_AttributeOwner)nOwner, ref AttributeNamesSH, nOwnerAttributeCount))
			{
			    HEU_Logger.LogWarning("Unable to get attribute names!" + warningMessage);
			    return false;
			}

			for (int nAttribute = 0; nAttribute < AttributeNamesSH.Length; nAttribute++)
			{
			    string attr_name = AttributeNamesSH[nAttribute];
			    if (attr_name == "") continue;

			    if (attr_name == "__topology")
			    {
				continue;
			    }

			    HAPI_AttributeInfo attr_info = new HAPI_AttributeInfo();
			    session.GetAttributeInfo(curveIdNode, 0, attr_name, (HAPI_AttributeOwner)nOwner, ref attr_info);
			    switch (attr_info.storage)
			    {
				case HAPI_StorageType.HAPI_STORAGETYPE_INT:
				    int[] intData = new int[attr_info.count * attr_info.tupleSize];
				    session.GetAttributeIntData(curveIdNode, 0, attr_name, ref attr_info, intData, 0, attr_info.count);
				    session.AddAttribute(curveIdNode, 0, attr_name, ref attr_info);
				    session.SetAttributeIntData(curveIdNode, 0, attr_name, ref attr_info, intData, 0, attr_info.count );

				    break;
				case HAPI_StorageType.HAPI_STORAGETYPE_FLOAT:
				    float[] floatData = new float[attr_info.count * attr_info.tupleSize];
				    session.GetAttributeFloatData(curveIdNode, 0, attr_name, ref attr_info, floatData, 0, attr_info.count);
				    session.AddAttribute(curveIdNode, 0, attr_name, ref attr_info);
				    session.SetAttributeFloatData(curveIdNode, 0, attr_name, ref attr_info, floatData, 0, attr_info.count );

				    break;
				case HAPI_StorageType.HAPI_STORAGETYPE_STRING:
				    string[] stringData = HEU_GeneralUtility.GetAttributeStringData(session, curveIdNode, 0, attr_name, ref attr_info);
				    session.AddAttribute(curveIdNode, 0, attr_name, ref attr_info);
				    session.SetAttributeStringData(curveIdNode, 0, attr_name, ref attr_info, stringData, 0, attr_info.count);
				    break;
				default:
				    //=HEU_Logger.Log("Storage type: " + attr_info.storage + " " + attr_name);
				    // primitive list doesn't matter
				    break;
			    }
			}
		}

		if (partInfos.type == HAPI_PartType.HAPI_PARTTYPE_CURVE)
		{
		    HAPI_CurveInfo curveInfo = new HAPI_CurveInfo();
		    session.GetCurveInfo(curveIdNode, 0, ref curveInfo);

		    int[] curveCounts = new int[curveInfo.curveCount];
		    session.GetCurveCounts(curveIdNode, 0, curveCounts, 0, curveInfo.curveCount);

		    int[] curveOrders = new int[curveInfo.curveCount];
		    session.GetCurveOrders(curveIdNode, 0, curveOrders, 0, curveInfo.curveCount);

		    float[] knotsArray = null;
		    if (curveInfo.hasKnots)
		    {
		        knotsArray = new float[curveInfo.knotCount];
		        session.GetCurveKnots(curveIdNode, 0, knotsArray, 0, curveInfo.knotCount);
		    }

		    session.SetCurveInfo(curveIdNode, 0, ref curveInfo);

		    session.SetCurveCounts(curveIdNode, 0, curveCounts, 0, curveInfo.curveCount);
		    session.SetCurveOrders(curveIdNode, 0, curveOrders, 0, curveInfo.curveCount);

		    if (curveInfo.hasKnots)
		    {
		        session.SetCurveKnots(curveIdNode, 0, knotsArray, 0, curveInfo.knotCount);
		    }

		}

		if (partInfos.faceCount > 0)
		{
		    int[] faceCounts = new int[partInfos.faceCount];
		    if (session.GetFaceCounts(curveIdNode, 0, faceCounts, 0, partInfos.faceCount, false))
		    {
		        session.SetFaceCount(curveIdNode, 0, faceCounts, 0, partInfos.faceCount);
		    }
		}

		if (partInfos.vertexCount > 0)
		{
		    int[] vertexList = new int[partInfos.vertexCount];
		    if (session.GetVertexList(curveIdNode, 0, vertexList, 0, partInfos.vertexCount))
		    {
		        session.SetVertexList(curveIdNode, 0, vertexList, 0, partInfos.vertexCount);
		    }
		}


		if (bAddRotations)
		{
		    HAPI_AttributeInfo attributeInfoRotation = new HAPI_AttributeInfo();
		    attributeInfoRotation.count = numberOfCVs;
		    attributeInfoRotation.tupleSize = 4;
		    attributeInfoRotation.exists = true;
		    attributeInfoRotation.owner = newAttributesOwner;
		    attributeInfoRotation.storage = HAPI_StorageType.HAPI_STORAGETYPE_FLOAT;
		    attributeInfoRotation.originalOwner = originalAttributesOwner;

		    session.AddAttribute(_geoID, 0, HEU_Defines.HAPI_ATTRIB_ROTATION, ref attributeInfoRotation);

		    float[] curveRotations = new float[numberOfCVs * 4];

		    for (int i = 0; i < numberOfCVs; i++)
		    {
		        Quaternion rotQuat = rotations[i];
		        Vector3 euler = rotQuat.eulerAngles;
		        euler.y = -euler.y;
		        euler.z = -euler.z;
		        rotQuat = Quaternion.Euler(euler);

		        curveRotations[i * 4 + 0] = rotQuat[0];
		        curveRotations[i * 4 + 1] = rotQuat[1];
		        curveRotations[i * 4 + 2] = rotQuat[2];
		        curveRotations[i * 4 + 3] = rotQuat[3];
		    }

		    session.SetAttributeFloatData(curveIdNode, 0, HEU_Defines.HAPI_ATTRIB_ROTATION, ref attributeInfoRotation, curveRotations, 0, attributeInfoRotation.count);
		}

		if (bAddScales)
		{
		    HAPI_AttributeInfo attributeInfoScale = new HAPI_AttributeInfo();
		    attributeInfoScale.count = numberOfCVs;
		    attributeInfoScale.tupleSize = 3;
		    attributeInfoScale.exists = true;
		    attributeInfoScale.owner = newAttributesOwner;
		    attributeInfoScale.storage = HAPI_StorageType.HAPI_STORAGETYPE_FLOAT;
		    attributeInfoScale.originalOwner = originalAttributesOwner;

		    session.AddAttribute(_geoID, 0, HEU_Defines.HAPI_ATTRIB_SCALE, ref attributeInfoScale);

		    float[] curveScales = new float[numberOfCVs * 3];

		    for (int i = 0; i < numberOfCVs; i++)
		    {
		        Vector3 scaleVector = scales[i];
		        curveScales[i * 3 + 0] = scaleVector.x;
		        curveScales[i * 3 + 1] = scaleVector.y;
		        curveScales[i * 3 + 2] = scaleVector.z;
		    }

		    session.SetAttributeFloatData(curveIdNode, 0, HEU_Defines.HAPI_ATTRIB_SCALE, ref attributeInfoScale, curveScales, 0, attributeInfoScale.count);
		}

		session.CommitGeo(GeoID);

		cookOptions.refineCurveToLinear = true;

		HEU_HAPIUtility.CookNodeInHoudiniWithOptions(session, curveIdNode, cookOptions, CurveName);

		// Cook one more time otherwise it won't properly update on rebuild!
		HEU_HAPIUtility.CookNodeInHoudini(session, parentAsset.AssetID, true, parentAsset.AssetName);

	    }


	    return true;

	}

	public void SyncFromParameters(HEU_SessionBase session, HEU_HoudiniAsset parentAsset)
	{
	    HAPI_NodeInfo geoNodeInfo = new HAPI_NodeInfo();
	    if (!session.GetNodeInfo(_geoID, ref geoNodeInfo))
	    {
		return;
	    }

	    if (_parameters != null)
	    {
		_parameters.CleanUp();
	    }
	    else
	    {
		_parameters = ScriptableObject.CreateInstance<HEU_Parameters>();
	    }

	    string geoNodeName = HEU_SessionManager.GetString(geoNodeInfo.nameSH, session);
	    _parameters._uiLabel = geoNodeName.ToUpper() + " PARAMETERS";

	    bool bResult = _parameters.Initialize(session, _geoID, ref geoNodeInfo, null, null, parentAsset);
	    if (!bResult)
	    {
		HEU_Logger.LogWarningFormat("Parameter generate failed for geo node {0}.", geoNodeInfo.id);
		_parameters.CleanUp();
		return;
	    }

	    UpdatePoints(session, 0);

	    // Since we just reset / created new our parameters and sync'd, we also need to 
	    // get the preset from Houdini session
	    if (!HEU_EditorUtility.IsEditorPlaying() && IsEditable())
	    {
		DownloadPresetData(session);
	    }
	}

	private void UpdatePoints(HEU_SessionBase session, HAPI_PartId partID)
	{
	    //

	    // We want to keep positions in sync with Houdini, but use our rotations/scales because
	    // The number of them depend on the curve type
	    List<Vector3> positions = new List<Vector3>();
	    List<Vector3> rotations = new List<Vector3>();
	    List<Vector3> scales = new List<Vector3>();

	    _curveNodeData.ForEach((CurveNodeData data) =>
	    {
	        rotations.Add(data.rotation);
	        scales.Add(data.scale);
	    });

	    _curveNodeData.Clear();

	    string pointList = _parameters.GetStringFromParameter(HEU_Defines.CURVE_COORDS_PARAM);
	    if (!string.IsNullOrEmpty(pointList))
	    {
		string[] pointSplit = pointList.Split(' ');
		for (int i = 0; i < pointSplit.Length; i++)
		{
		    string str = pointSplit[i];

		    string[] vecSplit = str.Split(',');
		    if (vecSplit.Length == 3)
		    {
			Vector3 position = new Vector3(-System.Convert.ToSingle(vecSplit[0], System.Globalization.CultureInfo.InvariantCulture),
				System.Convert.ToSingle(vecSplit[1], System.Globalization.CultureInfo.InvariantCulture),
				System.Convert.ToSingle(vecSplit[2], System.Globalization.CultureInfo.InvariantCulture));

			positions.Add(position);
		    }
		}
	    }

	    for (int i = 0; i < positions.Count; i++)
	    {
		CurveNodeData data = new CurveNodeData(positions[i]);

		if (_parentAsset != null && !_parentAsset.CurveDisableScaleRotation)
		{
		    if (rotations.IsValidIndex(i))
		    {
		        data.rotation = rotations[i];
		    }

		    if (scales.IsValidIndex(i))
		    {
		        data.scale = scales[i];
		    }
		}

		_curveNodeData.Add(data);
	    }

	}

	/// <summary>
	/// Project curve points onto collider or layer.
	/// </summary>
	/// <param name="parentAsset">Parent asset of the curve</param>
	/// <param name="rayDirection">Direction to cast ray</param>
	/// <param name="rayDistance">Maximum ray cast distance</param>
	public void ProjectToColliders(HEU_HoudiniAsset parentAsset, Vector3 rayDirection, float rayDistance)
	{
	    bool bRequiresUpload = false;

	    LayerMask layerMask = Physics.DefaultRaycastLayers;

	    HEU_Curve.CurveDrawCollision collisionType = parentAsset.CurveDrawCollision;
	    if (collisionType == CurveDrawCollision.COLLIDERS)
	    {
		List<Collider> colliders = parentAsset.GetCurveDrawColliders();

		bool bFoundHit = false;
		int numPoints = _curveNodeData.Count;
		for (int i = 0; i < numPoints; ++i)
		{
		    bFoundHit = false;
		    RaycastHit[] rayHits = Physics.RaycastAll(_curveNodeData[i].position, rayDirection, rayDistance, layerMask, QueryTriggerInteraction.Ignore);
		    foreach (RaycastHit hit in rayHits)
		    {
			foreach (Collider collider in colliders)
			{
			    if (hit.collider == collider)
			    {
				_curveNodeData[i].position = hit.point;
				bFoundHit = true;
				bRequiresUpload = true;
				break;
			    }
			}

			if (bFoundHit)
			{
			    break;
			}
		    }
		}


	    }
	    else if (collisionType == CurveDrawCollision.LAYERMASK)
	    {
		layerMask = parentAsset.GetCurveDrawLayerMask();

		int numPoints = _curveNodeData.Count;
		for (int i = 0; i < numPoints; ++i)
		{
		    RaycastHit hitInfo;
		    if (Physics.Raycast(_curveNodeData[i].position, rayDirection, out hitInfo, rayDistance, layerMask, QueryTriggerInteraction.Ignore))
		    {
			_curveNodeData[i].position = hitInfo.point;
			bRequiresUpload = true;
		    }
		}
	    }

	    if (bRequiresUpload)
	    {
		HEU_ParameterData paramData = _parameters.GetParameter(HEU_Defines.CURVE_COORDS_PARAM);
		if (paramData != null)
		{
		    paramData._stringValues[0] = GetPointsString(_curveNodeData);
		}

		SetEditState(CurveEditState.REQUIRES_GENERATION);
	    }
	}

	/// <summary>
	/// Returns points array as string
	/// </summary>
	/// <param name="points">List of points to stringify</param>
	/// <returns></returns>
	public static string GetPointsString(List<CurveNodeData> points)
	{
	    StringBuilder sb = new StringBuilder();
	    foreach (CurveNodeData pt in points)
	    {
		sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2} ", -pt.position[0], pt.position[1], pt.position[2]);
	    }
	    return sb.ToString();
	}

	public static string GetPointsString(List<Vector3> points)
	{
	    StringBuilder sb = new StringBuilder();
	    foreach (Vector3 pt in points)
	    {
		sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2} ", -pt[0], pt[1], pt[2]);
	    }
	    return sb.ToString();
	}

	public void SetEditState(CurveEditState editState)
	{
	    _editState = editState;
	}

	public void SetCurvePoint(int pointIndex, Vector3 newPosition)
	{
	    if (pointIndex >= 0 && pointIndex < _curveNodeData.Count)
	    {
		_curveNodeData[pointIndex].position = newPosition;
	    }
	}

	public Vector3 GetCurvePoint(int pointIndex)
	{
	    if (pointIndex >= 0 && pointIndex < _curveNodeData.Count)
	    {
		return _curveNodeData[pointIndex].position;
	    }
	    return Vector3.zero;
	}

	public List<CurveNodeData> GetAllPointTransforms()
	{
	    return _curveNodeData;
	}

	public List<Vector3> GetAllPoints()
	{
	    List<Vector3> points = new List<Vector3>();

	    _curveNodeData.ForEach((CurveNodeData transform) => points.Add(transform.position));

	    return points;
	}

	public int GetNumPoints()
	{
	    return _curveNodeData.Count;
	}

	public Vector3 GetTransformedPoint(int pointIndex)
	{
	    if (pointIndex >= 0 && pointIndex < _curveNodeData.Count)
	    {
		return GetTransformedPosition(_curveNodeData[pointIndex].position);
	    }
	    return Vector3.zero;
	}

	public Vector3 GetTransformedPosition(Vector3 inPosition)
	{
	    return this._targetGameObject.transform.TransformPoint(inPosition);
	}

	public Vector3 GetInvertedTransformedPosition(Vector3 inPosition)
	{
	    return this._targetGameObject.transform.InverseTransformPoint(inPosition);
	}

	public Vector3 GetInvertedTransformedDirection(Vector3 inPosition)
	{
	    return this._targetGameObject.transform.InverseTransformVector(inPosition);
	}

	public Vector3[] GetVertices()
	{
	    return _vertices;
	}

	public void SetCurveGeometryVisibility(bool bVisible)
	{
	    if (_targetGameObject != null)
	    {
		MeshRenderer renderer = _targetGameObject.GetComponent<MeshRenderer>();
		if (renderer != null)
		{
		    renderer.enabled = bVisible;
		}
	    }
	}

	public void DownloadPresetData(HEU_SessionBase session)
	{
	    if (_parameters != null)
	    {
		_parameters.DownloadPresetData(session);
	    }
	}

	public void UploadPresetData(HEU_SessionBase session)
	{
	    if (_parameters != null)
	    {
		_parameters.UploadPresetData(session);
	    }
	}

	public void DownloadAsDefaultPresetData(HEU_SessionBase session)
	{
	    if (_parameters != null)
	    {
		_parameters.DownloadAsDefaultPresetData(session);
	    }
	}

	public List<CurveNodeData> DuplicateCurveNodeData()
	{
	    List<CurveNodeData> curveNodes = new List<CurveNodeData>();
	    foreach (CurveNodeData curveData in _curveNodeData)
	    {
		curveNodes.Add(new CurveNodeData(curveData));
	    }

	    return curveNodes;
	}

	public void SetCurveNodeData(List<CurveNodeData> curveNodeData)
	{
	    _curveNodeData = curveNodeData;
	}

	public bool IsEquivalentTo(HEU_Curve other)
	{

	    bool bResult = true;

	    string header = "HEU_Curve";

	    if (other == null)
	    {
		HEU_Logger.LogError(header + " Not equivalent");
		return false;
	    }

	    HEU_TestHelpers.AssertTrueLogEquivalent(this._curveNodeData, other._curveNodeData, ref bResult, header, "_curveNodeData");

	    HEU_TestHelpers.AssertTrueLogEquivalent(this._vertices, other._vertices, ref bResult, header, "_vertices");

	    HEU_TestHelpers.AssertTrueLogEquivalent(this._isEditable, other._isEditable, ref bResult, header, "_isEditable");

	    HEU_TestHelpers.AssertTrueLogEquivalent(this._parameters, other._parameters , ref bResult, header, "_parameters");

	    HEU_TestHelpers.AssertTrueLogEquivalent(this._bUploadParameterPreset, other._bUploadParameterPreset, ref bResult, header, "_bUploadParamterPreset");
	    HEU_TestHelpers.AssertTrueLogEquivalent(this._curveName, other._curveName, ref bResult, header, "_curveName");
	    
	    HEU_TestHelpers.AssertTrueLogEquivalent(this._targetGameObject, other._targetGameObject, ref bResult, header, "_targetGameObject");

	    HEU_TestHelpers.AssertTrueLogEquivalent(this._isGeoCurve, other._isGeoCurve, ref bResult, header, "_isGeoCurve");

	    HEU_TestHelpers.AssertTrueLogEquivalent(this._editState, other._editState, ref bResult, header, "_editState");

	    // Skip HEU_HoudiniAsset

	    return bResult;
	}

    }

}   // HoudiniEngineUnity