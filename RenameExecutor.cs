using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SolidWorks.Interop.sldworks;

namespace SolidWorksTeamRenameTool
{
    internal sealed class RenameExecutor
    {
        private readonly SldWorks _swApp;
        private readonly List<string> _diagnostics = new List<string>();
        private Dictionary<string, List<object>> _componentsByPathCache;

        public RenameExecutor(object swApp)
        {
            _swApp = swApp as SldWorks;
        }

        public IList<string> Diagnostics
        {
            get { return _diagnostics.AsReadOnly(); }
        }

        public void Execute(IList<RenameTask> tasks)
        {
            _diagnostics.Clear();
            AddDiagnostic("设计树重命名模式：模拟手工 FeatureManager 改名。");

            RenameTask topTask = tasks.FirstOrDefault(t => t.IsTop);
            ModelDoc2 topModel = ResolveTopModel(topTask);
            if (topModel == null)
            {
                MarkError(topTask, "未找到当前顶层装配文档。");
                return;
            }

            ClearAllSelections(tasks, topModel);
            IList<RenameTask> executionTasks = BuildExecutionTasks(tasks);
            if (!PreflightDesignTree(executionTasks, topModel))
            {
                AddDiagnostic("预检未通过，停止执行。");
                return;
            }

            ModelDoc2 activeTopModel = ActivateTopDocument(topModel, topTask);
            BuildComponentPathCache(activeTopModel ?? topModel);

            AddDiagnostic("开始重命名非顶层组件，按层级从深到浅。");
            foreach (RenameTask task in executionTasks.Where(t => !t.IsTop).OrderByDescending(t => t.Level).ToList())
            {
                if (!RenameOne(task, activeTopModel ?? topModel))
                {
                    AddDiagnostic("单个组件重命名失败，继续执行后续任务：" + TaskLabel(task));
                }
            }

            AddDiagnostic("开始重命名顶层装配。");
            foreach (RenameTask task in executionTasks.Where(t => t.IsTop).ToList())
            {
                if (!RenameOne(task, activeTopModel ?? topModel))
                {
                    AddDiagnostic("顶层重命名失败，继续保存已成功的组件。");
                }
            }

            if (tasks.Any(t => t.Status == RenameStatus.Renamed || t.Status == RenameStatus.Saved))
            {
                AddDiagnostic("开始保存涉及的装配文档。");
                SaveTopAssemblyOnly(tasks, activeTopModel ?? topModel);
            }
            else
            {
                AddDiagnostic("没有成功重命名的文件，跳过保存。");
            }
        }

