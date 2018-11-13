﻿using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace com.tencent.pandora.tools
{
    public class ReferenceChecker
    {
        private static ReferenceChecker _instance;
        private Dictionary<int, string> _referenceDescriptionMap = new Dictionary<int, string>();
        private Dictionary<object, int> _referenceDataWhenPanelOpened;
        private Dictionary<object, int> _referenceDataWhenPanelClosed;

        private string _printObjMapKey = "PRINT_ORIGINAL_REFERENCE";

        public static ReferenceChecker Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ReferenceChecker();
                }
                return _instance;
            }
        }

        public Dictionary<int, string> ReferenceDescription
        {
            get
            {
                return _referenceDescriptionMap;
            }
        }

        public string PrintObjMapKey
        {
            get { return _printObjMapKey; }
        }

        public Dictionary<object, int> GetReferenceDataWhenPanelOpened()
        {
            if (Application.isPlaying == false)
            {
                DisplayWarningDialog("游戏工程没有运行，请运行后操作！");
                return null;
            }
            _referenceDataWhenPanelOpened = GetReferenceData();
            SetReferenceDescription(_referenceDataWhenPanelOpened, ref _referenceDescriptionMap);
            return _referenceDataWhenPanelOpened;
        }

        public Dictionary<object, int> GetReferenceDataWhenPanelClosed()
        {
            if (Application.isPlaying == false)
            {
                DisplayWarningDialog("游戏工程已结束运行，请在运行时操作！");
                return null;
            }
            //先调用lua gc
            LuaGC();
            _referenceDataWhenPanelClosed = GetReferenceData();
            //更新_referenceDescriptionMap，只保留未被释放的。
            UpdateReferenceDescription(_referenceDataWhenPanelClosed, ref _referenceDescriptionMap);
            return _referenceDataWhenPanelClosed;
        }

        private void UpdateReferenceDescription(Dictionary<object, int> referenceData, ref Dictionary<int, string> referenceDescriptionMap)
        {
            Dictionary<int, string> newMap = new Dictionary<int, string>();
            string description = "";
            foreach (var item in referenceData)
            {
                if (referenceDescriptionMap.TryGetValue(item.Value, out description))
                {
                    newMap[item.Value] = description;
                }
            }
            referenceDescriptionMap = newMap;
        }

        private void LuaGC()
        {
            object luaStatePointer = GetLuaStatePointer();
            if (luaStatePointer == null)
            {
                return;
            }
            LuaDLL.pua_gc((IntPtr)luaStatePointer, LuaGCOptions.LUA_GCCOLLECT, 0);
        }

        private object GetLuaStatePointer()
        {
            Type luaStateType = FindType("com.tencent.pandora.LuaState");
            object luaState = luaStateType.GetField("main").GetValue(null);
            if (luaState == null)
            {
                return null;
            }
            PropertyInfo pointerPropertyInfo = luaStateType.GetProperty("L", BindingFlags.Public | BindingFlags.Instance);
            object luaStatePointer = pointerPropertyInfo.GetValue(luaState, null);
            return luaStatePointer;
        }

        private Dictionary<object, int> GetReferenceData()
        {
            object luaStatePointer = GetLuaStatePointer();
            if (luaStatePointer == null)
            {
                return new Dictionary<object, int>();
            }
            Type objectCacheType = FindType("com.tencent.pandora.ObjectCache");
            MethodInfo objectCacheGetInfo = objectCacheType.GetMethod("get", BindingFlags.Public | BindingFlags.Static);
            object objectCache = objectCacheGetInfo.Invoke(null, new object[] { luaStatePointer });

            FieldInfo objMapInfo = objectCacheType.GetField("objMap", BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<object, int> objMap = objMapInfo.GetValue(objectCache) as Dictionary<object, int>;

            PrintOriginalReferenceData(objMap);
            return objMap;
        }

        private Type FindType(string typeName)
        {
            Type type = Type.GetType(typeName);

            if (type != null)
            {
                return type;
            }
            else
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly assembly = assemblies[i];
                    type = assembly.GetType(typeName);
                    if (type != null)
                    {
                        return type;
                    }
                }
                return null;
            }
        }

        private void FilterRefereceData(ref Dictionary<object, int> source)
        {
            List<object> deleteList = new List<object>();
            foreach (var item in source)
            {
                bool valid = (item.Key is UnityEngine.GameObject || item.Key is UnityEngine.Component);
                if (valid == false)
                {
                    deleteList.Add(item.Key);
                }
            }

            for (int i = 0, length = deleteList.Count; i < length; i++)
            {
                source.Remove(deleteList[i]);
            }
        }

        private void SetReferenceDescription(Dictionary<object, int> referenceData, ref Dictionary<int, string> referenceDescriptionMap)
        {
            referenceDescriptionMap.Clear();
            GameObject go = null;
            Component component = null;
            string description = "";

            foreach (var item in referenceData)
            {
                description = "";
                if (item.Key == null)
                {
                    continue;
                }
                if (item.Key is GameObject)
                {
                    go = item.Key as GameObject;
                    description = string.Format("ObjInfo：{0},\t Reference Index:{1} \r\n Path In Hierarchy:\r\n{2}", item.Key, item.Value, GetTransformPath(go.transform));
                }
                else if (item.Key is Component)
                {
                    component = item.Key as Component;
                    description = string.Format("ObjInfo：{0},\t Reference Index:{1} \r\n Path In Hierarchy:\r\n{2}", item.Key, item.Value, GetTransformPath(component.transform));
                }

                if (string.IsNullOrEmpty(description) == false)
                {

                    referenceDescriptionMap[item.Value] = description;
                }
            }
        }

        //path 是相对于活动面板的，把UI Root，Canvas 头去掉。
        private string GetTransformPath(Transform trans)
        {
            if (trans == null)
            {
                return "";
            }
            Transform parentTrans = trans;
            StringBuilder sb = new StringBuilder();
            while (parentTrans != null)
            {
                sb.Insert(0, parentTrans.name);
                sb.Insert(0, "/");
                parentTrans = parentTrans.parent;
            }
            string path = sb.ToString(1, sb.Length - 1);

            string rootNodeName = "";
            if (path.Contains("UI Root") == true)
            {
                rootNodeName = "UI Root";
            }
            else if (path.Contains("Canvas") == true)
            {
                rootNodeName = "Canvas";
            }

            int rootNodeNameIndex = path.IndexOf(rootNodeName);
            int subIndex = rootNodeNameIndex + rootNodeName.Length + 1;

            if (rootNodeNameIndex != -1 && subIndex < path.Length)
            {
                return path.Substring(subIndex);
            }
            else
            {
                return path;
            }
        }

        public static void DisplayWarningDialog(string message, string title = "")
        {
            EditorUtility.DisplayDialog(title, message, "我知道了");
        }

        public void PrintOriginalReferenceData(Dictionary<object, int> dict)
        {
            if (EditorPrefs.GetBool(_printObjMapKey, false) == false)
            {
                return;
            }

            foreach (var item in dict)
            {
                Debug.Log(string.Format("<color=#ffff00>object:{0},index:{1}</color>", item.Key, item.Value));
            }
        }
    }
}