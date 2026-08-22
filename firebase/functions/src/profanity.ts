/**
 * Glimmer Grove — what a keeper's name may say.
 *
 * `grove.ts` decides what a name may *contain* structurally: no bidirectional overrides, no
 * zero-width characters, sixteen characters, two of them visible. That is a rule about
 * drawing a row. This file is the rule about the words in it, and it is a different problem
 * with a different failure mode — a name that draws perfectly and is a slur.
 *
 * ## Why the fold is the feature and the list is not
 *
 * The obvious implementation is a list of words and `includes`. That is what shipped, and it
 * was weaker than it looked in three separate ways, all of which are one-keystroke bypasses:
 *
 * - **Leetspeak walked past it.** `f_u_c_k` was caught, because separators were stripped, but
 *   `fvck`, `5hit`, `f4ggot` and `phuck` were not.
 * - **One non-Latin character defeated it entirely.** The old squash *deleted* everything
 *   outside `a-z0-9` rather than folding it, so a Cyrillic `с` in `fuсk` was removed and the
 *   remainder — `fuk` — matched nothing.
 * - **Every non-Latin script was unfiltered, always.** A name written in Cyrillic, Arabic,
 *   Greek or kana squashed to the empty string and passed every test. For a game that ships
 *   globally that is not a corner case; it is most of the world.
 *
 * So the work is in the *fold*: reduce a name and every list entry to the same canonical form
 * first, and only then compare. A bigger list closes no bypass, and the whole industry's
 * experience is that a hand-grown list is the layer that matters least. What actually holds
 * the line is this fold, plus reporting and takedown (`reports.ts`), plus the fact that a
 * refused name is published as a generated handle rather than rejected — so the cost of a
 * false positive is a quieter board row and never a player who cannot name themselves.
 *
 * ## The four forms, and why there is more than one
 *
 * A single canonical form cannot serve both jobs, because the two jobs pull in opposite
 * directions. Mapping Cyrillic `а` to Latin `a` is exactly right when catching an English
 * slur spelled in lookalikes, and exactly wrong when comparing two genuinely Russian words —
 * it turns them into gibberish that no Russian list entry will match. So a name is reduced
 * four ways and matched against all of them:
 *
 * - {@link NameForms.plain} — compatibility normalisation, case fold, letters and digits
 *   only. Scripts survive intact. This is what makes the non-Latin half of the list work.
 * - {@link NameForms.base} — `plain`, plus combining marks stripped, plus the letters that
 *   *look* Latin folded onto Latin (Cyrillic, Greek, and the stroke letters like `ø` that
 *   normalisation leaves alone). Digits stay digits.
 * - {@link NameForms.loose} — `base`, plus the leetspeak classes collapsed: `{i l 1 | !}`
 *   become one character, as do `{o 0}`, `{s 5 $}`, `{a 4 @}` and the rest. This is the form
 *   that catches `s1ut`, `f4ggot` and `ph uck`.
 * - **squeezed** variants of the last two, in which a run of one character becomes one
 *   character, so `fuuuck` and `shiiiit` reduce onto their targets.
 *
 * Every list entry is folded by the *same* functions at load time, which is the only reason
 * any of this works across scripts: a Russian entry and a Russian name are mangled
 * identically, so they still match each other.
 *
 * ## Three classes, because one matching rule cannot be both safe and useful
 *
 * Matching a list anywhere inside a name is what catches `xXn1gg3rXx`. It is also what
 * blocked **Grapevine** in a gardening game, because `rape` is a substring of it — reported,
 * real, and live for a year. Matching only whole names is safe and catches almost nothing.
 * So the list is split by how it is matched rather than by what it means:
 *
 * - {@link NameBlocklist.anywhere} — a short, curated set of terms that essentially never
 *   occur inside an innocent word, matched as a substring. Every addition here is a
 *   false-positive risk and the seeder refuses entries under four characters.
 * - {@link NameBlocklist.exact} — the large vendored multilingual set, matched against the
 *   whole name and against each of its words. A whole-name match cannot have a false
 *   positive by construction, which is what makes it safe to be thousands of entries long.
 * - {@link NameBlocklist.reserved} — impersonation. Matched as a substring like `anywhere`,
 *   and kept a separate class so a refusal can say which it was. It is a substring class and
 *   not a whole-name one because the abuse is `AdminFern` rather than `Admin`, and because a
 *   shipped vector already asserted that `Fernadminmoss` is refused — a prior decision, and
 *   the right one. It pays for that with the same allowlist `anywhere` uses: `Badminton`,
 *   `Stafford` and `Systemic` are all rescued there.
 *
 * {@link NameBlocklist.allow} rescues the `anywhere` class: an allowed word is *cut out* of
 * the haystack before the substring test runs, so `grapevine` is not searched for `rape` at
 * all. That is the standard repair for the Scunthorpe problem and it is why `anywhere` can
 * hold `rape` and `cunt` at all.
 *
 * ## What is deliberately not here
 *
 * No client copy. A list shipped in a client is a list read out of the client, and so is a
 * fold — arguably the fold is worth more to somebody trying to defeat it, since it says
 * exactly what normalisation to route around. `GroveNames` mirrors the structural rules so
 * the rename panel can preview what will be published, and stops there.
 *
 * No stemming, no edit distance, no classifier. Each would trade a false-negative rate we
 * can measure for a false-positive rate we cannot, on the one string a player picked for
 * themselves.
 */

