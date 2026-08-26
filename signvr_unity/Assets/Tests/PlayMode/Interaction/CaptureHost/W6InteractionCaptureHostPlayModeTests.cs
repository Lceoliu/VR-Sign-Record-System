using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;

namespace SignVR.Interaction.PlayMode.Tests
{
    public sealed class W6InteractionCaptureHostPlayModeTests
    {
        [Test]
        public void ControllerDestroyCancelsOwnedArtifactFreeze()
        {
            InvokeController(nameof(ControllerDestroyCancelsOwnedArtifactFreeze));
        }

        [Test]
        public void ControllerDisableCancelsOwnedArtifactFreeze()
        {
            InvokeController(nameof(ControllerDisableCancelsOwnedArtifactFreeze));
        }

        [Test]
        public void DestroyDoesNotDuplicateDetachedTerminalization()
        {
            InvokeController(
                nameof(DestroyDoesNotDuplicateDetachedTerminalization)
            );
        }

        [Test]
        public void DisableOwnsLateInitializationThroughController()
        {
            InvokeController(
                nameof(DisableOwnsLateInitializationThroughController)
            );
        }

        [Test]
        public void HostDisableCancelsOwnedArtifactVerify()
        {
            InvokeController(nameof(HostDisableCancelsOwnedArtifactVerify));
        }

        [Test]
        public void HostLifecycleEpochRejectsStaleArtifactIterator()
        {
            InvokeController(nameof(HostLifecycleEpochRejectsStaleArtifactIterator));
        }

        [Test]
        public void LifecycleQueueFailureConvergesThroughController()
        {
            InvokeController(nameof(LifecycleQueueFailureConvergesThroughController));
        }

        [Test]
        public void PendingInitializationQueueFailureConvergesThroughController()
        {
            InvokeController(
                nameof(PendingInitializationQueueFailureConvergesThroughController)
            );
        }

        private static void InvokeController(string methodName)
        {
            Assert.That(
                Application.isPlaying,
                Is.True,
                "W6 lifecycle wrappers must execute through real PlayMode callbacks."
            );
            Type driver = Type.GetType(
                "SignVR.Interaction.CaptureHost." +
                "W6InteractionRunControllerTestDriver, Assembly-CSharp",
                throwOnError: true
            );
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                "Missing W6 Controller scenario " + methodName
            );
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
