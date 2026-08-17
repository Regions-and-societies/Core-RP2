using System;

namespace RegionsAndSocieties.Economy
{
    /// <summary>
    /// How a resource pool reads to the player. Pure string formatting — no <c>Find</c>, no Unity,
    /// no game types — which is the point: it lives here rather than inside the map mode so it can
    /// be compiled and tested in the sandbox, and so the inspect pane and the map mode cannot drift
    /// into describing the same pool two different ways.
    ///
    /// The four cases are deliberate. A province with no ceiling reads as "none" rather than
    /// "0 / 0", because a desert holds no timber and that is a fact about the terrain, not a
    /// shortage. A mined-out province names what it used to hold, because the player needs to be
    /// able to tell "there was never anything here" from "we took it all". And an untouched
    /// province shows a bare number with no percentage, so a fresh world does not present as a wall
    /// of "100%" that means nothing until something moves.
    /// </summary>
    public static class ResourceDisplay
    {
        public static string Line(ResourcePool pool, string label)
        {
            if (pool == null) return label + ": none";

            if (pool.cap <= 0f) return label + ": none";

            if (pool.IsExhausted)
            {
                return label + ": exhausted (was " + pool.cap.ToString("F0") + ")";
            }

            if (pool.Fraction >= 0.999f)
            {
                return label + ": " + pool.cap.ToString("F0");
            }

            return label + ": " + pool.current.ToString("F0")
                 + " / " + pool.cap.ToString("F0")
                 + " (" + pool.Fraction.ToString("P0") + ")";
        }
    }
}
