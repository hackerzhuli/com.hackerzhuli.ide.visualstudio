/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Hackerzhuli.Code.Editor.ProjectGeneration
{
    public interface IAssemblyNameProvider
    {
        string[] ProjectSupportedExtensions { get; }
        string ProjectGenerationRootNamespace { get; }
        ProjectGenerationFlag ProjectGenerationFlag { get; }

        string GetAssemblyNameFromScriptPath(string path);
        string GetAssemblyName(string assemblyOutputPath, string assemblyName);
        bool IsInternalizedPackagePath(string path);
        IEnumerable<Assembly> GetAssemblies(Func<string, bool> shouldFileBePartOfSolution);
        IEnumerable<string> GetAllAssetPaths();
        PackageInfo FindForAssetPath(string assetPath);

        ResponseFileData ParseResponseFile(string responseFilePath, string projectDirectory,
            string[] systemReferenceDirectories);

        void ToggleProjectGeneration(ProjectGenerationFlag preference);
    }

    public class AssemblyNameProvider : IAssemblyNameProvider
    {
        internal static readonly string AssemblyOutput = @"Temp\bin\Debug\".NormalizePathSeparators();
        internal static readonly string PlayerAssemblyOutput = @"Temp\bin\Debug\Player\".NormalizePathSeparators();
        private readonly Dictionary<string, PackageInfo> m_PackageInfoCache = new();

        private ProjectGenerationFlag m_ProjectGenerationFlag = (ProjectGenerationFlag)EditorPrefs.GetInt(
            "unity_project_generation_flag",
            (int)(ProjectGenerationFlag.Local | ProjectGenerationFlag.Embedded));

        internal virtual ProjectGenerationFlag ProjectGenerationFlagImpl
        {
            get => m_ProjectGenerationFlag;
            private set
            {
                EditorPrefs.SetInt("unity_project_generation_flag", (int)value);
                m_ProjectGenerationFlag = value;
            }
        }

        public string[] ProjectSupportedExtensions => EditorSettings.projectGenerationUserExtensions;

        public string ProjectGenerationRootNamespace => EditorSettings.projectGenerationRootNamespace;

        public ProjectGenerationFlag ProjectGenerationFlag
        {
            get => ProjectGenerationFlagImpl;
            private set => ProjectGenerationFlagImpl = value;
        }

        public string GetAssemblyNameFromScriptPath(string path)
        {
            return CompilationPipeline.GetAssemblyNameFromScriptPath(path);
        }

        public IEnumerable<Assembly> GetAssemblies(Func<string, bool> shouldFileBePartOfSolution)
        {
            var assemblies = GetAssembliesByType(AssembliesType.Editor, shouldFileBePartOfSolution, AssemblyOutput);

            if (!ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.PlayerAssemblies)) return assemblies;
            var playerAssemblies =
                GetAssembliesByType(AssembliesType.Player, shouldFileBePartOfSolution, PlayerAssemblyOutput);
            return assemblies.Concat(playerAssemblies);
        }

        public IEnumerable<string> GetAllAssetPaths()
        {
            return AssetDatabase.GetAllAssetPaths();
        }

        public PackageInfo FindForAssetPath(string assetPath)
        {
            var parentPackageAssetPath = ResolvePotentialParentPackageAssetPath(assetPath);
            if (parentPackageAssetPath == null) return null;

            if (m_PackageInfoCache.TryGetValue(parentPackageAssetPath, out var cachedPackageInfo))
                return cachedPackageInfo;

            var result = PackageInfo.FindForAssetPath(parentPackageAssetPath);
            m_PackageInfoCache[parentPackageAssetPath] = result;
            return result;
        }

        public bool IsInternalizedPackagePath(string path)
        {
            if (string.IsNullOrEmpty(path.Trim())) return false;
            var packageInfo = FindForAssetPath(path);
            if (packageInfo == null) return false;
            var packageSource = packageInfo.source;
            switch (packageSource)
            {
                case PackageSource.Embedded:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Embedded);
                case PackageSource.Registry:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Registry);
                case PackageSource.BuiltIn:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.BuiltIn);
                case PackageSource.Unknown:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Unknown);
                case PackageSource.Local:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Local);
                case PackageSource.Git:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Git);
                case PackageSource.LocalTarball:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.LocalTarBall);
            }

            return false;
        }

        public ResponseFileData ParseResponseFile(string responseFilePath, string projectDirectory,
            string[] systemReferenceDirectories)
        {
            return CompilationPipeline.ParseResponseFile(
                responseFilePath,
                projectDirectory,
                systemReferenceDirectories
            );
        }

        public void ToggleProjectGeneration(ProjectGenerationFlag preference)
        {
            if (ProjectGenerationFlag.HasFlag(preference))
                ProjectGenerationFlag ^= preference;
            else
                ProjectGenerationFlag |= preference;
        }

        public string GetAssemblyName(string assemblyOutputPath, string assemblyName)
        {
            if (assemblyOutputPath == PlayerAssemblyOutput)
                return assemblyName + ".Player";

            return assemblyName;
        }

        private static IEnumerable<Assembly> GetAssembliesByType(AssembliesType type,
            Func<string, bool> shouldFileBePartOfSolution, string outputPath)
        {
            foreach (var assembly in CompilationPipeline.GetAssemblies(type))
                if (assembly.sourceFiles.Any(shouldFileBePartOfSolution))
                    yield return new Assembly(
                        assembly.name,
                        outputPath,
                        assembly.sourceFiles,
                        assembly.defines,
                        assembly.assemblyReferences,
                        assembly.compiledAssemblyReferences,
                        assembly.flags,
                        assembly.compilerOptions,
                        assembly.rootNamespace
                    );
        }

        public string GetCompileOutputPath(string assemblyName)
        {
            // We need to keep this one for API surface check (AssemblyNameProvider is public), but not used anymore
            throw new NotImplementedException();
        }

        private static string ResolvePotentialParentPackageAssetPath(string assetPath)
        {
            const string packagesPrefix = "packages/";
            if (!assetPath.StartsWith(packagesPrefix, StringComparison.OrdinalIgnoreCase)) return null;

            var followupSeparator = assetPath.IndexOf('/', packagesPrefix.Length);
            if (followupSeparator == -1) return assetPath.ToLowerInvariant();

            return assetPath.Substring(0, followupSeparator).ToLowerInvariant();
        }

        internal void ResetPackageInfoCache()
        {
            m_PackageInfoCache.Clear();
        }

        public void ResetProjectGenerationFlag()
        {
            ProjectGenerationFlag = ProjectGenerationFlag.None;
        }
    }
}