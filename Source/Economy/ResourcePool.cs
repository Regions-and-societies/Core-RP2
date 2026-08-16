using System;

namespace RegionsAndSocieties.Economy
{
    /// <summary>
    /// One resource's stock in one province: what the terrain can hold, and what is actually there.
    ///
    /// Deliberately not <c>IExposable</c>. Keeping this pure is what lets the whole economy be
    /// tested without game assemblies; <c>GeographicProvince</c> scribes the two floats itself,
    /// reusing its existing save keys for <see cref="cap"/> so old saves load without a migration.
    /// </summary>
    public class ResourcePool
    {
        /// <summary>
        /// The ceiling the land imposes, from biome, hilliness, and tile count. This is the number
        /// <c>InitializeProvinceEconomics</c> has always computed — pre-0.7 it was the stock itself.
        /// </summary>
        public float cap;

        /// <summary>
        /// What is left. Negative means "never initialised", which is how a 0.6 save is recognised
        /// and seeded — see <see cref="EnsureSeeded"/>. Zero is a real, reachable state: exhausted.
        /// </summary>
        public float current = Unseeded;

        public const float Unseeded = -1f;

        public ResourcePool() { }

        public ResourcePool(float cap)
        {
            this.cap = cap;
            this.current = cap;
        }

        /// <summary>
        /// Bring a pool loaded from an older save into the new model. A province that only ever
        /// stored one number was storing a full stock, so a full stock is what it gets back.
        ///
        /// Idempotent, and it will not resurrect a province that has genuinely been mined out —
        /// exhausted reads as 0, not as unseeded.
        /// </summary>
        public void EnsureSeeded()
        {
            if (current < 0f) current = cap;
            if (cap < 0f) cap = 0f;
            if (current > cap) current = cap;
        }

        /// <summary>How full this pool is, 0 to 1. An empty cap reads as full — nothing is missing.</summary>
        public float Fraction
        {
            get
            {
                if (cap <= 0f) return 1f;
                float f = current / cap;
                return f < 0f ? 0f : (f > 1f ? 1f : f);
            }
        }

        public bool IsExhausted { get { return cap > 0f && current <= 0f; } }

        /// <summary>
        /// Take up to <paramref name="amount"/>, returning what was actually available. Callers
        /// must use the return value: asking for more than the province has is the normal case for
        /// an over-extended economy, and silently granting it would make depletion decorative.
        /// </summary>
        public float Draw(float amount)
        {
            if (amount <= 0f) return 0f;
            float taken = amount < current ? amount : current;
            if (taken < 0f) taken = 0f;
            current -= taken;
            return taken;
        }

        /// <summary>Put stock back, never past the cap. Returns the amount that actually landed.</summary>
        public float Grow(float amount)
        {
            if (amount <= 0f) return 0f;
            float room = cap - current;
            if (room <= 0f) return 0f;
            float added = amount < room ? amount : room;
            current += added;
            return added;
        }

        /// <summary>
        /// Move the cap when the terrain's assessment changes — a province gaining or losing tiles.
        /// The stock is clamped down but never scaled up: finding more room does not fill it.
        /// </summary>
        public void SetCap(float newCap)
        {
            cap = newCap < 0f ? 0f : newCap;
            if (current > cap) current = cap;
        }

        public override string ToString()
        {
            return current.ToString("0") + "/" + cap.ToString("0");
        }
    }
}
