using System;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditorInternal;
using UnityEngine;
using YooAsset.Editor;

namespace SwarmECS.Editor
{
    public static class CommercialPipelineSetup
    {
        public const string YooPackageName = "SwarmAssets";
        private const string HotUpdateAsmdefPath = "Assets/SwarmSandbox/HotUpdate/SwarmECS.HotUpdate.asmdef";
        private const string HotUpdateAssetPath = "Assets/HotUpdateAssets";

        [MenuItem("Swarm ECS/Commercial Pipeline/Configure YooAsset + HybridCLR")]
        public static void Configure()
        {
            ConfigureHybridClr();
            ConfigureYooAsset();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            AssetDatabase.SaveAssets();
            Debug.Log("[SwarmECS] YooAsset 3.0.4 + HybridCLR 8.12.0 project configuration completed.");
        }

        private static void ConfigureHybridClr()
        {
            AssemblyDefinitionAsset hotUpdateAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(HotUpdateAsmdefPath);
            if (hotUpdateAssembly == null)
            {
                throw new InvalidOperationException($"Hot-update asmdef not found: {HotUpdateAsmdefPath}");
            }

            HybridCLRSettings settings = HybridCLRSettings.Instance;
            settings.enable = true;
            settings.useGlobalIl2cpp = false;
            settings.hotUpdateAssemblyDefinitions = new[] { hotUpdateAssembly };
            settings.hotUpdateAssemblies = Array.Empty<string>();
            settings.preserveHotUpdateAssemblies = Array.Empty<string>();
            settings.patchAOTAssemblies = new[]
            {
                "mscorlib",
                "System",
                "System.Core",
            };
            HybridCLRSettings.Save();
        }

        private static void ConfigureYooAsset()
        {
            BundleCollectorPackage package = null;
            foreach (BundleCollectorPackage candidate in BundleCollectorSettingData.Setting.Packages)
            {
                if (candidate.PackageName == YooPackageName)
                {
                    package = candidate;
                    break;
                }
            }

            if (package == null)
            {
                package = BundleCollectorSettingData.CreatePackage(YooPackageName);
            }

            package.PackageDesc = "Swarm sandbox hot-update DLLs, AOT metadata and production assets";
            package.EnableAddressable = true;
            BundleCollectorGroup group = null;
            foreach (BundleCollectorGroup candidate in package.Groups)
            {
                if (candidate.GroupName == "HotUpdateAndData")
                {
                    group = candidate;
                    break;
                }
            }

            if (group == null)
            {
                group = BundleCollectorSettingData.CreateGroup(package, "HotUpdateAndData");
            }

            foreach (BundleCollector existing in group.Collectors)
            {
                if (existing.CollectPath == HotUpdateAssetPath)
                {
                    BundleCollectorSettingData.SaveFile();
                    return;
                }
            }

            BundleCollector collector = new()
            {
                CollectPath = HotUpdateAssetPath,
                CollectorGUID = AssetDatabase.AssetPathToGUID(HotUpdateAssetPath),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAll),
                AssetTags = "hotupdate",
            };
            BundleCollectorSettingData.CreateCollector(group, collector);
            BundleCollectorSettingData.SaveFile();
        }
    }
}
