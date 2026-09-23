using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SolidWorksTeamRenameTool
{
    internal sealed class AssemblyComponentReader
    {
        private const int SwDocAssembly = 2;
        private readonly List<RenameTask> _tasks = new List<RenameTask>();
        private readonly Dictionary<string, RenameTask> _seenFiles = new Dictionary<string, RenameTask>(StringComparer.OrdinalIgnoreCase);
        private int _visitCount;
        private const int MaxVisitCount = 20000;
        private Action<int> _progress;
        private List<ComponentNode> _rootNodes;

        public IReadOnlyList<RenameTask> Build(object topModel, Action<int> progress = null)
        {
            _tasks.Clear();
            _seenFiles.Clear();
            _visitCount = 0;
            _progress = progress;
            _rootNodes = new List<ComponentNode>();

            if (topModel == null)
            {
                AddError("No active document.");
                return _tasks;
            }

            if (GetDocumentType(topModel) != SwDocAssembly)
            {
                AddError("Active document is not an assembly.");
                return _tasks;
            }

            string topPath = GetModelPath(topModel);
            if (string.IsNullOrWhiteSpace(topPath))
            {
                AddError("Top assembly has not been saved.");
                return _tasks;
            }

            AddLoaded(RenameKind.TopAssembly, topPath, 0, string.Empty, Path.GetFileNameWithoutExtension(topPath), "已读取顶层装配。");

            object featMgr = TryGetProperty(topModel, "FeatureManager");
            object rootItem = featMgr == null ? null : TryInvoke(featMgr, "GetFeatureTreeRootItem2", 0);
            if (rootItem == null)
            {
                AddError("Cannot get FeatureManager tree root.");
                return _tasks;
            }

            TraverseTreeItems(rootItem, Path.GetFileNameWithoutExtension(topPath), 1, null);
            return _tasks;
        }

        private void TraverseTreeItems(object treeItem, string parentName, int level, ComponentNode parentNode)
        {
            object child = TryInvoke(treeItem, "GetFirstChild");
            while (child != null)
            {
                if (++_visitCount > MaxVisitCount)
                {
                    AddError("遍历节点过多，可能死循环，已停止。");
                    return;
                }

                if ((_visitCount % 20) == 0)
                {
                    System.Windows.Forms.Application.DoEvents();
                    if (_progress != null)
                    {
                        _progress(_visitCount);
                    }
                }

                if (GetTreeItemType(child) == 2)
                {
                    object comp = TryGetProperty(child, "Object");
                    if (comp != null)
                    {
                        ProcessComponent(comp, child, parentName, level, parentNode);
                    }
                }

                child = TryInvoke(child, "GetNext");
            }
        }

        private void ProcessComponent(object comp, object treeItem, string parentName, int level, ComponentNode parentNode)
        {
            string componentName = GetComponentName(comp);
            if (IsSuppressed(comp))
            {
                AddLoaded(RenameKind.Skip, string.Empty, level, parentName, componentName, "压缩组件，已列出但不会改名。");
                return;
            }

            string oldPath = GetComponentPath(comp);
            if (string.IsNullOrWhiteSpace(oldPath))
            {
                RenameKind virtualKind = GetVirtualKind(comp);
                if (virtualKind == RenameKind.Skip)
                {
                    AddLoaded(RenameKind.Skip, string.Empty, level, parentName, componentName, "未保存组件，已列出但不会改名。");
                    return;
                }

                string virtualName = GetVirtualShortName(componentName);
                AddLoaded(virtualKind, string.Empty, level, parentName, virtualName, "虚拟零部件，已读取。", true);

                ComponentNode node = new ComponentNode
                {
                    Component = comp,
                    Kind = virtualKind,
                    OldPath = string.Empty,
                    OldBaseName = virtualName,
                    ComponentName = componentName,
                    IsVirtual = true
                };
                AddNode(parentNode, node);

                if (virtualKind == RenameKind.Assembly)
                {
                    TraverseTreeItems(treeItem, virtualName, level + 1, node);
                }
                return;
            }

            RenameKind kind = KindFromPath(oldPath);
            if (kind == RenameKind.Skip)
            {
                AddLoaded(RenameKind.Skip, oldPath, level, parentName, componentName, "非 SLDASM/SLDPRT 文件，已列出但不会改名。");
                return;
            }

            if (IncrementDuplicateIfSeen(oldPath))
            {
                return;
            }

            AddLoaded(kind, oldPath, level, parentName, componentName, "已读取。");

            ComponentNode childNode = new ComponentNode
            {
                Component = comp,
                Kind = kind,
                OldPath = oldPath,
                OldBaseName = Path.GetFileNameWithoutExtension(oldPath),
                ComponentName = componentName,
                IsVirtual = false
            };
            AddNode(parentNode, childNode);

            if (kind == RenameKind.Assembly)
            {
                TraverseTreeItems(treeItem, componentName, level + 1, childNode);
            }
        }

        private void AddNode(ComponentNode parentNode, ComponentNode node)
        {
            if (parentNode == null)
            {
                _rootNodes.Add(node);
            }
            else
            {
                parentNode.Children.Add(node);
            }
        }

        public List<ComponentNode> GetNodes()
        {
            return _rootNodes ?? new List<ComponentNode>();
        }

        private void AddLoaded(RenameKind kind, string oldPath, int level, string parentName, string currentSegment, string reason, bool isVirtual = false)
        {
            RenameTask task = new RenameTask
            {
                Kind = kind,
                Status = RenameStatus.Loaded,
                Level = level,
                ParentPath = parentName,
                CurrentSegment = currentSegment,
                OldPath = oldPath,
                OldBaseName = isVirtual ? currentSegment : (string.IsNullOrWhiteSpace(oldPath) ? string.Empty : Path.GetFileNameWithoutExtension(oldPath)),
                IsVirtual = isVirtual,
                Reason = reason,
                DuplicateCount = 1
            };
            _tasks.Add(task);

            if (!isVirtual && !string.IsNullOrWhiteSpace(oldPath) && (kind == RenameKind.TopAssembly || kind == RenameKind.Assembly || kind == RenameKind.Part))
            {
                _seenFiles[NormalizePath(oldPath)] = task;
            }
        }

        private static RenameKind GetVirtualKind(object component)
        {
            object model = TryInvoke(component, "GetModelDoc2");
            if (model != null)
            {
                int docType = GetDocumentType(model);
                if (docType == 2) return RenameKind.Assembly;
                if (docType == 1) return RenameKind.Part;
            }

            return RenameKind.Part;
        }

        private static string GetVirtualShortName(string name2)
        {
            if (string.IsNullOrWhiteSpace(name2))
            {
                return string.Empty;
            }

            string s = name2.Trim();
            int slash = s.LastIndexOf('/');
            if (slash >= 0)
            {
                s = s.Substring(slash + 1);
            }

            int caret = s.IndexOf('^');
            if (caret >= 0)
            {
                s = s.Substring(0, caret);
            }

            s = s.Trim('[', ']');

            int dash = s.LastIndexOf('-');
            if (dash > 0)
            {
                s = s.Substring(0, dash);
            }

            return s.Trim();
        }

        private void AddError(string reason)
        {
            _tasks.Add(new RenameTask
            {
                Kind = RenameKind.Error,
                Status = RenameStatus.Error,
                DuplicateCount = 1,
                Reason = reason
            });
        }

        private bool IncrementDuplicateIfSeen(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            RenameTask first;
            if (!_seenFiles.TryGetValue(NormalizePath(path), out first))
            {
                return false;
            }

            first.DuplicateCount = Math.Max(1, first.DuplicateCount) + 1;
            first.Reason = "已读取。相同文件引用已合并。";
            return true;
        }

        private static int GetDocumentType(object model)
        {
            object value = TryInvoke(model, "GetType");
            if (value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static string GetModelPath(object model)
        {
            return Convert.ToString(TryInvoke(model, "GetPathName") ?? string.Empty);
        }

        private static string GetComponentName(object component)
        {
            return Convert.ToString(TryGetProperty(component, "Name2") ?? string.Empty);
        }

        private static string GetComponentPath(object component)
        {
            return Convert.ToString(TryInvoke(component, "GetPathName") ?? string.Empty);
        }

        private static int GetTreeItemType(object treeItem)
        {
            object value = TryGetProperty(treeItem, "ObjectType");
            if (value == null)
            {
                return -1;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static bool IsSuppressed(object component)
        {
            object value = TryInvoke(component, "IsSuppressed");
            if (value == null)
            {
                return false;
            }

            try
            {
                return Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        private static RenameKind KindFromPath(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".SLDASM", StringComparison.OrdinalIgnoreCase)) return RenameKind.Assembly;
            if (string.Equals(Path.GetExtension(path), ".SLDPRT", StringComparison.OrdinalIgnoreCase)) return RenameKind.Part;
            return RenameKind.Skip;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Trim().ToUpperInvariant();
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
    }
}
