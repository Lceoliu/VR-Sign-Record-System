using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests.CaptureHost
{
    public sealed class StandaloneInteractionRunPolicyTests
    {
        private const string RunModeTypeName =
            "SignVR.Interaction.CaptureHost.InteractionRunMode, Assembly-CSharp";
        private const string StartPolicyTypeName =
            "SignVR.Interaction.CaptureHost.InteractionStudyStartPolicy, " +
            "Assembly-CSharp";
        private const string SetupPolicyTypeName =
            "SignVR.Interaction.CaptureHost.InteractionCaptureSetupPolicy, " +
            "Assembly-CSharp";

        [Test]
        public void StandaloneStudy_SerializesAsZero()
        {
            Type runModeType = ResolveType(RunModeTypeName);

            Assert.That(
                Convert.ToInt32(Enum.Parse(runModeType, "StandaloneStudy")),
                Is.Zero
            );
        }

        [Test]
        public void EngineeringLocal_SerializesAsOne()
        {
            Type runModeType = ResolveType(RunModeTypeName);

            Assert.That(
                Convert.ToInt32(Enum.Parse(runModeType, "EngineeringLocal")),
                Is.EqualTo(1)
            );
        }

        [Test]
        public void RunMode_ContainsOnlyStandaloneAndEngineeringModes()
        {
            Type runModeType = ResolveType(RunModeTypeName);

            Assert.That(
                Enum.GetNames(runModeType),
                Is.EqualTo(new[] { "StandaloneStudy", "EngineeringLocal" })
            );
        }

        [Test]
        public void StandaloneStudy_StartsWithStrictStudyDefaults()
        {
            Assert.DoesNotThrow(() => ValidateStart(
                "StandaloneStudy",
                debugOverridesActive: false,
                engineeringLocalExplicitlyArmed: false,
                debugBuild: false
            ));
        }

        [Test]
        public void StandaloneStudy_RejectsDebugOverrides()
        {
            Assert.Throws<InvalidOperationException>(() => ValidateStart(
                "StandaloneStudy",
                debugOverridesActive: true,
                engineeringLocalExplicitlyArmed: false,
                debugBuild: true
            ));
        }

        [Test]
        public void EngineeringLocal_StartsOnlyWhenArmedInDebugBuild()
        {
            Assert.DoesNotThrow(() => ValidateStart(
                "EngineeringLocal",
                debugOverridesActive: false,
                engineeringLocalExplicitlyArmed: true,
                debugBuild: true
            ));
        }

        [Test]
        public void EngineeringLocal_RejectsReleaseBuild()
        {
            Assert.Throws<InvalidOperationException>(() => ValidateStart(
                "EngineeringLocal",
                debugOverridesActive: false,
                engineeringLocalExplicitlyArmed: true,
                debugBuild: false
            ));
        }

        [Test]
        public void EngineeringLocal_RejectsMissingExplicitArm()
        {
            Assert.Throws<InvalidOperationException>(() => ValidateStart(
                "EngineeringLocal",
                debugOverridesActive: false,
                engineeringLocalExplicitlyArmed: false,
                debugBuild: true
            ));
        }

        [Test]
        public void StandaloneStructure_AcceptsStrictStudyDefaults()
        {
            Assert.DoesNotThrow(() => ValidateStructure(
                controllerCount: 1,
                samplerCount: 1,
                referencesWired: true,
                runModeName: "StandaloneStudy",
                debugOverridesActive: false
            ));
        }

        [Test]
        public void StandaloneStructure_RejectsDebugOverrides()
        {
            Assert.Throws<InvalidOperationException>(() => ValidateStructure(
                controllerCount: 1,
                samplerCount: 1,
                referencesWired: true,
                runModeName: "StandaloneStudy",
                debugOverridesActive: true
            ));
        }

        private static void ValidateStart(
            string runModeName,
            bool debugOverridesActive,
            bool engineeringLocalExplicitlyArmed,
            bool debugBuild)
        {
            InvokePublicStatic(
                StartPolicyTypeName,
                "Validate",
                ParseRunMode(runModeName),
                debugOverridesActive,
                engineeringLocalExplicitlyArmed,
                debugBuild
            );
        }

        private static void ValidateStructure(
            int controllerCount,
            int samplerCount,
            bool referencesWired,
            string runModeName,
            bool debugOverridesActive)
        {
            InvokePublicStatic(
                SetupPolicyTypeName,
                "ValidateStructure",
                controllerCount,
                samplerCount,
                referencesWired,
                ParseRunMode(runModeName),
                debugOverridesActive
            );
        }

        private static object ParseRunMode(string name)
        {
            return Enum.Parse(ResolveType(RunModeTypeName), name);
        }

        private static Type ResolveType(string assemblyQualifiedName)
        {
            return Type.GetType(assemblyQualifiedName, throwOnError: true);
        }

        private static void InvokePublicStatic(
            string typeName,
            string methodName,
            params object[] arguments)
        {
            Type type = ResolveType(typeName);
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            ) ?? throw new MissingMethodException(type.FullName, methodName);
            try
            {
                method.Invoke(null, arguments);
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