        public string WriteCsv(IEnumerable<RenameTask> tasks, string topPath, string prefix)
        {
            string folder = Path.GetDirectoryName(topPath) ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
            string csvPath = Path.Combine(folder, prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

            using (var writer = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("Kind,Status,Code,OldName,NewName,OldPath,NewPath,Reason");
                foreach (RenameTask task in tasks)
                {
                    writer.WriteLine(string.Join(",",
                        Csv(task.Kind.ToString()),
                        Csv(task.Status.ToString()),
                        Csv(task.Code),
                        Csv(Path.GetFileName(task.OldPath ?? string.Empty)),
                        Csv(Path.GetFileName(task.NewPath ?? string.Empty)),
                        Csv(task.OldPath),
                        Csv(task.NewPath),
                        Csv(task.Reason)));
                }
            }

            return csvPath;
        }

        private ModelDoc2 ResolveTopModel(RenameTask topTask)
        {
            ModelDoc2 topModel = topTask == null ? null : topTask.Model;
            if (topModel == null && _swApp != null)
            {
                topModel = _swApp.ActiveDoc as ModelDoc2;
            }

            AddDiagnostic("顶层文档对象：" + TypeName(topModel));
            return topModel;
        }

        private ModelDoc2 ActivateTopDocument(ModelDoc2 topModel, RenameTask topTask)
        {
            if (_swApp == null || topModel == null)
            {
                AddDiagnostic("激活顶层装配跳过：swApp 或 topModel 为空。");
                return topModel;
            }

            var names = new List<string>();
            if (topTask != null && !string.IsNullOrWhiteSpace(topTask.OldPath))
            {
                names.Add(Path.GetFileName(topTask.OldPath));
                names.Add(Path.GetFileNameWithoutExtension(topTask.OldPath));
            }

            string title = TryGetString(topModel, "GetTitle");
            if (!string.IsNullOrWhiteSpace(title))
            {
                names.Add(title);
            }

            foreach (string name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (TryActivateDoc3(name) || TryActivateDoc2(name) || TryActivateDoc(name))
                {
                    AddDiagnostic("已激活顶层装配：" + name);
                    AddDiagnostic("当前活动文档标题：" + TryGetString(_swApp.ActiveDoc, "GetTitle"));
                    return GetActiveModelDoc() ?? topModel;
                }
            }

            AddDiagnostic("激活顶层装配未确认成功，继续尝试当前上下文。候选名=" + string.Join(" | ", names.ToArray()));
            return GetActiveModelDoc() ?? topModel;
        }

        private ModelDoc2 GetActiveModelDoc()
        {
            object active = null;
            try
            {
                active = _swApp == null ? null : _swApp.ActiveDoc;
            }
            catch
            {
            }

            if (active == null)
            {
                active = TryGetProperty(_swApp, "ActiveDoc");
            }

            AddDiagnostic("ActiveDoc 对象：" + TypeName(active) + "，标题=" + TryGetString(active, "GetTitle"));
            return active as ModelDoc2;
        }

        private bool TryActivateDoc3(string name)
        {
            try
            {
                object[] args = new object[] { name, false, 0, 0 };
                ParameterModifier modifier = new ParameterModifier(4);
                modifier[3] = true;
                object result = _swApp.GetType().InvokeMember(
                    "ActivateDoc3",
                    BindingFlags.InvokeMethod,
                    null,
                    _swApp,
                    args,
                    new ParameterModifier[] { modifier },
                    null,
                    null);
                AddDiagnostic("ActivateDoc3(" + name + ") 返回：" + FormatResult(result) + "，Error=" + FormatResult(args[3]));
                return result != null;
            }
            catch (Exception ex)
            {
                AddDiagnostic("ActivateDoc3(" + name + ") 异常：" + ex.Message);
                return false;
            }
        }

        private bool TryActivateDoc2(string name)
        {
            try
            {
                object[] args = new object[] { name, false, 0 };
                ParameterModifier modifier = new ParameterModifier(3);
                modifier[2] = true;
                object result = _swApp.GetType().InvokeMember(
                    "ActivateDoc2",
                    BindingFlags.InvokeMethod,
                    null,
                    _swApp,
                    args,
                    new ParameterModifier[] { modifier },
                    null,
                    null);
                AddDiagnostic("ActivateDoc2(" + name + ") 返回：" + FormatResult(result) + "，Error=" + FormatResult(args[2]));
                return result != null;
            }
            catch (Exception ex)
            {
                AddDiagnostic("ActivateDoc2(" + name + ") 异常：" + ex.Message);
                return false;
            }
        }

        private bool TryActivateDoc(string name)
        {
            object result = TryInvoke(_swApp, "ActivateDoc", name);
            AddDiagnostic("ActivateDoc(" + name + ") 返回：" + FormatResult(result));
            return result != null;
        }

        private IList<RenameTask> BuildExecutionTasks(IList<RenameTask> tasks)
        {
            var result = new List<RenameTask>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RenameTask task in tasks.Where(t => t.Status == RenameStatus.Pending).ToList())
            {
                string dedupeKey = task.IsVirtual
                    ? "virtual:" + (task.NewFileName ?? string.Empty)
                    : SafeFullPath(task.OldPath);

                if (string.IsNullOrWhiteSpace(dedupeKey))
                {
                    task.Status = RenameStatus.Error;
                    task.Reason = "原路径为空。";
                    continue;
                }

                if (seen.Contains(dedupeKey))
                {
                    task.Status = RenameStatus.Skipped;
                    task.Reason = AppendReason(task.Reason, "相同文件引用已复用。");
                    AddDiagnostic("重复引用跳过：" + TaskLabel(task));
                    continue;
                }

                seen.Add(dedupeKey);
                result.Add(task);
            }

            AddDiagnostic("待执行首次引用数量：" + result.Count);
            return result;
        }

