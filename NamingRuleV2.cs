using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;

namespace SolidWorksTeamRenameTool
{
    internal enum V2RenameMode
    {
        Rule,
        FindReplace
    }

    internal enum V2RuleTarget
    {
        Assembly,
        Part
    }

    internal enum V2FieldType
    {
        FixedText,
        ParentName,
        LevelLetter,
        LevelNumber,
        SiblingIndex,
        GlobalSeq,
        CustomProperty,
        OriginalName
    }

    internal enum V2Scope
    {
        All,
        Assemblies,
        Parts
    }

    internal sealed class V2RuleField
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Joiner { get; set; }
        public V2FieldType Type { get; set; }
        public string Param1 { get; set; }
        public string Param2 { get; set; }

        public V2RuleField Clone()
        {
            return new V2RuleField
            {
                Id = Id,
                Name = Name,
                Joiner = Joiner,
                Type = Type,
                Param1 = Param1,
                Param2 = Param2
            };
        }
    }

    internal sealed class V2RuleConfig
    {
        public V2RenameMode Mode { get; set; }
        public string FindText { get; set; }
        public string ReplaceText { get; set; }
        public string ProjectCode { get; set; }
        public FilterSettings Filters { get; private set; }
        public List<V2RuleField> AssemblyFields { get; private set; }
        public List<V2RuleField> PartFields { get; private set; }

        public V2RuleConfig()
        {
            Mode = V2RenameMode.Rule;
            FindText = "旧项目";
            ReplaceText = "新项目";
            ProjectCode = "Y01";
            Filters = new FilterSettings
            {
                SkipEnglishStart = false,
                SkipChineseStart = false,
                SkipNumberStart = false,
                SkipSymbolStart = false,
                SkipDuplicateReferences = true,
                SkipReadonlyFiles = true,
                SkipPathKeywords = true
            };
            AssemblyFields = new List<V2RuleField>();
            PartFields = new List<V2RuleField>();
        }

        public static V2RuleConfig CreateDefault()
        {
            V2RuleConfig config = new V2RuleConfig();
            config.ApplyTemplate("prefix");
            return config;
        }

        public V2RuleConfig Clone()
        {
            V2RuleConfig copy = new V2RuleConfig
            {
                Mode = Mode,
                FindText = FindText,
                ReplaceText = ReplaceText,
                ProjectCode = ProjectCode,
                Filters = Filters.Clone()
            };
            copy.AssemblyFields.Clear();
            copy.PartFields.Clear();
            copy.AssemblyFields.AddRange(AssemblyFields.Select(f => f.Clone()));
            copy.PartFields.AddRange(PartFields.Select(f => f.Clone()));
            return copy;
        }

        public void ApplyTemplate(string name)
        {
            Mode = V2RenameMode.Rule;
            AssemblyFields.Clear();
            PartFields.Clear();

            if (string.Equals(name, "hierarchy", StringComparison.OrdinalIgnoreCase))
            {
                AssemblyFields.Add(Field(1, "父组件名称", string.Empty, V2FieldType.ParentName, string.Empty, string.Empty));
                AssemblyFields.Add(Field(2, "装配层级", "-", V2FieldType.LevelLetter, "A", "仅装配体"));
                PartFields.Add(Field(10, "父组件", string.Empty, V2FieldType.ParentName, string.Empty, string.Empty));
                PartFields.Add(Field(11, "零件序号", "-", V2FieldType.SiblingIndex, "1", "3"));
                return;
            }

            if (string.Equals(name, "drawing", StringComparison.OrdinalIgnoreCase))
            {
                AssemblyFields.Add(Field(1, "父组件名称", string.Empty, V2FieldType.ParentName, string.Empty, string.Empty));
                AssemblyFields.Add(Field(2, "层级数字", "-", V2FieldType.LevelNumber, "1", "1"));
                PartFields.Add(Field(10, "父组件", string.Empty, V2FieldType.ParentName, string.Empty, string.Empty));
                PartFields.Add(Field(11, "零件序号", "-", V2FieldType.SiblingIndex, "1", "3"));
                return;
            }

            AssemblyFields.Add(Field(1, "父组件名称", string.Empty, V2FieldType.ParentName, string.Empty, string.Empty));
            AssemblyFields.Add(Field(2, "装配层级", "-", V2FieldType.LevelLetter, "A", "仅装配体"));
            AssemblyFields.Add(Field(3, "原文件名", " ", V2FieldType.OriginalName, string.Empty, string.Empty));
            PartFields.Add(Field(10, "父组件", string.Empty, V2FieldType.ParentName, string.Empty, string.Empty));
            PartFields.Add(Field(11, "零件序号", "-", V2FieldType.SiblingIndex, "1", "3"));
            PartFields.Add(Field(12, "原文件名", " ", V2FieldType.OriginalName, string.Empty, string.Empty));
        }

        public static V2RuleField Field(int id, string name, string joiner, V2FieldType type, string param1, string param2)
        {
            return new V2RuleField
            {
                Id = id,
                Name = FieldTypeLabel(type),
                Joiner = joiner,
                Type = type,
                Param1 = param1,
                Param2 = param2
            };
        }

        public static V2RuleField DefaultField(int id, V2FieldType type, string joiner)
        {
            return Field(id, FieldTypeLabel(type), joiner, type, DefaultParam1(type), DefaultParam2(type));
        }

        public static void ResetFieldForType(V2RuleField field, V2FieldType type)
        {
            if (field == null)
            {
                return;
            }

            field.Type = type;
            field.Name = FieldTypeLabel(type);
            field.Param1 = DefaultParam1(type);
            field.Param2 = DefaultParam2(type);
        }

        public static void Normalize(V2RuleConfig config)
        {
            if (config == null)
            {
                return;
            }

            NormalizeFields(config.AssemblyFields);
            NormalizeFields(config.PartFields);
        }

        private static void NormalizeFields(IEnumerable<V2RuleField> fields)
        {
            if (fields == null)
            {
                return;
            }

            foreach (V2RuleField field in fields)
            {
                if (field == null)
                {
                    continue;
                }

                field.Name = FieldTypeLabel(field.Type);
                if (field.Param1 == null)
                {
                    field.Param1 = DefaultParam1(field.Type);
                }
                if (field.Param2 == null)
                {
                    field.Param2 = DefaultParam2(field.Type);
                }
                if (field.Type == V2FieldType.LevelLetter)
                {
                    field.Param2 = NormalizeScopeText(field.Param2);
                    if (string.IsNullOrWhiteSpace(field.Param1))
                    {
                        field.Param1 = DefaultParam1(field.Type);
                    }
                }
                if (field.Type == V2FieldType.OriginalName)
                {
                    field.Param1 = string.Empty;
                    field.Param2 = string.Empty;
                }
            }
        }

        public static string FieldTypeLabel(V2FieldType type)
        {
            if (type == V2FieldType.FixedText) return "固定文本";
            if (type == V2FieldType.ParentName) return "父组件名称";
            if (type == V2FieldType.LevelLetter) return "层级字母";
            if (type == V2FieldType.LevelNumber) return "层级数字";
            if (type == V2FieldType.SiblingIndex) return "同级序号";
            if (type == V2FieldType.GlobalSeq) return "全局流水";
            if (type == V2FieldType.CustomProperty) return "自定义属性";
            if (type == V2FieldType.OriginalName) return "原文件名";
            return type.ToString();
        }

        public static string DefaultParam1(V2FieldType type)
        {
            if (type == V2FieldType.FixedText) return "项目号";
            if (type == V2FieldType.LevelLetter) return "A";
            if (type == V2FieldType.LevelNumber) return "1";
            if (type == V2FieldType.SiblingIndex) return "1";
            if (type == V2FieldType.GlobalSeq) return "1";
            if (type == V2FieldType.CustomProperty) return "图号";
            return string.Empty;
        }

        public static string DefaultParam2(V2FieldType type)
        {
            if (type == V2FieldType.LevelLetter) return "仅装配体";
            if (type == V2FieldType.LevelNumber) return "1";
            if (type == V2FieldType.SiblingIndex) return "3";
            if (type == V2FieldType.GlobalSeq) return "3";
            return string.Empty;
        }

        public static string NormalizeScopeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "仅装配体";
            if (string.Equals(text, "assemblies", StringComparison.OrdinalIgnoreCase)) return "仅装配体";
            if (string.Equals(text, "仅装配体", StringComparison.OrdinalIgnoreCase)) return "仅装配体";
            if (string.Equals(text, "parts", StringComparison.OrdinalIgnoreCase)) return "仅装配体";
            if (string.Equals(text, "仅零件", StringComparison.OrdinalIgnoreCase)) return "仅装配体";
            if (string.Equals(text, "全部", StringComparison.OrdinalIgnoreCase)) return "全部对象";
            return "全部对象";
        }
    }

    internal sealed class V2RenamePlanner
    {
        private sealed class Context
        {
            public string ParentNewName { get; set; }
            public int Level { get; set; }
            public int GlobalSeq { get; set; }
            public List<int> AllPath { get; private set; }
            public List<int> AssemblyPath { get; private set; }
            public int SiblingAll { get; set; }
            public int SiblingAssemblies { get; set; }
            public int SiblingParts { get; set; }

            public Context()
            {
                AllPath = new List<int>();
                AssemblyPath = new List<int>();
            }

            public Context CreateChild(string parentNewName, int level, int allIndex, int assemblyIndex, int partIndex, bool isAssembly)
            {
                Context child = new Context
                {
                    ParentNewName = parentNewName,
                    Level = level,
                    SiblingAll = allIndex,
                    SiblingAssemblies = assemblyIndex,
                    SiblingParts = partIndex
                };
                child.AllPath.AddRange(AllPath);
                child.AllPath.Add(allIndex);
                child.AssemblyPath.AddRange(AssemblyPath);
                if (isAssembly)
                {
                    child.AssemblyPath.Add(assemblyIndex);
                }
                return child;
            }
        }

        private sealed class NameResult
        {
            public string Name { get; set; }
            public string Segment { get; set; }
        }

        private readonly V2RuleConfig _config;
        private readonly List<RenameTask> _tasks = new List<RenameTask>();
        private readonly Dictionary<string, string> _seenCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RenameTask> _seenTasks = new Dictionary<string, RenameTask>(StringComparer.OrdinalIgnoreCase);
        private int _globalSeq;
        private bool _needsModelDoc;
        private int _visitCount;
        private const int SwDocAssembly = 2;
        private const int MaxVisitCount = 20000;
        private Action<int> _progress;

        public V2RenamePlanner(V2RuleConfig config)
        {
            _config = config == null ? V2RuleConfig.CreateDefault() : config.Clone();
        }

        public IReadOnlyList<RenameTask> Build(object topModel, Action<int> progress = null)
        {
            _tasks.Clear();
            _seenCodes.Clear();
            _seenTasks.Clear();
            _globalSeq = 1;
            _visitCount = 0;
            _progress = progress;

            _needsModelDoc = _config.AssemblyFields.Any(f => f.Type == V2FieldType.CustomProperty)
                          || _config.PartFields.Any(f => f.Type == V2FieldType.CustomProperty);

            if (topModel == null)
            {
                AddError("没有活动文档。");
                return _tasks;
            }

            if (GetDocumentType(topModel) != SwDocAssembly)
            {
                AddError("当前文档不是装配体。");
                return _tasks;
            }

            string topPath = GetModelPath(topModel);
            if (string.IsNullOrWhiteSpace(topPath))
            {
                AddError("顶层装配体尚未保存。");
                return _tasks;
            }

            string validationError = ValidateConfig();
            if (validationError != null)
            {
                AddError(validationError);
                return _tasks;
            }

            Context topContext = new Context { Level = 0, ParentNewName = _config.ProjectCode ?? string.Empty, GlobalSeq = _globalSeq++ };
            string topBase = Path.GetFileNameWithoutExtension(topPath);
            NameResult topName = BuildName(RenameKind.TopAssembly, topModel, topContext, topBase);
            AddRename(topModel, null, RenameKind.TopAssembly, topName.Name, topName.Segment, string.Empty, topPath, topName.Name, true, 0);
            _seenCodes[NormalizePath(topPath)] = topName.Name;
            RememberLastTask(topPath);

            object featMgr = TryGetProperty(topModel, "FeatureManager");
            object rootItem = featMgr == null ? null : TryInvoke(featMgr, "GetFeatureTreeRootItem2", 0);
            if (rootItem == null)
            {
                AddError("无法读取设计树。");
                return _tasks;
            }

            TraverseTreeItems(rootItem, topName.Name, topContext, 1);
            MarkNoOpRenames();
            MarkConflicts();
            return _tasks;
        }

        public IReadOnlyList<RenameTask> BuildFromNodes(List<ComponentNode> nodes, object topModel, Action<int> progress = null)
        {
            _tasks.Clear();
            _seenCodes.Clear();
            _seenTasks.Clear();
            _globalSeq = 1;
            _visitCount = 0;
            _progress = progress;

            _needsModelDoc = _config.AssemblyFields.Any(f => f.Type == V2FieldType.CustomProperty)
                          || _config.PartFields.Any(f => f.Type == V2FieldType.CustomProperty);

            if (topModel == null)
            {
                AddError("没有活动文档。");
                return _tasks;
            }

            string topPath = GetModelPath(topModel);
            if (string.IsNullOrWhiteSpace(topPath))
            {
                AddError("顶层装配体尚未保存。");
                return _tasks;
            }

            string validationError = ValidateConfig();
            if (validationError != null)
            {
                AddError(validationError);
                return _tasks;
            }

            Context topContext = new Context { Level = 0, ParentNewName = _config.ProjectCode ?? string.Empty, GlobalSeq = _globalSeq++ };
            string topBase = Path.GetFileNameWithoutExtension(topPath);
            NameResult topName = BuildName(RenameKind.TopAssembly, topModel, topContext, topBase);
            AddRename(topModel, null, RenameKind.TopAssembly, topName.Name, topName.Segment, string.Empty, topPath, topName.Name, true, 0);
            _seenCodes[NormalizePath(topPath)] = topName.Name;
            RememberLastTask(topPath);

            TraverseNodes(nodes ?? new List<ComponentNode>(), topName.Name, topContext, 1);
            MarkNoOpRenames();
            MarkConflicts();
            return _tasks;
        }

        private void TraverseTreeItems(object treeItem, string parentNewName, Context parentContext, int level)
        {
            int allIndex = 0;
            int assemblyIndex = 0;
            int partIndex = 0;

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

                object next = TryInvoke(child, "GetNext");

                if (GetTreeItemType(child) != 2)
                {
                    child = next;
                    continue;
                }

                object comp = TryGetProperty(child, "Object");
                if (comp == null)
                {
                    AddSkip(RenameKind.Skip, string.Empty, string.Empty, string.Empty, string.Empty, parentNewName, level, "无效组件。");
                    child = next;
                    continue;
                }

                if (IsSuppressed(comp))
                {
                    AddSkip(RenameKind.Skip, string.Empty, string.Empty, string.Empty, GetComponentName(comp), parentNewName, level, "压缩组件。");
                    child = next;
                    continue;
                }

                string oldPath = GetComponentPath(comp);
                string componentName = GetComponentName(comp);
                bool isVirtual = string.IsNullOrWhiteSpace(oldPath);

                RenameKind kind;
                string oldBase;
                string dedupeKey;

                if (isVirtual)
                {
                    kind = GetVirtualKind(comp);
                    oldBase = GetVirtualShortName(componentName);
                    dedupeKey = NormalizePath("virtual:" + componentName);
                    if (kind == RenameKind.Skip)
                    {
                        AddSkip(RenameKind.Skip, string.Empty, string.Empty, string.Empty, componentName, parentNewName, level, "未保存组件。");
                        child = next;
                        continue;
                    }
                }
                else
                {
                    kind = KindFromPath(oldPath);
                    if (kind == RenameKind.Skip)
                    {
                        AddSkip(kind, oldPath, oldPath, string.Empty, componentName, parentNewName, level, "不是 SLDASM/SLDPRT 文件。");
                        child = next;
                        continue;
                    }
                    oldBase = Path.GetFileNameWithoutExtension(oldPath);
                    dedupeKey = NormalizePath(oldPath);
                }

                if (_seenCodes.ContainsKey(dedupeKey))
                {
                    RenameTask firstTask;
                    if (_seenTasks.TryGetValue(dedupeKey, out firstTask))
                    {
                        firstTask.DuplicateCount = Math.Max(1, firstTask.DuplicateCount) + 1;
                        firstTask.Reason = AppendReason(firstTask.Reason, "相同文件引用已合并。");
                    }
                    child = next;
                    continue;
                }

                string skipReason = GetSkipReason(oldBase, isVirtual ? string.Empty : oldPath);

                object model = null;
                if (skipReason == null && _needsModelDoc)
                {
                    model = TryInvoke(comp, "GetModelDoc2");
                    if (model == null)
                    {
                        AddSkip(kind, oldPath, oldPath, string.Empty, componentName, parentNewName, level, "无法获取 ModelDoc2。");
                        child = next;
                        continue;
                    }
                }

                if (skipReason != null)
                {
                    if (kind == RenameKind.Assembly)
                    {
                        // 父装配被筛选跳过，整棵子树级联跳过，不再为子组件生成新名。
                        // 仍递增同级/全局计数器，保持后续同级组件的序号一致。
                        allIndex++;
                        assemblyIndex++;
                        _globalSeq++;
                        _seenCodes[dedupeKey] = oldBase;
                        AddSkip(kind, oldPath, oldPath, string.Empty, componentName, parentNewName, level, skipReason, model, comp);
                        RememberLastTask(dedupeKey);
                        MarkSubtreeSkipped(comp, level + 1, skipReason);
                    }
                    else
                    {
                        AddSkip(kind, oldPath, oldPath, string.Empty, componentName, parentNewName, level, skipReason, model, comp);
                    }
                    child = next;
                    continue;
                }

                int currentAll = allIndex++;
                int currentAssembly = assemblyIndex;
                int currentPart = partIndex;
                if (kind == RenameKind.Assembly)
                {
                    currentAssembly = assemblyIndex++;
                }
                else if (kind == RenameKind.Part)
                {
                    currentPart = partIndex++;
                }

                Context context = parentContext.CreateChild(parentNewName, level,
                    currentAll, currentAssembly, currentPart, kind == RenameKind.Assembly);
                context.GlobalSeq = ++_globalSeq;
                NameResult name = BuildName(kind, model, context, oldBase);

                _seenCodes[dedupeKey] = name.Name;

                AddRename(model, comp, kind, name.Name, isVirtual ? oldBase : name.Segment, parentNewName, oldPath, name.Name, false, level, isVirtual);

                RememberLastTask(dedupeKey);

                if (kind == RenameKind.Assembly)
                {
                    TraverseTreeItems(child, name.Name, context, level + 1);
                }

                child = next;
            }
        }

        private void TraverseNodes(List<ComponentNode> nodes, string parentNewName, Context parentContext, int level)
        {
            int allIndex = 0;
            int assemblyIndex = 0;
            int partIndex = 0;

            foreach (ComponentNode node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

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

                object comp = node.Component;
                string oldPath = node.OldPath ?? string.Empty;
                string componentName = node.ComponentName ?? string.Empty;
                bool isVirtual = node.IsVirtual;
                RenameKind kind = node.Kind;
                string oldBase = node.OldBaseName ?? string.Empty;
                string dedupeKey = isVirtual ? NormalizePath("virtual:" + componentName) : NormalizePath(oldPath);

                if (_seenCodes.ContainsKey(dedupeKey))
                {
                    RenameTask firstTask;
                    if (_seenTasks.TryGetValue(dedupeKey, out firstTask))
                    {
                        firstTask.DuplicateCount = Math.Max(1, firstTask.DuplicateCount) + 1;
                        firstTask.Reason = AppendReason(firstTask.Reason, "相同文件引用已合并。");
                    }
                    continue;
                }

                string skipReason = GetSkipReason(oldBase, isVirtual ? string.Empty : oldPath);

                object model = null;
                if (skipReason == null && _needsModelDoc)
                {
                    model = comp == null ? null : TryInvoke(comp, "GetModelDoc2");
                    if (model == null)
                    {
                        AddSkip(kind, oldPath, oldPath, string.Empty, componentName, parentNewName, level, "无法获取 ModelDoc2。");
                        continue;
                    }
                }

                if (skipReason != null)
                {
                    if (kind == RenameKind.Assembly)
                    {
                        allIndex++;
                        assemblyIndex++;
                        _globalSeq++;
                        _seenCodes[dedupeKey] = oldBase;
                        AddSkip(kind, oldPath, oldPath, string.Empty, componentName, parentNewName, level, skipReason, model, comp);
                        RememberLastTask(dedupeKey);
                        MarkSubtreeSkippedFromNodes(node, level + 1, skipReason);
                    }
                    else
                    {
                        AddSkip(kind, oldPath, oldPath, string.Empty, componentName, parentNewName, level, skipReason, model, comp);
                    }
                    continue;
                }

                int currentAll = allIndex++;
                int currentAssembly = assemblyIndex;
                int currentPart = partIndex;
                if (kind == RenameKind.Assembly)
                {
                    currentAssembly = assemblyIndex++;
                }
                else if (kind == RenameKind.Part)
                {
                    currentPart = partIndex++;
                }

                Context context = parentContext.CreateChild(parentNewName, level,
                    currentAll, currentAssembly, currentPart, kind == RenameKind.Assembly);
                context.GlobalSeq = ++_globalSeq;
                NameResult name = BuildName(kind, model, context, oldBase);

                _seenCodes[dedupeKey] = name.Name;

                AddRename(model, comp, kind, name.Name, isVirtual ? oldBase : name.Segment, parentNewName, oldPath, name.Name, false, level, isVirtual);

                RememberLastTask(dedupeKey);

                if (kind == RenameKind.Assembly)
                {
                    TraverseNodes(node.Children, name.Name, context, level + 1);
                }
            }
        }

        private NameResult BuildName(RenameKind kind, object model, Context context, string oldBase)
        {
            if (_config.Mode == V2RenameMode.FindReplace)
            {
                return new NameResult
                {
                    Name = string.IsNullOrEmpty(_config.FindText) ? oldBase : oldBase.Replace(_config.FindText, _config.ReplaceText ?? string.Empty),
                    Segment = string.Empty
                };
            }

            List<V2RuleField> fields = kind == RenameKind.Part ? _config.PartFields : _config.AssemblyFields;
            string name = string.Empty;
            string segment = string.Empty;
            foreach (V2RuleField field in fields)
            {
                string value = ComputeField(field, kind, model, context, oldBase);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                name += (field.Joiner ?? string.Empty) + value;
                segment = value;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = oldBase;
            }

            return new NameResult { Name = name, Segment = segment };
        }

        private string ComputeField(V2RuleField field, RenameKind kind, object model, Context context, string oldBase)
        {
            if (field.Type == V2FieldType.FixedText)
            {
                return field.Param1 ?? string.Empty;
            }

            if (field.Type == V2FieldType.ParentName)
            {
                return context.ParentNewName ?? string.Empty;
            }

            if (field.Type == V2FieldType.OriginalName)
            {
                return oldBase;
            }

            if (field.Type == V2FieldType.LevelLetter)
            {
                List<int> path = ScopeFromText(field.Param2) == V2Scope.Assemblies ? context.AssemblyPath : context.AllPath;
                return string.Join("-", path.Select(i => IndexToLetters(i + LettersToIndex(field.Param1))));
            }

            if (field.Type == V2FieldType.LevelNumber)
            {
                int start = ParseInt(field.Param1, 0);
                int width = ClampWidth(ParseInt(field.Param2, 2));
                return string.Join("-", context.AssemblyPath.Select(i => (start + i).ToString(new string('0', width))));
            }

            if (field.Type == V2FieldType.SiblingIndex)
            {
                int start = ParseInt(field.Param1, 1);
                int width = ClampWidth(ParseInt(field.Param2, 3));
                int index = kind == RenameKind.Part ? context.SiblingParts : kind == RenameKind.Assembly ? context.SiblingAssemblies : context.SiblingAll;
                return (start + index).ToString(new string('0', width));
            }

            if (field.Type == V2FieldType.GlobalSeq)
            {
                int start = ParseInt(field.Param1, 1);
                int width = ClampWidth(ParseInt(field.Param2, 3));
                return (start + context.GlobalSeq - 1).ToString(new string('0', width));
            }

            if (field.Type == V2FieldType.CustomProperty)
            {
                string value = ReadCustomProperty(model, field.Param1);
                return string.IsNullOrWhiteSpace(value) ? (field.Param2 ?? string.Empty) : value;
            }

            return string.Empty;
        }

        private string GetSkipReason(string fileBase, string oldPath)
        {
            string pathKeyword = GetPathKeywordReason(oldPath);
            if (pathKeyword != null)
            {
                return pathKeyword;
            }

            if (_config.Filters.SkipReadonlyFiles && IsReadonly(oldPath))
            {
                return "只读文件。";
            }

            string s = (fileBase ?? string.Empty).TrimStart();
            if (s.Length == 0)
            {
                return null;
            }

            char c = s[0];
            if (_config.Filters.SkipEnglishStart && IsEnglish(c))
            {
                return "首字符为英文。";
            }

            if (_config.Filters.SkipChineseStart && IsChinese(c))
            {
                return "首字符为中文。";
            }

            if (_config.Filters.SkipNumberStart && char.IsDigit(c))
            {
                return "首字符为数字。";
            }

            if (_config.Filters.SkipSymbolStart && IsSymbolOrOther(c))
            {
                return "首字符为符号。";
            }

            return null;
        }

        private string GetPathKeywordReason(string oldPath)
        {
            if (!_config.Filters.SkipPathKeywords || _config.Filters.PathKeywords == null || string.IsNullOrWhiteSpace(oldPath))
            {
                return null;
            }

            foreach (string keyword in _config.Filters.PathKeywords)
            {
                string k = (keyword ?? string.Empty).Trim();
                if (k.Length > 0 && oldPath.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "路径包含跳过关键词：" + k;
                }
            }

            return null;
        }

        private string ValidateConfig()
        {
            if (_config.Mode == V2RenameMode.FindReplace && string.IsNullOrEmpty(_config.FindText))
            {
                return "查找内容不能为空。";
            }

            foreach (V2RuleField field in _config.AssemblyFields.Concat(_config.PartFields))
            {
                if (!IsValidFileNamePart(field.Joiner) || !IsValidFileNamePart(field.Param1) || !IsValidFileNamePart(field.Param2))
                {
                    return "字段包含 Windows 文件名非法字符：" + field.Name;
                }
            }

            return null;
        }

        private void AddRename(object model, object component, RenameKind kind, string code, string currentSegment, string parentPath, string oldPath, string newBase, bool isTop, int level, bool isVirtual = false)
        {
            string ext = isVirtual ? string.Empty : Path.GetExtension(oldPath);
            string folder = isVirtual ? string.Empty : (Path.GetDirectoryName(oldPath) ?? string.Empty);
            string newFileName = isVirtual ? newBase : newBase + ext;
            string newPath = isVirtual ? string.Empty : Path.Combine(folder, newFileName);

            _tasks.Add(new RenameTask
            {
                Kind = kind,
                Status = RenameStatus.Pending,
                Model = model as ModelDoc2,
                Component = component as Component2,
                Code = code,
                FullCode = code,
                CurrentSegment = currentSegment,
                ParentPath = parentPath,
                OldPath = oldPath,
                OldBaseName = isVirtual ? currentSegment : Path.GetFileNameWithoutExtension(oldPath),
                NewPath = newPath,
                NewFileName = newFileName,
                IsTop = isTop,
                Level = level,
                DuplicateCount = 1,
                IsVirtual = isVirtual,
                Reason = string.Empty
            });
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

        private void AddSkip(RenameKind kind, string oldPath, string newPath, string code, string currentSegment, string parentPath, int level, string reason, object model = null, object component = null, bool skippedByParent = false)
        {
            _tasks.Add(new RenameTask
            {
                Kind = kind,
                Status = RenameStatus.Skipped,
                Model = model as ModelDoc2,
                Component = component as Component2,
                Code = code,
                FullCode = code,
                CurrentSegment = currentSegment,
                ParentPath = parentPath,
                OldPath = oldPath,
                NewPath = newPath,
                OldBaseName = string.IsNullOrWhiteSpace(oldPath) ? string.Empty : Path.GetFileNameWithoutExtension(oldPath),
                Level = level,
                DuplicateCount = 1,
                SkippedByParent = skippedByParent,
                Reason = reason
            });
        }

        private void MarkSubtreeSkipped(object parent, int level, string reason)
        {
            object[] children = TryInvoke(parent, "GetChildren") as object[];
            if (children == null || children.Length == 0)
            {
                return;
            }

            foreach (object child in children)
            {
                if (child == null)
                {
                    continue;
                }

                string oldPath = GetComponentPath(child);
                string componentName = GetComponentName(child);

                if (string.IsNullOrWhiteSpace(oldPath))
                {
                    AddSkip(RenameKind.Skip, string.Empty, string.Empty, string.Empty, componentName, string.Empty, level, "虚拟或未保存组件（父级被跳过）。", null, child, true);
                    continue;
                }

                RenameKind childKind = KindFromPath(oldPath);
                string normPath = NormalizePath(oldPath);
                if (_seenCodes.ContainsKey(normPath))
                {
                    continue;
                }

                _seenCodes[normPath] = string.Empty;
                if (childKind == RenameKind.Skip)
                {
                    AddSkip(childKind, oldPath, oldPath, string.Empty, componentName, string.Empty, level, "非 SLDASM/SLDPRT 文件（父级被跳过）。", null, child, true);
                    continue;
                }

                AddSkip(childKind, oldPath, oldPath, string.Empty, componentName, string.Empty, level, "父级被跳过：" + reason, null, child, true);

                if (childKind == RenameKind.Assembly)
                {
                    MarkSubtreeSkipped(child, level + 1, reason);
                }
            }
        }

        private void MarkSubtreeSkippedFromNodes(ComponentNode node, int level, string reason)
        {
            foreach (ComponentNode child in node.Children)
            {
                if (child == null)
                {
                    continue;
                }

                string oldPath = child.OldPath ?? string.Empty;
                string componentName = child.ComponentName ?? string.Empty;

                if (child.IsVirtual)
                {
                    AddSkip(child.Kind, string.Empty, string.Empty, string.Empty, componentName, string.Empty, level, "虚拟或未保存组件（父级被跳过）。", null, child.Component, true);
                    continue;
                }

                string normPath = NormalizePath(oldPath);
                if (_seenCodes.ContainsKey(normPath))
                {
                    continue;
                }

                _seenCodes[normPath] = string.Empty;
                if (child.Kind == RenameKind.Skip)
                {
                    AddSkip(child.Kind, oldPath, oldPath, string.Empty, componentName, string.Empty, level, "非 SLDASM/SLDPRT 文件（父级被跳过）。", null, child.Component, true);
                    continue;
                }

                AddSkip(child.Kind, oldPath, oldPath, string.Empty, componentName, string.Empty, level, "父级被跳过：" + reason, null, child.Component, true);

                if (child.Kind == RenameKind.Assembly)
                {
                    MarkSubtreeSkippedFromNodes(child, level + 1, reason);
                }
            }
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

        private void RememberLastTask(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && _tasks.Count > 0)
            {
                _seenTasks[NormalizePath(path)] = _tasks[_tasks.Count - 1];
            }
        }

        private void MarkNoOpRenames()
        {
            foreach (RenameTask task in _tasks.Where(t => t.Status == RenameStatus.Pending).ToList())
            {
                bool noChange = task.IsVirtual
                    ? string.Equals(task.OldBaseName, task.NewFileName, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(NormalizePath(task.OldPath), NormalizePath(task.NewPath), StringComparison.OrdinalIgnoreCase);

                if (noChange)
                {
                    task.Status = RenameStatus.Skipped;
                    task.Reason = "名称未变化。";
                }
            }
        }

        private void MarkConflicts()
        {
            Dictionary<string, RenameTask> targets = new Dictionary<string, RenameTask>(StringComparer.OrdinalIgnoreCase);

            foreach (RenameTask task in _tasks.Where(t => t.Status == RenameStatus.Pending).ToList())
            {
                string newNorm = task.IsVirtual
                    ? NormalizePath("virtual:" + task.NewFileName)
                    : NormalizePath(task.NewPath);

                if (targets.ContainsKey(newNorm))
                {
                    task.Status = RenameStatus.Conflict;
                    task.PreviewDuplicateTargetConflict = true;
                    task.Reason = "本次预览生成了重复目标。";
                    continue;
                }

                targets[newNorm] = task;

                if (!task.IsVirtual && File.Exists(task.NewPath) && !string.Equals(NormalizePath(task.OldPath), newNorm, StringComparison.OrdinalIgnoreCase))
                {
                    task.Status = RenameStatus.Conflict;
                    task.TargetExistsConflict = true;
                    task.Reason = "目标文件已存在。";
                }
            }
        }

        private static string AppendReason(string current, string addition)
        {
            if (string.IsNullOrWhiteSpace(current)) return addition;
            if (current.IndexOf(addition, StringComparison.OrdinalIgnoreCase) >= 0) return current;
            return current + " " + addition;
        }

        private static string ReadCustomProperty(object model, string propertyName)
        {
            if (model == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            try
            {
                object extension = model.GetType().InvokeMember(
                    "Extension", BindingFlags.GetProperty, null, model, null);
                if (extension == null) return string.Empty;

                object manager = extension.GetType().InvokeMember(
                    "CustomPropertyManager", BindingFlags.InvokeMethod, null,
                    extension, new object[] { string.Empty });
                if (manager == null) return string.Empty;

                object[] args = new object[] { propertyName, false, string.Empty, string.Empty, false, null };
                ParameterModifier modifier = new ParameterModifier(6);
                modifier[2] = true;
                modifier[3] = true;
                modifier[4] = true;
                modifier[5] = true;
                manager.GetType().InvokeMember(
                    "Get6", BindingFlags.InvokeMethod, null, manager, args,
                    new ParameterModifier[] { modifier }, null, null);

                string value = Convert.ToString(args[2] ?? string.Empty);
                string resolved = Convert.ToString(args[3] ?? string.Empty);
                return string.IsNullOrWhiteSpace(resolved) ? value : resolved;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsReadonly(string path)
        {
            try
            {
                return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEnglish(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        private static bool IsChinese(char c)
        {
            return (c >= '\u4e00' && c <= '\u9fff') ||
                (c >= '\u3400' && c <= '\u4dbf') ||
                (c >= '\uf900' && c <= '\ufaff');
        }

        private static bool IsSymbolOrOther(char c)
        {
            return !IsEnglish(c) && !IsChinese(c) && !char.IsDigit(c);
        }

        private static bool IsValidFileNamePart(string s)
        {
            if (s == null) return true;
            char[] invalid = Path.GetInvalidFileNameChars();
            return !s.Any(invalid.Contains);
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

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, out value) ? value : fallback;
        }

        private static int ClampWidth(int width)
        {
            if (width < 1) return 1;
            if (width > 12) return 12;
            return width;
        }

        private static string IndexToLetters(int index)
        {
            int n = Math.Max(1, index);
            string result = string.Empty;
            while (n > 0)
            {
                n--;
                result = (char)('A' + (n % 26)) + result;
                n /= 26;
            }
            return result;
        }

        private static int LettersToIndex(string text)
        {
            string letters = string.Empty;
            foreach (char c in (text ?? string.Empty).ToUpperInvariant())
            {
                if (c >= 'A' && c <= 'Z') letters += c;
            }
            if (letters.Length == 0) return 1;
            int result = 0;
            foreach (char c in letters)
            {
                result = result * 26 + (c - 'A' + 1);
            }
            return result;
        }

        private static V2Scope ScopeFromText(string text)
        {
            if (string.Equals(text, "仅装配体", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "assemblies", StringComparison.OrdinalIgnoreCase))
            {
                return V2Scope.Assemblies;
            }
            return V2Scope.All;
        }

        private static int GetDocumentType(object model)
        {
            if (model == null) return 0;
            try { return Convert.ToInt32(((dynamic)model).GetType()); } catch { }
            object value = TryInvoke(model, "GetType");
            try { return value == null ? 0 : Convert.ToInt32(value); } catch { return 0; }
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

        private static string GetModelPath(object model)
        {
            return Convert.ToString(TryInvoke(model, "GetPathName") ?? string.Empty);
        }

        private static bool IsSuppressed(object component)
        {
            object value = TryInvoke(component, "IsSuppressed");
            if (value == null) return false;
            try { return Convert.ToBoolean(value); } catch { return false; }
        }

        private static object TryGetProperty(object target, string name)
        {
            if (target == null) return null;
            try { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null); } catch { return null; }
        }

        private static object TryInvoke(object target, string name, params object[] args)
        {
            if (target == null) return null;
            try { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args); } catch { return null; }
        }
    }
}
