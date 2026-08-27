using System;

namespace SignVR.Interaction.Core
{
    public enum AssistanceAssignmentMode
    {
        RandomizedBlock = 0,
        ForcedTextAndPointing = 1
    }

    public sealed class AssistanceAssignment
    {
        public AssistanceAssignment(
            AssistanceCondition condition,
            int blockIndex,
            int slotIndex,
            AssistanceAssignmentMode mode =
                AssistanceAssignmentMode.RandomizedBlock)
        {
            CoreGuard.DefinedEnum(condition, nameof(condition));
            CoreGuard.DefinedEnum(mode, nameof(mode));
            if (blockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            if (slotIndex < 0 || slotIndex >= AssistanceBlockAllocator.BlockSize)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            Condition = condition;
            BlockIndex = blockIndex;
            SlotIndex = slotIndex;
            Mode = mode;
        }

        public AssistanceCondition Condition { get; }

        public int BlockIndex { get; }

        public int SlotIndex { get; }

        public AssistanceAssignmentMode Mode { get; }

        public bool IsForced =>
            Mode == AssistanceAssignmentMode.ForcedTextAndPointing;
    }

    /// <summary>
    /// Keeps the Assistance Assignment Block in memory for one application
    /// session. Creating a new allocator intentionally starts a new session.
    /// There is no return operation: once Start consumes a slot, aborting the
    /// resulting Run cannot put that slot back.
    /// </summary>
    public sealed class AssistanceBlockAllocator
    {
        public const int BlockSize = 3;

        private readonly DeterministicRandom random;
        private readonly AssistanceAssignmentMode mode;
        private AssistanceCondition[] currentBlock;
        private int nextSlotIndex;
        private int currentBlockIndex = -1;

        public AssistanceBlockAllocator()
            : this(unchecked(
                (Environment.TickCount * 397) ^ Guid.NewGuid().GetHashCode()
            ))
        {
        }

        public AssistanceBlockAllocator(int sessionSeed)
            : this(sessionSeed, forceTextAndPointing: false)
        {
        }

        public AssistanceBlockAllocator(
            int sessionSeed,
            bool forceTextAndPointing)
        {
            random = new DeterministicRandom(sessionSeed);
            mode = forceTextAndPointing
                ? AssistanceAssignmentMode.ForcedTextAndPointing
                : AssistanceAssignmentMode.RandomizedBlock;
        }

        public AssistanceBlockAllocator(bool forceTextAndPointing)
            : this(unchecked(
                (Environment.TickCount * 397) ^ Guid.NewGuid().GetHashCode()
            ), forceTextAndPointing)
        {
        }

        public AssistanceAssignmentMode Mode => mode;

        public int CurrentBlockIndex => currentBlockIndex;

        public int ConsumedSlotsInCurrentBlock => nextSlotIndex;

        public AssistanceAssignment AllocateNext()
        {
            return AllocateNext(assignment => assignment);
        }

        internal T AllocateNext<T>(Func<AssistanceAssignment, T> create)
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            EnsureAvailableBlock();
            var assignment = new AssistanceAssignment(
                currentBlock[nextSlotIndex],
                currentBlockIndex,
                nextSlotIndex,
                mode
            );

            // The slot is committed only after Start has successfully created
            // its immutable Run Plan.
            T result = create(assignment);
            nextSlotIndex++;
            return result;
        }

        private void EnsureAvailableBlock()
        {
            if (currentBlock != null && nextSlotIndex < BlockSize)
            {
                return;
            }

            currentBlockIndex++;
            nextSlotIndex = 0;
            currentBlock = mode == AssistanceAssignmentMode
                    .ForcedTextAndPointing
                ? new[]
                {
                    AssistanceCondition.TextAndPointing,
                    AssistanceCondition.TextAndPointing,
                    AssistanceCondition.TextAndPointing
                }
                : new[]
            {
                AssistanceCondition.TextAndPointing,
                AssistanceCondition.TextOnly,
                AssistanceCondition.SignOnly
            };
            if (mode == AssistanceAssignmentMode.RandomizedBlock)
            {
                random.Shuffle(currentBlock);
            }
        }
    }

    /// <summary>
    /// SplitMix64 with rejection-sampled bounded integers. Unlike
    /// System.Random, this gives Run Plan generation an explicit algorithm that
    /// remains reproducible across Mono, IL2CPP, and .NET runtime revisions.
    /// </summary>
    internal sealed class DeterministicRandom
    {
        private ulong state;

        public DeterministicRandom(int seed)
        {
            state = unchecked((ulong)(long)seed);
        }

        public int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveUpperBound)
                );
            }

            uint bound = (uint)exclusiveUpperBound;
            uint threshold = unchecked(0u - bound) % bound;
            uint sample;
            do
            {
                sample = NextUInt32();
            }
            while (sample < threshold);

            return (int)(sample % bound);
        }

        public void Shuffle<T>(T[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            for (int index = values.Length - 1; index > 0; index--)
            {
                int swapIndex = NextInt(index + 1);
                T value = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = value;
            }
        }

        private uint NextUInt32()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong value = state;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (uint)(value >> 32);
            }
        }
    }
}
