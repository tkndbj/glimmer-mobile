using System;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A door out of a page drawn as the keeper ladder's BUY LEVEL key: the hub's Daily
    /// Challenges door and the Refer a Friend door on the profile and the shop (the owner,
    /// 2026-09-28).
    ///
    /// <para>
    /// <b>One builder, because three screens wear it</b> (44d, said of code): the violet pill
    /// (<see cref="Skins.Gem"/>, the colour the BUY LEVEL key wears once the ladder is priced in
    /// gems), a cream caption on two lines on the left, lifted onto the pill's face by
    /// <see cref="UIKit.PillFaceLift"/>, and one of the owner's pictures on the right standing
    /// on the key's foot and rising out of its top. The caption is a loc key, so these doors
    /// are translated - the painted banners they replace carried English in their pixels.
    /// </para>
    /// <para>
    /// <b>The key is <see cref="KeyH"/> tall whatever its slot</b>, near the keeper key's 136
    /// and the pill sprite's own 166: the pill is sliced across its width only
    /// (<c>15,0,15,0</c>), so drawn much taller it smears its own moulded face (44a). The slot
    /// is taller than the key, and the difference is the room the picture rises into.
    /// </para>
    /// <para>
    /// <b>The top-left corner is the badge's</b> (<c>WaitingBadge.HubTopLeft</c> and its gift
    /// twin), so the caption starts <see cref="CaptionLeft"/> in. The caller builds the badge
    /// after this, so it draws over the key, and plays the entrance, which differs per page.
    /// </para>
    /// </summary>
    public static class DoorKey
    {
        /// <summary>The key's own height inside its slot.</summary>
        public const float KeyH = 160f;

        /// <summary>How far in from the key's right end the picture stands.</summary>
        public const float ArtRight = 18f;

        /// <summary>Where the caption starts: clear of the starburst on the key's top-left corner.</summary>
        public const float CaptionLeft = 118f;

        /// <summary>The tallest the picture is drawn, which is the hub's slot less a margin.</summary>
        public const float ArtMost = 274f;

        /// <summary>
        /// The widest the picture is drawn, so a wide picture (the chest hoard is 2.5:1) cannot
        /// squeeze the caption: a wide picture is drawn shorter and rises less out of the key.
        /// </summary>
        public const float ArtWidest = 480f;

        /// <summary>
        /// Where a <see cref="Plate"/>'s caption starts: a plate wears no badge, so its caption
        /// needs only the pill's own rounded end to clear.
        /// </summary>
        public const float PlateCaptionLeft = 44f;

        /// <summary>
        /// Builds the key at the foot of a slot <paramref name="slotH"/> tall whose centre is
        /// <paramref name="slotCentre"/> under <paramref name="anchor"/>. Returns the key, which is
        /// the button, so the caller can hang its badge and its entrance on it.
        /// </summary>
        public static Btn Build(string name, Transform parent, float width, float slotH, Vector2 anchor,
                                Vector2 slotCentre, string captionKey, string art, Action tap)
        {
            var centre = slotCentre - new Vector2(0f, (slotH - KeyH) * .5f);
            var key = UIKit.Button(name, parent, Art.S("Ui/" + Skins.Gem), new Vector2(width, KeyH), anchor,
                                   centre, tap);

            // A press-scale that squashes a key this wide reads as the screen flinching rather
            // than as a key going down.
            key.PressScale = .985f;

            Dress(key.transform, width, slotH, Loc.Get(captionKey), art, CaptionLeft, Pal.Cream);
            return key;
        }

        /// <summary>
        /// The same furniture as <see cref="Build"/> - the pill, the picture rising out of it and
        /// the two-line caption - as a <b>decoration rather than a door</b>: no tap, no press, no
        /// badge corner, and the pill's colour named by the caller (a <c>btn_*</c> cut, so it is
        /// a re-cut of the kit's own mould rather than a multiply on the violet one, 44g). The
        /// welcome page's header wears it under its sentence (the owner, 2026-10-07).
        ///
        /// <para>
        /// The caption is cream like the doors' unless the caller names its <paramref name="ink"/>.
        /// Returns the caption so a page that rewrites its sentence (the welcome page's
        /// "every turret is yours") writes it through <see cref="TwoLines"/> as this did.
        /// </para>
        /// </summary>
        public static Text Plate(string name, Transform parent, string pill, float width, float slotH,
                                 Vector2 anchor, Vector2 slotCentre, string caption, string art,
                                 Color? ink = null)
        {
            var centre = slotCentre - new Vector2(0f, (slotH - KeyH) * .5f);
            var plate = UIKit.Img(name, parent, Art.S("Ui/" + pill), Color.white, new Vector2(width, KeyH),
                                  anchor, centre);
            plate.raycastTarget = false;

            return Dress(plate.transform, width, slotH, caption, art, PlateCaptionLeft, ink ?? Pal.Cream);
        }

        /// <summary>
        /// The picture standing on the pill's foot at its right end, and the caption on two lines
        /// in the room left of it. Shared by the door and the plate so the two cannot drift (44d).
        /// </summary>
        static Text Dress(Transform key, float width, float slotH, string caption, string art, float captionLeft,
                          Color ink)
        {
            float lift = KeyH * UIKit.PillFaceLift;

            // The picture, right, standing on the key's foot. Read off the sprite rather than
            // typed, so a re-cut at another aspect keeps its shape; an address that has not
            // arrived draws nothing rather than a white box (7b).
            var picture = Art.S("Ui/" + art);
            float aspect = picture != null && picture.rect.height > 0f ? picture.rect.width / picture.rect.height : 1f;
            float artH = Mathf.Min(Mathf.Min(ArtMost, slotH - 6f), ArtWidest / aspect), artW = artH * aspect;
            var img = UIKit.Img("Art", key, picture, Color.white, new Vector2(artW, artH),
                                new Vector2(1f, 0f), new Vector2(-ArtRight - artW * .5f, artH * .5f + 4f));
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = picture != null;

            // The caption, left, on two lines in the room between the corner and the picture.
            float room = width - ArtRight - artW - 20f - captionLeft;
            var text = UIKit.Shrinkable(
                UIKit.Titled("Caption", key, TwoLines(caption.Upper()), 44,
                             ink, TextAnchor.MiddleCenter, new Vector2(room, KeyH * .82f),
                             new Vector2(0f, .5f), new Vector2(captionLeft + room * .5f, lift), 3f, 3f,
                             wrap: true),
                26);
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// A caption broken onto two lines at the space nearest its middle, so "DAILY CHALLENGES"
        /// and "REFER A FRIEND" both read as two balanced lines, and a one-word translation stays
        /// one line.
        /// </summary>
        public static string TwoLines(string s)
        {
            int best = -1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == ' ' && (best < 0 || Mathf.Abs(i - s.Length * .5f) < Mathf.Abs(best - s.Length * .5f)))
                    best = i;
            return best > 0 ? s.Substring(0, best) + "\n" + s.Substring(best + 1) : s;
        }
    }
}
