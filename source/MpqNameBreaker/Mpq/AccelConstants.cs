
namespace MpqNameBreaker.Mpq
{
    public struct AccelConstants : IEquatable<AccelConstants>
    {
        public uint hashALookup;     // The hash A that we are looking for
        public uint hashBLookup;     // The hash B that we are looking for
        public uint prefixSeed1a;    // Pre-computed hash A seed 1 for the string prefix
        public uint prefixSeed2a;    // Pre-computed hash A seed 2 for the string prefix
        public uint prefixSeed1b;    // Pre-computed hash B seed 1 for the string prefix
        public uint prefixSeed2b;    // Pre-computed hash B seed 2 for the string prefix
        public int batchCharCount;   // MAX = 8          // Number of generated chars in the batch
        public int suffixbytes;
        public int charsetLength;

        public override bool Equals(object obj)
        {
            return obj is AccelConstants constants && Equals(constants);
        }

        public bool Equals(AccelConstants other)
        {
            return hashALookup == other.hashALookup &&
                   hashBLookup == other.hashBLookup &&
                   prefixSeed1a == other.prefixSeed1a &&
                   prefixSeed2a == other.prefixSeed2a &&
                   prefixSeed1b == other.prefixSeed1b &&
                   prefixSeed2b == other.prefixSeed2b &&
                   batchCharCount == other.batchCharCount &&
                   suffixbytes == other.suffixbytes &&
                   charsetLength == other.charsetLength;
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(hashALookup);
            hash.Add(hashBLookup);
            hash.Add(prefixSeed1a);
            hash.Add(prefixSeed2a);
            hash.Add(prefixSeed1b);
            hash.Add(prefixSeed2b);
            hash.Add(batchCharCount);
            hash.Add(suffixbytes);
            hash.Add(charsetLength);
            return hash.ToHashCode();
        }

        public static bool operator ==(AccelConstants left, AccelConstants right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AccelConstants left, AccelConstants right)
        {
            return !(left == right);
        }
    }
}
