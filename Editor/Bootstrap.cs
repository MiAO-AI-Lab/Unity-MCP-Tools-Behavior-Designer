using System.Reflection;
using UnityEngine;
using UnityEditor;
using com.MiAO.MCP.Bootstrap;
using com.MiAO.MCP.Common;

namespace com.MiAO.MCP.BehaviorDesignerTools
{
    /// <summary>
    /// Essential Tools Bootstrap - Simplified bootstrap using Universal Package Bootstrap Framework
    /// Automatically initializes and registers essential tools when the package is loaded
    /// </summary>
    [InitializeOnLoad]
    public static class BehaviorDesignerToolsBootstrap
    {
        // Package configuration
        private const string PackageName = "com.miao.unity.mcp.behavior-designer-tools";
        private const string DisplayName = "Behavior Designer Tools";

        /// <summary>
        /// Static constructor - automatically called when Unity loads this assembly
        /// </summary>
        static BehaviorDesignerToolsBootstrap()
        {
            // Create package configuration using the simplified method
            var config = UniversalPackageBootstrap.CreateSimpleConfig(
                PackageName,
                DisplayName,
                Assembly.GetExecutingAssembly()
            );

            // Bootstrap using Universal Package Bootstrap Framework
            UniversalPackageBootstrap.Bootstrap(config);
        }

    }
}