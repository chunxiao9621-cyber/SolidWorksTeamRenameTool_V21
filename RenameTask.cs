using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace SolidWorksTeamRenameTool
{
    internal enum RenameKind
    {
        TopAssembly,
        Assembly,
        Part,
        Skip,
        Error
    }

    internal enum RenameStatus
    {
        Loaded,
        Pending,
        Skipped,
        Conflict,
        Renamed,
        Saved,
        Error
    }

    internal sealed class RenameTask
    {
        public RenameKind Kind { get; set; }
        public RenameStatus Status { get; set; }
        public ModelDoc2 Model { get; set; }
        public Component2 Component { get; set; }
        public string Code { get; set; }
        public string FullCode { get; set; }
        public string CurrentSegment { get; set; }
        public string ParentPath { get; set; }
        public string OldPath { get; set; }
        public string NewPath { get; set; }
        public string NewFileName { get; set; }
        public string OldBaseName { get; set; }
        public string Reason { get; set; }
        public bool IsTop { get; set; }
        public int Level { get; set; }
        public int DuplicateCount { get; set; }
        public bool SkippedByParent { get; set; }
        public bool TargetExistsConflict { get; set; }
        public bool PreviewDuplicateTargetConflict { get; set; }
        public bool UserConfirmedOverwriteTarget { get; set; }
        public bool IsVirtual { get; set; }
        public bool CanExecute
        {
            get { return Status == RenameStatus.Pending; }
        }
    }

    internal sealed class ComponentNode
    {
        public object Component { get; set; }
        public RenameKind Kind { get; set; }
        public string OldPath { get; set; }
        public string OldBaseName { get; set; }
        public string ComponentName { get; set; }
        public bool IsVirtual { get; set; }
        public List<ComponentNode> Children { get; private set; }

        public ComponentNode()
        {
            Children = new List<ComponentNode>();
        }
    }
}
