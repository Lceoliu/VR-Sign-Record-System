using System;

namespace SignVR.Interaction.Core
{
    public enum AssistanceCondition
    {
        TextAndPointing,
        TextOnly,
        SignOnly
    }

    public enum RunState
    {
        PreStart,
        Preparing,
        Scheduled,
        Running,
        Completing,
        Completed,
        Aborting,
        Aborted,
        Faulted
    }

    public enum PhaseState
    {
        Inactive,
        FirstPlayback,
        Active,
        ReplayPlayback,
        Completed,
        Stuck
    }

    public enum PhaseResult
    {
        Completed,
        Stuck
    }

    public enum RunResult
    {
        Completed,
        Aborted,
        Faulted
    }

    public static class AssistanceConditionPresentation
    {
        public static bool IncludesText(this AssistanceCondition condition)
        {
            switch (condition)
            {
                case AssistanceCondition.TextAndPointing:
                case AssistanceCondition.TextOnly:
                    return true;
                case AssistanceCondition.SignOnly:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(condition),
                        condition,
                        "Unknown assistance condition."
                    );
            }
        }

        public static bool IncludesPointing(this AssistanceCondition condition)
        {
            switch (condition)
            {
                case AssistanceCondition.TextAndPointing:
                    return true;
                case AssistanceCondition.TextOnly:
                case AssistanceCondition.SignOnly:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(condition),
                        condition,
                        "Unknown assistance condition."
                    );
            }
        }
    }

    internal static class CoreGuard
    {
        public static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    parameterName
                );
            }

            return value.Trim();
        }

        public static void DefinedEnum<T>(T value, string parameterName)
            where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "The enum value is not defined by the interaction contract."
                );
            }
        }
    }
}
