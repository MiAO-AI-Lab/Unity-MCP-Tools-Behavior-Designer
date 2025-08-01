#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
using com.MiAO.Unity.MCP.Common;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;



namespace com.MiAO.Unity.MCP.BehaviorDesignerTools
{
    // TODO: list all available tasks
    public partial class Tool_BehaviorDesigner
    {
        [McpPluginTool
        (
            "BehaviorDesigner_GenerateBehaviorTree",
            Title = "Generate BehaviorTree from DSL - Create complex behavior trees using simple DSL syntax"
        )]
        [Description(@"Generate a complete BehaviorTree from Domain-Specific Language (DSL) input.

DSL Syntax:
TaskType ""FriendlyName"" {
  ChildTaskType ""ChildName"" { param: value }
  // Support nested structure
}

Example:
Sequence ""MainBehavior"" {
  Selector ""CombatLogic"" {
    Wait ""PauseBeforeAttack"" { duration: 2.0 }
    Log ""AttackLog"" { text: ""Attacking enemy!"" }
  }
  Log ""PostCombatLog"" { text: ""Combat finished"" }
  Wait ""CooldownWait"" { duration: 1.0 }
}

Supported TaskTypes: 
- Composite: Sequence, Selector, Parallel, RandomSelector, RandomSequence
- Decorator: UntilFailure, UntilSuccess, Repeat, Inverter
- Action: Wait, Log, (and other custom action tasks)
- Conditional: (custom conditional tasks)

Common Parameters:
- duration: float (for Wait tasks)
- text: string (for Log tasks)
- logType: LogType (for Log tasks: Info, Warning, Error)
- repeatForever: bool (for Repeat tasks)
- count: int (for Repeat tasks)")]
        public static string GenerateBehaviorTree
        (
            [Description("Asset path to the BehaviorDesigner ExternalBehavior file. Starts with 'Assets/'. Ends with '.asset'.")]
            string assetPath,
            [Description("DSL script content defining the behavior tree structure and nodes")]
            string dslScript,
            [Description("Whether to clear existing behavior tree before generating new one")]
            bool clearExisting = true,
            [Description("Whether to auto-layout the generated behavior tree")]
            bool autoLayout = true
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dslScript))
                    return "[Error] DSL script cannot be empty.";

                // Load BehaviorSource with error handling
                var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
                
                // Validate loaded data
                var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
                if (!string.IsNullOrEmpty(validationError))
                    return validationError;

                // Clear existing tree if requested
                if (clearExisting)
                {
                    behaviorSource.RootTask = null;
                    behaviorSource.DetachedTasks?.Clear();
                }

                // Parse DSL and generate behavior tree
                var parser = new BehaviorTreeDSLParser();
                var parseResult = parser.Parse(dslScript);
                
                if (!parseResult.Success)
                    return $"[Error] DSL parsing failed: {parseResult.ErrorMessage}";

                // Generate behavior tree from parsed structure
                var generator = new BehaviorTreeGenerator(behaviorSource);
                var generateResult = generator.GenerateFromNode(parseResult.RootNode);
                
                if (!generateResult.Success)
                {
                    Debug.LogError(generateResult.ErrorMessage);
                    return $"[Error] Generation failed: {generateResult.ErrorMessage}";
                }

                // Auto layout if requested
                if (autoLayout && behaviorSource.RootTask != null)
                {
                    RepositionEntryAndRootTask(behaviorSource);
                    PerformAutoLayout(behaviorSource.RootTask);
                }

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save behavior tree");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                if (string.IsNullOrEmpty(generateResult.ErrorMessage))
                    return $"[Success] Generated behavior tree with {generateResult.NodesCreated} nodes from DSL script.";
                else
                    return $"[Success] Generated behavior tree with {generateResult.NodesCreated} nodes from DSL script. \n=================\n{generateResult.ErrorMessage}";
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.StackTrace);
                return Error.FailedToOperate("generate behavior tree from DSL", ex.Message);
            }
        }
    }

    #region DSL Parser

    public class BehaviorTreeDSLParser
    {
        public class ParseResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = "";
            public DSLNode RootNode { get; set; }
        }

        public class DSLNode
        {
            public string TaskType { get; set; } = "";
            public string FriendlyName { get; set; } = "";
            public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
            public List<DSLNode> Children { get; set; } = new List<DSLNode>();
        }

        public ParseResult Parse(string dslScript)
        {
            try
            {
                var tokens = Tokenize(dslScript);
                var rootNode = ParseNode(tokens, 0, out int endIndex);
                
                return new ParseResult
                {
                    Success = true,
                    RootNode = rootNode
                };
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message + "\n=================\n" + ex.StackTrace);
                return new ParseResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        internal static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            var regex = new Regex(@"""[^""]*""|[{}:,]|[^\s{}:,""]+");
            
            foreach (Match match in regex.Matches(input))
            {
                var token = match.Value.Trim();
                if (!string.IsNullOrEmpty(token))
                    tokens.Add(token);
            }
            Debug.Log($"Tokens: {string.Join(", ", tokens)}");
            return tokens;
        }

        private DSLNode ParseNode(List<string> tokens, int startIndex, out int endIndex)
        {
            endIndex = startIndex;
            
            if (startIndex >= tokens.Count)
                throw new Exception("Unexpected end of input");

            var node = new DSLNode();
            
            // Parse TaskType
            node.TaskType = tokens[startIndex];
            endIndex++;
            
            // Parse FriendlyName (optional quoted string)
            if (endIndex < tokens.Count && tokens[endIndex].StartsWith("\""))
            {
                node.FriendlyName = tokens[endIndex].Trim('"');
                endIndex++;
            }
            else
            {
                node.FriendlyName = node.TaskType;
            }
            
            // Parse opening brace and content
            if (endIndex < tokens.Count && tokens[endIndex] == "{")
            {
                endIndex++; // Skip opening brace
                
                while (endIndex < tokens.Count && tokens[endIndex] != "}")
                {
                    // Check if this is a parameter or a child node
                    if (IsParameter(tokens, endIndex))
                    {
                        ParseParameter(tokens, ref endIndex, node.Parameters);
                    }
                    else
                    {
                        // Parse child node
                        var childNode = ParseNode(tokens, endIndex, out endIndex);
                        node.Children.Add(childNode);
                    }
                }
                
                if (endIndex < tokens.Count && tokens[endIndex] == "}")
                {
                    endIndex++; // Skip closing brace
                }
            }
            
            return node;
        }

        private bool IsParameter(List<string> tokens, int index)
        {
            // A parameter is in format: paramName: value
            return index + 2 < tokens.Count && tokens[index + 1] == ":";
        }

        internal static void ParseParameter(List<string> tokens, ref int index, Dictionary<string, object> parameters)
        {
            if (index + 2 >= tokens.Count)
                throw new Exception("Invalid parameter format");

            string paramName = tokens[index];
            if (paramName.StartsWith("\"") && paramName.EndsWith("\""))
                paramName = paramName.Substring(1, paramName.Length - 2);

            string colon = tokens[index + 1];
            string paramValue = tokens[index + 2];

            if (colon != ":")
                throw new Exception($"Expected ':' after parameter name '{paramName}'");

            // Parse parameter value
            object value = ParseParameterValue(paramValue);
            parameters[paramName] = value;
            
            index += 3;
        }

        private static object ParseParameterValue(string valueStr)
        {
            // Remove quotes if present
            if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
            {
                return valueStr.Substring(1, valueStr.Length - 2);
            }
            
            // Try to parse as number
            if (int.TryParse(valueStr, out int intValue))
                return intValue;
                
            if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                return floatValue;
                
            // Try to parse as boolean
            if (bool.TryParse(valueStr, out bool boolValue))
                return boolValue;
                
            // Return as string
            return valueStr;
        }
    }

    #endregion

    #region Behavior Tree Generator

    public class BehaviorTreeGenerator
    {
        private BehaviorSource _behaviorSource;
        private int _nextTaskId = 1;

        public class GenerateResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = "";
            public int NodesCreated { get; set; }
        }

        public BehaviorTreeGenerator(BehaviorSource behaviorSource)
        {
            _behaviorSource = behaviorSource;
            _nextTaskId = GetNextAvailableTaskId(behaviorSource);
        }

        public GenerateResult GenerateFromNode(BehaviorTreeDSLParser.DSLNode rootNode)
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                int nodesCreated = 0;
                var rootTask = CreateTaskFromNode(rootNode, ref nodesCreated, sb);
                
                if (rootTask == null)
                    return new GenerateResult { Success = false, ErrorMessage = "Failed to create root task" };

                // Set as root task
                _behaviorSource.RootTask = rootTask;

                // Create entry task if it doesn't exist
                if (_behaviorSource.EntryTask == null)
                {
                    var entryTask = Tool_BehaviorDesigner.CreateEntryTask();
                    if (entryTask != null)
                    {
                        _behaviorSource.EntryTask = entryTask;
                        nodesCreated++;
                    }
                }

                return new GenerateResult { Success = true, NodesCreated = nodesCreated, ErrorMessage = sb.ToString() };
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message + "\n=================\n" + ex.StackTrace);
                return new GenerateResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private BehaviorDesigner.Runtime.Tasks.Task CreateTaskFromNode(BehaviorTreeDSLParser.DSLNode node, ref int nodesCreated, StringBuilder sb)
        {
            // Create task instance
            var taskType = Tool_BehaviorDesigner.FindTaskType(node.TaskType);
            if (taskType == null)
                throw new Exception($"Task type '{node.TaskType}' not found");

            var task = Activator.CreateInstance(taskType) as BehaviorDesigner.Runtime.Tasks.Task;
            if (task == null)
                throw new Exception($"Failed to create instance of task type '{node.TaskType}'");

            // Set basic properties
            task.ID = _nextTaskId++;
            task.FriendlyName = node.FriendlyName;
            task.NodeData = new NodeData();
            task.NodeData.Offset = Vector2.zero; // Will be set by auto layout

            nodesCreated++;

            // Set task parameters
            SetTaskParameters(task, node.Parameters, sb);

            // Create children if this is a composite task
            if (node.Children.Count > 0)
            {
                if (!CanTaskAcceptChildren(task))
                    throw new Exception($"Task type '{node.TaskType}' cannot accept children");

                var children = new List<BehaviorDesigner.Runtime.Tasks.Task>();
                foreach (var childNode in node.Children)
                {
                    var childTask = CreateTaskFromNode(childNode, ref nodesCreated, sb);
                    children.Add(childTask);
                }

                // Set children using reflection
                SetTaskChildren(task, children);
            }

            return task;
        }


        public static void SetTaskParameters(BehaviorDesigner.Runtime.Tasks.Task task, Dictionary<string, object> parameters, StringBuilder sb)
        {
            var taskType = task.GetType();
            
            foreach (var param in parameters)
            {
                try
                {
                    SetTaskParameter(task, taskType, param.Key, param.Value);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[Warning] Failed to set parameter '{param.Key}' on task '{task.FriendlyName}': {ex.Message}");
                    Debug.LogWarning($"Failed to set parameter '{param.Key}' on task '{task.FriendlyName}': {ex.Message} \n=================\n{ex.StackTrace}");
                }
            }
        }

        private static void SetTaskParameter(BehaviorDesigner.Runtime.Tasks.Task task, Type taskType, string paramName, object value)
        {
            // Try to find field
            var field = taskType.GetField(paramName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                var convertedValue = ConvertParameterValue(value, field.FieldType);
                field.SetValue(task, convertedValue);
                return;
            }

            // Try to find property
            var property = taskType.GetProperty(paramName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                var convertedValue = ConvertParameterValue(value, property.PropertyType);
                property.SetValue(task, convertedValue);
                return;
            }

            throw new Exception($"Parameter '{paramName}' not found on task type '{taskType.Name}'");
        }

        private static object ConvertParameterValue(object value, Type targetType)
        {
            if (value == null) return null;

            // Handle SharedVariable types
            if (targetType.IsSubclassOf(typeof(SharedVariable)))
            {
                var sharedVar = Activator.CreateInstance(targetType) as SharedVariable;
                if (sharedVar != null)
                {
                    // Get the Value property and set it
                    var valueProperty = targetType.GetProperty("Value");
                    if (valueProperty != null)
                    {
                        var convertedValue = ConvertParameterValue(value, valueProperty.PropertyType);
                        valueProperty.SetValue(sharedVar, convertedValue);
                    }
                    return sharedVar;
                }
            }

            // Handle enum types
            if (targetType.IsEnum)
            {
                if (value is string stringValue)
                {
                    return Enum.Parse(targetType, stringValue, true);
                }
            }

            // Handle LogType specifically
            if (targetType == typeof(LogType) && value is string logTypeStr) 
            {
                return Enum.Parse(typeof(LogType), logTypeStr, true);
            }

            // Direct type conversion
            if (targetType.IsAssignableFrom(value.GetType()))
                return value;

            // Try Convert.ChangeType
            try
            {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Cannot convert value '{value}' to type '{targetType.Name}' \n=================\n{ex.StackTrace}");
                throw new Exception($"Cannot convert value '{value}' to type '{targetType.Name}'");
            }
        }

        private static void SetTaskChildren(BehaviorDesigner.Runtime.Tasks.Task parentTask, List<BehaviorDesigner.Runtime.Tasks.Task> children)
        {
            var fields = parentTask.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(List<BehaviorDesigner.Runtime.Tasks.Task>))
                {
                    field.SetValue(parentTask, children);
                    break;
                }
            }
        }

        private static bool CanTaskAcceptChildren(BehaviorDesigner.Runtime.Tasks.Task task)
        {
            // Use the existing method from the main class
            return Tool_BehaviorDesigner.CanTaskAcceptChildren(task);
        }

        private int GetNextAvailableTaskId(BehaviorSource behaviorSource)
        {
            // Use the existing method from the main class
            return Tool_BehaviorDesigner.GetNextAvailableTaskId(behaviorSource);
        }
    }

    #endregion
}
