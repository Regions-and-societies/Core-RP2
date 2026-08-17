using System;
using System.Collections.Generic;

namespace RegionsAndSocieties.Integration
{
    public enum TypeMatch
    {
        /// <summary>Type.FullName of the object or any base type equals Value.</summary>
        ExactType,

        /// <summary>Type.FullName of the object or any base type starts with Value (namespace prefix).</summary>
        NamespacePrefix,

        /// <summary>Type.Name of the object or any base type contains Value.</summary>
        TypeNameContains,

        /// <summary>WorldObjectDef.defName contains Value.</summary>
        DefNameContains
    }

    public class WorldObjectTypeRule
    {
        public readonly TypeMatch match;
        public readonly string value;
        public readonly WorldObjectKind kind;

        public WorldObjectTypeRule(TypeMatch match, string value, WorldObjectKind kind)
        {
            this.match = match;
            this.value = value;
            this.kind = kind;
        }
    }

    /// <summary>
    /// Declarative description of how one mod exposes its world objects.
    ///
    /// Supporting "another mod that acts like Empire Refactored" is meant to be a new profile
    /// in <see cref="KnownModProfiles"/>, not new code. Only genuinely bespoke behaviour should
    /// need a hand-written <see cref="WorldObjectAdapterBase"/> subclass.
    /// </summary>
    public class WorldObjectAdapterProfile
    {
        /// <summary>Stable id, also the settings key.</summary>
        public string adapterId;

        public string displayName;

        public int priority = 150;

        /// <summary>
        /// Types whose presence proves the mod is loaded. If none of them resolve, the adapter
        /// stays inactive and costs nothing.
        /// </summary>
        public string[] markerTypes = new string[0];

        /// <summary>
        /// The mod this profile describes, as it appears in ModsConfig.xml. Optional, and used for
        /// nothing at runtime — the adapter still activates on marker types alone, so a profile with
        /// no packageId behaves exactly as before.
        ///
        /// <para>It exists so a test can tell the two silent states apart. "No marker resolved"
        /// means the mod is absent, which is normal and correct; it also means the marker names are
        /// wrong, which is a defect that looks identical from inside. Three of the four profiles
        /// shipped with wrong markers before anything could distinguish them: Empire's population
        /// names were invented (#30), World Domination's namespace was guessed, and Vanilla Expanded
        /// Framework renamed its assembly from VFECore to VEF and this table never noticed (#31).</para>
        ///
        /// <para>With this set, a mod that is active while its markers resolve to nothing is a
        /// failure rather than a shrug.</para>
        /// </summary>
        public string packageId;

        /// <summary>Evaluated in order; first match wins.</summary>
        public List<WorldObjectTypeRule> typeRules = new List<WorldObjectTypeRule>();

        /// <summary>Candidate member names holding the object's population (int or a collection).</summary>
        public string[] populationMembers = new string[0];

        /// <summary>Candidate member names holding the mod's own upgrade level.</summary>
        public string[] levelMembers = new string[0];

        /// <summary>Candidate member names holding the mod's maximum upgrade level.</summary>
        public string[] maxLevelMembers = new string[0];

        /// <summary>
        /// Fallback when the mod exposes no max-level member. 0 means "unknown".
        ///
        /// A level with no maximum is a numerator with no denominator, so
        /// <c>SettlementSizeEvaluator.FromLevel</c> ignores it and the holding is tiered on headcount
        /// alone. That is deliberate: guessing a maximum would invent a progression the mod does not
        /// have. Profiles that leave this at 0 are declaring "we can read a level but not judge it" —
        /// filling it in is the fix, and it needs the mod loaded to do honestly.
        /// </summary>
        public int assumedMaxLevel;

        /// <summary>
        /// Set for mods whose world objects represent the player's own holdings even when the
        /// object is not owned by the player Faction (Empire Refactored).
        /// </summary>
        public bool playerOwnedByDefault;

        /// <summary>Player-facing toggle. Null means "always on when the mod is present".</summary>
        public Func<bool> enabledGetter;

        public WorldObjectAdapterProfile Rule(TypeMatch match, string value, WorldObjectKind kind)
        {
            typeRules.Add(new WorldObjectTypeRule(match, value, kind));
            return this;
        }
    }
}
