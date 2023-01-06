/*
Copyright (c) 2015-2017 topameng(topameng@qq.com)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
//打开开关没有写入导出列表的纯虚类自动跳过
//#define JUMP_NODEFINED_ABSTRACT

using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.IO;
using System.Linq;
using System.Diagnostics;
using LuaInterface;

using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using Debugger = LuaInterface.Debugger;
using System.Threading;

[InitializeOnLoad]
public static class ToLuaMenu
{
    //不需要导出或者无法导出的类型
    public static List<Type> dropType = new List<Type>
    {
        typeof(ValueType),                                  //不需要
#if UNITY_4_6 || UNITY_4_7
        typeof(Motion),                                     //很多平台只是空类
#endif

#if UNITY_5_3_OR_NEWER
        typeof(UnityEngine.CustomYieldInstruction),
#endif
        typeof(UnityEngine.YieldInstruction),               //无需导出的类
        typeof(UnityEngine.WaitForEndOfFrame),              //内部支持
        typeof(UnityEngine.WaitForFixedUpdate),
        typeof(UnityEngine.WaitForSeconds),
        typeof(UnityEngine.Mathf),                          //lua层支持
        typeof(Plane),
        typeof(LayerMask),
        typeof(Vector3),
        typeof(Vector4),
        typeof(Vector2),
        typeof(Quaternion),
        typeof(Ray),
        typeof(Bounds),
        typeof(Color),
        typeof(Touch),
        typeof(RaycastHit),
        typeof(TouchPhase),
        //typeof(LuaInterface.LuaOutMetatable),               //手写支持
        typeof(LuaInterface.NullObject),
        typeof(System.Array),
        typeof(System.Reflection.MemberInfo),
        typeof(System.Reflection.BindingFlags),
        typeof(LuaInterface.LuaFunction),
        typeof(LuaInterface.LuaTable),
        typeof(LuaInterface.LuaThread),
        typeof(LuaInterface.LuaByteBuffer),                 //只是类型标识符
        typeof(DelegateFactory),                            //无需导出，导出类支持lua函数转换为委托。如UIEventListener.OnClick(luafunc)
    };

    //可以导出的内部支持类型
    public static List<Type> baseType = new List<Type>
    {
        typeof(System.Object),
        typeof(System.Delegate),
        typeof(System.String),
        typeof(System.Enum),
        typeof(System.Type),
        typeof(System.Collections.IEnumerator),
        typeof(UnityEngine.Object),
        typeof(LuaInterface.EventObject),
    };

    private static bool beAutoGen = false;
    private static bool beCheck = true;
    static List<BindType> allTypes = new List<BindType>();

    static ToLuaMenu()
    {
        string dir = CustomSettings.saveDir;
        string[] files = Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly);

        if (files.Length < 3 && beCheck)
        {
            if (EditorUtility.DisplayDialog("自动生成", "点击确定自动生成常用类型注册文件， 也可通过菜单逐步完成此功能", "确定", "取消"))
            {
                beAutoGen = true;
                GenLuaDelegates();
                AssetDatabase.Refresh();
                GenerateClassWraps();
                GenLuaBinder();
                beAutoGen = false;
            }

            beCheck = false;
        }
    }

    static string RemoveNameSpace(string name, string space)
    {
        if (space != null)
        {
            name = name.Remove(0, space.Length + 1);
        }

        return name;
    }

    public class BindType
    {
        public string name;                 //类名称
        public Type type;
        public bool IsStatic;
        public string wrapName = "";        //产生的wrap文件名字
        public string libName = "";         //注册到lua的名字
        public Type baseType = null;
        public string nameSpace = null;     //注册到lua的table层级

        public List<Type> extendList = new List<Type>();

        public BindType(Type t)
        {
            if (typeof(System.MulticastDelegate).IsAssignableFrom(t))
            {
                throw new NotSupportedException(string.Format("\nDon't export Delegate {0} as a class, register it in customDelegateList", LuaMisc.GetTypeName(t)));
            }

            //if (IsObsolete(t))
            //{
            //    throw new Exception(string.Format("\n{0} is obsolete, don't export it!", LuaMisc.GetTypeName(t)));
            //}

            type = t;
            nameSpace = ToLuaExport.GetNameSpace(t, out libName);
            name = ToLuaExport.CombineTypeStr(nameSpace, libName);
            libName = ToLuaExport.ConvertToLibSign(libName);

            if (name == "object")
            {
                wrapName = "System_Object";
                name = "System.Object";
            }
            else if (name == "string")
            {
                wrapName = "System_String";
                name = "System.String";
            }
            else
            {
                wrapName = name.Replace('.', '_');
                wrapName = ToLuaExport.ConvertToLibSign(wrapName);
            }

            int index = CustomSettings.staticClassTypes.IndexOf(type);

            if (index >= 0 || (type.IsAbstract && type.IsSealed))
            {
                IsStatic = true;
            }

            baseType = LuaMisc.GetExportBaseType(type);
        }

        public BindType SetBaseType(Type t)
        {
            baseType = t;
            return this;
        }

        public BindType AddExtendType(Type t)
        {
            if (!extendList.Contains(t))
            {
                extendList.Add(t);
            }

            return this;
        }

        public BindType SetWrapName(string str)
        {
            wrapName = str;
            return this;
        }

        public BindType SetLibName(string str)
        {
            libName = str;
            return this;
        }

        public BindType SetNameSpace(string space)
        {
            nameSpace = space;
            return this;
        }

        public static bool IsObsolete(Type type)
        {
            object[] attrs = type.GetCustomAttributes(true);

            for (int j = 0; j < attrs.Length; j++)
            {
                Type t = attrs[j].GetType();

                if (t == typeof(System.ObsoleteAttribute) || t == typeof(NoToLuaAttribute) || t.Name == "MonoNotSupportedAttribute" || t.Name == "MonoTODOAttribute")
                {
                    return true;
                }
            }

            return false;
        }
    }

    static void AutoAddBaseType(BindType bt, bool beDropBaseType)
    {
        Type t = bt.baseType;

        if (t == null)
        {
            return;
        }

        if (CustomSettings.sealedList.Contains(t))
        {
            CustomSettings.sealedList.Remove(t);
            Debugger.LogError("{0} not a sealed class, it is parent of {1}", LuaMisc.GetTypeName(t), bt.name);
        }

        if (t.IsInterface)
        {
            Debugger.LogWarning("{0} has a base type {1} is Interface, use SetBaseType to jump it", bt.name, t.FullName);
            bt.baseType = t.BaseType;
        }
        else if (dropType.IndexOf(t) >= 0)
        {
            Debugger.LogWarning("{0} has a base type {1} is a drop type", bt.name, t.FullName);
            bt.baseType = t.BaseType;
        }
        else if (!beDropBaseType || baseType.IndexOf(t) < 0)
        {
            int index = allTypes.FindIndex((iter) => { return iter.type == t; });

            if (index < 0)
            {
#if JUMP_NODEFINED_ABSTRACT
                if (t.IsAbstract && !t.IsSealed)
                {
                    Debugger.LogWarning("not defined bindtype for {0}, it is abstract class, jump it, child class is {1}", LuaMisc.GetTypeName(t), bt.name);
                    bt.baseType = t.BaseType;
                }
                else
                {
                    Debugger.LogWarning("not defined bindtype for {0}, autogen it, child class is {1}", LuaMisc.GetTypeName(t), bt.name);
                    bt = new BindType(t);
                    allTypes.Add(bt);
                }
#else
                Debugger.LogWarning("not defined bindtype for {0}, autogen it, child class is {1}", LuaMisc.GetTypeName(t), bt.name);
                bt = new BindType(t);
                allTypes.Add(bt);
#endif
            }
            else
            {
                return;
            }
        }
        else
        {
            return;
        }

        AutoAddBaseType(bt, beDropBaseType);
    }

    static BindType[] GenBindTypes(BindType[] list, bool beDropBaseType = true)
    {
        allTypes = new List<BindType>(list);

        for (int i = 0; i < list.Length; i++)
        {
            for (int j = i + 1; j < list.Length; j++)
            {
                if (list[i].type == list[j].type)
                    throw new NotSupportedException("Repeat BindType:" + list[i].type);
            }

            if (dropType.IndexOf(list[i].type) >= 0)
            {
                Debug.LogWarning(list[i].type.FullName + " in dropType table, not need to export");
                allTypes.Remove(list[i]);
                continue;
            }
            else if (beDropBaseType && baseType.IndexOf(list[i].type) >= 0)
            {
                Debug.LogWarning(list[i].type.FullName + " is base type, not need to export");
                allTypes.Remove(list[i]);
                continue;
            }
            else if (list[i].type.IsEnum)
            {
                continue;
            }

            AutoAddBaseType(list[i], beDropBaseType);
        }

        return allTypes.ToArray();
    }

    // [MenuItem("Lua/Gen Lua Wrap Files", false, 1)]
    public static void GenerateClassWraps()
    {
        if (!beAutoGen && EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此功能", "确定");
            return;
        }

        if (!File.Exists(CustomSettings.saveDir))
        {
            Directory.CreateDirectory(CustomSettings.saveDir);
        }

        allTypes.Clear();
        BindType[] typeList = CustomSettings.customTypeList;

        BindType[] list = GenBindTypes(typeList);
        ToLuaExport.allTypes.AddRange(baseType);

        for (int i = 0; i < list.Length; i++)
        {
            ToLuaExport.allTypes.Add(list[i].type);
        }

        for (int i = 0; i < list.Length; i++)
        {
            ToLuaExport.Clear();
            ToLuaExport.className = list[i].name;
            ToLuaExport.type = list[i].type;
            ToLuaExport.isStaticClass = list[i].IsStatic;
            ToLuaExport.baseType = list[i].baseType;
            ToLuaExport.wrapClassName = list[i].wrapName;
            ToLuaExport.libClassName = list[i].libName;
            ToLuaExport.extendList = list[i].extendList;
            ToLuaExport.Generate(CustomSettings.saveDir);
        }

        Debug.Log("Generate Lua wrap files over");
        ToLuaExport.allTypes.Clear();
        allTypes.Clear();
        AssetDatabase.Refresh();
    }

    static HashSet<Type> GetCustomTypeDelegates()
    {
        BindType[] list = CustomSettings.customTypeList;
        HashSet<Type> set = new HashSet<Type>();
        BindingFlags binding = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.Instance;

        for (int i = 0; i < list.Length; i++)
        {
            Type type = list[i].type;
            FieldInfo[] fields = type.GetFields(BindingFlags.GetField | BindingFlags.SetField | binding);
            PropertyInfo[] props = type.GetProperties(BindingFlags.GetProperty | BindingFlags.SetProperty | binding);
            MethodInfo[] methods = null;

            if (type.IsInterface)
            {
                methods = type.GetMethods();
            }
            else
            {
                methods = type.GetMethods(BindingFlags.Instance | binding);
            }

            for (int j = 0; j < fields.Length; j++)
            {
                Type t = fields[j].FieldType;

                if (ToLuaExport.IsDelegateType(t))
                {
                    set.Add(t);
                }
            }

            for (int j = 0; j < props.Length; j++)
            {
                Type t = props[j].PropertyType;

                if (ToLuaExport.IsDelegateType(t))
                {
                    set.Add(t);
                }
            }

            for (int j = 0; j < methods.Length; j++)
            {
                MethodInfo m = methods[j];

                if (m.IsGenericMethod)
                {
                    continue;
                }

                ParameterInfo[] pifs = m.GetParameters();

                for (int k = 0; k < pifs.Length; k++)
                {
                    Type t = pifs[k].ParameterType;
                    if (t.IsByRef) t = t.GetElementType();

                    if (ToLuaExport.IsDelegateType(t))
                    {
                        set.Add(t);
                    }
                }
            }

        }

        return set;
    }

    // [MenuItem("Lua/Gen Lua Delegates", false, 2)]
    static void GenLuaDelegates()
    {
        if (!beAutoGen && EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此功能", "确定");
            return;
        }

        ToLuaExport.Clear();
        List<DelegateType> list = new List<DelegateType>();
        list.AddRange(CustomSettings.customDelegateList);
        HashSet<Type> set = GetCustomTypeDelegates();

        foreach (Type t in set)
        {
            if (null == list.Find((p) => { return p.type == t; }))
            {
                list.Add(new DelegateType(t));
            }
        }

        ToLuaExport.GenDelegates(list.ToArray());
        set.Clear();
        ToLuaExport.Clear();
        AssetDatabase.Refresh();
        Debug.Log("Generate Lua delegates over");
    }

    static ToLuaTree<string> InitTree()
    {
        ToLuaTree<string> tree = new ToLuaTree<string>();
        ToLuaNode<string> root = tree.GetRoot();
        BindType[] list = GenBindTypes(CustomSettings.customTypeList);

        for (int i = 0; i < list.Length; i++)
        {
            string space = list[i].nameSpace;
            AddSpaceNameToTree(tree, root, space);
        }

        DelegateType[] dts = CustomSettings.customDelegateList;
        string str = null;

        for (int i = 0; i < dts.Length; i++)
        {
            string space = ToLuaExport.GetNameSpace(dts[i].type, out str);
            AddSpaceNameToTree(tree, root, space);
        }

        return tree;
    }

    static void AddSpaceNameToTree(ToLuaTree<string> tree, ToLuaNode<string> parent, string space)
    {
        if (space == null || space == string.Empty)
        {
            return;
        }

        string[] ns = space.Split(new char[] { '.' });

        for (int j = 0; j < ns.Length; j++)
        {
            List<ToLuaNode<string>> nodes = tree.Find((_t) => { return _t == ns[j]; }, j);

            if (nodes.Count == 0)
            {
                ToLuaNode<string> node = new ToLuaNode<string>();
                node.value = ns[j];
                parent.childs.Add(node);
                node.parent = parent;
                node.layer = j;
                parent = node;
            }
            else
            {
                bool flag = false;
                int index = 0;

                for (int i = 0; i < nodes.Count; i++)
                {
                    int count = j;
                    int size = j;
                    ToLuaNode<string> nodecopy = nodes[i];

                    while (nodecopy.parent != null)
                    {
                        nodecopy = nodecopy.parent;
                        if (nodecopy.value != null && nodecopy.value == ns[--count])
                        {
                            size--;
                        }
                    }

                    if (size == 0)
                    {
                        index = i;
                        flag = true;
                        break;
                    }
                }

                if (!flag)
                {
                    ToLuaNode<string> nnode = new ToLuaNode<string>();
                    nnode.value = ns[j];
                    nnode.layer = j;
                    nnode.parent = parent;
                    parent.childs.Add(nnode);
                    parent = nnode;
                }
                else
                {
                    parent = nodes[index];
                }
            }
        }
    }

    static string GetSpaceNameFromTree(ToLuaNode<string> node)
    {
        string name = node.value;

        while (node.parent != null && node.parent.value != null)
        {
            node = node.parent;
            name = node.value + "." + name;
        }

        return name;
    }

    static string RemoveTemplateSign(string str)
    {
        str = str.Replace('<', '_');

        int index = str.IndexOf('>');

        while (index > 0)
        {
            str = str.Remove(index, 1);
            index = str.IndexOf('>');
        }

        return str;
    }

    // [MenuItem("Lua/Gen LuaBinder File", false, 4)]
    static void GenLuaBinder()
    {
        if (!beAutoGen && EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此功能", "确定");
            return;
        }

        allTypes.Clear();
        ToLuaTree<string> tree = InitTree();
        StringBuilder sb = new StringBuilder();
        List<DelegateType> dtList = new List<DelegateType>();

        List<DelegateType> list = new List<DelegateType>();
        list.AddRange(CustomSettings.customDelegateList);
        HashSet<Type> set = GetCustomTypeDelegates();

        List<BindType> backupList = new List<BindType>();
        backupList.AddRange(allTypes);
        ToLuaNode<string> root = tree.GetRoot();
        string libname = null;

        foreach (Type t in set)
        {
            if (null == list.Find((p) => { return p.type == t; }))
            {
                DelegateType dt = new DelegateType(t);
                AddSpaceNameToTree(tree, root, ToLuaExport.GetNameSpace(t, out libname));
                list.Add(dt);
            }
        }

        sb.Append("// This source code was auto-generated by ToLua#, do not modify it\n");
        sb.Append("using System;\n");
        sb.Append("using UnityEngine;\n");
        sb.Append("using LuaInterface;\n");
        sb.Append("\n");
        sb.Append("public static partial class LuaBinder\n");
        sb.Append("{\n");
        sb.Append("\tstatic partial void Bind_Gen(LuaState L)\n");
        sb.Append("\t{\n");
        sb.Append("\t\tfloat t = Time.realtimeSinceStartup;\n");
        sb.Append("\t\tL.BeginModule(null);\n");

        GenRegisterInfo(null, sb, list, dtList);

        Action<ToLuaNode<string>> begin = (node) =>
        {
            if (node.value == null)
            {
                return;
            }

            sb.AppendFormat("\t\tL.BeginModule(\"{0}\");\n", node.value);
            string space = GetSpaceNameFromTree(node);

            GenRegisterInfo(space, sb, list, dtList);
        };

        Action<ToLuaNode<string>> end = (node) =>
        {
            if (node.value != null)
            {
                sb.Append("\t\tL.EndModule();\n");
            }
        };

        tree.DepthFirstTraversal(begin, end, tree.GetRoot());
        sb.Append("\t\tL.EndModule();\n");

        if (CustomSettings.dynamicList.Count > 0)
        {
            sb.Append("\t\tL.BeginPreLoad();\n");

            for (int i = 0; i < CustomSettings.dynamicList.Count; i++)
            {
                Type t1 = CustomSettings.dynamicList[i];
                BindType bt = backupList.Find((p) => { return p.type == t1; });
                if (bt != null) sb.AppendFormat("\t\tL.AddPreLoad(\"{0}\", LuaOpen_{1}, typeof({0}));\n", bt.name, bt.wrapName);
            }

            sb.Append("\t\tL.EndPreLoad();\n");
        }

        // sb.Append("\t\t// Debugger.Log(\"Register lua type cost time: {0}\", Time.realtimeSinceStartup - t);\n");
        sb.Append("\t}\n");

        for (int i = 0; i < dtList.Count; i++)
        {
            ToLuaExport.GenEventFunction(dtList[i].type, sb);
        }

        if (CustomSettings.dynamicList.Count > 0)
        {

            for (int i = 0; i < CustomSettings.dynamicList.Count; i++)
            {
                Type t = CustomSettings.dynamicList[i];
                BindType bt = backupList.Find((p) => { return p.type == t; });
                if (bt != null) GenPreLoadFunction(bt, sb);
            }
        }

        sb.Append("}\n\n");
        allTypes.Clear();
        string file = CustomSettings.saveDir + "LuaBinder.Gen.cs";

        using (StreamWriter textWriter = new StreamWriter(file, false, Encoding.UTF8))
        {
            textWriter.Write(sb.ToString());
            textWriter.Flush();
            textWriter.Close();
        }

        AssetDatabase.Refresh();
        Debugger.Log("Generate LuaBinder over !");
    }

    static void GenRegisterInfo(string nameSpace, StringBuilder sb, List<DelegateType> delegateList, List<DelegateType> wrappedDelegatesCache)
    {
        for (int i = 0; i < allTypes.Count; i++)
        {
            Type dt = CustomSettings.dynamicList.Find((p) => { return allTypes[i].type == p; });

            if (dt == null && allTypes[i].nameSpace == nameSpace)
            {
                string str = "\t\t" + allTypes[i].wrapName + "Wrap.Register(L);\n";
                sb.Append(str);
                allTypes.RemoveAt(i--);
            }
        }

        string funcName = null;

        for (int i = 0; i < delegateList.Count; i++)
        {
            DelegateType dt = delegateList[i];
            Type type = dt.type;
            string typeSpace = ToLuaExport.GetNameSpace(type, out funcName);

            if (typeSpace == nameSpace)
            {
                funcName = ToLuaExport.ConvertToLibSign(funcName);
                string abr = dt.abr;
                abr = abr == null ? funcName : abr;
                sb.AppendFormat("\t\tL.RegFunction(\"{0}\", {1});\n", abr, dt.name);
                wrappedDelegatesCache.Add(dt);
            }
        }
    }

    static void GenPreLoadFunction(BindType bt, StringBuilder sb)
    {
        string funcName = "LuaOpen_" + bt.wrapName;

        sb.Append("\n\t[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]\n");
        sb.AppendFormat("\tstatic int {0}(IntPtr L)\n", funcName);
        sb.Append("\t{\n");
        sb.Append("\t\ttry\n");
        sb.Append("\t\t{\n");
        sb.Append("\t\t\tLuaState state = LuaState.Get(L);\n");
        sb.AppendFormat("\t\t\tstate.BeginPreModule(\"{0}\");\n", bt.nameSpace);
        sb.AppendFormat("\t\t\t{0}Wrap.Register(state);\n", bt.wrapName);
        sb.AppendFormat("\t\t\tint reference = state.GetMetaReference(typeof({0}));\n", bt.name);
        sb.Append("\t\t\tstate.EndPreModule(L, reference);\n");
        sb.Append("\t\t\treturn 1;\n");
        sb.Append("\t\t}\n");
        sb.Append("\t\tcatch(Exception e)\n");
        sb.Append("\t\t{\n");
        sb.Append("\t\t\treturn LuaDLL.toluaL_exception(L, e);\n");
        sb.Append("\t\t}\n");
        sb.Append("\t}\n");
    }

    static string GetOS()
    {
        return LuaConst.osDir;
    }

    static string CreateStreamDir(string dir)
    {
        dir = Application.streamingAssetsPath + "/" + dir;

        if (!File.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return dir;
    }

    static void BuildLuaBundle(string subDir, string sourceDir)
    {
        string[] files = Directory.GetFiles(sourceDir + subDir, "*.bytes");
        string bundleName = subDir == null ? "lua.unity3d" : "lua" + subDir.Replace('/', '_') + ".unity3d";
        bundleName = bundleName.ToLower();

#if UNITY_4_6 || UNITY_4_7
        List<Object> list = new List<Object>();

        for (int i = 0; i < files.Length; i++)
        {
            Object obj = AssetDatabase.LoadMainAssetAtPath(files[i]);
            list.Add(obj);
        }

        BuildAssetBundleOptions options = BuildAssetBundleOptions.CollectDependencies | BuildAssetBundleOptions.CompleteAssets | BuildAssetBundleOptions.DeterministicAssetBundle;

        if (files.Length > 0)
        {
            string output = string.Format("{0}/{1}/" + bundleName, Application.streamingAssetsPath, GetOS());
            File.Delete(output);
            BuildPipeline.BuildAssetBundle(null, list.ToArray(), output, options, EditorUserBuildSettings.activeBuildTarget);
        }
#else
        for (int i = 0; i < files.Length; i++)
        {
            AssetImporter importer = AssetImporter.GetAtPath(files[i]);

            if (importer)
            {
                importer.assetBundleName = bundleName;
                importer.assetBundleVariant = null;
            }
        }
#endif
    }

    static void ClearAllLuaFiles()
    {
        string osPath = Application.streamingAssetsPath + "/" + GetOS();

        if (Directory.Exists(osPath))
        {
            string[] files = Directory.GetFiles(osPath, "Lua*.unity3d");

            for (int i = 0; i < files.Length; i++)
            {
                File.Delete(files[i]);
            }
        }

        string path = osPath + "/Lua";

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        path = Application.streamingAssetsPath + "/Lua";

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        path = Application.dataPath + "/temp";

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        path = Application.dataPath + "/Resources/Lua";

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        path = Application.persistentDataPath + "/" + GetOS() + "/Lua";

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    // [MenuItem("Lua/Gen LuaWrap + Binder", false, 4)]
    static void GenLuaWrapBinder()
    {
        if (EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此功能", "确定");
            return;
        }

        beAutoGen = true;
        AssetDatabase.Refresh();
        GenerateClassWraps();
        GenLuaBinder();
        beAutoGen = false;
    }

    [MenuItem("Lua/Generate All", false, 5)]
    static void GenLuaAll()
    {
        if (EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此功能", "确定");
            return;
        }

        beAutoGen = true;
        GenLuaDelegates();
        AssetDatabase.Refresh();
        GenerateClassWraps();
        GenLuaBinder();
        beAutoGen = false;
    }

    [MenuItem("Lua/Clear Wrap Files", false, 6)]
    static void ClearLuaWraps()
    {
        string[] files = Directory.GetFiles(CustomSettings.saveDir, "*.cs", SearchOption.TopDirectoryOnly);

        for (int i = 0; i < files.Length; i++)
        {
            File.Delete(files[i]);
        }

        ToLuaExport.Clear();

        // List<DelegateType> list = new List<DelegateType>();
        // ToLuaExport.GenDelegates(list.ToArray());
        // ToLuaExport.Clear();

        // StringBuilder sb = new StringBuilder();
        // sb.Append("using System;\n");
        // sb.Append("using LuaInterface;\n");
        // sb.Append("\n");
        // sb.Append("public static class LuaBinder\n");
        // sb.Append("{\n");
        // sb.Append("\tpublic static void Bind(LuaState L)\n");
        // sb.Append("\t{\n");
        // sb.Append("\t\tthrow new LuaException(\"Please generate LuaBinder files first!\\n");");
        // sb.Append("\t}\n");
        // sb.Append("}\n");
        //
        // string file = CustomSettings.saveDir + "LuaBinder.cs";
        //
        // using (StreamWriter textWriter = new StreamWriter(file, false, Encoding.UTF8))
        // {
        //     textWriter.Write(sb.ToString());
        //     textWriter.Flush();
        //     textWriter.Close();
        // }

        AssetDatabase.Refresh();
    }

    static void CopyLuaBytesFiles(string sourceDir, string destDir, bool appendext = true, string searchPattern = "*.lua", SearchOption option = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        string[] files = Directory.GetFiles(sourceDir, searchPattern, option);
        int len = sourceDir.Length;

        if (sourceDir[len - 1] == '/' || sourceDir[len - 1] == '\\')
        {
            --len;
        }

        for (int i = 0; i < files.Length; i++)
        {
            string str = files[i].Remove(0, len);
            string dest = destDir + "/" + str;
            if (appendext) dest += ".bytes";
            string dir = Path.GetDirectoryName(dest);
            Directory.CreateDirectory(dir);
            File.Copy(files[i], dest, true);
        }
    }


    [MenuItem("Lua/Copy Lua Files to Resources", false, 51)]
    public static void CopyLuaFilesToRes()
    {
        ClearAllLuaFiles();
        string destDir = Application.dataPath + "/Resources" + "/Lua";
        CopyLuaBytesFiles(LuaConst.luaDir, destDir);
        CopyLuaBytesFiles(LuaConst.toluaDir, destDir);
        AssetDatabase.Refresh();
        Debug.Log("Copy Lua files over");
    }

    // [MenuItem("Lua/Copy Lua Files to Persistent", false, 52)]
    public static void CopyLuaFilesToPersistent()
    {
        ClearAllLuaFiles();
        string destDir = Application.persistentDataPath + "/" + GetOS() + "/Lua";
        CopyLuaBytesFiles(LuaConst.luaDir, destDir, false);
        CopyLuaBytesFiles(LuaConst.toluaDir, destDir, false);
        AssetDatabase.Refresh();
        Debug.Log("Copy Lua files over");
    }

    static void GetAllDirs(string dir, List<string> list)
    {
        string[] dirs = Directory.GetDirectories(dir);
        list.AddRange(dirs);

        for (int i = 0; i < dirs.Length; i++)
        {
            GetAllDirs(dirs[i], list);
        }
    }

    static void CopyDirectory(string source, string dest, string searchPattern = "*.lua", SearchOption option = SearchOption.AllDirectories)
    {
        string[] files = Directory.GetFiles(source, searchPattern, option);

        for (int i = 0; i < files.Length; i++)
        {
            string str = files[i].Remove(0, source.Length);
            string path = dest + "/" + str;
            string dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            File.Copy(files[i], path, true);
        }
    }

    static void CopyBuildBat(string path, string tempDir)
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
        {
            File.Copy(path + "/Tools/LuaJIT/64/Build.bat", tempDir + "/Build.bat", true);
        }
        else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows)
        {
            if (IntPtr.Size == 4)
            {
                File.Copy(path + "/Tools/LuaJIT/32/Build.bat", tempDir + "/Build.bat", true);
            }
            else if (IntPtr.Size == 8)
            {
                File.Copy(path + "/Tools/LuaJIT/64/Build.bat", tempDir + "/Build.bat", true);
            }
        }
#if UNITY_5_3_OR_NEWER
        else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
#else
        else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iPhone)
#endif
        {
            //Debug.Log("iOS默认用64位，32位自行考虑");
            File.Copy(path + "/Tools/LuaJIT/64/Build.bat", tempDir + "/Build.bat", true);
        }
        else
        {
            File.Copy(path + "/Tools/LuaJIT/32/Build.bat", tempDir + "/Build.bat", true);
        }

    }

    [MenuItem("Lua/Build Lua Files to Resources (PC)", false, 53)]
    public static void BuildLuaToResources()
    {
        ClearAllLuaFiles();
        string tempDir = CreateStreamDir("Lua");
        string destDir = Application.dataPath + "/Resources" + "/Lua";

        string path = Application.dataPath.Replace('\\', '/');
        path = path.Substring(0, path.LastIndexOf('/'));
        CopyBuildBat(path, tempDir);
        CopyLuaBytesFiles(LuaConst.luaDir, tempDir, false);
        CopyLuaBytesFiles(LuaConst.toluaDir, tempDir, false);
        Process proc = Process.Start(tempDir + "/Build.bat");
        proc.WaitForExit();
        CopyLuaBytesFiles(tempDir + "/Out/", destDir, false, "*.lua.bytes");

        Directory.Delete(tempDir, true);
        var parent = Directory.GetParent(tempDir).FullName;
        if (!Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
            File.Delete(parent + ".meta");
        }

        AssetDatabase.Refresh();
    }

    // [MenuItem("Lua/Build Lua Files to Persistent (PC)", false, 54)]
    public static void BuildLuaToPersistent()
    {
        ClearAllLuaFiles();
        string tempDir = CreateStreamDir("Lua");
        string destDir = Application.persistentDataPath + "/" + GetOS() + "/Lua/";

        string path = Application.dataPath.Replace('\\', '/');
        path = path.Substring(0, path.LastIndexOf('/'));
        CopyBuildBat(path, tempDir);
        CopyLuaBytesFiles(LuaConst.luaDir, tempDir, false);
        Process proc = Process.Start(tempDir + "/Build.bat");
        proc.WaitForExit();
        CopyLuaBytesFiles(LuaConst.toluaDir, destDir, false);

        path = tempDir + "/Out/";
        string[] files = Directory.GetFiles(path, "*.lua.bytes");
        int len = path.Length;

        for (int i = 0; i < files.Length; i++)
        {
            path = files[i].Remove(0, len);
            path = path.Substring(0, path.Length - 6);
            path = destDir + path;

            File.Copy(files[i], path, true);
        }

        Directory.Delete(tempDir, true);
        AssetDatabase.Refresh();
    }

    // [MenuItem("Lua/Build Bundle Files not JIT", false, 55)]
    public static void BuildNotJitBundles()
    {
        ClearAllLuaFiles();
        CreateStreamDir(GetOS());

#if UNITY_4_6 || UNITY_4_7
        string tempDir = CreateStreamDir("Lua");
#else
        string tempDir = Application.dataPath + "/temp/Lua";

        if (!File.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }
#endif
        CopyLuaBytesFiles(LuaConst.luaDir, tempDir);
        CopyLuaBytesFiles(LuaConst.toluaDir, tempDir);

        AssetDatabase.Refresh();
        List<string> dirs = new List<string>();
        GetAllDirs(tempDir, dirs);

#if UNITY_5 || UNITY_5_3_OR_NEWER
        for (int i = 0; i < dirs.Count; i++)
        {
            string str = dirs[i].Remove(0, tempDir.Length);
            BuildLuaBundle(str.Replace('\\', '/'), "Assets/temp/Lua");
        }

        BuildLuaBundle(null, "Assets/temp/Lua");

        AssetDatabase.SaveAssets();
        string output = string.Format("{0}/{1}", Application.streamingAssetsPath, GetOS());
        BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.DeterministicAssetBundle, EditorUserBuildSettings.activeBuildTarget);

        //Directory.Delete(Application.dataPath + "/temp/", true);
#else
        for (int i = 0; i < dirs.Count; i++)
        {
            string str = dirs[i].Remove(0, tempDir.Length);
            BuildLuaBundle(str.Replace('\\', '/'), "Assets/StreamingAssets/Lua");
        }

        BuildLuaBundle(null, "Assets/StreamingAssets/Lua");
        Directory.Delete(Application.streamingAssetsPath + "/Lua/", true);
#endif
        AssetDatabase.Refresh();
    }

    // [MenuItem("Lua/Build LuaJIT Bundle Files (PC)", false, 56)]
    public static void BuildLuaBundles()
    {
        ClearAllLuaFiles();
        CreateStreamDir(GetOS());

#if UNITY_4_6 || UNITY_4_7
        string tempDir = CreateStreamDir("Lua");
#else
        string tempDir = Application.dataPath + "/temp/Lua";

        if (!File.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }
#endif

        string path = Application.dataPath.Replace('\\', '/');
        path = path.Substring(0, path.LastIndexOf('/'));
        CopyBuildBat(path, tempDir);
        CopyLuaBytesFiles(LuaConst.luaDir, tempDir, false);
        Process proc = Process.Start(tempDir + "/Build.bat");
        proc.WaitForExit();
        CopyLuaBytesFiles(LuaConst.toluaDir, tempDir + "/Out");

        AssetDatabase.Refresh();

        string sourceDir = tempDir + "/Out";
        List<string> dirs = new List<string>();
        GetAllDirs(sourceDir, dirs);

#if UNITY_5 || UNITY_5_3_OR_NEWER
        for (int i = 0; i < dirs.Count; i++)
        {
            string str = dirs[i].Remove(0, sourceDir.Length);
            BuildLuaBundle(str.Replace('\\', '/'), "Assets/temp/Lua/Out");
        }

        BuildLuaBundle(null, "Assets/temp/Lua/Out");

        AssetDatabase.Refresh();
        string output = string.Format("{0}/{1}", Application.streamingAssetsPath, GetOS());
        BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.DeterministicAssetBundle, EditorUserBuildSettings.activeBuildTarget);
        Directory.Delete(Application.dataPath + "/temp/", true);
#else
        for (int i = 0; i < dirs.Count; i++)
        {
            string str = dirs[i].Remove(0, sourceDir.Length);
            BuildLuaBundle(str.Replace('\\', '/'), "Assets/StreamingAssets/Lua/Out");
        }

        BuildLuaBundle(null, "Assets/StreamingAssets/Lua/Out/");
        Directory.Delete(tempDir, true);
#endif
        AssetDatabase.Refresh();
    }

    [MenuItem("Lua/Clear All Lua Files", false, 57)]
    public static void ClearLuaFiles()
    {
        ClearAllLuaFiles();
    }


    [MenuItem("Lua/Generate Base Type Wrap", false, 101)]
    static void GenBaseTypeLuaWrap()
    {
        if (!beAutoGen && EditorApplication.isCompiling)
        {
            EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此功能", "确定");
            return;
        }

        string dir = CustomSettings.toluaBaseType;

        if (!File.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        allTypes.Clear();
        ToLuaExport.allTypes.AddRange(baseType);
        List<BindType> btList = new List<BindType>();

        for (int i = 0; i < baseType.Count; i++)
        {
            btList.Add(new BindType(baseType[i]));
        }

        GenBindTypes(btList.ToArray(), false);
        BindType[] list = allTypes.ToArray();

        for (int i = 0; i < list.Length; i++)
        {
            ToLuaExport.Clear();
            ToLuaExport.className = list[i].name;
            ToLuaExport.type = list[i].type;
            ToLuaExport.isStaticClass = list[i].IsStatic;
            ToLuaExport.baseType = list[i].baseType;
            ToLuaExport.wrapClassName = list[i].wrapName;
            ToLuaExport.libClassName = list[i].libName;
            ToLuaExport.Generate(dir);
        }

        Debug.Log("Generate base type wrap files over");
        allTypes.Clear();
        AssetDatabase.Refresh();
    }

    static void CreateDefaultWrapFile(string path, string name)
    {
        StringBuilder sb = new StringBuilder();
        path = path + name + ".cs";
        sb.Append("using System;\n");
        sb.Append("using LuaInterface;\n");
        sb.Append("\n");
        sb.AppendFormat("public static class {0}\n", name);
        sb.Append("{\n");
        sb.Append("\tpublic static void Register(LuaState L)\n");
        sb.Append("\t{\n");
        sb.Append("\t\tthrow new LuaException(\"Please click menu Lua/Generate Base Type Wrap first!\");\n");
        sb.Append("\t}\n");
        sb.Append("}\n");

        using (StreamWriter textWriter = new StreamWriter(path, false, Encoding.UTF8))
        {
            textWriter.Write(sb.ToString());
            textWriter.Flush();
            textWriter.Close();
        }
    }

    [MenuItem("Lua/Clear Base Type Wrap", false, 102)]
    static void ClearBaseTypeLuaWrap()
    {
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "System_ObjectWrap");
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "System_DelegateWrap");
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "System_StringWrap");
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "System_EnumWrap");
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "System_TypeWrap");
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "System_Collections_IEnumeratorWrap");
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "UnityEngine_ObjectWrap");
        CreateDefaultWrapFile(CustomSettings.toluaBaseType, "LuaInterface_EventObjectWrap");

        Debug.Log("Clear base type wrap files over");
        AssetDatabase.Refresh();
    }

    static bool TryUpdate(string srcPath, string destPath)
    {
        if (GetFileContentMD5(srcPath) != GetFileContentMD5(destPath))
        {
            File.Copy(srcPath, destPath, true);
            return true;
        }

        return false;
    }

    static string GetFileContentMD5(string file)
    {
        if (!File.Exists(file))
        {
            return string.Empty;
        }

        FileStream fs = new FileStream(file, FileMode.Open);
        System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
        byte[] retVal = md5.ComputeHash(fs);
        fs.Close();

        StringBuilder sb = StringBuilderCache.Acquire();
        for (int i = 0; i < retVal.Length; i++)
        {
            sb.Append(retVal[i].ToString("x2"));
        }
        return StringBuilderCache.GetStringAndRelease(sb);
    }
}
