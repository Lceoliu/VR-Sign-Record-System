using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests.EditorTools
{
    public sealed class InteractionQaConsoleTests
    {
        private const string DriverTypeName =
            "SignVR.Editor.Interaction.Qa." +
            "InteractionQaConsoleTestDriver, Assembly-CSharp-Editor";

        [Test]
        public void ActionsUseOnlyTheApprovedQaAuthorityPort()
        {
            InvokeDriver(nameof(ActionsUseOnlyTheApprovedQaAuthorityPort));
        }

        [Test]
        public void CorrectAndWrongTargetResolutionCoversAllSixPhases()
        {
            InvokeDriver(nameof(
                CorrectAndWrongTargetResolutionCoversAllSixPhases
            ));
        }

        [Test]
        public void NotPlayingOrMissingComponentsFailsSafely()
        {
            InvokeDriver(nameof(NotPlayingOrMissingComponentsFailsSafely));
        }

        [Test]
        public void InputInjectionCannotBypassTheLifecycleGate()
        {
            InvokeDriver(nameof(InputInjectionCannotBypassTheLifecycleGate));
        }

        [Test]
        public void ScreenshotPathCannotEscapeTheIgnoredQaDirectory()
        {
            InvokeDriver(nameof(
                ScreenshotPathCannotEscapeTheIgnoredQaDirectory
            ));
        }

        [Test]
        public void ConsoleIsEditorOnlyAndReadsPublicPointingDiagnostics()
        {
            InvokeDriver(nameof(
                ConsoleIsEditorOnlyAndReadsPublicPointingDiagnostics
            ));
        }

        private static void InvokeDriver(string methodName)
        {
            Type driverType = Type.GetType(
                DriverTypeName,
                throwOnError: true
            );
            MethodInfo method = driverType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            ) ?? throw new MissingMethodException(DriverTypeName, methodName);

            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(
                    exception.InnerException ?? exception
                ).Throw();
                throw;
            }
        }
    }
}
