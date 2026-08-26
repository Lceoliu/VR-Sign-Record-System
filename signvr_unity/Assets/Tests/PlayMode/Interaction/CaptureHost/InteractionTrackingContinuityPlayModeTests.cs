using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;

namespace SignVR.Interaction.PlayMode.Tests
{
    public sealed class InteractionTrackingContinuityPlayModeTests
    {
        [Test]
        public void
            TrackingLossDoesNotAbortAndRecoveryContinuesRunningRun()
        {
            Assert.That(
                Application.isPlaying,
                Is.True,
                "Tracking continuity must execute in Play Mode."
            );
            InvokeDriver(nameof(
                TrackingLossDoesNotAbortAndRecoveryContinuesRunningRun
            ));
        }

        private static void InvokeDriver(string methodName)
        {
            Type driver = Type.GetType(
                "SignVR.Interaction.CaptureHost." +
                    "InteractionTrackingContinuityPlayModeTestDriver, " +
                    "Assembly-CSharp",
                throwOnError: true
            );
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                "Missing tracking continuity scenario " + methodName
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