        private bool PreflightDesignTree(IList<RenameTask> tasks, ModelDoc2 topModel)
        {
            AddDiagnostic("预检：设计树模式只改文件名，不移动目录。");
            CheckFeatureManagerRenameOption();
            var executionTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RenameTask task in tasks.ToList())
            {
                string oldPath = SafeFullPath(task.OldPath);
                string newPath = SafeFullPath(task.NewPath);
                string newBaseName = NewBaseName(task);

                AddDiagnostic("预检：" + TaskLabel(task) +
                    "，新基础名=" + newBaseName +
                    "，顶层=" + task.IsTop +
                    "，虚拟件=" + task.IsVirtual +
                    "，组件对象=" + TypeName(task.Component));

                if (task.IsVirtual)
                {
                    if (string.IsNullOrWhiteSpace(newBaseName))
                    {
                        MarkError(task, "预检失败：新基础名为空。");
                        continue;
                    }

                    if (!task.IsTop && task.Component == null)
                    {
                        MarkError(task, "预检失败：非顶层任务没有 Component2。");
                        continue;
                    }

                    string virtualTarget = "virtual:" + newBaseName;
                    if (executionTargets.Contains(virtualTarget))
                    {
                        MarkError(task, "预检失败：本次执行存在重复目标名，请在预览表中将重复项改为跳过。");
                        continue;
                    }

                    executionTargets.Add(virtualTarget);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
                {
                    MarkError(task, "预检失败：原路径或新路径为空。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(newBaseName))
                {
                    MarkError(task, "预检失败：新基础名为空。");
                    continue;
                }

                if (!SameFolder(oldPath, newPath))
                {
                    MarkError(task, "预检失败：设计树模式只允许同文件夹内改名。");
                    continue;
                }

                if (!task.IsTop && task.Component == null)
                {
                    MarkError(task, "预检失败：非顶层任务没有 Component2。");
                    continue;
                }

                if (IsReadOnly(oldPath))
                {
                    MarkError(task, "预检失败：原文件是只读文件。");
                    continue;
                }

                if (executionTargets.Contains(newPath))
                {
                    MarkError(task, "预检失败：本次执行存在重复目标文件名，请在预览表中将重复项改为跳过。");
                    continue;
                }

                executionTargets.Add(newPath);

                if (TargetExistsForDifferentFile(oldPath, newPath))
                {
                    if (!task.UserConfirmedOverwriteTarget)
                    {
                        MarkError(task, "预检失败：目标文件已存在，请在预览表状态列选择修改确认覆盖，或选择跳过。");
                        continue;
                    }

                    if (!MoveTargetToRecycleBin(task, newPath))
                    {
                        continue;
                    }
                }
            }

            if (HasErrors(tasks))
            {
                return false;
            }

            AddDiagnostic("预检通过。");
            return true;
        }

        private void CheckFeatureManagerRenameOption()
        {
            if (_swApp == null)
            {
                return;
            }

            try
            {
                int toggle = ResolveUserPreferenceToggle("swFeatureManagerEnableRenamingComponent");
                if (toggle < 0)
                {
                    AddDiagnostic("FeatureManager 改名选项检测：无法解析选项值，跳过检测。");
                    return;
                }

                object result = _swApp.GetType().InvokeMember(
                    "GetUserPreferenceToggle",
                    BindingFlags.InvokeMethod,
                    null,
                    _swApp,
                    new object[] { toggle });
                bool enabled = result != null && Convert.ToBoolean(result);
                AddDiagnostic("FeatureManager 改名选项：" + (enabled
                    ? "已开启"
                    : "未开启（请在 工具>选项>系统选项>FeatureManager 勾选“允许从 FeatureManager 重命名组件文件”，否则 RenameDocument 会失败）"));
            }
            catch (Exception ex)
            {
                AddDiagnostic("FeatureManager 改名选项检测异常：" + ex.Message);
            }
        }

        private static int ResolveUserPreferenceToggle(string memberName)
        {
            try
            {
                Type enumType = typeof(SolidWorks.Interop.swconst.swUserPreferenceToggle_e);
                if (Enum.IsDefined(enumType, memberName))
                {
                    return Convert.ToInt32(Enum.Parse(enumType, memberName));
                }
            }
            catch
            {
            }

            return -1;
        }

        private bool RenameOne(RenameTask task, ModelDoc2 activeTopModel)
        {
            string newBaseName = NewBaseName(task);
            AddDiagnostic("重命名开始：" + TaskLabel(task) +
                "，新基础名=" + newBaseName +
                "，顶层=" + task.IsTop);

            if (task.IsVirtual)
            {
                return RenameVirtual(task, newBaseName);
            }

            AddDiagnostic("Rename 使用活动文档标题：" + TryGetString(activeTopModel, "GetTitle") + "，对象=" + TypeName(activeTopModel));
            ClearSelection(activeTopModel);

            if (!task.IsTop)
            {
                UsePreferredComponentInstance(task, activeTopModel);
                bool selected = SelectComponent(task, activeTopModel);
                AddDiagnostic("组件选择结果：" + selected + "，" + TaskLabel(task));
                if (!selected)
                {
                    MarkError(task, "设计树选择组件失败。");
                    return false;
                }
            }
            else
            {
                bool selected = SelectTopRootComponent(activeTopModel);
                AddDiagnostic("顶层根组件选择结果：" + selected + "，" + TaskLabel(task));
                if (!selected)
                {
                    MarkError(task, "设计树选择顶层根组件失败。");
                    return false;
                }
            }

            LogSelectionState(activeTopModel);
            object extension = TryGetProperty(activeTopModel, "Extension");
            AddDiagnostic("RenameDocument Extension 对象：" + TypeName(extension));
            object result = InvokeRenameDocument(extension, newBaseName, task);
            int errorCode = ToInt(result, 0);
            AddDiagnostic("RenameDocument 返回：" + FormatResult(result) + "，解析错误码=" + errorCode + "，" + TaskLabel(task));

            string newFileName = NewFileName(task);
            if (errorCode == 19 &&
                !string.IsNullOrWhiteSpace(newFileName) &&
                !string.Equals(newBaseName, newFileName, StringComparison.OrdinalIgnoreCase))
            {
                AddDiagnostic("RenameDocument 19 后用带扩展名重试：" + newFileName + "，" + TaskLabel(task));
                LogSelectionState(activeTopModel);
                result = InvokeRenameDocument(extension, newFileName, task);
                errorCode = ToInt(result, 0);
                AddDiagnostic("RenameDocument 带扩展名返回：" + FormatResult(result) + "，解析错误码=" + errorCode + "，" + TaskLabel(task));
            }

            if (errorCode != 0)
            {
                Thread.Sleep(350);
                if (task.IsVirtual && VerifyRenameApplied(task, newBaseName))
                {
                    AddDiagnostic("虚拟件 RenameDocument 返回非 0（" + errorCode + "）但名称已更新，视为成功：" + TaskLabel(task));
                }
                else
                {
                    MarkError(task, RenameDocumentErrorReason(errorCode));
                    return false;
                }
            }
            else
            {
                Thread.Sleep(350);
                if (!task.IsVirtual && !VerifyRenameApplied(task, newBaseName))
                {
                    MarkError(task, "RenameDocument 返回 0，但回读名称未匹配，已按失败处理。");
                    return false;
                }
            }

            task.Status = RenameStatus.Renamed;
            task.Reason = "设计树重命名成功。";
            return true;
        }

        private bool RenameVirtual(RenameTask task, string newBaseName)
        {
            object component = task.Component;
            if (component == null)
            {
                MarkError(task, "虚拟件没有 Component2。");
                return false;
            }

            try
            {
                component.GetType().InvokeMember(
                    "Name2",
                    BindingFlags.SetProperty,
                    null,
                    component,
                    new object[] { newBaseName });
            }
            catch (Exception ex)
            {
                AddDiagnostic("虚拟件 Name2 设置异常：" + ex.Message + "，" + TaskLabel(task));
                MarkError(task, "虚拟件改名异常：" + ex.Message);
                return false;
            }

            task.Status = RenameStatus.Renamed;
            task.Reason = "虚拟件改名成功。";
            AddDiagnostic("虚拟件 Name2 设置成功：" + newBaseName + "，" + TaskLabel(task));
            return true;
        }

        private bool SelectTopRootComponent(ModelDoc2 topModel)
        {
            object configurationManager = TryGetProperty(topModel, "ConfigurationManager");
            AddDiagnostic("顶层 ConfigurationManager：" + TypeName(configurationManager));

            object activeConfiguration = TryGetProperty(configurationManager, "ActiveConfiguration");
            if (activeConfiguration == null)
            {
                activeConfiguration = TryInvoke(configurationManager, "ActiveConfiguration");
            }
            AddDiagnostic("顶层 ActiveConfiguration：" + TypeName(activeConfiguration));

            object rootComponent = TryInvoke(activeConfiguration, "GetRootComponent3", true);
            if (rootComponent == null)
            {
                rootComponent = TryInvoke(activeConfiguration, "GetRootComponent");
            }
            AddDiagnostic("顶层 RootComponent：" + TypeName(rootComponent));

            if (rootComponent == null)
            {
                return false;
            }

            object result = TryInvoke(rootComponent, "Select4", false, null, false);
            AddDiagnostic("Root Select4(false,null,false) 返回：" + FormatResult(result));
            if (IsTrue(result))
            {
                return true;
            }

            result = TryInvoke(rootComponent, "Select4", false, null);
            AddDiagnostic("Root Select4(false,null) 返回：" + FormatResult(result));
            if (IsTrue(result))
            {
                return true;
            }

            result = TryInvoke(rootComponent, "Select2", false, 0);
            AddDiagnostic("Root Select2(false,0) 返回：" + FormatResult(result));
            if (IsTrue(result))
            {
                return true;
            }

            result = TryInvoke(rootComponent, "Select", false);
            AddDiagnostic("Root Select(false) 返回：" + FormatResult(result));
            return IsTrue(result);
        }

        private void UsePreferredComponentInstance(RenameTask task, ModelDoc2 activeTopModel)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.OldPath))
            {
                return;
            }

