using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Small, allocation-light reflection helpers shared by the world-object adapters.
    /// Every lookup is cached per (type, memberName) so per-tick adapter calls stay cheap.
    /// </summary>
    public static class ReflectionHelper
    {
        private const BindingFlags InstanceAny =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private struct MemberKey : IEquatable<MemberKey>
        {
            public readonly Type type;
            public readonly string member;

            public MemberKey(Type type, string member)
            {
                this.type = type;
                this.member = member;
            }

            public bool Equals(MemberKey other)
            {
                return type == other.type && string.Equals(member, other.member, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is MemberKey && Equals((MemberKey)obj);
            }

            public override int GetHashCode()
            {
                int h = type != null ? type.GetHashCode() : 0;
                return (h * 397) ^ (member != null ? member.GetHashCode() : 0);
            }
        }

        // null value == "looked up, does not exist" (negative caching matters: misses are the common case)
        private static readonly Dictionary<MemberKey, MemberInfo> memberCache = new Dictionary<MemberKey, MemberInfo>();

        public static void ClearCaches()
        {
            memberCache.Clear();
        }

        private static MemberInfo ResolveMember(Type type, string name)
        {
            var key = new MemberKey(type, name);
            MemberInfo cached;
            if (memberCache.TryGetValue(key, out cached)) return cached;

            MemberInfo found = null;
            for (Type t = type; t != null && found == null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, InstanceAny);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    found = p;
                    break;
                }

                FieldInfo f = t.GetField(name, InstanceAny);
                if (f != null)
                {
                    found = f;
                    break;
                }
            }

            memberCache[key] = found;
            return found;
        }

        /// <summary>
        /// Reads the first member in <paramref name="names"/> that exists on <paramref name="instance"/>.
        /// Never throws — a reflection failure yields false.
        /// </summary>
        public static bool TryGetValue(object instance, string[] names, out object value)
        {
            value = null;
            if (instance == null || names == null) return false;

            Type type = instance.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name)) continue;

                MemberInfo m = ResolveMember(type, name);
                if (m == null) continue;

                try
                {
                    PropertyInfo p = m as PropertyInfo;
                    value = p != null ? p.GetValue(instance, null) : ((FieldInfo)m).GetValue(instance);
                }
                catch (Exception)
                {
                    // A property getter that throws (common when a mod object is half-initialised)
                    // must not take the whole classification pass down with it.
                    continue;
                }

                if (value != null) return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Reads an int-like member. Collections resolve to their element count, which lets one
        /// profile entry cover both <c>PawnCount</c> and a raw <c>occupants</c> list.
        /// </summary>
        public static bool TryGetInt(object instance, string[] names, out int result)
        {
            result = 0;
            object raw;
            if (!TryGetValue(instance, names, out raw)) return false;

            if (raw is int)
            {
                result = (int)raw;
                return true;
            }

            if (raw is float)
            {
                result = (int)(float)raw;
                return true;
            }

            if (raw is double)
            {
                result = (int)(double)raw;
                return true;
            }

            var collection = raw as ICollection;
            if (collection != null)
            {
                result = collection.Count;
                return true;
            }

            var enumerable = raw as IEnumerable;
            if (enumerable != null && !(raw is string))
            {
                int n = 0;
                foreach (object ignored in enumerable) n++;
                result = n;
                return true;
            }

            try
            {
                result = Convert.ToInt32(raw);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>True if <paramref name="type"/> or any of its base types is named <paramref name="fullName"/>.</summary>
        public static bool InheritsFrom(Type type, string fullName)
        {
            if (type == null || string.IsNullOrEmpty(fullName)) return false;
            for (Type t = type; t != null; t = t.BaseType)
            {
                if (string.Equals(t.FullName, fullName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>True if <paramref name="type"/> or any base type sits under the given namespace prefix.</summary>
        public static bool InheritsFromNamespace(Type type, string namespacePrefix)
        {
            if (type == null || string.IsNullOrEmpty(namespacePrefix)) return false;
            for (Type t = type; t != null; t = t.BaseType)
            {
                if (t.FullName != null && t.FullName.StartsWith(namespacePrefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
