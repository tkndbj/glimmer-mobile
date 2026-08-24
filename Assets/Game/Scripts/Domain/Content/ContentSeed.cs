namespace GlimmerGrove.Content
{
    /// <summary>
    /// The seed a level deals from: the one it authored, or one derived from its own id.
    ///
    /// <para>
    /// Written once because three modes want it and a fourth will. Deriving from the id means a
    /// level that authors nothing still deals the same opening to everybody for ever, a retry
    /// meets the board the player just played, and a bug is reproducible from the id alone —
    /// the daily chest's argument, in content.
    /// </para>
    /// </summary>
    public static class ContentSeed
    {
        public static uint For(int authored, LevelId id)
        {
            if (authored > 0) return (uint)authored;

            unchecked
            {
                uint hash = 2166136261u;
                string value = id.Value;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
                return hash == 0 ? 1u : hash;
            }
        }
    }
}
