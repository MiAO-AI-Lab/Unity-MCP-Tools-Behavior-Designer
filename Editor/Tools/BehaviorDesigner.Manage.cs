#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
using com.MiAO.Unity.MCP.Common;
using UnityEngine;
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
            "BehaviorDesigner_ManageBehaviorSource",
            Title = "Manage BehaviorDesigner BehaviorSource - Read, Add, Delete, Move nodes"
        )]
        [Description(@"Manage comprehensive BehaviorSource operations including:
- read: Read BehaviorDesigner content from asset path and return detailed node hierarchy
- addNode: Add a new node to the BehaviorSource with specified parent and elder-brother task IDs, automatically calculate node offset
- deleteNode: Delete a node by ID and recursively delete all child nodes
- moveNode: Move a node to a new parent with automatic offset calculation, recursively move all child nodes
- autoLayout: Auto layout the BehaviorSource, recursively layout all child nodes of the target task")]
        public string ManageBehaviorSource
        (
            [Description("Operation type: 'read', 'addNode', 'deleteNode', 'moveNode', 'autoLayout', 'listAvailableTaskTypes'")]
            string operation,
            [Description("Asset path to the BehaviorDesigner ExternalBehavior file. Starts with 'Assets/'. Ends with '.asset'.")]
            string assetPath,
            [Description("For addNode/deleteNode/moveNode/autoLayout: Target task ID")]
            int? taskId = null,
            [Description("For addNode/moveNode: the target parent task ID where the node should be attached")]
            int? parentTaskId = null,
            [Description("For addNode/moveNode: Elder-brother task ID (optional) - the node will be placed to the right of this elder-brother task. If not provided, the node will be placed as the leftmost child of the parent task.")]
            int? elderBrotherTaskId = null,
            [Description("For addNode: class name of the task type to create (e.g., 'BehaviorDesigner.Runtime.Tasks.Wait', 'Idle')")]
            string? taskTypeName = null,
            [Description("For addNode: Friendly name for the new task")]
            string? friendlyName = null,
            [Description("For read: Whether to include detailed task serialization information")]
            bool includeDetails = false,
            [Description("For listAvailableTaskTypes: Filter task types by namespace or category name. For example, 'BehaviorDesigner.Runtime.Tasks' or 'Composite'. If not provided, all task types will be listed.")]
            string? taskTypeFilter = null
        )
        {
            return operation.ToLower() switch
            {
                "read" => ReadBehaviorSource(assetPath, includeDetails),
                "addnode" => AddNode(assetPath, parentTaskId, elderBrotherTaskId, taskTypeName, friendlyName),
                "deletenode" => DeleteNode(assetPath, taskId),
                "movenode" => MoveNode(assetPath, taskId, parentTaskId, elderBrotherTaskId),
                "autolayout" => AutoLayout(assetPath, taskId),
                "listavailabletasktypes" => ListAvailableTaskTypes(taskTypeFilter),
                _ => Error.InvalidOperation()
            };
        }

        #region BehaviorSource Management Methods

        public static string ReadBehaviorSource(string assetPath, bool includeDetails)
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
                result.AppendLine($"[Success] BehaviorSource content from: {assetPath}");
                result.AppendLine($"Entry Task: {GetTaskInfo(behaviorSource.EntryTask)}");
                result.AppendLine($"Root Task: {GetTaskInfo(behaviorSource.RootTask)}");
                result.AppendLine($"Detached Tasks Count: {behaviorSource.DetachedTasks?.Count ?? 0}");

                // Check if BehaviorTree is empty
                if (behaviorSource.RootTask == null && (behaviorSource.DetachedTasks == null || behaviorSource.DetachedTasks.Count == 0))
                {
                    result.AppendLine("\n=== Empty BehaviorTree ===");
                    result.AppendLine("This BehaviorTree is empty. You can add the first node by using parentTaskId = -1 or null.");
                }
                else
                {
                    if (behaviorSource.RootTask != null)
                    {
                        result.AppendLine("\n=== Task Hierarchy ===");
                        PrintTaskHierarchy(behaviorSource.RootTask, result, 0, includeDetails);
                    }

                    if (behaviorSource.DetachedTasks?.Count > 0)
                    {
                        result.AppendLine("\n=== Detached Tasks ===");
                        foreach (var task in behaviorSource.DetachedTasks)
                        {
                            PrintTaskHierarchy(task, result, 0, includeDetails);
                        }
                    }
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.StackTrace);
                // Provide helpful error message for empty or corrupted BehaviorTree
                if (ex.Message.Contains("character") || ex.Message.Contains("deserialization"))
                {
                    return $"[Warning] BehaviorSource appears to be empty or corrupted: {ex.Message}\n" +
                           $"This might be an empty BehaviorTree. You can try adding the first node with parentTaskId = -1 or null.";
                }

                return Error.FailedToReadBehaviorSource(ex.Message);
            }
        }

        private static string AddNode(string assetPath, int? parentTaskId, int? elderBrotherTaskId, string? taskTypeName, string? friendlyName)
        {
            // Load BehaviorSource with error handling
            var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Validate loaded data
            var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
            if (!string.IsNullOrEmpty(validationError))
                return validationError;

            // Data processing operations that can run on any thread
            try
            {
                // Special handling for empty BehaviorTree
                bool isEmptyBehaviorTree = behaviorSource.RootTask == null;
                bool createAsRootTask = !parentTaskId.HasValue || parentTaskId.Value == -1 || isEmptyBehaviorTree;

                BehaviorDesigner.Runtime.Tasks.Task parentTask = null;

                if (!createAsRootTask)
                {
                    // Find parent task for normal node addition
                    parentTask = FindTaskById(behaviorSource, parentTaskId.Value);
                    if (parentTask == null)
                        return Error.ParentTaskNotFound(parentTaskId.Value);
                }

                // Create new task using common method
                var taskType = FindTaskType(taskTypeName);
                if (taskType == null)
                    return Error.TaskTypeNotFound(taskTypeName);

                var newTask = Activator.CreateInstance(taskType) as BehaviorDesigner.Runtime.Tasks.Task;
                if (newTask == null)
                    return Error.FailedToCreateTaskInstance(taskTypeName);

                // Set task properties
                newTask.ID = GetNextAvailableTaskId(behaviorSource);
                newTask.FriendlyName = friendlyName ?? taskType.Name;

                // Create NodeData
                newTask.NodeData = new NodeData();

                if (createAsRootTask)
                {
                    // Creating as root task (for empty BehaviorTree or when parentTaskId is -1)
                    newTask.NodeData.Offset = new Vector2(0, 20); // Position at origin

                    if (isEmptyBehaviorTree)
                    {
                        // Set as the root task
                        behaviorSource.RootTask = newTask;

                        // Also create an entry task if it doesn't exist using common method
                        if (behaviorSource.EntryTask == null)
                        {
                            var entryTask = CreateEntryTask();
                            if (entryTask != null)
                            {
                                behaviorSource.EntryTask = entryTask;
                            }
                        }
                    }
                    else
                    {
                        // Adding as detached task when parentTaskId is -1 but root exists
                        if (behaviorSource.DetachedTasks == null)
                            behaviorSource.DetachedTasks = new List<BehaviorDesigner.Runtime.Tasks.Task>();

                        newTask.NodeData.Offset = new Vector2(300, 0); // Position to the right
                        behaviorSource.DetachedTasks.Add(newTask);
                    }
                }
                else
                {
                    // Normal node addition to existing parent
                    newTask.NodeData.Offset = CalculateNodeOffset(parentTask, elderBrotherTaskId, behaviorSource);
                    AddTaskToParent(parentTask, newTask, elderBrotherTaskId);
                }

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save changes");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                // Generate success message
                string successMessage;
                if (createAsRootTask && isEmptyBehaviorTree)
                {
                    successMessage = $"[Success] Created first root task '{newTask.FriendlyName}' (ID: {newTask.ID}) in empty BehaviorTree.";
                }
                else if (createAsRootTask)
                {
                    successMessage = $"[Success] Added detached task '{newTask.FriendlyName}' (ID: {newTask.ID}) to BehaviorTree.";
                }
                else
                {
                    successMessage = $"[Success] Added new task '{newTask.FriendlyName}' (ID: {newTask.ID}) to parent task ID {parentTaskId}.";
                }

                return successMessage;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.StackTrace);
                return Error.FailedToOperate("add a node", ex.Message);
            }
        }

        private static string DeleteNode(string assetPath, int? taskId)
        {
            if (!taskId.HasValue)
                return Error.TaskIdRequire("deleting a node");

            // Load BehaviorSource with error handling
            var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Validate loaded data
            var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
            if (!string.IsNullOrEmpty(validationError))
                return validationError;

            // Data processing operations that can run on any thread
            try
            {
                var taskToDelete = FindTaskById(behaviorSource, taskId.Value);
                if (taskToDelete == null)
                    return Error.TaskNotFound(taskId.Value);

                // Count children that will be deleted
                int deletedCount = CountAllChildTasks(taskToDelete) + 1;

                // Remove task and all children
                RemoveTaskFromBehaviorSource(behaviorSource, taskToDelete);

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save changes");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                return $"[Success] Deleted task ID {taskId} and {deletedCount - 1} child tasks. Total deleted: {deletedCount}.";
            }
            catch (Exception ex)
            {
                return Error.FailedToOperate("delete a node", ex.Message);
            }
        }

        private static string MoveNode(string assetPath, int? taskId, int? parentTaskId, int? elderBrotherTaskId)
        {
            if (!taskId.HasValue)
                return Error.TaskIdRequire("moving a node");

            if (!parentTaskId.HasValue)
                return Error.ParentTaskIdRequired("moving a node");

            // Load BehaviorSource with error handling
            var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Validate loaded data
            var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
            if (!string.IsNullOrEmpty(validationError))
                return validationError;

            // Data processing operations that can run on any thread
            try
            {
                var taskToMove = FindTaskById(behaviorSource, taskId.Value);
                if (taskToMove == null)
                    return Error.TaskNotFound(taskId.Value);

                var newParentTask = FindTaskById(behaviorSource, parentTaskId.Value);
                if (newParentTask == null)
                    return Error.ParentTaskNotFound(parentTaskId.Value);

                // Check if the new parent task can accept children
                if (!CanTaskAcceptChildren(newParentTask))
                    return Error.TaskCannotAcceptChildren(parentTaskId.Value, newParentTask.GetType().Name);

                // Check if trying to move task to its own child (circular reference)
                if (IsTaskDescendantOf(newParentTask, taskToMove))
                    return Error.CircularReferenceDetected();

                // Remove from current parent
                RemoveTaskFromBehaviorSource(behaviorSource, taskToMove);

                // Calculate new offset and position
                Vector2 newOffset = CalculateNodeOffset(newParentTask, elderBrotherTaskId, behaviorSource);
                Vector2 offsetDelta = newOffset - taskToMove.NodeData.Offset;

                // Move task and all its children
                MoveTaskAndChildren(taskToMove, offsetDelta);

                // Add to new parent
                AddTaskToParent(newParentTask, taskToMove, elderBrotherTaskId);

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save changes");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                int movedCount = CountAllChildTasks(taskToMove) + 1;
                return $"[Success] Moved task ID {taskId} and {movedCount - 1} child tasks to parent ID {parentTaskId}. Total moved: {movedCount}.";
            }
            catch (Exception ex)
            {
                return Error.FailedToOperate("move a node", ex.Message);
            }
        }

        public static string ListAvailableTaskTypes(string? taskTypeFilter = null)
        {
            var result = new System.Text.StringBuilder();
            result.AppendLine("[Success] Available task types:");
            
            try
            {
                // Get all related assemblies
                var assemblies = new List<System.Reflection.Assembly>();
                var mainAssembly = typeof(BehaviorDesigner.Runtime.Tasks.Task).Assembly;
                assemblies.Add(mainAssembly);
                
                // Also check other assemblies in the current domain, in case the task types are in different assemblies
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if ((assembly.FullName.Contains("BehaviorDesigner") || 
                         assembly.FullName.Contains("Assembly-CSharp")) && 
                        !assemblies.Contains(assembly))
                    {
                        assemblies.Add(assembly);
                    }
                }

                var allTaskTypes = new List<System.Type>();
                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var allTypes = assembly.GetTypes();
                        
                        var taskTypes = allTypes
                            .Where(t => t.IsSubclassOf(typeof(BehaviorDesigner.Runtime.Tasks.Task)) && !t.IsAbstract)
                            .ToList();
                        
                        allTaskTypes.AddRange(taskTypes);
                    }
                    catch (System.Exception ex)
                    {
                        result.AppendLine($"Failed to get types from assembly {assembly.FullName}: {ex.Message}");
                        UnityEngine.Debug.LogWarning($"Failed to get types from assembly {assembly.FullName}: {ex.Message}");
                    }
                }

                // Dictionary grouped by namespace and category: namespace -> category -> list of type names
                var namespaceCategories = new Dictionary<string, Dictionary<string, List<string>>>();

                // result.AppendLine($"Total task types found: {allTaskTypes.Count}");

                foreach (var type in allTaskTypes)
                {
                    var namespaceName = type.Namespace ?? "Global";
                    var baseType = type.BaseType;

                    if (taskTypeFilter != null && !namespaceName.Contains(taskTypeFilter) && !baseType.Name.Contains(taskTypeFilter))
                        continue;
                    
                    // Ensure the namespace exists
                    if (!namespaceCategories.ContainsKey(namespaceName))
                    {
                        namespaceCategories[namespaceName] = new Dictionary<string, List<string>>();
                    }
                    
                    string categoryName;
                    if (baseType == null)
                    {
                        categoryName = "UnknownBaseType";
                    }
                    else
                    {
                        var baseName = baseType.Name;
                        switch (baseName)
                        {
                            case "Composite":
                                categoryName = "Composite";
                                break;
                            case "Decorator":
                                categoryName = "Decorator";
                                break;
                            case "Action":
                                categoryName = "Action";
                                break;
                            case "Conditional":
                                categoryName = "Conditional";
                                break;
                            default:
                                categoryName = baseName;
                                break;
                        }
                    }
                    
                    // Ensure the category exists
                    if (!namespaceCategories[namespaceName].ContainsKey(categoryName))
                    {
                        namespaceCategories[namespaceName][categoryName] = new List<string>();
                    }
                    
                    namespaceCategories[namespaceName][categoryName].Add(type.Name);
                }

                // Output the classification results
                var sortedNamespaces = namespaceCategories.Keys.OrderBy(ns => ns).ToList();
                
                foreach (var namespaceName in sortedNamespaces)
                {
                    var categories = namespaceCategories[namespaceName];
                    if (categories.Count == 0) continue;
                    
                    result.AppendLine($"\n{namespaceName}:");
                    
                    var sortedCategories = categories.Keys.OrderBy(cat => cat).ToList();
                    foreach (var categoryName in sortedCategories)
                    {
                        var typeNames = categories[categoryName];
                        if (typeNames.Count == 0) continue;
                        
                        result.AppendLine($"  {categoryName}:");
                        typeNames.Sort(); // Sort by alphabetical order
                        foreach (var typeName in typeNames)
                        {
                            result.AppendLine($"    {typeName}");
                        }
                    }
                }

                // If no task types are found, add debugging information
                if (allTaskTypes.Count == 0)
                {
                    result.AppendLine("\nNo task types found. Debugging info:");
                    result.AppendLine($"Task base type: {typeof(BehaviorDesigner.Runtime.Tasks.Task)}");
                    result.AppendLine($"Assembly: {typeof(BehaviorDesigner.Runtime.Tasks.Task).Assembly.FullName}");
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
        private static string AutoLayout(string assetPath, int? taskId)
        {
            // Load BehaviorSource with error handling
            var (behaviorSource, externalBehavior, errorMessage) = LoadBehaviorSourceWithErrorHandling(assetPath);
            
            // Validate loaded data
            var validationError = ValidateLoadedBehaviorSource(behaviorSource, externalBehavior, errorMessage);
            if (!string.IsNullOrEmpty(validationError))
                return validationError;

            // Data processing operations that can run on any thread
            try
            {
                // Determine the root task for layout
                BehaviorDesigner.Runtime.Tasks.Task rootTaskForLayout;
                if (taskId.HasValue)
                {
                    // Layout specific task and its children
                    rootTaskForLayout = FindTaskById(behaviorSource, taskId.Value);
                    if (rootTaskForLayout == null)
                        return Error.TaskNotFound(taskId.Value);
                }
                else
                {
                    // Layout entire behavior tree starting from root
                    rootTaskForLayout = behaviorSource.RootTask;
                    RepositionEntryAndRootTask(behaviorSource);
                    if (rootTaskForLayout == null)
                        return Error.NoRootTaskFound();
                }

                // Perform auto layout
                int layoutCount = PerformAutoLayout(rootTaskForLayout);

                // Save changes with error handling
                var saveError = SaveBehaviorSourceWithErrorHandling(externalBehavior, behaviorSource, "save changes");
                if (!string.IsNullOrEmpty(saveError))
                    return saveError;

                return $"[Success] Auto layout completed. {layoutCount} tasks were repositioned.";
            }
            catch (Exception ex)
            {
                return Error.FailedToOperate("perform auto layout", ex.Message);
            }
        }

        private static int PerformAutoLayout(BehaviorDesigner.Runtime.Tasks.Task task)
        {
            Dictionary<BehaviorDesigner.Runtime.Tasks.Task, int> subtreeWidths = new Dictionary<BehaviorDesigner.Runtime.Tasks.Task, int>();
            
            BuildSubtreeWidths(task, subtreeWidths);
            
            return PerformAutoLayoutWithSubtreeWidth(task, subtreeWidths);
        }

        /// <summary>
        /// Builds the subtree widths for each task
        /// </summary>
        /// <param name="task">The root task to build subtree widths for</param>
        /// <param name="subtreeWidths">The dictionary to store the subtree widths</param>
        private static void BuildSubtreeWidths(BehaviorDesigner.Runtime.Tasks.Task task, Dictionary<BehaviorDesigner.Runtime.Tasks.Task, int> subtreeWidths)
        {
            if (task == null) return;

            var children = GetTaskChildren(task);
            if (children == null || children.Count == 0)
            {
                subtreeWidths[task] = 1;
                return;
            }

            foreach (var child in children)
            {
                BuildSubtreeWidths(child, subtreeWidths);
            }

            int totalWidth = 0;
            foreach (var child in children)
            {
                totalWidth += subtreeWidths[child];
            }

            subtreeWidths[task] = totalWidth;
        }

        /// <summary>
        /// Performs auto layout on a task and all its children recursively
        /// </summary>
        /// <param name="task">The root task to layout</param>
        /// <returns>Number of tasks that were repositioned</returns>
        private static int PerformAutoLayoutWithSubtreeWidth(BehaviorDesigner.Runtime.Tasks.Task task, Dictionary<BehaviorDesigner.Runtime.Tasks.Task, int> subtreeWidths)
        {
            if (task == null) return 0;

            int layoutCount = 0;

            // Get children using the helper method
            var children = GetTaskChildren(task);

            // Layout this task's children first (bottom-up approach)
            if (children != null && children.Count > 0)
            {
                layoutCount += LayoutChildrenHorizontallyWithSubtreeWidth(children, subtreeWidths);
            }

            // Recursively layout all children
            if (children != null)
            {
                foreach (var child in children)
                {
                    layoutCount += PerformAutoLayoutWithSubtreeWidth(child, subtreeWidths);
                }
            }

            return layoutCount;
        }


        private static int LayoutChildrenHorizontallyWithSubtreeWidth(List<BehaviorDesigner.Runtime.Tasks.Task> children, Dictionary<BehaviorDesigner.Runtime.Tasks.Task, int> subtreeWidths)
        {
            if (children == null || children.Count == 0) return 0;
            
            int layoutCount = 0;
            float totalWidth = 0;

            foreach (var child in children)
            {
                totalWidth += subtreeWidths[child] * X_OFFSET;
            }

            float startingX = - totalWidth / 2f;
            float currentX = startingX;

            foreach (var child in children)
            {
                float childWidth = subtreeWidths[child] * X_OFFSET;
                float childX = currentX + childWidth / 2f;
                float childY = Y_OFFSET;

                child.NodeData.Offset = new Vector2(childX, childY);
                layoutCount++;

                currentX += childWidth;
            }

            return layoutCount;
        }

        /// <summary>
        /// Repositions the entry and root task of the behavior source Position the Entry Task to (0, -100) and the Root Task to (0, 0)
        /// </summary>
        /// <param name="behaviorSource">The behavior source to reposition</param>
        private static void RepositionEntryAndRootTask(BehaviorSource behaviorSource)
        {
            if (behaviorSource.EntryTask != null)
            {
                behaviorSource.EntryTask.NodeData.Offset = new Vector2(0, 0);
            }
            if (behaviorSource.RootTask != null)
            {
                behaviorSource.RootTask.NodeData.Offset = new Vector2(0, Y_OFFSET);
            }
        }
        #endregion

    }
}