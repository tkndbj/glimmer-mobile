using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The one place a device-local preference is written.
    ///
    /// <para>
    /// Both halves of its contract are the kind that rot silently. Drop the flush and the
    /// preference stops sticking for every player whose app is killed while backgrounded
    /// rather than quit cleanly — which on a phone is most of them, and it looks like the
    /// feature never worked rather than like a bug. Drop the comparison and a screen
    /// transition serialises the whole store to disk twice for no reason, which nothing
    /// anywhere would ever report.
    /// </para>
    /// <para>
    /// Reaches <c>PlayerPrefs</c>, so it runs in the Editor's Test Runner rather than offline.
    /// </para>
    /// </summary>
    public sealed class DevicePrefsTests
    {
        const string Key = "glimmer_test_deviceprefs";

        [SetUp]
        public void Clear() => Tidy();

        [TearDown]
        public void Tidy()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        [Test]
        public void AValueThatWasNotThereIsWritten()
        {
            Assert.IsTrue(DevicePrefs.WriteString(Key, "c02_two"));
            Assert.AreEqual("c02_two", PlayerPrefs.GetString(Key, string.Empty));
        }

        [Test]
        public void WritingWhatIsAlreadyStoredDoesNothing()
        {
            DevicePrefs.WriteString(Key, "c02_two");

            // The point of the whole class: a map arrival writes its mode and its chapter every
            // single time, and neither has usually changed.
            Assert.IsFalse(DevicePrefs.WriteString(Key, "c02_two"),
                           "an unchanged preference must not reach the disk");
            Assert.AreEqual("c02_two", PlayerPrefs.GetString(Key, string.Empty));
        }

        [Test]
        public void AChangedValueStillGetsThrough()
        {
            DevicePrefs.WriteString(Key, "c02_two");

            Assert.IsTrue(DevicePrefs.WriteString(Key, "c01_one"));
            Assert.AreEqual("c01_one", PlayerPrefs.GetString(Key, string.Empty));
        }

        [Test]
        public void AnEmptyValueOverAnAbsentKeyIsStillWritten()
        {
            // GetString's own default is the empty string, so comparing on value alone would
            // read "absent" as "already empty" and skip the write, leaving the key absent. The
            // contract is that the store reads back what was asked for.
            Assert.IsTrue(DevicePrefs.WriteString(Key, string.Empty));
            Assert.IsTrue(PlayerPrefs.HasKey(Key));

            Assert.IsFalse(DevicePrefs.WriteString(Key, string.Empty),
                           "and only the first one, because now it really is unchanged");
        }

        [Test]
        public void ANullValueIsStoredAsEmptyRatherThanThrowing()
        {
            Assert.IsTrue(DevicePrefs.WriteString(Key, null));
            Assert.AreEqual(string.Empty, PlayerPrefs.GetString(Key, "not empty"));
        }

        [Test]
        public void NoKeyIsNoWrite()
        {
            Assert.IsFalse(DevicePrefs.WriteString(null, "x"));
            Assert.IsFalse(DevicePrefs.WriteString(string.Empty, "x"));
        }
    }
}
