using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class StandaloneLocalArtifactModelTests
    {
        private const string DriverTypeName =
            "SignVR.Interaction.CaptureHost." +
            "StandaloneLocalArtifactModelTestDriver, Assembly-CSharp";

        [Test]
        public void LocalArtifactSetContainsExactlyFiveArtifacts()
        {
            Invoke(nameof(LocalArtifactSetContainsExactlyFiveArtifacts));
        }

        [Test]
        public void LocalArtifactFileNamesAreStable()
        {
            Invoke(nameof(LocalArtifactFileNamesAreStable));
        }

        [Test]
        public void UnknownLocalArtifactTypeFailsClosed()
        {
            Invoke(nameof(UnknownLocalArtifactTypeFailsClosed));
        }

        [Test]
        public void LocalArtifactSetExcludesWebcamUploadAndAcknowledgementState()
        {
            Invoke(nameof(
                LocalArtifactSetExcludesWebcamUploadAndAcknowledgementState
            ));
        }

        [Test]
        public void LocalArtifactSizeLimitsRemainStable()
        {
            Invoke(nameof(LocalArtifactSizeLimitsRemainStable));
        }

        [Test]
        public void DefaultCaptureBudgetsRetainLocalArtifactSizeLimits()
        {
            Invoke(nameof(DefaultCaptureBudgetsRetainLocalArtifactSizeLimits));
        }

        private static void Invoke(string methodName)
        {
            Type driver = Type.GetType(DriverTypeName, throwOnError: true);
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                "Missing standalone local artifact scenario " + methodName
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
