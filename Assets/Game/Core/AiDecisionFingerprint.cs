using System.Globalization;

namespace LocalTanks
{
    /// <summary>
    /// Incremental, platform-independent fingerprint of the ordered decisions that shaped a battle.
    /// It deliberately excludes frame times and sampled positions: the same tactical trace must compare
    /// equal even when a machine renders a frame a little later. Length-prefixed UTF-16 fields keep the
    /// stream unambiguous without relying on process-randomized string hashes.
    /// </summary>
    public sealed class AiDecisionFingerprint
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong hash = OffsetBasis;

        public int Count { get; private set; }
        public string Value => hash.ToString("x16", CultureInfo.InvariantCulture);

        public void Reset()
        {
            hash = OffsetBasis;
            Count = 0;
        }

        public void Append(string kind, string actor, string detail)
        {
            AppendField(kind);
            AppendField(actor);
            AppendField(detail);
            Count++;
        }

        private void AppendField(string value)
        {
            string field = value ?? string.Empty;
            AppendInt(field.Length);
            foreach (char character in field)
            {
                AppendByte((byte)character);
                AppendByte((byte)(character >> 8));
            }
        }

        private void AppendInt(int value)
        {
            unchecked
            {
                AppendByte((byte)value);
                AppendByte((byte)(value >> 8));
                AppendByte((byte)(value >> 16));
                AppendByte((byte)(value >> 24));
            }
        }

        private void AppendByte(byte value)
        {
            unchecked
            {
                hash ^= value;
                hash *= Prime;
            }
        }
    }
}