            List<object> components = CollectComponentsByPath(task.OldPath);
            if (components.Count == 0)
            {
                AddDiagnostic("优先实例选择：未找到同路径实例，继续使用预览组件。" + TaskLabel(task));
                return;
            }

            object preferred = components
                .OrderBy(c => PreferredAttemptOrder(ComponentName2(c)))
                .ThenBy(c => InstanceNumber(ComponentName2(c)))
                .FirstOrDefault();

            if (preferred == null)
            {
                return;
            }

            string oldName = ComponentName2(task.Component);
            string preferredName = ComponentName2(preferred);
            task.Component = preferred as Component2;
            task.CurrentSegment = preferredName;
            AddDiagnostic("优先实例选择：" + oldName + " -> " + preferredName +
                "，同路径实例数=" + components.Count + "，" + TaskLabel(task));
        }

        private void BuildComponentPathCache(ModelDoc2 topModel)
        {
            _componentsByPathCache = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
            object[] components = TryInvoke(topModel, "GetComponents", true) as object[];
            if (components == null)
            {
                AddDiagnostic("组件缓存构建：GetComponents(true) 未返回组件数组。");
                return;
            }

            foreach (object component in components)
            {
                if (component == null) continue;
                string path = SafeFullPath(Convert.ToString(TryInvoke(component, "GetPathName") ?? string.Empty));
                if (string.IsNullOrWhiteSpace(path)) continue;

                List<object> list;
                if (!_componentsByPathCache.TryGetValue(path, out list))
                {
                    list = new List<object>();
                    _componentsByPathCache[path] = list;
                }
                list.Add(component);
            }

            AddDiagnostic("组件路径缓存已构建，条目数=" + _componentsByPathCache.Count);
        }

