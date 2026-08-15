/**
 * The deterministic generator behind every reward the server has to recompute.
 *
 * This is the TypeScript half of the client's `ChestRandom`, and it is a contract rather
 * than a utility. Two rewards rest on it — a daily chest's contents and a glade's golden
 * multiplier — and in both cases the client shows the player a number that the server then
 * derives independently from the same inputs. If the two ever disagree, a player watches
 * their balance change after a sync, which is the worst thing an economy can do in front of
 * somebody and the hardest to explain afterwards.
 *
 * It lives in a file of its own so there is exactly one copy of it on this side, matching
 * the single `ChestRandom` on the client. It used to sit inside `daily.ts`; a second reward
 * needing the same sequence is precisely the moment a private copy becomes two copies that
 * drift.
 *
 * Every constant here is part of the contract: the FNV basis and prime, the xorshift
 * amounts, the byte order, the decimal spelling of integers and the plain modulo. Changing
 * any of them re-rolls every unopened chest and every glade in the world. See invariant 9c
 * and `firebase/shared/reward-vectors.json`, which both sides run as a test.
 *
 * Everything is 32-bit integer arithmetic, which JavaScript does exactly. Anything wider
 * would land inside the 53-bit mantissa and the two sides would agree on most inputs and
 * not all — the worst possible failure mode for money.
 */

const FNV_OFFSET_BASIS = 2166136261;
const FNV_PRIME = 16777619;

function absorbByte(hash: number, byte: number): number {
  return Math.imul(hash ^ (byte & 0xff), FNV_PRIME) >>> 0;
}

/** One UTF-16 code unit, low byte first. Spelled out so a non-ASCII key cannot diverge. */
function absorbChar(hash: number, code: number): number {
  return absorbByte(absorbByte(hash, code & 0xff), (code >> 8) & 0xff);
}

function absorbString(hash: number, text: string): number {
  let h = hash;
  for (let i = 0; i < text.length; i++) h = absorbChar(h, text.charCodeAt(i));
  return h;
}

/** Decimal digits, most significant first — the form a human would write. */
function absorbInt(hash: number, value: number): number {
  let h = hash;
  let v = Math.trunc(value);

  if (v < 0) {
    h = absorbChar(h, 45);                        // '-'
    v = -v;
  }

  let divisor = 1;
  while (Math.floor(v / divisor) >= 10) divisor *= 10;

  while (divisor > 0) {
    h = absorbChar(h, 48 + (Math.floor(v / divisor) % 10));
    divisor = Math.floor(divisor / 10);
  }

  return h;
}

/** xorshift32's one fixed point is zero, and it would return zero forever. */
function safe(hash: number): number {
  return hash === 0 ? FNV_OFFSET_BASIS >>> 0 : hash;
}

/** The chest seeding: who owns it, which day, which of that day's chests, which stream. */
export function chestSeed(playerKey: string, dayKey: number, chestIndex: number,
                          stream: number): number {
  let hash = FNV_OFFSET_BASIS >>> 0;

  hash = absorbString(hash, playerKey ?? "");
  hash = absorbChar(hash, 124);                   // '|'
  hash = absorbInt(hash, dayKey);
  hash = absorbChar(hash, 124);
  hash = absorbInt(hash, chestIndex);
  hash = absorbChar(hash, 124);
  hash = absorbInt(hash, stream);

  return safe(hash);
}

/**
 * The subject seeding: a reward keyed to a *thing* rather than to a date. The golden
 * bonus is the first, seeded from a level id.
 *
 * Deliberately a different layout from `chestSeed` rather than the same one with the id
 * stringified, so no chest seed and no subject seed can collide by coincidence and quietly
 * correlate two tables that were tuned independently.
 */
export function subjectSeed(playerKey: string, tag: string, subject: string,
                            stream: number): number {
  let hash = FNV_OFFSET_BASIS >>> 0;

  hash = absorbString(hash, playerKey ?? "");
  hash = absorbChar(hash, 124);
  hash = absorbString(hash, tag ?? "");
  hash = absorbChar(hash, 124);
  hash = absorbString(hash, subject ?? "");
  hash = absorbChar(hash, 124);
  hash = absorbInt(hash, stream);

  return safe(hash);
}

export class Rolls {
  private state: number;

  constructor(seed: number) {
    this.state = seed >>> 0;
  }

  /** xorshift32, exactly as Marsaglia gave it. */
  next(): number {
    let x = this.state;
    x = (x ^ (x << 13)) >>> 0;
    x = (x ^ (x >>> 17)) >>> 0;
    x = (x ^ (x << 5)) >>> 0;
    this.state = x;
    return x;
  }

  /**
   * A value in [0, bound). Plain modulo, with the bias that implies — at most one part in
   * 2^32 / bound, against bounds in the low hundreds, so far below the resolution of any
   * odds a player or a regulator is shown. Rejection sampling would remove it and add a
   * loop whose iteration count both implementations would have to match exactly, which is
   * a real risk traded for an imaginary one.
   */
  below(bound: number): number {
    return bound <= 1 ? 0 : this.next() % bound;
  }

  between(min: number, max: number): number {
    return max <= min ? min : min + this.below(max - min + 1);
  }
}
