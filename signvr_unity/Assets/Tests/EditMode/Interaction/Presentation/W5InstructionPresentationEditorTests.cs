using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests.Presentation
{
    public sealed class W5InstructionPresentationEditorTests
    {
        private const string SetupTypeName =
            "SignVR.Editor.Interaction.W5InstructionPresentationSetup, " +
            "Assembly-CSharp-Editor";

        [Test]
        public void W5SourceContractUsesFrozenCatalogConditionAndPlayerApi()
        {
            Invoke("ValidateSourceContractForAutomation");
        }

        [Test]
        public void SavedInteractionLabHasCompleteW5AnchorWiring()
        {
            Invoke("ValidateConfiguredSceneForAutomation");
        }

        private static void Invoke(string methodName)
        {
            Type type = Type.GetType(SetupTypeName, throwOnError: true);
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                $"Missing W5 validator entry point {methodName}."
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
