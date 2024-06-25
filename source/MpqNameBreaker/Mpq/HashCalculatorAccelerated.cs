using ILGPU;
using ILGPU.Runtime;
using MpqNameBreaker.NameGenerator;

namespace MpqNameBreaker.Mpq
{
    public class HashCalculatorAccelerated
    {
        // Constants
        public const uint HashSeed1 = 0x7FED7FED;
        public const uint HashSeed2 = 0xEEEEEEEE;

        public Context GPUContext { get; private set; }

        // Constructors
        public HashCalculatorAccelerated()
        {
            InitializeGpuContext();
        }

        public void InitializeGpuContext()
        {
            GPUContext = Context.Create(builder =>
            {
                // Notes: OptimizationLevel.O2 is actually really slow, not sure how to leverage it better if at all.
                builder.Optimize(OptimizationLevel.O1)
                .AllAccelerators()
                .Arrays(ArrayMode.InlineMutableStaticArrays)
                .Inlining(InliningMode.Aggressive)
#if DEBUG
                .Assertions()
                .AutoAssertions()
                .Debug()
#endif
                ;
            });
        }

        public Device GetBestDevice()
        {
            return GPUContext.Devices.OrderByDescending(device => device.MaxNumThreads).First();
        }

        // Integer power, does not work for negative exponent
        private static int IPow(int value, int exponent)
        {
            int result = 1;
            for (; exponent != 0; value *= value)
            {
                if ((exponent & 1) != 0)
                {
                    result *= value;
                }
                exponent >>= 1;
            }
            return result;
        }

