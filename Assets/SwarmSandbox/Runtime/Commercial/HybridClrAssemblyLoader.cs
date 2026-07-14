using System;
using System.Reflection;
using HybridCLR;

namespace SwarmECS.Runtime.Commercial
{
    /// <summary>Explicit AOT metadata and hot-update DLL loading boundary for HybridCLR.</summary>
    public static class HybridClrAssemblyLoader
    {
        public static LoadImageErrorCode LoadAotMetadata(byte[] strippedAotAssembly)
        {
            if (strippedAotAssembly == null || strippedAotAssembly.Length == 0)
            {
                throw new ArgumentException("AOT metadata DLL bytes are empty.", nameof(strippedAotAssembly));
            }

            return RuntimeApi.LoadMetadataForAOTAssembly(
                strippedAotAssembly,
                HomologousImageMode.SuperSet);
        }

        public static Assembly LoadHotUpdateAssembly(byte[] dllBytes, byte[] pdbBytes = null)
        {
            if (dllBytes == null || dllBytes.Length == 0)
            {
                throw new ArgumentException("Hot-update DLL bytes are empty.", nameof(dllBytes));
            }

            return pdbBytes == null || pdbBytes.Length == 0
                ? Assembly.Load(dllBytes)
                : Assembly.Load(dllBytes, pdbBytes);
        }
    }
}
