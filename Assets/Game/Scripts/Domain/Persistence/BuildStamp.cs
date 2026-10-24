namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Which app builds this account has run (save v47): the oldest since this code shipped and the
    /// newest. For analytics to split players by build; no gameplay reads either.
    ///
    /// <para>
    /// <b>Two monotone readings, never a "current build"</b> (invariant 11b). A field holding "the
    /// build this device is on" would be overwritten by whichever device synced last, so two phones
    /// on two builds would trade it back and forth on every sync. The oldest only ever falls and the
    /// newest only ever rises, so each is a join with nothing to decide.
    /// </para>
    /// <para>
    /// <b>Nought is "absent"</b>: <c>Release.AppVersion.Running</c> answers nought for a version it
    /// cannot read, so no real build is nought, and <c>JsonUtility</c>'s own default means the same
    /// thing. A build above <see cref="MaxBuild"/> is not recorded - the rules bound both fields
    /// (12b), and a value they refuse would lose every save write.
    /// </para>
    /// </summary>
    public static class BuildStamp
    {
        /// <summary>
        /// The largest build either field may hold: 99.99.99. Matches the bound in
        /// <c>firestore.rules</c>, which <c>CloudWireTests</c> holds.
        /// </summary>
        public const int MaxBuild = 999_999;

        static int _first, _last;

        /// <summary>The oldest build recorded, or nought.</summary>
        public static int First => _first;

        /// <summary>The newest build recorded, or nought.</summary>
        public static int Last => _last;

        /// <summary>
        /// Records that this account is running <paramref name="build"/>. Called once a launch,
        /// after the save loads. A build of nought (unreadable) or above <see cref="MaxBuild"/>
        /// records nothing; a build already recorded changes nothing and writes nothing.
        /// </summary>
        public static void Stamp(int build)
        {
            if (!IsBuild(build)) return;

            int first = JoinFirst(_first, build);
            int last = JoinLast(_last, build);
            if (first == _first && last == _last) return;

            _first = first;
            _last = last;
            SaveService.MarkDirty();
        }

        /// <summary>The oldest of two: the lowest that is a build at all, nought when neither is.</summary>
        public static int JoinFirst(int a, int b)
        {
            bool hasA = IsBuild(a), hasB = IsBuild(b);
            if (hasA && hasB) return a < b ? a : b;
            return hasA ? a : hasB ? b : 0;
        }

        /// <summary>The newest of two: the highest that is a build at all, nought when neither is.</summary>
        public static int JoinLast(int a, int b)
        {
            int x = IsBuild(a) ? a : 0, y = IsBuild(b) ? b : 0;
            return x > y ? x : y;
        }

        /// <summary>Whether a figure can be a recorded build: above nought and within the rules' bound.</summary>
        public static bool IsBuild(int build) => build > 0 && build <= MaxBuild;

        // --------------------------------------------------- file bridge (internal)
        /// <summary>
        /// Takes the file's figures as they are. Not repaired here: a loader that rewrote a field
        /// would make every write owe a sync (11f) - an out-of-bound figure is dropped by the joins
        /// and by <see cref="WriteInto"/> instead.
        /// </summary>
        internal static void LoadFrom(SaveFileDto dto)
        {
            _first = dto?.firstBuild ?? 0;
            _last = dto?.lastBuild ?? 0;
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            if (dto == null) return;
            dto.firstBuild = IsBuild(_first) ? _first : 0;
            dto.lastBuild = IsBuild(_last) ? _last : 0;
        }
    }
}
