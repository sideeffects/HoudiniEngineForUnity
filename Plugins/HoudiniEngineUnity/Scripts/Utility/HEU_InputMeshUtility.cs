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

using System.Collections.Generic;
using UnityEngine;

namespace HoudiniEngineUnity
{
	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Typedefs (copy these from HEU_Common.cs)
	using HAPI_NodeId = System.Int32;
	using HAPI_PartId = System.Int32;

	/// <summary>
	/// Utility class for uploading input mesh data
	/// </summary>
	public static class HEU_InputMeshUtility
	{
		public class HEU_UploadMeshData
		{
			public Mesh _mesh;
			public Material[] _materials;

			public string _meshPath;
			public string _meshName;

			public int _numVertices;
			public int _numSubMeshes;

			// This keeps track of indices start and length for each submesh
			public uint[] _indexStart;
			public uint[] _indexCount;

			public float _LODScreenTransition;
		}

		/// <summary>
		/// Generate list of HEU_UploadMeshData with data from meshes found on and under inputObject.
		/// If inputObject is a LODGroup, this will create mesh data for each LOD mesh under it.
		/// Otherwise a single HEU_UploadMeshData is returned in the list.
		/// </summary>
		/// <param name="inputObject">The GameObject to query mesh data from</param>
		/// <param name="bHasLODGroup">Set whether LOD group was found</param>
		/// <returns>List of HEU_UploadMeshData</returns>
		public static List<HEU_UploadMeshData> GenerateMeshDatasFromInputObject(GameObject inputObject, out bool bHasLODGroup)
		{
			List<HEU_UploadMeshData> meshDatas = new List<HEU_UploadMeshData>();

			bHasLODGroup = false;

			LODGroup lodGroup = inputObject.GetComponent<LODGroup>();
			if (lodGroup != null)
			{
				bHasLODGroup = true;

				LOD[] lods = lodGroup.GetLODs();
				for (int i = 0; i < lods.Length; ++i)
				{
					if (lods[i].renderers != null && lods[i].renderers.Length > 0)
					{
						GameObject childGO = lods[i].renderers[0].gameObject;
						HEU_UploadMeshData meshData = CreateSingleMeshData(childGO);
						if (meshData != null)
						{
							meshData._LODScreenTransition = lods[i].screenRelativeTransitionHeight;
							meshDatas.Add(meshData);
						}
					}
				}
			}
			else
			{
				HEU_UploadMeshData meshData = CreateSingleMeshData(inputObject);
				if (meshData != null)
				{
					meshDatas.Add(meshData);
				}
			}

			return meshDatas;
		}

		/// <summary>
		/// Returns HEU_UploadMeshData with mesh data found on meshGameObject.
		/// </summary>
		/// <param name="meshGameObject">The GameObject to query mesh data from</param>
		/// <returns>A valid HEU_UploadMeshData if mesh data found or null</returns>
		public static HEU_UploadMeshData CreateSingleMeshData(GameObject meshGameObject)
		{
			HEU_UploadMeshData meshData = new HEU_UploadMeshData();

			MeshFilter meshfilter = meshGameObject.GetComponent<MeshFilter>();
			if (meshfilter == null)
			{
				return null;
			}

			if (meshfilter.sharedMesh == null)
			{
				return null;
			}
			meshData._mesh = meshfilter.sharedMesh;
			meshData._numVertices = meshData._mesh.vertexCount;
			meshData._numSubMeshes = meshData._mesh.subMeshCount;

			meshData._meshName = meshGameObject.name;

			meshData._meshPath = HEU_AssetDatabase.GetAssetOrScenePath(meshGameObject);
			if (string.IsNullOrEmpty(meshData._meshPath))
			{
				meshData._meshPath = meshGameObject.name;
			}

			MeshRenderer meshRenderer = meshGameObject.GetComponent<MeshRenderer>();
			if (meshRenderer != null)
			{
				meshData._materials = meshRenderer.sharedMaterials;
			}

			return meshData;
		}

