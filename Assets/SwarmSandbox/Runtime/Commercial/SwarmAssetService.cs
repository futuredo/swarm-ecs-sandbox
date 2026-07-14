using System;
using System.Collections;
using YooAsset;

namespace SwarmECS.Runtime.Commercial
{
    /// <summary>
    /// Thin YooAsset 3.x boundary. The algorithm demo has no external art dependency,
    /// but production assets and hot-update DLLs can use the same package lifecycle.
    /// </summary>
    public sealed class SwarmAssetService
    {
        public const string DefaultPackageName = "SwarmAssets";

        public ResourcePackage Package { get; private set; }

        public bool IsReady { get; private set; }

        public string LastError { get; private set; }

        public IEnumerator InitializeOfflineAsync(string packageName = DefaultPackageName)
        {
            if (!YooAssets.IsInitialized)
            {
                YooAssets.Initialize();
            }

            if (!YooAssets.TryGetPackage(packageName, out ResourcePackage package))
            {
                package = YooAssets.CreatePackage(packageName);
            }

            OfflinePlayModeOptions options = new();
            options.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            InitializePackageOperation operation = package.InitializePackageAsync(options);
            yield return operation;

            if (operation.Status != EOperationStatus.Succeeded)
            {
                IsReady = false;
                LastError = operation.Error;
                yield break;
            }

            Package = package;
            IsReady = true;
            LastError = string.Empty;
        }

        public IEnumerator LoadRawBytesAsync(string location, Action<byte[]> onLoaded, Action<string> onError = null)
        {
            if (!IsReady || Package == null)
            {
                onError?.Invoke("YooAsset package is not initialized.");
                yield break;
            }

            AssetHandle handle = Package.LoadAssetAsync<RawFileObject>(location);
            yield return handle;
            if (handle.Status != EOperationStatus.Succeeded)
            {
                onError?.Invoke(handle.Error);
                handle.Release();
                yield break;
            }

            RawFileObject rawFile = handle.GetAssetObject<RawFileObject>();
            byte[] bytes = rawFile?.GetBytes();
            handle.Release();
            if (bytes == null)
            {
                onError?.Invoke($"Raw asset '{location}' returned no bytes.");
                yield break;
            }

            onLoaded?.Invoke(bytes);
        }
    }
}
