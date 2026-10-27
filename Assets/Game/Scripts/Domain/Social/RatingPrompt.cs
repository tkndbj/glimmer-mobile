using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Offers;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// When the store's own rating box may be asked for (invariant 68): the <c>ratingPrompt</c> block of
    /// <c>progression.json</c>, resolved.
    ///
    /// <para>
    /// <b>The moment is code, the thresholds are content</b>: a ladder win at <see cref="MinStars"/>,
    /// leaving the victory panel in the slot nothing else took (<c>WinOverlay.Leave</c>) - and only after
    /// <see cref="MinClearedLevels"/> levels, at least <see cref="GapDays"/> after the last ask, at most
    /// <see cref="MaxAsks"/> times an install, and never within <see cref="MoneyQuietMinutes"/> of a money
    /// popup. <b>Absent is the feature off</b>, and so is a block with a fault in it.
    /// </para>
    /// <para>
    /// <b>Nothing pays for a rating, and nothing could know one was left.</b> Both stores forbid an
    /// incentive, and neither API says whether the player rated - or even whether the box was drawn. A
    /// player who rated is never asked again because the stores themselves will not show it to them
    /// (Apple: once rated, and three times a year at most; Google Play: once reviewed, and on its own
    /// quota); this side only bounds how often it <em>asks</em>.
    /// </para>
    /// </summary>
    public sealed class RatingTable
    {
        /// <summary>The bounds a block is held to.</summary>
        public const int MaxMinLevels = 500, MaxGapDays = 365, MaxAsksEver = 10, MaxQuietMinutes = 24 * 60;

        public static readonly RatingTable Empty = new RatingTable();

        RatingTable() { }

        RatingTable(RatingPromptDto dto)
        {
            IsOn = true;
            MinClearedLevels = dto.minClearedLevels;
            MinStars = dto.minStars;
            GapDays = dto.gapDays;
            MaxAsks = dto.maxAsks;
            MoneyQuietMinutes = dto.moneyQuietMinutes;
        }

        /// <summary>Whether the file carried a block that resolved. False is the feature off.</summary>
        public bool IsOn { get; }

        /// <summary>Levels cleared before the box may be asked for.</summary>
        public int MinClearedLevels { get; }

        /// <summary>The stars the win must have taken.</summary>
        public int MinStars { get; }

        /// <summary>Days between two asks.</summary>
        public int GapDays { get; }

        /// <summary>Asks an install may ever make.</summary>
        public int MaxAsks { get; }

        /// <summary>Minutes a deals or offer popup keeps the box away.</summary>
        public int MoneyQuietMinutes { get; }

        /// <summary>
        /// Reads the optional block. Never throws and never returns null: anything wrong is named in
        /// <paramref name="problems"/> and the table resolves to <see cref="Empty"/>.
        /// </summary>
        public static RatingTable Resolve(RatingPromptDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;

            bool ok = true;
            void Range(string field, int value, int least, int most)
            {
                if (value >= least && value <= most) return;
                problems.Add($"ratingPrompt.{field} is {value}; it must be {least} to {most}");
                ok = false;
            }

            Range("minClearedLevels", dto.minClearedLevels, 1, MaxMinLevels);
            Range("minStars", dto.minStars, 1, 3);
            Range("gapDays", dto.gapDays, 1, MaxGapDays);
            Range("maxAsks", dto.maxAsks, 1, MaxAsksEver);
            Range("moneyQuietMinutes", dto.moneyQuietMinutes, 0, MaxQuietMinutes);

            return ok ? new RatingTable(dto) : Empty;
        }
    }

    /// <summary>What this install has asked: how many times, and when last. Device-local, never in the save.</summary>
    public readonly struct RatingHistory
    {
        public readonly int Asks;
        public readonly long LastAskUnix;

        public RatingHistory(int asks, long lastAskUnix)
        {
            Asks = asks < 0 ? 0 : asks;
            LastAskUnix = lastAskUnix < 0 ? 0 : lastAskUnix;
        }

        /// <summary>The history after one more ask at <paramref name="nowUnix"/>.</summary>
        public RatingHistory After(long nowUnix) => new RatingHistory(Asks + 1, Math.Max(LastAskUnix, nowUnix));

        /// <summary><c>asks|lastAskUnix</c>, hand-written so the fixtures read it offline.</summary>
        public string Encode() => Asks + "|" + LastAskUnix;

        /// <summary>The line back; anything unreadable is a history that has asked nothing.</summary>
        public static RatingHistory Decode(string line)
        {
            if (string.IsNullOrEmpty(line)) return default;
            var parts = line.Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int asks) || !long.TryParse(parts[1], out long last))
                return default;
            return new RatingHistory(asks, last);
        }
    }

    /// <summary>Why the box was or was not asked for. The spellings are analytics'.</summary>
    public enum RatingVerdict
    {
        Ask = 0,
        Off,
        TooFewLevels,
        TooFewStars,
        AskedEnough,
        TooSoon,
        AfterMoney,
    }

    /// <summary>The one decision, pure, so a fixture holds every rule.</summary>
    public static class RatingRules
    {
        public static RatingVerdict Judge(RatingTable table, RatingHistory history, int stars, int clearedLevels,
                                          long nowUnix, long lastMoneyPopupUnix)
        {
            if (table == null || !table.IsOn) return RatingVerdict.Off;
            if (clearedLevels < table.MinClearedLevels) return RatingVerdict.TooFewLevels;
            if (stars < table.MinStars) return RatingVerdict.TooFewStars;
            if (history.Asks >= table.MaxAsks) return RatingVerdict.AskedEnough;

            // A clock set backwards reads as "too soon", never as a licence to ask again.
            if (history.LastAskUnix > 0 && nowUnix - history.LastAskUnix < table.GapDays * 86400L)
                return RatingVerdict.TooSoon;

            if (lastMoneyPopupUnix > 0 && table.MoneyQuietMinutes > 0 &&
                nowUnix - lastMoneyPopupUnix < table.MoneyQuietMinutes * 60L)
                return RatingVerdict.AfterMoney;

            return RatingVerdict.Ask;
        }
    }

    /// <summary>
    /// This install's asks (invariant 68). <b>Per install, never per account and never in the save</b>: the
    /// stores pace their box per store account on the device, and an ask spent on one game account is spent
    /// for whoever holds the phone. <b>Marked when asked, before the box is requested</b> - a request the
    /// store then declines to draw still counts, so a store that keeps declining is never asked in a loop.
    /// </summary>
    public static class RatingLedger
    {
        const string Key = "glimmer.rating.asks";

        static EndlessCoins.ITallyStore _store = new PrefsStore();

        public static RatingTable Table => ProgressionRules.Table?.Rating ?? RatingTable.Empty;

        public static RatingHistory History
        {
            get
            {
                try { return RatingHistory.Decode(_store.Read(Key)); }
                catch (Exception e) { Debug.LogException(e); return default; }
            }
        }

        /// <summary>The verdict for a win at <paramref name="stars"/> now, on this install.</summary>
        public static RatingVerdict Judge(int stars)
            => RatingRules.Judge(Table, History, stars, PlayerProgress.ClearedCount, GameClock.NowUnix(),
                                 OfferLedger.LastPopupUnix);

        /// <summary>Spends one ask. Called once, immediately before the store's box is requested.</summary>
        public static void NoteAsked()
        {
            var next = History.After(GameClock.NowUnix());
            try { _store.Write(Key, next.Encode()); }
            catch (Exception e) { Debug.LogException(e); }
            Telemetry.Track("rating_prompt_asked", "ask", next.Asks, "cleared", PlayerProgress.ClearedCount);
        }

        sealed class PrefsStore : EndlessCoins.ITallyStore
        {
            public string Read(string key) => PlayerPrefs.GetString(key, string.Empty);
            public void Write(string key, string value) => DevicePrefs.WriteString(key, value);

            public void Delete(string key)
            {
                if (!PlayerPrefs.HasKey(key)) return;
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Installs a store; the game's own for null. For fixtures.</summary>
        internal static void UseStore(EndlessCoins.ITallyStore store) => _store = store ?? new PrefsStore();
    }
}
