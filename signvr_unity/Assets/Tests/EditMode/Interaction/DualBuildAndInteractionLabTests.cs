using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class DualBuildAndInteractionLabTests
    {
        private const string ValidatorTypeName =
            "SignVR.Editor.Interaction.InteractionLabValidator, " +
            "Assembly-CSharp-Editor";

        [Test]
        public void BuildEntriesUseDistinctIdentityAndOneExplicitScene()
        {
            InvokeValidator("ValidateBuildContractForAutomation");
        }

        [Test]
        public void GeneratedInteractionLabPassesCleanSceneContract()
        {
            InvokeValidator("ValidateSceneForAutomation");
        }

        private static void InvokeValidator(string methodName)
        {
            Type validator = Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            );
            MethodInfo method = validator.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                $"Missing validator entry point {methodName}."
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