		/// <summary>
		/// Upload the given list of mesh data in uploadMeshes into node with inputNodeID.
		/// If the source was a LOD group, then group names are assigned to the geometry.
		/// </summary>
		/// <param name="session">Session to upload to</param>
		/// <param name="inputNodeID">The ID of the input node to upload into</param>
		/// <param name="uploadMeshes">List of mesh data</param>
		/// <param name="bHasLODGroup">Whether the source was a LOD group and therefore treat it specially</param>
		/// <returns></returns>
		public static bool UploadInputMeshData(HEU_SessionBase session, HAPI_NodeId inputNodeID, List<HEU_UploadMeshData> uploadMeshes, bool bHasLODGroup)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<Color> colors = new List<Color>();

			List<int> pointIndexList = new List<int>();
			List<int> vertIndexList = new List<int>();

			int numMaterials = 0;

			// For all meshes:
			// Accumulate vertices, normals, uvs, colors, and indices.
			// Keep track of indices start and count for each mesh for later when uploading material assignments and groups.
			// Find shared vertices, and use unique set of vertices to use as point positions.
			// Need to reindex indices for both unique vertices, as well as vertex attributes.
			int numMeshes = uploadMeshes.Count;
			for (int i = 0; i < numMeshes; ++i)
			{
				Vector3[] meshVertices = uploadMeshes[i]._mesh.vertices;

				List <Vector3> uniqueVertices = new List<Vector3>();

				// Keep track of old vertex positions (old vertex slot points to new unique vertex slot)
				int[] reindexVertices = new int[meshVertices.Length];
				
				for(int j = 0; j < meshVertices.Length; ++j)
				{
					reindexVertices[j] = -1;
				}

				// For each vertex, check against subsequent vertices for shared positions.
				for(int a = 0; a < meshVertices.Length; ++a)
				{
					Vector3 va = meshVertices[a];

					if (reindexVertices[a] == -1)
					{
						uniqueVertices.Add(va);

						// Reindex to point to unique vertex slot
						reindexVertices[a] = uniqueVertices.Count - 1;
					}
					
					for (int b = a + 1; b < meshVertices.Length; ++b)
					{
						if (va == meshVertices[b])
						{
							// Shared vertex -> reindex to point to unique vertex slot
							reindexVertices[b] = reindexVertices[a];
						}
					}
				}

				int vertexOffset = vertices.Count;
				vertices.AddRange(uniqueVertices);

				Vector3[] meshNormals = uploadMeshes[i]._mesh.normals;
				Vector2[] meshUVs = uploadMeshes[i]._mesh.uv;
				Color[] meshColors = uploadMeshes[i]._mesh.colors;

				uploadMeshes[i]._indexStart = new uint[uploadMeshes[i]._numSubMeshes];
				uploadMeshes[i]._indexCount = new uint[uploadMeshes[i]._numSubMeshes];

				// For each submesh:
				// Generate face to point index -> pointIndexList
				// Generate face to vertex attribute index -> vertIndexList
				for (int j = 0; j < uploadMeshes[i]._numSubMeshes; ++j)
				{
					int indexStart = pointIndexList.Count;
					int vertIndexStart = vertIndexList.Count;

					// Indices have to be re-indexed with our own offset
					int[] meshIndices = uploadMeshes[i]._mesh.GetTriangles(j);
					int numIndices = meshIndices.Length;
					for (int k = 0; k < numIndices; ++k)
					{
						int originalIndex = meshIndices[k];
						meshIndices[k] = reindexVertices[originalIndex];

						pointIndexList.Add(vertexOffset + meshIndices[k]);
						vertIndexList.Add(vertIndexStart + k);

						if (meshNormals != null && (originalIndex < meshNormals.Length))
						{
							normals.Add(meshNormals[originalIndex]);
						}

						if (meshUVs != null && (originalIndex < meshUVs.Length))
						{
							uvs.Add(meshUVs[originalIndex]);
						}

						if (meshColors != null && (originalIndex < meshColors.Length))
						{
							colors.Add(meshColors[originalIndex]);
						}
					}

					uploadMeshes[i]._indexStart[j] = (uint)indexStart;
					uploadMeshes[i]._indexCount[j] = (uint)(pointIndexList.Count) - uploadMeshes[i]._indexStart[j];
				}

				numMaterials += uploadMeshes[i]._materials != null ? uploadMeshes[i]._materials.Length : 0;
			}

