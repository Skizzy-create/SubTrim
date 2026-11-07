using System;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace SubTrim
{
    /// <summary>
    /// Typed accessors for fields and properties of classes that are only known by name at run time
    /// (other mods' PartModules). The accessors are compiled once with DynamicMethod, so calling them
    /// does not box or allocate, unlike FieldInfo.GetValue/SetValue. If Reflection.Emit is unavailable
    /// the accessors fall back to (allocating) reflection.
    ///
    /// Value types must match exactly; an enum member can be read or written as its underlying integer type.
    /// The owner type must be a class. Members are searched up the inheritance chain, including private ones.
    /// </summary>
    public static class FastAccess
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static Type FindType(string fullName)
        {
            for (int i = 0; i < AssemblyLoader.loadedAssemblies.Count; i++)
            {
                Assembly asm = AssemblyLoader.loadedAssemblies[i].assembly;
                if (asm == null) continue;
                Type t;
                try { t = asm.GetType(fullName, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Flags);
                if (f != null) return f;
            }
            return null;
        }

        static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, Flags);
                if (p != null) return p;
            }
            return null;
        }

        static bool Compatible(Type memberType, Type wanted)
        {
            if (memberType == wanted) return true;
            if (memberType.IsEnum && Enum.GetUnderlyingType(memberType) == wanted) return true;
            if (!wanted.IsValueType && !memberType.IsValueType && wanted.IsAssignableFrom(memberType)) return true;
            return false;
        }

        /// <summary>Returns null if the member does not exist or has an incompatible type.</summary>
        public static Func<object, T> Getter<T>(Type owner, string member)
        {
            if (owner == null || owner.IsValueType) return null;
            FieldInfo field = FindField(owner, member);
            if (field != null && Compatible(field.FieldType, typeof(T)))
            {
                try
                {
                    var dm = new DynamicMethod("SubTrim_get_" + owner.Name + "_" + member, typeof(T), new[] { typeof(object) }, typeof(FastAccess).Module, true);
                    ILGenerator il = dm.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, field.DeclaringType);
                    il.Emit(OpCodes.Ldfld, field);
                    il.Emit(OpCodes.Ret);
                    return (Func<object, T>)dm.CreateDelegate(typeof(Func<object, T>));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SubTrim] Emit failed for " + owner.Name + "." + member + ", using reflection: " + e.Message);
                    if (field.FieldType.IsEnum) return o => (T)Convert.ChangeType(field.GetValue(o), typeof(T));
                    return o => (T)field.GetValue(o);
                }
            }
            PropertyInfo prop = FindProperty(owner, member);
            MethodInfo get = prop != null ? prop.GetGetMethod(true) : null;
            if (get != null && Compatible(prop.PropertyType, typeof(T)))
            {
                try
                {
                    var dm = new DynamicMethod("SubTrim_pget_" + owner.Name + "_" + member, typeof(T), new[] { typeof(object) }, typeof(FastAccess).Module, true);
                    ILGenerator il = dm.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, get.DeclaringType);
                    il.Emit(get.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, get);
                    il.Emit(OpCodes.Ret);
                    return (Func<object, T>)dm.CreateDelegate(typeof(Func<object, T>));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SubTrim] Emit failed for " + owner.Name + "." + member + ", using reflection: " + e.Message);
                    if (prop.PropertyType.IsEnum) return o => (T)Convert.ChangeType(get.Invoke(o, null), typeof(T));
                    return o => (T)get.Invoke(o, null);
                }
            }
            return null;
        }

        /// <summary>Returns null if the field does not exist or has an incompatible type. Fields only.</summary>
        public static Action<object, T> Setter<T>(Type owner, string member)
        {
            if (owner == null || owner.IsValueType) return null;
            FieldInfo field = FindField(owner, member);
            if (field == null || field.IsInitOnly || field.IsLiteral || !Compatible(field.FieldType, typeof(T))) return null;
            try
            {
                var dm = new DynamicMethod("SubTrim_set_" + owner.Name + "_" + member, null, new[] { typeof(object), typeof(T) }, typeof(FastAccess).Module, true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, field.DeclaringType);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, field);
                il.Emit(OpCodes.Ret);
                return (Action<object, T>)dm.CreateDelegate(typeof(Action<object, T>));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SubTrim] Emit failed for " + owner.Name + "." + member + ", using reflection: " + e.Message);
                if (field.FieldType.IsEnum) return (o, v) => field.SetValue(o, Enum.ToObject(field.FieldType, v));
                return (o, v) => field.SetValue(o, v);
            }
        }
    }
}