        private List<object> CollectComponentsByPath(string oldPath)
        {
            if (_componentsByPathCache == null) return new List<object>();

            string normalized = SafeFullPath(oldPath);
            List<object> list;
            if (_componentsByPathCache.TryGetValue(normalized, out list))
                return list;

            return new List<object>();
        }

        private static int PreferredAttemptOrder(string name)
        {
            int number = InstanceNumber(name);
            if (number == 1)
            {
                return 0;
            }

            return number > 1 ? 1 : 2;
        }

        private static int InstanceNumber(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return int.MaxValue;
            }

            int dash = name.LastIndexOf('-');
            if (dash < 0 || dash >= name.Length - 1)
            {
                return int.MaxValue;
            }

            int value;
            return int.TryParse(name.Substring(dash + 1), out value) ? value : int.MaxValue;
        }

        private bool SelectComponent(RenameTask task, ModelDoc2 activeTopModel)
        {
            object component = task.Component;
            if (component == null)
            {
                return false;
            }

            AddComponentSelectionDiagnostics(component, activeTopModel, task);
            if (SelectComponentById(component, activeTopModel, task))
            {
                return true;
            }

            object result = TryInvoke(component, "Select4", false, null, false);
            AddDiagnostic("Select4(false,null,false) 返回：" + FormatResult(result));
            if (IsTrue(result))
            {
                return true;
            }

            result = TryInvoke(component, "Select4", false, null);
            AddDiagnostic("Select4(false,null) 返回：" + FormatResult(result));
            if (IsTrue(result))
            {
                return true;
            }

            result = TryInvoke(component, "Select2", false, 0);
            AddDiagnostic("Select2(false,0) 返回：" + FormatResult(result));
            if (IsTrue(result))
            {
                return true;
            }

            result = TryInvoke(component, "Select", false);
            AddDiagnostic("Select(false) 返回：" + FormatResult(result));
            return IsTrue(result);
        }

        private void AddComponentSelectionDiagnostics(object component, ModelDoc2 activeTopModel, RenameTask task)
        {
            string name2 = ComponentName2(component);
            string selectByIdString = Convert.ToString(TryInvoke(component, "GetSelectByIDString") ?? string.Empty);
            string topTitle = TryGetString(activeTopModel, "GetTitle");

            AddDiagnostic("Component Name2=" + name2 +
                "，GetSelectByIDString=" + selectByIdString +
                "，TopTitle=" + topTitle +
                "，" + TaskLabel(task));
        }

        private bool SelectComponentById(object component, ModelDoc2 activeTopModel, RenameTask task)
        {
            object extension = TryGetProperty(activeTopModel, "Extension");
            if (extension == null)
            {
                AddDiagnostic("SelectByID2 跳过：Extension 为空。");
                return false;
            }

            foreach (string candidate in ComponentSelectCandidates(component, activeTopModel, task))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                ClearSelection(activeTopModel);
                object result = SelectById2(extension, candidate, "COMPONENT");
                AddDiagnostic("SelectByID2 COMPONENT [" + candidate + "] 返回：" + FormatResult(result));
                if (IsTrue(result))
                {
                    AddDiagnostic("SelectByID2 选中组件成功：" + candidate + "，" + TaskLabel(task));
                    return true;
                }
            }

            ClearSelection(activeTopModel);
            return false;
        }

        private object SelectById2(object extension, string candidate, string objectType)
        {
            try
            {
                ModelDocExtension typedExtension = extension as ModelDocExtension;
                if (typedExtension != null)
                {
                    return typedExtension.SelectByID2(candidate, objectType, 0.0, 0.0, 0.0, false, 0, null, 0);
                }
            }
            catch (Exception ex)
            {
                AddDiagnostic("SelectByID2 强类型异常 [" + candidate + "]：" + ex.Message);
            }

            try
            {
                return extension.GetType().InvokeMember(
                    "SelectByID2",
                    BindingFlags.InvokeMethod,
                    null,
                    extension,
                    new object[]
                    {
                        candidate,
                        objectType,
                        0.0,
                        0.0,
                        0.0,
                        false,
                        0,
                        null,
                        0
                    });
            }
            catch (TargetInvocationException ex)
            {
                string message = ex.InnerException == null ? ex.Message : ex.InnerException.Message;
                AddDiagnostic("SelectByID2 反射目标异常 [" + candidate + "]：" + message);
                return null;
            }
            catch (Exception ex)
            {
                AddDiagnostic("SelectByID2 反射异常 [" + candidate + "]：" + ex.Message);
                return null;
            }
        }

        private IEnumerable<string> ComponentSelectCandidates(object component, ModelDoc2 activeTopModel, RenameTask task)
        {
            var candidates = new List<string>();
            string selectByIdString = Convert.ToString(TryInvoke(component, "GetSelectByIDString") ?? string.Empty);
            string name2 = ComponentName2(component);
            string topTitle = TryGetString(activeTopModel, "GetTitle");
            string topBase = Path.GetFileNameWithoutExtension(topTitle ?? string.Empty);
            string oldBase = Path.GetFileNameWithoutExtension(task.OldPath ?? string.Empty);

            AddCandidate(candidates, selectByIdString);
            AddCandidate(candidates, name2);
            AddCandidate(candidates, name2 + "@" + topBase);
            AddCandidate(candidates, name2 + "@" + topTitle);
            AddCandidate(candidates, oldBase + "-1@" + topBase);
            AddCandidate(candidates, oldBase + "-1@" + topTitle);

            return candidates;
        }

        private static void AddCandidate(ICollection<string> candidates, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!candidates.Contains(value))
            {
                candidates.Add(value);
            }
        }

        private static string ComponentName2(object component)
        {
            object value = TryGetProperty(component, "Name2");
            if (value == null)
            {
                value = TryInvoke(component, "Name2");
            }

            return value == null ? string.Empty : Convert.ToString(value);
        }

        private void LogSelectionState(ModelDoc2 model)
        {
            object selectionManager = TryGetProperty(model, "SelectionManager");
            AddDiagnostic("SelectionManager 对象：" + TypeName(selectionManager));
            if (selectionManager == null)
            {
                return;
            }

            object count = TryInvoke(selectionManager, "GetSelectedObjectCount2", -1);
            AddDiagnostic("选择集数量 GetSelectedObjectCount2(-1)=" + FormatResult(count));

            int selectedCount = ToInt(count, 0);
            int max = Math.Min(selectedCount, 3);
            for (int i = 1; i <= max; i++)
            {
                object type = TryInvoke(selectionManager, "GetSelectedObjectType3", i, -1);
                object selectedObject = TryInvoke(selectionManager, "GetSelectedObject6", i, -1);
                AddDiagnostic("选择项 " + i + " 类型=" + FormatResult(type) + "，对象=" + TypeName(selectedObject));
            }
        }

        private object InvokeRenameDocument(object extension, string newBaseName, RenameTask task)
        {
            if (extension == null)
            {
                MarkError(task, "RenameDocument 失败：ModelDocExtension 为空。");
                return -1;
            }

            try
            {
                return extension.GetType().InvokeMember(
                    "RenameDocument",
                    BindingFlags.InvokeMethod,
                    null,
                    extension,
                    new object[] { newBaseName });
            }
            catch (Exception ex)
            {
                AddDiagnostic("RenameDocument 异常：" + ex.Message);
                MarkError(task, "RenameDocument 异常：" + ex.Message);
                return -1;
            }
        }

        private static string RenameDocumentErrorReason(int errorCode)
        {
            if (errorCode == 19)
            {
                return "RenameDocument 返回错误：19（SolidWorks 拒绝设计树改名；常见于镜像/派生/受限组件，可在预览表改为跳过后继续执行）。";
            }

            return "RenameDocument 返回错误：" + errorCode + "。";
        }

        private bool VerifyRenameApplied(RenameTask task, string newBaseName)
        {
            if (task == null || string.IsNullOrWhiteSpace(newBaseName))
            {
                return false;
            }

            if (task.IsTop)
            {
                string modelTitle = TryGetString(task.Model, "GetTitle");
                string activeTitle = TryGetString(GetActiveModelDoc(), "GetTitle");
                AddDiagnostic("顶层改名验证：ModelTitle=" + modelTitle + "，ActiveTitle=" + activeTitle + "，期望=" + newBaseName);
                return NameMatches(modelTitle, newBaseName) || NameMatches(activeTitle, newBaseName);
            }

            object component = task.Component;
            string name2 = ComponentName2(component);
            string pathName = Convert.ToString(TryInvoke(component, "GetPathName") ?? string.Empty);

            if (task.IsVirtual)
            {
                object model = TryInvoke(component, "GetModelDoc2");
                string title = model == null ? string.Empty : TryGetString(model, "GetTitle");
                AddDiagnostic("虚拟件改名验证：Name2=" + name2 + "，Title=" + title + "，期望=" + newBaseName + "，" + TaskLabel(task));
                return NameMatches(name2, newBaseName) || NameMatches(title, newBaseName);
            }

            AddDiagnostic("组件改名验证：Name2=" + name2 + "，PathName=" + pathName + "，期望=" + newBaseName + "，" + TaskLabel(task));
            return NameMatches(name2, newBaseName) || NameMatches(Path.GetFileNameWithoutExtension(pathName), newBaseName);
        }

        private static bool NameMatches(string actual, string expectedBaseName)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expectedBaseName))
            {
                return false;
            }

            string actualBase = Path.GetFileNameWithoutExtension(actual.Trim());
            return actualBase.Equals(expectedBaseName, StringComparison.OrdinalIgnoreCase) ||
                actualBase.StartsWith(expectedBaseName + "-", StringComparison.OrdinalIgnoreCase) ||
                actualBase.StartsWith(expectedBaseName + "<", StringComparison.OrdinalIgnoreCase) ||
                actualBase.IndexOf(expectedBaseName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SaveTopAssemblyOnly(IList<RenameTask> tasks, ModelDoc2 activeTop)
        {
            if (activeTop == null)
            {
                MarkSaveFailure(null, "保存失败：未找到顶层装配文档。");
                return;
            }

            int errors = 0;
            int warnings = 0;
            try
            {
                object rebuildAll = TryInvoke(activeTop, "ForceRebuild3", true);
                AddDiagnostic("Top ForceRebuild3(true) 返回：" + FormatResult(rebuildAll));
                object editRebuild = TryInvoke(activeTop, "EditRebuild3");
                AddDiagnostic("Top EditRebuild3 返回：" + FormatResult(editRebuild));

                bool hasRenamedChild = tasks.Any(t => !t.IsTop && (t.Status == RenameStatus.Renamed || t.Status == RenameStatus.Saved));
                bool ok;
                if (hasRenamedChild)
                {
                    ok = TrySave3(activeTop, 5, "Top Save3(silent+SaveReferenced)", null, out errors, out warnings);
                    if (!ok)
                    {
                        ok = TrySave3(activeTop, 4, "Top Save3(SaveReferenced)", null, out errors, out warnings);
                    }
                }
                else
                {
                    ok = TrySave3(activeTop, 1, "Top Save3(silent)", null, out errors, out warnings);
                    if (!ok)
                    {
                        ok = TrySave3(activeTop, 0, "Top Save3(normal)", null, out errors, out warnings);
                    }
                }

                if (!ok)
                {
                    object save2 = TryInvoke(activeTop, "Save2", true);
                    AddDiagnostic("Top Save2(true) 返回：" + FormatResult(save2));
                    ok = IsTrue(save2);
                }

                if (!ok)
                {
                    object save = TryInvoke(activeTop, "Save");
                    AddDiagnostic("Top Save() 返回：" + FormatResult(save));
                    ok = IsTrue(save);
                }

                if (!ok)
                {
                    MarkSaveFailure(null, "保存顶层装配失败：Error=" + errors + "，Warning=" + warnings + "。");
                    return;
                }

                foreach (RenameTask task in tasks.Where(t => t.Status == RenameStatus.Renamed).ToList())
                {
                    task.Status = RenameStatus.Saved;
                    task.Reason = AppendReason(task.Reason, "已保存顶层装配。");
                }
            }
            catch (Exception ex)
            {
                MarkSaveFailure(null, "保存顶层装配异常：" + ex.Message);
            }
        }

        private bool TrySave3(ModelDoc2 model, int options, string label, RenameTask task, out int errors, out int warnings)
        {
            errors = 0;
            warnings = 0;
            try
            {
                bool ok = model.Save3(options, ref errors, ref warnings);
                AddDiagnostic(label + " 返回：" + ok + "，Error=" + errors + "，Warning=" + warnings + "，" + TaskLabel(task));
                return ok;
            }
            catch (Exception ex)
            {
                AddDiagnostic(label + " 异常：" + ex.Message + "，" + TaskLabel(task));
                return false;
            }
        }

        private void MarkSaveFailure(RenameTask task, string reason)
        {
            if (task != null && (task.Status == RenameStatus.Renamed || task.Status == RenameStatus.Saved))
            {
                task.Reason = AppendReason(task.Reason, reason);
                AddDiagnostic("保存失败但保留重命名完成状态：" + reason + " " + TaskLabel(task));
                return;
            }

            MarkError(task, reason);
        }

        private void ClearAllSelections(IEnumerable<RenameTask> tasks, ModelDoc2 topModel)
        {
            ClearSelection(topModel);
            foreach (RenameTask task in tasks)
            {
                ClearSelection(task.Model);
            }
        }

        private void ClearSelection(ModelDoc2 model)
        {
            try
            {
                if (model != null)
                {
                    model.ClearSelection2(true);
                }
            }
            catch (Exception ex)
            {
                AddDiagnostic("清空选择失败：" + ex.Message);
            }
        }

        private static string NewBaseName(RenameTask task)
        {
            string source = !string.IsNullOrWhiteSpace(task.NewFileName) ? task.NewFileName : Path.GetFileName(task.NewPath ?? string.Empty);
            return Path.GetFileNameWithoutExtension(source ?? string.Empty);
        }

        private static string NewFileName(RenameTask task)
        {
            string source = !string.IsNullOrWhiteSpace(task.NewFileName) ? task.NewFileName : Path.GetFileName(task.NewPath ?? string.Empty);
            return Path.GetFileName(source ?? string.Empty);
        }

        private void MarkError(RenameTask task, string reason)
        {
            if (task != null)
            {
                task.Status = RenameStatus.Error;
                task.Reason = reason;
            }

            AddDiagnostic("错误：" + reason + " " + TaskLabel(task));
        }

        private static bool SameFolder(string leftPath, string rightPath)
        {
            try
            {
                string left = Path.GetDirectoryName(Path.GetFullPath(leftPath)) ?? string.Empty;
                string right = Path.GetDirectoryName(Path.GetFullPath(rightPath)) ?? string.Empty;
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool TargetExistsForDifferentFile(string oldPath, string newPath)
        {
            if (!File.Exists(newPath))
            {
                return false;
            }

            return !string.Equals(SafeFullPath(oldPath), SafeFullPath(newPath), StringComparison.OrdinalIgnoreCase);
        }

        private bool MoveTargetToRecycleBin(RenameTask task, string targetPath)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    targetPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                task.TargetExistsConflict = false;
                task.UserConfirmedOverwriteTarget = false;
                task.Reason = AppendReason(task.Reason, "已有同名目标文件已移到回收站。");
                AddDiagnostic("已有同名目标文件已移到回收站：" + targetPath + " " + TaskLabel(task));
                return true;
            }
            catch (Exception ex)
            {
                MarkError(task, "覆盖失败：无法将已有同名目标文件移到回收站。" + ex.Message);
                return false;
            }
        }

        private static bool IsReadOnly(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path) &&
                    (File.GetAttributes(path) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
            }
            catch
            {
                return false;
            }
        }

        private static object TryGetProperty(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            try
            {
                return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
            }
            catch
            {
                return null;
            }
        }

        private static object TryInvoke(object target, string name, params object[] args)
        {
            if (target == null)
            {
                return null;
            }

            try
            {
                return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetString(object target, string methodName)
        {
            object value = TryInvoke(target, methodName);
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static bool IsTrue(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            if (value == null)
            {
                return false;
            }

            try
            {
                return Convert.ToInt32(value) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static int ToInt(object value, int defaultValue)
        {
            if (value == null)
            {
                return defaultValue;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string FormatResult(object result)
        {
            Array array = result as Array;
            if (array == null)
            {
                return result == null ? "null" : Convert.ToString(result);
            }

            var values = new List<string>();
            foreach (object item in array)
            {
                values.Add(item == null ? "null" : Convert.ToString(item));
            }

            return string.Join(",", values.ToArray());
        }

        private static string TypeName(object value)
        {
            return value == null ? "null" : value.GetType().FullName;
        }

        private static string SafeFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private void AddDiagnostic(string message)
        {
            _diagnostics.Add(DateTime.Now.ToString("HH:mm:ss") + " " + message);
        }

        private static string TaskLabel(RenameTask task)
        {
            if (task == null)
            {
                return string.Empty;
            }

            return "[" + task.Kind + " L" + task.Level + "] " +
                Path.GetFileName(task.OldPath ?? string.Empty) + " -> " +
                Path.GetFileName(task.NewPath ?? string.Empty);
        }

        private static string AppendReason(string current, string addition)
        {
            if (string.IsNullOrWhiteSpace(current))
            {
                return addition;
            }

            return current + " " + addition;
        }

        private static bool HasErrors(IEnumerable<RenameTask> tasks)
        {
            return tasks.Any(t => t.Status == RenameStatus.Error || t.Status == RenameStatus.Conflict);
        }

        private static string Csv(string value)
        {
            string s = value ?? string.Empty;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

    }
}