			// It is possible for some meshes to not have normals/uvs/colors while others do.
			// In the case where an attribute is missing on some meshes, we clear out those attributes so we don't upload
			// partial attribute data.
			int totalAllVertexCount = vertIndexList.Count;
			if (normals.Count != totalAllVertexCount)
			{
				normals = null;
			}

			if (uvs.Count != totalAllVertexCount)
			{
				uvs = null;
			}

			if (colors.Count != totalAllVertexCount)
			{
				colors = null;
			}

			HAPI_PartInfo partInfo = new HAPI_PartInfo();
			partInfo.faceCount = vertIndexList.Count / 3;
			partInfo.vertexCount = vertIndexList.Count;
			partInfo.pointCount = vertices.Count;
			partInfo.pointAttributeCount = 1;
			partInfo.vertexAttributeCount = 0;
			partInfo.primitiveAttributeCount = 0;
			partInfo.detailAttributeCount = 0;

			if (normals != null && normals.Count > 0)
			{
				partInfo.vertexAttributeCount++;
			}

			if (uvs != null && uvs.Count > 0)
			{
				partInfo.vertexAttributeCount++;
			}

			if (colors != null && colors.Count > 0)
			{
				partInfo.vertexAttributeCount++;
			}

			if (numMaterials > 0)
			{
				partInfo.primitiveAttributeCount++;
			}

			if (numMeshes > 0)
			{
				partInfo.primitiveAttributeCount++;
			}

			if (bHasLODGroup)
			{
				partInfo.primitiveAttributeCount++;
				partInfo.detailAttributeCount++;
			}

			HAPI_GeoInfo displayGeoInfo = new HAPI_GeoInfo();
			if (!session.GetDisplayGeoInfo(inputNodeID, ref displayGeoInfo))
			{
				return false;
			}

			HAPI_NodeId displayNodeID = displayGeoInfo.nodeId;

			if (!session.SetPartInfo(displayNodeID, 0, ref partInfo))
			{
				return false;
			}

			int[] faceCounts = new int[partInfo.faceCount];
			for (int i = 0; i < partInfo.faceCount; ++i)
			{
				faceCounts[i] = 3;
			}

			int[] triIndices = pointIndexList.ToArray();

			if (!HEU_GeneralUtility.SetArray2Arg(displayNodeID, 0, session.SetFaceCount, faceCounts, 0, partInfo.faceCount))
			{
				return false;
			}

			if (!HEU_GeneralUtility.SetArray2Arg(displayNodeID, 0, session.SetVertexList, triIndices, 0, partInfo.vertexCount))
			{
				return false;
			}

			if (!SetMeshPointAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_POSITION, 3, vertices.ToArray(), ref partInfo, true))
			{
				return false;
			}

			int[] vertIndices = vertIndexList.ToArray();

