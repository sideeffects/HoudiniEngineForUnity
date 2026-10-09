using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoudiniEngineUnity
{
    // Records source element ownership instead of inferring it from GameObject names.
    [Serializable]
    internal class HEU_OutputAttributeScope
    {
        public GameObject _gameObject;

        public List<GameObject> _lodObjects = new List<GameObject>();

        public int[] _primitives = new int[0];

        public int[] _points = new int[0];

        public int[] _vertices = new int[0];

        public bool _isInstance;

        public bool _isPackedInstance;

        public int _instanceCount;

        public string _path;

        public List<Component> _scriptComponents = new List<Component>();

        public List<Component> _ownedScriptComponents = new List<Component>();

        public bool _ownsStore;

        internal int[] Indices(HAPI_AttributeOwner owner)
        {
            switch (owner)
            {
                case HAPI_AttributeOwner.HAPI_ATTROWNER_PRIM:
                    return _primitives;

                case HAPI_AttributeOwner.HAPI_ATTROWNER_POINT: 
                    return _points;

                case HAPI_AttributeOwner.HAPI_ATTROWNER_VERTEX: 
                    return _vertices;

                case HAPI_AttributeOwner.HAPI_ATTROWNER_DETAIL: 
                    return new int[] { 0 };

                default:
                    return new int[0];
            }
        }

        internal static HEU_OutputAttributeScope ForMesh(GameObject go, HEU_GenerateGeoCache cache, string[] paths, string path)
        {
            HEU_OutputAttributeScope scope = new HEU_OutputAttributeScope();
            scope._gameObject = go;
            scope._path = path;

            List<int> faces = new List<int>();
            List<int> vertices = new List<int>();
            SortedSet<int> points = new SortedSet<int>();
            int offset = 0;
            for (int face = 0; face < cache._faceCounts.Length; ++face)
            {
                if (String.Equals(paths[face], path, StringComparison.Ordinal))
                {
                    faces.Add(face);
                    for (int v = 0; v < cache._faceCounts[face]; ++v)
                    {
                        vertices.Add(offset + v);
                        points.Add(cache._vertexList[offset + v]);
                    }
                }
                offset += cache._faceCounts[face];
            }
            scope._primitives = faces.ToArray();
            scope._vertices = vertices.ToArray();
            scope._points = new List<int>(points).ToArray();
            return scope;
        }
    }

    // Short-lived per cook/phase cache: never serialize stale HAPI attribute values.
    internal class HEU_OutputAttributeReader
    {
        private readonly HEU_SessionBase _session;

        private readonly int _geoID;

        private readonly int _partID;

        private readonly Dictionary<string, HEU_OutputAttribute> _attributes = new Dictionary<string, HEU_OutputAttribute>();

        private readonly HashSet<string> _warnings = new HashSet<string>();

        private static readonly HAPI_AttributeOwner[] MeshOwners = {
            HAPI_AttributeOwner.HAPI_ATTROWNER_PRIM, HAPI_AttributeOwner.HAPI_ATTROWNER_POINT,
            HAPI_AttributeOwner.HAPI_ATTROWNER_VERTEX, HAPI_AttributeOwner.HAPI_ATTROWNER_DETAIL };

        private static readonly HAPI_AttributeOwner[] PackedOwners = {
            HAPI_AttributeOwner.HAPI_ATTROWNER_POINT, HAPI_AttributeOwner.HAPI_ATTROWNER_PRIM,
            HAPI_AttributeOwner.HAPI_ATTROWNER_DETAIL };

        private static readonly HAPI_AttributeOwner[] InstanceOwners = {
            HAPI_AttributeOwner.HAPI_ATTROWNER_POINT, HAPI_AttributeOwner.HAPI_ATTROWNER_DETAIL };

        internal HEU_OutputAttributeReader(HEU_SessionBase session, int geoID, int partID)
        {
            _session = session; _geoID = geoID; _partID = partID;
        }

        private HEU_OutputAttribute Read(string name, HAPI_AttributeOwner owner)
        {
            string key = ((int)owner).ToString() + ":" + name;

            HEU_OutputAttribute result;
            if (_attributes.TryGetValue(key, out result))
                return result;

            HAPI_AttributeInfo info = new HAPI_AttributeInfo();
            if (!_session.GetAttributeInfo(_geoID, _partID, name, owner, ref info) || !info.exists || info.count < 1 || info.tupleSize < 1)
            {
                _attributes[key] = null;
                return null;
            }

            result = HEU_GeneralUtility.CreateOutputAttributeHelper(name, ref info);

            bool success = false;
            int size = info.count * info.tupleSize;
            if (info.storage == HAPI_StorageType.HAPI_STORAGETYPE_STRING)
            {
                int[] handles = new int[size];
                success = HEU_GeneralUtility.GetAttributeArray(_geoID, _partID, name, ref info, handles, _session.GetAttributeStringData, info.count);
                if (success)
                {
                    result._stringValues = HEU_SessionManager.GetStringValuesFromStringIndices(handles);
                    success = result._stringValues != null && result._stringValues.Length == size;
                }
            }
            else if (info.storage == HAPI_StorageType.HAPI_STORAGETYPE_INT)
            {
                result._intValues = new int[size];
                success = HEU_GeneralUtility.GetAttributeArray(_geoID, _partID, name, ref info, result._intValues, _session.GetAttributeIntData, info.count);
            }
            else if (info.storage == HAPI_StorageType.HAPI_STORAGETYPE_FLOAT)
            {
                result._floatValues = new float[size];
                success = HEU_GeneralUtility.GetAttributeArray(_geoID, _partID, name, ref info, result._floatValues, _session.GetAttributeFloatData, info.count);
            }

            if (!success)
            {
                Warn(key, "Unable to read scoped attribute '" + name + "' (supported storage: string, int, float).");
                result = null;
            }
            _attributes[key] = result;
            return result;
        }

        private void Warn(string key, string message)
        {
            if (_warnings.Add(key))
                HEU_Logger.LogWarning(message);
        }

        private HEU_OutputAttribute Resolve(HEU_OutputAttributeScope scope, string name, out int[] indices)
        {
            foreach (HAPI_AttributeOwner owner in scope._isPackedInstance ? PackedOwners : (scope._isInstance ? InstanceOwners : MeshOwners))
            {
                indices = scope.Indices(owner);
                if (indices.Length == 0)
                    continue;

                HEU_OutputAttribute attr = Read(name, owner);

                if (attr == null)
                    continue;

                if (scope._isPackedInstance && owner != HAPI_AttributeOwner.HAPI_ATTROWNER_DETAIL && attr._count != scope._instanceCount)
                {
                    Warn(name + ":packed-count:" + owner, "Attribute '" + name + "' on " + owner
                        + " does not match the packed transform count. Ignoring this owner.");
                    continue;
                }

                bool valid = true;
                foreach (int index in indices) if (index < 0 || index >= attr._count) 
                { 
                        valid = false; 
                        break; 
                }

                if (valid) 
                    return attr;

                Warn(name + ":range", "Source indices are outside attribute '" + name + "'. Ignoring that owner.");
            }
            indices = new int[0];
            return null;
        }

        internal HEU_OutputAttribute Select(HEU_OutputAttributeScope scope, string name)
        {
            int[] indices;
            HEU_OutputAttribute source = Resolve(scope, name, out indices);
            if (source == null) 
                return null;

            HEU_OutputAttribute result = new HEU_OutputAttribute();
            result._name = name;
            result._class = source._class;
            result._type = source._type;
            result._tupleSize = source._tupleSize;
            result._count = indices.Length;

            result._intValues = Slice(source._intValues, indices, source._tupleSize);
            result._floatValues = Slice(source._floatValues, indices, source._tupleSize);
            result._stringValues = Slice(source._stringValues, indices, source._tupleSize);

            return result;
        }

        internal static T[] Slice<T>(T[] values, int[] indices, int tupleSize)
        {
            if (values == null) 
                return null;

            T[] result = new T[indices.Length * tupleSize];
            for (int i = 0; i < indices.Length; ++i)
                Array.Copy(values, indices[i] * tupleSize, result, i * tupleSize, tupleSize);
            return result;
        }

        private HEU_OutputAttribute Scalar(HEU_OutputAttributeScope scope, string name, HAPI_StorageType type, out int index)
        {
            index = 0;

            int[] indices;
            HEU_OutputAttribute attr = Resolve(scope, name, out indices);            
            if (attr == null) 
                return null;

            if (attr._tupleSize != 1 || attr._type != type)
            {
                Warn(name + ":type", name + " must be a scalar " + type + ". Ignoring the property.");
                return null;
            }

            index = indices[0];
            foreach (int other in indices)
            {
                bool different = type == HAPI_StorageType.HAPI_STORAGETYPE_STRING
                    ? !String.Equals(attr._stringValues[index], attr._stringValues[other], StringComparison.Ordinal)
                    : attr._intValues[index] != attr._intValues[other];

                if (different)
                {
                    Warn(name + ":" + scope._path, "Conflicting " + name + " values for output '" + scope._path
                        + "'. Using its first source element. Use different unity_path values for different object properties.");
                    break;
                }
            }
            return attr;
        }

        internal string StringValue(HEU_OutputAttributeScope scope, string name)
        {
            int index;
            HEU_OutputAttribute attr = Scalar(scope, name, HAPI_StorageType.HAPI_STORAGETYPE_STRING, out index);
            return attr != null ? attr._stringValues[index] : null;
        }

        internal int? IntValue(HEU_OutputAttributeScope scope, string name)
        {
            int index;
            HEU_OutputAttribute attr = Scalar(scope, name, HAPI_StorageType.HAPI_STORAGETYPE_INT, out index);
            return attr != null ? (int?)attr._intValues[index] : null;
        }

        internal bool UseInstanceFlags(HEU_OutputAttributeScope scope)
        {
            return scope._isInstance && IntValue(scope, HEU_Defines.UNITY_USE_INSTANCE_FLAGS_ATTR) == 1;
        }

        // Copy each node independently: isStatic alone loses partial editor static masks.
        internal static void CopyOutputFlags(GameObject source, GameObject target)
        {
            HEU_GeneralUtility.CopyFlags(source, target, false);
#if UNITY_EDITOR
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(target,
                UnityEditor.GameObjectUtility.GetStaticEditorFlags(source));
#endif
        }

        internal void ApplyFlags(HEU_OutputAttributeScope scope)
        {
            if (!UseInstanceFlags(scope))
            {
                string tag = StringValue(scope, HEU_PluginSettings.UnityTagAttributeName);
                string layerName = StringValue(scope, HEU_PluginSettings.UnityLayerAttributeName);
                int? isStatic = IntValue(scope, HEU_PluginSettings.UnityStaticAttributeName);
                int layer = String.IsNullOrEmpty(layerName) ? -1 : LayerMask.NameToLayer(layerName);

                if (!String.IsNullOrEmpty(layerName) && layer < 0)
                    Warn("layer:" + layerName, "Unity layer '" + layerName + "' does not exist. Add it in Unity's Tags and Layers settings.");

                ApplyFlagsTo(scope._gameObject, scope._isInstance, tag, layer, isStatic);

                foreach (GameObject lod in scope._lodObjects)
                    ApplyFlagsTo(lod, false, tag, layer, isStatic);
            }

#if UNITY_EDITOR && UNITY_2018_3_OR_NEWER
            if (scope._isInstance && scope._gameObject != null)
            {
                foreach (Transform transform in scope._gameObject.GetComponentsInChildren<Transform>(true))
                {
                    if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(transform.gameObject))
                        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
                }
                foreach (MeshRenderer renderer in scope._gameObject.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(renderer))
                        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
#endif
        }

        private void ApplyFlagsTo(GameObject go, bool recursive, string tag, int layer, int? isStatic)
        {
            if (go == null)
                return;

            if (!String.IsNullOrEmpty(tag))
            {
                try 
                {
                    HEU_GeneralUtility.SetTag(go, tag, recursive);
                }
                catch (Exception ex)
                { 
                    Warn("tag:" + tag, "Cannot assign Unity tag '" + tag + "': " + ex.Message);
                }
            }

            if (layer >= 0) 
                HEU_GeneralUtility.SetLayer(go, layer, recursive);

            if (isStatic.HasValue) 
                HEU_EditorUtility.SetStatic(go, isStatic.Value == 1, recursive);
        }

        internal void ApplyScript(HEU_OutputAttributeScope scope)
        {
            string script = StringValue(scope, HEU_PluginSettings.UnityScriptAttributeName);

            if (String.IsNullOrEmpty(script))
                return;

            HashSet<Component> previous = new HashSet<Component>(scope._gameObject.GetComponents<Component>());
            HEU_GeneralUtility.AttachScriptWithInvokeFunction(script, scope._gameObject);
            foreach (Component component in scope._gameObject.GetComponents<Component>())
            {
                if (component != null && !previous.Contains(component))
                {
                    if (!scope._ownedScriptComponents.Contains(component))
                        scope._ownedScriptComponents.Add(component);

                    if (!scope._scriptComponents.Contains(component))
                        scope._scriptComponents.Add(component);
                }
            }
            foreach (string token in script.Split(';'))
            {
                string typeName = token.Split(':')[0].Trim();
                Type type = HEU_GeneralUtility.GetSystemTypeByName(typeName);
                Component component = type != null ? scope._gameObject.GetComponent(type) : null;

                if (component != null && !scope._scriptComponents.Contains(component)) 
                    scope._scriptComponents.Add(component);
            }
        }

        internal void UpdateStore(HEU_OutputAttributeScope scope)
        {
            string names = StringValue(scope, HEU_Defines.HENGINE_STORE_ATTR);
            if (String.IsNullOrEmpty(names))
            {
                // An absent store attribute should not strip a component authored in a prefab.
                if (!scope._isInstance) 
                    HEU_GeneralUtility.DestroyComponent<HEU_OutputAttributesStore>(scope._gameObject);

                return;
            }

            if (scope._gameObject.GetComponent<HEU_OutputAttributesStore>() == null) 
                scope._ownsStore = true;

            HEU_OutputAttributesStore store = HEU_GeneralUtility.GetOrCreateComponent<HEU_OutputAttributesStore>(scope._gameObject);
            store.Clear();
            HashSet<string> stored = new HashSet<string>(StringComparer.Ordinal);
            foreach (string token in names.Split(','))
            {
                string name = token.Trim();
                if (name.Length == 0 || !stored.Add(name)) 
                    continue;

                HEU_OutputAttribute attr = Select(scope, name);
                if (attr != null) 
                    store.SetAttribute(attr);
            }
        }
    }
}