        public static void HashStringsBatchOptimized(
            Index1D index,
            ArrayView<byte> charset,                 // 1D array holding the charset bytes
            ArrayView2D<int, Stride2D.DenseX> charsetIndexes,        // 2D array containing the char indexes of one batch string seed (one string per line, hashes will be computed starting from this string)
            ArrayView<byte> suffixBytes,             // 1D array holding the indexes of the suffix chars
            SpecializedValue<AccelConstants> opt,    // Values that can be optimized
            SpecializedValue<int> firstBatch,        // boolean
            int nameCount,                           // Name count limit (used as return condition)
            ArrayView<int> foundNameCharsetIndexes,  // 1D array containing the found name (if found)
            ArrayView<uint> cryptTableA,
            ArrayView<uint> cryptTableB,
            ArrayView<int> beforeIndexes,            // Hint name that comes before this (for bounding)
            ArrayView<int> afterIndexes              // Hint name that comes after this (for bounding)
        )
        {
            // Brute force increment variables
            int generatedCharIndex = 0;

            // Greatest number of characters matching a hint
            int beforeHintIndex = -1;
            int afterHintIndex = -1;

            // Hash precalculated seeds (after prefix)
            uint[] precalcSeeds1 = new uint[BruteForceBatches.MaxGeneratedChars];
            uint[] precalcSeeds2 = new uint[BruteForceBatches.MaxGeneratedChars];
            precalcSeeds1[0] = opt.Value.prefixSeed1a;
            precalcSeeds2[0] = opt.Value.prefixSeed2a;
            int precalcSeedIndex = 0;

            // Brute force increment preparation
            // Increase name count to !numChars-1 for first batch first name seed
            if (firstBatch != 0 && index == 0)
            {
                nameCount = -1;
                for (int i = 1; i <= opt.Value.batchCharCount; ++i)
                {
                    nameCount += IPow(opt.Value.charsetLength, i);
                }
                nameCount += IPow(opt.Value.charsetLength, opt.Value.batchCharCount);
            }

            // Find the position of the last generated char
            for (int i = 1; i < BruteForceBatches.MaxGeneratedChars; ++i)
            {
                if (charsetIndexes[index, i] == -1)
                {
                    generatedCharIndex = i - 1;
                    break;
                }
            }

            // TODO: This can be moved to BruteForceBatches.cs to be done on the CPU, but probably not that important since it is outside the bottleneck
            if (opt.Value.beforebytes > 0)
            {
                beforeHintIndex = Math.Min(opt.Value.beforebytes - 1, generatedCharIndex);
                for (int i = 0; i <= beforeHintIndex; i++)
                {
                    charsetIndexes[index, i] = beforeIndexes[i];
                }
            }

            // For each name compute hash
            for (; nameCount != 0; nameCount--)
            {
                // Subsequent names
                uint s1 = precalcSeeds1[precalcSeedIndex];
                uint s2 = precalcSeeds2[precalcSeedIndex];

                // Hash calculation
                // TODO opt: test condition i <= generatedCharIndex without the charsetIdx test inside
                for (int i = precalcSeedIndex; i < BruteForceBatches.MaxGeneratedChars; ++i)
                {
                    // Retrieve the current char of the string
                    int charsetIdx = charsetIndexes[index, i];

                    if (charsetIdx == -1) // break if end of the string is reached
                        break;

                    uint ch = charset[charsetIdx];

                    // Hash calculation
                    s1 = cryptTableA[(long)ch] ^ (s1 + s2);
                    s2 = ch + s1 + s2 + (s2 << 5) + 3;

                    // Store precalc seeds except if we are at the last character of the string
                    // (then it's not needed because this char changes constantly)
                    if (i < generatedCharIndex)
                    {
                        precalcSeedIndex++;
                        precalcSeeds1[precalcSeedIndex] = s1;
                        precalcSeeds2[precalcSeedIndex] = s2;
                    }
                }

                // Process suffix
                if (opt.Value.suffixbytes > 0)
                {
                    for (int i = 0; i < opt.Value.suffixbytes - 1; ++i)
                    {
                        // Retrieve current suffix char
                        uint ch = suffixBytes[i];

                        // Hash calculation
                        s1 = cryptTableA[(long)ch] ^ (s1 + s2);
                        s2 = ch + s1 + s2 + (s2 << 5) + 3;
                    }
                    // Optimization, the final XOR is pre-performed on CPU
                    s1 += s2;
                }

                // Check if it matches the hash that we are looking for
                // No precalculation because this is only executed on matches and collisions
                if (s1 == opt.Value.hashALookup)
                {
                    s1 = opt.Value.prefixSeed1b;
                    s2 = opt.Value.prefixSeed2b;

                    // TODO opt: test condition i <= generatedCharIndex without the charsetIdx test inside
                    for (int i = 0; i < BruteForceBatches.MaxGeneratedChars; ++i)
                    {
                        // Retrieve the current char of the string
                        int charsetIdx = charsetIndexes[index, i];

                        if (charsetIdx == -1) // break if end of the string is reached
                            break;

                        uint ch = charset[charsetIdx];

                        // Hash calculation
                        s1 = cryptTableB[(long)ch] ^ (s1 + s2);
                        s2 = ch + s1 + s2 + (s2 << 5) + 3;
                    }

                    // Process suffix
                    if (opt.Value.suffixbytes > 0)
                    {
                        for (int i = 0; i < opt.Value.suffixbytes - 1; ++i)
                        {
                            // Retrieve current suffix char
                            uint ch = suffixBytes[i];

                            // Hash calculation
                            s1 = cryptTableB[(long)ch] ^ (s1 + s2);
                            s2 = ch + s1 + s2 + (s2 << 5) + 3;
                        }
                        // Optimization, the final XOR is pre-performed on CPU
                        s1 += s2;
                    }

                    if (s1 == opt.Value.hashBLookup)
                    {
                        // Populate foundNameCharsetIndexes and return
                        for (int i = 0; i < BruteForceBatches.MaxGeneratedChars; ++i)
                            foundNameCharsetIndexes[i] = charsetIndexes[index, i];

                        return;
                    }
                }

                // NOTE: Checks on opt.Value.beforebytes > 0 (and afterbytes) should be optimized out entirely if hints are unused

                // Move to next name in the batch (brute force increment)
                // If we are AT the last char of the charset
                int chrIdx = charsetIndexes[index, generatedCharIndex];
                if (chrIdx == opt.Value.charsetLength - 1 
                    || (opt.Value.afterbytes > 0 
                    && generatedCharIndex < opt.Value.afterbytes 
                    && generatedCharIndex == afterHintIndex 
                    && chrIdx == afterIndexes[generatedCharIndex]))
                {
                    // Go through all the charset indexes in reverse order
                    int stopValue = generatedCharIndex - opt.Value.batchCharCount + 1;
                    if (firstBatch != 0 || stopValue < 0)
                        stopValue = 0;

                    if (generatedCharIndex >= stopValue)
                    {
                        int numCharsReset = 0;
                        for (int i = generatedCharIndex; i >= stopValue; --i)
                        {
                            // Retrieve the current char of the string
                            Index2D idx = new Index2D(index, i);

                            // If we are at the last char of the charset then go back to the first char
                            if (charsetIndexes[idx] == opt.Value.charsetLength - 1
                                || (opt.Value.afterbytes > 0
                                && i < opt.Value.afterbytes
                                && i <= afterHintIndex
                                && charsetIndexes[idx] == afterIndexes[i]))
                            {
                                numCharsReset++;
                                charsetIndexes[idx] = 0;
                                if (opt.Value.afterbytes > 0)
                                {
                                    afterHintIndex = i - 1;
                                }
                            }
                            // If we are not at the last char of the charset then move to next char
                            else
                            {
                                charsetIndexes[idx]++;
                                if (opt.Value.beforebytes > 0 && beforeHintIndex == i)
                                {
                                    beforeHintIndex--;
                                }
                                if (opt.Value.afterbytes > 0 && i < opt.Value.afterbytes && afterHintIndex == i - 1 && charsetIndexes[idx] == afterIndexes[i])
                                {
                                    afterHintIndex++;
                                }
                                break;
                            }
                        }

                        // Go back in the precalc seeds (to recalculate since the char changed)
                        precalcSeedIndex -= numCharsReset;
                        if (precalcSeedIndex < 0) precalcSeedIndex = 0;

                        // When numCharsReset is equal to the number of chars total (we hit the max iterations for the string length)
                        if (numCharsReset == generatedCharIndex)
                        {
                            // Increase name size by one char
                            generatedCharIndex++;
                            if (opt.Value.beforebytes > 0)
                            {
                                // initialize new indexes with before hint
                                beforeHintIndex = Math.Min(opt.Value.beforebytes - 1, generatedCharIndex);
                                for (int i = 0; i <= beforeHintIndex; i++)
                                {
                                    charsetIndexes[index, i] = beforeIndexes[i];
                                }
                            }
                            else
                            {
                                charsetIndexes[index, generatedCharIndex] = 0;
                            }
                            if (opt.Value.afterbytes > 0)
                            {
                                afterHintIndex = -1;
                                for (int i = 0; i <= Math.Min(opt.Value.afterbytes - 1, generatedCharIndex) && charsetIndexes[index, i] == afterIndexes[i]; i++)
                                {
                                    afterHintIndex++;
                                }
                            }
                        }
                    }
                }
                // If the generated char is within the charset
                else
                {
                    // Move to next char
                    int chr = ++charsetIndexes[index, generatedCharIndex];
                    if (opt.Value.beforebytes > 0 && beforeHintIndex == generatedCharIndex)
                    {
                        beforeHintIndex--;
                    }
                    // TODO: potential optimization, make before/afterIndexes fixed size to match charsetIndexes and eliminate an extra bounds comparison
                    if (opt.Value.afterbytes > 0 && generatedCharIndex < opt.Value.afterbytes && afterHintIndex == generatedCharIndex - 1 && chr == afterIndexes[generatedCharIndex])
                    {
                        afterHintIndex++;
                    }
                }
            }
        }
    }
}