// ------------------------------------------------------------------- the tables

/**
 * Letters that are *shaped* like Latin letters, folded onto them.
 *
 * **Shape, not sound, and that distinction is the whole table.** A transliteration would map
 * Cyrillic `ф` to `f`, which is right phonetically and useless here — nobody spells `fuck`
 * with `ф`, because it does not look like an `f`. They spell it with `с` for `c` and `а` for
 * `a`, which are pixel-identical in most faces. So every row below is a homoglyph somebody
 * would actually reach for.
 *
 * Case folding runs *before* this, so only the lowercase forms need entries — the uppercase
 * Cyrillic `Н` that reads as `H` arrives here as `н`, which reads as `h`, and that is the
 * mapping given.
 *
 * Compatibility normalisation has already dealt with the fullwidth forms, the mathematical
 * alphabets, the circled letters and the superscripts, so none of those appear here. What
 * *is* here beyond the two big alphabets are the Latin letters carrying a stroke or a hook —
 * `ø ł đ ħ ŧ ı ſ ƒ ĸ` — because a stroke is not a combining mark and decomposition leaves
 * them exactly as they were.
 */
const HOMOGLYPHS: Record<string, string> = {
  // Cyrillic
  "а": "a", "ь": "b", "с": "c", "ԁ": "d", "е": "e", "ғ": "f", "ԍ": "g", "һ": "h",
  "н": "h", "і": "i", "ї": "i", "ј": "j", "к": "k", "ӏ": "l", "м": "m", "п": "n",
  "о": "o", "р": "p", "ԛ": "q", "г": "r", "ѕ": "s", "т": "t", "ц": "u", "ѵ": "v",
  "ԝ": "w", "ѡ": "w", "х": "x", "у": "y", "ү": "y", "з": "3", "ч": "4", "б": "6",

  // Greek
  "α": "a", "β": "b", "ϲ": "c", "δ": "d", "ε": "e", "ζ": "z", "η": "n", "θ": "o",
  "ι": "i", "κ": "k", "λ": "l", "μ": "u", "ν": "v", "ξ": "e", "ο": "o", "π": "n",
  "ρ": "p", "ς": "s", "σ": "o", "τ": "t", "υ": "u", "φ": "o", "χ": "x", "ψ": "y",
  "ω": "w",

  // Latin letters whose difference from ASCII is a stroke or a hook, which decomposition
  // does not remove because a stroke is not a combining mark.
  "ø": "o", "ł": "l", "đ": "d", "ħ": "h", "ŧ": "t", "ı": "i", "ſ": "s", "ƒ": "f",
  "ĸ": "k", "ɩ": "i", "ɑ": "a", "ɡ": "g", "ɛ": "e", "ɔ": "c", "ʀ": "r", "ʏ": "y",
  "ʙ": "b", "ᴀ": "a", "ᴄ": "c", "ᴅ": "d", "ᴇ": "e", "ᴊ": "j", "ᴋ": "k", "ᴍ": "m",
  "ᴏ": "o", "ᴘ": "p", "ᴛ": "t", "ᴜ": "u", "ᴠ": "v", "ᴢ": "z",
};

/**
 * The multi-character expansions, which have to run as string replacements rather than per
 * character.
 *
 * `ß` and the ligatures are the same two facts `names.ts` closes for a different reason: the
 * two runtimes disagree about them. Here it does not matter that they disagree, only that
 * `Scheiße` and `Scheisse` reach the list as one word.
 */
const EXPANSIONS: [RegExp, string][] = [
  [/ß/gu, "ss"], [/æ/gu, "ae"], [/œ/gu, "oe"], [/þ/gu, "th"], [/ð/gu, "d"],
  [/ĳ/gu, "ij"], [/ﬀ/gu, "ff"], [/ﬁ/gu, "fi"], [/ﬂ/gu, "fl"], [/ﬃ/gu, "ffi"],
  [/ﬄ/gu, "ffl"], [/ﬅ/gu, "st"], [/ﬆ/gu, "st"],
];

