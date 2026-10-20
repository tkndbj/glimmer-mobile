using System;
using GlimmerGrove.Localization;
using GlimmerGrove.Shuffle;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>SECRET UNLOCKED, or EVOLUTION</b> (MODES.md 59n, the owner, 2026-10-14: "show an
    /// animation on the screen ... under it write what it does ... an OKAY button ... these are
    /// rare and hard to make, so they genuinely should feel rewarding; legendary ones should
    /// truly feel super rare and amazing").
    ///
    /// <para>
    /// <b>Three movements, the reveal's own</b> (<see cref="WardRevealOverlay"/>): the room
    /// darkens and the cards that made it gather - each picture circling in toward the middle,
    /// trailing light - then the break, where they fuse into the emblem in a flash with the
    /// room thrown into colour, and then the words: the headline, the tier, the power's name,
    /// one plain sentence saying what it does, and the cards it was made of. <b>The tier sets
    /// the scale</b>: an epic breaks harder than a rare, and a legendary gets a third fan in
    /// gold, a rainbow round its emblem, three rounds of confetti, the room shaking, a rain of
    /// sparks for as long as it stands and its headline shimmering.
    /// </para>
    /// <para>
    /// <b>It holds the run</b> (a modal over the board, <c>RunHold.Covered</c>) and closes on
    /// OKAY and nothing else - not the scrim, and not the back key until the key is there - so
    /// a celebration is never tapped through by a finger still on its way off the hand.
    /// </para>
    /// </summary>
    public sealed class ShuffleUnlockOverlay : ModalView
    {
        public ShuffleBond Bond;

        /// <summary>The run's own cards (MODES.md 59l), for the pictures; the shipped ones when null.</summary>
        public Func<string, ShuffleCard> CardOf;

        /// <summary>Run once the panel has gone: the next unlock, or nothing.</summary>
        public Action OnDone;

        /// <summary>
        /// The build that unlocked it: an ascension gathers the school's cards it holds (MODES.md
        /// 59r). Null gathers the school's catalog cards instead.
        /// </summary>
        public ShuffleBuild Held;

        /// <summary>The most cards an ascension gathers round its emblem: its keystone and six of its school.</summary>
        const int MostGathered = 7;

        Layout.ShuffleUnlockLayout L => Layout.ShuffleUnlockLayout.Default;

        RectTransform _stage, _emblemRt, _fanRt, _fan2Rt, _fan3Rt, _okRt;
        Image _veil, _vignette, _fan, _fan2, _fan3, _glow, _halo, _emblem, _flash;
        Image[] _aurora;
        Text _title, _tier, _name, _note;
        RectTransform[] _parts, _recipe;
        Btn _ok;

        int _grand;
        Color _kind, _tint;
        bool _settled, _done, _ascension;
        float _age;

        protected override void Build()
        {
            if (Bond == null) { Close(); return; }

            // **An ascension breaks one step past a legendary** (MODES.md 59r): the rarest thing a
            // run can earn, so the room's every figure that climbs with the tier climbs once more.
            _ascension = Bond.IsAscension;
            _grand = _ascension ? 4 : Bond.Tier == ShuffleTier.Legendary ? 3 : Bond.Tier == ShuffleTier.Epic ? 2 : 1;
            _kind = ShuffleChoiceOverlay.GlowOf(Bond); // each secret's own colour, the evolutions' purple
            _tint = ShuffleChoiceOverlay.TintOf(Bond.Tier);

            BuildRoom();
            BuildEmblem();
            BuildWords();
            BuildKey();

            // Above everything, and last, so the break whites out the whole composition.
            _flash = UIKit.Img("Flash", Content, Art.Pixel, new Color(1f, .99f, .94f, 0f));
            UIKit.StretchTo((RectTransform)_flash.transform, 0, 0, 0, 0);
            _flash.raycastTarget = false;

            Play();
        }

        ShuffleCard Card(string id) => CardOf?.Invoke(id) ?? ShuffleCards.Find(id);

        // ------------------------------------------------------------------ the room
        void BuildRoom()
        {
            // Nothing closes it but the key: the scrim swallows every tap.
            Scrim = UIKit.Scrim(Content, .9f, null);

            _veil = UIKit.Img("Veil", Content, Art.Glow(256, .9f), Pal.A(Color.Lerp(_kind, Color.black, .82f), 0f),
                              new Vector2(1800f, 2600f), new Vector2(.5f, .5f), new Vector2(0f, L.EmblemY));
            _veil.raycastTarget = false;

            _aurora = new Image[3];
            var hues = new[] { _kind, _tint, Pal.Gold };
            for (int i = 0; i < _aurora.Length; i++)
            {
                _aurora[i] = UIKit.Img("Aurora", Content, Art.Glow(256, 1.7f), Pal.A(hues[i], 0f),
                                       Vector2.one * (1100f + i * 120f), new Vector2(.5f, .5f), AuroraHome[i]);
                _aurora[i].raycastTarget = false;
                Drift(_aurora[i], AuroraHome[i], i);
            }

            _fan = Rays("Fan", 10 + _grand * 4, _kind, 1300f, out _fanRt);
            _fan2 = Rays("Fan2", 6 + _grand * 2, _tint, 980f, out _fan2Rt);
            if (_grand >= 3) _fan3 = Rays("Fan3", 18, Pal.Gold, 1600f, out _fan3Rt);

            _glow = UIKit.Img("Glow", Content, Art.Glow(256, 1.6f), Pal.A(_kind, 0f),
                              Vector2.one * 900f, new Vector2(.5f, .5f), new Vector2(0f, L.EmblemY));
            _glow.raycastTarget = false;

            _vignette = UIKit.Img("Vignette", Content, Art.Vignette(256), new Color(0f, 0f, 0f, 0f));
            UIKit.StretchTo((RectTransform)_vignette.transform, 0, 0, 0, 0);
            _vignette.raycastTarget = false;

            // What `ModalView.Close` scales out: everything that is the panel rather than the room.
            _stage = UIKit.Box("Stage", Content, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);
            Panel = _stage;
        }

        static readonly Vector2[] AuroraHome = { new Vector2(-380f, 640f), new Vector2(420f, 120f), new Vector2(-260f, -560f) };

        Image Rays(string name, int count, Color tint, float size, out RectTransform rt)
        {
            var img = UIKit.Img(name, Content, Art.Rays(512, count), Pal.A(tint, 0f), Vector2.one * size,
                                new Vector2(.5f, .5f), new Vector2(0f, L.EmblemY));
            img.raycastTarget = false;
            rt = (RectTransform)img.transform;
            rt.localScale = Vector3.zero;
            return img;
        }

        static void Drift(Image blob, Vector2 home, int index)
        {
            float span = 50f + index * 14f, period = 13f + index * 4f;
            Tween.Run(period, Ease.Linear, t =>
            {
                if (!blob) return;
                float a = t * Mathf.PI * 2f;
                ((RectTransform)blob.transform).anchoredPosition = home + new Vector2(Mathf.Sin(a) * span, Mathf.Cos(a * 2f) * span * .6f);
            }, blob, "drift").Loop(-1, false);
        }

        // ------------------------------------------------------------------ the emblem
        void BuildEmblem()
        {
            var at = new Vector2(0f, L.EmblemY);

            _halo = UIKit.Img("Halo", _stage, Art.Glow(256, 2f), Pal.A(_kind, 0f), Vector2.one * L.EmblemSize * 2.4f,
                              new Vector2(.5f, .5f), at);
            _halo.raycastTarget = false;

            var mark = Art.S("Ui/" + Bond.Mark);
            _emblem = UIKit.Img("Emblem", _stage, mark, Color.white, Vector2.one * L.EmblemSize, new Vector2(.5f, .5f), at);
            _emblem.preserveAspect = true;
            _emblem.raycastTarget = false;
            _emblem.enabled = mark != null;
            _emblemRt = (RectTransform)_emblem.transform;
            _emblemRt.localScale = Vector3.zero;

            // The cards that made it, standing round the middle before they gather: every part of
            // a secret, the one card as many times as it was taken for an evolution, or an
            // ascension's keystone and the cards of its school the build holds.
            var gathered = Gathered();
            _parts = new RectTransform[gathered.Count];

            for (int i = 0; i < gathered.Count; i++)
            {
                var spot = at + Around(i, gathered.Count, L.Gather);
                _parts[i] = Picture("Part", _stage, gathered[i], L.PartSize, spot);
                _parts[i].localScale = Vector3.zero;
            }
        }

        /// <summary>The cards that stand round the emblem before they gather, in the order they stand.</summary>
        System.Collections.Generic.List<ShuffleCard> Gathered()
        {
            var cards = new System.Collections.Generic.List<ShuffleCard>(MostGathered);

            if (Bond.IsSecret)
            {
                for (int i = 0; i < Bond.Parts.Length; i++) cards.Add(Card(Bond.Parts[i]));
                return cards;
            }

            if (Bond.IsEvolution)
            {
                for (int i = 0; i < Bond.Needs; i++) cards.Add(Card(Bond.Parts[0]));
                return cards;
            }

            cards.Add(Card(Bond.Parts[0]));

            // The school's cards the build holds (each once), else the school's catalog cards.
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { Bond.Parts[0] };
            if (Held != null)
                foreach (var card in Held.Taken)
                    if (cards.Count < MostGathered && ShuffleSchools.Of(card) == Bond.School && seen.Add(card.Id))
                        cards.Add(card);

            foreach (var card in ShuffleCards.All)
                if (cards.Count < MostGathered && ShuffleSchools.Of(card) == Bond.School && seen.Add(card.Id))
                    cards.Add(Card(card.Id));

            return cards;
        }

        /// <summary>Where the <paramref name="index"/>th of <paramref name="count"/> things stands on a circle, the first at the top.</summary>
        static Vector2 Around(int index, int count, float radius)
        {
            float a = Mathf.PI * .5f + index * Mathf.PI * 2f / Mathf.Max(1, count);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        /// <summary>A card's picture in the kit's seat, lit from behind in its tier. Nothing in the seat when the picture has not arrived (7b).</summary>
        RectTransform Picture(string name, Transform parent, ShuffleCard card, float size, Vector2 at)
        {
            var seat = UIKit.Img(name, parent, Art.S("Ui/" + Skins.Slot), Color.white, Vector2.one * size,
                                 new Vector2(.5f, .5f), at);
            seat.type = Image.Type.Sliced;
            seat.raycastTarget = false;

            if (card != null)
            {
                UIKit.Halo(seat.transform, ShuffleChoiceOverlay.TintOf(card.Tier), size * 1.6f, .4f);

                var sprite = Art.S(card.Icon);
                var icon = UIKit.Img("Icon", seat.transform, sprite, Color.white, Vector2.one * size * .86f,
                                     new Vector2(.5f, .5f), Vector2.zero);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = sprite != null;
            }

            return (RectTransform)seat.transform;
        }

        // ------------------------------------------------------------------ the words
        void BuildWords()
        {
            string headline = Loc.Get(Bond.IsSecret ? "ui.shuffle.secret_unlocked"
                                      : Bond.IsEvolution ? "ui.shuffle.evolution_unlocked"
                                      : "ui.shuffle.ascension_unlocked").Upper();

            _title = UIKit.Shrinkable(
                UIKit.Titled("Title", _stage, headline, L.TitleSize, Color.Lerp(_kind, Color.white, .35f),
                             TextAnchor.MiddleCenter, new Vector2(L.Wide, L.TitleH), new Vector2(.5f, .5f),
                             new Vector2(0f, L.TitleY), outline: 6f, shadow: 7f), L.TitleFloor);
            Hide(_title);

            _tier = UIKit.Shrinkable(
                UIKit.Titled("Tier", _stage, Loc.Get(ShuffleChoiceOverlay.TierKey(Bond.Tier)).Upper(), L.TierSize, _tint,
                             TextAnchor.MiddleCenter, new Vector2(L.Wide, L.TierH), new Vector2(.5f, .5f),
                             new Vector2(0f, L.TierY), outline: 4f, shadow: 4f), L.TierFloor);
            Hide(_tier);

            _name = UIKit.Shrinkable(
                UIKit.Titled("Name", _stage, Loc.Get(Bond.NameKey), L.NameSize, Pal.Gold,
                             TextAnchor.MiddleCenter, new Vector2(L.Wide, L.NameH), new Vector2(.5f, .5f),
                             new Vector2(0f, L.NameY), outline: 5f, shadow: 6f), L.NameFloor);
            Hide(_name);

            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", _stage, Loc.Get(Bond.NoteKey), L.NoteSize, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(L.NoteWide, L.NoteH), new Vector2(.5f, .5f),
                             new Vector2(0f, L.NoteY), outline: 3f, shadow: 4f, wrap: true), L.NoteFloor);
            Hide(_note);

            // The recipe: the cards it took, with a plus between two for a secret and the count
            // under the one card for an evolution - pictures, so it reads in every language. An
            // ascension's is its keystone, a plus, and its school's gem with the points it took.
            if (_ascension)
            {
                AscensionRecipe();
                return;
            }

            int parts = Bond.Parts.Length;
            _recipe = new RectTransform[parts];

            for (int i = 0; i < parts; i++)
            {
                var spot = new Vector2(L.RecipeX(i, parts), L.RecipeY);
                _recipe[i] = Picture("Recipe", _stage, Card(Bond.Parts[i]), L.RecipeIcon, spot);
                _recipe[i].localScale = Vector3.zero;

                if (Bond.IsSecret && i + 1 < parts)
                {
                    var plus = UIKit.Titled("Plus", _stage, "+", 56, Pal.Cream, TextAnchor.MiddleCenter,
                                            new Vector2(L.RecipeGap, L.RecipeIcon), new Vector2(.5f, .5f),
                                            spot + new Vector2((L.RecipeIcon + L.RecipeGap) * .5f, 0f), outline: 3f, shadow: 3f);
                    plus.raycastTarget = false;
                    Hide(plus);
                    _plusSigns.Add(plus);
                }
            }

            if (!Bond.IsSecret && parts > 0)
            {
                var times = UIKit.Titled("Times", _recipe[0], Loc.Format("ui.shuffle.times", Bond.Needs), 44, _tint, TextAnchor.MiddleCenter,
                                         new Vector2(L.RecipeIcon, 56f), new Vector2(.5f, .5f),
                                         new Vector2(L.RecipeIcon * .62f, -L.RecipeIcon * .38f), outline: 4f, shadow: 4f);
                times.raycastTarget = false;
            }
        }

        readonly System.Collections.Generic.List<Text> _plusSigns = new System.Collections.Generic.List<Text>(3);

        /// <summary>An ascension's recipe: its keystone, a plus, and its school's gem times the points it took.</summary>
        void AscensionRecipe()
        {
            _recipe = new RectTransform[2];

            var first = new Vector2(L.RecipeX(0, 2), L.RecipeY);
            _recipe[0] = Picture("Recipe", _stage, Card(Bond.Parts[0]), L.RecipeIcon, first);
            _recipe[0].localScale = Vector3.zero;

            var plus = UIKit.Titled("Plus", _stage, "+", 56, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(L.RecipeGap, L.RecipeIcon), new Vector2(.5f, .5f),
                                    first + new Vector2((L.RecipeIcon + L.RecipeGap) * .5f, 0f), outline: 3f, shadow: 3f);
            plus.raycastTarget = false;
            Hide(plus);
            _plusSigns.Add(plus);

            var second = new Vector2(L.RecipeX(1, 2), L.RecipeY);
            var seat = UIKit.Img("Recipe", _stage, Art.S("Ui/" + Skins.Slot), Color.white, Vector2.one * L.RecipeIcon,
                                 new Vector2(.5f, .5f), second);
            seat.type = Image.Type.Sliced;
            seat.raycastTarget = false;
            UIKit.Halo(seat.transform, _kind, L.RecipeIcon * 1.6f, .45f);

            var gem = Art.S("Ui/" + Bond.Mark);
            var img = UIKit.Img("Gem", seat.transform, gem, Color.white, Vector2.one * L.RecipeIcon * .7f,
                                new Vector2(.5f, .5f), Vector2.zero);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = gem != null;

            var times = UIKit.Titled("Times", seat.transform, Loc.Format("ui.shuffle.times", Bond.Needs), 44, _kind,
                                     TextAnchor.MiddleCenter, new Vector2(L.RecipeIcon, 56f), new Vector2(.5f, .5f),
                                     new Vector2(L.RecipeIcon * .62f, -L.RecipeIcon * .38f), outline: 4f, shadow: 4f);
            times.raycastTarget = false;

            _recipe[1] = (RectTransform)seat.transform;
            _recipe[1].localScale = Vector3.zero;
        }

        static void Hide(Graphic g)
        {
            if (g == null) return;
            var c = g.color;
            c.a = 0f;
            g.color = c;
        }

        static void Show(Graphic g, float alpha, float over)
        {
            if (g != null) Tween.Fade(g, alpha, over);
        }

        // ------------------------------------------------------------------ the key
        void BuildKey()
        {
            _ok = UIKit.Button("Okay", _stage, Art.S("Ui/" + Skins.Settled), new Vector2(L.ButtonW, L.ButtonH),
                               new Vector2(.5f, .5f), new Vector2(0f, L.ButtonY), Okay);
            _okRt = (RectTransform)_ok.transform;
            _okRt.localScale = Vector3.zero;

            UIKit.Shrinkable(
                UIKit.Titled("Label", _ok.transform, Loc.Get("ui.shuffle.okay").Upper(), 50, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(L.ButtonW - 80f, 70f), new Vector2(.5f, .5f),
                             new Vector2(0f, L.ButtonH * UIKit.PillFaceLift), 0f, 3f), 28);
        }

        void Okay()
        {
            if (!_settled || _done) return;
            _done = true;

            Audio.Sfx("collect", .6f);
            var next = OnDone;
            OnDone = null;
            Close(() => next?.Invoke(), quiet: true);
        }

        /// <summary>The back key closes it once the key is there, and is swallowed before.</summary>
        public override bool OnBack()
        {
            Okay();
            return true;
        }

        // ------------------------------------------------------------------ the sequence
        void Play()
        {
            var cue = new Cue(this);
            var at = new Vector2(0f, L.EmblemY);

            // -- gathering ---------------------------------------------------
            cue.With(() =>
            {
                Tween.Fade(_veil, .85f, .35f);
                Tween.Fade(_vignette, .55f, .4f);
                for (int i = 0; i < _aurora.Length; i++) Tween.Fade(_aurora[i], .18f - i * .03f, .7f);
                Audio.Sfx("charge", .5f, .8f + .1f * _grand);
            });

            cue.Then(.08f, null).Repeat(_parts.Length, .07f, i =>
            {
                if (_parts[i] == null) return;
                Tween.Pop(_parts[i], 0f, .36f);
                Audio.Sfx("pop", .35f, 1f + i * .12f);
            });

            // Rings closing on the middle, one more for every step of tier.
            cue.Then(.12f, null).Repeat(2 + _grand, .1f, i => Collapse(i));

            // The cards circle in, faster as they come, each trailing sparks.
            cue.Then(.05f, () =>
            {
                for (int i = 0; i < _parts.Length; i++) Gather(i, at, .62f);
            });

            // -- the break ---------------------------------------------------
            cue.Then(.62f, () => Break(at));

            // -- the words ---------------------------------------------------
            cue.Then(.36f, () =>
            {
                _title.color = Pal.A(_title.color, 1f);
                _title.transform.localScale = Vector3.one * 2.2f;
                Tween.Scale(_title.transform, 1f, .28f, Ease.InCubic)
                     .OnDone(() =>
                     {
                         if (!_title) return;
                         Tween.Punch(_title.transform, .18f, .36f);
                         Tween.Shake((RectTransform)_stage, 10f + 6f * _grand, .25f);
                         Audio.Sfx("stand", .55f);
                     });
            });

            cue.Then(.22f, () =>
            {
                Show(_tier, 1f, .3f);
                Burst.Sparks(_stage, new Vector2(0f, L.TierY), _tint, 10 + 6 * _grand, 260f, 16f, .5f);
            });

            cue.Then(.18f, () =>
            {
                _name.color = Pal.A(_name.color, 1f);
                _name.transform.localScale = Vector3.zero;
                Tween.Pop(_name.transform, 0f, .42f);
                Audio.Sfx("star", .5f);
            });

            cue.Then(.2f, () => Show(_note, 1f, .4f));

            cue.Then(.16f, null).Repeat(_recipe.Length, .09f, i =>
            {
                if (_recipe[i] == null) return;
                Tween.Pop(_recipe[i], 0f, .36f);
                if (i < _plusSigns.Count) Show(_plusSigns[i], 1f, .25f);
            });

            // -- settled -----------------------------------------------------
            cue.Then(.25f, () =>
            {
                Tween.Pop(_okRt, .6f, .46f);
                _settled = true;
            });
        }

        /// <summary>A ring closing on the emblem before there is anything there.</summary>
        void Collapse(int index)
        {
            var colour = index % 2 == 0 ? _kind : _tint;
            var ring = UIKit.Img("In", Content, Art.Ring(256, 7f), Pal.A(colour, 0f), Vector2.one * 1150f,
                                 new Vector2(.5f, .5f), new Vector2(0f, L.EmblemY));
            ring.raycastTarget = false;

            Tween.Run(.46f, Ease.InCubic, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(1f, .14f, t);
                ring.color = Pal.A(colour, .75f * Mathf.Sin(t * Mathf.PI));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>One card spiralling in to the middle over <paramref name="over"/> seconds, shedding sparks.</summary>
        void Gather(int index, Vector2 at, float over)
        {
            var part = _parts[index];
            if (part == null) return;

            float start = Mathf.PI * .5f + index * Mathf.PI * 2f / _parts.Length;
            float nextSpark = 0f;

            Tween.Run(over, Ease.Linear, t =>
            {
                if (!part) return;

                float k = Ease.InCubic(t);
                float a = start + k * Mathf.PI * 1.4f;
                float r = L.Gather * (1f - k);

                part.anchoredPosition = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                part.localScale = Vector3.one * Mathf.Lerp(1f, .3f, k);
                part.localRotation = Quaternion.Euler(0f, 0f, k * 240f);

                if (t >= nextSpark)
                {
                    nextSpark = t + .12f;
                    Burst.Sparks(_stage, part.anchoredPosition, _kind, 3, 90f, 12f, .35f);
                }
            }, part).OnDone(() => { if (part) part.gameObject.SetActive(false); });
        }

        /// <summary>
        /// The break: the cards fuse in a flash, the emblem bursts out of it, the room is thrown
        /// into colour, rings and confetti, and the screen is shaken - all of it larger at every
        /// step of tier, and a legendary's with a third fan, a rainbow and a rain of sparks.
        /// </summary>
        void Break(Vector2 at)
        {
            // Its own sting, never `win` (owner, 2026-10-14: a combo must not sound like a level
            // finished), struck lower the grander it is; the bigger tiers keep their blast under it.
            Audio.Sfx("secret", .8f, 1.06f - .06f * _grand);
            if (_grand >= 2) Audio.Sfx("boom", .45f + .15f * _grand, .7f);
            if (_grand >= 3) Audio.Sfx("thunder", .5f, .9f);

            _flash.color = new Color(1f, .99f, .94f, .5f + .15f * _grand);
            Tween.Fade(_flash, 0f, .32f + .1f * _grand, Ease.OutQuad);

            Tween.Shake((RectTransform)Content, 14f + 10f * _grand, .3f + .12f * _grand);

            _emblemRt.localScale = Vector3.one * .2f;
            Tween.Scale(_emblemRt, 1f, .55f, Ease.OutBack);
            Tween.Fade(_halo, .7f, .4f);

            _fanRt.localScale = Vector3.one * .3f;
            Tween.Scale(_fanRt, 1f, .7f, Ease.OutQuint);
            Tween.Fade(_fan, .3f + .04f * _grand, .5f);

            _fan2Rt.localScale = Vector3.one * .3f;
            Tween.Scale(_fan2Rt, 1f, .82f, Ease.OutQuint);
            Tween.Fade(_fan2, .22f, .55f);

            if (_fan3 != null)
            {
                _fan3Rt.localScale = Vector3.one * .2f;
                Tween.Scale(_fan3Rt, 1f, 1f, Ease.OutQuint);
                Tween.Fade(_fan3, .3f, .7f);
            }

            Tween.Fade(_glow, .4f + .08f * _grand, .5f);

            for (int i = 0; i < 2 + 2 * _grand; i++)
                Shockwave(i * .07f, i % 3 == 0 ? Color.white : i % 3 == 1 ? _kind : _tint, at);

            Burst.Sparks(_stage, at, Color.white, 18 + 10 * _grand, 520f, 24f, .7f);
            Burst.Sparks(_stage, at, _kind, 14 + 8 * _grand, 420f, 20f, .8f);
            Burst.Confetti(Content, 40 + 30 * _grand);

            if (_grand >= 2) Tween.After(.35f, () => { if (this) Burst.Confetti(Content, 40 * _grand); }, this);
            if (_grand >= 3)
            {
                Tween.After(.75f, () => { if (this) Burst.Confetti(Content, 90); }, this);
                Fireflies.Spawn(Content, 34, Pal.A(Pal.Gold, .8f), 6f, 18f);
            }

            _live = true;
        }

        /// <summary>A ring leaving the emblem on the break.</summary>
        void Shockwave(float delay, Color colour, Vector2 at)
        {
            var ring = UIKit.Img("Wave", Content, Art.Ring(256, 10f), Pal.A(colour, 0f), Vector2.one * 420f,
                                 new Vector2(.5f, .5f), at);
            ring.raycastTarget = false;

            Tween.Run(.75f, Ease.OutQuint, t =>
            {
                if (!ring) return;
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.2f, 3.6f, t);
                ring.color = Pal.A(colour, .85f * (1f - t));
            }, ring).Delay(delay).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        // ------------------------------------------------------------------ while it stands
        bool _live;

        static readonly Color[] Rainbow = { Pal.Poppy, Pal.Marigold, Pal.Pollen, Pal.Verdant, Pal.Azure, Pal.Foxglove };

        /// <summary>
        /// Keeps the room alive once it has broken: the fans turning against each other, the
        /// emblem breathing, and - for a legendary - the halo walking the rainbow and the
        /// headline shimmering gold to white.
        /// </summary>
        void Update()
        {
            if (!_live) return;

            float dt = Time.unscaledDeltaTime;
            _age += dt;

            if (_fanRt) _fanRt.localRotation = Quaternion.Euler(0f, 0f, _age * (10f + 4f * _grand));
            if (_fan2Rt) _fan2Rt.localRotation = Quaternion.Euler(0f, 0f, -_age * (16f + 5f * _grand));
            if (_fan3Rt) _fan3Rt.localRotation = Quaternion.Euler(0f, 0f, _age * 6f);

            if (_emblemRt && _settled)
            {
                float breathe = 1f + .04f * Mathf.Sin(_age * 2.4f);
                _emblemRt.localScale = Vector3.one * breathe;
                _emblemRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_age * 1.3f) * 4f);
            }

            if (_grand < 3) return;

            // **An ascension glows in its own element**, breathing from its colour to white,
            // rather than walking the legendary's rainbow: the room says which school ascended.
            if (_ascension)
            {
                float beat = .5f + .5f * Mathf.Sin(_age * 3.2f);
                var lit = Color.Lerp(_kind, Color.white, .35f * beat);
                if (_halo) _halo.color = Pal.A(lit, .75f);
                if (_glow) _glow.color = Pal.A(_kind, .45f + .15f * beat);
                if (_title && _settled) _title.color = Color.Lerp(_kind, Color.white, .45f + .55f * beat);
                return;
            }

            float walk = _age * 1.6f;
            int a = Mathf.FloorToInt(walk) % Rainbow.Length;
            var hue = Color.Lerp(Rainbow[a], Rainbow[(a + 1) % Rainbow.Length], walk - Mathf.Floor(walk));

            if (_halo) _halo.color = Pal.A(hue, .7f);
            if (_glow) _glow.color = Pal.A(hue, .5f);
            if (_title && _settled) _title.color = Color.Lerp(Pal.Gold, Color.white, .5f + .5f * Mathf.Sin(_age * 5f));
        }
    }
}
