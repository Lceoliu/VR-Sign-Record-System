using System;
using System.Globalization;

namespace SignVR.Interaction.Core
{
    /// <summary>
    /// Owns the pseudonymous Participant identity for one Interaction
    /// application launch. The composition root creates one instance and
    /// shares it across every Run in that Participant Session.
    /// </summary>
    public sealed class ParticipantSession
    {
        public ParticipantSession()
            : this(
                () => DateTimeOffset.UtcNow,
                Guid.NewGuid
            )
        {
        }

        public ParticipantSession(
            Func<DateTimeOffset> clock,
            Func<Guid> guidSource)
        {
            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }
            if (guidSource == null)
            {
                throw new ArgumentNullException(nameof(guidSource));
            }

            DateTimeOffset createdAtUtc = clock().ToUniversalTime();
            Guid entropy = guidSource();
            if (entropy == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Participant Session GUID entropy must not be empty."
                );
            }

            ParticipantId = string.Format(
                CultureInfo.InvariantCulture,
                "P-{0:yyyyMMdd-HHmmss}-{1:N}",
                createdAtUtc,
                entropy
            );
        }

        public string ParticipantId { get; }
    }
}