/**
 * The leetspeak classes: characters that stand in for a letter often enough to be worth
 * collapsing onto it.
 *
 * **Applied only to {@link NameForms.loose}, and that separation is load-bearing.** Collapsing
 * `1` onto `i` is what catches `s1ut`; it is also what would turn `Anal1` into `anali` and
 * make the whole-name test for `anal` miss. So the un-collapsed {@link NameForms.base} is
 * kept and matched too, and between them both spellings are caught.
 *
 * `l` and `i` end up on the same character deliberately. They are the pair an attacker
 * actually exploits — `1`, `l`, `I`, `|` and `!` are one glyph in most faces — and no
 * legitimate word survives being told them apart anyway once `1` is in play.
 */
const LEET: Record<string, string> = {
  "0": "o", "1": "i", "3": "e", "4": "a", "5": "s", "6": "g", "7": "t", "8": "b",
  "9": "g", "2": "z",
  "@": "a", "$": "s", "!": "i", "|": "i", "+": "t", "(": "c", ")": "o", "[": "c",
  "]": "o", "{": "c", "}": "o", "<": "c", ">": "o", "*": "o", "#": "h", "&": "a",
  "¢": "c", "€": "e", "£": "l", "×": "x",
  "l": "i", "v": "u", "q": "g",
};

/** Digraphs an attacker substitutes wholesale. Applied to `loose` before the per-character map. */
const LEET_DIGRAPHS: [RegExp, string][] = [
  [/ph/gu, "f"], [/vv/gu, "w"], [/\|\|/gu, "u"],
];

/**
 * The longest folded form kept.
 *
 * A name is sixteen characters, but compatibility normalisation expands — U+3390 is one
 * character and folds to four — so the fold has to be bounded independently of the name. It
 * bounds a string used for comparison and nothing else.
 */
export const MAX_FOLD_LENGTH = 64;

// -------------------------------------------------------------------- the folds

const COMBINING = /\p{Mn}/gu;
const LETTER_OR_DIGIT = /[\p{L}\p{Nd}]/u;
const WORD_BREAK = /[\s\p{P}\p{S}]+/u;

function normalise(text: string, form: "NFKC" | "NFKD"): string {
  try {
    return text.normalize(form);
  } catch {
    // Unreachable for anything `sanitiseName` lets through, which has already dropped the
    // lone surrogates that are the only way `normalize` throws. Folding what we have beats
    // refusing the name.
    return text;
  }
}

/**
 * Case folding, with the one context-sensitive mapping closed by hand.
 *
 * Lowercasing produces final sigma at the end of a Greek word and medial sigma everywhere
 * else, so the same word folds two ways depending on where it sits. They are one letter and
 * a player would not accept that moving a name's last letter makes it a different name.
 */
function lower(text: string): string {
  return text.toLowerCase().replace(/ς/gu, "σ");
}

function keepLettersAndDigits(text: string): string {
  let out = "";

  for (const ch of text) {
    if (!LETTER_OR_DIGIT.test(ch)) continue;
    if (out.length >= MAX_FOLD_LENGTH) break;
    out += ch;
  }

  return out;
}

function applyHomoglyphs(text: string): string {
  let out = "";
  for (const ch of text) out += HOMOGLYPHS[ch] ?? ch;
  return out;
}

function applyExpansions(text: string): string {
  let out = text;
  for (const [pattern, replacement] of EXPANSIONS) out = out.replace(pattern, replacement);
  return out;
}

function applyLeet(text: string): string {
  let out = text;
  for (const [pattern, replacement] of LEET_DIGRAPHS) out = out.replace(pattern, replacement);

  let mapped = "";
  for (const ch of out) mapped += LEET[ch] ?? ch;
  return mapped;
}

/** A run of one character becomes one character: `fuuuck` reduces onto `fuck`. */
export function squeeze(text: string): string {
  let out = "";
  let previous = "";

  for (const ch of text) {
    if (ch === previous) continue;
    out += ch;
    previous = ch;
  }

  return out;
}

/** Digits removed, so `Anal1` still tests as `anal` against the whole-name class. */
function lettersOnly(text: string): string {
  return text.replace(/\p{Nd}/gu, "");
}

/**
 * Every form of a name that is compared against the list.
 *
 * Built once per name and once per list entry, by the same function, which is the only
 * reason a Russian entry matches a Russian name — both are mangled identically.
 */
