using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SignVR.EditorTools
{
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
        public const string BundleVersion = "1.0.0";
        public const int AndroidVersionCode = 1;

        [MenuItem("SignVR/Release/Apply Product Identity")]
        public static void ApplyProductIdentity()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = BundleVersion;
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                ApplicationIdentifier
            );
            PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;

            Debug.Log(
                $"[SignVRReleaseSettings] Applied {ApplicationIdentifier} " +
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
            string applicationIdentifier = PlayerSettings.GetApplicationIdentifier(
                NamedBuildTarget.Android
            );
            if (PlayerSettings.companyName != CompanyName ||
                PlayerSettings.productName != ProductName ||
                PlayerSettings.bundleVersion != BundleVersion ||
                applicationIdentifier != ApplicationIdentifier ||
                PlayerSettings.Android.bundleVersionCode != AndroidVersionCode)
            {
                throw new InvalidOperationException(
                    "SignVR product identity does not match the release constants."
                );
            }

            Debug.Log("[SignVRReleaseSettings] Product identity is valid.");
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
    }
}
