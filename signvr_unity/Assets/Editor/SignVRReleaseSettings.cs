using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SignVR.EditorTools
{
    internal enum SignVRProduct
    {
        Recorder,
        Interaction
    }

    public static class SignVRReleaseSettings
    {
        [Serializable]
        private sealed class SigningInput
        {
            public string keystore_path;
            public string keystore_password;
            public string key_alias;
            public string key_alias_password;
        }

        public const string CompanyName = "SignVR";
        public const string ProductName = "SignVR Recorder";
        public const string ApplicationIdentifier = "com.signvr.recorder";
        public const string InteractionProductName = "SignVR Interaction";
        public const string InteractionApplicationIdentifier =
            "com.signvr.interaction";
        public const string BundleVersion = "1.0.0";
        public const int AndroidVersionCode = 1;

        // Preserve the Recorder menu path used before the Interaction product
        // was introduced. Existing operator muscle memory and automation stay
        // valid while Interaction gets its own explicit entry below.
        [MenuItem("SignVR/Release/Apply Product Identity")]
        public static void ApplyProductIdentity()
        {
            ApplyProductIdentity(SignVRProduct.Recorder);
        }

        [MenuItem("SignVR/Release/Interaction/Apply Product Identity")]
        public static void ApplyInteractionProductIdentity()
        {
            ApplyProductIdentity(SignVRProduct.Interaction);
        }

        internal static void ApplyProductIdentity(SignVRProduct product)
        {
            GetIdentity(
                product,
                out string productName,
                out string applicationIdentifier
            );

            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = productName;
            PlayerSettings.bundleVersion = BundleVersion;
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                applicationIdentifier
            );
            PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;

            Debug.Log(
                $"[SignVRReleaseSettings] Applied {applicationIdentifier} " +
                $"version {BundleVersion} ({AndroidVersionCode})."
            );
        }

        [MenuItem("SignVR/Release/Apply Android Signing")]
        public static void ApplyAndroidSigning()
        {
            string inputPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Library",
                "SignVRSigningInput.json"
            );

            try
            {
                SigningInput input = File.Exists(inputPath)
                    ? JsonUtility.FromJson<SigningInput>(
                        File.ReadAllText(inputPath)
                    )
                    : null;

                string keystorePath = input != null
                    ? input.keystore_path
                    : RequireEnvironment("SIGNVR_ANDROID_KEYSTORE_PATH");
                if (!File.Exists(keystorePath))
                {
                    throw new FileNotFoundException(
                        "SignVR Android keystore was not found.",
                        keystorePath
                    );
                }

                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(
                    keystorePath
                );
                PlayerSettings.Android.keystorePass = input != null
                    ? input.keystore_password
                    : RequireEnvironment("SIGNVR_ANDROID_KEYSTORE_PASSWORD");
                PlayerSettings.Android.keyaliasName = input != null
                    ? input.key_alias
                    : RequireEnvironment("SIGNVR_ANDROID_KEY_ALIAS");
                PlayerSettings.Android.keyaliasPass = input != null
                    ? input.key_alias_password
                    : RequireEnvironment("SIGNVR_ANDROID_KEY_ALIAS_PASSWORD");

                Debug.Log(
                    "[SignVRReleaseSettings] Android release signing is configured."
                );
            }
            finally
            {
                if (File.Exists(inputPath))
                {
                    File.Delete(inputPath);
                }
            }
        }

        [MenuItem("SignVR/Release/Validate Product Identity")]
        public static void ValidateProductIdentity()
        {
            ValidateProductIdentity(SignVRProduct.Recorder);
        }

        [MenuItem("SignVR/Release/Interaction/Validate Product Identity")]
        public static void ValidateInteractionProductIdentity()
        {
            ValidateProductIdentity(SignVRProduct.Interaction);
        }

        internal static void ValidateProductIdentity(SignVRProduct product)
        {
            GetIdentity(
                product,
                out string productName,
                out string expectedApplicationIdentifier
            );
            string applicationIdentifier = PlayerSettings.GetApplicationIdentifier(
                NamedBuildTarget.Android
            );
            if (PlayerSettings.companyName != CompanyName ||
                PlayerSettings.productName != productName ||
                PlayerSettings.bundleVersion != BundleVersion ||
                applicationIdentifier != expectedApplicationIdentifier ||
                PlayerSettings.Android.bundleVersionCode != AndroidVersionCode)
            {
                throw new InvalidOperationException(
                    $"SignVR {product} product identity does not match the " +
                    "release constants."
                );
            }

            Debug.Log(
                $"[SignVRReleaseSettings] {product} product identity is valid."
            );
        }

        internal static string GetProductName(SignVRProduct product)
        {
            GetIdentity(product, out string productName, out _);
            return productName;
        }

        internal static string GetApplicationIdentifier(SignVRProduct product)
        {
            GetIdentity(product, out _, out string applicationIdentifier);
            return applicationIdentifier;
        }

        private static string RequireEnvironment(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Required environment variable is missing: {name}"
                );
            }

            return value;
        }

        private static void GetIdentity(
            SignVRProduct product,
            out string productName,
            out string applicationIdentifier)
        {
            switch (product)
            {
                case SignVRProduct.Recorder:
                    productName = ProductName;
                    applicationIdentifier = ApplicationIdentifier;
                    return;
                case SignVRProduct.Interaction:
                    productName = InteractionProductName;
                    applicationIdentifier = InteractionApplicationIdentifier;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(product),
                        product,
                        "Unknown SignVR product."
                    );
            }
        }
    }
}
