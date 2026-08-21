using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Where the groves this device is <em>not</em> playing right now are kept.
    ///
    /// <para>
    /// <b>Why this exists.</b> Switching accounts used to be a replacement: the local save was
    /// wiped and the incoming account's grove was downloaded over it. That makes the switch a
    /// network operation with no undo, and every step after the sign-in a place it can strand
    /// somebody — which is exactly what shipped. A player switching between two of their own
    /// Google accounts met "signed in, but that grove could not be loaded", then a panel saying
    /// their phone was signed in as somebody else, then a destructive prompt offering to
    /// discard twenty-six glades. Every one of those sentences was produced by a failure to
    /// read one document at the single most fragile moment in an app's life: the frame after it
    /// returns from an OAuth browser, when the process has just been foregrounded and the
    /// Firestore stream has just been re-authenticated.
    /// </para>
    /// <para>
    /// <b>The fix is to stop making the switch depend on that read.</b> A grove that is leaving
    /// is copied here first, and a grove that is arriving is restored from here if this device
    /// has seen it before. Both are local, both are instant, and neither can fail for a reason
    /// the player has to understand. The server copy is then folded in by the ordinary sync — a
    /// pull, a monotonic join and a push, retried with a backoff like every other sync — so a
    /// switch made in a lift finishes as a switch and catches up when the doors open, rather
    /// than reporting an error about a grove that was never in danger.
    /// </para>
    /// <para>
    /// <b>What it is not.</b> It is not a second source of truth and nothing merges across it.
    /// One archive is one account's whole save file, restored wholesale by
    /// <see cref="SaveService.SwitchTo"/> and never joined with another; the identity travels
    /// inside the file, so a slot that does not name the account being asked for is discarded
    /// rather than adopted. <c>AccountGate</c> is unchanged and still guards every push. What
    /// changes is that its refusal now has a local repair — archive what is here, restore what
    /// the session is — instead of being a dead end the player has to read about.
    /// </para>
    /// <para>
    /// It is deliberately a cache, not a backup. Evicting the least recently used slot loses a
    /// copy and never a grove: the switch that filled it pushed it to the server first, which
    /// is the one step of the old design most worth keeping.
    /// </para>
    /// </summary>
    public interface IAccountArchive
    {
        /// <summary>Whether a grove for this account is on this device.</summary>
        bool Has(string userId);

        /// <summary>
        /// The archived grove, or null. Reading does not remove it — the caller deletes it
        /// only once it has been adopted, so a process death between the two costs nothing.
        /// </summary>
        SaveFileDto Read(string userId);

        /// <summary>Puts a grove away under the account it belongs to.</summary>
        bool Stash(string userId, SaveFileDto dto);

        /// <summary>Drops a slot, because it has been restored or is being replaced.</summary>
        void Forget(string userId);
    }

    /// <summary>
    /// The archive on disk: one folder per account under <c>accounts/</c>, each holding an
    /// ordinary save file.
    ///
    /// <para>
    /// It reuses <see cref="SaveStore"/> rather than writing JSON itself, and that is the whole
    /// design of this class. The atomic write, the backup rotation, the corrupt-file recovery
    /// and the checksum are the parts of persistence that are hard to get right, they are
    /// already right, and they are already tested against a real filesystem in
    /// <c>SaveStoreTests</c>. A second copy of them would be a second thing to get wrong —
    /// invariant 5b's lesson in the file with the least reason to relearn it.
    /// </para>
    /// <para>
    /// The folder name is a hash of the account id rather than the id itself. A uid is an
    /// opaque token from a provider and this build must never be the reason one of them turns
    /// out not to be a legal path segment; the id is stored inside the file anyway, which is
    /// what <see cref="Read"/> checks, so a hash collision reads as an absent slot rather than
    /// as somebody else's grove.
    /// </para>
    /// </summary>
    public sealed class AccountArchiveStore : IAccountArchive
    {
        /// <summary>
        /// How many groves this device keeps a copy of.
        ///
        /// Bounded because it is a cache and a device shared by a family is an ordinary thing.
        /// Six is generous against the real use — a player and their second account — and an
        /// eviction costs a copy rather than a grove, because a switch pushes the outgoing
        /// grove to the server before it fills a slot.
        /// </summary>
        public const int MaxArchived = 6;

        readonly string _root;

        public AccountArchiveStore(string root = null)
            => _root = root ?? Path.Combine(Application.persistentDataPath, "accounts");

        public bool Has(string userId)
            => !string.IsNullOrEmpty(userId) && new SaveStore(Folder(userId)).Exists;

        public SaveFileDto Read(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;

            var slot = new SaveStore(Folder(userId));
            if (!slot.Exists) return null;

            var dto = slot.Load();

            // The identity travels inside the file, so this is what makes the hash safe: a
            // slot that does not name the account being asked for is not this player's grove
            // however it came to be there, and adopting it would be the one mistake this whole
            // subsystem exists to make unreachable.
            if (dto == null || dto.cloud == null ||
                !string.Equals(dto.cloud.userId, userId, StringComparison.Ordinal))
            {
                Debug.LogWarning("[Save] an archived grove did not name the account it was filed " +
                                 "under; ignoring it");
                return null;
            }

            return dto;
        }

        public bool Stash(string userId, SaveFileDto dto)
        {
            if (string.IsNullOrEmpty(userId) || dto == null) return false;

            // Written into the copy rather than assumed from the folder. The file is the
            // record; a slot whose contents disagree with its name is discarded on the way
            // back in, and this is what stops that ever being the ordinary case.
            dto.cloud ??= new CloudStateDto();
            dto.cloud.userId = userId;

            try
            {
                string folder = Folder(userId);
                Directory.CreateDirectory(folder);

                if (!new SaveStore(folder).Save(dto)) return false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] could not archive a grove: " + e.Message);
                return false;
            }

            Evict();
            return true;
        }

        public void Forget(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;
            Remove(Folder(userId));
        }

        // ------------------------------------------------------------- internals
        string Folder(string userId) => Path.Combine(_root, Key(userId));

        /// <summary>
        /// FNV-1a over the id's UTF-8 bytes, in hex. The same hash the chest roll uses, for the
        /// same reason: it is short, it needs no dependency, and it is stable across runtimes
        /// and builds — a folder name that changed with the .NET version would orphan every
        /// archive on the device the day the engine was upgraded.
        /// </summary>
        internal static string Key(string userId)
        {
            const ulong Offset = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong hash = Offset;
            foreach (byte b in Encoding.UTF8.GetBytes(userId ?? string.Empty))
            {
                hash ^= b;
                hash *= Prime;
            }

            return hash.ToString("x16");
        }

        /// <summary>
        /// Keeps the newest <see cref="MaxArchived"/> slots and drops the rest.
        ///
        /// Ordered by the folder's own write time rather than by an index file. An index would
        /// be a second thing to keep in step with the directory and a third thing that can be
        /// corrupt, to decide something whose worst outcome is that a second account downloads
        /// its grove again.
        /// </summary>
        void Evict()
        {
            try
            {
                if (!Directory.Exists(_root)) return;

                var folders = new List<string>(Directory.GetDirectories(_root));
                if (folders.Count <= MaxArchived) return;

                folders.Sort((a, b) => Directory.GetLastWriteTimeUtc(b)
                                                .CompareTo(Directory.GetLastWriteTimeUtc(a)));

                for (int i = MaxArchived; i < folders.Count; i++) Remove(folders[i]);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] could not tidy the account archive: " + e.Message);
            }
        }

        static void Remove(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception e) { Debug.LogWarning("[Save] could not remove an archived grove: " + e.Message); }
        }
    }

    /// <summary>
    /// An archive that keeps nothing.
    ///
    /// The default, so that <see cref="SaveService"/> is never holding null and a caller that
    /// forgot to supply one degrades to the old behaviour — a switch that downloads — rather
    /// than throwing. Tests that are not about the archive use it too.
    /// </summary>
    public sealed class NullAccountArchive : IAccountArchive
    {
        public bool Has(string userId) => false;
        public SaveFileDto Read(string userId) => null;
        public bool Stash(string userId, SaveFileDto dto) => true;
        public void Forget(string userId) { }
    }
}
