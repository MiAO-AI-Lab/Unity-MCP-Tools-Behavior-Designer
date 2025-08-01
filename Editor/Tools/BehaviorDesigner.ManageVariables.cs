#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
using com.MiAO.Unity.MCP.Common;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;
using System.ComponentModel;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.MiAO.Unity.MCP.BehaviorDesignerTools
{
    public partial class Tool_BehaviorDesigner
    {
        [McpPluginTool
        (
            "BehaviorDesigner_ManageVariables",
            Title = "Manage BehaviorDesigner Variables in BehaviorSource - Read, Add, Delete, Update variables, and List Available Variable Types"
        )]
        [Description(@"Manage comprehensive BehaviorSource variable operations including:
- read: Read all variables from BehaviorSource asset and return detailed variable information
- addVariable: Add a new variable to the BehaviorSource with specified type and name
- deleteVariable: Delete a variable by name from the BehaviorSource
- updateVariable: Update variable value by name
- listAvailableVariableTypes: List all available SharedVariable types that can be created")]
        public string ManageVariables
        (
            [Description("Operation type: 'read', 'addVariable', 'deleteVariable', 'updateVariable', 'listAvailableVariableTypes'")]
            string operation,
            [Description("Asset path to the BehaviorDesigner ExternalBehavior file. Starts with 'Assets/'. Ends with '.asset'.")]
            string assetPath,
            [Description("For addVariable/deleteVariable/updateVariable: Variable name")]
            string? variableName = null,
            [Description("For addVariable: Variable type name (e.g., 'SharedString', 'SharedInt', 'SharedFloat', 'SharedBool')")]
            string? variableTypeName = null,
            [Description("For addVariable/updateVariable: Variable value (string representation)")]
            string? variableValue = null,
            [Description("For addVariable: Variable tooltip description")]
            string? tooltip = null,
            [Description("For read: Whether to include detailed variable serialization information")]
            bool includeDetails = false,
            [Description("For listAvailableVariableTypes: Filter variable types by namespace or category name. For example, 'BehaviorDesigner.Runtime' or 'Shared'. If not provided, all variable types will be listed.")]
            string? variableTypeFilter = null
        )
        {
            return operation.ToLower() switch
            {
                "read" => ReadVariables(assetPath, includeDetails),
                "addvariable" => AddVariable(assetPath, variableName, variableTypeName, variableValue, tooltip),
                "deletevariable" => DeleteVariable(assetPath, variableName),
                "updatevariable" => UpdateVariable(assetPath, variableName, variableValue),
                "listavailablevariabletypes" => ListAvailableVariableTypes(variableTypeFilter),
                _ => Error.InvalidOperation()
            };
        }

        #region Variable Management Methods

        public static string ReadVariables(string assetPath, bool includeDetails)
        {
            // Load BehaviorSource with error handling
            var (behaviorSource, _, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Check for errors before proceeding
            if (!string.IsNullOrEmpty(errorMessage))
                return Error.FailedToReadBehaviorSource(errorMessage);
            
            if (behaviorSource == null) 
                return Error.FailedToReadBehaviorSource("BehaviorSource is null");

            // Data processing operations that can run on any thread
            try
            {
                var result = new System.Text.StringBuilder();
                result.AppendLine($"[Success] Variables from BehaviorSource: {assetPath}");

                var variables = behaviorSource.Variables;
                if (variables == null || variables.Count == 0)
                {
                    result.AppendLine("\n=== No Variables Found ===");
                    result.AppendLine("This BehaviorSource has no variables. You can add variables using the 'addVariable' operation.");
                }
                else
                {
                    result.AppendLine($"\n=== Variables ({variables.Count}) ===");
                    for (int i = 0; i < variables.Count; i++)
                    {
                        var variable = variables[i];
                        result.AppendLine($"Variable {i + 1}: {GetVariableInfo(variable, includeDetails)}");
                    }
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.StackTrace);
                return Error.FailedToReadBehaviorSource(ex.Message);
            }
        }

        public static string AddVariable(string assetPath, string? variableName, string? variableTypeName, string? variableValue, string? tooltip)
        {
            // Validate required parameters
            if (string.IsNullOrEmpty(variableName))
                return Error.VariableNameRequired("adding a variable");

            if (string.IsNullOrEmpty(variableTypeName))
                return Error.VariableTypeRequired("adding a variable");

            // Load BehaviorSource with error handling
            var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Validate loaded data
            var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
            if (!string.IsNullOrEmpty(validationError))
                return validationError;

            // Data processing operations that can run on any thread
            try
            {
                // Check if variable name already exists
                if (VariableExists(behaviorSource, variableName))
                    return Error.VariableAlreadyExists(variableName);

                // Find variable type
                var variableType = FindVariableType(variableTypeName);
                if (variableType == null)
                    return Error.VariableTypeNotFound(variableTypeName);

                // Create new variable
                var newVariable = Activator.CreateInstance(variableType) as SharedVariable;
                if (newVariable == null)
                    return Error.FailedToCreateVariableInstance(variableTypeName);

                // Set variable properties
                newVariable.Name = variableName;
                if (!string.IsNullOrEmpty(tooltip))
                    newVariable.Tooltip = tooltip;

                // Set variable value if provided
                if (!string.IsNullOrEmpty(variableValue))
                {
                    var setValueError = SetVariableValue(newVariable, variableValue);
                    if (!string.IsNullOrEmpty(setValueError))
                        return setValueError;
                }

                // Add variable to BehaviorSource
                if (behaviorSource.Variables == null)
                    behaviorSource.Variables = new List<SharedVariable>();

                behaviorSource.Variables.Add(newVariable);

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save changes");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                return $"[Success] Added variable '{variableName}' of type '{variableTypeName}' to BehaviorSource.";
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.StackTrace);
                return Error.FailedToOperate("add a variable", ex.Message);
            }
        }

        public static string DeleteVariable(string assetPath, string? variableName)
        {
            if (string.IsNullOrEmpty(variableName))
                return Error.VariableNameRequired("deleting a variable");

            // Load BehaviorSource with error handling
            var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Validate loaded data
            var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
            if (!string.IsNullOrEmpty(validationError))
                return validationError;

            // Data processing operations that can run on any thread
            try
            {
                var variableToDelete = FindVariableByName(behaviorSource, variableName);
                if (variableToDelete == null)
                    return Error.VariableNotFound(variableName);

                // Remove variable from BehaviorSource
                behaviorSource.Variables.Remove(variableToDelete);

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save changes");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                return $"[Success] Deleted variable '{variableName}' from BehaviorSource.";
            }
            catch (Exception ex)
            {
                return Error.FailedToOperate("delete a variable", ex.Message);
            }
        }

        public static string UpdateVariable(string assetPath, string? variableName, string? variableValue)
        {
            if (string.IsNullOrEmpty(variableName))
                return Error.VariableNameRequired("updating a variable");

            if (string.IsNullOrEmpty(variableValue))
                return Error.VariableValueRequired("updating a variable");

            // Load BehaviorSource with error handling
            var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Validate loaded data
            var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
            if (!string.IsNullOrEmpty(validationError))
                return validationError;

            // Data processing operations that can run on any thread
            try
            {
                var variableToUpdate = FindVariableByName(behaviorSource, variableName);
                if (variableToUpdate == null)
                    return Error.VariableNotFound(variableName);

                // Set variable value
                var setValueError = SetVariableValue(variableToUpdate, variableValue);
                if (!string.IsNullOrEmpty(setValueError))
                    return setValueError;

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save changes");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                return $"[Success] Updated variable '{variableName}' with value '{variableValue}'.";
            }
            catch (Exception ex)
            {
                return Error.FailedToOperate("update a variable", ex.Message);
            }
        }

        public static string ListAvailableVariableTypes(string? variableTypeFilter = null)
        {
            var result = new System.Text.StringBuilder();
            result.AppendLine("[Success] Available variable types:");
            
            try
            {
                // Get all related assemblies
                var assemblies = new List<System.Reflection.Assembly>();
                var mainAssembly = typeof(SharedVariable).Assembly;
                assemblies.Add(mainAssembly);
                
                // Also check other assemblies in the current domain
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if ((assembly.FullName.Contains("BehaviorDesigner") || 
                         assembly.FullName.Contains("Assembly-CSharp")) && 
                        !assemblies.Contains(assembly))
                    {
                        assemblies.Add(assembly);
                    }
                }

                var allVariableTypes = new List<System.Type>();
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var allTypes = assembly.GetTypes();
                        
                        var variableTypes = allTypes
                            .Where(t => t.IsSubclassOf(typeof(SharedVariable)) && !t.IsAbstract)
                            .ToList();
                        
                        allVariableTypes.AddRange(variableTypes);
                    }
                    catch (System.Exception ex)
                    {
                        result.AppendLine($"Failed to get types from assembly {assembly.FullName}: {ex.Message}");
                        UnityEngine.Debug.LogWarning($"Failed to get types from assembly {assembly.FullName}: {ex.Message}");
                    }
                }

                // Group by namespace
                var namespaceGroups = new Dictionary<string, List<string>>();

                foreach (var type in allVariableTypes)
                {
                    var namespaceName = type.Namespace ?? "Global";

                    if (variableTypeFilter != null && !namespaceName.Contains(variableTypeFilter) && !type.Name.Contains(variableTypeFilter))
                        continue;
                    
                    if (!namespaceGroups.ContainsKey(namespaceName))
                        namespaceGroups[namespaceName] = new List<string>();
                    
                    namespaceGroups[namespaceName].Add(type.Name);
                }

                // Output the results
                var sortedNamespaces = namespaceGroups.Keys.OrderBy(ns => ns).ToList();
                
                foreach (var namespaceName in sortedNamespaces)
                {
                    var typeNames = namespaceGroups[namespaceName];
                    if (typeNames.Count == 0) continue;
                    
                    result.AppendLine($"\n{namespaceName}:");
                    typeNames.Sort(); // Sort by alphabetical order
                    foreach (var typeName in typeNames)
                    {
                        result.AppendLine($"  {typeName}");
                    }
                }

                // If no variable types are found, add debugging information
                if (allVariableTypes.Count == 0)
                {
                    result.AppendLine("\nNo variable types found. Debugging info:");
                    result.AppendLine($"SharedVariable base type: {typeof(SharedVariable)}");
                    result.AppendLine($"Assembly: {typeof(SharedVariable).Assembly.FullName}");
                    result.AppendLine($"Total assemblies checked: {assemblies.Count}");
                    foreach (var asm in assemblies)
                    {
                        result.AppendLine($"  - {asm.FullName}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                result.AppendLine($"Error occurred: {ex.Message}");
                result.AppendLine($"Stack trace: {ex.StackTrace}");
            }

            return result.ToString();
        }

        #endregion

        #region Helper Methods

        private static string GetVariableInfo(SharedVariable variable, bool includeDetails)
        {
            var info = $"Name: {variable.Name}, Type: {variable.GetType().Name}";
            
            if (!string.IsNullOrEmpty(variable.Tooltip))
                info += $", Tooltip: {variable.Tooltip}";

            if (includeDetails)
            {
                // Get the actual value using reflection
                var valueField = variable.GetType().GetField("mValue",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (valueField != null)
                {
                    var actualValue = valueField.GetValue(variable);
                    if (actualValue != null)
                    {
                        info += $", Value: {SerializeValue(actualValue)}";
                    }
                    else
                    {
                        info += ", Value: null";
                    }
                }
            }

            return info;
        }

        private static bool VariableExists(BehaviorSource behaviorSource, string variableName)
        {
            if (behaviorSource.Variables == null) return false;
            return behaviorSource.Variables.Any(v => v.Name == variableName);
        }

        private static SharedVariable? FindVariableByName(BehaviorSource behaviorSource, string variableName)
        {
            if (behaviorSource.Variables == null) return null;
            return behaviorSource.Variables.FirstOrDefault(v => v.Name == variableName);
        }

        private static Type? FindVariableType(string variableTypeName)
        {
            // Get all related assemblies
            var assemblies = new List<System.Reflection.Assembly>();
            var mainAssembly = typeof(SharedVariable).Assembly;
            assemblies.Add(mainAssembly);
            
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if ((assembly.FullName.Contains("BehaviorDesigner") || 
                     assembly.FullName.Contains("Assembly-CSharp")) && 
                    !assemblies.Contains(assembly))
                {
                    assemblies.Add(assembly);
                }
            }

            foreach (var assembly in assemblies)
            {
                try
                {
                    var type = assembly.GetType(variableTypeName);
                    if (type != null && type.IsSubclassOf(typeof(SharedVariable)) && !type.IsAbstract)
                        return type;

                    // Also try with full namespace
                    var fullTypeName = $"BehaviorDesigner.Runtime.{variableTypeName}";
                    type = assembly.GetType(fullTypeName);
                    if (type != null && type.IsSubclassOf(typeof(SharedVariable)) && !type.IsAbstract)
                        return type;
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"Failed to get type {variableTypeName} from assembly {assembly.FullName}: {ex.Message}");
                }
            }

            return null;
        }

        private static string? SetVariableValue(SharedVariable variable, string valueString)
        {
            try
            {
                var valueField = variable.GetType().GetField("mValue",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (valueField == null)
                    return "Failed to find value field in variable";

                var targetType = valueField.FieldType;
                object? parsedValue = null;

                // Parse value based on type
                if (targetType == typeof(string))
                {
                    parsedValue = valueString;
                }
                else if (targetType == typeof(int))
                {
                    if (int.TryParse(valueString, out int intValue))
                        parsedValue = intValue;
                    else
                        return $"Failed to parse '{valueString}' as int";
                }
                else if (targetType == typeof(float))
                {
                    if (float.TryParse(valueString, out float floatValue))
                        parsedValue = floatValue;
                    else
                        return $"Failed to parse '{valueString}' as float";
                }
                else if (targetType == typeof(bool))
                {
                    if (bool.TryParse(valueString, out bool boolValue))
                        parsedValue = boolValue;
                    else
                        return $"Failed to parse '{valueString}' as bool";
                }
                else if (targetType == typeof(Vector2))
                {
                    parsedValue = ParseVector2(valueString);
                }
                else if (targetType == typeof(Vector3))
                {
                    parsedValue = ParseVector3(valueString);
                }
                else if (targetType == typeof(Vector4))
                {
                    parsedValue = ParseVector4(valueString);
                }
                else if (targetType == typeof(Color))
                {
                    parsedValue = ParseColor(valueString);
                }
                else
                {
                    return $"Unsupported variable type: {targetType.Name}";
                }

                if (parsedValue != null)
                {
                    valueField.SetValue(variable, parsedValue);
                    return null; // Success
                }
                else
                {
                    return $"Failed to parse value '{valueString}' for type {targetType.Name}";
                }
            }
            catch (Exception ex)
            {
                return $"Error setting variable value: {ex.Message}";
            }
        }

        private static Vector2? ParseVector2(string valueString)
        {
            try
            {
                // Remove parentheses and split by comma
                var cleanValue = valueString.Trim('(', ')', ' ');
                var parts = cleanValue.Split(',');
                if (parts.Length == 2)
                {
                    if (float.TryParse(parts[0].Trim(), out float x) && 
                        float.TryParse(parts[1].Trim(), out float y))
                    {
                        return new Vector2(x, y);
                    }
                }
            }
            catch { }
            return null;
        }

        private static Vector3? ParseVector3(string valueString)
        {
            try
            {
                var cleanValue = valueString.Trim('(', ')', ' ');
                var parts = cleanValue.Split(',');
                if (parts.Length == 3)
                {
                    if (float.TryParse(parts[0].Trim(), out float x) && 
                        float.TryParse(parts[1].Trim(), out float y) &&
                        float.TryParse(parts[2].Trim(), out float z))
                    {
                        return new Vector3(x, y, z);
                    }
                }
            }
            catch { }
            return null;
        }

        private static Vector4? ParseVector4(string valueString)
        {
            try
            {
                var cleanValue = valueString.Trim('(', ')', ' ');
                var parts = cleanValue.Split(',');
                if (parts.Length == 4)
                {
                    if (float.TryParse(parts[0].Trim(), out float x) && 
                        float.TryParse(parts[1].Trim(), out float y) &&
                        float.TryParse(parts[2].Trim(), out float z) &&
                        float.TryParse(parts[3].Trim(), out float w))
                    {
                        return new Vector4(x, y, z, w);
                    }
                }
            }
            catch { }
            return null;
        }

        private static Color? ParseColor(string valueString)
        {
            try
            {
                // Handle RGBA format: RGBA(r,g,b,a)
                if (valueString.StartsWith("RGBA(") && valueString.EndsWith(")"))
                {
                    var cleanValue = valueString.Substring(5, valueString.Length - 6);
                    var parts = cleanValue.Split(',');
                    if (parts.Length == 4)
                    {
                        if (float.TryParse(parts[0].Trim(), out float r) && 
                            float.TryParse(parts[1].Trim(), out float g) &&
                            float.TryParse(parts[2].Trim(), out float b) &&
                            float.TryParse(parts[3].Trim(), out float a))
                        {
                            return new Color(r, g, b, a);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        #endregion
    }
}
