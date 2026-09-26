using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace SolidWorksTeamRenameTool
{
    internal sealed class RenameDiagnosticResult
    {
        public string TextPath { get; set; }
        public string CsvPath { get; set; }
    }

    internal sealed class RenameDiagnosticRunner
    {
        private readonly SldWorks _swApp;
        private readonly List<string> _log = new List<string>();
        private readonly List<DiagnosticRow> _rows = new List<DiagnosticRow>();
        private ModelDoc2 _activeTopModel;

        public RenameDiagnosticRunner(object swApp)
        {
            _swApp = swApp as SldWorks;
        }

        public RenameDiagnosticResult Run(IList<RenameTask> tasks)
        {
            _log.Clear();
            _rows.Clear();
            Add("高级诊断开始。此模式会真实调用 SolidWorks 改名接口，请仅在测试副本中运行。");

            _activeTopModel = ResolveTopModel(tasks);
            Add("顶层文档：" + TypeName(_activeTopModel) + "，标题=" + TryGetString(_activeTopModel, "GetTitle"));

            var groups = tasks
                .Where(t => !t.IsVirtual && !string.IsNullOrWhiteSpace(t.OldPath))
                .GroupBy(t => SafeFullPath(t.OldPath), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => Path.GetFileName(g.Key))
                .ToList();

            foreach (IGrouping<string, RenameTask> group in groups)
            {
                DiagnoseFileGroup(group.Key, group.ToList());
            }

            foreach (RenameTask task in tasks.Where(t => t.IsVirtual).ToList())
            {
                DiagnoseVirtualComponent(task);
            }

            return WriteFiles();
        }

        private void DiagnoseFileGroup(string oldPath, List<RenameTask> tasks)
        {
            RenameTask representative = tasks.FirstOrDefault();
            string targetName = representative == null ? string.Empty : NewBaseName(representative);
            string asciiName = "TEST_RENAME_" + DateTime.Now.ToString("HHmmss") + "_" + (_rows.Count + 1);

            Add("");
            Add("文件诊断：" + Path.GetFileName(oldPath));
            Add("原路径：" + oldPath);
            Add("目标基础名：" + targetName);
            List<RenameTask> instanceTasks = ExpandComponentInstances(oldPath, representative, tasks);

            Add("只读=" + IsReadOnly(oldPath) +
                "，目标已存在=" + TargetExistsForDifferentFile(oldPath, representative == null ? string.Empty : representative.NewPath) +
                "，预览实例数=" + tasks.Count +
                "，装配树实际实例数=" + instanceTasks.Count);

            List<RenameTask> ordered = instanceTasks
                .OrderBy(t => PreferredAttemptOrder(ComponentName2(t.Component)))
                .ThenBy(t => InstanceNumber(ComponentName2(t.Component)))
                .ToList();

            var attemptedRows = new List<DiagnosticRow>();
            foreach (RenameTask task in ordered)
            {
                DiagnosticRow row = DiagnoseInstance(task, targetName, false, string.Empty);
                attemptedRows.Add(row);
                _rows.Add(row);

                if (row.TargetReturnCode == 0)
                {
                    Add("文件结论：" + Path.GetFileName(oldPath) + " 目标名在实例 " + row.InstanceName + " 上成功。");
                    break;
                }
            }

            if (!attemptedRows.Any(r => r.TargetReturnCode == 0))
            {
                RenameTask asciiTask = ordered.OrderBy(t => InstanceNumber(ComponentName2(t.Component))).FirstOrDefault();
                if (asciiTask != null)
                {
                    DiagnosticRow asciiRow = DiagnoseInstance(asciiTask, asciiName, true, "ASCII 临时名测试");
                    attemptedRows.Add(asciiRow);
                    _rows.Add(asciiRow);
                }
            }

            Add(BuildGroupConclusion(oldPath, attemptedRows));
        }

        private void DiagnoseVirtualComponent(RenameTask task)
        {
            Add("");
            Add("虚拟件诊断：" + (task.OldBaseName ?? string.Empty) + " -> " + NewBaseName(task));

            object component = task.Component;
            if (component == null)
            {
                Add("虚拟件无 Component2，无法诊断。");
                return;
            }

            string shortName = NewBaseName(task);
            if (string.IsNullOrWhiteSpace(shortName))
            {
                Add("虚拟件目标名为空，无法诊断。");
                return;
            }

            bool restoreToggle = false;
            int toggle = ResolveUserPreferenceToggle("swExtRefUpdateCompNames");
            if (toggle >= 0 && GetUserPreferenceToggle(toggle))
            {
                SetUserPreferenceToggle(toggle, false);
                restoreToggle = true;
                Add("已临时关闭 swExtRefUpdateCompNames。");
            }

            try
            {
                component.GetType().InvokeMember(
                    "Name2",
                    BindingFlags.SetProperty,
                    null,
                    component,
                    new object[] { shortName });
                Add("虚拟件 Name2 设置成功：" + shortName);
            }
            catch (Exception ex)
            {
                Add("虚拟件 Name2 设置异常：" + ex.Message);
            }
            finally
            {
                if (restoreToggle)
                {
                    SetUserPreferenceToggle(toggle, true);
                    Add("已恢复 swExtRefUpdateCompNames。");
                }
            }

            string nameAfter = ComponentName2(component);
            Add("虚拟件改名后 Name2=" + nameAfter + "，验证=" + NameMatches(nameAfter, shortName));
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

        private bool GetUserPreferenceToggle(int option)
        {
            if (_swApp == null)
            {
                return false;
            }

            try
            {
                object result = _swApp.GetType().InvokeMember(
                    "GetUserPreferenceToggle",
                    BindingFlags.InvokeMethod,
                    null,
                    _swApp,
                    new object[] { option });
                return result != null && Convert.ToBoolean(result);
            }
            catch (Exception ex)
            {
                Add("GetUserPreferenceToggle 异常：" + ex.Message);
                return false;
            }
        }

        private bool SetUserPreferenceToggle(int option, bool on)
        {
            if (_swApp == null)
            {
                return false;
            }

            try
            {
                object result = _swApp.GetType().InvokeMember(
                    "SetUserPreferenceToggle",
                    BindingFlags.InvokeMethod,
                    null,
                    _swApp,
                    new object[] { option, on });
                return result != null && Convert.ToBoolean(result);
            }
            catch (Exception ex)
            {
                Add("SetUserPreferenceToggle 异常：" + ex.Message);
                return false;
            }
        }

        private List<RenameTask> ExpandComponentInstances(string oldPath, RenameTask representative, List<RenameTask> fallbackTasks)
        {
            if (representative == null || representative.IsTop)
            {
                return fallbackTasks;
            }

            var result = new List<RenameTask>();
            foreach (object component in CollectComponentsByPath(oldPath))
            {
                result.Add(CloneForComponent(representative, component));
            }

            if (result.Count == 0)
            {
                Add("警告：未能从装配树重新收集实例，退回使用预览任务实例。");
                return fallbackTasks;
            }

            Add("收集到实例：" + string.Join(" | ", result.Select(t => ComponentName2(t.Component)).ToArray()));
            return result;
        }

        private IEnumerable<object> CollectComponentsByPath(string oldPath)
        {
            var result = new List<object>();
            if (_activeTopModel == null || string.IsNullOrWhiteSpace(oldPath))
            {
                return result;
            }

            string normalized = SafeFullPath(oldPath);
            object[] components = TryInvoke(_activeTopModel, "GetComponents", true) as object[];
            if (components == null)
            {
                Add("GetComponents(true) 未返回组件数组。");
                return result;
            }

            foreach (object component in components)
            {
                if (component == null)
                {
                    continue;
                }

                string path = Convert.ToString(TryInvoke(component, "GetPathName") ?? string.Empty);
                if (string.Equals(SafeFullPath(path), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(component);
                }
            }

            return result;
        }

        private static RenameTask CloneForComponent(RenameTask source, object component)
        {
            return new RenameTask
            {
                Kind = source.Kind,
                Status = source.Status,
                Model = source.Model,
                Component = component as Component2,
                Code = source.Code,
                FullCode = source.FullCode,
                CurrentSegment = ComponentName2(component),
                ParentPath = source.ParentPath,
                OldPath = source.OldPath,
                NewPath = source.NewPath,
                NewFileName = source.NewFileName,
                OldBaseName = source.OldBaseName,
                Reason = source.Reason,
                IsTop = source.IsTop,
                Level = source.Level,
                DuplicateCount = source.DuplicateCount
            };
        }

        private DiagnosticRow DiagnoseInstance(RenameTask task, string testName, bool asciiTest, string note)
        {
            var row = new DiagnosticRow();
            row.FileName = Path.GetFileName(task.OldPath ?? string.Empty);
            row.TargetFileName = Path.GetFileName(task.NewPath ?? string.Empty);
            row.TestName = testName;
            row.TestKind = asciiTest ? "ASCII" : "正式目标";
            row.Note = note;
            row.InstanceName = task.IsTop ? "(顶层)" : ComponentName2(task.Component);
            row.SelectByIdString = task.IsTop ? "(顶层根组件)" : Convert.ToString(TryInvoke(task.Component, "GetSelectByIDString") ?? string.Empty);
            row.PathNameBefore = task.IsTop ? Convert.ToString(task.OldPath ?? string.Empty) : Convert.ToString(TryInvoke(task.Component, "GetPathName") ?? string.Empty);

            Add("测试实例：" + row.InstanceName + "，测试名=" + testName + "，类型=" + row.TestKind);

            ModelDoc2 activeModel = ActivateTopDocument();
            ClearSelection(activeModel);
            row.SelectOk = task.IsTop ? SelectTopRoot(activeModel) : SelectComponent(task.Component, activeModel, row.SelectByIdString);
            FillSelectionState(activeModel, row);

            if (!row.SelectOk)
            {
                row.Conclusion = "选择对象问题";
                Add("选择失败：" + row.InstanceName);
                return row;
            }

            object extension = TryGetProperty(activeModel, "Extension");
            row.TargetReturnCode = ToInt(InvokeRenameDocument(extension, testName), -999);
            row.NameAfter = task.IsTop ? TryGetString(activeModel, "GetTitle") : ComponentName2(task.Component);
            row.PathNameAfter = task.IsTop ? Convert.ToString(task.NewPath ?? string.Empty) : Convert.ToString(TryInvoke(task.Component, "GetPathName") ?? string.Empty);
            row.VerifyOk = NameMatches(row.NameAfter, testName) || NameMatches(Path.GetFileNameWithoutExtension(row.PathNameAfter), testName);

            Add("RenameDocument 返回=" + row.TargetReturnCode +
                "，回读名称=" + row.NameAfter +
                "，回读路径=" + row.PathNameAfter +
                "，验证=" + row.VerifyOk);

            if (row.TargetReturnCode == 0)
            {
                SaveActiveDocument(activeModel, row);
            }

            row.Conclusion = RowConclusion(row);
            return row;
        }

        private string BuildGroupConclusion(string oldPath, IList<DiagnosticRow> rows)
        {
            bool anySelectOk = rows.Any(r => r.SelectOk);
            bool anyTargetOk = rows.Any(r => r.TestKind == "正式目标" && r.TargetReturnCode == 0);
            bool anyTargetFail = rows.Any(r => r.TestKind == "正式目标" && r.SelectOk && r.TargetReturnCode != 0);
            bool anyAsciiOk = rows.Any(r => r.TestKind == "ASCII" && r.TargetReturnCode == 0);
            bool anySaveFail = rows.Any(r => r.TargetReturnCode == 0 && !r.SaveOk);

            string result;
            if (!anySelectOk)
            {
                result = "选择对象问题";
            }
            else if (anyTargetOk && anyTargetFail)
            {
                result = "重复实例限制";
            }
            else if (!anyTargetOk && anyAsciiOk)
            {
                result = "名称格式问题";
            }
            else if ((anyTargetOk || anyAsciiOk) && anySaveFail)
            {
                result = "保存阶段问题";
            }
            else if (!anyTargetOk && !anyAsciiOk)
            {
                result = "文档状态限制";
            }
            else
            {
                result = "目标名可改名";
            }

            string best = rows.Where(r => r.TargetReturnCode == 0).Select(r => r.InstanceName).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(best))
            {
                result += "；建议实例=" + best;
            }

            return "文件结论：" + Path.GetFileName(oldPath) + " -> " + result;
        }

        private static string RowConclusion(DiagnosticRow row)
        {
            if (!row.SelectOk)
            {
                return "选择对象问题";
            }

            if (row.TargetReturnCode == 0 && row.SaveOk)
            {
                return "改名并保存成功";
            }

            if (row.TargetReturnCode == 0)
            {
                return "保存阶段问题";
            }

            if (row.TestKind == "ASCII" && row.TargetReturnCode == 0)
            {
                return "名称格式问题";
            }

            return "RenameDocument 返回 " + row.TargetReturnCode;
        }

        private ModelDoc2 ResolveTopModel(IList<RenameTask> tasks)
        {
            RenameTask topTask = tasks.FirstOrDefault(t => t.IsTop);
            ModelDoc2 topModel = topTask == null ? null : topTask.Model;
            if (topModel == null && _swApp != null)
            {
                topModel = _swApp.ActiveDoc as ModelDoc2;
            }

            return topModel;
        }

        private ModelDoc2 ActivateTopDocument()
        {
            if (_activeTopModel == null || _swApp == null)
            {
                return _activeTopModel;
            }

            string title = TryGetString(_activeTopModel, "GetTitle");
            if (!string.IsNullOrWhiteSpace(title))
            {
                TryInvoke(_swApp, "ActivateDoc", title);
            }

            object active = null;
            try
            {
                active = _swApp.ActiveDoc;
            }
            catch
            {
            }

            return active as ModelDoc2 ?? _activeTopModel;
        }

        private bool SelectComponent(object component, ModelDoc2 model, string selectByIdString)
        {
            object extension = TryGetProperty(model, "Extension");
            foreach (string candidate in ComponentSelectCandidates(component, model, selectByIdString))
            {
                bool ok = SelectById2(extension, candidate, "COMPONENT");
                Add("SelectByID2[" + candidate + "]=" + ok);
                if (ok)
                {
                    return true;
                }
            }

            object fallback = TryInvoke(component, "Select2", false, 0);
            Add("Select2 fallback=" + FormatResult(fallback));
            return IsTrue(fallback);
        }

        private bool SelectTopRoot(ModelDoc2 model)
        {
            object configurationManager = TryGetProperty(model, "ConfigurationManager");
            object activeConfiguration = TryGetProperty(configurationManager, "ActiveConfiguration") ?? TryInvoke(configurationManager, "ActiveConfiguration");
            object root = TryInvoke(activeConfiguration, "GetRootComponent3", true) ?? TryInvoke(activeConfiguration, "GetRootComponent");
            object selected = TryInvoke(root, "Select2", false, 0);
            Add("Top root Select2=" + FormatResult(selected));
            return IsTrue(selected);
        }

        private IEnumerable<string> ComponentSelectCandidates(object component, ModelDoc2 model, string selectByIdString)
        {
            var candidates = new List<string>();
            string name2 = ComponentName2(component);
            string topTitle = TryGetString(model, "GetTitle");
            string topBase = Path.GetFileNameWithoutExtension(topTitle ?? string.Empty);

            AddCandidate(candidates, selectByIdString);
            AddCandidate(candidates, name2);
            AddCandidate(candidates, name2 + "@" + topBase);
            AddCandidate(candidates, name2 + "@" + topTitle);
            return candidates;
        }

        private void FillSelectionState(ModelDoc2 model, DiagnosticRow row)
        {
            object selectionManager = TryGetProperty(model, "SelectionManager");
            object count = TryInvoke(selectionManager, "GetSelectedObjectCount2", -1);
            row.SelectionCount = ToInt(count, 0);
            object type = row.SelectionCount > 0 ? TryInvoke(selectionManager, "GetSelectedObjectType3", 1, -1) : null;
            row.SelectionType = FormatResult(type);
            Add("SelectionCount=" + row.SelectionCount + "，SelectionType=" + row.SelectionType);
        }

        private void SaveActiveDocument(ModelDoc2 model, DiagnosticRow row)
        {
            int errors = 0;
            int warnings = 0;
            try
            {
                row.SaveOk = model != null && model.Save3(1, ref errors, ref warnings);
                row.SaveError = errors;
                row.SaveWarning = warnings;
                Add("Save3=" + row.SaveOk + "，Error=" + errors + "，Warning=" + warnings);
            }
            catch (Exception ex)
            {
                row.SaveOk = false;
                row.SaveException = ex.Message;
                Add("Save3 异常：" + ex.Message);
            }
        }

        private RenameDiagnosticResult WriteFiles()
        {
            string folder = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "SolidWorksTeamRenameTool",
                "diagnostics");
            Directory.CreateDirectory(folder);

            string textPath = Path.Combine(folder, "diagnostic.txt");
            string csvPath = Path.Combine(folder, "diagnostic.csv");

            File.WriteAllLines(textPath, _log.ToArray(), Encoding.UTF8);
            using (var writer = new StreamWriter(csvPath, false, Encoding.UTF8))
            {
                writer.WriteLine("FileName,TargetFileName,InstanceName,SelectByIdString,PathBefore,TestKind,TestName,SelectOk,SelectionCount,SelectionType,RenameReturn,VerifyOk,NameAfter,PathAfter,SaveOk,SaveError,SaveWarning,Conclusion,Note");
                foreach (DiagnosticRow row in _rows)
                {
                    writer.WriteLine(string.Join(",",
                        Csv(row.FileName),
                        Csv(row.TargetFileName),
                        Csv(row.InstanceName),
                        Csv(row.SelectByIdString),
                        Csv(row.PathNameBefore),
                        Csv(row.TestKind),
                        Csv(row.TestName),
                        Csv(row.SelectOk.ToString()),
                        Csv(row.SelectionCount.ToString()),
                        Csv(row.SelectionType),
                        Csv(row.TargetReturnCode.ToString()),
                        Csv(row.VerifyOk.ToString()),
                        Csv(row.NameAfter),
                        Csv(row.PathNameAfter),
                        Csv(row.SaveOk.ToString()),
                        Csv(row.SaveError.ToString()),
                        Csv(row.SaveWarning.ToString()),
                        Csv(row.Conclusion),
                        Csv(row.Note)));
                }
            }

            return new RenameDiagnosticResult { TextPath = textPath, CsvPath = csvPath };
        }

        private object InvokeRenameDocument(object extension, string newName)
        {
            if (extension == null)
            {
                return -1;
            }

            try
            {
                return extension.GetType().InvokeMember(
                    "RenameDocument",
                    BindingFlags.InvokeMethod,
                    null,
                    extension,
                    new object[] { newName });
            }
            catch (Exception ex)
            {
                Add("RenameDocument 异常：" + ex.Message);
                return -1;
            }
        }

        private static bool SelectById2(object extension, string candidate, string objectType)
        {
            if (extension == null || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            try
            {
                ModelDocExtension typedExtension = extension as ModelDocExtension;
                if (typedExtension != null)
                {
                    return typedExtension.SelectByID2(candidate, objectType, 0.0, 0.0, 0.0, false, 0, null, 0);
                }
            }
            catch
            {
            }

            return false;
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
            catch
            {
            }
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

        private static string ComponentName2(object component)
        {
            object value = TryGetProperty(component, "Name2") ?? TryInvoke(component, "Name2");
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static string NewBaseName(RenameTask task)
        {
            string source = !string.IsNullOrWhiteSpace(task.NewFileName) ? task.NewFileName : Path.GetFileName(task.NewPath ?? string.Empty);
            if (task.IsVirtual)
            {
                return source ?? string.Empty;
            }
            return Path.GetFileNameWithoutExtension(source ?? string.Empty);
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

        private static bool TargetExistsForDifferentFile(string oldPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(newPath) || !File.Exists(newPath))
            {
                return false;
            }

            return !string.Equals(SafeFullPath(oldPath), SafeFullPath(newPath), StringComparison.OrdinalIgnoreCase);
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
            return result == null ? "null" : Convert.ToString(result);
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

        private static void AddCandidate(ICollection<string> candidates, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !candidates.Contains(value))
            {
                candidates.Add(value);
            }
        }

        private static string Csv(string value)
        {
            string s = value ?? string.Empty;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private void Add(string message)
        {
            _log.Add(DateTime.Now.ToString("HH:mm:ss") + " " + message);
        }

        private sealed class DiagnosticRow
        {
            public string FileName;
            public string TargetFileName;
            public string InstanceName;
            public string SelectByIdString;
            public string PathNameBefore;
            public string TestKind;
            public string TestName;
            public bool SelectOk;
            public int SelectionCount;
            public string SelectionType;
            public int TargetReturnCode = -999;
            public bool VerifyOk;
            public string NameAfter;
            public string PathNameAfter;
            public bool SaveOk;
            public int SaveError;
            public int SaveWarning;
            public string SaveException;
            public string Conclusion;
            public string Note;
        }
    }
}
