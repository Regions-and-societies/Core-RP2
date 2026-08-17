using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace RegionsAndSocieties.Integration
{
    /// <summary>
    /// Generic adapter driven entirely by a <see cref="WorldObjectAdapterProfile"/>.
    /// Resolves nothing at construction time, so an unloaded mod imposes no cost and throws nothing.
    /// </summary>
    public class ReflectionWorldObjectAdapter : WorldObjectAdapterBase
    {
        private readonly WorldObjectAdapterProfile profile;

        private bool markerResolved;
        private bool markerPresent;

        // Per-object-type classification cache. World object types are stable for the process
        // lifetime, so this collapses the rule walk to a dictionary hit after first sight.
        private readonly Dictionary<Type, WorldObjectKind> typeKindCache = new Dictionary<Type, WorldObjectKind>();

        public ReflectionWorldObjectAdapter(WorldObjectAdapterProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            this.profile = profile;
        }

        public WorldObjectAdapterProfile Profile
        {
            get { return profile; }
        }

        public override string AdapterId
        {
            get { return profile.adapterId; }
        }

        public override string DisplayName
        {
            get { return string.IsNullOrEmpty(profile.displayName) ? profile.adapterId : profile.displayName; }
        }

        public override int Priority
        {
            get { return profile.priority; }
        }

        public bool ModPresent
        {
            get
            {
                if (!markerResolved)
                {
                    markerResolved = true;
                    markerPresent = false;
                    if (profile.markerTypes != null)
                    {
                        for (int i = 0; i < profile.markerTypes.Length; i++)
                        {
                            string tn = profile.markerTypes[i];
                            if (string.IsNullOrEmpty(tn)) continue;
                            if (GenTypes.GetTypeInAnyAssembly(tn) != null)
                            {
                                markerPresent = true;
                                break;
                            }
                        }
                    }
                }
                return markerPresent;
            }
        }

        public override bool IsActive
        {
            get
            {
                if (!ModPresent) return false;
                if (profile.enabledGetter == null) return true;
                try
                {
                    return profile.enabledGetter();
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        public override bool IsPresent
        {
            get { return ModPresent; }
        }

        public override bool PlayerOwnedByDefault
        {
            get { return profile.playerOwnedByDefault; }
        }

        public override bool TryClassify(WorldObject obj, out WorldObjectKind kind)
        {
            kind = WorldObjectKind.Unknown;
            if (obj == null) return false;

            Type type = obj.GetType();

            WorldObjectKind cached;
            if (typeKindCache.TryGetValue(type, out cached))
            {
                if (cached != WorldObjectKind.Unknown)
                {
                    kind = cached;
                    return true;
                }
                // Cached miss on the type — a def-name rule may still match this instance.
                return TryDefNameRules(obj, out kind);
            }

            WorldObjectKind typeResult = EvaluateTypeRules(type);
            typeKindCache[type] = typeResult;

            if (typeResult != WorldObjectKind.Unknown)
            {
                kind = typeResult;
                return true;
            }

            return TryDefNameRules(obj, out kind);
        }

        /// <summary>
        /// What this profile's type rules make of <paramref name="type"/>, ignoring def-name rules
        /// and the cache.
        ///
        /// <para>Public so the suite can ask the question without an instance. Rules are not scoped
        /// to the declaring mod — the registry offers every world object to every active adapter and
        /// takes the first non-Unknown answer — so a rule can silently claim another mod's types
        /// (#33). Detecting that needs the real matcher rather than a copy of it in the test, for
        /// the same reason <c>SynapseLoadOrderCheck.FindViolations</c> is public: the check the game
        /// performs and the check the suite performs must not be able to drift apart.</para>
        /// </summary>
        public WorldObjectKind EvaluateTypeRules(Type type)
        {
            var rules = profile.typeRules;
            if (rules == null) return WorldObjectKind.Unknown;

            for (int i = 0; i < rules.Count; i++)
            {
                WorldObjectTypeRule rule = rules[i];
                if (rule == null || string.IsNullOrEmpty(rule.value)) continue;

                switch (rule.match)
                {
                    case TypeMatch.ExactType:
                        if (ReflectionHelper.InheritsFrom(type, rule.value)) return rule.kind;
                        break;

                    case TypeMatch.NamespacePrefix:
                        if (ReflectionHelper.InheritsFromNamespace(type, rule.value)) return rule.kind;
                        break;

                    case TypeMatch.TypeNameContains:
                        for (Type t = type; t != null; t = t.BaseType)
                        {
                            if (t.Name.IndexOf(rule.value, StringComparison.OrdinalIgnoreCase) >= 0) return rule.kind;
                        }
                        break;
                }
            }

            return WorldObjectKind.Unknown;
        }

        private bool TryDefNameRules(WorldObject obj, out WorldObjectKind kind)
        {
            kind = WorldObjectKind.Unknown;
            var rules = profile.typeRules;
            if (rules == null || obj.def == null || obj.def.defName == null) return false;

            for (int i = 0; i < rules.Count; i++)
            {
                WorldObjectTypeRule rule = rules[i];
                if (rule == null || rule.match != TypeMatch.DefNameContains || string.IsNullOrEmpty(rule.value)) continue;

                if (obj.def.defName.IndexOf(rule.value, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    kind = rule.kind;
                    return true;
                }
            }

            return false;
        }

        public override bool TryGetPopulation(WorldObject obj, out int population)
        {
            population = 0;
            if (obj == null || profile.populationMembers == null || profile.populationMembers.Length == 0) return false;
            return ReflectionHelper.TryGetInt(obj, profile.populationMembers, out population);
        }

        public override bool TryGetLevel(WorldObject obj, out int level, out int maxLevel)
        {
            level = 0;
            maxLevel = 0;
            if (obj == null || profile.levelMembers == null || profile.levelMembers.Length == 0) return false;
            if (!ReflectionHelper.TryGetInt(obj, profile.levelMembers, out level)) return false;

            if (profile.maxLevelMembers == null
                || profile.maxLevelMembers.Length == 0
                || !ReflectionHelper.TryGetInt(obj, profile.maxLevelMembers, out maxLevel))
            {
                maxLevel = profile.assumedMaxLevel;
            }

            return true;
        }
    }
}