export interface NameForms {
  /** Normalised and case folded, scripts intact. Carries the non-Latin half of the list. */
  plain: string;

  /** `plain` with marks stripped and lookalike letters folded onto Latin. */
  base: string;

  /** `base` with the leetspeak classes collapsed. */
  loose: string;

  /** The words of the name, in `base` form, for the whole-word classes. */
  words: string[];
}

/**
 * Reduces a name to the forms the list is compared against.
 *
 * Takes a name that has already been through `sanitiseName`; it does no bounding of its own
 * beyond {@link MAX_FOLD_LENGTH}, because the structural rules are a different file's job and
 * duplicating them here would be a second place for them to drift.
 */
export function foldName(visible: string): NameForms {
  if (typeof visible !== "string" || visible.length === 0) {
    return { plain: "", base: "", loose: "", words: [] };
  }

  const expanded = applyExpansions(lower(normalise(visible, "NFKC")));
  const plain = keepLettersAndDigits(expanded);

  // Decomposition then mark-stripping is what removes the diacritics, and it is done on the
  // *expanded* text rather than the finished `plain` so that a stroke letter and an accented
  // letter are both handled before anything is dropped.
  const stripped = normalise(expanded, "NFKD").replace(COMBINING, "");
  const base = keepLettersAndDigits(applyHomoglyphs(stripped));
  const loose = keepLettersAndDigits(applyLeet(applyHomoglyphs(stripped)));

  // Words are taken from the text *before* separators are dropped, because that is the only
  // point at which the separators still exist. `Fern Bimbo` has to test its second word.
  const words: string[] = [];
  for (const word of applyHomoglyphs(stripped).split(WORD_BREAK)) {
    const folded = keepLettersAndDigits(word);
    if (folded.length > 0 && words.length < 16) words.push(folded);
  }

  return { plain, base, loose, words };
}

// ------------------------------------------------------------------- the list

/** The list as it is authored and published: plain words, in classes. */
export interface NameBlocklist {
  /** Bumped by the seeder. Diagnostic only — nothing branches on it. */
  version: number;

  /** Matched as a substring. Short, curated, and every entry is a false-positive risk. */
  anywhere: string[];

  /** Matched against the whole name and each of its words. The large vendored set. */
  exact: string[];

  /** Impersonation, matched like `exact`. Separate so a refusal can say which it was. */
  reserved: string[];

  /** Cut out of the haystack before the `anywhere` test. The Scunthorpe repair. */
  allow: string[];
}

/** The list with every entry pre-folded. Built once per instance and cached. */
/** One folded entry of a substring class. */
export interface PreparedWord {
  word: string;
  base: string;
  loose: string;
}

export interface PreparedBlocklist {
  version: number;
  anywhere: PreparedWord[];

  /** Substring, like {@link anywhere}, and reported as its own kind. */
  reserved: PreparedWord[];

  /**
   * Every folded form of every whole-word entry, mapped back to **the word as it was
   * written**.
   *
   * A set would do to answer "is this refused", and that is what this was. It is a map
   * because the refusal now reaches a log, and a log needs to name the entry that fired so a
   * bad one can be found — where a set could only give back the folded player name that
   * matched it, which is a different string with a different owner and no business being in
   * an operations log.
   */
  exact: Map<string, string>;
  allow: { base: string; loose: string }[];
}

/** Why a name was refused. */
export type NameRefusal = "anywhere" | "exact" | "reserved";

export interface NameVerdict {
  allowed: boolean;

  /** The list entry that matched, for the log. Never shown to a player. */
  word?: string;

  kind?: NameRefusal;
}

const ALLOWED: NameVerdict = { allowed: true };

/**
 * Folds every entry once, so a claim does no normalisation work beyond the name itself.
 *
 * Entries that fold to nothing are dropped rather than kept as empty strings, which would
 * match every name ever — the one way a bad list entry could take the whole game's names
 * down, and cheap to make unrepresentable here rather than trusting the seeder alone.
 */
