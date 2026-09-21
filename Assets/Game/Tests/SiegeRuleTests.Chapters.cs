using System.Collections.Generic;
using System.Text;
using UnityEngine;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The second chapter, and the two questions that are about the pair of chapters rather than
    /// about either one.
    ///
    /// <para>
    /// <b>A second part of the same class rather than a file of its own</b>, because it needs the
    /// harness the first part already has - <c>Rung</c>, <c>Layout</c>, <c>Hold</c>, <c>Aimed</c>
    /// and <c>Health</c> - and a second copy of a player model is a second player. It is a second
    /// <em>file</em> because <c>SiegeRuleTests.cs</c> is already two thousand lines and a chapter
    /// is not a reason to make it three.
    /// </para>
    /// </summary>
    public sealed partial class SiegeRuleTests
    {
        // ------------------------------------------------------------------ the second chapter
        /// <summary>
        /// Every rung of Broodmarch, held inline exactly as `Tools/chapters/s03_broodmarch.py`
        /// writes it.
        ///
        /// <b>Inline for <see cref="Chapter"/>'s reason</b>: a fixture that loads JSON goes through
        /// <c>JsonUtility</c>, which is a native call, so the offline runner reports the whole file
        /// as "needs the Editor" and it becomes the one gate nobody runs on the way past. What
        /// keeps a hand copy honest is <c>Tools/verify/rungs.py</c>, which holds these rows to
        /// the shipped body offline and names the line to change when they drift.
        /// </summary>
        static readonly Rung[] Broodmarch =
        {
            new Rung("s03_firstbrood", new[] { "grrgbybg", "brbygrgy", "gbyrbbyr", "rrggrrbr", "rggbbgrg" }, "rgby", "rgby", new[] { "rgbyRGby", "RGBYrgby", "RGBYRGby" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_hollowshell", new[] { "ybrgbgyr", "yrbrrbrb", "gbrbyybg", "ybgbrgry", "bygygybr" }, "rgby", "rgby", new[] { "rgbyRGby", "RGBY#rGby", "RGBY#gRGby" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_mirewalk", new[] { "gbyybrrb", "ybbrgbyy", "gygyrgrg", "bbybyyrb", "yygrrbyb" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY!rRGby", "RGBYRG!gBY!b" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_spinecrest", new[] { "gbygybbr", "rrggrrbr", "rgybyrgy", "ygbgrbyb", "brrgybyr" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGby#gRGby", "RGby#bRGby" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_blightfen", new[] { "bbgygbbg", "grbgryry", "rgyrygyr", "brgbbrgr", "yrgbyrby" }, "rgby", "rgby", new[] { "rgbyRGby", "RGbyRGby", "RGBY#rRGby" }, "blightcaller:b", 25, 0, 75, 92, "pl"),
            new Rung("s03_stillmire", new[] { "rggybgrr", "yrybbrby", "ggbrggyg", "rryygybr", "bgbrrgrb" }, "rgby", "rgby", new[] { "RRRrrrrg", "GGG#gGGgb", "BBBbbbbYYy" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_thornbrood", new[] { "rbyrgrbb", "rryybgyg", "yggyybrg", "yybrrgyr", "bbggbyyr" }, "rgby", "rgby", new[] { "RGbyrgby", "RGbyRGbyrg", "RGBY#yRGbyrg" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_gloamfield", new[] { "gygrybyb", "byrrbbrb", "brggygyy", "yybbyggb", "grbgrbby" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY!gRGby", "RGBY#bRGby" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_deepmire", new[] { "gygrbgbg", "yrybryyb", "gbgrgbyg", "ryybybbg", "bybbryry" }, "rgby", "rgby", new[] { "rgbyRGby", "RGbyRGby", "RGby#rRGby#gby", "RGBYrgby" }, "", 25, 0, 75, 92, "pl"),
            new Rung("s03_broodheart", new[] { "rrggrbgb", "ybgyryrg", "gybyggyy", "rgrbgrry", "rgrbybrr" }, "rgby", "rgby", new[] { "RGbyRGby", "RGby#rRGby", "RGBYRG#gby" }, "warbringer:g", 25, 0, 75, 92, "pl"),
        };

        // ------------------------------------------------------------------ the third chapter
        /// <summary>
        /// Every rung of Barrowfell, held inline exactly as `Tools/chapters/s04_barrowfell.py`
        /// writes it, and held to the shipped body by `Tools/verify/rungs.py`.
        ///
        /// See <see cref="Broodmarch"/> for why a hand copy exists and what keeps it honest.
        /// </summary>
        static readonly Rung[] Barrowfell =
        {
            new Rung("s04_firstbone", new[] { "byrrggyr", "ggbyrgby", "ybbgrbgb", "rygbybry", "bgyryrrb" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBYRGby", "RGBYRGbyrg" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_palerow", new[] { "ybryyggb", "gybgrbrg", "yggybrby", "ybrrbygy", "gbbrggrb" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY#rRGby", "RGBY#gRGbyrg" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_shieldwall", new[] { "yrgbygyr", "rgbgrbrg", "bbryrgby", "yggryrrg", "rgybbgyy" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGby#g#bRGby", "RGBY#y#rRGby" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_scytheway", new[] { "grbyrygb", "rgygrgby", "byyrbryb", "rrgbrbyy", "bbggrbgg" }, "rgby", "rgby", new[] { "RGBYRGBy", "RGBY#gRG!gby", "RGBY!bRG#yRGby" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_hollowgrave", new[] { "bgrryggy", "gyyrybrb", "rbygrgyg", "rrgrbgrb", "yygbyygr" }, "rgby", "rgby", new[] { "RGBYRGBy", "RGBY!rRG#bby", "RGBY#r#gRG!gby" }, "gravemaw:r", 25, 11, 67, 84, "pls"),
            new Rung("s04_boneyard", new[] { "yrrgbrgr", "bbyybrgy", "yggbrbyr", "ybyrgyry", "bgrybgrg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBYRGby", "RGBY#rRGbyrg" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_deadmarch", new[] { "gbbyygrg", "yrggbbyr", "ryyrbryg", "yggrggby", "rrybyrbb" }, "rgby", "rgby", new[] { "RGBYRGBy", "RGBYRGBY", "RGBY#rRGBy" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_lichgate", new[] { "bgybgryb", "rybbybrg", "rbyrgbyr", "ggrgrrgg", "gyybyryr" }, "rgby", "rgby", new[] { "RGby#rRGBY", "RGBY!gRGBy", "RGBY#b#yRGBy" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_longbarrow", new[] { "bgrbgbgy", "ybryrybg", "grgbrgyb", "gygrgbrr", "ryyryyrg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGbyRGby", "RGby#rRGby#gby", "RGBY!yRGBY" }, "", 25, 11, 67, 84, "pls"),
            new Rung("s04_barrowheart", new[] { "brgbgybr", "bybrrbgg", "rgyggybr", "rygbrryb", "bgyygbgy" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY#rRGby", "RGBYRG#gby" }, "bonecaller:b", 25, 11, 67, 84, "pls"),
        };

        // ------------------------------------------------------------------ the fourth chapter
        /// <summary>
        /// Every rung of Ashenhold, held inline exactly as `Tools/chapters/s05_ashenhold.py`
        /// writes it, and held to the shipped body by `Tools/verify/rungs.py`.
        ///
        /// See <see cref="Broodmarch"/> for why a hand copy exists and what keeps it honest.
        /// </summary>
        static readonly Rung[] Ashenhold =
        {
            new Rung("s05_firstiron", new[] { "ggyyrbry", "grrbgrbg", "yrgbbygr", "ygyyggry", "rygrybyg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBYRGby", "RGBY#rRGbyrg" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_shieldline", new[] { "rrbrggyb", "rgyyrbgb", "ggrbbyrg", "rrygygby", "ygyrrgyy" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g#bRGby", "RGbyRGbyrg" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_pikewall", new[] { "bygrgbgy", "ryybyryy", "grbggybg", "bryyrbrr", "gbbgygrg" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGby#g#b#yRGby", "RGByRGbyrg" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_emberrow", new[] { "brbybyby", "bryrbrbg", "yggbrgyr", "rgyryyrg", "rybygbby" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g!gRG#bby", "RGby!bRGByby" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_chainfall", new[] { "ybygygrr", "rgbrbbgr", "grgyrbyy", "ygrbggrr", "ybybggyy" }, "rgby", "rgby", new[] { "RGBYRGBy", "RGBY#rRG!bby", "RGby#rRGBy" }, "shackler:g", 25, 12, 52, 66, "plsf"),
            new Rung("s05_ironyard", new[] { "gybgyrbb", "grbrbgrr", "yyggrbgy", "grgbrygb", "bybrgyyg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY#rRGby", "RGby#gRGby", "RGBYRGbyrg" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_hollowvigil", new[] { "bgyryrgb", "rbrbgygr", "rgyggryb", "ggrbyrry", "bygrybgy" }, "rgby", "rgby", new[] { "RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#bbbYYY#yy" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_sunderway", new[] { "rbrgbrrb", "rgbgybyr", "gybrbrgg", "yrgrygyr", "gbgyybbg" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY!gRG#bby", "RGBY#bRGByby" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_longmarch", new[] { "ygygygbr", "rrbgyyby", "bybbrggb", "grrybryy", "bbyrgbyg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY#rRGby", "RGby#gRGby#bby", "RGBYRGby" }, "", 25, 12, 52, 66, "plsf"),
            new Rung("s05_ashenheart", new[] { "bgyyrrbb", "yrrgbyyr", "gygbbgrr", "gybggrbg", "rbbgybbg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY#rRGby", "RGBY#gRG#bby" }, "ironclad:b", 25, 12, 52, 66, "plsf"),
        };

        /// <summary>
        /// Thundercrag, the fifth chapter: the wild, a thunderer on the fifth rung and a colossus
        /// on the tenth, every rung dealing all five charms and every raider three tenths tougher
        /// than the baseline. Held to `s06_thundercrag.json` by `Tools/verify/rungs.py`.
        /// </summary>
        static readonly Rung[] Thundercrag =
        {
            new Rung("s06_firstcrag", new[] { "ggyybbrg", "yrgygybr", "rrbgrryb", "bgbbygby", "rgyygbrg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY#rRGby", "RGBY#g#bRGbyrg" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_mudslide", new[] { "rbyrbygr", "gyybgrby", "bggrrbgb", "rbryygry", "gbbyybby" }, "rgby", "rgby", new[] { "rgbyrgbyrgby", "RGby#rRGbyrg", "RGBY#g#bRGby" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_frostline", new[] { "ryrbbygb", "gyyggbrb", "rbryrgyy", "bgbyybrg", "rgrgbrby" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g#bRGby", "RGby#r#yRGbyrg" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_hollowpeak", new[] { "rbrggybr", "ygyygbry", "bggrbybg", "ryyrgyyr", "gbgybgrr" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g!gRG#bby", "RGby!b#rRGByby" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_thunderhead", new[] { "bryggyyg", "rgbygbbr", "yyrbyryg", "grgygbgb", "rbybrgyg" }, "rgby", "rgby", new[] { "RGBYRGby", "RGBY#rRG!bby", "RGby#gRGBy" }, "thunderer:y", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_glacierwall", new[] { "ryrgyrgg", "bbggbybg", "ryybryrb", "gbgrygrb", "grygbggr" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGby#g#b#yRGby", "RGBY!rRG#byrg" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_stoneward", new[] { "gbrrgbrr", "bggygybr", "yyrybrgb", "ybbggrry", "rrygbbyb" }, "rgby", "rgby", new[] { "RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#b#bbbYYY#yy" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_boulderrun", new[] { "rbbyybry", "yyggyryy", "rbryrbgg", "gbrbgygb", "ryybrrbr" }, "rgby", "rgby", new[] { "RGbyRGby", "RGBY#rRGby", "RGby#g#bRGby", "RGBY!yRGbyrg" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_highpass", new[] { "ybgygbgb", "yrgygyrb", "bybbryyg", "bbrgbgbr", "rgyrbggr" }, "rgby", "rgby", new[] { "rgbyrgby", "RGby#rrgby", "rgby#brgbyby", "RGbyrgby" }, "", 25, 13, 48, 62, "plsfh"),
            new Rung("s06_cragheart", new[] { "brbyyrbb", "gybgyggb", "yrggbrgr", "bgrbgrby", "bbyybgrg" }, "rgby", "rgby", new[] { "rgbyrgby", "RGby#rrgby", "RGbyrgby" }, "colossus:b", 25, 13, 48, 62, "plsfh"),
        };

        /// <summary>
        /// Dustcrown, the sixth chapter: the court, a gorgon on the fifth rung and a sunlord on
        /// the tenth, every rung dealing all six charms and every raider four tenths tougher than
        /// the baseline. Held to `s07_dustcrown.json` by `Tools/verify/rungs.py`.
        /// </summary>
        static readonly Rung[] Dustcrown =
        {
            new Rung("s07_firstdune", new[] { "yrybrbgb", "rggrgbrr", "rbbyrgyr", "gbggygyb", "grrgyrbb" }, "rgby", "rgby", new[] { "RGbyrgbyr", "RGby#rRGby", "RGBY#g#bRGbyr" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_bonefield", new[] { "ryggyrrb", "yyrrgybb", "gbbybygy", "bryggrbb", "gygbbrgy" }, "rgby", "rgby", new[] { "rgbyrgbyrgby", "RGby#rRGby", "RGBY#g#bRGbyr" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_saltpan", new[] { "yrrggygb", "bybgbrby", "gygrygrr", "gbbrygyg", "rgygrbyr" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g#bRGby", "RGby#r#yRGbyr" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_dryreach", new[] { "ybyrgbry", "bryrgbbr", "gbgyyrgg", "ggybrrgb", "bybrbybr" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g!gRG#bby", "RGby!b#rRGByby" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_gorgongate", new[] { "yygyyggr", "rbbrrbyr", "ygbygryg", "yryrgbgb", "bgybybgy" }, "rgby", "rgby", new[] { "RGbyRGby", "RGby#rRG!bby", "RGby#gRGby" }, "gorgon:g", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_sunscour", new[] { "ybygbbyr", "rrgrrggr", "yybgrgyy", "yrrbybrg", "rgybygrb" }, "rgby", "rgby", new[] { "RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#b#bbbYYY#y#yy" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_glassridge", new[] { "rybbrggb", "ggrygbry", "rrbgybby", "bryygrgr", "gbgbrryy" }, "rgby", "rgby", new[] { "RGbyRGby", "RGby#g#b#yrgby", "RGBY!rRG#by" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_duststorm", new[] { "gyybbggb", "bbrgrybr", "rgygbbgb", "ygryrgyg", "ybybrrby" }, "rgby", "rgby", new[] { "RGbyrgby", "RGBY#rRGby", "RGby#g#brgby", "RGBY!yRGby" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_thelongwalk", new[] { "rbyybybb", "ygyrgrrb", "grbgybgg", "brryyryg", "yygbbyrb" }, "rgby", "rgby", new[] { "rgbyrgby", "RGby#rrgby", "rgby#brgbyby", "RGBYrgbyr" }, "", 25, 14, 45, 59, "plsfha"),
            new Rung("s07_crownfall", new[] { "yygbbryb", "brrybgbr", "yyggrgyb", "bbyrybry", "rrgrybgb" }, "rgby", "rgby", new[] { "rgbyrgby", "rgby#rrgby", "RGbyrgby" }, "sunlord:y", 40, 14, 45, 59, "plsfha"),
        };

        /// <summary>
        /// Bonereach, the seventh chapter: the dead lands, a harrower on the fifth rung and a
        /// hollowking on the tenth, every rung dealing all six charms and every raider five
        /// tenths tougher than the baseline. Held to `s08_bonereach.json` by
        /// `Tools/verify/rungs.py`.
        /// </summary>
        static readonly Rung[] Bonereach =
        {
            new Rung("s08_firstreach", new[] { "rgrbygrr", "bgrbgrby", "gyygybgr", "bbgrbgyg", "rgyrbybg" }, "rgby", "rgby", new[] { "RGbyrgbyr", "RGby#rRGby", "RGBY#g#bRGbyr" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_bonespur", new[] { "bgyygrrg", "yrgbybbr", "grgrgbgg", "gbbyyrgb", "byrybryr" }, "rgby", "rgby", new[] { "rgbyrgbyrgby", "RGby#rRGby", "RGBY#g#bRGbyr" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_ropebridge", new[] { "ggbgrygy", "ryybgrbr", "brbrybyg", "gybgrryy", "bygyybgg" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g#bRGby", "RGby#r#yRGbyr" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_crystalrise", new[] { "bgbyygrg", "gbgrrbyb", "yrybyygg", "bgbgrbrr", "rrbrgbyb" }, "rgby", "rgby", new[] { "RGby#rRGby", "RGBY#g!gRG#bby", "RGby!b#rRGByby" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_harrowgate", new[] { "ryryrbrg", "bbryrgby", "gbgrgybb", "grygbyyg", "bgrgbrrg" }, "rgby", "rgby", new[] { "RGbyRGby", "RGby#rRG!bby", "RGby#gRGby" }, "harrower:r", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_thinair", new[] { "ygrrbyby", "gybgrgrr", "rbbygbyg", "ryyrgygy", "bgrbybrb" }, "rgby", "rgby", new[] { "RRRR#rrrrg", "GGGG#g#ggggb", "BBBB#b#bbbYYY#y#yy" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_shatterstep", new[] { "rbbryryr", "gbgrybyb", "grrgbrry", "bybryggb", "yybbyrry" }, "rgby", "rgby", new[] { "RGbyRGby", "RGby#g#b#yrgby", "RGBY!rRG#by" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_deadfall", new[] { "gbgrrgbr", "brygygyg", "rbgbrbry", "gyrbgrgb", "bgyrbrgb" }, "rgby", "rgby", new[] { "RGbyrgby", "RGBY#rRGby", "RGby#g#brgby", "RGBY!yRGby" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_thelastspan", new[] { "gbrbrbyy", "rgyyrggy", "bbggbgyr", "rrygybrb", "rybrrygy" }, "rgby", "rgby", new[] { "rgbyrgby", "RGby#rrgby", "rgby#brgbyby", "RGBYrgbyr" }, "", 25, 15, 42, 56, "plsfha"),
            new Rung("s08_hollowcrown", new[] { "ggyrbyby", "ygbgbrrg", "byrygyry", "gbgyybbr", "gbbrrggr" }, "rgby", "rgby", new[] { "rgbyrgby", "rgby#rrgby", "RGbyrgby" }, "hollowking:b", 40, 15, 42, 56, "plsfha"),
        };

        // ------------------------------------------------------------------ the lines it plays
        /// <summary>
        /// The four turrets a player who has bought nothing stands: the free bolt, four times.
        /// </summary>
        static WardLine Bare() => WardLine.Starter(WardCatalog.Default);

        /// <summary>
        /// The four turrets a player who has bought <em>one rung</em> of the shelf stands.
        ///
        /// <para>
        /// <b>The first credit rung on every colour, and nothing above it.</b> It is the cheapest
        /// four-turret line that exists at all, and it is what a player meets first whether or not
        /// they have to (the sequential unlock is gone, invariant 42c, so this is now the line
        /// somebody buys rather than the only one they are offered) - and at the roster's own
        /// prices it is about twice
        /// what a chapter of three-star clears pays, which is the honest reading of "a little
        /// higher turrets": a player who spent their first chapter's earnings on the line rather
        /// than on the grove.
        /// </para>
        /// <para>
        /// <b>Named rather than indexed</b>, so a re-rung shelf (which has happened twice) fails
        /// this loudly instead of quietly measuring a different turret.
        /// </para>
        /// </summary>
        const string FirstRung = "siphon";

        /// <summary>
        /// The line a chapter past the second is measured on: four <c>ember</c>, one star.
        ///
        /// <para>
        /// <b>It exists because the rule these gates were written against was replaced.</b> Every
        /// one of them asked whether a chapter could be held on the line a player <em>arrives</em>
        /// with, and called a rung that could not a wall. On 2026-09-20 the owner judged the
        /// opposite: the free bolt used to clear the sixth chapter at 40 of 90, and a shelf nobody
        /// has to shop at is a shelf nobody pays for. So a chapter past the second is measured on
        /// a line somebody bought, and the starter reading is kept for the share the shelf
        /// recovers rather than as a floor.
        /// </para>
        /// <para>
        /// <b>Reachable rather than strong, which is the whole of why it is this turret.</b>
        /// <c>mortar</c> and <c>breaker</c> read better and are gated at keeper 16 and 26 against
        /// content that pays for about keeper 13, so a gate built on either would assert that a
        /// chapter is clearable with a turret nobody can buy. <c>ember</c> is keeper 6 and 2,400
        /// credits, which is one chapter of three-starred clears.
        /// </para>
        /// </summary>
        const string Workhorse = "ember";

        static WardLine Bought() => Standing(Workhorse);


        static WardLine Kitted() => Standing(FirstRung);

        /// <summary>Four of one turret, whichever rung of the shelf it is.</summary>
        static WardLine Standing(string id)
        {
            var catalog = WardCatalog.Default;
            Assert.IsNotNull(catalog.Find(id),
                             $"'{id}' is not on the roster any more, so this test is "
                             + "measuring the starter twice");

            var chosen = new List<WardSlot>();
            for (int i = 0; i < WardLine.Colours.Length; i++)
                chosen.Add(new WardSlot(WardLine.Colours[i], id));

            var line = WardLine.Resolve(catalog, chosen, (model, colour) => true);

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(id, line.At(i).Id,
                                "the kitted line did not resolve to what it asked for");

            return line;
        }

        /// <summary>
        /// Four <b>different</b> turrets, one per colour, each at one star.
        ///
        /// <para>
        /// <b>The line a real player stands, and the one thing <see cref="Standing"/> cannot
        /// measure.</b> Every sweep in this file until now played four of one turret, which is
        /// what makes the reading easy to attribute - a chapter's difficulty against exactly one
        /// ability. It is also a line nobody builds: turrets are bought <em>per colour</em>
        /// (invariant 42c), so what a player who has spent a chapter's earnings actually owns is
        /// a handful of different machines on different seats, and the colour lock (37bl) means
        /// each of them answers a different quarter of the hill.
        /// </para>
        /// <para>
        /// <b>It is not a stronger line and it is not a weaker one - it is a differently shaped
        /// one</b>, and that is the reading: a mixed line has one good answer to each colour
        /// rather than one answer repeated four times, so a chapter that leans on a single
        /// material (plate, say) reads harder here and a chapter that spreads its threats reads
        /// easier. Nothing about it is a number this file may tune against; it is drawn so an
        /// author can see the shape of the curve a player really meets.
        /// </para>
        /// <para>
        /// <b>Named rather than indexed</b>, for <see cref="Standing"/>'s reason: a re-rung shelf
        /// has happened twice, and this fails loudly rather than quietly measuring four copies
        /// of something else.
        /// </para>
        /// </summary>
        static WardLine Mixed(params string[] ids)
        {
            Assert.AreEqual(WardLine.Colours.Length, ids.Length,
                            "a mixed line needs one turret per colour");

            var catalog = WardCatalog.Default;
            var chosen = new List<WardSlot>();

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                Assert.IsNotNull(catalog.Find(ids[i]),
                                 $"'{ids[i]}' is not on the roster any more, so this test is "
                                 + "measuring the starter instead");

                chosen.Add(new WardSlot(WardLine.Colours[i], ids[i]));
            }

            var line = WardLine.Resolve(catalog, chosen, (model, colour) => true);

            for (int i = 0; i < WardLine.Colours.Length; i++)
                Assert.AreEqual(ids[i], line.At(i).Id,
                                "the mixed line did not resolve to what it asked for");

            return line;
        }

        // ------------------------------------------------------------------ the sweep
        /// <summary>What a whole chapter did, at every rhythm, on one line.</summary>
        struct Sweep
        {
            public int Held;            // runs finished with two wards or more standing
            public int Starred;         // runs finished inside the three-star line
            public int Silvered;        // ... inside the two-star line
            public int Bronzed;         // ... and outside it
            public int Runs;            // rhythms times rungs
            public int Walled;          // rungs held at no rhythm at all
            public string Table;
        }

        /// <summary>
        /// Play a whole chapter at every rhythm on one line, and report what happened.
        ///
        /// <b>Nine rhythms rather than one, for invariant 37aq's reason</b>: a match changes the
        /// field, the field decides the next match, and two rhythms twenty milliseconds apart play
        /// out completely differently - so one sample of this is a coin toss and a green tick was
        /// once a coin landing the right way up.
        /// </summary>
        /// <summary>
        /// Every authored siege chapter, in the order a player meets them.
        ///
        /// <b>One list rather than a literal at each call site</b>, because the two structural
        /// gates below both walk it and a fourth chapter added to one and not the other is a
        /// chapter nothing checked - which is the shape of fault this file exists to catch.
        /// </summary>
        /// <b>A property rather than a static field</b>, and that is not style. These tables are
        /// spread across two files of one partial class, and the order static field initialisers
        /// run in across the parts of a partial type is the compiler's business - so a `readonly`
        /// array built from them read `Chapter` as **null** and both gates below failed with a
        /// null reference rather than with anything about a chapter. Evaluated on use, there is no
        /// order to get wrong.
        static (string Name, Rung[] Rungs)[] Ladder => new[]
        {
            ("Thornwatch", Chapter),
            ("Broodmarch", Broodmarch),
            ("Barrowfell", Barrowfell),
            ("Ashenhold", Ashenhold),
            ("Thundercrag", Thundercrag),
            ("Dustcrown", Dustcrown),
            ("Bonereach", Bonereach),
        };

        static Sweep Play(Rung[] chapter, WardLine line)
        {
            float[] rhythms = { 2.20f, 2.25f, 2.30f, 2.35f, 2.40f, 2.45f, 2.50f, 2.55f, 2.60f };

            var swept = new Sweep { Runs = chapter.Length * rhythms.Length };
            var table = new StringBuilder();

            for (int i = 0; i < chapter.Length; i++)
            {
                var layout = chapter[i].Built();
                int par = SiegeTuning.Par(layout);

                // **The chapter's own lines, not the shared 1.20 / 1.40.** A siege authors its own
                // because its par overstates what a run really spends - see `siege.GOLD_FACTOR` -
                // and a sweep that graded against the default would report a ladder this mode does
                // not ship.
                int gold = (par * chapter[i].Gold + 99) / 100;
                int silver = (par * chapter[i].Silver + 99) / 100;

                int held = 0, starred = 0, touched = 0, silvered = 0, bronzed = 0;
                int worstLine = int.MaxValue, whole = 0;

                // **What a finished run really spent, as a percentage of par**, which is the one
                // reading that says whether the star ladder is doing any work. The grade is a
                // multiple of par (invariant 22), so a chapter where every clear lands under 120
                // is a chapter where three stars is what clearing is called.
                int leanest = int.MaxValue, fattest = 0;

                foreach (float rhythm in rhythms)
                {
                    var board = SiegeBoard.Build(layout, line);
                    int matches = Hold(board, rhythm, out int _);

                    whole = board.Wards.Count * SiegeTuning.WardHealth;
                    int standing = Health(board);

                    if (board.IsFinished && board.WardsStanding >= 2) held++;
                    if (board.IsFinished && matches <= gold) starred++;
                    else if (board.IsFinished && matches <= silver) silvered++;
                    else if (board.IsFinished) bronzed++;
                    if (standing < whole) touched++;
                    if (standing < worstLine) worstLine = standing;

                    if (!board.IsFinished) continue;

                    int spent = matches * 100 / Mathf.Max(1, par);
                    if (spent < leanest) leanest = spent;
                    if (spent > fattest) fattest = spent;
                }

                if (leanest == int.MaxValue) leanest = 0;

                swept.Held += held;
                swept.Starred += starred;
                swept.Silvered += silvered;
                swept.Bronzed += bronzed;

                // **A wall is asked again, finer, and only then believed.** `held` is a *rate* and
                // nine samples measure a rate perfectly well; `walled` is a *universal* — held at
                // no rhythm at all — and no nine samples can establish one. This mode is chaotic
                // at a finer scale than the sweep steps in (invariant 37aq): a match changes the
                // field and the field decides the next match, so two rhythms twenty milliseconds
                // apart play out completely differently. Measured, `s04_deadmarch` read 0 of these
                // nine and **10 of twenty-five** — the gate called a wall on a rung an ordinary
                // player holds two times in five. It was green by luck once and red by luck here,
                // and both are the same fault.
                //
                // Only a suspected wall pays for the second sweep, so the common path is unchanged.
                if (held == 0 && Walled(layout, line)) swept.Walled++;

                table.AppendLine($"  {chapter[i].Id,-18} par {par,3}  3* {gold,3}"
                                 + $"  held {held}/{rhythms.Length}"
                                 + $"  grades {starred}/{silvered}/{bronzed}"
                                 + $"  spent {leanest,3}-{fattest,3}% of par"
                                 + $"  worst line {worstLine,3} of {whole,3}"
                                 + $"  reached {touched}/{rhythms.Length}");
            }

            swept.Table = table.ToString();
            return swept;
        }

        /// <summary>
        /// Whether a rung really is held at <em>no</em> rhythm, asked at four times the resolution.
        ///
        /// <para>
        /// <b>A second, finer sweep rather than a finer one everywhere.</b> Widening
        /// <see cref="Play"/> itself would quadruple the cost of every gate in this file to sharpen
        /// one reading out of six — and the other five are rates, which nine samples already
        /// measure. This is asked only of a rung that read nought, which is a handful a run.
        /// </para>
        /// <para>
        /// <b>It answers the moment it finds one</b>, because that is the whole question: a rung
        /// held at one rhythm is not a wall, and how many more there are is <see cref="Sweep.Held"/>'s
        /// business rather than this one's.
        /// </para>
        /// </summary>
        static bool Walled(SiegeLayout layout, WardLine line)
        {
            // Between and around the nine, at a quarter of their spacing, so the rhythms it tries
            // are the ones `Play` steps over rather than the ones it already asked.
            for (float rhythm = 2.10f; rhythm <= 2.70f; rhythm += .0125f)
            {
                var board = SiegeBoard.Build(layout, line);
                Hold(board, rhythm, out int _);

                if (board.IsFinished && board.WardsStanding >= 2) return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ the gates
        /// <summary>
        /// Every rung of Broodmarch parses, has a legal opening swap, and asks for a line.
        ///
        /// The layout half of what <c>Tools/chapters/s03_broodmarch.py</c> proves, run here as
        /// well because the Python is a mirror and this is the rule that ships.
        /// </summary>
        [Test]
        public void EveryRungOfBroodmarchReads()
        {
            var faults = new List<string>();

            for (int i = 0; i < Broodmarch.Length; i++)
            {
                var rung = Broodmarch[i];
                var layout = rung.Built();

                if (layout.Fault != null)
                {
                    faults.Add($"{rung.Id}: {layout.Fault}");
                    continue;
                }

                if (SiegeTuning.Par(layout) < 1)
                    faults.Add($"{rung.Id}: par came out below one");

                // `AnySwap`, never `AnyMove` - the second answers "is this run still going", which
                // a fresh board is whatever its field looks like (invariant 37ad narrowed it for
                // exactly this reason). What a level has to be authored with is a field somebody
                // can actually touch.
                var board = SiegeBoard.Build(layout);
                if (!board.AnySwap())
                    faults.Add($"{rung.Id}: the field is dealt with no legal swap on it");
            }

            Assert.IsEmpty(faults, string.Join("\n", faults));
        }

        /// <summary>
        /// Each chapter sends <b>two</b> bosses, on its fifth rung and its tenth.
        ///
        /// <para>
        /// <b>A structural rule rather than a tuning one, and the reason is that a count is what a
        /// player feels.</b> The first chapter shipped four bosses across ten rungs - one every two
        /// or three - and the owner's call was that half a chapter being a boss rung leaves no rung
        /// that is remembered for anything else. Five and ten is the shape the genre uses: a
        /// midpoint and a finale.
        /// </para>
        /// <para>
        /// It is checked rather than trusted because it is exactly the sort of thing a content edit
        /// undoes without noticing - a boss token is one field on one rung, and both gates that
        /// read a chapter today are happy with a boss anywhere or nowhere.
        /// </para>
        /// </summary>
        [Test]
        public void EachChapterSendsTwoBossesOnTheFifthAndTheTenthRung()
        {
            var faults = new List<string>();

            foreach (var pair in Ladder)
            {
                var name = pair.Item1;
                var rungs = pair.Item2;

                Assert.AreEqual(10, rungs.Length, $"{name} is not ten rungs long any more");

                for (int i = 0; i < rungs.Length; i++)
                {
                    bool wanted = i == 4 || i == 9;
                    bool sends = rungs[i].Built().HasBoss;

                    if (sends == wanted) continue;

                    faults.Add(sends
                                   ? $"{name} rung {i + 1} ({rungs[i].Id}) sends a boss and should not"
                                   : $"{name} rung {i + 1} ({rungs[i].Id}) sends no boss and should");
                }
            }

            Assert.IsEmpty(faults, string.Join("\n", faults));
        }

        /// <summary>
        /// **The star ladder has more than one rung**, on every chapter.
        ///
        /// <para>
        /// <b>Invariant 5d asked of the grade.</b> This mode shipped three chapters where every run
        /// that was held was also three-starred — on both lines, on every rung — so two thirds of
        /// the ladder rejected nothing and "three stars" was simply what clearing was called. It
        /// was not a tuning slip: a siege's par is arithmetic rather than a search (37a) and it
        /// <em>overstates</em>, so the shared 1.20 line sat far above anything a real run spends.
        /// Measured across 270 swept runs, every clear came in between 49% and 100% of par.
        /// </para>
        /// <para>
        /// <b>So the bar is a share rather than a count.</b> A chapter where three stars is most of
        /// what happens is one whose gold factor is too loose, whatever the numbers in the body
        /// say — and the failure is completely silent, because a level with a generous grade
        /// validates, plays and pays exactly like one with a tight one.
        /// </para>
        /// </summary>
        [Test]
        public void TheStarLadderHasMoreThanOneRungOnEveryChapter()
        {
            // Three quarters, which is loose on purpose: what this refuses is a ladder that has
            // collapsed, not one that leans generous. Measured 2026-09-14 on the starter line:
            // Thornwatch, Broodmarch and Barrowfell land between 21% and 45%.
            const int MostlyGold = 75;

            var faults = new List<string>();

            foreach (var pair in Ladder)
            {
                var swept = Play(pair.Rungs, Bare());

                int cleared = swept.Starred + swept.Silvered + swept.Bronzed;
                if (cleared == 0)
                {
                    faults.Add($"{pair.Name} finished no run at all on the starter line, so there "
                               + "is no ladder here to measure");
                    continue;
                }

                int share = swept.Starred * 100 / cleared;
                if (share <= MostlyGold) continue;

                faults.Add($"{share}% of {pair.Name}'s clears take three stars "
                           + $"({swept.Starred} of {cleared}), so two of the three bands reject "
                           + "almost nothing - tighten that chapter's goldFactor "
                           + "(Tools/verify/siege.STAR_FACTORS)");
            }

            Assert.IsEmpty(faults, string.Join("\n", faults));
        }

        /// <summary>
        /// No boss verb is sent by any two chapters.
        ///
        /// <para>
        /// <b>Invariant 37z asked across a ladder rather than within one.</b> That entry was bought
        /// by two bosses separated by a run-time hue reading as one boss, and the rule it left is
        /// that a boss has to be a <em>fight</em>. A player meeting a smite in chapter one and a
        /// smite in chapter three has met one fight twice, however far apart they are - so the
        /// verbs are dealt one each across the thirty rungs: a warlord and an overlord, a
        /// blightcaller and a warbringer, and now a gravemaw and a bonecaller.
        /// </para>
        /// <para>
        /// <b>This is the rule that prices a chapter, which is worth saying out loud.</b> Two
        /// bosses a chapter against a fixed set of verbs means the mode supports exactly
        /// <em>verbs / 2</em> chapters and then needs more - so a fourth siege chapter is a code
        /// change and not only content, and knowing that before it is commissioned is the whole
        /// point of checking it here rather than trusting it.
        /// </para>
        /// </summary>
        [Test]
        public void NoBossVerbIsSentByAnyTwoChapters()
        {
            var seen = new Dictionary<SiegeSpell, string>();
            var faults = new List<string>();

            foreach (var pair in Ladder)
            {
                foreach (var rung in pair.Item2)
                {
                    var layout = rung.Built();
                    if (!layout.HasBoss) continue;

                    var craft = SiegeTuning.SpellOf(layout.BossKind);

                    if (seen.TryGetValue(craft, out string already))
                        faults.Add($"{rung.Id} sends a {craft} and so does {already}, so one fight "
                                   + "is sent twice across the two chapters");
                    else
                        seen[craft] = rung.Id;
                }
            }

            Assert.IsEmpty(faults, string.Join("\n", faults));
        }

        /// <summary>
        /// **The second chapter is hard on the starter turret and comfortable one rung up the
        /// shelf** — which is the brief it was commissioned against, measured rather than felt.
        ///
        /// <para>
        /// <b>This is the first gate in this mode that plays a <em>chosen</em> line.</b> Everything
        /// else here plays <c>SiegeBoard.Build(layout)</c>, which falls back to the starter - right
        /// for the first chapter, where the free bolt is the whole game, and useless for a question
        /// whose whole subject is whether turrets are the answer. <c>SiegeBoard.Build</c> has taken
        /// a line since the loadout shipped (invariant 42); nothing had asked it for one.
        /// </para>
        /// <para>
        /// <b>Four assertions, and each closes a different way of getting this wrong.</b>
        /// <list type="number">
        /// <item>No rung is <em>walled</em> on the starter - a rung an unhurried player holds at no
        /// rhythm at all with the line they arrive on is a wall rather than a reason to shop, and
        /// invariant 24's argument about the heart gate says the same thing about the moment a
        /// player is stopped.</item>
        /// <item>The starter is <em>strictly harder</em> here than on the first chapter. A second
        /// chapter that plays like the first did not get harder, whatever its par says.</item>
        /// <item>One rung of the shelf really answers it - the kitted line holds strictly more runs
        /// than the bare one. A shelf that changes nothing is invariant 5d's decoration on the one
        /// thing in this game a player pays for.</item>
        /// <item>And the kitted line clears it comfortably, so "doable" is a measurement and not a
        /// hope.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>The floors are a record of where the chapter stands, not targets</b> — the same
        /// bargain <c>AnUnhurriedPlayerHoldsThisLine</c> strikes. A change that moves them wants
        /// measuring and then moving deliberately; a change that drops them several points is a
        /// change that loses runs, whatever one rhythm says.
        /// </para>
        /// </summary>
        [Test]
        public void TheSecondChapterAsksForBetterTurrets()
        {
            // Where this chapter stands, measured. Set below what was read, because a sweep of
            // ninety is steady and not exact.
            // Measured 2026-09-13, with the four boss cadences halved (`SiegeTuning.BossCastEvery`):
            // 63 on the starter, 79 one rung up, against Thornwatch's 78 on the starter. It read
            // 66 and 81 at the old cadence. The worst rung on the starter is still the finale at
            // 1 of 9, and the shelf is worth sixteen runs across the chapter - which is the whole
            // story this chapter is meant to tell.
            //
            // **The warbringer is why the finale still reads 1 of 9 rather than 0.** Halving its
            // cadence and leaving its roar at 2 walled `s03_broodheart` at every rhythm, because a
            // roar lands on all four wards and its cadence is therefore multiplied by four; it
            // roars twice as often for half as much instead (`SiegeTuning.WarbringerCast`). This
            // gate is what caught that, and it caught it as a wall rather than as a floor.
            // Re-read off the sweep at `SiegeTuning.RefillSettlesPercent` 60 on
            // 2026-09-20, after the refill stopped dealing free chains (37el): 26 on the
            // starter and 47 one rung up, against Thornwatch's 46. Set under what was read,
            // because a sweep of ninety is steady and not exact.
            const int BareFloor = 20;       // the starter still finishes a good share of it
            const int KittedFloor = 40;     // and the first paid rung is the line it expects

            // **What the shelf recovers, as a share of what the starter loses - and it is a share
            // rather than a count because a count stopped being able to say this.** It was
            // "twelve runs more", measured when the starter held 63 of 90 and had 27 to recover.
            // The charms (`SiegeCharm`) moved the starter to 76, which leaves fourteen runs in
            // existence: a twelve-run gap is then arithmetically almost impossible whatever the
            // shelf is worth, so the old assertion had quietly become a test of the chapter's
            // baseline rather than of the shelf. A half of what is being lost is a sentence that
            // survives the baseline moving in either direction.
            const int Recovers = 25;        // per cent of the runs the starter loses, at least

            // And the other half, which is where the shelf's value now mostly shows: a bought line
            // does not merely *clear* more of this chapter, it clears it *well*. Runs held
            // saturate against a ceiling of ninety; three-stars do not.
            const int Grades = 4;           // three-starred runs the shelf is worth, at least

            var bare = Play(Broodmarch, Bare());
            var kitted = Play(Broodmarch, Kitted());
            var first = Play(Chapter, Bare());

            var faults = new List<string>();

            if (bare.Walled > 0)
                faults.Add($"{bare.Walled} rung(s) of Broodmarch are held at no rhythm at all on "
                           + "the starter line, which is a wall rather than a reason to buy a "
                           + "turret");

            if (bare.Held >= first.Held)
                faults.Add($"on the starter line Broodmarch held {bare.Held} of {bare.Runs} runs "
                           + $"against Thornwatch's {first.Held} of {first.Runs} - the second "
                           + "chapter is not harder than the first, which is what it is for");

            int losing = bare.Runs - bare.Held;
            int back = kitted.Held - bare.Held;

            if (losing <= 0 || back * 100 < losing * Recovers)
                faults.Add($"one rung of the shelf moved Broodmarch from {bare.Held} to "
                           + $"{kitted.Held} of {kitted.Runs} runs - {back} of the {losing} the "
                           + $"starter loses, against the {Recovers}% this chapter is authored to "
                           + "recover. A shelf that changes little is decoration on the one thing "
                           + "in this game a player pays for");

            if (kitted.Starred < bare.Starred + Grades)
                faults.Add($"one rung of the shelf moved Broodmarch from {bare.Starred} "
                           + $"three-starred runs to {kitted.Starred}, which is under the {Grades} "
                           + "it is authored to be worth - and a grade is where a purchase shows "
                           + "once the runs held are near the ceiling");

            if (bare.Held < BareFloor)
                faults.Add($"on the starter line Broodmarch held {bare.Held} of {bare.Runs} runs "
                           + $"against a floor of {BareFloor}, so it has become a wall rather than "
                           + "hard - re-measure before moving the floor");

            if (kitted.Held < KittedFloor)
                faults.Add($"one rung up the shelf Broodmarch held {kitted.Held} of "
                           + $"{kitted.Runs} runs against a floor of {KittedFloor}, so it is not "
                           + "doable with better turrets either - re-measure before moving the "
                           + "floor");

            if (kitted.Starred == 0)
                faults.Add("three stars was out of reach on every rung at every rhythm even one "
                           + "rung up the shelf, so nobody playing this way ever sees three");

            Assert.IsEmpty(faults,
                           string.Join("\n", faults)
                           + $"\n\nBroodmarch on the starter ({bare.Held}/{bare.Runs} held, "
                           + $"{bare.Starred} three-starred):\n" + bare.Table
                           + $"\nBroodmarch on {FirstRung} ({kitted.Held}/{kitted.Runs} held, "
                           + $"{kitted.Starred} three-starred):\n" + kitted.Table
                           + $"\nThornwatch on the starter for comparison "
                           + $"({first.Held}/{first.Runs} held):\n" + first.Table);
        }

        /// <summary>
        /// **The third chapter needs a line somebody bought**, which is the brief it was
        /// commissioned against and is measured rather than felt.
        ///
        /// <para>
        /// <b>The same shape as <see cref="TheSecondChapterAsksForBetterTurrets"/> with the bar
        /// moved, and the move is the point.</b> Chapter two asks whether the shelf answers a
        /// chapter <em>at all</em>: hard on the starter, comfortable one rung up. Chapter three is
        /// the first one authored on the assumption that a player has already used it — so the free
        /// bolt has to be genuinely losing runs and the first purchase has to be a large, obvious
        /// answer rather than a nudge.
        /// </para>
        /// <para>
        /// <b>Two lines rather than three, and that is a finding rather than a shortcut.</b> The
        /// obvious third line is "two rungs up the shelf" — and it is <em>weaker</em> here than one
        /// rung up, measured: 47 of 90 against 72. That is not a defect, it is invariant 37ax
        /// working as written. The shelf is ordered by <b>how much of the hill an ability
        /// reaches</b>, never by how hard a turret hits, and every turret fires the same primary
        /// bolt within a weight the roster authors per model (37bb) — <c>siphon</c> carries 1.1x
        /// and <c>beacon</c> carries the baseline with more toughness instead. So "further up the
        /// shelf" and "stronger against this hill" are two different orderings, and a gate that
        /// asserted the first was the second would be measuring a rule this project does not have.
        /// </para>
        /// <para>
        /// <b>Four assertions, and each closes a way of getting this wrong.</b>
        /// <list type="number">
        /// <item>No rung is <em>walled</em> on the starter. A rung an unhurried player holds at no
        /// rhythm at all with the line they arrive on is a wall rather than a reason to shop, which
        /// is invariant 24's argument about the heart gate said about the shelf.</item>
        /// <item>The starter is strictly harder here than on chapter two, or the chapter did not
        /// get harder whatever its par says.</item>
        /// <item>The first purchase is worth real runs — and a large number of them, because that
        /// is the sentence this chapter exists to say.</item>
        /// <item>And a bought line clears it comfortably and can reach three stars, so "doable if
        /// you have been shopping" is a measurement rather than a hope.</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>The floors are a record of where the chapter stands, not targets.</b> A change that
        /// moves them wants measuring and then moving deliberately; a change that drops them
        /// several points is a change that loses runs, whatever one rhythm says (invariant 37aq).
        /// </para>
        /// </summary>
        [Test]
        public void TheThirdChapterAsksForABoughtLine()
        {
            // Where this chapter stands, measured 2026-09-14, with its raiders carrying a tenth
            // more health than the two chapters before it (`SiegeTuning.ToughnessFor`). Set below
            // what was read, because a sweep of ninety is steady and not exact: **28 on the
            // starter and 57 one rung up**, against Broodmarch's 63 on the starter. The shelf is
            // worth twenty-nine runs across the chapter, which is the whole story this chapter is
            // meant to tell, and it was eighteen before the surge.
            //
            // **A tenth is a long way in this mode, which nothing had measured before.** The same
            // ten rungs read 54 on the starter unsurged, 28 at a tenth more health, and **5** at
            // three tenths — a wall, at every rhythm, on every rung. The line's damage is roughly
            // fixed and the hill walks at a fixed speed, so health buys time on the hill directly
            // and the line only survives fourteen blows: the lever is a cliff rather than a slope,
            // and a chapter step of one tenth is the whole of what it can take.
            const int AcceptedWalls = 1;  // rungs held at no rhythm on the workhorse, measured
            const int BoughtFloor = 50;     // comfortably clearable once the shelf has been used

            // **A share of what the starter loses rather than a count of runs**, for the reason
            // written out in `TheSecondChapterAsksForBetterTurrets`: a count is a test of the
            // chapter's baseline as much as of the shelf, and this chapter's baseline moved when
            // the charms shipped. The share asked for here is higher than chapter two's, because
            // that is what this chapter is: the first one authored on the assumption that the
            // shelf has already been used.
            // **Measured 45, and it did not move when the charms did** — which is the evidence
            // this shape is the right one. Before the charms this chapter read 28 on the starter
            // and 57 one rung up: 29 of the 62 it was losing, or 47%. After them it reads 46 and
            // 67: 21 of 44, or 48%. The baseline moved eighteen runs and the sentence about the
            // shelf did not move at all, where the old count of runs would have failed outright.
            const int Recovers = 45;        // per cent of the runs the starter loses
            const int Grades = 14;           // three-starred runs the shelf is worth

            var bare = Play(Barrowfell, Bare());
            var cheap = Play(Barrowfell, Standing(FirstRung));
            var bought = Play(Barrowfell, Bought());
            var before = Play(Broodmarch, Bare());

            var faults = new List<string>();

            if (bought.Walled > AcceptedWalls)
                faults.Add($"{bought.Walled} rung(s) of Barrowfell are held at no rhythm at all "
                           + $"on a '{Workhorse}' line, against the {AcceptedWalls} "
                           + "measured and accepted - a rung nobody can hold having "
                           + "shopped is a wall, and one the starter cannot hold is the "
                           + "shelf working");

            if (bare.Held >= before.Held)
                faults.Add($"on the starter line Barrowfell held {bare.Held} of {bare.Runs} runs "
                           + $"against Broodmarch's {before.Held} of {before.Runs} - the third "
                           + "chapter is not harder than the second, which is what it is for");

            int losing = bare.Runs - bare.Held;
            int back = bought.Held - bare.Held;

            if (losing <= 0 || back * 100 < losing * Recovers)
                faults.Add($"one rung of the shelf moved Barrowfell from {bare.Held} to "
                           + $"{bought.Held} of {bought.Runs} runs - {back} of the {losing} the "
                           + $"starter loses, against the {Recovers}% this chapter is authored to "
                           + "recover. A shelf that changes little is decoration on the one thing "
                           + "in this game a player pays for");

            if (bought.Starred < bare.Starred + Grades)
                faults.Add($"one rung of the shelf moved Barrowfell from {bare.Starred} "
                           + $"three-starred runs to {bought.Starred}, which is under the {Grades} "
                           + "it is authored to be worth");

            // **No floor under the starter any more**, and its absence is the rule change: this
            // chapter is not supposed to be clearable on the line a player arrives
            // with. What the starter reading is still for is the share above - a shelf
            // that recovers nothing is decoration on the one thing a player pays for.

            if (bought.Held < BoughtFloor)
                faults.Add($"one rung up the shelf Barrowfell held {bought.Held} of "
                           + $"{bought.Runs} runs against a floor of {BoughtFloor}, so it is not "
                           + "doable with a bought line either - re-measure before moving the "
                           + "floor");

            if (bought.Starred == 0)
                faults.Add("three stars was out of reach on every rung at every rhythm even one "
                           + "rung up the shelf, so nobody playing this way ever sees three");

            Assert.IsEmpty(faults,
                           string.Join("\n", faults)
                           + $"\n\nBarrowfell on the starter ({bare.Held}/{bare.Runs} held, "
                           + $"{bare.Starred} three-starred):\n" + bare.Table
                           + $"\nBarrowfell on {FirstRung} ({bought.Held}/{bought.Runs} held, "
                           + $"{bought.Starred} three-starred):\n" + bought.Table
                           + $"\nBroodmarch on the starter for comparison "
                           + $"({before.Held}/{before.Runs} held):\n" + before.Table);
        }

        /// <summary>
        /// **The fourth chapter is harder than the third and is still not a wall**, measured on
        /// the same three lines every chapter gate here plays.
        ///
        /// <para>
        /// <b>Two things make it harder and only one of them is visible in the file.</b> Its
        /// raiders carry <em>two</em> tenths more health than the baseline against Barrowfell's
        /// one (<c>SiegeTuning.ToughnessFor</c>), which invariant 37bz measured as the sharpest
        /// lever this mode has — the same ten rungs read 54 of 90 unsurged, 28 at one tenth and
        /// <b>5</b> at three. And its composition is heavier: armour from the first rung, three
        /// shields in a wave where Barrowfell's worst carried two, and bombers standing in it.
        /// </para>
        /// <para>
        /// <b>The assertions are Barrowfell's, one chapter along</b>, and the reason they are the
        /// same four is that they close the same four ways of getting this wrong: a rung nobody
        /// can hold on the line they arrive with, a chapter that did not actually get harder, a
        /// shelf that buys nothing, and a chapter nobody can clear even having shopped. What moves
        /// is the floors, which are a <em>record of where this chapter stands</em> rather than
        /// targets — a change that drops them several points is a change that loses runs, whatever
        /// one rhythm says (invariant 37aq).
        /// </para>
        /// <para>
        /// <b>Recovered share rather than a count of runs</b>, for invariant 37ch's reason: a
        /// count is a test of the chapter's own baseline as much as of the shelf, and this
        /// chapter's baseline is the lowest in the mode.
        /// </para>
        /// </summary>
        [Test]
        public void TheFourthChapterIsFoughtOnABoughtLine()
        {
            // Where this chapter stands, measured 2026-09-14 over 90 runs a line.
            //
            // **38 on the starter against Barrowfell's 47**, which is the whole of what "harder
            // than the chapter before it" means here, and no rung is walled.
            const int AcceptedWalls = 0;  // rungs held at no rhythm on the workhorse, measured
            const int BoughtFloor = 44;     // clearable once the shelf has been used

            // **A share of what the starter loses, and this chapter's is far under Barrowfell's
            // 45% — which is a finding rather than a slip.** `siphon` is the cheapest four-turret
            // line in the game and its ability drains; this chapter is built out of *armour*, and
            // a bulwark halves every bolt that is not its own colour. So the cheapest purchase is
            // close to the worst possible answer to it, and it still buys twelve runs in ninety.
            // Measured: 38 -> 50 held, 10 -> 17 three-starred.
            const int Recovers = 35;        // per cent of the runs the starter loses
            const int Grades = 10;           // three-starred runs the shelf is worth

            // **And the line that really answers this chapter, which is the point of measuring a
            // second one at all** (invariant 37bw: the shelf is ordered by *reach*, so "further
            // up" is not "stronger" — a chapter has to be told which purchase answers *it*).
            // `cleaver` carries `WardAbility.Rend`, and Rend is the one thing on the shelf that
            // ignores a bulwark's soak outright (`SiegeBoard.Through`). A chapter made of armour
            // that a shield-breaker did not answer would be a chapter whose difficulty is not
            // about the thing it is drawn as.
            const string Answers = "cleaver";

            var bare = Play(Ashenhold, Bare());
            var cheap = Play(Ashenhold, Standing(FirstRung));
            var bought = Play(Ashenhold, Bought());
            var answered = Play(Ashenhold, Standing(Answers));
            var before = Play(Barrowfell, Bare());

            var faults = new List<string>();

            if (bought.Walled > AcceptedWalls)
                faults.Add($"{bought.Walled} rung(s) of Ashenhold are held at no rhythm at all "
                           + $"on a '{Workhorse}' line, against the {AcceptedWalls} "
                           + "measured and accepted - a rung nobody can hold having "
                           + "shopped is a wall, and one the starter cannot hold is the "
                           + "shelf working");

            if (bare.Held >= before.Held)
                faults.Add($"on the starter line Ashenhold held {bare.Held} of {bare.Runs} runs "
                           + $"against Barrowfell's {before.Held} of {before.Runs} - the fourth "
                           + "chapter is not harder than the third, which is what it is for");

            int losing = bare.Runs - bare.Held;
            int back = bought.Held - bare.Held;

            if (losing <= 0 || back * 100 < losing * Recovers)
                faults.Add($"one rung of the shelf moved Ashenhold from {bare.Held} to "
                           + $"{bought.Held} of {bought.Runs} runs - {back} of the {losing} the "
                           + $"starter loses, against the {Recovers}% this chapter is authored to "
                           + "recover");

            if (bought.Starred < bare.Starred + Grades)
                faults.Add($"one rung of the shelf moved Ashenhold from {bare.Starred} "
                           + $"three-starred runs to {bought.Starred}, which is under the {Grades} "
                           + "it is authored to be worth");

            // **The assertion this chapter exists to make.** A shield-breaker has to beat a drain
            // against a hill built out of shields; if it does not, the armour is decoration and
            // the difficulty is coming from somewhere else entirely (invariant 5d, asked of a
            // chapter's own material rather than of a mechanic).
            if (answered.Held <= cheap.Held)
                faults.Add($"'{Answers}', which ignores a bulwark's soak, held {answered.Held} of "
                           + $"{answered.Runs} runs against '{FirstRung}'s {cheap.Held} - so a "
                           + "chapter built out of armour is not answered by the one ability that "
                           + "beats armour, and its difficulty is not what it is drawn as");

            // **No floor under the starter any more**, and its absence is the rule change: this
            // chapter is not supposed to be clearable on the line a player arrives
            // with. What the starter reading is still for is the share above - a shelf
            // that recovers nothing is decoration on the one thing a player pays for.

            if (bought.Held < BoughtFloor)
                faults.Add($"one rung up the shelf Ashenhold held {bought.Held} of "
                           + $"{bought.Runs} runs against a floor of {BoughtFloor}, so it is not "
                           + "doable with a bought line either - re-measure before moving the "
                           + "floor");

            if (bought.Starred == 0)
                faults.Add("three stars was out of reach on every rung at every rhythm even one "
                           + "rung up the shelf, so nobody playing this way ever sees three");

            Assert.IsEmpty(faults,
                           string.Join("\n", faults)
                           + $"\n\nAshenhold on the starter ({bare.Held}/{bare.Runs} held, "
                           + $"{bare.Starred} three-starred):\n" + bare.Table
                           + $"\nAshenhold on {FirstRung} ({bought.Held}/{bought.Runs} held, "
                           + $"{bought.Starred} three-starred):\n" + bought.Table
                           + $"\nAshenhold on {Answers} ({answered.Held}/{answered.Runs} held, "
                           + $"{answered.Starred} three-starred):\n" + answered.Table
                           + $"\nBarrowfell on the starter for comparison "
                           + $"({before.Held}/{before.Runs} held):\n" + before.Table);
        }

        /// <summary>
        /// **A shackler's chain is shorter than the gap between two of them**, which is the one
        /// number in this boss that is a rule rather than a taste.
        ///
        /// <para>
        /// <b>Invariant 5d, arriving as arithmetic.</b> A bind takes a ward out for
        /// <c>ShacklerBind</c> seconds and takes nothing else at all; if it could be re-thrown
        /// before the last one ran out, the chained colour would be off the hill for the whole
        /// fight and there would be no play that answers it — a fail state that rejects nothing.
        /// The gap between the two is the entire mechanic, so it is checked rather than left to a
        /// comment on the constant.
        /// </para>
        /// <para>
        /// <b>And the margin has to be worth something</b>, not merely positive: a ward that comes
        /// back a tenth of a second before it is chained again is a ward that never fires.
        /// </para>
        /// </summary>
        [Test]
        public void TheFifthChapterIsFoughtOnABoughtLine()
        {
            const int AcceptedWalls = 3;  // rungs held at no rhythm on the workhorse, measured
            const int BoughtFloor = 40;     // clearable once the shelf has been used
            const int Recovers = 25;        // per cent of the runs the starter loses
            const int Grades = 3;           // three-starred runs the shelf is worth
            const string Answers = "cleaver";

            var bare = Play(Thundercrag, Bare());
            var cheap = Play(Thundercrag, Standing(FirstRung));
            var bought = Play(Thundercrag, Bought());
            var answered = Play(Thundercrag, Standing(Answers));
            var before = Play(Ashenhold, Bare());

            var faults = new List<string>();

            if (bought.Walled > AcceptedWalls)
                faults.Add($"{bought.Walled} rung(s) of Thundercrag are held at no rhythm at all "
                           + $"on a '{Workhorse}' line, against the {AcceptedWalls} "
                           + "measured and accepted - a rung nobody can hold having "
                           + "shopped is a wall, and one the starter cannot hold is the "
                           + "shelf working");

            // **Harder than the fourth chapter, which is what the third step of the surge is
            // for** (invariant 37bz): a tenth is a cliff, and this chapter stands one further
            // down it than Ashenhold does.
            if (bare.Held >= before.Held)
                faults.Add($"on the starter line Thundercrag held {bare.Held} of {bare.Runs} runs "
                           + $"against Ashenhold's {before.Held} of {before.Runs} - the fifth "
                           + "chapter is not harder than the fourth, which is what it is for");

            int losing = bare.Runs - bare.Held;
            int back = bought.Held - bare.Held;

            if (losing <= 0 || back * 100 < losing * Recovers)
                faults.Add($"one rung of the shelf moved Thundercrag from {bare.Held} to "
                           + $"{bought.Held} of {bought.Runs} runs - {back} of the {losing} the "
                           + $"starter loses, against the {Recovers}% this chapter is authored to "
                           + "recover");

            if (bought.Starred < bare.Starred + Grades)
                faults.Add($"one rung of the shelf moved Thundercrag from {bare.Starred} "
                           + $"three-starred runs to {bought.Starred}, which is under the {Grades} "
                           + "it is authored to be worth");

            // A chapter built out of stone golems is answered by the ability that beats
            // armour, exactly as the fourth was (invariant 37da).
            if (answered.Held <= cheap.Held)
                faults.Add($"'{Answers}', which ignores a bulwark's soak, held {answered.Held} of "
                           + $"{answered.Runs} runs against '{FirstRung}'s {cheap.Held} - so a "
                           + "chapter built out of stone is not answered by the one ability that "
                           + "beats armour, and its difficulty is not what it is drawn as");

            // **No floor under the starter any more**, and its absence is the rule change: this
            // chapter is not supposed to be clearable on the line a player arrives
            // with. What the starter reading is still for is the share above - a shelf
            // that recovers nothing is decoration on the one thing a player pays for.

            if (bought.Held < BoughtFloor)
                faults.Add($"one rung up the shelf Thundercrag held {bought.Held} of "
                           + $"{bought.Runs} runs against a floor of {BoughtFloor}, so it is not "
                           + "doable with a bought line either - re-measure before moving the "
                           + "floor");

            if (bought.Starred == 0)
                faults.Add("three stars was out of reach on every rung at every rhythm even one "
                           + "rung up the shelf, so nobody playing this way ever sees three");

            Assert.IsEmpty(faults,
                           string.Join("\n", faults)
                           + $"\n\nThundercrag on the starter ({bare.Held}/{bare.Runs} held, "
                           + $"{bare.Starred} three-starred):\n" + bare.Table
                           + $"\nThundercrag on {FirstRung} ({bought.Held}/{bought.Runs} held, "
                           + $"{bought.Starred} three-starred):\n" + bought.Table
                           + $"\nThundercrag on {Answers} ({answered.Held}/{answered.Runs} held, "
                           + $"{answered.Starred} three-starred):\n" + answered.Table
                           + $"\nAshenhold on the starter for comparison "
                           + $"({before.Held}/{before.Runs} held):\n" + before.Table);

            System.Console.WriteLine($"Thundercrag on the starter ({bare.Held}/{bare.Runs} held, "
                                     + $"{bare.Starred} three-starred):\n" + bare.Table
                                     + $"\nThundercrag on {FirstRung} ({bought.Held}/{bought.Runs} "
                                     + $"held, {bought.Starred} three-starred):\n" + bought.Table
                                     + $"\nThundercrag on {Answers} ({answered.Held}/{answered.Runs} "
                                     + $"held, {answered.Starred} three-starred):\n" + answered.Table);
        }

        /// <summary>
        /// The sixth chapter, measured the way the fourth and fifth were: harder than the one
        /// before it on the starter, clearable one rung up the shelf, and answered by the ability
        /// its material asks for.
        ///
        /// <para>
        /// <b>Every figure below is read off this chapter's own sweep</b> and none of them is
        /// carried over from Thundercrag's, which is invariant 37cb said about a gate rather than
        /// about a star line: a surge moves what a chapter holds, so a floor copied from the
        /// chapter before it is a floor measuring the wrong thing.
        /// </para>
        /// <para>
        /// <b>What it cannot see is either of the two new verbs.</b> The model player pours into
        /// whichever ward its rhythm sends it to and digs nothing, so a glare's real cost - the
        /// player *choosing* to feed elsewhere - and a seal's - dropping everything to fill one
        /// tube - are both invisible here. What this measures is the hill; what proves the bosses
        /// fight is <c>EveryShippedBossRungIsAFight</c>, and what proves the verbs are
        /// <c>SiegeRuleTests.Dustcrown</c>.
        /// </para>
        /// </summary>
        [Test]
        public void TheSixthChapterIsFoughtOnABoughtLine()
        {
            // **Every one of these is read off this chapter's own sweep**, taken on 2026-09-17
            // and printed by the WriteLine at the foot of this method: 40 of 90 held on the
            // starter against Thundercrag's 45, 59 one rung up the shelf, 83 on the ability its
            // material asks for, and seven and nine three-starred runs on the first two. The
            // floors sit clear of those rather than on them, because nine rhythms is a sample.
            const int AcceptedWalls = 2;  // rungs held at no rhythm on the workhorse, measured
            const int BoughtFloor = 45;     // clearable once the shelf has been used (59)
            const int Recovers = 35;        // per cent of the runs the starter loses
            const int Grades = 2;           // three-starred runs the shelf is worth
            const string Answers = "cleaver";

            var bare = Play(Dustcrown, Bare());
            var cheap = Play(Dustcrown, Standing(FirstRung));
            var bought = Play(Dustcrown, Bought());
            var answered = Play(Dustcrown, Standing(Answers));
            var before = Play(Thundercrag, Bare());

            var faults = new List<string>();

            if (bought.Walled > AcceptedWalls)
                faults.Add($"{bought.Walled} rung(s) of Dustcrown are held at no rhythm at all "
                           + $"on a '{Workhorse}' line, against the {AcceptedWalls} "
                           + "measured and accepted - a rung nobody can hold having "
                           + "shopped is a wall, and one the starter cannot hold is the "
                           + "shelf working");

            // **Harder than the fifth chapter, which is what the fourth step of the surge is
            // for** (invariant 37bz): a tenth is a cliff, and this chapter stands one further
            // down it than Thundercrag does.
            if (bare.Held >= before.Held)
                faults.Add($"on the starter line Dustcrown held {bare.Held} of {bare.Runs} runs "
                           + $"against Thundercrag's {before.Held} of {before.Runs} - the sixth "
                           + "chapter is not harder than the fifth, which is what it is for");

            int losing = bare.Runs - bare.Held;
            int back = bought.Held - bare.Held;

            if (losing <= 0 || back * 100 < losing * Recovers)
                faults.Add($"one rung of the shelf moved Dustcrown from {bare.Held} to "
                           + $"{bought.Held} of {bought.Runs} runs - {back} of the {losing} the "
                           + $"starter loses, against the {Recovers}% this chapter is authored to "
                           + "recover");

            if (bought.Starred < bare.Starred + Grades)
                faults.Add($"one rung of the shelf moved Dustcrown from {bare.Starred} "
                           + $"three-starred runs to {bought.Starred}, which is under the {Grades} "
                           + "it is authored to be worth");

            // A chapter built out of plate is answered by the ability that beats armour, exactly
            // as the fourth and fifth were (invariant 37da).
            if (answered.Held <= cheap.Held)
                faults.Add($"\'{Answers}\', which ignores a bulwark\'s soak, held {answered.Held} of "
                           + $"{answered.Runs} runs against \'{FirstRung}\'s {cheap.Held} - so a "
                           + "chapter built out of plate is not answered by the one ability that "
                           + "beats armour, and its difficulty is not what it is drawn as");

            // **No floor under the starter any more**, and its absence is the rule change: this
            // chapter is not supposed to be clearable on the line a player arrives
            // with. What the starter reading is still for is the share above - a shelf
            // that recovers nothing is decoration on the one thing a player pays for.

            if (bought.Held < BoughtFloor)
                faults.Add($"one rung up the shelf Dustcrown held {bought.Held} of "
                           + $"{bought.Runs} runs against a floor of {BoughtFloor}, so it is not "
                           + "doable with a bought line either - re-measure before moving the "
                           + "floor");

            if (bought.Starred == 0)
                faults.Add("three stars was out of reach on every rung at every rhythm even one "
                           + "rung up the shelf, so nobody playing this way ever sees three");

            System.Console.WriteLine($"Dustcrown on the starter ({bare.Held}/{bare.Runs} held, "
                                     + $"{bare.Starred} three-starred):\n" + bare.Table
                                     + $"\nDustcrown on {FirstRung} ({bought.Held}/{bought.Runs} "
                                     + $"held, {bought.Starred} three-starred):\n" + bought.Table
                                     + $"\nDustcrown on {Answers} ({answered.Held}/{answered.Runs} "
                                     + $"held, {answered.Starred} three-starred):\n" + answered.Table);

            Assert.IsEmpty(faults,
                           string.Join("\n", faults)
                           + $"\n\nDustcrown on the starter ({bare.Held}/{bare.Runs} held, "
                           + $"{bare.Starred} three-starred):\n" + bare.Table
                           + $"\nDustcrown on {FirstRung} ({bought.Held}/{bought.Runs} held, "
                           + $"{bought.Starred} three-starred):\n" + bought.Table
                           + $"\nDustcrown on {Answers} ({answered.Held}/{answered.Runs} held, "
                           + $"{answered.Starred} three-starred):\n" + answered.Table
                           + $"\nThundercrag on the starter for comparison "
                           + $"({before.Held}/{before.Runs} held):\n" + before.Table);
        }

        /// <summary>
        /// The seventh chapter, measured the way the fourth, fifth and sixth were: harder than
        /// the one before it on the starter, clearable one rung up the shelf, and answered by
        /// the ability its material asks for.
        ///
        /// <para>
        /// <b>Every figure below is read off this chapter's own sweep</b> and none of them is
        /// carried over from Dustcrown's, which is invariant 37cb said about a gate rather than
        /// about a star line: a surge moves what a chapter holds, so a floor copied from the
        /// chapter before it is a floor measuring the wrong thing.
        /// </para>
        /// <para>
        /// <b>It draws a fourth line nothing else in this file draws</b> — four <em>different</em>
        /// turrets, one per colour, each at one star (<see cref="Mixed"/>). Every sweep here
        /// plays four of one turret, which is what makes a reading attributable to one ability
        /// and is a line nobody builds: turrets are bought per colour (invariant 42c), so a
        /// player arriving at the seventh chapter owns a handful of different machines. It is
        /// **printed and not gated**, deliberately — it is drawn so an author can see the shape
        /// of the curve a real player meets, and a floor on it would be this file tuning against
        /// a line whose strength is four separate purchases.
        /// </para>
        /// <para>
        /// <b>What it cannot see is either of the two new verbs.</b> The model player pours into
        /// whichever ward its rhythm sends it to and reaches for nothing on the ground, so a
        /// harrow's real cost — the beat spent taking the cog back — and a wane's — having kept
        /// all four posts working *before* it lands — are both invisible here. What this
        /// measures is the hill; what proves the bosses fight is
        /// <c>EveryShippedBossRungIsAFight</c>, and what proves the verbs is
        /// <c>SiegeRuleTests.Bonereach</c>.
        /// </para>
        /// </summary>
        [Test]
        public void TheSeventhChapterIsFoughtOnABoughtLine()
        {
            // **Two of these are set and two are deliberately nought, and the difference is
            // whether the figure needed a measurement.**
            //
            // `Recovers` and `Grades` are *shares and steps* rather than counts, which is
            // invariant 37ch's whole point: a share keeps saying the same thing while the
            // baseline moves, so the fourth, fifth and sixth chapters all carry these two
            // unchanged and a seventh has no reason to differ. They are live.
            //
            // **`BareFloor` and `BoughtFloor` are absolute counts and this chapter has not been
            // swept yet**, so they are nought - and a check that cannot fail is not a check
            // (CLAUDE.md), which is why it is written here rather than left to be discovered.
            // **Read them off the first run of this fixture and set them**, the way every
            // chapter from the fourth on was: the WriteLine at the foot prints the two numbers
            // to use, and the floors go a clear margin under what was measured because nine
            // rhythms is a sample. Until then what holds this chapter up is everything else in
            // this method, and none of it needed a number: no rung walled at any rhythm, harder
            // on the starter than Dustcrown, one rung of the shelf recovering a fifth of what
            // the starter loses and paying a grade, and `cleaver` beating `siphon` on a chapter
            // built out of plate.
            const int AcceptedWalls = 2;  // rungs held at no rhythm on the workhorse, measured
            const int BoughtFloor = 0;      // UNSET - read it off the sweep below
            const int Recovers = 30;        // per cent of the runs the starter loses
            const int Grades = 2;           // three-starred runs the shelf is worth
            const string Answers = "cleaver";

            var bare = Play(Bonereach, Bare());
            var cheap = Play(Bonereach, Standing(FirstRung));
            var bought = Play(Bonereach, Bought());
            var answered = Play(Bonereach, Standing(Answers));
            var spread = Play(Bonereach, Mixed("siphon", "ember", "rime", "cleaver"));
            var before = Play(Dustcrown, Bare());

            var faults = new List<string>();

            if (bought.Walled > AcceptedWalls)
                faults.Add($"{bought.Walled} rung(s) of Bonereach are held at no rhythm at all "
                           + $"on a '{Workhorse}' line, against the {AcceptedWalls} "
                           + "measured and accepted - a rung nobody can hold having "
                           + "shopped is a wall, and one the starter cannot hold is the "
                           + "shelf working");

            // **Harder than the sixth chapter, which is what the fifth step of the surge is
            // for** (invariant 37bz): a tenth is a cliff, and this chapter stands one further
            // down it than Dustcrown does.
            if (bare.Held >= before.Held)
                faults.Add($"on the starter line Bonereach held {bare.Held} of {bare.Runs} runs "
                           + $"against Dustcrown's {before.Held} of {before.Runs} - the seventh "
                           + "chapter is not harder than the sixth, which is what it is for");

            int losing = bare.Runs - bare.Held;
            int back = bought.Held - bare.Held;

            if (losing <= 0 || back * 100 < losing * Recovers)
                faults.Add($"one rung of the shelf moved Bonereach from {bare.Held} to "
                           + $"{bought.Held} of {bought.Runs} runs - {back} of the {losing} the "
                           + $"starter loses, against the {Recovers}% this chapter is authored "
                           + "to recover");

            if (bought.Starred < bare.Starred + Grades)
                faults.Add($"one rung of the shelf moved Bonereach from {bare.Starred} "
                           + $"three-starred runs to {bought.Starred}, which is under the "
                           + $"{Grades} it is authored to be worth");

            // A chapter built out of plate is answered by the ability that beats armour, exactly
            // as the fourth, fifth and sixth were (invariant 37da).
            if (answered.Held <= cheap.Held)
                faults.Add($"\'{Answers}\', which ignores a bulwark\'s soak, held "
                           + $"{answered.Held} of {answered.Runs} runs against "
                           + $"\'{FirstRung}\'s {cheap.Held} - so a chapter built out of "
                           + "plate is not answered by the one ability that beats armour, and "
                           + "its difficulty is not what it is drawn as");

            // **No floor under the starter any more**, and its absence is the rule change: this
            // chapter is not supposed to be clearable on the line a player arrives
            // with. What the starter reading is still for is the share above - a shelf
            // that recovers nothing is decoration on the one thing a player pays for.

            if (bought.Held < BoughtFloor)
                faults.Add($"one rung up the shelf Bonereach held {bought.Held} of "
                           + $"{bought.Runs} runs against a floor of {BoughtFloor}, so it is "
                           + "not doable with a bought line either - re-measure before moving "
                           + "the floor");

            if (bought.Starred == 0)
                faults.Add("three stars was out of reach on every rung at every rhythm even one "
                           + "rung up the shelf, so nobody playing this way ever sees three");

            string report =
                $"Bonereach on the starter ({bare.Held}/{bare.Runs} held, "
                + $"{bare.Starred} three-starred):\n" + bare.Table
                + $"\nBonereach on {FirstRung} ({bought.Held}/{bought.Runs} held, "
                + $"{bought.Starred} three-starred):\n" + bought.Table
                + $"\nBonereach on {Answers} ({answered.Held}/{answered.Runs} held, "
                + $"{answered.Starred} three-starred):\n" + answered.Table
                + $"\nBonereach on four different one-star turrets - siphon, ember, rime, "
                + $"cleaver ({spread.Held}/{spread.Runs} held, {spread.Starred} three-starred, "
                + $"{spread.Silvered} two-starred, {spread.Bronzed} one-starred):\n"
                + spread.Table
                + $"\nDustcrown on the starter for comparison ({before.Held}/{before.Runs} "
                + $"held):\n" + before.Table;

            System.Console.WriteLine(report);

            Assert.IsEmpty(faults, string.Join("\n", faults) + "\n\n" + report);
        }

        [Test]
        public void AShacklersChainAlwaysRunsOutBeforeTheNextOne()
        {
            Assert.Less(SiegeTuning.ShacklerBind, SiegeTuning.ShacklerCastEvery,
                        "a shackler would re-chain a ward before its last chain expired, so that "
                        + "colour would be off the hill for the whole fight");

            float free = SiegeTuning.ShacklerCastEvery - SiegeTuning.ShacklerBind;

            Assert.GreaterOrEqual(free, SiegeTuning.FireEvery,
                                  "a chained ward comes back with less than one bolt's worth of "
                                  + "time before the next arrow, so the seconds it is sold back "
                                  + "buy nothing");
        }

        /// <summary>
        /// **An ironclad is answered by one ward and every other boss by the whole line.**
        ///
        /// <para>
        /// <b>The aegis is not a number anywhere — it is this predicate</b>
        /// (<c>SiegeTuning.EveryWardReaches</c>), so the only way to state it is to ask it. A
        /// change that made bosses uniform again would delete the fourth chapter's finale without
        /// touching a single file that mentions it, and every other gate would stay green: the
        /// level parses, par is unmoved, and the fight simply stops being one.
        /// </para>
        /// </summary>
        [Test]
        public void EveryWardReachesEveryBossAtFullWeightWhateverItWears()
        {
            // A boss wears no colour (37dn): the ironclad's aegis no longer locks three wards
            // out, and no ward is doubled against any boss - every one lands the full bolt.
            var line = Bare();

            for (int c = 0; c < WardLine.Colours.Length; c++)
            {
                var ward = new SiegeWard(c, line.At(c));

                for (int wears = 0; wears < WardLine.Colours.Length; wears++)
                {
                    var clad = new SiegeRaider(0, wears, SiegeKind.Ironclad, 0, 0f);
                    var over = new SiegeRaider(1, wears, SiegeKind.Overlord, 0, 0f);
                    var creeper = new SiegeRaider(2, wears, SiegeKind.Creeper, 0, 0f);

                    Assert.AreEqual(SiegeTuning.BossReachTenths, ward.ReachTenths(clad),
                                    $"a {WardLine.Colours[c]} ward is not at full weight against an "
                                    + $"ironclad wearing {WardLine.Colours[wears]}");
                    Assert.AreEqual(SiegeTuning.BossReachTenths, ward.ReachTenths(over));
                    Assert.IsTrue(ward.Doubles(over), "a boss is drawn as a full hit from every ward");

                    // And an ordinary raider still keeps the lock (37bl).
                    Assert.AreEqual(c == wears ? 10 : 0, ward.ReachTenths(creeper));
                }
            }
        }
    }
}
