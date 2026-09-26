using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Challenges;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The hill and the ward line of a challenge: raiders walking down four lanes onto four
    /// posts, drawn from the live mode's own art and stepped by a replay of what the rules
    /// said happened.
    ///
    /// <para>
    /// <b>It draws a state and replays a list; it decides nothing.</b> <see cref="Repaint"/>
    /// puts every widget where <see cref="ChallengeHill"/> says it is, and a replay walks one
    /// turn's events in the order the rules resolved them — bolts, steps, blows, musters — so
    /// what a player sees is exactly the order the model used and nothing this class made up.
    /// </para>
    /// <para>
    /// <b>Turns are queued, never awaited by the board</b> (2026-09-26). The first cut had the
    /// screen wait for the whole replay — a bolt, a step and a blow is about a second — and
    /// refuse every tap made inside it, which on the glade read as conduits that "sometimes
    /// don't rotate". Now <see cref="Enqueue"/> takes a turn and returns; the hill drains its
    /// queue on its own, and a player who moves faster than the hill walks sees it hurry
    /// (<see cref="Hurry"/>) rather than sees their tap thrown away. <b>Which is why every
    /// event is drawn off the event and never off the model</b>: with two turns queued the
    /// model is already two steps ahead of the picture, so a step reads its new distance from
    /// the event, a bolt takes its damage off the widget's own drawn health, and only the
    /// repaint that follows the <em>last</em> queued turn asks the model where things stand.
    /// A widget the replay leaves behind is put right by that repaint.
    /// </para>
    /// <para>
    /// <b>Each colour has a lane, and the lane ends on that colour's post.</b> That is the
    /// colour lock drawn rather than explained: a red raider walks at the red turret, so
    /// which puzzle move to make next is readable off the hill without a caption.
    /// </para>
    /// </summary>
    public sealed class ChallengeHillView : MonoBehaviour
    {
        sealed class Mob
        {
            public int Id, Colour;

            /// <summary>The health the widget is drawn at, stepped by events (see the class note).</summary>
            public int Shown;

            public RectTransform Node;
            public Image Body, Fill;
            public bool Dying;
        }

        sealed class Post
        {
            /// <summary>The health the post is drawn at, stepped by events.</summary>
            public int Shown;

            public RectTransform Node;
            public Image Body, Fill, Bank;
            public Text Held;
            public bool Down;
        }

        /// <summary>How tall a creeper is, in units. The siege's own figure for its kind.</summary>
        const float RaiderTall = 1.0f;

        /// <summary>
        /// A post is drawn at this fraction of a siege turret. The siege sizes a turret to the
        /// match-three cell it fires from; here the line stands under a hill, and four posts
        /// at full size took two cells of a screen the hill needs (the owner's "make turrets
        /// smaller", 2026-09-23). Everything on a post — chassis, socket, glow, health, bank
        /// — is sized off <see cref="_post"/>, and everything on the hill off the unit, so
        /// the raiders did not shrink with them.
        /// </summary>
        public const float PostScale = .72f;

        const float FlightFor = .22f, StepFor = .30f, Stagger = .10f;

        /// <summary>
        /// How much faster a turn is replayed while another is already waiting behind it. A
        /// player two moves ahead of the hill sees it catch up in a beat rather than drift
        /// further behind with every move; the tweens keep their own pace and only the waits
        /// between beats shorten, so nothing snaps.
        /// </summary>
        public const float Hurry = 1.6f;

        /// <summary>How far below the band's top edge a raider stands when it musters, in units.</summary>
        public const float HillTopInset = .95f;

        ChallengeHill _hill;
        RectTransform _root, _hillLayer, _mobs, _wall, _fx;
        readonly List<Mob> _mob = new List<Mob>(16);
        Post[] _posts;

        readonly Queue<List<ChallengeEvent>> _turns = new Queue<List<ChallengeEvent>>(4);
        bool _draining;

        float _unit, _post, _wide, _hillTop, _hillFoot, _lineY;

        /// <summary>The line's top edge, in the host's space: where the puzzle band may begin.</summary>
        public float Foot { get; private set; }

        /// <summary>Whether every queued turn has been drawn. The ending waits on it.</summary>
        public bool Idle => !_draining && _turns.Count == 0;

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Build into a host whose top is the hill's top and whose height is the hill and the
        /// line together. <c>unit</c> is the siege's cell: everything on the line is sized off it.
        /// </summary>
        public void Build(RectTransform host, ChallengeHill hill, float unit, float lineBand)
        {
            _hill = hill;
            _unit = unit;
            _post = unit * PostScale;
            _root = host;
            _wide = host.rect.width;

            // **The top of the hill is a body's height inside the band**, not at its edge:
            // a raider stands *on* the hill top with its gem pip above it, and the readout
            // row sits directly above this host. The mirror is what found them overlapping.
            float h = host.rect.height;
            _hillTop = h * .5f - unit * HillTopInset;
            _hillFoot = h * .5f - (h - lineBand);
            _lineY = _hillFoot - lineBand * .30f;
            Foot = -h * .5f;

            _hillLayer = Layer("Hill");
            _mobs = Layer("Raiders");
            _wall = Layer("Line");
            _fx = Layer("Fx");

            Ground();
            Line();
            Repaint();
        }

        RectTransform Layer(string name)
        {
            var rt = UIKit.Node(name, _root);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.sizeDelta = _root.rect.size;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        void Ground()
        {
            float tall = _hillTop - _hillFoot + _unit * HillTopInset;
            var band = new Vector2(_wide, tall + _unit * .5f);

            var host = UIKit.Node("Ground", _hillLayer);
            host.anchorMin = host.anchorMax = new Vector2(.5f, .5f);
            host.sizeDelta = band;
            host.anchoredPosition = new Vector2(0f, (_hillTop + _hillFoot) * .5f);
            host.gameObject.AddComponent<RectMask2D>();

            var rock = ChallengeArt.Ground();
            var ground = UIKit.Img("Rock", host, rock, Color.white,
                                   rock != null && rock.rect.height > 0f
                                       ? SiegeView.GroundSize(band, rock.rect.width / rock.rect.height)
                                       : band);
            ground.raycastTarget = false;
            ground.enabled = rock != null;

            // A faint lane down to each post, so the lock reads before a raider has stepped out.
            for (int i = 0; i < ChallengeColours.Count; i++)
            {
                var lane = UIKit.Img("Lane", _hillLayer, Art.SoftCapsule(40, 120),
                                     Pal.A(ChallengeArt.Tint(i), .10f),
                                     new Vector2(_unit * .9f, tall));
                lane.raycastTarget = false;
                lane.rectTransform.anchoredPosition = new Vector2(PostX(i), (_hillTop + _hillFoot) * .5f);
            }
        }

        void Line()
        {
            float band = _hillFoot - Foot;

            var wall = UIKit.Img("Rampart", _wall, ChallengeArt.Rampart(), Color.white,
                                 new Vector2(_wide, band * 1.02f));
            wall.raycastTarget = false;
            wall.type = Image.Type.Sliced;
            wall.rectTransform.anchoredPosition = new Vector2(0f, _hillFoot - band * .5f);
            wall.enabled = wall.sprite != null;

            _posts = new Post[_hill.Wards.Count];

            for (int i = 0; i < _posts.Length; i++)
            {
                var ward = _hill.Wards[i];
                var post = new Post { Shown = ward.Health };

                post.Node = UIKit.Node("Ward", _wall);
                post.Node.anchorMin = post.Node.anchorMax = new Vector2(.5f, .5f);
                post.Node.sizeDelta = new Vector2(_post * 1.8f, _post * 2.3f);
                post.Node.anchoredPosition = new Vector2(PostX(i), _lineY);

                var socket = UIKit.Img("Base", post.Node, ChallengeArt.Socket(), Color.white,
                                       new Vector2(_post * 1.7f, _post * .8f));
                socket.raycastTarget = false;
                socket.rectTransform.anchoredPosition = new Vector2(0f, -_post * .88f);
                socket.enabled = socket.sprite != null;

                var glow = UIKit.Img("Glow", post.Node, Art.Glow(128, 2.1f),
                                     Pal.A(ChallengeArt.Tint(ward.Colour), .22f),
                                     new Vector2(_post * 3.1f, _post * 3.1f));
                glow.raycastTarget = false;

                post.Body = UIKit.Img("Post", post.Node, ChallengeArt.Ward(ward.Colour), Color.white,
                                      new Vector2(_post * SiegeView.BodyWide, _post * SiegeView.BodyTall));
                post.Body.raycastTarget = false;
                post.Body.preserveAspect = true;
                post.Body.rectTransform.anchoredPosition = new Vector2(0f, _post * .06f);
                post.Body.enabled = post.Body.sprite != null;

                // Health: a trough and a fill above the chassis, the siege's own furniture.
                var bar = UIKit.Node("Health", post.Node);
                bar.anchorMin = bar.anchorMax = new Vector2(.5f, .5f);
                bar.sizeDelta = new Vector2(_post * 1.06f, _post * .17f);
                bar.anchoredPosition = new Vector2(0f, _post * 1.26f);

                var kerb = UIKit.Img("Trough", bar, Art.Round(10), new Color(0f, 0f, 0f, .66f), bar.sizeDelta);
                kerb.raycastTarget = false;

                post.Fill = UIKit.Img("Fill", bar, Art.Round(10), Pal.Cream,
                                      new Vector2(bar.sizeDelta.x - 4f, bar.sizeDelta.y - 4f));
                post.Fill.raycastTarget = false;
                post.Fill.rectTransform.pivot = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchorMin = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchorMax = new Vector2(0f, .5f);
                post.Fill.rectTransform.anchoredPosition = new Vector2(2f, 0f);

                // The bank: bolts fed and waiting for a target, as a count on a disc.
                var pipAt = new Vector2(_post * .62f, _post * .30f);
                post.Bank = UIKit.Img("Bank", post.Node, Art.Disc(64), Pal.A(ChallengeArt.Tint(ward.Colour), 1f),
                                      new Vector2(_post * .40f, _post * .40f));
                post.Bank.raycastTarget = false;
                post.Bank.rectTransform.anchoredPosition = pipAt;

                post.Held = UIKit.Titled("Held", post.Node, string.Empty, Mathf.RoundToInt(_post * .26f),
                                         Pal.Cream, TextAnchor.MiddleCenter,
                                         new Vector2(_post * .5f, _post * .4f), default, default, 1.5f, 0f);
                post.Held.rectTransform.anchoredPosition = pipAt;

                _posts[i] = post;
            }
        }

        // ------------------------------------------------------------------ the posts, from outside
        /// <summary>
        /// The node a colour's post is built in, for a puzzle to fly a feed at and a lesson to
        /// ring. Null before <see cref="Build"/> or for a colour the line does not hold.
        /// </summary>
        public RectTransform PostNode(int colour)
        {
            if (_posts == null) return null;
            for (int i = 0; i < _posts.Length; i++)
                if (_hill.Wards[i].Colour == colour) return _posts[i].Node;
            return null;
        }

        /// <summary>
        /// A feed has just landed on a colour's post: the chassis takes it with a small punch
        /// and a ring of its colour spreads off the socket, so the moment a merge reaches the
        /// line is a thing that is seen rather than a number that changes. The replay that
        /// follows draws what the feed bought; this is only the arrival.
        /// </summary>
        public void Fed(int colour)
        {
            Post post = null;
            for (int i = 0; _posts != null && i < _posts.Length; i++)
                if (_hill.Wards[i].Colour == colour) post = _posts[i];
            if (post == null || post.Body == null) return;

            Tween.Punch(post.Body.transform, .14f, .24f);

            var tint = ChallengeArt.Tint(colour);
            var ring = UIKit.Img("Fed", _fx, Art.Ring(128, 9f), Pal.A(tint, .9f),
                                 new Vector2(_post * .9f, _post * .9f), new Vector2(.5f, .5f),
                                 post.Node.anchoredPosition + new Vector2(0f, _post * .3f));
            ring.raycastTarget = false;
            var rt = ring.rectTransform;
            Tween.Scale(rt, 2.2f, .34f, Ease.OutCubic);
            Tween.Fade(ring, 0f, .34f, Ease.InQuad).OnDone(() => { if (rt) Destroy(rt.gameObject); });
        }

        // ------------------------------------------------------------------ geometry
        public float PostX(int index)
        {
            int n = ChallengeColours.Count;
            float wide = _wide / (n + .6f);
            return (index - (n - 1) * .5f) * wide;
        }

        float MarchY(int distance)
        {
            float t = _hill.Length <= 0 ? 1f : 1f - Mathf.Clamp01(distance / (float)_hill.Length);
            return Mathf.Lerp(_hillTop, _hillFoot + _unit * .45f, t);
        }

        /// <summary>
        /// Where a body of a colour stands at a distance. Bodies sharing a lane step sideways
        /// a little so two at one distance both read. Takes the distance rather than the
        /// raider, because during a queued replay the raider's own distance is ahead of the
        /// picture (the class note).
        /// </summary>
        Vector2 Seat(int colour, int id, int distance)
        {
            float side = ((id % 3) - 1) * _unit * .28f;
            return new Vector2(PostX(colour) + side, MarchY(distance));
        }

        // ------------------------------------------------------------------ painting
        /// <summary>Put everything where the model says it is, and draw every health as the model holds it.</summary>
        public void Repaint()
        {
            for (int i = 0; i < _posts.Length; i++)
            {
                _posts[i].Shown = _hill.Wards[i].Health;
                PaintPost(i);
            }

            for (int i = 0; i < _hill.Raiders.Count; i++)
            {
                var raider = _hill.Raiders[i];
                var mob = Widget(raider, raider.Distance);
                if (mob == null) continue;

                mob.Node.anchoredPosition = Seat(raider.Colour, raider.Id, raider.Distance);
                mob.Shown = raider.Health;
                PaintHealth(mob, raider);
            }

            Reap();
        }

        void PaintBanks()
        {
            for (int i = 0; i < _posts.Length; i++)
            {
                var ward = _hill.Wards[i];
                var post = _posts[i];
                bool show = ward.Alive && ward.Banked > 0;
                post.Bank.enabled = show;
                post.Held.enabled = show;
                post.Held.text = show ? ward.Banked.ToString() : string.Empty;
            }
        }

        void PaintPost(int i)
        {
            var ward = _hill.Wards[i];
            var post = _posts[i];

            int shown = Mathf.Clamp(post.Shown, 0, ward.MaxHealth);
            float frac = ward.MaxHealth > 0 ? shown / (float)ward.MaxHealth : 0f;
            var size = post.Fill.rectTransform.sizeDelta;
            post.Fill.rectTransform.sizeDelta = new Vector2((_post * 1.06f - 4f) * frac, size.y);
            post.Fill.color = frac > .34f ? Pal.Cream : Pal.Rose;

            bool show = ward.Alive && ward.Banked > 0;
            post.Bank.enabled = show;
            post.Held.enabled = show;
            post.Held.text = show ? ward.Banked.ToString() : string.Empty;

            if (shown <= 0 && !post.Down)
            {
                post.Down = true;
                var down = ChallengeArt.WardDown();
                if (down != null) post.Body.sprite = down;
                post.Body.color = new Color(.55f, .55f, .6f, 1f);
            }
        }

        Mob Widget(ChallengeRaider raider, int distance)
        {
            for (int i = 0; i < _mob.Count; i++) if (_mob[i].Id == raider.Id) return _mob[i];
            return raider.Alive ? Hatch(raider, distance) : null;
        }

        Mob Widget(int id)
        {
            for (int i = 0; i < _mob.Count; i++) if (_mob[i].Id == id) return _mob[i];
            return null;
        }

        Mob Hatch(ChallengeRaider raider, int distance)
        {
            var mob = new Mob { Id = raider.Id, Colour = raider.Colour, Shown = raider.MaxHealth };

            mob.Node = UIKit.Node("Raider", _mobs);
            mob.Node.anchorMin = mob.Node.anchorMax = new Vector2(.5f, .5f);
            mob.Node.sizeDelta = new Vector2(_unit, _unit);
            mob.Node.anchoredPosition = Seat(raider.Colour, raider.Id, distance);

            float tall = _unit * RaiderTall;
            var frames = ChallengeArt.Raider(raider.Colour);
            bool drawn = frames != null && frames.Length > 0 && frames[0] != null;
            float wide = drawn && frames[0].rect.height > 0f ? tall * frames[0].rect.width / frames[0].rect.height : tall;

            var shadow = UIKit.Img("Shadow", mob.Node, Art.Glow(64, .25f), new Color(0f, 0f, 0f, .55f),
                                   new Vector2(wide * .78f, tall * .94f * .37f));
            shadow.raycastTarget = false;
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -tall * .94f * .21f);

            if (drawn)
            {
                mob.Body = UIKit.Img("Body", mob.Node, frames[0], Color.white, new Vector2(wide, tall));
                mob.Body.raycastTarget = false;
                mob.Body.preserveAspect = true;
                Flipbook.Attach(mob.Body, frames, 13f, true);
                Tween.Bob(mob.Body.rectTransform, tall * .035f, .72f, raider.Id * .37f);
            }
            else
            {
                // No cast resident: a tinted gem stands in, which is never a white rectangle.
                mob.Body = UIKit.Img("Body", mob.Node, Art.Disc(96), ChallengeArt.Tint(raider.Colour),
                                     new Vector2(tall * .7f, tall * .7f));
                mob.Body.raycastTarget = false;
            }

            var bar = UIKit.Node("Bar", mob.Node);
            bar.anchorMin = bar.anchorMax = new Vector2(.5f, .5f);
            bar.sizeDelta = new Vector2(tall * .72f, _unit * .13f);
            bar.anchoredPosition = new Vector2(0f, tall * .58f);

            var trough = UIKit.Img("Trough", bar, Art.Round(10), new Color(0f, 0f, 0f, .66f), bar.sizeDelta);
            trough.raycastTarget = false;

            mob.Fill = UIKit.Img("Fill", bar, Art.Round(10), Pal.Rose,
                                 new Vector2(bar.sizeDelta.x - 4f, bar.sizeDelta.y - 4f));
            mob.Fill.raycastTarget = false;
            mob.Fill.rectTransform.pivot = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMin = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchorMax = new Vector2(0f, .5f);
            mob.Fill.rectTransform.anchoredPosition = new Vector2(2f, 0f);

            float pip = _unit * .34f;
            var gem = UIKit.Img("Pip", mob.Node, ChallengeArt.Gem(raider.Colour), Color.white,
                                new Vector2(pip, pip));
            gem.raycastTarget = false;
            gem.preserveAspect = true;
            gem.rectTransform.anchoredPosition = new Vector2(0f, tall * .58f + _unit * .24f);
            gem.enabled = gem.sprite != null;

            var group = mob.Node.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            Tween.Fade(group, 1f, .25f);

            _mob.Add(mob);
            PaintHealth(mob, raider);
            return mob;
        }

        void PaintHealth(Mob mob, ChallengeRaider raider)
        {
            int shown = Mathf.Clamp(mob.Shown, 0, raider.MaxHealth);
            float frac = raider.MaxHealth > 0 ? shown / (float)raider.MaxHealth : 0f;
            var size = mob.Fill.rectTransform.sizeDelta;
            mob.Fill.rectTransform.sizeDelta = new Vector2((_unit * RaiderTall * .72f - 4f) * Mathf.Clamp01(frac), size.y);
        }

        /// <summary>Take down the widget of anything dead that no replay is still using.</summary>
        void Reap()
        {
            for (int i = _mob.Count - 1; i >= 0; i--)
            {
                var mob = _mob[i];
                var raider = _hill.Find(mob.Id);
                if (raider != null && raider.Alive) continue;
                if (mob.Dying) continue;

                Fell(mob);
            }
        }

        void Fell(Mob mob)
        {
            mob.Dying = true;
            var node = mob.Node;
            Tween.KillAll(node);

            var group = node.GetComponent<CanvasGroup>() ?? node.gameObject.AddComponent<CanvasGroup>();
            Tween.Fade(group, 0f, .28f).OnDone(() => { if (node) Destroy(node.gameObject); });
            Tween.Scale(node, .6f, .28f, Ease.InQuad);

            _mob.Remove(mob);
        }

        // ------------------------------------------------------------------ the replay
        /// <summary>
        /// Queue one turn's events to be drawn after whatever is already drawing. Returns at
        /// once; <see cref="Idle"/> says when the queue has run dry. The list is copied,
        /// because the run reuses its report on the next input.
        /// </summary>
        public void Enqueue(IReadOnlyList<ChallengeEvent> events)
        {
            var copy = new List<ChallengeEvent>(events.Count);
            for (int i = 0; i < events.Count; i++) copy.Add(events[i]);
            _turns.Enqueue(copy);

            if (!_draining) StartCoroutine(Drain());
        }

        IEnumerator Drain()
        {
            _draining = true;

            while (_turns.Count > 0)
            {
                var turn = _turns.Dequeue();
                float pace = _turns.Count > 0 ? Hurry : 1f;

                yield return Replay(turn, pace);
                if (!this) yield break;

                // Between queued turns only the banks are re-read — they carry no position
                // and no step, so the model's answer cannot jump anything. The last turn is
                // followed by the full repaint.
                if (_turns.Count > 0) PaintBanks();
                else Repaint();
            }

            _draining = false;
        }

        /// <summary>
        /// Play one turn's events in order, at <paramref name="pace"/> times the resting speed.
        /// Everything is drawn off the event (the class note).
        /// </summary>
        IEnumerator Replay(List<ChallengeEvent> events, float pace)
        {
            int at = 0;
            while (at < events.Count)
            {
                var e = events[at];

                switch (e.Kind)
                {
                    case ChallengeEventKind.Bolt:
                    {
                        // A run of bolts is drawn staggered and waited on together.
                        int end = at;
                        while (end < events.Count && events[end].Kind == ChallengeEventKind.Bolt) end++;
                        int count = end - at;
                        float stagger = (count > 6 ? .05f : Stagger) / pace;

                        for (int i = at; i < end; i++) Bolt(events[i], (i - at) * stagger);

                        yield return new WaitForSecondsRealtime((count - 1) * stagger + FlightFor + .12f / pace);
                        at = end;
                        continue;
                    }

                    case ChallengeEventKind.Stepped:
                    {
                        int end = at;
                        while (end < events.Count && events[end].Kind == ChallengeEventKind.Stepped) end++;

                        for (int i = at; i < end; i++) Step(events[i]);

                        yield return new WaitForSecondsRealtime(StepFor / pace);
                        at = end;
                        continue;
                    }

                    case ChallengeEventKind.Struck:
                        Struck(e);
                        yield return new WaitForSecondsRealtime(.16f / pace);
                        break;

                    case ChallengeEventKind.Mustered:
                    {
                        var raider = _hill.Find(e.Raider);
                        if (raider != null) Widget(raider, e.Amount);
                        break;
                    }
                }

                at++;
            }

            yield return new WaitForSecondsRealtime(.08f / pace);
        }

        void Bolt(ChallengeEvent e, float delay)
        {
            var post = _posts[e.Ward];
            var raider = _hill.Find(e.Raider);
            var mob = Widget(e.Raider);
            if (mob == null || raider == null) return;

            int colour = _hill.Wards[e.Ward].Colour;
            var tint = ChallengeArt.Tint(colour);
            var from = new Vector2(PostX(e.Ward), _lineY + _post * 1.0f);
            var to = mob.Node.anchoredPosition;
            var dir = to - from;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            var node = mob.Node;
            var body = post.Body;

            Tween.After(delay, () =>
            {
                if (!node || !this) return;

                // The recoil and the flash at the barrel.
                if (body)
                {
                    Tween.KillChannel(body, "kick");
                    Tween.Run(.18f, Ease.OutQuad, t =>
                    {
                        if (!body) return;
                        float k = t < .3f ? t / .3f : 1f - (t - .3f) / .7f;
                        body.rectTransform.anchoredPosition = new Vector2(0f, _post * .06f - _post * .13f * k);
                    }, body, "kick");
                }

                Puff(ChallengeArt.Muzzle(colour), from, angle, _post * 2.7f, .32f, SiegeView.MuzzleAt);

                var shot = Reel(ChallengeArt.Shot(colour), from, angle, _unit * 1.2f, SiegeView.HeadAt);
                if (shot == null)
                {
                    shot = UIKit.Img("Shot", _fx, Art.Capsule(20, 60), tint, new Vector2(_unit * .22f, _unit * .7f),
                                     new Vector2(.5f, .5f), from).rectTransform;
                    shot.localRotation = Quaternion.Euler(0f, 0f, angle);
                }

                var rt = shot;
                Tween.Run(FlightFor, Ease.Linear, t =>
                {
                    if (!rt) return;
                    rt.anchoredPosition = Vector2.Lerp(from, node ? node.anchoredPosition : to, t);
                }, rt).OnDone(() =>
                {
                    if (rt) Destroy(rt.gameObject);
                    if (!this) return;

                    var land = node ? node.anchoredPosition : to;
                    Puff(ChallengeArt.Hit(colour), land, angle, _unit * (e.Ended ? 4.1f : 3.2f), .35f, .5f);
                    Number(land, e.Amount);

                    if (node) Tween.Punch(node, .1f, .14f);

                    // The blow comes off the drawn health, never the model's: with turns
                    // queued the model may already hold the next turn's answer.
                    mob.Shown -= e.Amount;

                    if (e.Ended)
                    {
                        Audio.SfxVaried("zap", .42f);
                        if (!mob.Dying) Fell(mob);
                    }
                    else
                    {
                        PaintHealth(mob, raider);
                    }
                });

                Audio.Sfx("shot", .20f);
            }, this);
        }

        RectTransform Reel(Sprite[] frames, Vector2 at, float angle, float wide, float head)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return null;

            float aspect = frames[0].rect.width > 0f ? frames[0].rect.height / frames[0].rect.width : 1f;
            float tall = wide * aspect;

            var node = UIKit.Node("Reel", _fx);
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.sizeDelta = new Vector2(wide, tall);
            node.anchoredPosition = at;
            node.localRotation = Quaternion.Euler(0f, 0f, angle);

            var img = UIKit.Img("Frames", node, frames[0], Color.white, new Vector2(wide, tall),
                                new Vector2(.5f, .5f), new Vector2(0f, -(head - .5f) * tall));
            img.raycastTarget = false;
            img.preserveAspect = true;
            Flipbook.Attach(img, frames, 30f, true);

            return node;
        }

        void Puff(Sprite[] frames, Vector2 at, float angle, float wide, float life, float head)
        {
            var node = Reel(frames, at, angle, wide, head);
            if (node == null) return;

            Tween.After(life, () => { if (node) Destroy(node.gameObject); }, node);
        }

        void Number(Vector2 at, int amount)
        {
            var label = UIKit.Titled("Hit", _fx, "-" + amount, Mathf.RoundToInt(_unit * .30f), Pal.Cream,
                                     TextAnchor.MiddleCenter, new Vector2(_unit * 1.5f, _unit * .5f),
                                     new Vector2(.5f, .5f), at + new Vector2(0f, _unit * .3f), 2f, 2f);
            var rt = label.rectTransform;
            Tween.Move(rt, at + new Vector2(0f, _unit * .9f), .5f, Ease.OutQuad);
            Tween.Fade(label, 0f, .5f, Ease.InQuad).OnDone(() => { if (rt) Destroy(rt.gameObject); });
        }

        void Step(ChallengeEvent e)
        {
            var raider = _hill.Find(e.Raider);
            if (raider == null) return;

            // Hatched at the step's own distance plus one if a muster was never drawn, so a
            // body the picture has not met walks in from the top rather than appearing mid-hill.
            var mob = Widget(raider, e.Amount + 1);
            if (mob == null) return;

            Tween.KillChannel(mob.Node, "walk");
            Tween.Move(mob.Node, Seat(raider.Colour, raider.Id, e.Amount), StepFor, Ease.InOutSine);
        }

        void Struck(ChallengeEvent e)
        {
            var post = _posts[e.Ward];
            Tween.Punch(post.Body.transform, .16f, .22f);
            Tween.Shake(post.Node, _unit * .08f, .25f);

            post.Shown -= e.Amount;
            PaintPost(e.Ward);

            Audio.Sfx(e.Ended ? "shatter" : "poke", .5f);

            var mob = Widget(e.Raider);
            if (mob != null) Tween.Punch(mob.Node, .2f, .2f);
        }
    }
}