			//if(normals != null && !SetMeshPointAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_NORMAL, 3, normals.ToArray(), ref partInfo, true))
			if (normals != null && !SetMeshVertexAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_NORMAL, 3, normals.ToArray(), vertIndices, ref partInfo, true))
			{
				return false;
			}

			if (uvs != null && uvs.Count > 0)
			{
				Vector3[] uvs3 = new Vector3[uvs.Count];
				for (int i = 0; i < uvs.Count; ++i)
				{
					uvs3[i][0] = uvs[i][0];
					uvs3[i][1] = uvs[i][1];
					uvs3[i][2] = 0;
				}
				//if(!SetMeshPointAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_UV, 3, uvs3, ref partInfo, false))
				if (!SetMeshVertexAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_UV, 3, uvs3, vertIndices, ref partInfo, false))
				{
					return false;
				}
			}

			if (colors != null && colors.Count > 0)
			{
				Vector3[] rgb = new Vector3[colors.Count];
				float[] alpha = new float[colors.Count];
				for (int i = 0; i < colors.Count; ++i)
				{
					rgb[i][0] = colors[i].r;
					rgb[i][1] = colors[i].g;
					rgb[i][2] = colors[i].b;

					alpha[i] = colors[i].a;
				}

				//if(!SetMeshPointAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_COLOR, 3, rgb, ref partInfo, false))
				if (!SetMeshVertexAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_COLOR, 3, rgb, vertIndices, ref partInfo, false))
				{
					return false;
				}

				//if(!SetMeshPointAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_ALPHA, 1, alpha, ref partInfo, false))
				if (!SetMeshVertexFloatAttribute(session, displayNodeID, 0, HEU_Defines.HAPI_ATTRIB_ALPHA, 1, alpha, vertIndices, ref partInfo))
				{
					return false;
				}
			}

			// Set material names for round-trip perservation of material assignment
			// Each HEU_UploadMeshData might have a list of submeshes and materials
			// These are all combined into a single mesh, with group names
			if (numMaterials > 0)
			{
				bool bFoundAtleastOneValidMaterial = false;

				string[] materialIDs = new string[partInfo.faceCount];
				for (int g = 0; g < uploadMeshes.Count; ++g)
				{
					if (uploadMeshes[g]._numSubMeshes != uploadMeshes[g]._materials.Length)
					{
						// Number of submeshes should equal number of materials since materials determine submeshes
						continue;
					}

					for (int i = 0; i < uploadMeshes[g]._materials.Length; ++i)
					{
						string materialName = HEU_AssetDatabase.GetAssetPathWithSubAssetSupport(uploadMeshes[g]._materials[i]);
						if (materialName == null)
						{
							materialName = "";
						}
						else if (materialName.StartsWith(HEU_Defines.DEFAULT_UNITY_BUILTIN_RESOURCES))
						{
							materialName = HEU_AssetDatabase.GetUniqueAssetPathForUnityAsset(uploadMeshes[g]._materials[i]);
						}

						bFoundAtleastOneValidMaterial |= !string.IsNullOrEmpty(materialName);

						int faceStart = (int)uploadMeshes[g]._indexStart[i] / 3;
						int faceEnd = faceStart + ((int)uploadMeshes[g]._indexCount[i] / 3);
						for (int m = faceStart; m < faceEnd; ++m)
						{
							materialIDs[m] = materialName;
						}
					}
				}

				if (bFoundAtleastOneValidMaterial)
				{
					HAPI_AttributeInfo materialIDAttrInfo = new HAPI_AttributeInfo();
					materialIDAttrInfo.exists = true;
					materialIDAttrInfo.owner = HAPI_AttributeOwner.HAPI_ATTROWNER_PRIM;
					materialIDAttrInfo.storage = HAPI_StorageType.HAPI_STORAGETYPE_STRING;
					materialIDAttrInfo.count = partInfo.faceCount;
					materialIDAttrInfo.tupleSize = 1;
					materialIDAttrInfo.originalOwner = HAPI_AttributeOwner.HAPI_ATTROWNER_INVALID;

					if (!session.AddAttribute(displayNodeID, 0, HEU_PluginSettings.UnityMaterialAttribName, ref materialIDAttrInfo))
					{
						return false;
					}

					if (!HEU_GeneralUtility.SetAttributeArray(displayNodeID, 0, HEU_PluginSettings.UnityMaterialAttribName, ref materialIDAttrInfo, materialIDs, session.SetAttributeStringData, partInfo.faceCount))
					{
						return false;
					}
				}
			}

			// Set mesh name attribute
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			attrInfo.exists = true;
			attrInfo.owner = HAPI_AttributeOwner.HAPI_ATTROWNER_PRIM;
			attrInfo.storage = HAPI_StorageType.HAPI_STORAGETYPE_STRING;
			attrInfo.count = partInfo.faceCount;
			attrInfo.tupleSize = 1;
			attrInfo.originalOwner = HAPI_AttributeOwner.HAPI_ATTROWNER_INVALID;

			if (session.AddAttribute(displayNodeID, 0, HEU_PluginSettings.UnityInputMeshAttr, ref attrInfo))
			{
				string[] primitiveNameAttr = new string[partInfo.faceCount];

				for (int g = 0; g < uploadMeshes.Count; ++g)
				{
					for (int i = 0; i < uploadMeshes[g]._numSubMeshes; ++i)
					{
						int faceStart = (int)uploadMeshes[g]._indexStart[i] / 3;
						int faceEnd = faceStart + ((int)uploadMeshes[g]._indexCount[i] / 3);
						for (int m = faceStart; m < faceEnd; ++m)
						{
							primitiveNameAttr[m] = uploadMeshes[g]._meshPath;
						}
					}
				}

				if (!HEU_GeneralUtility.SetAttributeArray(displayNodeID, 0, HEU_PluginSettings.UnityInputMeshAttr, ref attrInfo, primitiveNameAttr, session.SetAttributeStringData, partInfo.faceCount))
				{
					return false;
				}
			}
			else
			{
				return false;
			}

			// Set LOD group membership
			if (bHasLODGroup)
			{
				int[] membership = new int[partInfo.faceCount];

				for (int g = 0; g < uploadMeshes.Count; ++g)
				{
					if (g > 0)
					{
						// Clear array
						for (int m = 0; m < partInfo.faceCount; ++m)
						{
							membership[m] = 0;
						}
					}

					// Set 1 for faces belonging to this group
					for (int s = 0; s < uploadMeshes[g]._numSubMeshes; ++s)
					{
						int faceStart = (int)uploadMeshes[g]._indexStart[s] / 3;
						int faceEnd = faceStart + ((int)uploadMeshes[g]._indexCount[s] / 3);
						for (int m = faceStart; m < faceEnd; ++m)
						{
							membership[m] = 1;
						}
					}

					if (!session.AddGroup(displayNodeID, 0, HAPI_GroupType.HAPI_GROUPTYPE_PRIM, uploadMeshes[g]._meshName))
					{
						return false;
					}

					if (!session.SetGroupMembership(displayNodeID, 0, HAPI_GroupType.HAPI_GROUPTYPE_PRIM, uploadMeshes[g]._meshName, membership, 0, partInfo.faceCount))
					{
						return false;
					}
				}
			}

			return session.CommitGeo(displayNodeID);
		}

		/// <summary>
		/// Create input node for the given inputObject, and upload its mesh data (along with LOD meshes).
		/// Outputs the inputNodeID if successfully uploaded mesh data and returns true.
		/// </summary>
		/// <param name="session">Session to create input node</param>
		/// <param name="assetID">The parent asset ID</param>
		/// <param name="inputObject">The input GameObject to query mesh data from</param>
		/// <param name="inputNodeID">Output of input node ID if successfully created</param>
		/// <returns>True if successfully created and uploaded mesh data</returns>
		public static bool CreateInputNodeWithGeoData(HEU_SessionBase session, HAPI_NodeId assetID, GameObject inputObject, out HAPI_NodeId inputNodeID)
		{
			inputNodeID = HEU_Defines.HEU_INVALID_NODE_ID;

			if (!HEU_HAPIUtility.IsNodeValidInHoudini(session, assetID))
			{
				return false;
			}

			bool bHasLODGroup = false;
			List<HEU_UploadMeshData> uploadMeshDatas = GenerateMeshDatasFromInputObject(inputObject, out bHasLODGroup);
			if (uploadMeshDatas == null || uploadMeshDatas.Count == 0)
			{
				return false;
			}

			// If connected asset is not valid, then need to create an input asset
			if (inputNodeID == HEU_Defines.HEU_INVALID_NODE_ID)
			{
				string inputName = null;

				HAPI_NodeId newNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
				session.CreateInputNode(out newNodeID, inputName);
				if (newNodeID == HEU_Defines.HEU_INVALID_NODE_ID || !HEU_HAPIUtility.IsNodeValidInHoudini(session, newNodeID))
				{
					Debug.LogErrorFormat("Failed to create new input node in Houdini session!");
					return false;
				}

				inputNodeID = newNodeID;

				if (!session.CookNode(inputNodeID, false))
				{
					Debug.LogErrorFormat("New input node failed to cook!");
					return false;
				}
			}

			return UploadInputMeshData(session, inputNodeID, uploadMeshDatas, bHasLODGroup);
		}

		public static bool CreateInputNodeWithMultiObjects(HEU_SessionBase session, HAPI_NodeId assetID,
			ref HAPI_NodeId connectedAssetID, ref List<HEU_InputObjectInfo> inputObjects, ref List<HAPI_NodeId> inputObjectsConnectedAssetIDs, bool bKeepWorldTransform)
		{
			// Create the merge SOP node.
			if (!session.CreateNode(-1, "SOP/merge", null, true, out connectedAssetID))
			{
				Debug.LogErrorFormat("Unable to create merge SOP node for connecting input assets.");
				return false;
			}

			int numObjects = inputObjects.Count;
			for (int i = 0; i < numObjects; ++i)
			{
				HAPI_NodeId meshNodeID = HEU_Defines.HEU_INVALID_NODE_ID;
				inputObjectsConnectedAssetIDs.Add(meshNodeID);

				// Skipping null gameobjects. Though if this causes issues, can always let it continue
				// to create input node, but not upload mesh data
				if (inputObjects[i]._gameObject == null)
				{
					continue;
				}

				bool bResult = CreateInputNodeWithGeoData(session, assetID, inputObjects[i]._gameObject, out meshNodeID);
				if (!bResult || meshNodeID == HEU_Defines.HEU_INVALID_NODE_ID)
				{
					string errorMsg = string.Format("Input at index {0} is not valid", i);
					if (inputObjects[i]._gameObject.GetComponent<HEU_HoudiniAssetRoot>() != null)
					{
						errorMsg += " because it is an HDA. Change the Input Type to HDA.";
					}
					else if (inputObjects[i]._gameObject.GetComponent<MeshFilter>() == null || inputObjects[i]._gameObject.GetComponent<MeshFilter>().sharedMesh == null)
					{
						errorMsg += " because it does not have a valid Mesh. Make sure the GameObject has a MeshFilter component with a valid mesh.";
					}
					else
					{
						errorMsg += ". Unable to create input node.";
					}

					Debug.LogErrorFormat(errorMsg);

					// Skipping this and continuing input processing since this isn't a deal breaker
					continue;
				}

				inputObjectsConnectedAssetIDs[i] = meshNodeID;

				if (!session.ConnectNodeInput(connectedAssetID, i, meshNodeID))
				{
					Debug.LogErrorFormat("Unable to connect input nodes!");
					return false;
				}

				UploadInputObjectTransform(session, inputObjects[i], meshNodeID, bKeepWorldTransform);
			}

			return true;
		}

		public static bool UploadInputObjectTransform(HEU_SessionBase session, HEU_InputObjectInfo inputObject, HAPI_NodeId connectedAssetID, bool bKeepWorldTransform)
		{
			Matrix4x4 inputTransform = Matrix4x4.identity;
			if (inputObject._useTransformOffset)
			{
				if (bKeepWorldTransform)
				{
					// Add offset tranform to world transform
					Transform inputObjTransform = inputObject._gameObject.transform;
					Vector3 position = inputObjTransform.position + inputObject._translateOffset;
					Quaternion rotation = inputObjTransform.rotation * Quaternion.Euler(inputObject._rotateOffset);
					Vector3 scale = Vector3.Scale(inputObjTransform.localScale, inputObject._scaleOffset);

					Vector3 rotVector = rotation.eulerAngles;
					inputTransform = HEU_HAPIUtility.GetMatrix4x4(ref position, ref rotVector, ref scale);
				}
				else
				{
					// Offset from origin.
					inputTransform = HEU_HAPIUtility.GetMatrix4x4(ref inputObject._translateOffset, ref inputObject._rotateOffset, ref inputObject._scaleOffset);
				}
			}
			else
			{
				inputTransform = inputObject._gameObject.transform.localToWorldMatrix;
			}

			HAPI_TransformEuler transformEuler = HEU_HAPIUtility.GetHAPITransformFromMatrix(ref inputTransform);

			HAPI_NodeInfo meshNodeInfo = new HAPI_NodeInfo();
			if (!session.GetNodeInfo(connectedAssetID, ref meshNodeInfo))
			{
				return false;
			}

			if (session.SetObjectTransform(meshNodeInfo.parentId, ref transformEuler))
			{
				inputObject._syncdTransform = inputTransform;
			}

			return true;
		}

		public static bool SetMeshPointAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, string attrName,
			int tupleSize, Vector3[] data, ref HAPI_PartInfo partInfo, bool bConvertToHoudiniCoordinateSystem)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			attrInfo.exists = true;
			attrInfo.owner = HAPI_AttributeOwner.HAPI_ATTROWNER_POINT;
			attrInfo.storage = HAPI_StorageType.HAPI_STORAGETYPE_FLOAT;
			attrInfo.count = partInfo.pointCount;
			attrInfo.tupleSize = tupleSize;
			attrInfo.originalOwner = HAPI_AttributeOwner.HAPI_ATTROWNER_INVALID;

			float[] attrValues = new float[partInfo.pointCount * tupleSize];

			if (session.AddAttribute(geoID, 0, attrName, ref attrInfo))
			{
				float conversionMultiplier = bConvertToHoudiniCoordinateSystem ? -1f : 1f;

				for (int i = 0; i < partInfo.pointCount; ++i)
				{
					attrValues[i * tupleSize + 0] = conversionMultiplier * data[i][0];

					for (int j = 1; j < tupleSize; ++j)
					{
						attrValues[i * tupleSize + j] = data[i][j];
					}
				}
			}

			return HEU_GeneralUtility.SetAttributeArray(geoID, partID, attrName, ref attrInfo, attrValues, session.SetAttributeFloatData, partInfo.pointCount);
		}

		public static bool SetMeshVertexAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, string attrName,
			int tupleSize, Vector3[] data, int[] indices, ref HAPI_PartInfo partInfo, bool bConvertToHoudiniCoordinateSystem)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			attrInfo.exists = true;
			attrInfo.owner = HAPI_AttributeOwner.HAPI_ATTROWNER_VERTEX;
			attrInfo.storage = HAPI_StorageType.HAPI_STORAGETYPE_FLOAT;
			attrInfo.count = partInfo.vertexCount;
			attrInfo.tupleSize = tupleSize;
			attrInfo.originalOwner = HAPI_AttributeOwner.HAPI_ATTROWNER_INVALID;

			float[] attrValues = new float[partInfo.vertexCount * tupleSize];

			if (session.AddAttribute(geoID, 0, attrName, ref attrInfo))
			{
				float conversionMultiplier = bConvertToHoudiniCoordinateSystem ? -1f : 1f;

				for (int i = 0; i < partInfo.vertexCount; ++i)
				{
					attrValues[i * tupleSize + 0] = conversionMultiplier * data[indices[i]][0];

					for (int j = 1; j < tupleSize; ++j)
					{
						attrValues[i * tupleSize + j] = data[indices[i]][j];
					}
				}
			}

			return HEU_GeneralUtility.SetAttributeArray(geoID, partID, attrName, ref attrInfo, attrValues, session.SetAttributeFloatData, partInfo.vertexCount);
		}

		public static bool SetMeshVertexFloatAttribute(HEU_SessionBase session, HAPI_NodeId geoID, HAPI_PartId partID, string attrName,
			int tupleSize, float[] data, int[] indices, ref HAPI_PartInfo partInfo)
		{
			HAPI_AttributeInfo attrInfo = new HAPI_AttributeInfo();
			attrInfo.exists = true;
			attrInfo.owner = HAPI_AttributeOwner.HAPI_ATTROWNER_VERTEX;
			attrInfo.storage = HAPI_StorageType.HAPI_STORAGETYPE_FLOAT;
			attrInfo.count = partInfo.vertexCount;
			attrInfo.tupleSize = tupleSize;
			attrInfo.originalOwner = HAPI_AttributeOwner.HAPI_ATTROWNER_INVALID;

			float[] attrValues = new float[partInfo.vertexCount * tupleSize];

			if (session.AddAttribute(geoID, 0, attrName, ref attrInfo))
			{
				for (int i = 0; i < partInfo.vertexCount; ++i)
				{
					for (int j = 0; j < tupleSize; ++j)
					{
						attrValues[i * tupleSize + j] = data[indices[i] * tupleSize + j];
					}
				}
			}

			return HEU_GeneralUtility.SetAttributeArray(geoID, partID, attrName, ref attrInfo, attrValues, session.SetAttributeFloatData, partInfo.vertexCount);
		}

		/// <summary>
		/// Uploads given mesh geometry into Houdini.
		/// Creates a new part for given geo node, and uploads vertices, indices, UVs, Normals, and Colors.
		/// </summary>
		/// <param name="session"></param>
		/// <param name="assetNodeID"></param>
		/// <param name="objectID"></param>
		/// <param name="geoID"></param>
		/// <param name="mesh"></param>
		/// <returns>True if successfully uploaded all required data.</returns>
		public static bool UploadMeshIntoHoudiniNode(HEU_SessionBase session, HAPI_NodeId assetNodeID, HAPI_NodeId objectID, HAPI_NodeId geoID, ref Mesh mesh)
		{
			bool bSuccess = false;

			Vector3[] vertices = mesh.vertices;
			int[] triIndices = mesh.triangles;
			Vector2[] uvs = mesh.uv;
			Vector3[] normals = mesh.normals;
			Color[] colors = mesh.colors;

			HAPI_PartInfo partInfo = new HAPI_PartInfo();
			partInfo.faceCount = triIndices.Length / 3;
			partInfo.vertexCount = triIndices.Length;
			partInfo.pointCount = vertices.Length;
			partInfo.pointAttributeCount = 1;
			partInfo.vertexAttributeCount = 0;
			partInfo.primitiveAttributeCount = 0;
			partInfo.detailAttributeCount = 0;

			if (uvs != null && uvs.Length > 0)
			{
				partInfo.pointAttributeCount++;
			}
			if (normals != null && normals.Length > 0)
			{
				partInfo.pointAttributeCount++;
			}
			if (colors != null && colors.Length > 0)
			{
				partInfo.pointAttributeCount++;
			}

			bSuccess = session.SetPartInfo(geoID, 0, ref partInfo);
			if (!bSuccess)
			{
				return false;
			}

			int[] faceCounts = new int[partInfo.faceCount];
			for (int i = 0; i < partInfo.faceCount; ++i)
			{
				faceCounts[i] = 3;
			}
			bSuccess = HEU_GeneralUtility.SetArray2Arg(geoID, 0, session.SetFaceCount, faceCounts, 0, partInfo.faceCount);
			if (!bSuccess)
			{
				return false;
			}

			int[] vertexList = new int[partInfo.vertexCount];
			for (int i = 0; i < partInfo.faceCount; ++i)
			{
				for (int j = 0; j < 3; ++j)
				{
					vertexList[i * 3 + j] = triIndices[i * 3 + j];
				}
			}
			bSuccess = HEU_GeneralUtility.SetArray2Arg(geoID, 0, session.SetVertexList, vertexList, 0, partInfo.vertexCount);
			if (!bSuccess)
			{
				return false;
			}

			bSuccess = HEU_InputMeshUtility.SetMeshPointAttribute(session, geoID, 0, HEU_Defines.HAPI_ATTRIB_POSITION, 3, vertices, ref partInfo, true);
			if (!bSuccess)
			{
				return false;
			}

			bSuccess = HEU_InputMeshUtility.SetMeshPointAttribute(session, geoID, 0, HEU_Defines.HAPI_ATTRIB_NORMAL, 3, normals, ref partInfo, true);
			if (!bSuccess)
			{
				return false;
			}

			if (uvs != null && uvs.Length > 0)
			{
				Vector3[] uvs3 = new Vector3[uvs.Length];
				for (int i = 0; i < uvs.Length; ++i)
				{
					uvs3[i][0] = uvs[i][0];
					uvs3[i][1] = uvs[i][1];
					uvs3[i][2] = 0;
				}
				bSuccess = HEU_InputMeshUtility.SetMeshPointAttribute(session, geoID, 0, HEU_Defines.HAPI_ATTRIB_UV, 3, uvs3, ref partInfo, false);
				if (!bSuccess)
				{
					return false;
				}
			}

			if (colors != null && colors.Length > 0)
			{
				Vector3[] rgb = new Vector3[colors.Length];
				Vector3[] alpha = new Vector3[colors.Length];
				for (int i = 0; i < colors.Length; ++i)
				{
					rgb[i][0] = colors[i].r;
					rgb[i][1] = colors[i].g;
					rgb[i][2] = colors[i].b;

					alpha[i][0] = colors[i].a;
				}

				bSuccess = HEU_InputMeshUtility.SetMeshPointAttribute(session, geoID, 0, HEU_Defines.HAPI_ATTRIB_COLOR, 3, rgb, ref partInfo, false);
				if (!bSuccess)
				{
					return false;
				}

				bSuccess = HEU_InputMeshUtility.SetMeshPointAttribute(session, geoID, 0, HEU_Defines.HAPI_ATTRIB_ALPHA, 1, alpha, ref partInfo, false);
				if (!bSuccess)
				{
					return false;
				}
			}

			// TODO: additional attributes (for painting)

			return session.CommitGeo(geoID);
		}
	}

}   // HoudiniEngineUnity