export function prepareBlocklist(list: NameBlocklist): PreparedBlocklist {
  const substrings = (words: string[] | undefined): PreparedWord[] => {
    const out: PreparedWord[] = [];

    for (const word of words ?? []) {
      const forms = foldName(word);
      if (forms.base.length === 0 || forms.loose.length === 0) continue;
      out.push({ word, base: forms.base, loose: forms.loose });
    }

    return out;
  };

  const anywhere = substrings(list.anywhere);
  const reserved = substrings(list.reserved);

  const allow: PreparedBlocklist["allow"] = [];
  for (const word of list.allow ?? []) {
    const forms = foldName(word);
    if (forms.base.length === 0) continue;
    allow.push({ base: forms.base, loose: forms.loose });
  }

  // Longest first, so cutting `grapevine` out of a name happens before `grape` would cut half
  // of it and leave `vine` welded to whatever followed.
  allow.sort((a, b) => b.base.length - a.base.length);

  const fold = (words: string[] | undefined): Map<string, string> => {
    const map = new Map<string, string>();

    for (const word of words ?? []) {
      const forms = foldName(word);

      // First writer wins, so the shortest, plainest spelling of a word that folds several
      // ways is the one a log names.
      for (const form of [forms.plain, forms.base, forms.loose]) {
        if (form.length > 0 && !map.has(form)) map.set(form, word);
      }
    }

    return map;
  };

  return {
    version: Math.floor(list.version ?? 0),
    anywhere,
    reserved,
    exact: fold(list.exact),
    allow,
  };
}

/**
 * Removes the allowed words from a haystack before the substring test.
 *
 * Replaced with a space rather than deleted, because deleting joins what was either side of
 * the removal and can manufacture a match that was never in the name: cutting `grape` out of
 * a name ending `...ther` + `grape` + `ist...` would weld `therapist` together out of two
 * innocent halves. A character the fold can never produce is a wall.
 *
 * **The same transform is applied to the entry as to the haystack, and that is not symmetry
 * for its own sake.** The squeezed haystack is where it bites: `Shiiiitake` does not contain
 * `shiitake`, so carving the un-squeezed form leaves it alone, and squeezing afterwards
 * produces `shitake` -- which contains `shit`. Carving the *squeezed* haystack with the
 * *squeezed* allowance is what closes that, and it is why this takes a transform rather than
 * a flag.
 */
function carve(
  haystack: string,
  allow: { base: string; loose: string }[],
  pick: (entry: { base: string; loose: string }) => string
): string {
  let out = haystack;

  for (const entry of allow) {
    const word = pick(entry);
    if (word.length === 0) continue;
    if (!out.includes(word)) continue;

    out = out.split(word).join(" ");
  }

  return out;
}

export function judgeName(visible: string, list: PreparedBlocklist): NameVerdict {
  const forms = foldName(visible);
  if (forms.base.length === 0 && forms.plain.length === 0) return ALLOWED;

  // Whole name and per word, against every form. A whole-string comparison cannot have a
  // false positive, which is what lets this class be thousands of entries long.
  const candidates = [
    forms.plain, forms.base, forms.loose,
    lettersOnly(forms.base), lettersOnly(forms.plain),
    ...forms.words,
  ];

  for (const candidate of candidates) {
    if (candidate.length === 0) continue;

    // The entry, never the candidate. They are the same string in folded form for most
    // matches, and the candidate is the *player's name* whenever they are not.
    const entry = list.exact.get(candidate);
    if (entry !== undefined) return { allowed: false, word: entry, kind: "exact" };
  }

  // Substring, on the two aggressive forms and their squeezed variants, with the allowed
  // words carved out of each. Each haystack carries its own transform so the allowance is
  // reduced exactly as far as the text it is being cut out of -- see `carve`.
  const haystacks = [
    carve(forms.base, list.allow, (e) => e.base),
    carve(squeeze(forms.base), list.allow, (e) => squeeze(e.base)),
    carve(forms.loose, list.allow, (e) => e.loose),
    carve(squeeze(forms.loose), list.allow, (e) => squeeze(e.loose)),
  ];

  // Impersonation first, so `Admin` is reported as what it is rather than as whichever class
  // happens to be searched first. The two classes cannot disagree about the answer, only about
  // the reason, and the reason is what a moderator reads.
  const found = search(haystacks, list.reserved, "reserved")
    ?? search(haystacks, list.anywhere, "anywhere");

  if (found) return found;

  return ALLOWED;
}

/** The substring pass, over already-carved haystacks. */
function search(
  haystacks: string[], entries: PreparedWord[], kind: NameRefusal
): NameVerdict | null {
  for (const entry of entries) {
    // A haystack is squeezed, so the needle has to be too — `fuuuck` reduces to `fuck`, but an
    // entry that itself carries a double letter (`bollocks`) reduces to `bolocks` and would
    // never be found in a squeezed haystack otherwise.
    const needles = [entry.base, entry.loose, squeeze(entry.base), squeeze(entry.loose)];

    for (const haystack of haystacks) {
      if (haystack.length === 0) continue;

      for (const needle of needles) {
        if (needle.length === 0) continue;
        if (haystack.includes(needle)) return { allowed: false, word: entry.word, kind };
      }
    }
  }

  return null;
}
