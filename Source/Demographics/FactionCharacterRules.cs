namespace RegionsAndSocieties.Demographics
{
    /// <summary>The broad character a faction's people have, beyond their raw tech level (#27).</summary>
    public enum FactionArchetype
    {
        Generic = 0,      // no strong character — neutral modifiers
        Outlander,        // settled productive townsfolk
        Tribe,            // oral-culture tribal peoples
        Raider,           // pirates/raiders — live off others, not off knowledge or production
        Imperial,         // stratified high-culture polity (the Empire)
        Merchant,         // wealthy trading society
        Scavenger,        // practical salvagers — technical but not schooled
        AncientElite,     // ancient, elite, long-lived remnants
        Cult,             // secretive, anti-rational cult
    }

    /// <summary>
    /// Faction-character modifiers to the demographic model (#27). Tech level alone made a pirate band
    /// read as an educated, prosperous populace — pirates run on <b>looted</b> industrial gear, they do
    /// not school or produce. This layer gives each base/DLC faction a <b>character</b> that skews
    /// knowledge (education) and wealth (socioeconomic tier) away from what its tech level alone implies.
    ///
    /// <para>Pure: defName + a couple of def flags in, two scalars out — no game types — so it is
    /// unit-tested without a game and the same table drives every faction. Base game + DLC factions are
    /// classified by defName; anything unknown (a modded or Vanilla-Factions-Expanded faction) falls back
    /// to a trait-based guess, which a compatibility patch can override with a better mapping. First-pass
    /// values, tunable.</para>
    /// </summary>
    public static class FactionCharacterRules
    {
        /// <summary>The two modifiers a character applies: a knowledge skew added to the education
        /// research-skew (−1 pulls attainment down, +1 up), and a multiplier on base wealth (&lt;1 poorer,
        /// &gt;1 richer) feeding the socioeconomic tier.</summary>
        public struct Character
        {
            public float knowledgeSkew;
            public float wealthMultiplier;
            public Character(float knowledgeSkew, float wealthMultiplier)
            {
                this.knowledgeSkew = knowledgeSkew;
                this.wealthMultiplier = wealthMultiplier;
            }
        }

        /// <summary>The modifiers for an archetype. Raiders and cults read down, traders and empires up.</summary>
        public static Character CharacterOf(FactionArchetype a)
        {
            switch (a)
            {
                case FactionArchetype.Outlander:    return new Character(+0.10f, 1.05f);
                case FactionArchetype.Tribe:        return new Character(-0.15f, 0.85f);
                case FactionArchetype.Raider:       return new Character(-0.60f, 0.65f);   // loot, don't school or produce
                case FactionArchetype.Imperial:     return new Character(+0.45f, 1.30f);   // educated, stratified, wealthy
                case FactionArchetype.Merchant:     return new Character(+0.20f, 1.45f);   // rich traders
                case FactionArchetype.Scavenger:    return new Character(-0.05f, 0.95f);   // practical, not academic
                case FactionArchetype.AncientElite: return new Character(+0.50f, 1.15f);   // ancient knowledge
                case FactionArchetype.Cult:         return new Character(-0.35f, 0.80f);   // anti-rational
                default:                            return new Character(0f, 1f);
            }
        }

        /// <summary>
        /// Classify a faction into its archetype. Known base-game and DLC factions are matched by defName;
        /// an unknown faction (modded / VFE) falls back to a trait guess: a permanent-enemy band of
        /// medieval-or-better tech reads as raiders, a neolithic-or-below faction as a tribe, everything
        /// else neutral. <paramref name="techLevel"/> is RimWorld's TechLevel ordinal (Animal=1 … Archotech=7).
        /// </summary>
        public static FactionArchetype Classify(string defName, int techLevel, bool permanentEnemy)
        {
            switch (defName)
            {
                // Raiders — Core + Ideology + Biotech pirate variants.
                case "Pirate":
                case "CannibalPirate":
                case "PirateWaster":
                case "PirateYttakin":
                    return FactionArchetype.Raider;

                // Settled outlander unions (Core + Biotech pig union).
                case "OutlanderCivil":
                case "OutlanderRough":
                case "OutlanderRoughPig":
                    return FactionArchetype.Outlander;

                // Tribes — Core + Ideology + Biotech variants.
                case "TribeCivil":
                case "TribeRough":
                case "TribeSavage":
                case "TribeCannibal":
                case "NudistTribe":
                case "TribeRoughNeanderthal":
                case "TribeSavageImpid":
                    return FactionArchetype.Tribe;

                case "Empire":        return FactionArchetype.Imperial;      // Royalty
                case "TradersGuild":  return FactionArchetype.Merchant;      // Odyssey
                case "Salvagers":     return FactionArchetype.Scavenger;     // Odyssey

                // Ancient, elite remnants (Core ancients + Biotech sanguophages).
                case "Ancients":
                case "AncientsHostile":
                case "Sanguophages":
                    return FactionArchetype.AncientElite;

                case "HoraxCult":     return FactionArchetype.Cult;          // Anomaly
            }

            // Unknown faction (modded / VFE): guess from traits; a CP can override with a real mapping.
            if (permanentEnemy && techLevel >= 3) return FactionArchetype.Raider;
            if (techLevel <= 2) return FactionArchetype.Tribe;
            return FactionArchetype.Generic;
        }
    }
}
